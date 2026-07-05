namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 通用分页查询参数。Service 层用此 DTO,UI 框架无关。
/// 字段从 BootstrapBlazor 的 QueryPageOptions 收敛而来,语义对齐。
/// </summary>
public class PagedQuery
{
    /// <summary>自由文本搜索(由调用方决定应用到哪些字段)。</summary>
    public string? SearchText { get; init; }

    /// <summary>排序字段名(由调用方决定是否信任)。为空表示不排序。</summary>
    public string? SortName { get; init; }

    /// <summary>排序方向。</summary>
    public SortDirection SortOrder { get; init; } = SortDirection.Asc;

    /// <summary>页码,1-based。</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>每页条数。</summary>
    public int PageItems { get; init; } = 20;

    /// <summary>已应用的过滤条数(用于透传给 PagedResult.IsFiltered 标志,不做 SQL where)。</summary>
    public int FilterCount { get; init; }

    /// <summary>是否有高级搜索表单绑定。</summary>
    public bool HasSearchModel { get; init; }
}
