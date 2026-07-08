using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// (2026-07-08 Review #L-9) 从 AddDrawingNumbers.razor 提取。
/// 弹窗中"图名候选"复选框项:含是否已勾选 / 是否已加入订单两个状态。
/// </summary>
public class CheckboxItem
{
    public int Id { get; set; }
    public DrawingNames DrawingName { get; set; } = new();
    public bool IsChecked { get; set; }
    public bool IsAdded { get; set; }
}
