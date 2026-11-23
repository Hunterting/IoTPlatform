using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 用户-客户授权关系（多对多中间表）。
///
/// 业务规则（见多客户授权模型设计 §3.1 / §5.5）：
///  - 一个 admin 可持有多条客户授权；同一个客户也可授予多个 admin（D6）。
///    唯一约束是 (UserId, CustomerId)，customer_id 上刻意不加单列唯一约束。
///  - 刻意 <b>不实现</b> <see cref="Data.IHasAppCode"/>：本表没有 AppCode，且授权解析需要
///    userId 上下文；若纳入全局租户过滤器，会因上下文缺失导致 admin 集合被误过滤。
///  - Source 区分回填与人工分配：manual / migration / seed。
///  - RevokedAt 预留给 M10 审计；本轮撤销仍是物理删除，该列保持 NULL。
/// </summary>
[Table("user_customers")]
public class UserCustomer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>被授权的 admin 用户 ID（FK → users.Id，ON DELETE CASCADE）。</summary>
    public long UserId { get; set; }

    /// <summary>授权客户 ID（FK → customers.Id，ON DELETE RESTRICT）。</summary>
    public long CustomerId { get; set; }

    /// <summary>审计：授予人用户 ID（M10 预留，ON DELETE SET NULL）。</summary>
    public long? GrantedByUserId { get; set; }

    /// <summary>来源：manual / migration / seed。</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = "manual";

    /// <summary>授予时间（UTC）。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后变更时间（UTC）。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>预留：软撤销时间；本轮不启用，始终为 NULL。</summary>
    public DateTime? RevokedAt { get; set; }

    [ForeignKey("UserId")]
    public virtual User? User { get; set; }

    [ForeignKey("CustomerId")]
    public virtual Customer? Customer { get; set; }

    [ForeignKey("GrantedByUserId")]
    public virtual User? GrantedByUser { get; set; }
}
