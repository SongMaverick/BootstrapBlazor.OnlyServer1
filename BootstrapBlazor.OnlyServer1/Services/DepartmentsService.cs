using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;
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
        private readonly ILogger<DepartmentsService> _logger;

        public DepartmentsService(ISqlSugarClient db, ILogger<DepartmentsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>下拉用:只返回 IsActive 且未删除的部门。</summary>
        public Task<List<Departments>> GetActiveAsync()
        {
            return _db.Queryable<Departments>()
                .Where(d => d.IsActive && !d.IsDelete)
                .OrderBy(d => d.Id)
                .ToListAsync();
        }

        /// <summary>管理列表用:支持 Name/Code 搜索 + 排序 + 分页(包含软删)。</summary>
        public Task<QueryData<Departments>> QueryAsync(QueryPageOptions options)
        {
            try
            {
                var query = _db.Queryable<Departments>().Where(d => !d.IsDelete);

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(d => d.Name.Contains(options.SearchText)
                                           || (d.Code != null && d.Code.Contains(options.SearchText)));
                }

                if (!string.IsNullOrEmpty(options.SortName))
                {
                    query = options.SortOrder == SortOrder.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderBy(d => d.Id);
                }

                var total = query.Count();
                var items = query.Skip((options.PageIndex - 1) * options.PageItems).Take(options.PageItems).ToList();

                return Task.FromResult(new QueryData<Departments>
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
                _logger.LogError(ex, "Error querying departments");
                return Task.FromResult(new QueryData<Departments>());
            }
        }

        /// <summary>保存(Id==0 新增,否则 更新)。返回 (ok, error)。</summary>
        public async Task<(bool ok, string? error)> SaveAsync(Departments d)
        {
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
                    .Where(x => x.NormalizedName == d.NormalizedName && x.Id != d.Id && !x.IsDelete)
                    .FirstAsync();
                if (dup != null)
                {
                    return (false, $"部门名 '{d.Name}' 已存在(Id={dup.Id})");
                }

                if (d.Id == 0)
                {
                    d.CreateTime = DateTime.Now;
                    d.IsDelete = false;
                    if (!d.IsActive) d.IsActive = true;
                    var newId = await _db.Insertable(d).ExecuteReturnIdentityAsync();
                    d.Id = newId;
                    _logger.LogInformation("新增部门: {Name} (Id={Id})", d.Name, newId);
                }
                else
                {
                    _logger.LogInformation("更新部门: {Name} (Id={Id})", d.Name, d.Id);
                    await _db.Updateable(d)
                        .IgnoreColumns(x => new { x.CreateTime, x.IsDelete })
                        .ExecuteCommandAsync();
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存部门失败: {Name}", d.Name);
                return (false, $"保存失败:{ex.Message}");
            }
        }

        /// <summary>软删:置 IsDelete=true,IsActive=false。下拉不再出现。</summary>
        public async Task<bool> SoftDeleteAsync(IEnumerable<Departments> depts)
        {
            var list = depts.ToList();
            try
            {
                foreach (var d in list)
                {
                    d.IsDelete = true;
                    d.IsActive = false;
                    _logger.LogInformation("软删部门: {Name}(Id={Id})", d.Name, d.Id);
                    await _db.Updateable(d)
                        .UpdateColumns(x => new { x.IsDelete, x.IsActive })
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
