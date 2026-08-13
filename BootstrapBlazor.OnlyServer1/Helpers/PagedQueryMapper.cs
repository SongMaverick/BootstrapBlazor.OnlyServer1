using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Helpers;

/// <summary>
/// UI 层 ↔ Service 层边界处的类型映射。
/// 保持 Service 完全不引用 BootstrapBlazor;UI 框架换掉时只改这个文件。
/// </summary>
public static class PagedQueryMapper
{
    /// <summary>BootstrapBlazor QueryPageOptions → 通用 PagedQuery</summary>
    public static PagedQuery ToPagedQuery(this QueryPageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new PagedQuery
        {
            SearchText = options.SearchText,
            SortName = options.SortName,
            SortOrder = options.SortOrder == SortOrder.Asc ? Models.SortDirection.Asc : Models.SortDirection.Desc,
            PageIndex = options.PageIndex <= 0 ? 1 : options.PageIndex,
            PageItems = options.PageItems <= 0 ? 20 : options.PageItems,
            FilterCount = options.Filters?.Count ?? 0,
            HasSearchModel = options.SearchModel is not null,
        };
    }

    /// <summary>(2026-08-13) 订单管理带日期范围筛选的扩展重载。
    /// UI 层把 4 个 DateTimePicker 状态用闭包捕获,经此方法转 PagedQuery 传给 Service。</summary>
    public static PagedQuery ToPagedQuery(
        this QueryPageOptions options,
        DateTime? primarySubmitStart, DateTime? primarySubmitEnd,
        DateTime? secondarySubmitStart, DateTime? secondarySubmitEnd)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new PagedQuery
        {
            SearchText = options.SearchText,
            SortName = options.SortName,
            SortOrder = options.SortOrder == SortOrder.Asc ? Models.SortDirection.Asc : Models.SortDirection.Desc,
            PageIndex = options.PageIndex <= 0 ? 1 : options.PageIndex,
            PageItems = options.PageItems <= 0 ? 20 : options.PageItems,
            FilterCount =
                (primarySubmitStart.HasValue ? 1 : 0) +
                (primarySubmitEnd.HasValue   ? 1 : 0) +
                (secondarySubmitStart.HasValue ? 1 : 0) +
                (secondarySubmitEnd.HasValue   ? 1 : 0),
            HasSearchModel = options.SearchModel is not null,
            PrimarySubmitStart   = primarySubmitStart,
            PrimarySubmitEnd     = primarySubmitEnd,
            SecondarySubmitStart = secondarySubmitStart,
            SecondarySubmitEnd   = secondarySubmitEnd,
        };
    }

    /// <summary>通用 PagedResult&lt;T&gt; → BootstrapBlazor QueryData&lt;T&gt;</summary>
    public static QueryData<T> ToQueryData<T>(this PagedResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new QueryData<T>
        {
            Items = result.Items,
            TotalCount = result.TotalCount,
            IsFiltered = result.IsFiltered,
            IsSorted = result.IsSorted,
            IsSearch = result.IsSearch,
        };
    }

    /// <summary>通用 SelectItem&lt;T&gt; → BootstrapBlazor SelectedItem(Via ToString)。</summary>
    public static SelectedItem ToSelectedItem<TKey>(this SelectItem<TKey> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new SelectedItem(item.Value?.ToString() ?? string.Empty, item.Text ?? string.Empty);
    }

    public static List<SelectedItem> ToSelectedItems<TKey>(this IEnumerable<SelectItem<TKey>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(i => i.ToSelectedItem()).ToList();
    }

    /// <summary>BootstrapBlazor ItemChangedType → 通用 Models.ItemChangedType</summary>
    public static Models.ItemChangedType ToDomain(this BootstrapBlazor.Components.ItemChangedType t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t == BootstrapBlazor.Components.ItemChangedType.Add
            ? Models.ItemChangedType.Add
            : Models.ItemChangedType.Update;
    }

    /// <summary>通用 Models.ItemChangedType → BootstrapBlazor ItemChangedType</summary>
    public static BootstrapBlazor.Components.ItemChangedType ToBootstrapBlazor(this Models.ItemChangedType t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t == Models.ItemChangedType.Add
            ? BootstrapBlazor.Components.ItemChangedType.Add
            : BootstrapBlazor.Components.ItemChangedType.Update;
    }
}

