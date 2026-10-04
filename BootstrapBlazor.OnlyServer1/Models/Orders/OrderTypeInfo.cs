using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.OrderManagement;

[SugarTable("OrderTypes")]
public class OrderTypeInfo
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int OrderId { get; set; }

    [SugarColumn(IsNullable = false, Length = 20)]
    public string OrderType { get; set; } = "全新";

    [SugarColumn(IsNullable = true, Length = 50)]
    public string? ReferenceOrderNumber { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedTime { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; }
}
