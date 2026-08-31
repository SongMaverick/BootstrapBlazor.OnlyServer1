using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.ViewModels;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.OrderManagement;

/// <summary>
/// (2026-08-17 重构)「订单一次二次人员时间」Service:OrderDesignPhases 表的读写(订单管理业务域 2/3)。
///
/// 列表以 Orders 为主体 LEFT JOIN OrderDesignPhases LEFT JOIN Users x2。
/// "PhasesId == 0 → INSERT,否则 UPDATE" 的 upsert 模式。
///
/// Scoped 生命周期,与其他业务 Service 一致。
/// </summary>
public class OrderDesignPhasesService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<OrderDesignPhasesService> _logger;

    private static readonly HashSet<string> _allowedSortCols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(OrderDesignPhasesRow.OrderId),
            nameof(OrderDesignPhasesRow.OrderNumber),
            nameof(OrderDesignPhasesRow.Power),
            nameof(OrderDesignPhasesRow.RatedVoltage),
            nameof(OrderDesignPhasesRow.Phase1SubmitDate),
            nameof(OrderDesignPhasesRow.Phase1ConfirmDate),
            nameof(OrderDesignPhasesRow.Phase2SubmitDate),
            nameof(OrderDesignPhasesRow.Phase2ConfirmDate),
        };

    public OrderDesignPhasesService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<OrderDesignPhasesService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ===== 查询 =====

    /// <summary>
    /// 分页查询订单阶段列表。Orders 为主表,LEFT JOIN 未删除的 OrderDesignPhases
    /// 以及两次 LEFT JOIN Users 取一/二次负责人姓名。
    /// </summary>
    public Task<PagedResult<OrderDesignPhasesRow>> QueryAsync(PagedQuery options)
    {
        try
        {
            var query = _db.Queryable<Orders>()
                .LeftJoin<OrderDesignPhases>((o, p) => o.Id == p.OrderId && !p.IsDeleted)
                .LeftJoin<Users>((o, p, u1) => p.Phase1EngineerId == u1.Id)
                .LeftJoin<Users>((o, p, u1, u2) => p.Phase2EngineerId == u2.Id)
                .Where(o => !o.IsDeleted);

            var kw = options.SearchText;
            if (!string.IsNullOrEmpty(kw))
            {
                query = query.Where((o, p, u1, u2) =>
                    o.OrderNumber.ToString().Contains(kw)
                    || (u1.Name != null && u1.Name.Contains(kw))
                    || (u2.Name != null && u2.Name.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(options.SortName) && _allowedSortCols.Contains(options.SortName))
            {
                query = options.SortOrder == SortDirection.Asc
                    ? query.OrderBy($"{options.SortName} asc")
                    : query.OrderBy($"{options.SortName} desc");
            }
            else
            {
                query = query.OrderBy((o, p) => o.OrderNumber, OrderByType.Desc);
            }

            var total = query.Count();
            var items = query
                .Select((o, p, u1, u2) => new OrderDesignPhasesRow
                {
                    PhasesId = p.Id,
                    OrderId = o.Id,
                    Phase1EngineerId = p.Phase1EngineerId,
                    Phase1SubmitDate = p.Phase1SubmitDate,
                    Phase1ConfirmDate = p.Phase1ConfirmDate,
                    Phase2EngineerId = p.Phase2EngineerId,
                    Phase2SubmitDate = p.Phase2SubmitDate,
                    Phase2ConfirmDate = p.Phase2ConfirmDate,
                    OrderNumber = o.OrderNumber,
                    Power = o.Power,
                    RatedVoltage = o.RatedVoltage,
                    IsReactor = o.IsReactor,
                    Phase1EngineerName = u1.Name,
                    Phase2EngineerName = u2.Name,
                })
                .Skip((options.PageIndex - 1) * options.PageItems)
                .Take(options.PageItems)
                .ToList();

            return Task.FromResult(new PagedResult<OrderDesignPhasesRow>
            {
                Items = items,
                TotalCount = total,
                IsFiltered = options.FilterCount > 0,
                IsSorted = !string.IsNullOrEmpty(options.SortName),
                IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.HasSearchModel,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying OrderDesignPhases");
            return Task.FromResult(new PagedResult<OrderDesignPhasesRow>());
        }
    }

    /// <summary>
    /// 按订单号取单条阶段信息行。给主从布局(/order-management)右侧用:
    /// 列表已确认订单存在,所以走 Orders LEFT JOIN OrderDesignPhases LEFT JOIN Users x2;
    /// 若该订单尚无 OrderDesignPhases,PhasesId=0,业务字段为默认值,保存时按 upsert 处理。
    /// 订单不存在或被删除时返回 null。
    /// </summary>
    public Task<OrderDesignPhasesRow?> GetByOrderIdAsync(int orderId)
    {
        if (orderId <= 0) return Task.FromResult<OrderDesignPhasesRow?>(null);

        try
        {
            var row = _db.Queryable<Orders>()
                .LeftJoin<OrderDesignPhases>((o, p) => o.Id == p.OrderId && !p.IsDeleted)
                .LeftJoin<Users>((o, p, u1) => p.Phase1EngineerId == u1.Id)
                .LeftJoin<Users>((o, p, u1, u2) => p.Phase2EngineerId == u2.Id)
                .Where(o => o.Id == orderId && !o.IsDeleted)
                .Select((o, p, u1, u2) => new OrderDesignPhasesRow
                {
                    PhasesId = p.Id,
                    OrderId = o.Id,
                    Phase1EngineerId = p.Phase1EngineerId,
                    Phase1SubmitDate = p.Phase1SubmitDate,
                    Phase1ConfirmDate = p.Phase1ConfirmDate,
                    Phase2EngineerId = p.Phase2EngineerId,
                    Phase2SubmitDate = p.Phase2SubmitDate,
                    Phase2ConfirmDate = p.Phase2ConfirmDate,
                    OrderNumber = o.OrderNumber,
                    Power = o.Power,
                    RatedVoltage = o.RatedVoltage,
                    IsReactor = o.IsReactor,
                    Phase1EngineerName = u1.Name,
                    Phase2EngineerName = u2.Name,
                })
                .First();

            return Task.FromResult<OrderDesignPhasesRow?>(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OrderDesignPhases by orderId {OrderId}", orderId);
            return Task.FromResult<OrderDesignPhasesRow?>(null);
        }
    }

    // ===== 保存(upsert) =====

    public async Task<(bool ok, string? error)> SaveAsync(OrderDesignPhasesRow row)
    {
        // 纵深防御(2026-07-07 Review #H1)
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
        if (!authOk)
        {
            _logger.LogWarning(
                "OrderDesignPhasesService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        if (row.OrderId <= 0)
        {
            return (false, "订单不存在,无法保存阶段信息");
        }

        var (validOk, validErr) = Validate(row);
        if (!validOk) return (false, validErr);

        var operatorName = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
        var now = DateTime.Now;

        try
        {
            var orderExists = await _db.Queryable<Orders>()
                .Where(o => o.Id == row.OrderId && !o.IsDeleted)
                .AnyAsync();
            if (!orderExists)
            {
                return (false, "订单不存在或已被删除,请刷新页面后重试");
            }

            if (row.PhasesId <= 0)
            {
                var existing = await _db.Queryable<OrderDesignPhases>()
                    .Where(p => p.OrderId == row.OrderId && !p.IsDeleted)
                    .FirstAsync();
                if (existing != null)
                {
                    return (false, "该订单的阶段信息已被他人创建,请刷新页面后重试");
                }

                var entity = new OrderDesignPhases
                {
                    OrderId = row.OrderId,
                    Phase1EngineerId = row.Phase1EngineerId,
                    Phase1SubmitDate = row.Phase1SubmitDate,
                    Phase1ConfirmDate = row.Phase1ConfirmDate,
                    Phase2EngineerId = row.Phase2EngineerId,
                    Phase2SubmitDate = row.Phase2SubmitDate,
                    Phase2ConfirmDate = row.Phase2ConfirmDate,
                    CreatedBy = operatorName,
                    CreatedTime = now,
                    UpdatedBy = operatorName,
                    UpdatedTime = now,
                    IsDeleted = false,
                };

                try
                {
                    row.PhasesId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
                }
                catch (Exception ex) when (IsDuplicatePhasesError(ex))
                {
                    _logger.LogWarning("Duplicate OrderId on OrderDesignPhases INSERT: {OrderId}", row.OrderId);
                    return (false, "该订单的阶段信息已存在,请刷新页面后重试");
                }

                _logger.LogInformation("OrderDesignPhases inserted: orderId={OrderId} phasesId={PhasesId}",
                    row.OrderId, row.PhasesId);
            }
            else
            {
                var entity = await _db.Queryable<OrderDesignPhases>()
                    .Where(p => p.Id == row.PhasesId && !p.IsDeleted)
                    .FirstAsync();
                if (entity == null)
                {
                    return (false, "阶段信息不存在或已被删除,请刷新页面后重试");
                }

                entity.Phase1EngineerId = row.Phase1EngineerId;
                entity.Phase1SubmitDate = row.Phase1SubmitDate;
                entity.Phase1ConfirmDate = row.Phase1ConfirmDate;
                entity.Phase2EngineerId = row.Phase2EngineerId;
                entity.Phase2SubmitDate = row.Phase2SubmitDate;
                entity.Phase2ConfirmDate = row.Phase2ConfirmDate;
                entity.UpdatedBy = operatorName;
                entity.UpdatedTime = now;

                await _db.Updateable(entity).ExecuteCommandAsync();
                _logger.LogInformation("OrderDesignPhases updated: orderId={OrderId} phasesId={PhasesId}",
                    row.OrderId, row.PhasesId);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OrderDesignPhases: orderId={OrderId}", row.OrderId);
            return (false, "保存失败,请重试");
        }
    }

    // ===== 内部工具 =====

    /// <summary>
    /// 业务校验:ConfirmDate 不能早于 SubmitDate(有 ConfirmDate 必有 SubmitDate)。
    /// Phase1/Phase2 独立校验。
    /// 状态由日期推断,故不维护独立 Status 字段。
    /// </summary>
    private static (bool ok, string? error) Validate(OrderDesignPhasesRow row)
    {
        var err = ValidatePhase(row.Phase1SubmitDate, row.Phase1ConfirmDate, "一次");
        if (err != null) return (false, err);
        err = ValidatePhase(row.Phase2SubmitDate, row.Phase2ConfirmDate, "二次");
        if (err != null) return (false, err);
        return (true, null);
    }

    private static string? ValidatePhase(DateTime? submit, DateTime? confirm, string phaseLabel)
    {
        if (confirm.HasValue && !submit.HasValue)
        {
            return $"{phaseLabel}设计已有确认日期但缺少提交日期";
        }
        if (confirm.HasValue && submit.HasValue && confirm.Value < submit.Value)
        {
            return $"{phaseLabel}设计确认日期不能早于提交日期";
        }
        return null;
    }

    private static bool IsDuplicatePhasesError(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("2627") || msg.Contains("2601")) return true;
        if (msg.Contains("IX_OrderDesignPhases_OrderId", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("unique key", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
