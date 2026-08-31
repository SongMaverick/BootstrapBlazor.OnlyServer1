using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Drawing;

public class DrawingNames
{

    /// <summary>
    /// 序号
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 所属部分
    /// </summary>
    public string Part { get; set; } = "";

    /// <summary>
    /// 图纸名称
    /// </summary>
    public string ComponentName { get; set; } = "";

    /// <summary>
    /// 创建人
    /// </summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>
    /// 更新人
    /// </summary>
    public string UpdatedBy { get; set; } = "";

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedTime { get; set; }

    /// <summary>
    /// 是否删除。DB 列名沿用旧库 Isdelete(C# 命名约定统一为 IsDeleted)。
    /// </summary>
    [SugarColumn(ColumnName = "Isdelete")]
    public bool IsDeleted { get; set; }
}