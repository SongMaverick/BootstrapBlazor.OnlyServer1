using System.Globalization;
using System.Text;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.TapChanger;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.TapChanger;

/// <summary>
/// 有载分接开关(Tap-Changer)型号 Service(2026-09-01 引入)。
///
/// 范围最小:CRUD + 下拉项 + 启停。
/// 写操作走 Admin 角色纵深防御。
/// Name 由 Service 在 Save 前按图例格式自动拼装
///   格式:"1XDU I 3204-245-245-06 05 0 D"(列数+型号 用 "X" 连接;相数/电流 用 " " 分隔;
///        YJD/Um/分接选择器 用 "-" 分隔;接线图 4 段用 " " 分隔)。
///   空段会被跳过(避免出现空 token)。
/// </summary>
public class TapChangerModelService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<TapChangerModelService> _logger;

    public TapChangerModelService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<TapChangerModelService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>取所有未删除、且启用的型号,预加载 Manufacturer 名称。按 Name 升序。</summary>
    public async Task<List<TapChangerModel>> GetAllAsync()
    {
        var list = await _db.Queryable<TapChangerModel>()
            .Where(m => !m.IsDeleted && m.IsActive)
            .OrderBy(m => m.Name)
            .ToListAsync();
        return list;
    }

    /// <summary>取所有未删除的型号(含停用项)—— 给管理页用。</summary>
    public async Task<List<TapChangerModel>> GetAllIncludingInactiveAsync()
    {
        var list = await _db.Queryable<TapChangerModel>()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.IsActive, OrderByType.Desc)
            .OrderBy(m => m.Name)
            .ToListAsync();
        return list;
    }

    /// <summary>
    /// 按厂家取启用型号(给"按厂家选型号"的下拉项用)。ManufacturerId 传 0 时返回空集。
    /// </summary>
    public async Task<List<TapChangerModel>> GetByManufacturerAsync(int manufacturerId)
    {
        if (manufacturerId <= 0) return new List<TapChangerModel>();
        return await _db.Queryable<TapChangerModel>()
            .Where(m => m.ManufacturerId == manufacturerId && !m.IsDeleted && m.IsActive)
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    /// <summary>
    /// 新建型号。Service 自动拼装 Name(Name 必须由分量拼出,调用方不要手动设)。
    /// 失败时 error 是用户可读的中文消息。
    /// </summary>
    public async Task<(bool ok, string? error, int? id)> CreateAsync(TapChangerModel input)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerModelService.CreateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr, null);
        }

        if (input.ManufacturerId <= 0) return (false, "请选择关联的分接开关厂家", null);

        // 校验厂家存在且未软删
        var mfrExists = await _db.Queryable<TapChangerManufacturer>()
            .Where(x => x.Id == input.ManufacturerId && !x.IsDeleted)
            .AnyAsync();
        if (!mfrExists) return (false, "关联的分接开关厂家不存在或已被删除", null);

        // 至少要有 1 个非空分量,否则 Name 为空
        var hasAny = input.NumberOfColumns.HasValue
            || !string.IsNullOrWhiteSpace(input.TypeCode)
            || !string.IsNullOrWhiteSpace(input.NumberOfPhases)
            || !string.IsNullOrWhiteSpace(input.RatedCurrentCode)
            || !string.IsNullOrWhiteSpace(input.YjdCode)
            || input.UmKv.HasValue
            || !string.IsNullOrWhiteSpace(input.TapSelectorSize)
            || !string.IsNullOrWhiteSpace(input.BD1)
            || !string.IsNullOrWhiteSpace(input.BD2)
            || !string.IsNullOrWhiteSpace(input.BD3)
            || !string.IsNullOrWhiteSpace(input.BD4);
        if (!hasAny) return (false, "型号代码各分量为空,无法拼装 Name,请至少填写一项", null);

        var name = BuildName(input);
        if (name.Length == 0) return (false, "型号代码(Name)拼装结果为空,请检查输入", null);
        if (name.Length > 200) return (false, $"型号代码过长({name.Length}/200 字符)", null);

        try
        {
            var existing = await _db.Queryable<TapChangerModel>()
                .Where(m => m.Name == name && !m.IsDeleted)
                .FirstAsync();
            if (existing != null) return (false, $"型号「{name}」已存在", null);

            var now = DateTime.Now;
            var userName = _currentUser.GetDisplayName() ?? "System";
            var entity = new TapChangerModel
            {
                ManufacturerId = input.ManufacturerId,
                Name = name,
                NumberOfColumns = input.NumberOfColumns,
                TypeCode = input.TypeCode?.Trim(),
                NumberOfPhases = input.NumberOfPhases?.Trim(),
                RatedCurrentCode = input.RatedCurrentCode?.Trim(),
                YjdCode = input.YjdCode?.Trim(),
                UmKv = input.UmKv,
                TapSelectorSize = input.TapSelectorSize?.Trim(),
                BD1 = input.BD1?.Trim(),
                BD2 = input.BD2?.Trim(),
                BD3 = input.BD3?.Trim(),
                BD4 = input.BD4?.Trim(),
                IsActive = input.IsActive,
                CreatedBy = userName,
                CreatedTime = now,
                UpdatedBy = userName,
                UpdatedTime = now,
                IsDeleted = false,
            };
            var newId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
            _logger.LogInformation("TapChangerModel created: id={Id} name={Name}", newId, name);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating TapChangerModel name={Name}", name);
            return (false, "保存失败,请重试", null);
        }
    }

    /// <summary>编辑型号。Name 会重新拼装。重名校验时排除自己。</summary>
    public async Task<(bool ok, string? error)> UpdateAsync(TapChangerModel input)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerModelService.UpdateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        if (input.Id <= 0) return (false, "型号 Id 无效");
        if (input.ManufacturerId <= 0) return (false, "请选择关联的分接开关厂家");

        var name = BuildName(input);
        if (name.Length == 0) return (false, "型号代码(Name)拼装结果为空,请检查输入");
        if (name.Length > 200) return (false, $"型号代码过长({name.Length}/200 字符)");

        try
        {
            var entity = await _db.Queryable<TapChangerModel>()
                .Where(m => m.Id == input.Id && !m.IsDeleted)
                .FirstAsync();
            if (entity == null) return (false, "型号不存在或已被删除");

            var mfrExists = await _db.Queryable<TapChangerManufacturer>()
                .Where(x => x.Id == input.ManufacturerId && !x.IsDeleted)
                .AnyAsync();
            if (!mfrExists) return (false, "关联的分接开关厂家不存在或已被删除");

            var dup = await _db.Queryable<TapChangerModel>()
                .Where(m => m.Name == name && !m.IsDeleted && m.Id != input.Id)
                .FirstAsync();
            if (dup != null) return (false, $"型号「{name}」已被其他条目占用");

            entity.ManufacturerId = input.ManufacturerId;
            entity.Name = name;
            entity.NumberOfColumns = input.NumberOfColumns;
            entity.TypeCode = input.TypeCode?.Trim();
            entity.NumberOfPhases = input.NumberOfPhases?.Trim();
            entity.RatedCurrentCode = input.RatedCurrentCode?.Trim();
            entity.YjdCode = input.YjdCode?.Trim();
            entity.UmKv = input.UmKv;
            entity.TapSelectorSize = input.TapSelectorSize?.Trim();
            entity.BD1 = input.BD1?.Trim();
            entity.BD2 = input.BD2?.Trim();
            entity.BD3 = input.BD3?.Trim();
            entity.BD4 = input.BD4?.Trim();
            entity.IsActive = input.IsActive;
            entity.UpdatedBy = _currentUser.GetDisplayName();
            entity.UpdatedTime = DateTime.Now;
            await _db.Updateable(entity).ExecuteCommandAsync();
            _logger.LogInformation("TapChangerModel updated: id={Id} name={Name}", entity.Id, name);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating TapChangerModel id={Id}", input.Id);
            return (false, "保存失败,请重试");
        }
    }

    /// <summary>启停切换(只翻 IsActive,不改其它字段)。</summary>
    public async Task<(bool ok, string? error)> SetActiveAsync(int id, bool isActive)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerModelService.SetActiveAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        try
        {
            var rows = await _db.Updateable<TapChangerModel>()
                .SetColumns(m => new TapChangerModel
                {
                    IsActive = isActive,
                    UpdatedBy = _currentUser.GetDisplayName(),
                    UpdatedTime = DateTime.Now,
                })
                .Where(m => m.Id == id && !m.IsDeleted)
                .ExecuteCommandAsync();
            if (rows == 0) return (false, "型号不存在或已被删除");
            _logger.LogInformation("TapChangerModel active toggled: id={Id} isActive={IsActive}", id, isActive);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling TapChangerModel active id={Id}", id);
            return (false, "操作失败,请重试");
        }
    }

    /// <summary>软删型号。已存在的应用引用应在外层先处理。</summary>
    public async Task<(bool ok, string? error)> DeleteAsync(int id)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin);
        if (!authOk)
        {
            _logger.LogWarning(
                "TapChangerModelService.DeleteAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        try
        {
            var rows = await _db.Updateable<TapChangerModel>()
                .SetColumns(m => new TapChangerModel
                {
                    IsDeleted = true,
                    UpdatedBy = _currentUser.GetDisplayName(),
                    UpdatedTime = DateTime.Now,
                })
                .Where(m => m.Id == id && !m.IsDeleted)
                .ExecuteCommandAsync();
            if (rows == 0) return (false, "型号不存在或已被删除");
            _logger.LogInformation("TapChangerModel soft-deleted: id={Id}", id);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting TapChangerModel id={Id}", id);
            return (false, "删除失败,请重试");
        }
    }

    /// <summary>
    /// 按图中编码规则拼装 Name。
    /// 格式:
    ///   [NumberOfColumns]X[TypeCode]   (例: 1XDU)
    ///   [ ]  [NumberOfPhases]           (例: " I")
    ///   [ ]  [RatedCurrentCode]         (例: " 3204")
    ///   [-YjdCode -UmKv -TapSelectorSize](例: "-245-245")
    ///   [-BD1 BD2 BD3 BD4]              (例: "-06 05 0 D")
    /// 空段会被跳过,避免出现 "X..." / " 3204"(前导 X / 双空格)之类的视觉噪音。
    /// </summary>
    public static string BuildName(TapChangerModel m)
    {
        var sb = new StringBuilder();

        // Section 1+2:[NumberOfColumns] X [TypeCode]
        if (m.NumberOfColumns.HasValue && !string.IsNullOrWhiteSpace(m.TypeCode))
        {
            sb.Append(m.NumberOfColumns.Value).Append('X').Append(m.TypeCode.Trim());
        }
        else if (m.NumberOfColumns.HasValue)
        {
            sb.Append(m.NumberOfColumns.Value);
        }
        else if (!string.IsNullOrWhiteSpace(m.TypeCode))
        {
            sb.Append(m.TypeCode.Trim());
        }

        // Section 3:[NumberOfPhases] [RatedCurrentCode]
        var phase = m.NumberOfPhases?.Trim();
        var rated = m.RatedCurrentCode?.Trim();
        if (!string.IsNullOrEmpty(phase) && !string.IsNullOrEmpty(rated))
        {
            sb.Append(' ').Append(phase).Append(' ').Append(rated);
        }
        else if (!string.IsNullOrEmpty(phase))
        {
            sb.Append(' ').Append(phase);
        }
        else if (!string.IsNullOrEmpty(rated))
        {
            sb.Append(' ').Append(rated);
        }

        // Section 4:-[YjdCode] -[UmKv] -[TapSelectorSize]
        var s4Parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.YjdCode)) s4Parts.Add(m.YjdCode.Trim());
        if (m.UmKv.HasValue) s4Parts.Add(m.UmKv.Value.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(m.TapSelectorSize)) s4Parts.Add(m.TapSelectorSize.Trim());
        if (s4Parts.Count > 0)
        {
            sb.Append('-').Append(string.Join('-', s4Parts));
        }

        // Section 5:-[BD1] [BD2] [BD3] [BD4]
        var bdParts = new[] { m.BD1, m.BD2, m.BD3, m.BD4 }
            .Select(s => s?.Trim() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        if (bdParts.Count > 0)
        {
            sb.Append('-').Append(string.Join(' ', bdParts));
        }

        return sb.ToString();
    }
}
