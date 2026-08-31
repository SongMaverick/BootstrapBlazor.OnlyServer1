using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services;

/// <summary>
/// (2026-08-15) 开关厂家 Service。
///
/// 范围最小:只负责 SwitchManufacturers 表的 CRUD + 下拉项。
/// 创建/更新/删除 走 Admin 角色校验(纵深防御,沿用项目惯例)。
/// </summary>
public class SwitchManufacturerService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<SwitchManufacturerService> _logger;

    public SwitchManufacturerService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<SwitchManufacturerService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>取所有未删除、且启用的厂家(按 Name 升序)。</summary>
    public Task<List<SwitchManufacturer>> GetAllAsync()
    {
        return _db.Queryable<SwitchManufacturer>()
            .Where(m => !m.IsDeleted && m.IsActive)
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    /// <summary>取所有未删除的厂家(含停用项)—— 给管理页用。</summary>
    public Task<List<SwitchManufacturer>> GetAllIncludingInactiveAsync()
    {
        return _db.Queryable<SwitchManufacturer>()
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
    /// 新建厂家。Name 必填且 Trim 后非空;同名(IsDeleted=0)拒绝。
    /// 失败时 error 是用户可读的中文消息。
    /// </summary>
    public async Task<(bool ok, string? error, int? id)> CreateAsync(string name)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "SwitchManufacturerService.CreateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr, null);
        }

        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return (false, "厂家名称不能为空", null);
        if (trimmed.Length > 100) return (false, $"厂家名称过长({trimmed.Length}/100 字符)", null);

        var normalized = trimmed.ToUpperInvariant();

        try
        {
            var existing = await _db.Queryable<SwitchManufacturer>()
                .Where(m => m.NormalizedName == normalized && !m.IsDeleted)
                .FirstAsync();
            if (existing != null) return (false, $"厂家「{trimmed}」已存在", null);

            var entity = new SwitchManufacturer
            {
                Name = trimmed,
                NormalizedName = normalized,
                IsActive = true,
                CreatedTime = DateTime.Now,
                IsDeleted = false,
            };
            var newId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
            _logger.LogInformation("SwitchManufacturer created: id={Id} name={Name}", newId, trimmed);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating SwitchManufacturer name={Name}", trimmed);
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
                "SwitchManufacturerService.UpdateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return (false, "厂家名称不能为空");
        if (trimmed.Length > 100) return (false, $"厂家名称过长({trimmed.Length}/100 字符)");

        var normalized = trimmed.ToUpperInvariant();

        try
        {
            var entity = await _db.Queryable<SwitchManufacturer>()
                .Where(m => m.Id == id && !m.IsDeleted)
                .FirstAsync();
            if (entity == null) return (false, "厂家不存在或已被删除");

            var dup = await _db.Queryable<SwitchManufacturer>()
                .Where(m => m.NormalizedName == normalized && !m.IsDeleted && m.Id != id)
                .FirstAsync();
            if (dup != null) return (false, $"厂家「{trimmed}」已被其他条目占用");

            entity.Name = trimmed;
            entity.NormalizedName = normalized;
            await _db.Updateable(entity).ExecuteCommandAsync();
            _logger.LogInformation("SwitchManufacturer renamed: id={Id} newName={Name}", id, trimmed);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating SwitchManufacturer id={Id}", id);
            return (false, "保存失败,请重试");
        }
    }

    /// <summary>软删厂家。已存在的应用引用应在外层先处理。</summary>
    public async Task<(bool ok, string? error)> DeleteAsync(int id)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "SwitchManufacturerService.DeleteAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        try
        {
            var rows = await _db.Updateable<SwitchManufacturer>()
                .SetColumns(m => new SwitchManufacturer { IsDeleted = true })
                .Where(m => m.Id == id && !m.IsDeleted)
                .ExecuteCommandAsync();
            if (rows == 0) return (false, "厂家不存在或已被删除");
            _logger.LogInformation("SwitchManufacturer soft-deleted: id={Id}", id);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting SwitchManufacturer id={Id}", id);
            return (false, "删除失败,请重试");
        }
    }
}