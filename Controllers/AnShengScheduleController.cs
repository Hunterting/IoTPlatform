// T10：安圣定时任务 API（仅后端，不含前端）。
//
// 【路由不冲突】与 AnShengSwitchController 同处 `api/v1/ansheng` 模板层级，本控制器的字面量
//   段为 `time-tasks`（GET/POST）与 `time-tasks/{slotNum}`（GET/POST），与既有 `action` /
//   `delay-tasks` 等互不重叠，不会触发 AmbiguousMatchException。
//
// 【拒绝信封（铁律②）】业务失败一律 ApiResponse<T>.BadRequest(message, data)，HTTP 恒 200。
//   <b>禁止</b>裸 BadRequest() / StatusCode(400)。验收 #1/#2 的「400」指 ApiResponse.Code=400，
//   不是 HTTP 400。仅乐观并发冲突（验收 #5）返回真正的 HTTP 409（带信封体）。
//
// 【下发唯一入口（铁律③）】所有写端点调 IAnShengScheduleService，其内部只走
//   IAnShengCommandService.SendCommandAsync；本控制器不碰 MQTT / Builder / Guard。
//   仅 Switch4G 放行由 Catalog(GroupTimeTask) + Guard 结构性保证（验收 #1）。
//
// 【二次确认（验收 #2）】confirm=true 的判定在服务层，本控制器只负责透传请求体。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Configuration;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.Controllers;

/// <summary>
/// 安圣二开设备「定时任务」控制器（T10）。
///
/// 端点一览：
/// <list type="bullet">
///   <item><c>GET  /api/v1/ansheng/{deviceId}/time-tasks</c> —— 读全部插槽定时任务镜像（普通 / 循环两组）；</item>
///   <item><c>POST /api/v1/ansheng/{deviceId}/time-tasks</c> —— 整表覆盖定时任务（需 confirm）；</item>
///   <item><c>GET  /api/v1/ansheng/{deviceId}/time-tasks/{slotNum}</c> —— 读单插槽定时任务镜像；</item>
///   <item><c>POST /api/v1/ansheng/{deviceId}/time-tasks/{slotNum}</c> —— 设置单插槽定时任务（需 confirm）。</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/ansheng")]
[PermissionAuthorize(Permissions.VIEW_DEVICES)]
public class AnShengScheduleController : ControllerBase
{
    private readonly IAnShengScheduleService _scheduleService;
    private readonly ILogger<AnShengScheduleController> _logger;

    /// <summary>构造控制器。</summary>
    public AnShengScheduleController(
        IAnShengScheduleService scheduleService,
        ILogger<AnShengScheduleController> logger)
    {
        _scheduleService = scheduleService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // 定时任务（整表）
    // ─────────────────────────────────────────────

    /// <summary>
    /// 读取全部插槽的定时任务镜像（平台侧视图，设备权威）。
    /// 每条带 <see cref="AnShengTimeTaskDto.IsStale"/>（&gt;24h 未同步）与 <see cref="AnShengTimeTaskDto.RowVersion"/>（并发令牌）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>按插槽升序的定时任务集合列表。</returns>
    [HttpGet("{deviceId:long}/time-tasks")]
    public async Task<ActionResult<ApiResponse<List<AnShengSlotTimeTaskSetDto>>>> GetTimeTasks(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var sets = await _scheduleService.GetTimeTasksAsync(deviceId, ct);
            return ApiResponse<List<AnShengSlotTimeTaskSetDto>>.Success(sets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询定时任务镜像失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<List<AnShengSlotTimeTaskSetDto>>.Error($"查询定时任务失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 整表覆盖定时任务（<c>setTimeTasks</c>）。需 <c>confirm=true</c>（验收 #2）。
    /// 命令出网后立即返回乐观镜像，随后在新作用域写后回读覆盖真值（验收 #3）。
    /// 非 Switch4G 品类由 Guard 结构性拒绝、带 <see cref="AnShengCommandRejectReason.RejectedByKind"/>（验收 #1）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">整表覆盖请求（含插槽集合与 confirm / rowVersion）。</param>
    /// <returns>下发结果 + 乐观镜像快照。</returns>
    [HttpPost("{deviceId:long}/time-tasks")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengTimeTaskResultDto>>> SetTimeTasks(
        long deviceId,
        [FromBody] AnShengSetTimeTasksRequest request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengTimeTaskResultDto>.BadRequest("请求体不能为空");
            }

            var sets = request.Slots
                .Select(s => new AnShengSlotTimeTaskSet
                {
                    SlotNum = s.SlotNum,
                    TimeTasks = s.TimeTasks.Select(ToTimeTaskItem).ToList(),
                    LoopTimeTasks = s.LoopTimeTasks.Select(ToLoopTimeTaskItem).ToList()
                })
                .ToList();

            var result = await _scheduleService.SetTimeTasksAsync(
                deviceId, sets, request.Confirm, request.RowVersion, ct);

            if (result.ConcurrencyConflict)
            {
                // 验收 #5：乐观并发冲突 → 真正的 HTTP 409（带信封体），区别于业务拒绝的 200。
                // 与单插槽动作 SetSlotTimeTasks 保持同一口径，两条路径不得漂移。
                return StatusCode(409, ApiResponse<AnShengTimeTaskResultDto>.Fail(
                    409, result.ErrorMessage ?? "定时任务已被其他操作修改，请刷新后重试", result));
            }

            return result.Accepted
                ? ApiResponse<AnShengTimeTaskResultDto>.Success(result, "定时任务已下发")
                : ApiResponse<AnShengTimeTaskResultDto>.BadRequest(
                    result.ErrorMessage ?? "定时任务下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发整表定时任务失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengTimeTaskResultDto>.Error($"定时任务下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 定时任务（单插槽）
    // ─────────────────────────────────────────────

    /// <summary>
    /// 读取单个插槽的定时任务镜像（普通 / 循环两组）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">插槽编号，从 1 开始。</param>
    /// <returns>该插槽的定时任务集合；无任务时为 null。</returns>
    [HttpGet("{deviceId:long}/time-tasks/{slotNum:int}")]
    public async Task<ActionResult<ApiResponse<AnShengSlotTimeTaskSetDto?>>> GetSlotTimeTasks(
        long deviceId, int slotNum)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (slotNum < 1)
            {
                return ApiResponse<AnShengSlotTimeTaskSetDto?>.BadRequest("slotNum 必须 ≥ 1");
            }

            var set = await _scheduleService.GetSlotTimeTasksAsync(deviceId, slotNum, ct);
            return ApiResponse<AnShengSlotTimeTaskSetDto?>.Success(set);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询单插槽定时任务失败: DeviceId={DeviceId}, SlotNum={SlotNum}", deviceId, slotNum);
            return ApiResponse<AnShengSlotTimeTaskSetDto?>.Error($"查询定时任务失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 设置单个插槽的定时任务（<c>setSlotTimeTasks</c>）。需 <c>confirm=true</c>（验收 #2）。
    /// <paramref name="slotNum"/> &lt; 1 或越界（&gt; 插槽数）在<b>下发前</b>拦截、返回 400 且不下发（验收 #7）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">插槽编号，从 1 开始（来自路由）。</param>
    /// <param name="request">单插槽定时任务请求（含普通 / 循环数组与 confirm / rowVersion）。</param>
    /// <returns>下发结果 + 乐观镜像快照。</returns>
    [HttpPost("{deviceId:long}/time-tasks/{slotNum:int}")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengTimeTaskResultDto>>> SetSlotTimeTasks(
        long deviceId, int slotNum,
        [FromBody] AnShengSetSlotTimeTasksRequest request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengTimeTaskResultDto>.BadRequest("请求体不能为空");
            }

            if (slotNum < 1)
            {
                // 验收 #7：slotNum 非法，下发前拦截，HTTP 200 + Code=400，命令不出网。
                return ApiResponse<AnShengTimeTaskResultDto>.BadRequest("slotNum 必须 ≥ 1");
            }

            var timeTasks = request.TimeTasks.Select(ToTimeTaskItem).ToList();
            var loopTasks = request.LoopTimeTasks.Select(ToLoopTimeTaskItem).ToList();

            var result = await _scheduleService.SetSlotTimeTasksAsync(
                deviceId, slotNum, timeTasks, loopTasks, request.Confirm, request.RowVersion, ct);

            if (result.ConcurrencyConflict)
            {
                // 验收 #5：乐观并发冲突 → 真正的 HTTP 409（带信封体），区别于业务拒绝的 200。
                return StatusCode(409, ApiResponse<AnShengTimeTaskResultDto>.Fail(
                    409, result.ErrorMessage ?? "定时任务已被其他操作修改，请刷新后重试", result));
            }

            return result.Accepted
                ? ApiResponse<AnShengTimeTaskResultDto>.Success(result, $"插槽 {slotNum} 定时任务已下发")
                : ApiResponse<AnShengTimeTaskResultDto>.BadRequest(
                    result.ErrorMessage ?? "定时任务下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发单插槽定时任务失败: DeviceId={DeviceId}, SlotNum={SlotNum}", deviceId, slotNum);
            return ApiResponse<AnShengTimeTaskResultDto>.Error($"定时任务下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 私有工具
    // ─────────────────────────────────────────────

    /// <summary>请求项 → 传输视图（普通定时）。</summary>
    private static AnShengTimeTaskItem ToTimeTaskItem(AnShengTimeTaskItemRequest r)
        => new()
        {
            Id = r.Id,
            Enable = r.Enable,
            WeekDays = r.WeekDays ?? new List<int>(),
            Hour = r.Hour,
            Minute = r.Minute,
            Action = r.Action,
            UploadEnable = r.UploadEnable
        };

    /// <summary>请求项 → 传输视图（循环定时）。</summary>
    private static AnShengLoopTimeTaskItem ToLoopTimeTaskItem(AnShengLoopTimeTaskItemRequest r)
        => new()
        {
            Id = r.Id,
            Enable = r.Enable,
            WeekDays = r.WeekDays ?? new List<int>(),
            SHour = r.SHour,
            SMinute = r.SMinute,
            EHour = r.EHour,
            EMinute = r.EMinute,
            OnMins = r.OnMins,
            OffMins = r.OffMins
        };
}
