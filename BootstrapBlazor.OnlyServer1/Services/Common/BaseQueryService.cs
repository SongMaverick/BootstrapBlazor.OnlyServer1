using System.Linq.Expressions;
using BootstrapBlazor.OnlyServer1.Models;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.Queries;

/// <summary>
/// 通用列表查询基类。
/// 4 个业务 Service 的 QueryAsync 之前各写一份,这里抽公共逻辑:
///   - 软删过滤谓词(子类声明)
///   - 搜索谓词(子类可选 override)
///   - 排序(根据 PagedQuery.SortName + SortDirection,无则按 DefaultSortField desc)
///   - Count + Skip/Take 分页
///   - PagedResult 封装
///
/// 每个子类只需重写 2-3 个小方法即可,全部公共代码集中此处。
/// </summary>
public abstract class BaseQueryService<TEntity> where TEntity : class, new()
{
    protected ISqlSugarClient Db { get; }
    protected ILogger Logger { get; }

    protected BaseQueryService(ISqlSugarClient db, ILogger logger)
    {
        Db = db;
        Logger = logger;
    }

    /// <summary>软删过滤谓词。子类声明该实体如何"未删除"。</summary>
    protected abstract Expression<Func<TEntity, bool>> NotDeletedPredicate();

    /// <summary>
    /// 自定义搜索谓词(子类可 override)。
    /// 默认返回 null —— 某些 Service(如 Roles)不需要支持关键字搜索。
    /// </summary>
    protected virtual Expression<Func<TEntity, bool>>? BuildSearchPredicate(string keyword) => null;

    /// <summary>
    /// 默认排序字段名(SortName 为空或不在白名单时使用)。
    /// (2026-07-09 Review #M10):子类实现 MUST 返回硬编码常量字符串(如 "CreatedTime"、"Id")。
    /// 绝对不允许从用户输入、配置文件或数据库读到的值构造返回值 ——
    /// BaseQueryService.QueryAsync 会直接拼到 OrderBy(string) 里传给 SQL。
    /// 改 abstract 时若值要变,直接修改本方法的返回值,不要做成动态。
    /// </summary>
    protected abstract string DefaultSortField();

    /// <summary>
    /// (2026-07-07 Review #H3):
    /// SqlSugar.OrderBy(string) 是字符串拼接,SortName 来自前端表格列头。
    /// BootstrapBlazor Table 写法上 SortName 是写死的,但任何绕过 Blazor 的入口
    /// (手机 App / Web API / 任务 / 测试代码)都可能注入恶意 SortName。
    /// 子类 override 声明自己允许的字段;不在白名单 → 静默回退 DefaultSortField。
    /// </summary>
    protected virtual IReadOnlyCollection<string> AllowedSortCols => Array.Empty<string>();

    public virtual Task<PagedResult<TEntity>> QueryAsync(PagedQuery options, CancellationToken ct = default)
    {
        try
        {
            var query = Db.Queryable<TEntity>().Where(NotDeletedPredicate());

            var kw = options.SearchText;
            if (!string.IsNullOrEmpty(kw))
            {
                var searchPredicate = BuildSearchPredicate(kw);
                if (searchPredicate != null)
                {
                    query = query.Where(searchPredicate);
                }
            }

            // H3 白名单:不在 AllowedSortCols 的 SortName 一律忽略,回退默认排序
            var allowed = AllowedSortCols;
            var isSortAllowed = !string.IsNullOrEmpty(options.SortName)
                && allowed.Contains(options.SortName);
            if (isSortAllowed)
            {
                query = options.SortOrder == SortDirection.Asc
                    ? query.OrderBy($"{options.SortName} asc")
                    : query.OrderBy($"{options.SortName} desc");
            }
            else
            {
                // (2026-07-09 Review #M10):此处走 SqlSugar.OrderBy(string) 字符串拼接。
                // 安全前提:DefaultSortField() 必须返回硬编码常量(见该方法注释)。
                // 禁止改成从 options / 用户输入 / DB 读到的值。
                query = query.OrderBy($"{DefaultSortField()} desc");
            }

            var total = query.Count();
            var items = query
                .Skip((options.PageIndex - 1) * options.PageItems)
                .Take(options.PageItems)
                .ToList();

            return Task.FromResult(new PagedResult<TEntity>
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
            Logger.LogError(ex, "Error querying {Entity}", typeof(TEntity).Name);
            return Task.FromResult(new PagedResult<TEntity>());
        }
    }
}
