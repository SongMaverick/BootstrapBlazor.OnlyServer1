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

    // 订单管理(结构设计订单明细,/order-management)
    // MD = Mechanical Design,与 Models/OrderMDDetail 一致。
    // 明细行随订单存在,不单独新增,故没有 OrderMDDetail.Create
    public const string OrderMDDetailRead   = "OrderMDDetail.Read";
    public const string OrderMDDetailEdit   = "OrderMDDetail.Edit";
    public const string OrderMDDetailDelete = "OrderMDDetail.Delete";

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
