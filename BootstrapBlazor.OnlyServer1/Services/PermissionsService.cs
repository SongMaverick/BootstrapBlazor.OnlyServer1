using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 权限码 Service。供:
    ///   - /roles 页面:展示分组权限码供勾选
    ///   - DisplayNameClaimsFactory:读取用户所有角色关联的权限码,写入 Cookie Claim
    ///   - 业务页面:HasPermission() 不直接走本 Service,走 CurrentUserAccessor
    /// </summary>
    public class PermissionsService
    {
        private readonly ISqlSugarClient _db;
        private readonly CurrentUserAccessor _currentUser;
        private readonly ILogger<PermissionsService> _logger;

        public PermissionsService(ISqlSugarClient db, CurrentUserAccessor currentUser, ILogger<PermissionsService> logger)
        {
            _db = db;
            _currentUser = currentUser;
            _logger = logger;
        }

        /// <summary>所有启用的权限码,按 Category / SortOrder 排序。</summary>
        public Task<List<Permissions>> GetAllActiveAsync()
        {
            return _db.Queryable<Permissions>()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Category).OrderBy(p => p.SortOrder)
                .ToListAsync();
        }

        /// <summary>分组形式(供 /roles 编辑弹窗用):Category → 该分类下的权限码列表。</summary>
        public async Task<Dictionary<string, List<Permissions>>> GetGroupedByCategoryAsync()
        {
            var all = await GetAllActiveAsync();
            return all
                .GroupBy(p => p.Category)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>指定角色已关联的权限码 Code 集合。</summary>
        public async Task<HashSet<string>> GetCodesByRoleIdAsync(int roleId)
        {
            var rows = await _db.Queryable<RolePermissions>()
                .InnerJoin<Permissions>((rp, p) => rp.PermissionId == p.Id)
                .Where((rp, p) => rp.RoleId == roleId && p.IsActive)
                .Select((rp, p) => p.Code)
                .ToListAsync();
            return rows.ToHashSet();
        }

        /// <summary>
        /// 指定用户最终拥有的所有权限码 = 用户所有角色 × 权限码的并集。
        /// 供 DisplayNameClaimsFactory 写 Cookie Claim 使用。
        /// </summary>
        public async Task<List<string>> GetCodesByUserIdAsync(int userId)
        {
            return await _db.Queryable<UserRolesRow>()
                .InnerJoin<RolePermissions>((ur, rp) => ur.RoleId == rp.RoleId)
                .InnerJoin<Permissions>((ur, rp, p) => rp.PermissionId == p.Id)
                .Where((ur, rp, p) => ur.UserId == userId && p.IsActive)
                .Select((ur, rp, p) => p.Code)
                .Distinct()
                .ToListAsync();
        }

        /// <summary>给指定角色一次性替换权限关联(全删后全插)。</summary>
        public async Task SetRolePermissionsAsync(int roleId, IEnumerable<int> permissionIds)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 覆盖角色权限是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "PermissionsService.SetRolePermissionsAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return;
            }

            var ids = permissionIds.Distinct().ToList();
            await _db.Deleteable<RolePermissions>().Where(rp => rp.RoleId == roleId).ExecuteCommandAsync();
            if (ids.Count == 0) return;

            var rows = ids.Select(pid => new RolePermissions { RoleId = roleId, PermissionId = pid }).ToList();
            await _db.Insertable(rows).ExecuteCommandAsync();
        }

        /// <summary>完整覆盖某个用户的所有权限码(合并其全部角色关联的权限码)。通常用于诊断/重建。</summary>
        public async Task<List<string>> GetCodesByRoleIdsAsync(IEnumerable<int> roleIds)
        {
            var ids = roleIds.Distinct().ToList();
            if (ids.Count == 0) return new List<string>();
            return await _db.Queryable<RolePermissions>()
                .InnerJoin<Permissions>((rp, p) => rp.PermissionId == p.Id)
                .Where((rp, p) => ids.Contains(rp.RoleId) && p.IsActive)
                .Select((rp, p) => p.Code)
                .Distinct()
                .ToListAsync();
        }
    }

    /// <summary>UserRoles 表的轻量 DTO,供 SqlSugar 多表关联使用(同主项目 SqlSugarUserStore)。</summary>
    [SugarTable("UserRoles")]
    public class UserRolesRow
    {
        [SugarColumn(IsPrimaryKey = true)] public int UserId { get; set; }
        [SugarColumn(IsPrimaryKey = true)] public int RoleId { get; set; }
    }
}
