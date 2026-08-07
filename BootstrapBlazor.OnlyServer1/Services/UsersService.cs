using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 用户 Service:封装 UserManagement.razor 的数据访问 + 改密码逻辑。
    /// Scoped 生命周期。密码哈希改由 UserManager(PBKDF2)统一处理。
    /// </summary>
    public class UsersService
    {
        private readonly ISqlSugarClient _db;
        private readonly UserManager<Users> _userManager;
        private readonly IdentityOptions _identityOptions;
        private readonly CurrentUserAccessor _currentUser;
        private readonly ILogger<UsersService> _logger;

        // (2026-07-07 Review #H3) OrderBy(string) 字符串拼接 SortName 防注入
        private static readonly HashSet<string> _usersAllowedSortCols =
            new(StringComparer.OrdinalIgnoreCase)
            {
                nameof(Users.Id),
                nameof(Users.Name),
                nameof(Users.WorkerNumber),
                nameof(Users.Gid),
                nameof(Users.DepartmentId),
                nameof(Users.CreatedTime),
            };

        public UsersService(
            ISqlSugarClient db,
            UserManager<Users> userManager,
            IOptions<IdentityOptions> identityOptions,
            CurrentUserAccessor currentUser,
            ILogger<UsersService> logger)
        {
            _db = db;
            _userManager = userManager;
            _identityOptions = identityOptions.Value;
            _currentUser = currentUser;
            _logger = logger;
        }

        public Task<PagedResult<Users>> QueryAsync(PagedQuery options, bool hideAdminRole = false)
        {
            try
            {
                // LEFT JOIN Departments:DepartmentId 可空,NULL 时仍要显示(部门列空)
                // 过滤软删
                //   - 默认:ADMIN 角色用户也展示(超管在此页面可统一管理所有用户角色分配)
                //   - hideAdminRole=true:排除 ADMIN 角色用户(Operator / Viewer 视角,看不到超管)
                //     唯一超管不可解除 ADMIN 的保护由 RolesService.RemoveUserFromRoleAsync 提供
                var query = _db.Queryable<Users>()
                    .LeftJoin<Departments>((u, d) => u.DepartmentId == d.Id)
                    .Where(u => !u.IsDeleted);

                if (hideAdminRole)
                {
                    // 排除 ADMIN 角色的用户:UserRoles 中关联的 Role.NormalizedName = 'ADMIN'
                    // (2026-08-07) SqlSugar 不支持在 Where lambda 里嵌套 _db.Queryable<>().Any()
                    //   ——编译能过但生成 SQL 不对。改成"先 ToList() 拉出 admin user ids,再 NOT IN"
                    //   ——ADMIN 用户极少(通常 1-2 人),这个额外查询成本可忽略
                    var adminUserIds = _db.Queryable<UserRolesRow>()
                        .InnerJoin<Roles>((ur, r) => ur.RoleId == r.Id)
                        .Where((ur, r) => r.NormalizedName == "ADMIN")
                        .Select(ur => ur.UserId)
                        .Distinct()
                        .ToList();
                    if (adminUserIds.Count > 0)
                    {
                        query = query.Where(u => !adminUserIds.Contains(u.Id));
                    }
                }

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(u => u.Name.Contains(options.SearchText) || u.Gid.Contains(options.SearchText));
                }

                if (!string.IsNullOrEmpty(options.SortName) && _usersAllowedSortCols.Contains(options.SortName))
                {
                    query = options.SortOrder == SortDirection.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderBy(u => u.Id);
                }

                var total = query.Count();
                var items = query
                    .Select<Users>((u, d) => new Users
                    {
                        Id = u.Id,
                        Name = u.Name,
                        WorkerNumber = u.WorkerNumber,
                        Gid = u.Gid,
                        CreatedTime = u.CreatedTime,
                        IsDeleted = u.IsDeleted,
                        UserName = u.UserName,
                        NormalizedUserName = u.NormalizedUserName,
                        Email = u.Email,
                        NormalizedEmail = u.NormalizedEmail,
                        PhoneNumber = u.PhoneNumber,
                        PasswordHash = u.PasswordHash,
                        SecurityStamp = u.SecurityStamp,
                        ConcurrencyStamp = u.ConcurrencyStamp,
                        LockoutEnd = u.LockoutEnd,
                        EmailConfirmed = u.EmailConfirmed,
                        PhoneNumberConfirmed = u.PhoneNumberConfirmed,
                        TwoFactorEnabled = u.TwoFactorEnabled,
                        LockoutEnabled = u.LockoutEnabled,
                        AccessFailedCount = u.AccessFailedCount,
                        DepartmentId = u.DepartmentId,
                        DepartmentName = d.Name
                    })
                    .Skip((options.PageIndex - 1) * options.PageItems)
                    .Take(options.PageItems)
                    .ToList();

                // 二次查角色名(逗号分隔),回填到 RoleNames(IsIgnore,不入库)
                if (items.Count > 0)
                {
                    var userIds = items.Select(u => u.Id).ToList();
                    var roleMaps = _db.Queryable<Roles>()
                        .InnerJoin<UserRolesRow>((r, ur) => r.Id == ur.RoleId)
                        .Where((r, ur) => userIds.Contains(ur.UserId))
                        .Select((r, ur) => new { ur.UserId, r.Name })
                        .ToList();
                    var grouped = roleMaps
                        .GroupBy(x => x.UserId)
                        .ToDictionary(g => g.Key, g => string.Join(",", g.Select(x => x.Name)));
                    foreach (var u in items)
                    {
                        u.RoleNames = grouped.TryGetValue(u.Id, out var n) ? n : "";
                    }
                }

                return Task.FromResult(new PagedResult<Users>
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
                _logger.LogError(ex, "Error querying users");
                return Task.FromResult(new PagedResult<Users>());
            }
        }

        public Task<Users> NewUserAsync()
        {
            return Task.FromResult(new Users
            {
                CreatedTime = DateTime.Now,
                IsDeleted = false,
                DepartmentId = null
            });
        }

        /// <summary>
        /// 保存用户。返回 (ok, error):
        ///   - Add:必填初始密码,Gid 必填且转大写
        ///   - Update:不修改 PasswordHash / CreatedTime / IsDeleted(防止覆盖)
        ///
        /// 授权规则(纵深防御,2026-07-08 Review #H1 补漏):仅 Admin。
        /// 不删这段校验、不删这个注释。Razor 层 [Authorize] 不足 —
        /// 任何能注入 Service 的路径(测试 / 未来 Controller / 健康检查 / 定时任务)
        /// 都能绕过页面授权。
        /// </summary>
        public async Task<(bool ok, string? error)> SaveAsync(Users user, string? rawPassword, ItemChangedType changedType)
        {
            // ====================================================================
            // 纵深防御(2026-07-08 Review #H1 补漏):
            // 上次审查 12 处 Service mutation 加 RequireRole 时漏了本方法 + SoftDeleteAsync,
            // 导致任何人注入 Service 都能改 / 软删任意用户(冒充审计行、创建新账号)。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
            if (!authOk)
            {
                _logger.LogWarning(
                    "UsersService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), authErr);
                return (false, authErr);
            }

            if (string.IsNullOrEmpty(user.Gid))
            {
                return (false, "Gid不能为空");
            }
            user.Gid = user.Gid.ToUpper();
            user.NormalizedUserName = user.WorkerNumber.ToString().ToUpperInvariant();

            try
            {
                if (changedType == ItemChangedType.Add)
                {
                    if (string.IsNullOrEmpty(rawPassword))
                    {
                        return (false, "新增用户时初始密码不能为空");
                    }

                    user.UserName = user.WorkerNumber.ToString();
                    user.CreatedTime = DateTime.Now;
                    user.IsDeleted = false;

                    _logger.LogInformation("Adding new user: {Name}", user.Name);
                    var result = await _userManager.CreateAsync(user, rawPassword);
                    if (!result.Succeeded)
                    {
                        var error = string.Join("; ", result.Errors.Select(e => e.Description));
                        return (false, error);
                    }
                }
                else
                {
                    // 编辑:不改 PasswordHash / CreatedTime / IsDeleted
                    _logger.LogInformation("Updating user: {Name}", user.Name);
                    user.SecurityStamp = Guid.NewGuid().ToString();
                    var result = await _userManager.UpdateAsync(user);
                    if (!result.Succeeded)
                    {
                        var error = string.Join("; ", result.Errors.Select(e => e.Description));
                        return (false, error);
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user");
                return (false, "保存失败,请重试");
            }
        }

        /// <summary>
        /// 软删用户。
        /// 授权规则(纵深防御,2026-07-08 Review #H1 补漏):仅 Admin。
        /// 不删这段校验、不删这个注释。
        /// </summary>
        public async Task<bool> SoftDeleteAsync(IEnumerable<Users> users)
        {
            // ====================================================================
            // 纵深防御(2026-07-08 Review #H1 补漏):
            // 与 SaveAsync 同因:上次审查 12 处修补漏了本方法。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
            if (!authOk)
            {
                _logger.LogWarning(
                    "UsersService.SoftDeleteAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), authErr);
                return false;
            }

            var userList = users.ToList();
            try
            {
                foreach (var user in userList)
                {
                    _logger.LogInformation("Soft-deleting user: {Name}", user.Name);
                    var result = await _userManager.DeleteAsync(user);
                    if (!result.Succeeded)
                    {
                        _logger.LogWarning("Soft-delete user failed: {Name} {Errors}",
                            user.Name, string.Join(";", result.Errors.Select(e => e.Description)));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting users");
                return false;
            }
        }

        /// <summary>
        /// 改密码:由 UserManager.ChangePasswordAsync 处理(PBKDF2 校验旧密码 + 哈希新密码)。
        /// 长度下限从 IdentityOptions.Password.RequiredLength 读取(配置项)。
        /// 授权规则(纵深防御):调用者必须是 userId 本人,或具备 Admin 角色。
        /// 仅依赖调用方 Razor 的 [Authorize] 不够 — 任何能注入 Service 的代码都会绕过。
        /// </summary>
        public async Task<(bool ok, string? error)> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
        {
            // 1) 授权:必须本人,或 Admin
            var callerId = _currentUser.GetUserId();
            var isSelf = callerId > 0 && callerId == userId;
            var isAdmin = _currentUser.IsInAnyRole(RoleNames.Admin);
            if (!isSelf && !isAdmin)
            {
                _logger.LogWarning(
                    "ChangePasswordAsync denied (unauthorized): callerId={Caller} targetId={Target} isAdmin={IsAdmin}",
                    callerId, userId, isAdmin);
                return (false, "无权修改此用户的密码");
            }

            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
            {
                _logger.LogWarning(
                    "ChangePasswordAsync rejected (empty input): callerId={Caller} targetId={Target}",
                    callerId, userId);
                return (false, "旧密码和新密码均不能为空");
            }
            if (newPassword.Length < _identityOptions.Password.RequiredLength)
            {
                _logger.LogWarning(
                    "ChangePasswordAsync rejected (new-password too short, len={Len}, required={Required}): callerId={Caller} targetId={Target}",
                    newPassword.Length, _identityOptions.Password.RequiredLength, callerId, userId);
                return (false, $"新密码长度至少 {_identityOptions.Password.RequiredLength} 位");
            }

            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                {
                    _logger.LogWarning(
                        "ChangePasswordAsync target not found: callerId={Caller} targetId={Target}",
                        callerId, userId);
                    return (false, "用户不存在");
                }

                var result = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);
                if (!result.Succeeded)
                {
                    var error = result.Errors.FirstOrDefault()?.Description ?? "修改失败";
                    var isPasswordIssue = error.Contains("password") || error.Contains("Password");
                    // 旧密码错误很可能是用户真的记错,但高频出现于同一账户可能是撞库
                    _logger.LogWarning(
                        "ChangePasswordAsync failed (identity rejection, passwordIssue={IsPasswordIssue}): callerId={Caller} targetId={Target} errors={Errors}",
                        isPasswordIssue, callerId, userId, string.Join("; ", result.Errors.Select(e => e.Code)));
                    var friendlyError = isPasswordIssue ? "旧密码不正确" : error;
                    return (false, friendlyError);
                }

                // 审计:成功事件留痕(以后排查"是谁何时改了谁的密码")
                _logger.LogInformation(
                    "ChangePasswordAsync succeeded: callerId={Caller} targetId={Target} isSelf={IsSelf} isAdminReset={IsAdminReset}",
                    callerId, userId, isSelf, isAdmin && !isSelf);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChangePasswordAsync failed: userId={Id}", userId);
                return (false, "密码修改失败,请重试");
            }
        }
    }
}