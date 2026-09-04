using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.OrderManagement;

public class Orders
{

    /// <summary>
    /// 序号 自增(Order / ReactorOrder 合并,Id 用新自增值,保证唯一)
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 订单号
    /// </summary>
    public int OrderNumber { get; set; }

    /// <summary>
    /// 容量
    /// </summary>
    public int Power { get; set; }

    /// <summary>
    /// 额定电压
    /// </summary>
    public int RatedVoltage { get; set; }

    /// <summary>
    /// 是否为电抗器
    /// </summary>
    public bool IsReactor { get; set; }

    /// <summary>
    /// 创建人
    /// </summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedTime { get; set; }

    /// <summary>
    /// 更新人
    /// </summary>
    public string UpdatedBy { get; set; } = "";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>
    /// 是否删除
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    // ===== (2026-09-04) 视图虚拟列:不映射到 Orders 表,只用于 GetMasterListAsync 的 LeftJoin 投影。
    // 订单管理页订单列表 + 一次提交日期筛选需要 OrderDesignPhases.Phase1SubmitDate。
    // 走"在 Orders 上加一个 [SugarColumn(IsIgnore=true)] 字段"而不是新建 OrderListRow:
    //   - 不改 Table TItem 类型,UI 层只多一个 TableColumn 即可
    //   - 不影响现有 _selectedOrder (OrderEntity) 类型推断
    // IsIgnore=true 告诉 SqlSugar CodeFirst 不要建/改列,运行时也只是普通 CLR 属性填充。 =====
    [SugarColumn(IsIgnore = true)]
    public DateTime? Phase1SubmitDate { get; set; }
}