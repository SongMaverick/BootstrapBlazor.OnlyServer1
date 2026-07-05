namespace BootstrapBlazor.OnlyServer1.Models.BusinessRules;

/// <summary>
/// 单条 Power → DrawingType 映射规则。
/// 范围是闭区间 [Min, Max];Max 为 null 表示无上限。
/// Power ≤ 2 默认无映射(null)用第一条 Min=0 Max=2 Type=null 表示;
/// Power ≥ 400 落到 Max=null 的最后一条。
/// </summary>
public class PowerRangeRule
{
    public int Min { get; set; }

    /// <summary>闭区间上界。null 表示无限。</summary>
    public int? Max { get; set; }

    /// <summary>命中此区间的 DrawingType(如 "C02")。null 表示无映射。</summary>
    public string? Type { get; set; }
}
