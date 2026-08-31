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
/// (2026-08-17 重构)「订单项目信息」Service:OrderInfo 表的读写(订单管理业务域 1/3)。
///
/// 列表以 Orders 为主体 LEFT JOIN OrderInfo —— 尚未填写项目信息的订单也要出现在列表里,
/// 所以不能继承 BaseQueryService(它只支持单表 Queryable&lt;T&gt;)。
/// 这里照之前 OrderMDDetailService 的 LeftJoin + 显式 Select 投影写法手写查询。
///
/// 明细行不单独新增/删除订单:新增订单请走 /orders。本页的"保存"是 upsert
/// (InfoId == 0 → INSERT,否则 UPDATE)。订单本身完全不受影响。
///
/// Scoped 生命周期,与其他业务 Service 一致。
/// </summary>
public class OrderInfoService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<OrderInfoService> _logger;

    // (2026-07-07 Review #H3) OrderBy(string) 字符串拼接 SortName 防注入。
    // 值必须是下面 Select 投影里出现过的列名;不在集合内的 SortName 静默忽略,回退默认排序。
    private static readonly HashSet<string> _allowedSortCols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(OrderInfoRow.OrderId),
            nameof(OrderInfoRow.OrderNumber),
            nameof(OrderInfoRow.Power),
            nameof(OrderInfoRow.RatedVoltage),
            nameof(OrderInfoRow.ProjectName),
            nameof(OrderInfoRow.MarketType),
            nameof(OrderInfoRow.UnitCount),
        };

    public OrderInfoService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<OrderInfoService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ===== 查询 =====

    /// <summary>
    /// 分页查询订单项目信息列表。Orders 为主表,LEFT JOIN 未删除的 OrderInfo。
    /// </summary>
    public Task<PagedResult<OrderInfoRow>> QueryAsync(PagedQuery options)
    {
        try
        {
            // 注意 join 条件里带上 !i.IsDeleted:软删的项目信息不能被 join 出来。
            var query = _db.Queryable<Orders>()
                .LeftJoin<OrderInfo>((o, i) => o.Id == i.OrderId && !i.IsDeleted)
                .Where(o => !o.IsDeleted);

            var kw = options.SearchText;
            if (!string.IsNullOrEmpty(kw))
            {
                query = query.Where((o, i) =>
                    o.OrderNumber.ToString().Contains(kw)
                    || (i.ProjectName != null && i.ProjectName.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(options.SortName) && _allowedSortCols.Contains(options.SortName))
            {
                query = options.SortOrder == SortDirection.Asc
                    ? query.OrderBy($"{options.SortName} asc")
                    : query.OrderBy($"{options.SortName} desc");
            }
            else
            {
                // 默认:订单号倒序(新订单在前),与 Excel 的排产习惯一致
                query = query.OrderBy((o, i) => o.OrderNumber, OrderByType.Desc);
            }

            var total = query.Count();
            var items = query
                .Select((o, i) => new OrderInfoRow
                {
                    InfoId = i.Id,
                    OrderId = o.Id,
                    ProjectName = i.ProjectName,
                    MarketType = i.MarketType,
                    UnitCount = i.UnitCount,
                    OrderNumber = o.OrderNumber,
                    Power = o.Power,
                    RatedVoltage = o.RatedVoltage,
                    IsReactor = o.IsReactor,
                })
                .Skip((options.PageIndex - 1) * options.PageItems)
                .Take(options.PageItems)
                .ToList();

            // 没有 OrderInfo 行时 UnitCount 会被 join 成 0,展示上按 1 台处理
            foreach (var item in items)
            {
                if (item.InfoId == 0 && item.UnitCount == 0) item.UnitCount = 1;
            }

            return Task.FromResult(new PagedResult<OrderInfoRow>
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
            _logger.LogError(ex, "Error querying OrderInfo");
            return Task.FromResult(new PagedResult<OrderInfoRow>());
        }
    }

    /// <summary>
    /// 按订单号取单条项目信息行。给主从布局(/order-management)右侧用:
    /// 列表已确认订单存在,所以走 Orders LEFT JOIN OrderInfo;
    /// 若该订单尚无 OrderInfo,InfoId=0,业务字段为默认值,保存时按 upsert 处理。
    /// 订单不存在或被删除时返回 null。
    /// </summary>
    public Task<OrderInfoRow?> GetByOrderIdAsync(int orderId)
    {
        if (orderId <= 0) return Task.FromResult<OrderInfoRow?>(null);

        try
        {
            var row = _db.Queryable<Orders>()
                .LeftJoin<OrderInfo>((o, i) => o.Id == i.OrderId && !i.IsDeleted)
                .Where(o => o.Id == orderId && !o.IsDeleted)
                .Select((o, i) => new OrderInfoRow
                {
                    InfoId = i.Id,
                    OrderId = o.Id,
                    ProjectName = i.ProjectName,
                    MarketType = i.MarketType,
                    UnitCount = i.UnitCount,
                    OrderNumber = o.OrderNumber,
                    Power = o.Power,
                    RatedVoltage = o.RatedVoltage,
                    IsReactor = o.IsReactor,
                })
                .First();

            // 没有 OrderInfo 行时 UnitCount 会被 join 成 0,展示上按 1 台处理
            if (row != null && row.InfoId == 0 && row.UnitCount == 0)
            {
                row.UnitCount = 1;
            }
            return Task.FromResult<OrderInfoRow?>(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OrderInfo by orderId {OrderId}", orderId);
            return Task.FromResult<OrderInfoRow?>(null);
        }
    }

    // ===== 保存(upsert) =====

    /// <summary>
    /// 保存订单项目信息。返回 (ok, error):ok=false 时 error 是用户可读的中文消息。
    /// InfoId == 0 → INSERT 一条新项目信息;否则 UPDATE 现有项目信息。
    /// </summary>
    public async Task<(bool ok, string? error)> SaveAsync(OrderInfoRow row)
    {
        // ====================================================================
        // 纵深防御(2026-07-07 Review #H1):
        // Razor 层 [Authorize] 只挡外面;Service 必须自己也挡。
        // 防:新 Controller / 任务调度 / 测试代码直接调 Service 绕过 Razor。
        // 不删这段校验、不删这个注释。
        // 规则:Admin 与 Operator 均可维护订单项目信息。
        // ====================================================================
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
        if (!authOk)
        {
            _logger.LogWarning(
                "OrderInfoService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        if (row.OrderId <= 0)
        {
            return (false, "订单不存在,无法保存项目信息");
        }

        var (validOk, validErr) = Validate(row);
        if (!validOk) return (false, validErr);

        var operatorName = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
        var now = DateTime.Now;

        try
        {
            // 订单必须存在且未删除
            var orderExists = await _db.Queryable<Orders>()
                .Where(o => o.Id == row.OrderId && !o.IsDeleted)
                .AnyAsync();
            if (!orderExists)
            {
                return (false, "订单不存在或已被删除,请刷新页面后重试");
            }

            if (row.InfoId <= 0)
            {
                // 并发兜底:另一个会话可能刚给这个订单建了项目信息。
                // 真正的唯一性由 IX_OrderInfo_OrderId (WHERE IsDeleted=0) 保证。
                var existing = await _db.Queryable<OrderInfo>()
                    .Where(i => i.OrderId == row.OrderId && !i.IsDeleted)
                    .FirstAsync();
                if (existing != null)
                {
                    return (false, "该订单的项目信息已被他人创建,请刷新页面后重试");
                }

                var entity = new OrderInfo
                {
                    OrderId = row.OrderId,
                    ProjectName = row.ProjectName,
                    MarketType = row.MarketType,
                    UnitCount = row.UnitCount,
                    CreatedBy = operatorName,
                    CreatedTime = now,
                    UpdatedBy = operatorName,
                    UpdatedTime = now,
                    IsDeleted = false,
                };

                try
                {
                    row.InfoId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
                }
                catch (Exception ex) when (IsDuplicateOrderInfoError(ex))
                {
                    _logger.LogWarning("Duplicate OrderId on OrderInfo INSERT: {OrderId}", row.OrderId);
                    return (false, "该订单的项目信息已存在,请刷新页面后重试");
                }

                _logger.LogInformation("OrderInfo inserted: orderId={OrderId} infoId={InfoId}",
                    row.OrderId, row.InfoId);
            }
            else
            {
                var entity = await _db.Queryable<OrderInfo>()
                    .Where(i => i.Id == row.InfoId && !i.IsDeleted)
                    .FirstAsync();
                if (entity == null)
                {
                    return (false, "项目信息不存在或已被删除,请刷新页面后重试");
                }

                entity.ProjectName = row.ProjectName;
                entity.MarketType = row.MarketType;
                entity.UnitCount = row.UnitCount;
                entity.UpdatedBy = operatorName;
                entity.UpdatedTime = now;

                await _db.Updateable(entity).ExecuteCommandAsync();
                _logger.LogInformation("OrderInfo updated: orderId={OrderId} infoId={InfoId}",
                    row.OrderId, row.InfoId);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OrderInfo: orderId={OrderId}", row.OrderId);
            return (false, "保存失败,请重试");
        }
    }

    // ===== 内部工具 =====

    /// <summary>业务校验。放在 Service 而不是只靠 UI,理由同上面的纵深防御注释。</summary>
    private static (bool ok, string? error) Validate(OrderInfoRow row)
    {
        if (row.UnitCount <= 0)
        {
            return (false, "台数必须大于 0");
        }
        if (!string.IsNullOrEmpty(row.MarketType) && row.MarketType.Length > 20)
        {
            return (false, "国内/出口字段过长");
        }
        return (true, null);
    }

    /// <summary>
    /// SQL Server 唯一索引冲突检测。做法与 OrdersService.IsDuplicateOrderNumberError 一致:
    /// 先看错误号 2627 / 2601,再用索引名和英文关键字兜底(防消息被本地化后号码匹配失效)。
    /// </summary>
    private static bool IsDuplicateOrderInfoError(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("2627") || msg.Contains("2601")) return true;
        if (msg.Contains("IX_OrderInfo_OrderId", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("unique key", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
