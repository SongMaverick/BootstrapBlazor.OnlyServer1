using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.Identity;
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
        private readonly CurrentUserAccessor _currentUser;
        private readonly ILogger<RolesService> _logger;

        public const string AdminRoleName = Models.Identity.RoleNames.Admin;

        // (2026-07-07 Review #H3) OrderBy(string) 字符串拼接 SortName 防注入
        private static readonly HashSet<string> _rolesAllowedSortCols =
            new(StringComparer.OrdinalIgnoreCase)
            {
                nameof(Roles.Id),
                nameof(Roles.Name),
                nameof(Roles.NormalizedName),
            };

        public RolesService(
            ISqlSugarClient db,
            UserManager<Users> userManager,
            CurrentUserAccessor currentUser,
            ILogger<RolesService> logger)
        {
            _db = db;
            _userManager = userManager;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ============================================================
        // 角色列表查询
        // ============================================================

        /// <summary>管理列表:含 用户数 / 权限数 字段,支持搜索/排序/分页。</summary>
        public Task<PagedResult<Roles>> QueryAsync(PagedQuery options)
        {
            try
            {
                var query = _db.Queryable<Roles>();

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(r => r.Name.Contains(options.SearchText));
                }

                if (!string.IsNullOrEmpty(options.SortName) && _rolesAllowedSortCols.Contains(options.SortName))
                {
                    query = options.SortOrder == SortDirection.Asc
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

                return Task.FromResult(new PagedResult<Roles>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.FilterCount > 0,
                    IsSorted = !string.IsNullOrEmpty(options.SortName),
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.HasSearchModel
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying roles");
                return Task.FromResult(new PagedResult<Roles>());
            }
        }

        /// <summary>
        /// 所有角色(供 /user-roles 下拉)。
        /// (2026-07-08 Review #L-5):与 GetAllActiveAsync 命名差异说明——
        /// Roles 模型没有 IsDeleted/IsActive 列,IdentityRole&lt;int&gt; 也没有。
        /// 所以"All"就是字面意义的"全部",而 DepartmentsService.GetAllActiveAsync 是"活跃+未删"。
        /// 不要因为对齐命名而改名,会让方法看起来像有过滤实际没有。
        /// </summary>
        public Task<List<Roles>> GetAllAsync()
        {
            return _db.Queryable<Roles>().OrderBy(r => r.Id).ToListAsync();
        }

        /// <summary>下拉选项 Id → Name 映射(框架无关,UI 层再映射为 SelectedItem)。</summary>
        public async Task<List<SelectItem<int>>> GetSelectItemsAsync()
        {
            var roles = await GetAllAsync();
            return roles.Select(r => new SelectItem<int>(r.Id, r.Name ?? string.Empty)).ToList();
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
                .Where((u, ur) => ur.RoleId == roleId && !u.IsDeleted)
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
        // 批量查询(消除 N+1:list 页每行单独 count/查询改成一次 IN)
        // ============================================================

        /// <summary>
        /// 批量取多个角色的用户数(返回 roleId → 用户数)。
        /// 没有用户的角色也返回 0(保证调用方按 roleId 直接索引不缺值)。
        /// </summary>
        public async Task<Dictionary<int, int>> GetUserCountsByRoleIdsAsync(IEnumerable<int> roleIds)
        {
            var ids = roleIds.Distinct().ToList();
            var dict = ids.ToDictionary(id => id, _ => 0);
            if (ids.Count == 0) return dict;

            var rows = await _db.Queryable<UserRolesRow>()
                .Where(ur => ids.Contains(ur.RoleId))
                .Select(ur => ur.RoleId)
                .ToListAsync();

            foreach (var grp in rows.GroupBy(id => id))
            {
                dict[grp.Key] = grp.Count();
            }
            return dict;
        }

        /// <summary>
        /// 批量取多个角色的权限数(返回 roleId → 权限数)。
        /// 没有权限的角色也返回 0。
        /// </summary>
        public async Task<Dictionary<int, int>> GetPermissionCountsByRoleIdsAsync(IEnumerable<int> roleIds)
        {
            var ids = roleIds.Distinct().ToList();
            var dict = ids.ToDictionary(id => id, _ => 0);
            if (ids.Count == 0) return dict;

            var rows = await _db.Queryable<RolePermissions>()
                .Where(rp => ids.Contains(rp.RoleId))
                .Select(rp => rp.RoleId)
                .ToListAsync();

            foreach (var grp in rows.GroupBy(id => id))
            {
                dict[grp.Key] = grp.Count();
            }
            return dict;
        }

        /// <summary>
        /// 批量取多个用户的角色 Id 列表(返回 userId → 角色 Id 列表)。
        /// 没有角色的用户不返回键(调用方 TryGetValue 即可)。
        /// </summary>
        public async Task<Dictionary<int, List<int>>> GetRoleIdsByUserIdsAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, List<int>>();

            var rows = await _db.Queryable<UserRolesRow>()
                .Where(ur => ids.Contains(ur.UserId))
                .ToListAsync();

            return rows
                .GroupBy(ur => ur.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());
        }

        // ============================================================
        // 角色 CRUD
        // ============================================================

        /// <summary>
        /// 保存角色。权限码通过 add/remove 的 delta 模式应用,不接收全替换。
        /// 返回 (ok, error)。
        ///   - Add:Id==0,NormalizedName 必填且唯一。removePermissionIds 被忽略(尚无现有权限)
        ///   - Update:拒绝改 ADMIN(业务约束:ADMIN 名字固定,通过 /roles 内置 25 个权限)
        ///
        /// (2026-07-08 Review #M9 修复):
        /// 旧签名 `IEnumerable<int> permissionIds` 是"全替换"语义,Razor 编辑角色名时
        /// 传 Array.Empty<int>() → DELETE RolePermissions + INSERT 空集合 → 改名丢全部权限。
        /// 新签名要求调用方显式表达"加哪些 / 删哪些",Service 不再清空任何调用方未声明的权限。
        /// Razor 编辑角色名的场景下,只需传 (null, null),权限完全保留。
        /// </summary>
        public async Task<(bool ok, string? error)> SaveAsync(
            Roles role,
            IEnumerable<int>? addPermissionIds,
            IEnumerable<int>? removePermissionIds,
            ItemChangedType changedType)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 角色 CRUD 是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "RolesService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return (false, err);
            }

            if (string.IsNullOrWhiteSpace(role.Name))
            {
                return (false, "角色名称不能为空");
            }

            role.Name = role.Name.Trim();
            role.NormalizedName = role.Name.ToUpperInvariant();

            try
            {
                // 同名冲突 — 在事务外做(只读)
                var dup = await _db.Queryable<Roles>()
                    .Where(r => r.NormalizedName == role.NormalizedName && r.Id != role.Id)
                    .FirstAsync();
                if (dup != null)
                {
                    return (false, $"角色名 '{role.Name}' 已存在(Id={dup.Id})");
                }

                // ====================================================================
                // (2026-07-07 Review #M6):
                // "保存角色 + 同步权限码" 必须事务,否则 INSERT 失败 → 角色被 DELETE
                // 旧权限、INSERT 又失败 → 角色权限永久丢失。
                // 老代码是 DELETE → INSERT 两步独立,失败后无人值守;
                // 现在 BeginTran() → CommitTran()/RollbackTran()。
                // ====================================================================
                _db.Ado.BeginTran();
                try
                {
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
                            _db.Ado.RollbackTran();
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

                    // 同步权限码(在事务内 — 任一失败则全部回滚)
                    // (2026-07-08 Review #M9):只应用调用方显式声明的 delta,
                    // 不再做"全删 + 全插",避免改名等无关操作误清空权限。
                    await ApplyPermissionDeltaAsync(role.Id, addPermissionIds, removePermissionIds);

                    _db.Ado.CommitTran();
                }
                catch
                {
                    _db.Ado.RollbackTran();
                    throw;
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存角色失败: {Name}", role.Name);
                return (false, "保存失败,请重试");
            }
        }

        // ====================================================================
        // (2026-07-08 Review #M9) 私有 helper:
        // 把"全替换"语义改成"应用 delta" — 调用方显式表达加/删,Service 不擅自清空。
        // 幂等:同一 delta 重复执行结果相同。
        // 必须由 SaveAsync 在事务内调用(不自己开事务 — 沿用调用方的事务边界)。
        // ====================================================================
        private async Task ApplyPermissionDeltaAsync(
            int roleId,
            IEnumerable<int>? addIds,
            IEnumerable<int>? removeIds)
        {
            var add = (addIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            var remove = (removeIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (add.Count == 0 && remove.Count == 0) return;

            // 查当前已存在的权限 Id 集合(用于去重和有效性校验)
            var existing = (await _db.Queryable<RolePermissions>()
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.PermissionId)
                .ToListAsync()).ToHashSet();

            // Add:只插入"当前不存在"的,避免主键冲突
            var toInsert = add.Where(id => !existing.Contains(id)).ToList();
            if (toInsert.Count > 0)
            {
                var rows = toInsert.Select(pid => new RolePermissions { RoleId = roleId, PermissionId = pid }).ToList();
                await _db.Insertable(rows).ExecuteCommandAsync();
                _logger.LogInformation(
                    "角色权限新增(增量): roleId={RoleId} addCount={Count} addedIds={Ids}",
                    roleId, toInsert.Count, string.Join(",", toInsert));
            }

            // Remove:只删除"当前存在"的(避免对不存在的 id 产生空操作噪音日志)
            var toDelete = remove.Where(id => existing.Contains(id)).ToList();
            if (toDelete.Count > 0)
            {
                await _db.Deleteable<RolePermissions>()
                    .Where(rp => rp.RoleId == roleId && toDelete.Contains(rp.PermissionId))
                    .ExecuteCommandAsync();
                _logger.LogInformation(
                    "角色权限移除(增量): roleId={RoleId} removeCount={Count} removedIds={Ids}",
                    roleId, toDelete.Count, string.Join(",", toDelete));
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
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 删除角色是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "RolesService.DeleteAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return (false, err);
            }

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
                return (false, "删除失败,请重试");
            }
        }

        // ============================================================
        // 用户-角色 关联
        // ============================================================

        /// <summary>用 _userManager.AddToRoleAsync(走 Identity 标准管道,SqlSugarUserStore 实现)。</summary>
        public async Task<(bool ok, string? error)> AddUserToRoleAsync(int userId, int roleId)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 给用户加角色是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "RolesService.AddUserToRoleAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return (false, err);
            }

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
                return (false, "操作失败,请查看日志");
            }
        }

        public async Task<(bool ok, string? error)> RemoveUserFromRoleAsync(int userId, int roleId)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 解除角色是高权限操作:仅 Admin(已有的"唯一 ADMIN 保护"在业务层)。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "RolesService.RemoveUserFromRoleAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return (false, err);
            }

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
                return (false, "操作失败,请查看日志");
            }
        }

        /// <summary>覆盖式设置用户的全部角色:先 RemoveFromRolesAsync 清空再 AddToRolesAsync。</summary>
        public async Task<(bool ok, string? error)> SetUserRolesAsync(int userId, IEnumerable<int> roleIds)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 覆盖式设角色是高权限操作:仅 Admin(已有"唯一 ADMIN 保护"在业务层)。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "RolesService.SetUserRolesAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return (false, err);
            }

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
                return (false, "操作失败,请查看日志");
            }
        }
    }
}
