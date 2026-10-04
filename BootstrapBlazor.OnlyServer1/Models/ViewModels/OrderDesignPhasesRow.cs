using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// 「订单一次二次人员时间」列表行(订单管理业务域 2/3)。
///
/// 数据来源:Orders LEFT JOIN OrderDesignPhases LEFT JOIN Users x2 —— 列表以订单为主体,
/// 尚未填写阶段信息的订单也会出现(此时 PhasesId = 0,业务字段全为默认值)。
/// 保存时由 OrderDesignPhasesService 按 PhasesId 决定 INSERT 还是 UPDATE(upsert)。
///
/// 订单号/容量/电压/是否电抗器 来自 Orders,本页只读,修改请去 /orders。
/// Phase1EngineerName / Phase2EngineerName 是 join 出来的展示字段,不落库。
/// </summary>
public class OrderDesignPhasesRow
{
    // ===== 来自 OrderDesignPhases =====

    /// <summary>OrderDesignPhases.Id;为 0 表示该订单尚无阶段记录。</summary>
    public int PhasesId { get; set; }

    /// <summary>Orders.Id。</summary>
    public int OrderId { get; set; }

    // 一次
    public int? Phase1EngineerId { get; set; }

    public DateTime? Phase1SubmitDate { get; set; }

    public DateTime? Phase1ConfirmDate { get; set; }

    // 二次
    public int? Phase2EngineerId { get; set; }

    public DateTime? Phase2SubmitDate { get; set; }

    public DateTime? Phase2ConfirmDate { get; set; }

    public int? ProjectManagerId { get; set; }

    // ===== 来自 Orders(只读) =====

    public int OrderNumber { get; set; }

    public int Power { get; set; }

    public int RatedVoltage { get; set; }

    public bool IsReactor { get; set; }

    // ===== join 出来的展示字段,不落库 =====

    [SugarColumn(IsIgnore = true)]
    public string? Phase1EngineerName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string? Phase2EngineerName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string? ProjectManagerName { get; set; }
}