namespace BootstrapBlazor.OnlyServer1.Models.BusinessRules;

/// <summary>
/// DrawingType 业务规则的可配置项。
/// 集中放于 appsettings.json 的 "BusinessRules.DrawingType" 节。
/// 修改规则不再动代码,直接改配置 + 重新发布即可。
/// </summary>
public class DrawingTypeOptions
{
    /// <summary>电抗器(IsReactor=true)订单对应的 DrawingType。</summary>
    public string ReactorType { get; set; } = "C08";

    /// <summary>
    /// Power → DrawingType 映射规则列表。
    /// 匹配逻辑:遍历列表,找第一条 Min ≤ Power 且 (Max 为 null 或 Power ≤ Max) 的规则。
    /// </summary>
    public List<PowerRangeRule> PowerRanges { get; set; } = new();

    /// <summary>
    /// 每种 DrawingType 的图号种子值(DrawingNumberAllocator 在该类型首次分配时使用)。
    /// 缺省 = 0。
    /// </summary>
    public Dictionary<string, int> Seeds { get; set; } = new();
}
