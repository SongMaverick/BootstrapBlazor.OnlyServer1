using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.DrawingNames;

/// <summary>
/// 图名(DrawingNames 表)CRUD。
/// 跟"图号"(C0X 9 表)是两件事:
///   - 图号 = 一次绘图的编号,如 17001
///   - 图名 = 一次绘图的模板描述,如 "ESL-01 变压器外壳"
/// 本 Service 管后者;前者在 C0XQueryService / C0XWriteService。
/// Scoped 生命周期。
/// </summary>
public class DrawingNameService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<DrawingNameService> _logger;

    public DrawingNameService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<DrawingNameService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<List<BootstrapBlazor.OnlyServer1.Models.DrawingNames>> GetAllDrawingNamesAsync()
    {
        return await _db.Queryable<BootstrapBlazor.OnlyServer1.Models.DrawingNames>()
            .Where(dn => !dn.IsDeleted)
            .OrderBy(dn => dn.Id)
            .ToListAsync();
    }

    /// <summary>查询所有图名(包括已删除的),用于"图名列表"管理页面</summary>
    public async Task<List<BootstrapBlazor.OnlyServer1.Models.DrawingNames>> GetAllDrawingNamesIncludingDeletedAsync()
    {
        return await _db.Queryable<BootstrapBlazor.OnlyServer1.Models.DrawingNames>()
            .OrderBy(dn => dn.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 查询所有不重复的 Part 类别(去重 + 排序 + 过滤空值)
    /// 供"添加图名"弹窗下拉框使用,自动跟库里的 Part 同步。
    /// </summary>
    public async Task<List<string>> GetDistinctPartsAsync()
    {
        var list = await _db.Queryable<BootstrapBlazor.OnlyServer1.Models.DrawingNames>()
            .Where(dn => !string.IsNullOrWhiteSpace(dn.Part))
            .Select(dn => dn.Part)
            .Distinct()
            .ToListAsync();

        // SqlSugar 的 Distinct 在 List<string> 上可能不生效,做一遍内存去重兜底
        return list
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 新增图名。
    /// </summary>
    public async Task<int> AddDrawingNameAsync(string componentName, string part)
    {
        // ====================================================================
        // 纵深防御(2026-07-07 Review #H1):
        // 加图名是业务操作:Admin/Operator。
        // 不删这段校验、不删这个注释。
        // ====================================================================
        var (ok, err) = _currentUser.RequireRole(BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Admin,
                                                BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Operator);
        if (!ok)
        {
            _logger.LogWarning(
                "DrawingNameService.AddDrawingNameAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), err);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(componentName) || string.IsNullOrWhiteSpace(part))
        {
            _logger.LogWarning("AddDrawingNameAsync: invalid input, ComponentName='{Name}', Part='{Part}'", componentName, part);
            return 0;
        }

        var currentUserName = _currentUser.GetDisplayName() ?? "System";

        var newName = new BootstrapBlazor.OnlyServer1.Models.DrawingNames
        {
            Part = part,
            ComponentName = componentName.Trim(),
            CreatedBy = currentUserName,
            CreatedTime = DateTime.Now,
            UpdatedBy = currentUserName,
            UpdatedTime = DateTime.Now,
            IsDeleted = false
        };

        try
        {
            var insertedId = await _db.Insertable(newName).ExecuteReturnIdentityAsync();
            _logger.LogInformation("Added drawing name: {Name} (Part={Part}) by {User}", componentName, part, currentUserName);
            return insertedId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddDrawingNameAsync failed: {Name} {Part}", componentName, part);
            return 0;
        }
    }

    /// <summary>
    /// 软删除图名(IsDeleted=true)。
    /// </summary>
    public async Task<bool> DeleteDrawingNameAsync(int id)
    {
        // ====================================================================
        // 纵深防御(2026-07-07 Review #H1):
        // 删图名:Admin/Operator。
        // 不删这段校验、不删这个注释。
        // ====================================================================
        var (ok, err) = _currentUser.RequireRole(BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Admin,
                                                BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Operator);
        if (!ok)
        {
            _logger.LogWarning(
                "DrawingNameService.DeleteDrawingNameAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), err);
            return false;
        }

        try
        {
            string userName = _currentUser.GetDisplayName() ?? "System";

            var result = await _db.Updateable<BootstrapBlazor.OnlyServer1.Models.DrawingNames>()
                .SetColumns(it => new BootstrapBlazor.OnlyServer1.Models.DrawingNames
                {
                    IsDeleted = true,
                    UpdatedBy = userName,
                    UpdatedTime = DateTime.Now
                })
                .Where(it => it.Id == id)
                .ExecuteCommandAsync();

            if (result > 0)
            {
                _logger.LogInformation("Soft-deleted drawing name id={Id} by {User}", id, userName);
            }
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteDrawingNameAsync failed: {Id}", id);
            return false;
        }
    }
}
