namespace BootstrapBlazor.OnlyServer1.Models.ViewModels;

public class OrderTypeRow
{
    public int TypeId { get; set; }
    public int OrderId { get; set; }
    public string OrderType { get; set; } = "全新";
    public string? ReferenceOrderNumber { get; set; }
}
