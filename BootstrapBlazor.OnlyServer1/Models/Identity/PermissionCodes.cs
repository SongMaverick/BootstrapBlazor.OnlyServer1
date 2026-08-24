namespace BootstrapBlazor.OnlyServer1.Models.Identity;

/// <summary>
/// 细粒度操作权限码常量。
/// 用于 Razor 的 CurrentUser.HasPermission(...) 按钮级控制、API 授权等。
/// 与数据库 Permissions 表 Code 列一一对应(由 DbMigrator 灌种)。
/// </summary>
public static class PermissionCodes
{
    // 订单
    public const string OrderCreate = "Order.Create";
    public const string OrderEdit   = "Order.Edit";
    public const string OrderDelete = "Order.Delete";

    // 订单管理(/order-management)
    // (2026-08-17 重构)从原 OrderMDDetail 拆成 3 张业务表,对应 3 组权限码。
    // 每组都只有 Read/Edit(没有 Create/Delete —— 1:1 强绑定订单,创建跟随新订单,清空内容用 Edit 即可)。
    public const string OrderInfoRead = "OrderInfo.Read";
    public const string OrderInfoEdit = "OrderInfo.Edit";

    public const string OrderDesignPhasesRead = "OrderDesignPhases.Read";
    public const string OrderDesignPhasesEdit = "OrderDesignPhases.Edit";

    public const string OrderImportantComponentsRead = "OrderImportantComponents.Read";
    public const string OrderImportantComponentsEdit = "OrderImportantComponents.Edit";

    // 订单问题汇总(供 BootstrapBlazor.OrderManagementSetup 灌权限码用,主项目已无页面引用)
    //   Read   — 看到问题汇总
    //   Add    — 添加新问题
    //   EditStatus — 改 open/close
    public const string OrderIssueRead       = "OrderIssue.Read";
    public const string OrderIssueAdd        = "OrderIssue.Add";
    public const string OrderIssueEditStatus = "OrderIssue.EditStatus";

    // 图号
    public const string DrawingNumberAllocate = "DrawingNumber.Allocate";
    public const string DrawingNumberRead     = "DrawingNumber.Read";

    // 用户
    public const string UserCreate = "User.Create";
    public const string UserDelete = "User.Delete";

    // 部门
    public const string DepartmentCreate = "Department.Create";
    public const string DepartmentDelete = "Department.Delete";

    // 图名
    public const string DrawingNameCreate = "DrawingName.Create";
    public const string DrawingNameDelete = "DrawingName.Delete";
}
