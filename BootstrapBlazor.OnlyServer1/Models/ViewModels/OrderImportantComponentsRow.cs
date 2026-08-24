using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// 「订单重要组件」列表行(订单管理业务域 3/3)。
///
/// 数据来源:Orders LEFT JOIN OrderImportantComponents —— 列表以订单为主体,
/// 尚未填写组件特征的订单也会出现(此时 ComponentsId = 0,业务字段全为默认值)。
/// 保存时由 OrderImportantComponentsService 按 ComponentsId 决定 INSERT 还是 UPDATE(upsert)。
///
/// 订单号/容量/电压/是否电抗器 来自 Orders,本页只读,修改请去 /orders。
/// </summary>
public class OrderImportantComponentsRow
{
    // ===== 来自 OrderImportantComponents =====

    /// <summary>OrderImportantComponents.Id;为 0 表示该订单尚无组件记录。</summary>
    public int ComponentsId { get; set; }

    /// <summary>Orders.Id。</summary>
    public int OrderId { get; set; }

    public bool IsSplitFrame { get; set; }

    public bool IsWeldedTankRim { get; set; }

    public int? SwitchCount { get; set; }

    public string? TestBushingModel { get; set; }

    public int? TestBushingQuantity { get; set; }

    // ===== 来自 Orders(只读) =====

    public int OrderNumber { get; set; }

    public int Power { get; set; }

    public int RatedVoltage { get; set; }

    public bool IsReactor { get; set; }
}