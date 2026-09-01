using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.TapChanger;

/// <summary>
/// 有载分接开关(Tap-Changer)厂家字典。
/// NormalizedName 用于大小写无关的 UNIQUE 索引(UpperInvariant),
/// 名字不同的"常州西电"和"常州 西电"会被视作同一条记录。
/// 软删(IsDeleted=true)与启停(IsActive)是两条不同的链路:
///   IsActive  — 字典级启停,下拉项中是否可选
///   IsDeleted — 是否在管理页里出现
/// </summary>
[SugarTable("TapChangerManufacturers")]
public class TapChangerManufacturer
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = false, Length = 100)]
    public string NormalizedName { get; set; } = "";

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
