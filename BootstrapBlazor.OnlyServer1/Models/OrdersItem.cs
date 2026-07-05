using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 订单关联表 - 用于存储旧订单ID和新订单ID的映射关系
/// </summary>
[SugarTable("OrdersItem")]
public class OrdersItem
{
    /// <summary>
    /// 主键ID
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 旧订单ID（从Order.sql和ReactorOrder.sql中读取）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public int OldOrderId { get; set; }

    /// <summary>
    /// 新订单ID（从SQL Server数据库中读取）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public int NewOrderId { get; set; }

    /// <summary>
    /// 是否为电抗器订单
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsReactor { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedTime { get; set; }
}