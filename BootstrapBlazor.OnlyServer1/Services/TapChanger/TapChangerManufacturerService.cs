using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.TapChanger;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.TapChanger;

/// <summary>
/// 有载分接开关厂家 Service(2026-09-01 重建)。
///
/// 范围最小:只负责 TapChangerManufacturers 表的 CRUD + 下拉项。
/// 创建/更新/删除 走 Admin 角色校验(纵深防御,沿用项目惯例)。
/// </summary>
public class TapChangerManufacturerService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<TapChangerManufacturerService> _logger;

    public TapChangerManufacturerService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<TapChangerManufacturerService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>取所有未删除、且启用的厂家(按 Name 升序)。</summary>
    public Task<List<TapChangerManufacturer>> GetAllAsync()
    {
        return _db.Queryable<TapChangerManufacturer>()
            .Where(m => !m.IsDeleted && m.IsActive)
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    /// <summary>取所有未删除的厂家(含停用项)—— 给管理页用。</summary>
    public Task<List<TapChangerManufacturer>> GetAllIncludingInactiveAsync()
    {
        return _db.Queryable<TapChangerManufacturer>()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.IsActive, OrderByType.Desc)
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    /// <summary>BootstrapBlazor Select 用的下拉项:Id → Name。仅含 IsActive 的。</summary>
    public async Task<List<SelectedItem>> GetSelectItemsAsync()
    {
        var items = await GetAllAsync();
        return items
            .Select(m => new SelectedItem(m.Id.ToString(), m.Name))
            .ToList();
    }

    /// <summary>
    /// 新建厂家。Name 必填且 Trim 后非空;大小写无关去重(IsDeleted=0)。
    /// 失败时 error 是用户可读的中文消息。
    /// </summary>
    public async Task<(bool ok, string? error, int? id)> CreateAsync(string name)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerManufacturerService.CreateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr, null);
        }

        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return (false, "厂家名称不能为空", null);
        if (trimmed.Length > 100) return (false, $"厂家名称过长({trimmed.Length}/100 字符)", null);

        var normalized = trimmed.ToUpperInvariant();

        try
        {
            var existing = await _db.Queryable<TapChangerManufacturer>()
                .Where(m => m.NormalizedName == normalized && !m.IsDeleted)
                .FirstAsync();
            if (existing != null) return (false, $"厂家「{trimmed}」已存在", null);

            var now = DateTime.Now;
            var userId = _currentUser.GetDisplayName() ?? "System";
            var entity = new TapChangerManufacturer
            {
                Name = trimmed,
                NormalizedName = normalized,
                IsActive = true,
                CreatedBy = userId,
                CreatedTime = now,
                UpdatedBy = userId,
                UpdatedTime = now,
                IsDeleted = false,
            };
            var newId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
            _logger.LogInformation("TapChangerManufacturer created: id={Id} name={Name}", newId, trimmed);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating TapChangerManufacturer name={Name}", trimmed);
            return (false, "保存失败,请重试", null);
        }
    }

    /// <summary>重命名厂家。Id 必须存在且未删除。Name 同样校验唯一。</summary>
    public async Task<(bool ok, string? error)> UpdateAsync(int id, string name)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerManufacturerService.UpdateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return (false, "厂家名称不能为空");
        if (trimmed.Length > 100) return (false, $"厂家名称过长({trimmed.Length}/100 字符)");

        var normalized = trimmed.ToUpperInvariant();

        try
        {
            var entity = await _db.Queryable<TapChangerManufacturer>()
                .Where(m => m.Id == id && !m.IsDeleted)
                .FirstAsync();
            if (entity == null) return (false, "厂家不存在或已被删除");

            var dup = await _db.Queryable<TapChangerManufacturer>()
                .Where(m => m.NormalizedName == normalized && !m.IsDeleted && m.Id != id)
                .FirstAsync();
            if (dup != null) return (false, $"厂家「{trimmed}」已被其他条目占用");

            entity.Name = trimmed;
            entity.NormalizedName = normalized;
            entity.UpdatedBy = _currentUser.GetDisplayName();
            entity.UpdatedTime = DateTime.Now;
            await _db.Updateable(entity).ExecuteCommandAsync();
            _logger.LogInformation("TapChangerManufacturer renamed: id={Id} newName={Name}", id, trimmed);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating TapChangerManufacturer id={Id}", id);
            return (false, "保存失败,请重试");
        }
    }

    /// <summary>启停切换(只翻 IsActive,不改 NormalizedName)。</summary>
    public async Task<(bool ok, string? error)> SetActiveAsync(int id, bool isActive)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerManufacturerService.SetActiveAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        try
        {
            var rows = await _db.Updateable<TapChangerManufacturer>()
                .SetColumns(m => new TapChangerManufacturer
                {
                    IsActive = isActive,
                    UpdatedBy = _currentUser.GetDisplayName(),
                    UpdatedTime = DateTime.Now,
                })
                .Where(m => m.Id == id && !m.IsDeleted)
                .ExecuteCommandAsync();
            if (rows == 0) return (false, "厂家不存在或已被删除");
            _logger.LogInformation("TapChangerManufacturer active toggled: id={Id} isActive={IsActive}", id, isActive);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling TapChangerManufacturer active id={Id}", id);
            return (false, "操作失败,请重试");
        }
    }

    /// <summary>软删厂家。已存在的应用引用应在外层先处理。</summary>
    public async Task<(bool ok, string? error)> DeleteAsync(int id)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerManufacturerService.DeleteAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        try
        {
            var rows = await _db.Updateable<TapChangerManufacturer>()
                .SetColumns(m => new TapChangerManufacturer
                {
                    IsDeleted = true,
                    UpdatedBy = _currentUser.GetDisplayName(),
                    UpdatedTime = DateTime.Now,
                })
                .Where(m => m.Id == id && !m.IsDeleted)
                .ExecuteCommandAsync();
            if (rows == 0) return (false, "厂家不存在或已被删除");
            _logger.LogInformation("TapChangerManufacturer soft-deleted: id={Id}", id);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting TapChangerManufacturer id={Id}", id);
            return (false, "删除失败,请重试");
        }
    }
}
