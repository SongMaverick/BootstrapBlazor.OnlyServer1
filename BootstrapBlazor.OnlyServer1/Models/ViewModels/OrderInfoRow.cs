using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// 「订单项目信息」列表行(订单管理业务域 1/3)。
///
/// 数据来源:Orders LEFT JOIN OrderInfo —— 列表以订单为主体,
/// 尚未填写项目信息的订单也会出现(此时 InfoId = 0,业务字段全为默认值)。
/// 保存时由 OrderInfoService 按 InfoId 决定 INSERT 还是 UPDATE(upsert)。
///
/// 订单号/容量/电压/是否电抗器 来自 Orders,本页只读,修改请去 /orders。
/// </summary>
public class OrderInfoRow
{
    // ===== 来自 OrderInfo =====

    /// <summary>OrderInfo.Id;为 0 表示该订单尚无项目信息行。</summary>
    public int InfoId { get; set; }

    /// <summary>Orders.Id。</summary>
    public int OrderId { get; set; }

    public string? ProjectName { get; set; }

    public string? MarketType { get; set; }

    public int UnitCount { get; set; } = 1;

    // ===== 来自 Orders(只读) =====

    public int OrderNumber { get; set; }

    public int Power { get; set; }

    public int RatedVoltage { get; set; }

    public bool IsReactor { get; set; }
}