using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.OrderManagement;

/// <summary>
/// 订单重要组件特征(订单管理 - 业务域 3/3)。
///
/// 与 Orders 一对一扩展:OrderId 引用 Orders.Id,由过滤唯一索引
/// IX_OrderImportantComponents_OrderId (WHERE IsDeleted = 0) 保证"一个订单只有一条有效记录"。
///
/// (2026-08-17 重构)从原 OrderMDDetail 表拆出。本表只放"组件/物理特征"维度:
/// 分体大架子 / 焊接箱沿 / 开关数量 / 试验套管(型号+数量+显式"是否需要")。
///
/// 设计变更:试验套管"是否需要"从原隐式判断(型号 || 数量任一有值)改为显式
/// IsTestBushingRequired 字段 —— 避免 Excel 合并单元格逻辑被原样搬进库。
/// TestBushingModel / TestBushingQuantity 仅当 IsTestBushingRequired=true 时填写。
/// </summary>
[SugarTable("OrderImportantComponents")]
public class OrderImportantComponents
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>关联的订单 Id(Orders.Id)。同一订单只允许一条未删除的记录。</summary>
    public int OrderId { get; set; }

    /// <summary>是否分体 / 大架子。</summary>
    public bool IsSplitFrame { get; set; }

    /// <summary>焊接箱沿。</summary>
    public bool IsWeldedTankRim { get; set; }

    /// <summary>开关数量。</summary>
    [SugarColumn(IsNullable = true)]
    public int? SwitchCount { get; set; }

    /// <summary>(2026-08-17 新增)是否需要试验套管 —— 显式 boolean,不再用"型号||数量任一非空"隐式判断。</summary>
    //public bool IsTestBushingRequired { get; set; }

    /// <summary>试验套管型号。仅 IsTestBushingRequired=true 时填写。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? TestBushingModel { get; set; }

    /// <summary>试验套管数量(整数,允许空)。仅 IsTestBushingRequired=true 时填写。</summary>
    [SugarColumn(IsNullable = true)]
    public int? TestBushingQuantity { get; set; }

    // ===== 审计字段 =====

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedTime { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; }
}