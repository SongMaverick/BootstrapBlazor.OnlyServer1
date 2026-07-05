namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 通用下拉/选择项。Service 层用它表达"值/文本"对,UI 层再映射为
/// 具体框架的 SelectedItem / MudBlazor / 浏览器原生等。
/// </summary>
public class SelectItem<TKey>
{
    public TKey Value { get; set; } = default!;

    public string Text { get; set; } = "";

    public SelectItem() { }

    public SelectItem(TKey value, string text)
    {
        Value = value;
        Text = text;
    }
}
