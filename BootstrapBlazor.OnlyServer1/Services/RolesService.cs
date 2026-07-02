using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;
using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 角色 Service。供 /roles 和 /user-roles 两个页面使用。
    /// Scoped 生命周期。涉及:
    ///   - Roles CRUD
    ///   - RolePermissions 关联维护
    ///   - UserRoles 关联维护(通过 _userManager.AddToRoleAsync / RemoveFromRoleAsync)
    /// </summary>
    public class RolesService
    {
        private readonly ISqlSugarClient _db;
        private readonly UserManager<Users> _userManager;
        private readonly ILogger<RolesService> _logger;

        public const string AdminRoleName = "Admin";

        public RolesService(
            ISqlSugarClient db,
            UserManager<Users> userManager,
            ILogger<RolesService> logger)
        {
            _db = db;
            _userManager = userManager;
            _logger = logger;
        }

        // ============================================================
        // 角色列表查询
        // ============================================================

        /// <summary>管理列表:含 用户数 / 权限数 字段,支持搜索/排序/分页。</summary>
        public Task<QueryData<Roles>> QueryAsync(QueryPageOptions options)
        {
            try
            {
                var query = _db.Queryable<Roles>();

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(r => r.Name.Contains(options.SearchText));
                }

                if (!string.IsNullOrEmpty(options.SortName))
                {
                    query = options.SortOrder == SortOrder.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderBy(r => r.Id);
                }

                var total = query.Count();
                var items = query
                    .Select(r => new Roles
                    {
                        Id = r.Id,
                        Name = r.Name,
                        NormalizedName = r.NormalizedName,
                        ConcurrencyStamp = r.ConcurrencyStamp
                    })
                    .Skip((options.PageIndex - 1) * options.PageItems)
                    .Take(options.PageItems)
                    .ToList();

                return Task.FromResult(new QueryData<Roles>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.Filters.Any(),
                    IsSorted = !string.IsNullOrEmpty(options.SortName),
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.SearchModel != null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying roles");
                return Task.FromResult(new QueryData<Roles>());
            }
        }

        /// <summary>所有角色(供 /user-roles 下拉)。</summary>
        public Task<List<Roles>> GetAllAsync()
        {
            return _db.Queryable<Roles>().OrderBy(r => r.Id).ToListAsync();
        }

        /// <summary>下拉选项 Id → Name 映射。</summary>
        public async Task<List<SelectedItem>> GetSelectItemsAsync()
        {
            var roles = await GetAllAsync();
            return roles.Select(r => new SelectedItem(r.Id.ToString(), r.Name)).ToList();
        }

        /// <summary>角色下的用户数。</summary>
        public Task<int> CountUsersInRoleAsync(int roleId)
        {
            return _db.Queryable<UserRolesRow>().Where(ur => ur.RoleId == roleId).CountAsync();
        }

        /// <summary>角色下的权限数。</summary>
        public Task<int> CountPermissionsInRoleAsync(int roleId)
        {
            return _db.Queryable<RolePermissions>().Where(rp => rp.RoleId == roleId).CountAsync();
        }

        /// <summary>角色下的用户列表(含姓名 / 员工号)。</summary>
        public async Task<List<Users>> GetUsersInRoleAsync(int roleId)
        {
            return await _db.Queryable<Users>()
                .InnerJoin<UserRolesRow>((u, ur) => u.Id == ur.UserId)
                .Where((u, ur) => ur.RoleId == roleId && !u.IsDelete)
                .Select((u, ur) => new Users
                {
                    Id = u.Id,
                    Name = u.Name,
                    WorkerNumber = u.WorkerNumber,
                    GID = u.GID
                })
                .ToListAsync();
        }

        /// <summary>用户的所有角色 Id 列表。</summary>
        public async Task<List<int>> GetRoleIdsByUserIdAsync(int userId)
        {
            var rows = await _db.Queryable<UserRolesRow>().Where(ur => ur.UserId == userId).ToListAsync();
            return rows.Select(r => r.RoleId).ToList();
        }

        /// <summary>用户的角色名列表(供显示)。</summary>
        public async Task<List<string>> GetRoleNamesByUserIdAsync(int userId)
        {
            return await _db.Queryable<Roles>()
                .InnerJoin<UserRolesRow>((r, ur) => r.Id == ur.RoleId)
                .Where((r, ur) => ur.UserId == userId)
                .Select((r, ur) => r.Name)
                .ToListAsync();
        }

        // ============================================================
        // 角色 CRUD
        // ============================================================

        /// <summary>
        /// 保存角色 + 权限码集合。返回 (ok, error)。
        ///   - Add:Id==0,NormalizedName 必填且唯一
        ///   - Update:拒绝改 ADMIN(业务约束:ADMIN 名字固定,通过 /roles 内置 25 个权限)
        /// </summary>
        public async Task<(bool ok, string? error)> SaveAsync(
            Roles role,
            IEnumerable<int> permissionIds,
            ItemChangedType changedType)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
            {
                return (false, "角色名称不能为空");
            }

            role.Name = role.Name.Trim();
            role.NormalizedName = role.Name.ToUpperInvariant();

            try
            {
                // 同名冲突
                var dup = await _db.Queryable<Roles>()
                    .Where(r => r.NormalizedName == role.NormalizedName && r.Id != role.Id)
                    .FirstAsync();
                if (dup != null)
                {
                    return (false, $"角色名 '{role.Name}' 已存在(Id={dup.Id})");
                }

                if (changedType == ItemChangedType.Add)
                {
                    role.ConcurrencyStamp = Guid.NewGuid().ToString();
                    var newId = await _db.Insertable(role).ExecuteReturnIdentityAsync();
                    role.Id = newId;
                    _logger.LogInformation("新增角色: {Name}(Id={Id})", role.Name, newId);
                }
                else
                {
                    // 拒绝改 ADMIN 角色名(防误改)
                    var existing = await _db.Queryable<Roles>().Where(r => r.Id == role.Id).FirstAsync();
                    if (existing == null)
                    {
                        return (false, "角色不存在");
                    }
                    if (existing.NormalizedName == AdminRoleName.ToUpperInvariant())
                    {
                        // ADMIN 只更新 ConcurrencyStamp,不允许改 Name
                        existing.ConcurrencyStamp = Guid.NewGuid().ToString();
                        await _db.Updateable(existing)
                            .UpdateColumns(r => r.ConcurrencyStamp)
                            .ExecuteCommandAsync();
                        role.Name = existing.Name;
                        role.NormalizedName = existing.NormalizedName;
                    }
                    else
                    {
                        existing.Name = role.Name;
                        existing.NormalizedName = role.NormalizedName;
                        existing.ConcurrencyStamp = Guid.NewGuid().ToString();
                        await _db.Updateable(existing)
                            .UpdateColumns(r => new { r.Name, r.NormalizedName, r.ConcurrencyStamp })
                            .ExecuteCommandAsync();
                    }
                    _logger.LogInformation("更新角色: {Name}(Id={Id})", role.Name, role.Id);
                }

                // 同步权限码
                var ids = (permissionIds ?? Enumerable.Empty<int>()).Distinct().ToList();
                await _db.Deleteable<RolePermissions>().Where(rp => rp.RoleId == role.Id).ExecuteCommandAsync();
                if (ids.Count > 0)
                {
                    var rows = ids.Select(pid => new RolePermissions { RoleId = role.Id, PermissionId = pid }).ToList();
                    await _db.Insertable(rows).ExecuteCommandAsync();
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存角色失败: {Name}", role.Name);
                return (false, $"保存失败:{ex.Message}");
            }
        }

        /// <summary>
        /// 删除角色。返回 (ok, error)。
        ///   - 拒绝删除 ADMIN
        ///   - 拒绝删除还有用户的角色(先清人)
        ///   - 自动级联清理 RolePermissions
        /// </summary>
        public async Task<(bool ok, string? error)> DeleteAsync(IEnumerable<Roles> roles)
        {
            var list = roles.ToList();
            try
            {
                foreach (var role in list)
                {
                    var existing = await _db.Queryable<Roles>().Where(r => r.Id == role.Id).FirstAsync();
                    if (existing == null) continue;

                    if (existing.NormalizedName == AdminRoleName.ToUpperInvariant())
                    {
                        return (false, "ADMIN 角色是系统保留角色,不允许删除");
                    }

                    var userCount = await _db.Queryable<UserRolesRow>().Where(ur => ur.RoleId == role.Id).CountAsync();
                    if (userCount > 0)
                    {
                        return (false, $"角色 '{existing.Name}' 还有 {userCount} 名用户,请先解除关联");
                    }

                    await _db.Deleteable<RolePermissions>().Where(rp => rp.RoleId == role.Id).ExecuteCommandAsync();
                    await _db.Deleteable<Roles>().Where(r => r.Id == role.Id).ExecuteCommandAsync();
                    _logger.LogInformation("删除角色: {Name}(Id={Id})", existing.Name, existing.Id);
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除角色失败");
                return (false, $"删除失败:{ex.Message}");
            }
        }

        // ============================================================
        // 用户-角色 关联
        // ============================================================

        /// <summary>用 _userManager.AddToRoleAsync(走 Identity 标准管道,SqlSugarUserStore 实现)。</summary>
        public async Task<(bool ok, string? error)> AddUserToRoleAsync(int userId, int roleId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null) return (false, "用户不存在");
                var role = await _db.Queryable<Roles>().Where(r => r.Id == roleId).FirstAsync();
                if (role == null) return (false, "角色不存在");

                var result = await _userManager.AddToRoleAsync(user, role.Name);
                if (!result.Succeeded)
                {
                    return (false, string.Join("; ", result.Errors.Select(e => e.Description)));
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddUserToRoleAsync failed: userId={Uid}, roleId={Rid}", userId, roleId);
                return (false, ex.Message);
            }
        }

        public async Task<(bool ok, string? error)> RemoveUserFromRoleAsync(int userId, int roleId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null) return (false, "用户不存在");
                var role = await _db.Queryable<Roles>().Where(r => r.Id == roleId).FirstAsync();
                if (role == null) return (false, "角色不存在");

                // ADMIN 是最后一名超管时,拒绝解除关联(避免锁死系统)
                if (role.NormalizedName == AdminRoleName.ToUpperInvariant())
                {
                    var adminCount = await _db.Queryable<UserRolesRow>()
                        .InnerJoin<Roles>((ur, r) => ur.RoleId == r.Id)
                        .Where((ur, r) => r.NormalizedName == AdminRoleName.ToUpperInvariant())
                        .CountAsync();
                    if (adminCount <= 1)
                    {
                        return (false, "ADMIN 是系统唯一超管,不能解除关联(会锁死系统)");
                    }
                }

                var result = await _userManager.RemoveFromRoleAsync(user, role.Name);
                if (!result.Succeeded)
                {
                    return (false, string.Join("; ", result.Errors.Select(e => e.Description)));
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveUserFromRoleAsync failed: userId={Uid}, roleId={Rid}", userId, roleId);
                return (false, ex.Message);
            }
        }

        /// <summary>覆盖式设置用户的全部角色:先 RemoveFromRolesAsync 清空再 AddToRolesAsync。</summary>
        public async Task<(bool ok, string? error)> SetUserRolesAsync(int userId, IEnumerable<int> roleIds)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null) return (false, "用户不存在");

                var targetIds = roleIds.Distinct().ToList();
                var targetRoles = await _db.Queryable<Roles>().Where(r => targetIds.Contains(r.Id)).ToListAsync();
                var targetNames = targetRoles.Select(r => r.Name).ToArray();

                // ADMIN 保护:若该用户当前唯一 ADMIN 且目标不包含 ADMIN,拒绝
                var currentRoleNames = await _userManager.GetRolesAsync(user);
                if (currentRoleNames.Contains(AdminRoleName) && !targetNames.Contains(AdminRoleName))
                {
                    var adminCount = await _db.Queryable<UserRolesRow>()
                        .InnerJoin<Roles>((ur, r) => ur.RoleId == r.Id)
                        .Where((ur, r) => r.NormalizedName == AdminRoleName.ToUpperInvariant())
                        .CountAsync();
                    if (adminCount <= 1)
                    {
                        return (false, "ADMIN 是系统唯一超管,不能移除该角色(会锁死系统)");
                    }
                }

                // 1) 清空当前角色
                if (currentRoleNames.Count > 0)
                {
                    var rmResult = await _userManager.RemoveFromRolesAsync(user, currentRoleNames);
                    if (!rmResult.Succeeded)
                    {
                        return (false, "清除原角色失败:" + string.Join("; ", rmResult.Errors.Select(e => e.Description)));
                    }
                }

                // 2) 添加新角色
                if (targetNames.Length > 0)
                {
                    var addResult = await _userManager.AddToRolesAsync(user, targetNames);
                    if (!addResult.Succeeded)
                    {
                        return (false, string.Join("; ", addResult.Errors.Select(e => e.Description)));
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetUserRolesAsync failed: userId={Uid}", userId);
                return (false, ex.Message);
            }
        }
    }
}
