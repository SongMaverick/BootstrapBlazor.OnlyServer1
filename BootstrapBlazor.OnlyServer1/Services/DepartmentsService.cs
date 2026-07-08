using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 部门 Service。供 DepartmentManagement 列表 / UserManagement 新增弹窗下拉使用。
    /// Scoped 生命周期。仅允许软删(规范:有员工引用时禁止硬删——FK_Users_Departments)。
    /// </summary>
    public class DepartmentsService
    {
        private readonly ISqlSugarClient _db;
        private readonly CurrentUserAccessor _currentUser;
        private readonly ILogger<DepartmentsService> _logger;

        // (2026-07-07 Review #H3) OrderBy(string) 字符串拼接 SortName 防注入
        private static readonly HashSet<string> _deptsAllowedSortCols =
            new(StringComparer.OrdinalIgnoreCase)
            {
                nameof(Departments.Id),
                nameof(Departments.Name),
                nameof(Departments.Code),
                nameof(Departments.IsActive),
                nameof(Departments.CreatedTime),
            };

        public DepartmentsService(ISqlSugarClient db, CurrentUserAccessor currentUser, ILogger<DepartmentsService> logger)
        {
            _db = db;
            _currentUser = currentUser;
            _logger = logger;
        }

        /// <summary>
        /// 下拉用:只返回 IsActive 且未删除的部门。
        /// (2026-07-08 Review #L-5) 改名 GetActiveAsync → GetAllActiveAsync,与
        /// PermissionsService.GetAllActiveAsync 命名对齐(便于"全栈活跃"语义一眼可读)。
        /// </summary>
        public Task<List<Departments>> GetAllActiveAsync()
        {
            return _db.Queryable<Departments>()
                .Where(d => d.IsActive && !d.IsDeleted)
                .OrderBy(d => d.Id)
                .ToListAsync();
        }

        /// <summary>管理列表用:支持 Name/Code 搜索 + 排序 + 分页(包含软删)。</summary>
        public Task<PagedResult<Departments>> QueryAsync(PagedQuery options)
        {
            try
            {
                var query = _db.Queryable<Departments>().Where(d => !d.IsDeleted);

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(d => d.Name.Contains(options.SearchText)
                                           || (d.Code != null && d.Code.Contains(options.SearchText)));
                }

                if (!string.IsNullOrEmpty(options.SortName) && _deptsAllowedSortCols.Contains(options.SortName))
                {
                    query = options.SortOrder == SortDirection.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderBy(d => d.Id);
                }

                var total = query.Count();
                var items = query.Skip((options.PageIndex - 1) * options.PageItems).Take(options.PageItems).ToList();

                return Task.FromResult(new PagedResult<Departments>
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
                _logger.LogError(ex, "Error querying departments");
                return Task.FromResult(new PagedResult<Departments>());
            }
        }

        /// <summary>保存(Id==0 新增,否则 更新)。返回 (ok, error)。</summary>
        public async Task<(bool ok, string? error)> SaveAsync(Departments d)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 部门 CRUD 是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "DepartmentsService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return (false, err);
            }

            if (string.IsNullOrWhiteSpace(d.Name))
            {
                return (false, "部门名称不能为空");
            }
            d.Name = d.Name.Trim();
            d.NormalizedName = d.Name.ToUpperInvariant();

            try
            {
                // 同名冲突检测(忽略自己)
                var dup = await _db.Queryable<Departments>()
                    .Where(x => x.NormalizedName == d.NormalizedName && x.Id != d.Id && !x.IsDeleted)
                    .FirstAsync();
                if (dup != null)
                {
                    return (false, $"部门名 '{d.Name}' 已存在(Id={dup.Id})");
                }

                if (d.Id == 0)
                {
                    d.CreatedTime = DateTime.Now;
                    d.IsDeleted = false;
                    if (!d.IsActive) d.IsActive = true;
                    var newId = await _db.Insertable(d).ExecuteReturnIdentityAsync();
                    d.Id = newId;
                    _logger.LogInformation("新增部门: {Name} (Id={Id})", d.Name, newId);
                }
                else
                {
                    _logger.LogInformation("更新部门: {Name} (Id={Id})", d.Name, d.Id);
                    await _db.Updateable(d)
                        .IgnoreColumns(x => new { x.CreatedTime, x.IsDeleted })
                        .ExecuteCommandAsync();
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存部门失败: {Name}", d.Name);
                return (false, "保存失败,请重试");
            }
        }

        /// <summary>软删:置 IsDeleted=true,IsActive=false。下拉不再出现。</summary>
        public async Task<bool> SoftDeleteAsync(IEnumerable<Departments> depts)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 软删部门是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "DepartmentsService.SoftDeleteAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return false;
            }

            var list = depts.ToList();
            try
            {
                foreach (var d in list)
                {
                    d.IsDeleted = true;
                    d.IsActive = false;
                    _logger.LogInformation("软删部门: {Name}(Id={Id})", d.Name, d.Id);
                    await _db.Updateable(d)
                        .UpdateColumns(x => new { x.IsDeleted, x.IsActive })
                        .Where(x => x.Id == d.Id)
                        .ExecuteCommandAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "软删部门失败");
                return false;
            }
        }
    }
}
