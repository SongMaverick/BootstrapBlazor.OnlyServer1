using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.TapChanger;

/// <summary>
/// 有载分接开关(Tap-Changer)型号字典。
/// 一行 = 一个型号设计变体(由 11 个分量字段合成 Name)。
/// FK:ManufacturerId → TapChangerManufacturers.Id(默认 NO ACTION,有型号引用时禁硬删厂家)。
/// Name 由 Service.CreateAsync/UpdateAsync 在入口处按图例格式拼装,
///   1XDU-I-3204-245-245-06 05 0 D(BD 用空格、其它字段用 "-" / "X" / 空格的固定连接符)
/// 长度 200 容纳典型长型号;极个别超长会被截断报错。
/// </summary>
[SugarTable("TapChangerModels")]
public class TapChangerModel
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>FK → TapChangerManufacturers.Id。NOT NULL。</summary>
    [SugarColumn(IsNullable = false)]
    public int ManufacturerId { get; set; }

    /// <summary>完整型号代码,由 Service 在 Save 前自动拼装,可按 Name 唯一索引去重。</summary>
    [SugarColumn(IsNullable = false, Length = 200)]
    public string Name { get; set; } = "";

    /// <summary>图中:Number of columns(例 1)。</summary>
    public int? NumberOfColumns { get; set; }

    /// <summary>图中:Type(例 "DU")。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? TypeCode { get; set; }

    /// <summary>图中:Number of phases(例 "I" / "II" / "III")。</summary>
    [SugarColumn(IsNullable = true, Length = 10)]
    public string? NumberOfPhases { get; set; }

    /// <summary>图中:Rated current(例 "3204")。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? RatedCurrentCode { get; set; }

    /// <summary>图中:Y/JD 标识(可空)。</summary>
    [SugarColumn(IsNullable = true, Length = 10)]
    public string? YjdCode { get; set; }

    /// <summary>图中:Um (kV),例 245。</summary>
    public decimal? UmKv { get; set; }

    /// <summary>图中:Tap selector size(例 "245")。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? TapSelectorSize { get; set; }

    /// <summary>图中:Basic connection diagram 第 1 段(例 "06")。</summary>
    [SugarColumn(IsNullable = true, Length = 10)]
    public string? BD1 { get; set; }

    /// <summary>图中:Basic connection diagram 第 2 段(例 "05")。</summary>
    [SugarColumn(IsNullable = true, Length = 10)]
    public string? BD2 { get; set; }

    /// <summary>图中:Basic connection diagram 第 3 段(例 "0")。</summary>
    [SugarColumn(IsNullable = true, Length = 10)]
    public string? BD3 { get; set; }

    /// <summary>图中:Basic connection diagram 第 4 段(例 "D")。</summary>
    [SugarColumn(IsNullable = true, Length = 10)]
    public string? BD4 { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>创建人(审计字段)。</summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedTime { get; set; }

    /// <summary>更新人(审计字段)。</summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public string? UpdatedBy { get; set; }

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; }
}
