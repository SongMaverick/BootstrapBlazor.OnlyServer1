namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// 图号行视图模型。
/// (2026-07-08 Review #L-9) 从 Models/ 移到 Models/ViewModels/,与 RoleRowViewModel / UserWithRolesViewModel 同目录。
/// </summary>
public class DrawingNumberViewModel
{
    public int Id { get; set; }

    public string DrawingTypeNumber { get; set; }

    public string DrawingName { get; set; }

    public string DrawingType { get; set; }

    public int OrderId { get; set; }

    public int OrderNumber { get; set; }

    public string CreatedBy { get; set; }

    public DateTime CreatedTime { get; set; }

    public string UpdatedBy { get; set; }

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; } = false;
}