namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// (2026-07-08 Review #L-9) 从 DrawingNameList.razor 提取。
/// "添加图名"弹窗的表单模型。
/// </summary>
public class DrawingNameAddModel
{
    public string ComponentName { get; set; } = string.Empty;
    public string Part { get; set; } = string.Empty;
}
