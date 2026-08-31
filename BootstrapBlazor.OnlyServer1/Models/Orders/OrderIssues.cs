using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// (2026-08-13) 订单问题汇总。每条记录对应某订单的一个问题/障碍,长文本描述 + open/close 状态。
///
/// 数据来源:订单管理页面 "添加问题" 按钮 → 弹窗写入。
/// 状态变更:订单管理 → 问题汇总 页面行内下拉框(open/close)直接编辑。
///
/// 字段命名约定:跟 OrderMDDetail 一致,CreatedBy/UpdatedBy 存用户名,
/// 不直接存 UserId(避免离职后历史记录失真)。
/// </summary>
[SugarTable("OrderIssues")]
public class OrderIssues
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>关联的订单 Id(Orders.Id)。</summary>
    public int OrderId { get; set; }

    /// <summary>
    /// 问题描述(长文本)。NVARCHAR(MAX) 容纳多段文字。
    /// 可空:虽然业务上不该空,但为了 SqlSugar CodeFirst 顺利建表允许 null。
    /// </summary>
    [SugarColumn(IsNullable = true, Length = -1)]
    public string? Content { get; set; }

    /// <summary>
    /// (2026-08-14) 解决方法(长文本)。业务规则:必须先填写才能 close。
    /// 关闭后仍允许编辑(事后补充记录)。NVARCHAR(MAX) 同 Content。
    /// </summary>
    [SugarColumn(IsNullable = true, Length = -1)]
    public string? Resolution { get; set; }

    /// <summary>
    /// 问题状态:open / close。字符串而非 enum,方便后续扩展(pending/wontfix 等)。
    /// 默认值 "open" 由应用层在 Create 时写入。
    /// </summary>
    [SugarColumn(IsNullable = false, Length = 20)]
    public string Status { get; set; } = "open";

    // ===== 审计字段(与 Models/OrderMDDetail.cs / Users.cs 一致) =====

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedTime { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    // (2026-08-14) 关闭人 / 关闭时间:仅在 open → close 转换时由 Service 写入,
    // close 之后不能再修改状态,所以这两个字段一旦写入就是"首次也是唯一一次"的关闭记录。
    [SugarColumn(IsNullable = true, Length = 100)]
    public string? ClosedBy { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ClosedTime { get; set; }

    public bool IsDeleted { get; set; } = false;
}

/// <summary>(2026-08-13) 问题状态字符串常量。</summary>
public static class IssueStatus
{
    public const string Open = "open";
    public const string Close = "close";

    public static readonly string[] All = { Open, Close };
}