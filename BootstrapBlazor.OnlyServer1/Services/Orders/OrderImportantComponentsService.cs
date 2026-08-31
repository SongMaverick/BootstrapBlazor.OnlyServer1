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
/// (2026-08-17 重构)「订单重要组件」Service:OrderImportantComponents 表的读写(订单管理业务域 3/3)。
///
/// Scoped 生命周期,与其他业务 Service 一致。
/// </summary>
public class OrderImportantComponentsService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<OrderImportantComponentsService> _logger;

    private static readonly HashSet<string> _allowedSortCols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(OrderImportantComponentsRow.OrderId),
            nameof(OrderImportantComponentsRow.OrderNumber),
            nameof(OrderImportantComponentsRow.Power),
            nameof(OrderImportantComponentsRow.RatedVoltage),
            nameof(OrderImportantComponentsRow.SwitchCount),
            nameof(OrderImportantComponentsRow.IsSplitFrame),
            nameof(OrderImportantComponentsRow.IsWeldedTankRim),
        };

    public OrderImportantComponentsService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<OrderImportantComponentsService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ===== 查询 =====

    /// <summary>
    /// 分页查询订单组件特征列表。Orders 为主表,LEFT JOIN 未删除的 OrderImportantComponents。
    /// </summary>
    public Task<PagedResult<OrderImportantComponentsRow>> QueryAsync(PagedQuery options)
    {
        try
        {
            var query = _db.Queryable<Orders>()
                .LeftJoin<OrderImportantComponents>((o, c) => o.Id == c.OrderId && !c.IsDeleted)
                .Where(o => !o.IsDeleted);

            var kw = options.SearchText;
            if (!string.IsNullOrEmpty(kw))
            {
                query = query.Where((o, c) =>
                    o.OrderNumber.ToString().Contains(kw)
                    || (c.TestBushingModel != null && c.TestBushingModel.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(options.SortName) && _allowedSortCols.Contains(options.SortName))
            {
                query = options.SortOrder == SortDirection.Asc
                    ? query.OrderBy($"{options.SortName} asc")
                    : query.OrderBy($"{options.SortName} desc");
            }
            else
            {
                query = query.OrderBy((o, c) => o.OrderNumber, OrderByType.Desc);
            }

            var total = query.Count();
            var items = query
                .Select((o, c) => new OrderImportantComponentsRow
                {
                    ComponentsId = c.Id,
                    OrderId = o.Id,
                    IsSplitFrame = c.IsSplitFrame,
                    IsWeldedTankRim = c.IsWeldedTankRim,
                    SwitchCount = c.SwitchCount,
                    TestBushingModel = c.TestBushingModel,
                    TestBushingQuantity = c.TestBushingQuantity,
                    OrderNumber = o.OrderNumber,
                    Power = o.Power,
                    RatedVoltage = o.RatedVoltage,
                    IsReactor = o.IsReactor,
                })
                .Skip((options.PageIndex - 1) * options.PageItems)
                .Take(options.PageItems)
                .ToList();

            return Task.FromResult(new PagedResult<OrderImportantComponentsRow>
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
            _logger.LogError(ex, "Error querying OrderImportantComponents");
            return Task.FromResult(new PagedResult<OrderImportantComponentsRow>());
        }
    }

    /// <summary>
    /// 按订单号取单条组件特征行。给主从布局(/order-management)右侧用:
    /// 列表已确认订单存在,所以走 Orders LEFT JOIN OrderImportantComponents;
    /// 若该订单尚无 OrderImportantComponents,ComponentsId=0,业务字段为默认值,保存时按 upsert 处理。
    /// 订单不存在或被删除时返回 null。
    /// </summary>
    public Task<OrderImportantComponentsRow?> GetByOrderIdAsync(int orderId)
    {
        if (orderId <= 0) return Task.FromResult<OrderImportantComponentsRow?>(null);

        try
        {
            var row = _db.Queryable<Orders>()
                .LeftJoin<OrderImportantComponents>((o, c) => o.Id == c.OrderId && !c.IsDeleted)
                .Where(o => o.Id == orderId && !o.IsDeleted)
                .Select((o, c) => new OrderImportantComponentsRow
                {
                    ComponentsId = c.Id,
                    OrderId = o.Id,
                    IsSplitFrame = c.IsSplitFrame,
                    IsWeldedTankRim = c.IsWeldedTankRim,
                    SwitchCount = c.SwitchCount,
                    TestBushingModel = c.TestBushingModel,
                    TestBushingQuantity = c.TestBushingQuantity,
                    OrderNumber = o.OrderNumber,
                    Power = o.Power,
                    RatedVoltage = o.RatedVoltage,
                    IsReactor = o.IsReactor,
                })
                .First();

            return Task.FromResult<OrderImportantComponentsRow?>(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OrderImportantComponents by orderId {OrderId}", orderId);
            return Task.FromResult<OrderImportantComponentsRow?>(null);
        }
    }

    // ===== 保存(upsert) =====

    public async Task<(bool ok, string? error)> SaveAsync(OrderImportantComponentsRow row)
    {
        // 纵深防御(2026-07-07 Review #H1)
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
        if (!authOk)
        {
            _logger.LogWarning(
                "OrderImportantComponentsService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        if (row.OrderId <= 0)
        {
            return (false, "订单不存在,无法保存组件信息");
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

            if (row.ComponentsId <= 0)
            {
                var existing = await _db.Queryable<OrderImportantComponents>()
                    .Where(c => c.OrderId == row.OrderId && !c.IsDeleted)
                    .FirstAsync();
                if (existing != null)
                {
                    return (false, "该订单的组件信息已被他人创建,请刷新页面后重试");
                }

                var entity = new OrderImportantComponents
                {
                    OrderId = row.OrderId,
                    IsSplitFrame = row.IsSplitFrame,
                    IsWeldedTankRim = row.IsWeldedTankRim,
                    SwitchCount = row.SwitchCount,
                    TestBushingModel = row.TestBushingModel,
                    TestBushingQuantity = row.TestBushingQuantity,
                    CreatedBy = operatorName,
                    CreatedTime = now,
                    UpdatedBy = operatorName,
                    UpdatedTime = now,
                    IsDeleted = false,
                };

                try
                {
                    row.ComponentsId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
                }
                catch (Exception ex) when (IsDuplicateComponentsError(ex))
                {
                    _logger.LogWarning("Duplicate OrderId on OrderImportantComponents INSERT: {OrderId}", row.OrderId);
                    return (false, "该订单的组件信息已存在,请刷新页面后重试");
                }

                _logger.LogInformation("OrderImportantComponents inserted: orderId={OrderId} componentsId={ComponentsId}",
                    row.OrderId, row.ComponentsId);
            }
            else
            {
                var entity = await _db.Queryable<OrderImportantComponents>()
                    .Where(c => c.Id == row.ComponentsId && !c.IsDeleted)
                    .FirstAsync();
                if (entity == null)
                {
                    return (false, "组件信息不存在或已被删除,请刷新页面后重试");
                }

                entity.IsSplitFrame = row.IsSplitFrame;
                entity.IsWeldedTankRim = row.IsWeldedTankRim;
                entity.SwitchCount = row.SwitchCount;
                entity.TestBushingModel = row.TestBushingModel;
                entity.TestBushingQuantity = row.TestBushingQuantity;
                entity.UpdatedBy = operatorName;
                entity.UpdatedTime = now;

                await _db.Updateable(entity).ExecuteCommandAsync();
                _logger.LogInformation("OrderImportantComponents updated: orderId={OrderId} componentsId={ComponentsId}",
                    row.OrderId, row.ComponentsId);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OrderImportantComponents: orderId={OrderId}", row.OrderId);
            return (false, "保存失败,请重试");
        }
    }

    // ===== 内部工具 =====

    /// <summary>
    /// 业务校验:
    ///   - SwitchCount 不能为负
    ///   - TestBushingQuantity 不能为负
    /// </summary>
    private static (bool ok, string? error) Validate(OrderImportantComponentsRow row)
    {
        if (row.SwitchCount is < 0)
        {
            return (false, "开关数量不能为负数");
        }
        if (row.TestBushingQuantity is < 0)
        {
            return (false, "试验套管数量不能为负数");
        }
        return (true, null);
    }

    private static bool IsDuplicateComponentsError(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("2627") || msg.Contains("2601")) return true;
        if (msg.Contains("IX_OrderImportantComponents_OrderId", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("unique key", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
