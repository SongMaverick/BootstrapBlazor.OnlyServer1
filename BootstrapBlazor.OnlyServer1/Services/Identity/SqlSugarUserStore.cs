using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// ASP.NET Core Identity 用户存储的 SqlSugar 实现。
    /// 不依赖 EF Core,直接读写 Users / UserRoles / UserClaims 表。
    /// 实现了完整 Identity 接口集,使 AddIdentityCore 走标准管道,
    /// 不再需要自定义 ClaimsPrincipalFactory。
    /// </summary>
    public class SqlSugarUserStore :
        IUserStore<Users>,
        IUserPasswordStore<Users>,
        IUserSecurityStampStore<Users>,
        IUserLockoutStore<Users>,
        IUserEmailStore<Users>,
        IUserClaimStore<Users>,
        IUserLoginStore<Users>,
        IUserTwoFactorStore<Users>,
        IUserPhoneNumberStore<Users>,
        IUserRoleStore<Users>,
        IQueryableUserStore<Users>,
        IDisposable
    {
        private readonly ISqlSugarClient _db;

        public SqlSugarUserStore(ISqlSugarClient db)
        {
            _db = db;
        }

        // ============================================================
        // IQueryableUserStore
        // ============================================================
        // SqlSugar 的 ISugarQueryable<T> 在 v5.1.4 不实现 System.Linq.IQueryable<T>,
        // 这里只能把数据先拉到内存再 AsQueryable() 返回 — 每次属性访问都触发 ToList(),
        // 用户表大时是隐患。当前用法上 Identity 框架主要走 FindByNameAsync 等精确查询,
        // 不频繁走 Users 属性,实测可接受。如果未来 Users 用户量上千 / 万级,改为:
        //   1) 自定义 IQueryable<TUser> 适配器把 Expression 翻译给 SqlSugar;或
        //   2) 把 IQueryableUserStore 拆出 SqlSugarUserStore,只暴露精确查询方法。
        public IQueryable<Users> Users
        {
            get
            {
                var list = _db.Queryable<Users>().Where(u => !u.IsDeleted).ToList();
                return list.AsQueryable();
            }
        }

        // ============================================================
        // 辅助:Disposed 标记
        // ============================================================
        private bool _disposed;

        public void Dispose() => _disposed = true;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqlSugarUserStore));
        }

        // ============================================================
        // IUserStore<Users>
        // ============================================================
        public async Task<string> GetUserIdAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.Id.ToString());
        }

        public async Task<string?> GetUserNameAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.UserName);
        }

        public async Task SetUserNameAsync(Users user, string? userName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.UserName = userName;
            user.NormalizedUserName = userName?.ToUpperInvariant();
            await Task.CompletedTask;
        }

        public async Task<string?> GetNormalizedUserNameAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.NormalizedUserName);
        }

        public async Task SetNormalizedUserNameAsync(Users user, string? normalizedName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.NormalizedUserName = normalizedName;
            await Task.CompletedTask;
        }

        public async Task<IdentityResult> CreateAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                if (string.IsNullOrEmpty(user.SecurityStamp))
                    user.SecurityStamp = Guid.NewGuid().ToString();
                if (string.IsNullOrEmpty(user.ConcurrencyStamp))
                    user.ConcurrencyStamp = Guid.NewGuid().ToString();
                if (string.IsNullOrEmpty(user.NormalizedUserName) && !string.IsNullOrEmpty(user.UserName))
                    user.NormalizedUserName = user.UserName.ToUpperInvariant();

                var newId = await _db.Insertable(user).ExecuteReturnIdentityAsync(cancellationToken);
                user.Id = newId;
                return IdentityResult.Success;
            }
            catch (Exception ex)
            {
                return IdentityResult.Failed(new IdentityError { Description = "操作失败,请查看日志" });
            }
        }

        public async Task<IdentityResult> UpdateAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                // 每次更新刷新 ConcurrencyStamp,防止脏写
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                var rows = await _db.Updateable(user)
                    .IgnoreColumns(u => new { u.IsDeleted, u.CreatedTime })
                    .ExecuteCommandAsync(cancellationToken);
                return rows > 0
                    ? IdentityResult.Success
                    : IdentityResult.Failed(new IdentityError { Description = "UPDATE 返回 0 行" });
            }
            catch (Exception ex)
            {
                return IdentityResult.Failed(new IdentityError { Description = "操作失败,请查看日志" });
            }
        }

        public async Task<IdentityResult> DeleteAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                user.IsDeleted = true;
                var rows = await _db.Updateable<Users>()
                    .UpdateColumns(u => new { u.IsDeleted, u.ConcurrencyStamp })
                    .Where(u => u.Id == user.Id)
                    .ExecuteCommandAsync(cancellationToken);
                return rows > 0
                    ? IdentityResult.Success
                    : IdentityResult.Failed(new IdentityError { Description = "软删除失败" });
            }
            catch (Exception ex)
            {
                return IdentityResult.Failed(new IdentityError { Description = "操作失败,请查看日志" });
            }
        }

        public async Task<Users?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!int.TryParse(userId, out var id)) return null;
            return await _db.Queryable<Users>()
                .Where(u => u.Id == id && !u.IsDeleted)
                .FirstAsync(cancellationToken);
        }

        public async Task<Users?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await _db.Queryable<Users>()
                .Where(u => u.NormalizedUserName == normalizedUserName && !u.IsDeleted)
                .FirstAsync(cancellationToken);
        }

        // ============================================================
        // IUserPasswordStore<Users>
        // ============================================================
        public async Task SetPasswordHashAsync(Users user, string? passwordHash, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.PasswordHash = passwordHash;
            await Task.CompletedTask;
        }

        public async Task<string?> GetPasswordHashAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.PasswordHash);
        }

        public async Task<bool> HasPasswordAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
        }

        // ============================================================
        // IUserSecurityStampStore<Users>
        // ============================================================
        public async Task SetSecurityStampAsync(Users user, string stamp, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.SecurityStamp = stamp;
            await Task.CompletedTask;
        }

        public async Task<string?> GetSecurityStampAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.SecurityStamp);
        }

        // ============================================================
        // IUserLockoutStore<Users>
        // ============================================================
        public async Task<DateTimeOffset?> GetLockoutEndDateAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return user.LockoutEnd;
        }

        public async Task SetLockoutEndDateAsync(Users user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.LockoutEnd = lockoutEnd;
            await Task.CompletedTask;
        }

        public async Task<int> IncrementAccessFailedCountAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.AccessFailedCount++;
            await _db.Updateable<Users>()
                .SetColumns(u => new Users { AccessFailedCount = user.AccessFailedCount })
                .Where(u => u.Id == user.Id)
                .ExecuteCommandAsync(cancellationToken);
            return user.AccessFailedCount;
        }

        public async Task ResetAccessFailedCountAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.AccessFailedCount = 0;
            await _db.Updateable<Users>()
                .SetColumns(u => new Users { AccessFailedCount = 0 })
                .Where(u => u.Id == user.Id)
                .ExecuteCommandAsync(cancellationToken);
        }

        public async Task<int> GetAccessFailedCountAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.AccessFailedCount);
        }

        public async Task<bool> GetLockoutEnabledAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.LockoutEnabled);
        }

        public async Task SetLockoutEnabledAsync(Users user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.LockoutEnabled = enabled;
            await Task.CompletedTask;
        }

        // ============================================================
        // IUserEmailStore<Users>
        // ============================================================
        public async Task SetEmailAsync(Users user, string? email, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.Email = email;
            user.NormalizedEmail = email?.ToUpperInvariant();
            await Task.CompletedTask;
        }

        public async Task<string?> GetEmailAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.Email);
        }

        public async Task<bool> GetEmailConfirmedAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.EmailConfirmed);
        }

        public async Task SetEmailConfirmedAsync(Users user, bool confirmed, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.EmailConfirmed = confirmed;
            await Task.CompletedTask;
        }

        public async Task<Users?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await _db.Queryable<Users>()
                .Where(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted)
                .FirstAsync(cancellationToken);
        }

        public async Task<string?> GetNormalizedEmailAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.NormalizedEmail);
        }

        public async Task SetNormalizedEmailAsync(Users user, string? normalizedEmail, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.NormalizedEmail = normalizedEmail;
            await Task.CompletedTask;
        }

        // ============================================================
        // IUserClaimStore<Users>
        // ============================================================
        public async Task<IList<Claim>> GetClaimsAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var rows = await _db.Queryable<UserClaimRow>()
                .Where(c => c.UserId == user.Id)
                .ToListAsync(cancellationToken);
            return rows
                .Where(r => !string.IsNullOrEmpty(r.ClaimType))
                .Select(r => new Claim(r.ClaimType!, r.ClaimValue ?? ""))
                .ToList();
        }

        public async Task AddClaimsAsync(Users user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var rows = claims.Select(c => new UserClaimRow
            {
                UserId = user.Id,
                ClaimType = c.Type,
                ClaimValue = c.Value
            }).ToList();
            if (rows.Count > 0)
                await _db.Insertable(rows).ExecuteCommandAsync(cancellationToken);
        }

        public async Task ReplaceClaimAsync(Users user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _db.Updateable<UserClaimRow>()
                .SetColumns(c => new UserClaimRow { ClaimType = newClaim.Type, ClaimValue = newClaim.Value })
                .Where(c => c.UserId == user.Id && c.ClaimType == claim.Type && c.ClaimValue == claim.Value)
                .ExecuteCommandAsync(cancellationToken);
        }

        public async Task RemoveClaimsAsync(Users user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            foreach (var claim in claims)
            {
                await _db.Deleteable<UserClaimRow>()
                    .Where(c => c.UserId == user.Id && c.ClaimType == claim.Type && c.ClaimValue == claim.Value)
                    .ExecuteCommandAsync(cancellationToken);
            }
        }

        public async Task<IList<Users>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var userIds = await _db.Queryable<UserClaimRow>()
                .Where(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)
                .Select(c => c.UserId)
                .ToListAsync(cancellationToken);
            if (userIds.Count == 0) return new List<Users>();
            return await _db.Queryable<Users>()
                .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
                .ToListAsync();
        }

        // ============================================================
        // IUserLoginStore<Users>(预留,支持第三方登录扩展)
        // ============================================================
        public async Task AddLoginAsync(Users user, UserLoginInfo login, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            // 当前无 UserLogins 表,如需支持第三方登录请添加表结构
            await Task.CompletedTask;
        }

        public async Task RemoveLoginAsync(Users user, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await Task.CompletedTask;
        }

        public async Task<IList<UserLoginInfo>> GetLoginsAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(new List<UserLoginInfo>());
        }

        public async Task<Users?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult<Users?>(null);
        }

        // ============================================================
        // IUserTwoFactorStore<Users>
        // ============================================================
        public async Task SetTwoFactorEnabledAsync(Users user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.TwoFactorEnabled = enabled;
            await Task.CompletedTask;
        }

        public async Task<bool> GetTwoFactorEnabledAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.TwoFactorEnabled);
        }

        // ============================================================
        // IUserPhoneNumberStore<Users>
        // ============================================================
        public async Task SetPhoneNumberAsync(Users user, string? phoneNumber, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.PhoneNumber = phoneNumber;
            await Task.CompletedTask;
        }

        public async Task<string?> GetPhoneNumberAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.PhoneNumber);
        }

        public async Task<bool> GetPhoneNumberConfirmedAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(user.PhoneNumberConfirmed);
        }

        public async Task SetPhoneNumberConfirmedAsync(Users user, bool confirmed, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            user.PhoneNumberConfirmed = confirmed;
            await Task.CompletedTask;
        }

        // ============================================================
        // IUserRoleStore<Users>
        // ============================================================
        public async Task AddToRoleAsync(Users user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var normalized = roleName.ToUpperInvariant();
            var role = await _db.Queryable<Roles>()
                .Where(r => r.NormalizedName == normalized)
                .FirstAsync(cancellationToken);
            if (role == null)
                throw new InvalidOperationException($"Role '{roleName}' not found");

            var exists = await _db.Queryable<UserRoleRow>()
                .Where(ur => ur.UserId == user.Id && ur.RoleId == role.Id)
                .AnyAsync(cancellationToken);
            if (exists) return;

            await _db.Insertable(new UserRoleRow { UserId = user.Id, RoleId = role.Id })
                .ExecuteCommandAsync(cancellationToken);
        }

        public async Task RemoveFromRoleAsync(Users user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var normalized = roleName.ToUpperInvariant();
            var role = await _db.Queryable<Roles>()
                .Where(r => r.NormalizedName == normalized)
                .FirstAsync(cancellationToken);
            if (role == null) return;

            await _db.Deleteable<UserRoleRow>()
                .Where(ur => ur.UserId == user.Id && ur.RoleId == role.Id)
                .ExecuteCommandAsync(cancellationToken);
        }

        public async Task<IList<string>> GetRolesAsync(Users user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await _db.Queryable<UserRoleRow>()
                .InnerJoin<Roles>((ur, r) => ur.RoleId == r.Id)
                .Where((ur, r) => ur.UserId == user.Id)
                .Select((ur, r) => r.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> IsInRoleAsync(Users user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var normalized = roleName.ToUpperInvariant();
            return await _db.Queryable<UserRoleRow>()
                .InnerJoin<Roles>((ur, r) => ur.RoleId == r.Id)
                .Where((ur, r) => ur.UserId == user.Id && r.NormalizedName == normalized)
                .AnyAsync(cancellationToken);
        }

        public async Task<IList<Users>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var normalized = roleName.ToUpperInvariant();
            var userIds = await _db.Queryable<UserRoleRow>()
                .InnerJoin<Roles>((ur, r) => ur.RoleId == r.Id)
                .Where((ur, r) => r.NormalizedName == normalized)
                .Select((ur, r) => ur.UserId)
                .ToListAsync(cancellationToken);
            if (userIds.Count == 0) return new List<Users>();
            return await _db.Queryable<Users>()
                .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
                .ToListAsync();
        }

        // ============================================================
        // 轻量映射实体(避免引入 Models 全字段)
        // ============================================================
        [SugarTable("UserClaims")]
        private class UserClaimRow
        {
            [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
            public int Id { get; set; }
            public int UserId { get; set; }
            public string? ClaimType { get; set; }
            public string? ClaimValue { get; set; }
        }

        [SugarTable("UserRoles")]
        private class UserRoleRow
        {
            [SugarColumn(IsPrimaryKey = true)]
            public int UserId { get; set; }
            [SugarColumn(IsPrimaryKey = true)]
            public int RoleId { get; set; }
        }
    }
}