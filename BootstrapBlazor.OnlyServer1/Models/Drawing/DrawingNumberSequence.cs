using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Drawing;

/// <summary>
/// 图号序列表 — 每种 drawingType(C01..C09) 一行,记录当前已分配到的最大 DrawingNumber。
/// 分配时通过单条 UPDATE...OUTPUT 原子地 +1 并读出新值,无需显式事务或应用层锁。
/// (2026-07-08 Review #L-7) 注释 DrawNumber → DrawingNumber。
/// </summary>
[SugarTable("DrawingNumberSequence")]
public class DrawingNumberSequence
{
    /// <summary>主键即 drawingType(C01/C02/.../C09)</summary>
    [SugarColumn(IsPrimaryKey = true, Length = 10)]
    public string DrawingType { get; set; } = "";

    /// <summary>当前已分配的最大 DrawingNumber</summary>
    public int CurrentMax { get; set; }
}