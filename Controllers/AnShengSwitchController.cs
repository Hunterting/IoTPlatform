// T8-4：安圣开关动作与延时任务 API。
//
// 【为什么单独开一个控制器而不是往 AnShengController 里塞】
//   AnShengController 已经承载了「发现 / 认领 / 档案 / 命令流水」四类职责、600+ 行。
//   开关与延时是一组高内聚、面向终端用户的操作型端点，独立成类后：
//     · 权限可以整类收紧到 SEND_DEVICE_COMMANDS，而不用逐个方法标注；
//     · 将来 T10 定时任务可以顺理成章地并进来，不再动 AnShengController。
//
// 【路由不冲突的依据】本控制器全部端点的第二段都是<b>字面量</b>
//   （action / actions / delay-tasks），与 AnShengController 的 {deviceId:long}/command、
//   {deviceId:long}/profile、{deviceId:long}/auto-report 等既有路由处于同一模板层级但字面量互不相同，
//   ASP.NET Core 路由表不会产生 AmbiguousMatchException。
//
// 【拒收信封（设计 §7.2，铁律②）】
//   业务失败一律 ApiResponse<T>.BadRequest(message, data)，HTTP 状态恒为 200。
//   **禁止**裸 BadRequest() / StatusCode(400) —— AnShengCommandRejectionEnvelopeTests 锁定了这个行为。
//   验收 #5 的「400」指的是 ApiResponse.Code=400，不是 HTTP 400。
//
// 【下发唯一入口（设计 §7.3，铁律③）】
//   action / actions 直接调 IAnShengCommandService.SendCommandAsync；
//   startDelayTask / stopDelayTask 调 IAnShengScheduleService（它内部同样只走 SendCommandAsync）。
//   本控制器不碰 MQTT 适配器、不碰 AnShengCommandBuilder、不碰 Guard。
//   喇叭类设备的 RejectedByKind 因此是「结构性保证」而非本文件里的某个 if。

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Controllers;

/// <summary>
/// 安圣二开设备「开关动作 + 延时任务」控制器（T8）。
///
/// 端点一览：
/// <list type="bullet">
///   <item><c>POST /api/v1/ansheng/{deviceId}/action</c> —— 单插槽通断；</item>
///   <item><c>POST /api/v1/ansheng/{deviceId}/actions</c> —— 多插槽批量通断；</item>
///   <item><c>GET  /api/v1/ansheng/{deviceId}/delay-tasks</c> —— 读延时任务镜像；</item>
///   <item><c>POST /api/v1/ansheng/{deviceId}/delay-tasks/start</c> —— 开始/配置延时任务；</item>
///   <item><c>POST /api/v1/ansheng/{deviceId}/delay-tasks/stop</c> —— 停止延时任务。</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/ansheng")]
[PermissionAuthorize(Permissions.VIEW_DEVICES)]
public class AnShengSwitchController : ControllerBase
{
    /// <summary>安圣协议方法名：单插槽开关动作。</summary>
    private const string MethodAction = "action";

    /// <summary>安圣协议方法名：多插槽开关动作。</summary>
    private const string MethodActions = "actions";

    private readonly IAnShengCommandService _commandService;
    private readonly IAnShengScheduleService _scheduleService;
    private readonly IAnShengDeviceProfileService _profileService;
    private readonly ILogger<AnShengSwitchController> _logger;

    /// <summary>
    /// 构造控制器。
    /// </summary>
    /// <param name="commandService">安圣命令下发服务（T7 单点入口）。</param>
    /// <param name="scheduleService">安圣延时任务调度服务（T8）。</param>
    /// <param name="profileService">安圣设备能力档案服务，用于读取 <c>SlotsSnapshot</c>。</param>
    /// <param name="logger">日志。</param>
    public AnShengSwitchController(
        IAnShengCommandService commandService,
        IAnShengScheduleService scheduleService,
        IAnShengDeviceProfileService profileService,
        ILogger<AnShengSwitchController> logger)
    {
        _commandService = commandService;
        _scheduleService = scheduleService;
        _profileService = profileService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // 开关动作
    // ─────────────────────────────────────────────

    /// <summary>
    /// 单插槽开关动作（<c>action</c>）。
    ///
    /// 【返回的 <c>Slots</c> 为什么可能是旧的】设备权威 + 异步刷新（设计 §8-2 裁定：不阻塞）。
    /// 本端点在命令<b>出网即返回</b>，此刻设备应答尚未到达，
    /// <c>Slots</c> 读的是当前 <c>Profile.SlotsSnapshot</c>（可能为 null 或上一次的值）。
    /// 真正的新状态由应答经 <c>AnShengMessageRouter</c> 钩子异步写回。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">动作请求。</param>
    /// <returns>下发结果 + 当前插槽快照。</returns>
    [HttpPost("{deviceId:long}/action")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengSwitchResultDto>>> Action(
        long deviceId,
        [FromBody] AnShengActionRequest request)
    {
        var ct = HttpContext.RequestAborted;

        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengSwitchResultDto>.BadRequest("请求体不能为空");
            }

            var parameters = new Dictionary<string, object?>
            {
                ["slotNum"] = request.SlotNum,
                ["action"] = NormalizeAction(request.Action)
            };

            // 可选字段：为 null 时<b>不下发该键</b>。安圣设备对未知/多余字段并不总是宽容，
            // 且报文字节级一致性（验收 #1）要求我们不凭空多塞字段。
            if (request.HasStopDelayTask.HasValue)
            {
                parameters["hasStopDelayTask"] = request.HasStopDelayTask.Value;
            }

            var response = await _commandService.SendCommandAsync(deviceId, MethodAction, parameters, ct);
            var result = await BuildSwitchResultAsync(deviceId, response, ct);

            return result.Accepted
                ? ApiResponse<AnShengSwitchResultDto>.Success(
                    result, $"插槽 {request.SlotNum} 动作 {parameters["action"]} 已下发")
                : ApiResponse<AnShengSwitchResultDto>.BadRequest(
                    response.ErrorMessage ?? "开关动作下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "下发插槽开关动作失败: DeviceId={DeviceId}, SlotNum={SlotNum}, Action={Action}",
                deviceId, request?.SlotNum, request?.Action);

            return ApiResponse<AnShengSwitchResultDto>.Error($"开关动作下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 多插槽批量开关动作（<c>actions</c>）。
    ///
    /// <c>slotNums</c> 在报文里必须是<b>JSON 数组</b>（如 <c>[1,3]</c>）而不是逗号串，
    /// 这是验收 #2 的断言点；数组形态由 <c>AnShengCommandBuilder</c> 保证，本端点只负责原样透传。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">批量动作请求。</param>
    /// <returns>下发结果 + 当前插槽快照。</returns>
    [HttpPost("{deviceId:long}/actions")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengSwitchResultDto>>> Actions(
        long deviceId,
        [FromBody] AnShengActionsRequest request)
    {
        var ct = HttpContext.RequestAborted;

        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengSwitchResultDto>.BadRequest("请求体不能为空");
            }

            // 这是唯一在控制器层做的校验，且只针对「报文形状退化」这一种情况：
            // slotNums 为空会构造出 {"slotNums":[]} 的空操作帧 —— 白白占一次在途与一条流水。
            // 其余业务校验（品类/动作合法性/插槽越界）一律交给 AnShengCommandGuard 单点判定，
            // 绝不在这里造第二套规则。
            if (request.SlotNums == null || request.SlotNums.Length == 0)
            {
                return ApiResponse<AnShengSwitchResultDto>.BadRequest("slotNums 不能为空");
            }

            var parameters = new Dictionary<string, object?>
            {
                ["slotNums"] = request.SlotNums,
                ["action"] = NormalizeAction(request.Action)
            };

            if (request.HasStopDelayTask.HasValue)
            {
                parameters["hasStopDelayTask"] = request.HasStopDelayTask.Value;
            }

            var response = await _commandService.SendCommandAsync(deviceId, MethodActions, parameters, ct);
            var result = await BuildSwitchResultAsync(deviceId, response, ct);

            return result.Accepted
                ? ApiResponse<AnShengSwitchResultDto>.Success(
                    result, $"{request.SlotNums.Length} 个插槽动作 {parameters["action"]} 已下发")
                : ApiResponse<AnShengSwitchResultDto>.BadRequest(
                    response.ErrorMessage ?? "批量开关动作下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "下发批量插槽开关动作失败: DeviceId={DeviceId}, SlotCount={SlotCount}, Action={Action}",
                deviceId, request?.SlotNums?.Length, request?.Action);

            return ApiResponse<AnShengSwitchResultDto>.Error($"批量开关动作下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 延时任务
    // ─────────────────────────────────────────────

    /// <summary>
    /// 读取延时任务镜像（<c>getDelayTasks</c> 的平台侧视图）。
    ///
    /// 【本端点不查设备】返回的是平台镜像（乐观值或已被回读覆盖的真值），
    /// 每条带 <c>IsStale</c>（>24h 未同步）供前端提示「建议手动同步」。
    /// 设计 §8-4 裁定：不提供 <c>?force=true</c> 实时穿透，前端「手动同步」按钮留 T9。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>按插槽升序的延时任务镜像列表。</returns>
    [HttpGet("{deviceId:long}/delay-tasks")]
    public async Task<ActionResult<ApiResponse<List<AnShengDelayTaskDto>>>> GetDelayTasks(long deviceId)
    {
        var ct = HttpContext.RequestAborted;

        try
        {
            var tasks = await _scheduleService.GetDelayTasksAsync(deviceId, ct);
            return ApiResponse<List<AnShengDelayTaskDto>>.Success(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询延时任务镜像失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<List<AnShengDelayTaskDto>>.Error($"查询延时任务失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 开始 / 配置某插槽的延时任务（<c>startDelayTask</c>）。
    ///
    /// 成功后平台先落一份「乐观镜像」并立即返回；≥100ms 后在新作用域自动触发一次
    /// <c>getDelayTasks</c>，用设备真值覆盖镜像并 bump <c>SyncedAt</c>（验收 #3）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">延时任务参数。</param>
    /// <returns>下发结果 + 当前镜像快照。</returns>
    [HttpPost("{deviceId:long}/delay-tasks/start")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengDelayTaskResultDto>>> StartDelayTask(
        long deviceId,
        [FromBody] AnShengStartDelayTaskRequest request)
    {
        var ct = HttpContext.RequestAborted;

        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengDelayTaskResultDto>.BadRequest("请求体不能为空");
            }

            var result = await _scheduleService.StartDelayTaskAsync(
                deviceId,
                request.SlotNum,
                request.Enable,
                request.SAction,
                request.EAction,
                request.Secs,
                ct);

            return result.Accepted
                ? ApiResponse<AnShengDelayTaskResultDto>.Success(
                    result, $"插槽 {request.SlotNum} 延时任务已下发")
                : ApiResponse<AnShengDelayTaskResultDto>.BadRequest(
                    result.ErrorMessage ?? "延时任务下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "下发开始延时任务失败: DeviceId={DeviceId}, SlotNum={SlotNum}",
                deviceId, request?.SlotNum);

            return ApiResponse<AnShengDelayTaskResultDto>.Error($"延时任务下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 停止某插槽的延时任务（<c>stopDelayTask</c>）。同样带写后回读。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">停止请求（含插槽编号）。</param>
    /// <returns>下发结果 + 当前镜像快照。</returns>
    [HttpPost("{deviceId:long}/delay-tasks/stop")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengDelayTaskResultDto>>> StopDelayTask(
        long deviceId,
        [FromBody] AnShengStopDelayTaskRequest request)
    {
        var ct = HttpContext.RequestAborted;

        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengDelayTaskResultDto>.BadRequest("请求体不能为空");
            }

            var result = await _scheduleService.StopDelayTaskAsync(deviceId, request.SlotNum, ct);

            return result.Accepted
                ? ApiResponse<AnShengDelayTaskResultDto>.Success(
                    result, $"插槽 {request.SlotNum} 延时任务已停止")
                : ApiResponse<AnShengDelayTaskResultDto>.BadRequest(
                    result.ErrorMessage ?? "停止延时任务失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "下发停止延时任务失败: DeviceId={DeviceId}, SlotNum={SlotNum}",
                deviceId, request?.SlotNum);

            return ApiResponse<AnShengDelayTaskResultDto>.Error($"停止延时任务失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 私有工具
    // ─────────────────────────────────────────────

    /// <summary>
    /// 把 <see cref="AnShengCommandResponse"/> 包装成 <see cref="AnShengSwitchResultDto"/>，
    /// 并附上当前 <c>Profile.SlotsSnapshot</c>。
    ///
    /// 【被拒时也读快照】喇叭类被拒同样返回 <c>Slots</c>（大概率为 null）——
    /// 保持响应体形状稳定，前端不必为失败分支写第二套解析。
    /// 档案读取失败不升级为整体失败：<c>Slots</c> 是锦上添花的附加信息，
    /// 让它把一次「命令已成功下发」翻转成 500 是不可接受的。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="response">命令下发响应。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>开关动作结果。</returns>
    private async Task<AnShengSwitchResultDto> BuildSwitchResultAsync(
        long deviceId, AnShengCommandResponse response, CancellationToken ct)
    {
        int[]? slots = null;

        try
        {
            var profile = await _profileService.GetByDeviceIdAsync(deviceId, ct);
            slots = AnShengScheduleService.ParseSlotsSnapshot(profile?.SlotsSnapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "读取插槽快照失败（不影响下发结果）: DeviceId={DeviceId}", deviceId);
        }

        return new AnShengSwitchResultDto
        {
            Accepted = response.Success,
            CommandId = response.CommandId,
            FrameId = response.FrameId,
            RejectReason = response.RejectReason,
            ErrorMessage = response.Success ? null : response.ErrorMessage,
            Payload = response.Payload,
            Slots = slots
        };
    }

    /// <summary>
    /// 动作串归一：空白回落 <c>on</c>，其余仅 <c>Trim</c> + 转小写。
    /// <b>不做白名单纠正</b> —— 非法值必须原样送到 <c>AnShengCommandGuard</c> 被拒，
    /// 悄悄改成合法值等于替调用方隐藏了 bug。
    /// </summary>
    /// <param name="action">原始动作串。</param>
    /// <returns>归一化后的动作串。</returns>
    private static string NormalizeAction(string? action)
        => string.IsNullOrWhiteSpace(action) ? "on" : action.Trim().ToLowerInvariant();
}
