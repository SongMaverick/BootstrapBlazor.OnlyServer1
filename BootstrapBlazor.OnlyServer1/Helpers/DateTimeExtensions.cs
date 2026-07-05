namespace BootstrapBlazor.OnlyServer1.Helpers;

/// <summary>
/// DateTime 在 Razor 中显示的统一格式。
/// 用法:@someDate.ToDisplayString() 代替 @someDate.ToString("yyyy-MM-dd HH:mm:ss")
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>默认显示格式(本地时区)。</summary>
    public const string DisplayFormat = "yyyy-MM-dd HH:mm:ss";

    public static string ToDisplayString(this DateTime dt) => dt.ToString(DisplayFormat);

    public static string ToDisplayString(this DateTime? dt) => dt?.ToString(DisplayFormat) ?? "";
}
