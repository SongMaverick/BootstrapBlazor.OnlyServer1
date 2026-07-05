namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// Service 层对"新增/修改"语义的轻量枚举。
/// 镜像 BootstrapBlazor.Components.ItemChangedType 的成员,但属
/// Models 下,Service 层不再依赖 UI 框架。
/// 转换见 <c>BootstrapBlazor.OnlyServer1.Helpers.ItemChangedTypeMapper.ToDomain</c>。
/// </summary>
public enum ItemChangedType
{
    Add,
    Update,
}
