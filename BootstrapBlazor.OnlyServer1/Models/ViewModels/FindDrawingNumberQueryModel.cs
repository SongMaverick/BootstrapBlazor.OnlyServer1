namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// (2026-07-08 Review #L-9) 从 FindDrawingNumber.razor 提取。
/// 查询图号页的表单模型:DrawingType + 中段 2 位 + 末段 3 位。
/// </summary>
public class FindDrawingNumberQueryModel
{
    public string DrawingNumberType { get; set; } = string.Empty;

    /// <summary>
    /// 中段 2 位(对应 DrawingTypeNumber 的 "XX" 部分,例如 "34")。
    /// </summary>
    public string MiddleDigits { get; set; } = string.Empty;

    /// <summary>
    /// 末段 3 位(对应 DrawingTypeNumber 的 "XXX" 部分,例如 "050")。
    /// </summary>
    public string LastDigits { get; set; } = string.Empty;
}
