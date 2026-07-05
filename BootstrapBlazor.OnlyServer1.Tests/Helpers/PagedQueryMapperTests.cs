using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Helpers;
using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Tests.Helpers;

/// <summary>
/// PagedQueryMapper 的双向 round-trip 测试。
/// 锁定 Service ↔ UI 边界的契约 —— 改 Mapper 必须同步改这些测试。
/// </summary>
public class PagedQueryMapperTests
{
    private static QueryPageOptions SampleQueryPageOptions() => new()
    {
        SearchText = "2024",
        SortName = "Id",
        SortOrder = SortOrder.Desc,
        PageIndex = 3,
        PageItems = 50,
        // Filters 是 get-only 计算属性(由其他属性合成),不能从外部赋值;
        // 此处不显式设值,测试中关注 SearchText/SortOrder 等可写属性即可。
        SearchModel = "x"
    };

    [Fact]
    public void ToPagedQuery_ConvertsAllFieldsCorrectly()
    {
        var opts = SampleQueryPageOptions();

        var paged = opts.ToPagedQuery();

        Assert.Equal("2024", paged.SearchText);
        Assert.Equal("Id", paged.SortName);
        Assert.Equal(SortDirection.Desc, paged.SortOrder);
        Assert.Equal(3, paged.PageIndex);
        Assert.Equal(50, paged.PageItems);
        Assert.True(paged.HasSearchModel);
    }

    [Fact]
    public void ToPagedQuery_AscSortOrderMapsToAsc()
    {
        var opts = SampleQueryPageOptions();
        opts.SortOrder = SortOrder.Asc;

        Assert.Equal(SortDirection.Asc, opts.ToPagedQuery().SortOrder);
    }

    [Fact]
    public void ToPagedQuery_NormalizesInvalidPageIndexAndPageItems()
    {
        var opts = new QueryPageOptions
        {
            SearchText = null,
            SortName = null,
            SortOrder = SortOrder.Asc,
            PageIndex = 0,         // 异常值
            PageItems = -1,        // 异常值
            SearchModel = null
        };

        var paged = opts.ToPagedQuery();

        Assert.Equal(1, paged.PageIndex);
        Assert.Equal(20, paged.PageItems);
    }

    [Fact]
    public void ToPagedQuery_NoFilters_FilterCountIsZero()
    {
        var opts = new QueryPageOptions();

        var paged = opts.ToPagedQuery();

        Assert.Equal(0, paged.FilterCount);
    }

    [Fact]
    public void ToPagedQuery_NullSearchModel_HasSearchModelFalse()
    {
        var opts = new QueryPageOptions { SearchModel = null };
        var paged = opts.ToPagedQuery();

        Assert.False(paged.HasSearchModel);
    }

    [Fact]
    public void ToQueryData_CopiesAllFields()
    {
        var result = new PagedResult<Orders>
        {
            Items = new() { new() { OrderNumber = 1 } },
            TotalCount = 42,
            IsFiltered = true,
            IsSorted = true,
            IsSearch = true
        };

        var qd = result.ToQueryData();

        Assert.Single(qd.Items);
        Assert.Equal(42, qd.TotalCount);
        Assert.True(qd.IsFiltered);
        Assert.True(qd.IsSorted);
        Assert.True(qd.IsSearch);
    }

    // ===== SortItem<TKey> ↔ SelectedItem =====

    [Fact]
    public void SelectItem_ToSelectedItem_FormatsStringValue()
    {
        var item = new SelectItem<int>(42, "Hello");

        var selected = item.ToSelectedItem();

        Assert.Equal("42", selected.Value);
        Assert.Equal("Hello", selected.Text);
    }

    [Fact]
    public void SelectItems_ToSelectedItems_BulkConversion()
    {
        var items = new[]
        {
            new SelectItem<int>(1, "Admin"),
            new SelectItem<int>(2, "Operator"),
            new SelectItem<int>(3, "Viewer")
        };

        var selected = items.ToSelectedItems().ToList();

        Assert.Equal(3, selected.Count);
        Assert.Equal("Admin", selected[0].Text);
        Assert.Equal("Viewer", selected[2].Text);
    }

    [Fact]
    public void ToDomain_ItemChangedTypeConvert()
    {
        Assert.Equal(Models.ItemChangedType.Add,
            BootstrapBlazor.Components.ItemChangedType.Add.ToDomain());
        Assert.Equal(Models.ItemChangedType.Update,
            BootstrapBlazor.Components.ItemChangedType.Update.ToDomain());
    }

    [Fact]
    public void ToBootstrapBlazor_ItemChangedTypeConvert()
    {
        Assert.Equal(BootstrapBlazor.Components.ItemChangedType.Add,
            Models.ItemChangedType.Add.ToBootstrapBlazor());
        Assert.Equal(BootstrapBlazor.Components.ItemChangedType.Update,
            Models.ItemChangedType.Update.ToBootstrapBlazor());
    }
}
