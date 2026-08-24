using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 订单设计阶段人员 + 时间(订单管理 - 业务域 2/3)。
///
/// 与 Orders 一对一扩展:OrderId 引用 Orders.Id,由过滤唯一索引
/// IX_OrderDesignPhases_OrderId (WHERE IsDeleted = 0) 保证"一个订单只有一条有效记录"。
///
/// (2026-08-17 重构)从原 OrderMDDetail 表拆出。一/二次各跑自己的状态机:
///   - Status:  NotStarted / Submitted / Confirmed
///   - SubmitDate: 阶段图纸提交日期(Status 进入 Submitted 时填写)
///   - ConfirmDate: 阶段图纸确认日期(Status 进入 Confirmed 时填写)
/// 4 个日期硬并列改为 2 个状态机,体现"提交后才能确认"的业务约束。
/// EngineerId → Users.Id,表示该阶段的项目负责人。
/// </summary>
[SugarTable("OrderDesignPhases")]
public class OrderDesignPhases
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>关联的订单 Id(Orders.Id)。同一订单只允许一条未删除的记录。</summary>
    public int OrderId { get; set; }

    // ===== 一次设计 =====

    /// <summary>项目负责人(一次)→ Users.Id。</summary>
    [SugarColumn(IsNullable = true)]
    public int? Phase1EngineerId { get; set; }

    /// <summary>一次图纸提交日期。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? Phase1SubmitDate { get; set; }

    /// <summary>一次图纸确认日期。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? Phase1ConfirmDate { get; set; }

    // ===== 二次设计 =====

    /// <summary>项目负责人(二次)→ Users.Id。</summary>
    [SugarColumn(IsNullable = true)]
    public int? Phase2EngineerId { get; set; }

    /// <summary>二次图纸提交日期。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? Phase2SubmitDate { get; set; }

    /// <summary>二次图纸确认日期。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? Phase2ConfirmDate { get; set; }

    // ===== 审计字段 =====

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedTime { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    public bool IsDeleted { get; set; }
}