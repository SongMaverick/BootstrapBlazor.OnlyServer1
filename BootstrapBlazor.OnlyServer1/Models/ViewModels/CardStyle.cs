namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

/// <summary>
/// (2026-07-08 Review #L-9) 从 AddDrawingNumbers.razor 提取。
/// 卡片样式:用 Part 名字 hash 选 Bootstrap 配色,同一 Part 颜色稳定。
/// BodyBg 是 header 同色系的浅色,让图名区域背景"延续" header 的色感。
/// </summary>
public record CardStyle(string HeaderClass, string HeaderTextClass, string BodyBg);
