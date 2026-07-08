using System.ComponentModel.DataAnnotations;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// (2026-07-08 Review #L-9) 从 OrderDrawingNumbers.razor 提取。
/// (2026-07-08 Review #L-7) 字段 DrawName → DrawingName。
/// 订单图号编辑弹窗的表单模型,目前只有 DrawingName 一个字段。
/// </summary>
public class OrderDrawingNumberEditModel
{
    [Required(ErrorMessage = "图纸名称不能为空")]
    public string DrawingName { get; set; } = string.Empty;
}
