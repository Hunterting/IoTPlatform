using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建设备请求
/// </summary>
public class CreateDeviceRequest
{
    [Required(ErrorMessage = "设备名称不能为空")]
    [MaxLength(200, ErrorMessage = "设备名称长度不能超过200字符")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 应用代码（租户码）。
    ///
    /// 【★ 收敛后语义（T2）：本字段<b>不再被服务端信任</b>为设备的归属租户。】
    /// 设备归属 AppCode 一律由服务端按调用者身份决定：
    ///   · 超级管理员直接新增：必须显式给出目标租户，见 <see cref="TargetAppCode"/>；
    ///   · 租户（admin/operator 等）：入口已在前端隐藏、接口已返回 403，不会走到落库。
    /// 故移除必填约束；即便客户端传了 AppCode，服务端也会以 <see cref="TargetAppCode"/> 或
    /// 调用者身份为准，避免请求体劫持多租户隔离（风险 R3）。
    /// </summary>
    [MaxLength(50, ErrorMessage = "应用代码长度不能超过50字符")]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>
    /// 目标租户码（仅超级管理员直接新增时使用）。
    ///
    /// 收敛方案要求「AppCode 由服务端按调用者身份确定」。超级管理员代表平台级身份、
    /// token 通常不带 AppCode claim，故必须在此<b>显式指定</b>要建到哪个租户下；
    /// 为空时 <see cref="DeviceService.CreateDeviceAsync"/> 会拒绝（HTTP 400）。
    /// 租户账号无权调用本接口（控制器 403），该字段对其无意义。
    /// </summary>
    [MaxLength(50, ErrorMessage = "目标租户码长度不能超过50字符")]
    public string? TargetAppCode { get; set; }

    [MaxLength(100, ErrorMessage = "型号长度不能超过100字符")]
    public string? Model { get; set; }

    [MaxLength(100, ErrorMessage = "序列号长度不能超过100字符")]
    public string? SerialNumber { get; set; }

    [MaxLength(100, ErrorMessage = "类别长度不能超过100字符")]
    public string? Category { get; set; }

    [MaxLength(200, ErrorMessage = "位置长度不能超过200字符")]
    public string? Location { get; set; }

    public long? AreaId { get; set; }

    public long? ProjectId { get; set; }

    [MaxLength(200, ErrorMessage = "项目名称长度不能超过200字符")]
    public string? ProjectName { get; set; }

    public string? EnergyTypes { get; set; }

    [Required(ErrorMessage = "状态不能为空")]
    [MaxLength(20, ErrorMessage = "状态长度不能超过20字符")]
    public string Status { get; set; } = "offline";

    public DateTime? InstallDate { get; set; }

    public DateTime? LastMaintenance { get; set; }

    [MaxLength(200, ErrorMessage = "供应商长度不能超过200字符")]
    public string? Supplier { get; set; }

    public DateTime? WarrantyDate { get; set; }

    public double? Power { get; set; }

    [MaxLength(20, ErrorMessage = "电压长度不能超过20字符")]
    public string? Voltage { get; set; }

    public bool MeterInstalled { get; set; }
}
