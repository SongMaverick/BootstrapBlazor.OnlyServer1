namespace BootstrapBlazor.OnlyServer1.Models.Common;

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

    // ===== (2026-08-13) 订单管理页面日期范围筛选 =====
    // 都用 [起始, 结束] 闭区间,任一为空表示该侧不限制。
    // Service 层把 End 当作"含当天"(End.AddDays(1) 排他比较)。

    /// <summary>一次图纸提交日期 — 起始(含)</summary>
    public DateTime? PrimarySubmitStart { get; init; }

    /// <summary>一次图纸提交日期 — 结束(含当天)</summary>
    public DateTime? PrimarySubmitEnd { get; init; }

    /// <summary>二次图纸提交日期 — 起始(含)</summary>
    public DateTime? SecondarySubmitStart { get; init; }

    /// <summary>二次图纸提交日期 — 结束(含当天)</summary>
    public DateTime? SecondarySubmitEnd { get; init; }
}
