using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Drawing;

/// <summary>
/// 开关厂家实体。Orders.TapChangerManufacturerId / SwitchManufacturerId 引用本表 Id。
/// NormalizedName 用于 UNIQUE 索引(类似 Departments / Identity Roles 模式)。
/// FK ON DELETE NO ACTION(默认):有订单引用时禁止硬删,仅可软删(IsDeleted=true)。
/// </summary>
[SugarTable("SwitchManufacturers")]
public class SwitchManufacturer
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = false, Length = 100)]
    public string NormalizedName { get; set; } = "";

    public bool IsActive { get; set; } = true;

    /// <summary>创建人(审计字段,沿用项目惯例)。</summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedTime { get; set; }

    /// <summary>更新人(审计字段)。</summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public string? UpdatedBy { get; set; }

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; }
}