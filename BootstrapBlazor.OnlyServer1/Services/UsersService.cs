using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;
using Microsoft.AspNetCore.Identity;
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
        private readonly ILogger<UsersService> _logger;

        public UsersService(
            ISqlSugarClient db,
            UserManager<Users> userManager,
            ILogger<UsersService> logger)
        {
            _db = db;
            _userManager = userManager;
            _logger = logger;
        }

        public Task<QueryData<Users>> QueryAsync(QueryPageOptions options)
        {
            try
            {
                // LEFT JOIN Departments:DepartmentId 可空,NULL 时仍要显示(部门列空)
                // 过滤软删;ADMIN 角色用户也展示(超管在此页面可统一管理所有用户角色分配)
                // 唯一超管不可解除 ADMIN 的保护由 RolesService.RemoveUserFromRoleAsync 提供
                var query = _db.Queryable<Users>()
                    .LeftJoin<Departments>((u, d) => u.DepartmentId == d.Id)
                    .Where(u => !u.IsDelete);

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(u => u.Name.Contains(options.SearchText) || u.GID.Contains(options.SearchText));
                }

                if (!string.IsNullOrEmpty(options.SortName))
                {
                    query = options.SortOrder == SortOrder.Asc
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
                        GID = u.GID,
                        CreateTime = u.CreateTime,
                        IsDelete = u.IsDelete,
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

                return Task.FromResult(new QueryData<Users>
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
                _logger.LogError(ex, "Error querying users");
                return Task.FromResult(new QueryData<Users>());
            }
        }

        public Task<Users> NewUserAsync()
        {
            return Task.FromResult(new Users
            {
                CreateTime = DateTime.Now,
                IsDelete = false,
                DepartmentId = null
            });
        }

        /// <summary>
        /// 保存用户。返回 (ok, error):
        ///   - Add:必填初始密码,GID 必填且转大写
        ///   - Update:不修改 PasswordHash / CreateTime / IsDelete(防止覆盖)
        /// </summary>
        public async Task<(bool ok, string? error)> SaveAsync(Users user, string? rawPassword, ItemChangedType changedType)
        {
            if (string.IsNullOrEmpty(user.GID))
            {
                return (false, "GID不能为空");
            }
            user.GID = user.GID.ToUpper();
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
                    user.CreateTime = DateTime.Now;
                    user.IsDelete = false;

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
                    // 编辑:不改 PasswordHash / CreateTime / IsDelete
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

        public async Task<bool> SoftDeleteAsync(IEnumerable<Users> users)
        {
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
        /// </summary>
        public async Task<(bool ok, string? error)> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
            {
                return (false, "旧密码和新密码均不能为空");
            }
            if (newPassword.Length < 6)
            {
                return (false, "新密码长度至少 6 位");
            }

            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                {
                    return (false, "用户不存在");
                }

                var result = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);
                if (!result.Succeeded)
                {
                    var error = result.Errors.FirstOrDefault()?.Description ?? "修改失败";
                    var friendlyError = error.Contains("password") || error.Contains("Password")
                        ? "旧密码不正确"
                        : error;
                    return (false, friendlyError);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChangePasswordAsync failed: userId={Id}", userId);
                return (false, $"密码修改出错:{ex.Message}");
            }
        }
    }
}