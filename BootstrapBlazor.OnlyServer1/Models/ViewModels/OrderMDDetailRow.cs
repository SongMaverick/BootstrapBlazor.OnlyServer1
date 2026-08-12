using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// 「订单管理」列表行。
///
/// 数据来源是 Orders LEFT JOIN OrderMDDetail —— 列表以订单为主体,
/// 尚未填写明细的订单也会出现(此时 DetailId = 0,明细字段全为默认值)。
/// 保存时由 OrderMDDetailService 按 DetailId 决定 INSERT 还是 UPDATE(upsert)。
///
/// 订单号/容量/电压/是否电抗器 来自 Orders,本页只读,修改请去 /orders。
/// PrimaryEngineerName / SecondaryEngineerName 是 join 出来的展示字段,不落库。
/// </summary>
public class OrderMDDetailRow
{
    // ===== 来自 OrderMDDetail =====

    /// <summary>OrderMDDetail.Id;为 0 表示该订单尚无明细行</summary>
    public int DetailId { get; set; }

    /// <summary>Orders.Id</summary>
    public int OrderId { get; set; }

    public string? Customer { get; set; }

    public string? MarketType { get; set; }

    public int UnitCount { get; set; } = 1;

    public int? PrimaryEngineerId { get; set; }

    public int? SecondaryEngineerId { get; set; }

    public DateTime? PrimarySubmitDate { get; set; }

    public DateTime? PrimaryConfirmDate { get; set; }

    public string? DesignTool { get; set; }

    public DateTime? SecondarySubmitDate { get; set; }

    public DateTime? SecondaryConfirmDate { get; set; }

    public bool IsSplitFrame { get; set; }

    public string? TestBushingModel { get; set; }

    public int? TestBushingQuantity { get; set; }

    public int? SwitchCount { get; set; }

    public bool IsWeldedTankRim { get; set; }

    // ===== 来自 Orders(只读) =====

    public int OrderNumber { get; set; }

    public int Power { get; set; }

    public int RatedVoltage { get; set; }

    public bool IsReactor { get; set; }

    // ===== join 出来的展示字段,不落库 =====

    [SugarColumn(IsIgnore = true)]
    public string? PrimaryEngineerName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string? SecondaryEngineerName { get; set; }
}