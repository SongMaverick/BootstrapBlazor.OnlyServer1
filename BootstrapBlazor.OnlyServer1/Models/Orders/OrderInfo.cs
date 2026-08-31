using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.OrderManagement;

/// <summary>
/// 订单项目级信息(订单管理 - 业务域 1/3)。
///
/// 与 Orders 一对一扩展:OrderId 引用 Orders.Id,由过滤唯一索引
/// IX_OrderInfo_OrderId (WHERE IsDeleted = 0) 保证"一个订单只有一条有效 OrderInfo"。
///
/// (2026-08-17 重构)从原 OrderMDDetail 表拆出,只保留"项目级"信息:
/// 项目名称 / 国内出口 / 台数。
/// 人员+日期走 OrderDesignPhases,物理特征走 OrderImportantComponents。
///
/// 订单号/容量/电压/是否电抗器 不在本表,统一读 Orders,避免两处维护。
/// </summary>
[SugarTable("OrderInfo")]
public class OrderInfo
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>关联的订单 Id(Orders.Id)。同一订单只允许一条未删除的 OrderInfo。</summary>
    public int OrderId { get; set; }

    /// <summary>项目名称。</summary>
    [SugarColumn(IsNullable = true, Length = 200)]
    public string? ProjectName { get; set; }

    /// <summary>国内 / 出口。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? MarketType { get; set; }

    /// <summary>台数。默认 1 台(空白明细时显示)。</summary>
    public int UnitCount { get; set; } = 1;

    // ===== 审计字段(与 Models/Orders.cs / Models/OrderDesignPhases.cs 一致) =====

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedTime { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; }
}