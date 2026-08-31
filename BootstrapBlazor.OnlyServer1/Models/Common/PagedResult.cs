namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 通用分页结果。Service 层用此 DTO,UI 框架无关。
/// 字段从 BootstrapBlazor 的 QueryData&lt;T&gt; 收敛而来,语义对齐。
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; init; } = new();

    public int TotalCount { get; init; }

    public bool IsFiltered { get; init; }

    public bool IsSorted { get; init; }

    public bool IsSearch { get; init; }
}
