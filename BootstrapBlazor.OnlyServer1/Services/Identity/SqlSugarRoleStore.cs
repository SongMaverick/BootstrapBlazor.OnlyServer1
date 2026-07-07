using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// ASP.NET Core Identity 角色存储的 SqlSugar 实现。
    /// 角色 UI 暂不实现,但 Store 已就绪以便后续接入。
    /// </summary>
    public class SqlSugarRoleStore :
        IRoleStore<Roles>,
        IRoleClaimStore<Roles>,
        IDisposable
    {
        private readonly ISqlSugarClient _db;
        private bool _disposed;

        public SqlSugarRoleStore(ISqlSugarClient db)
        {
            _db = db;
        }

        public void Dispose() => _disposed = true;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqlSugarRoleStore));
        }

        // ============================================================
        // IRoleStore<Roles>
        // ============================================================
        public async Task<IdentityResult> CreateAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                if (string.IsNullOrEmpty(role.ConcurrencyStamp))
                    role.ConcurrencyStamp = Guid.NewGuid().ToString();
                var newId = await _db.Insertable(role).ExecuteReturnIdentityAsync(cancellationToken);
                role.Id = newId;
                return IdentityResult.Success;
            }
            catch (Exception ex)
            {
                return IdentityResult.Failed(new IdentityError { Description = "操作失败,请查看日志" });
            }
        }

        public async Task<IdentityResult> UpdateAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                role.ConcurrencyStamp = Guid.NewGuid().ToString();
                var rows = await _db.Updateable(role).ExecuteCommandAsync(cancellationToken);
                return rows > 0
                    ? IdentityResult.Success
                    : IdentityResult.Failed(new IdentityError { Description = "UPDATE 返回 0 行" });
            }
            catch (Exception ex)
            {
                return IdentityResult.Failed(new IdentityError { Description = "操作失败,请查看日志" });
            }
        }

        public async Task<IdentityResult> DeleteAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                var rows = await _db.Deleteable<Roles>().Where(r => r.Id == role.Id).ExecuteCommandAsync(cancellationToken);
                return rows > 0
                    ? IdentityResult.Success
                    : IdentityResult.Failed(new IdentityError { Description = "DELETE 返回 0 行" });
            }
            catch (Exception ex)
            {
                return IdentityResult.Failed(new IdentityError { Description = "操作失败,请查看日志" });
            }
        }

        public async Task<string> GetRoleIdAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(role.Id.ToString());
        }

        public async Task<string?> GetRoleNameAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(role.Name);
        }

        public async Task SetRoleNameAsync(Roles role, string? roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            role.Name = roleName;
            role.NormalizedName = roleName?.ToUpperInvariant();
            await Task.CompletedTask;
        }

        public async Task<string?> GetNormalizedRoleNameAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await Task.FromResult(role.NormalizedName);
        }

        public async Task SetNormalizedRoleNameAsync(Roles role, string? normalizedName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            role.NormalizedName = normalizedName;
            await Task.CompletedTask;
        }

        public async Task<Roles?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!int.TryParse(roleId, out var id)) return null;
            return await _db.Queryable<Roles>().Where(r => r.Id == id).FirstAsync(cancellationToken);
        }

        public async Task<Roles?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await _db.Queryable<Roles>()
                .Where(r => r.NormalizedName == normalizedRoleName)
                .FirstAsync(cancellationToken);
        }

        // ============================================================
        // IRoleClaimStore<Roles>
        // ============================================================
        public async Task<IList<Claim>> GetClaimsAsync(Roles role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var rows = await _db.Queryable<RoleClaimRow>()
                .Where(c => c.RoleId == role.Id)
                .ToListAsync(cancellationToken);
            return rows
                .Where(r => !string.IsNullOrEmpty(r.ClaimType))
                .Select(r => new Claim(r.ClaimType!, r.ClaimValue ?? ""))
                .ToList();
        }

        public async Task AddClaimAsync(Roles role, Claim claim, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _db.Insertable(new RoleClaimRow
            {
                RoleId = role.Id,
                ClaimType = claim.Type,
                ClaimValue = claim.Value
            }).ExecuteCommandAsync(cancellationToken);
        }

        public async Task RemoveClaimAsync(Roles role, Claim claim, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _db.Deleteable<RoleClaimRow>()
                .Where(c => c.RoleId == role.Id && c.ClaimType == claim.Type && c.ClaimValue == claim.Value)
                .ExecuteCommandAsync(cancellationToken);
        }

        [SugarTable("RoleClaims")]
        private class RoleClaimRow
        {
            [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
            public int Id { get; set; }
            public int RoleId { get; set; }
            public string? ClaimType { get; set; }
            public string? ClaimValue { get; set; }
        }
    }
}