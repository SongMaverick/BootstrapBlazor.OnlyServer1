using System.Reflection;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.DrawingNames;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// (2026-07-07 Review #H3 / 2026-07-08 命名审查 L-4)
/// OrderBy(string) 字符串拼接 SortName 防注入测试。
/// 这些测试只验证白名单本身包含哪些列 — 不模拟 SqlSugar 执行。
/// 真正的运行时验证需要在集成测试里跑真实 SQL。
/// </summary>
public class OrderByWhitelistTests
{
    // 2026-07-08 L-4 重构后 OrdersService 把 _ordersAllowedSortCols 暴露为
    // IReadOnlyCollection<string>(与 BaseQueryService.AllowedSortCols 对齐),
    // 其他 Service 仍是本地 HashSet<string>。统一用 IReadOnlyCollection 接住两边。
    private static IReadOnlyCollection<string> GetAllowedSortCols(Type serviceType, string fieldName)
    {
        var field = serviceType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IReadOnlyCollection<string>)field.GetValue(null)!;
    }

    // ===== OrdersService =====

    [Fact]
    public void OrdersService_Whitelist_ContainsExpectedFields()
    {
        var allowed = GetAllowedSortCols(typeof(OrdersService), "_ordersAllowedSortCols");

        Assert.Contains(nameof(Orders.Id), allowed);
        Assert.Contains(nameof(Orders.OrderNumber), allowed);
        Assert.Contains(nameof(Orders.CreatedTime), allowed);
        Assert.Contains(nameof(Orders.CreatedBy), allowed);
    }

    [Theory]
    [InlineData("Id; DROP TABLE Users;--")]   // 经典注入
    [InlineData("Password")]                  // 表外字段(永远不能允许)
    [InlineData("DepartmentId_InnerJoin")]    // 任何非白名单字段
    [InlineData("")]                          // 空
    public void OrdersService_Whitelist_RejectsInvalidSortName(string malicious)
    {
        var allowed = GetAllowedSortCols(typeof(OrdersService), "_ordersAllowedSortCols");
        Assert.DoesNotContain(malicious, allowed);
    }

    // ===== UsersService =====

    [Fact]
    public void UsersService_Whitelist_ContainsExpectedFields()
    {
        var allowed = GetAllowedSortCols(typeof(UsersService), "_usersAllowedSortCols");
        Assert.Contains(nameof(Users.Id), allowed);
        Assert.Contains(nameof(Users.WorkerNumber), allowed);
        Assert.Contains(nameof(Users.GID), allowed);
        Assert.Contains(nameof(Users.CreatedTime), allowed);
    }

    // ===== RolesService =====

    [Fact]
    public void RolesService_Whitelist_ContainsExpectedFields()
    {
        var allowed = GetAllowedSortCols(typeof(RolesService), "_rolesAllowedSortCols");
        Assert.Contains(nameof(Roles.Id), allowed);
        Assert.Contains(nameof(Roles.Name), allowed);
        Assert.Contains(nameof(Roles.NormalizedName), allowed);
    }

    // ===== DepartmentsService =====

    [Fact]
    public void DepartmentsService_Whitelist_ContainsExpectedFields()
    {
        var allowed = GetAllowedSortCols(typeof(DepartmentsService), "_deptsAllowedSortCols");
        Assert.Contains(nameof(Departments.Id), allowed);
        Assert.Contains(nameof(Departments.Name), allowed);
        Assert.Contains(nameof(Departments.Code), allowed);
        Assert.Contains(nameof(Departments.IsActive), allowed);
    }

    [Theory]
    [InlineData("1=1")]
    [InlineData("Name; SELECT 1; --")]
    public void Whitelists_ContainNoSqlFragments(string malicious)
    {
        // 一次性扫描 4 个白名单,确保没意外把 SQL 片段包含进去
        var all = new[]
        {
            GetAllowedSortCols(typeof(OrdersService),     "_ordersAllowedSortCols"),
            GetAllowedSortCols(typeof(UsersService),      "_usersAllowedSortCols"),
            GetAllowedSortCols(typeof(RolesService),      "_rolesAllowedSortCols"),
            GetAllowedSortCols(typeof(DepartmentsService),"_deptsAllowedSortCols"),
        };
        foreach (var allowed in all)
        {
            Assert.DoesNotContain(malicious, allowed);
            Assert.All(allowed, col =>
            {
                Assert.False(col.Contains(';'));
                Assert.False(col.Contains('='));
                Assert.False(col.Contains(' '));
            });
        }
    }
}
