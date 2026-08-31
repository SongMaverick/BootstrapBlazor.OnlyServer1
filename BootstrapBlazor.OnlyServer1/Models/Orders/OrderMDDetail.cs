using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.OrderManagement;

/// <summary>
/// 结构设计订单明细(订单管理)。
/// 与 Orders 一对一扩展:OrderId 引用 Orders.Id,由过滤唯一索引
/// IX_OrderMDDetail_OrderId (WHERE IsDeleted = 0) 保证"一个订单只有一条有效明细"。
///
/// 字段来源:2026 订单管理.xlsx →「2026结构设计订单」sheet。
/// 订单号/容量/电压/是否电抗器 不在本表,统一读 Orders,避免两处维护。
/// 「问题汇总」「详细设计完成状态」两列本期不做。
///
/// 命名约定:MD = Mechanical Design(机械设计),与项目里「MD 部门」一致。
/// </summary>
[SugarTable("OrderMDDetail")]
public class OrderMDDetail
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 关联的订单 Id(Orders.Id)。同一订单只允许一条未删除的明细。
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// 项目名称(2026-08-13 由 Customer 改名,DB 列同步 sp_rename)
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 200)]
    public string? ProjectName { get; set; }

    /// <summary>
    /// 国内 / 出口
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? MarketType { get; set; }

    /// <summary>
    /// 台数
    /// </summary>
    public int UnitCount { get; set; } = 1;

    /// <summary>
    /// 项目负责人(一次)→ Users.Id
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? PrimaryEngineerId { get; set; }

    /// <summary>
    /// 项目负责人(二次)→ Users.Id
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? SecondaryEngineerId { get; set; }

    /// <summary>
    /// 一次图纸提交日期
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? PrimarySubmitDate { get; set; }

    /// <summary>
    /// 一次图纸确认日期
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? PrimaryConfirmDate { get; set; }

    /// <summary>
    /// 设计工具(2D / 3D)
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? DesignTool { get; set; }

    /// <summary>
    /// 二次图纸提交日期
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? SecondarySubmitDate { get; set; }

    /// <summary>
    /// 二次图纸确认日期
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? SecondaryConfirmDate { get; set; }

    /// <summary>
    /// 是否分体 / 大架子
    /// </summary>
    public bool IsSplitFrame { get; set; }

    /// <summary>
    /// 试验套管型号。是否需要试验套管改为隐式判断:型号或数量任一有值 = 需要。
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 200)]
    public string? TestBushingModel { get; set; }

    /// <summary>
    /// 试验套管数量(整数,允许空)。是否需要试验套管改为隐式判断:型号或数量任一有值 = 需要。
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? TestBushingQuantity { get; set; }

    /// <summary>
    /// 开关数量
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? SwitchCount { get; set; }

    /// <summary>
    /// 焊接箱沿
    /// </summary>
    public bool IsWeldedTankRim { get; set; }

    // ===== 审计字段(与 Models/Orders.cs 一致) =====

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedTime { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; } = false;
}