namespace BootstrapBlazor.OnlyServer1.Models.Common;

/// <summary>
/// 排序方向。独立于 BootstrapBlazor 的同名枚举(BootstrapBlazor.Components.SortOrder),
/// 避免 Service 层依赖 UI 框架。命名取 "SortDirection" 是为与 UI 框架的 SortOrder 区分。
/// </summary>
public enum SortDirection
{
    Asc = 0,
    Desc = 1,
}
