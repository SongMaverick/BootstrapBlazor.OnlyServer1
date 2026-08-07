namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// 图号行视图模型。
/// (2026-07-08 Review #L-9) 从 Models/ 移到 Models/ViewModels/,与 RoleRowViewModel / UserWithRolesViewModel 同目录。
/// </summary>
public class DrawingNumberViewModel
{
    public int Id { get; set; }

    // (2026-08-06)抑制 CS8618 — 全部由 C0XQueryService.ToViewModel() 一次填齐,
    // 没有"未初始化"路径。直接用 null! 占位告诉编译器别再检查。
    public string DrawingTypeNumber { get; set; } = null!;

    public string DrawingName { get; set; } = null!;

    public string DrawingType { get; set; } = null!;

    public int OrderId { get; set; }

    public int OrderNumber { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedTime { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; } = false;
}