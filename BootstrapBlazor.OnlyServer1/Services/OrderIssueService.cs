using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services;

/// <summary>
/// (2026-08-13) 订单问题汇总 Service。
///
/// 表:OrderIssues
/// 入口:
///   - /order-management 每行 "添加问题" 弹窗 → CreateAsync(orderId, content)
///   - /order-issues?orderId=X 页面 → QueryByOrderIdAsync(orderId, paged)
///                                     UpdateStatusAsync(issueId, newStatus) (行内下拉框)
///
/// Scoped 生命周期,与其他业务 Service 一致。
/// </summary>
public class OrderIssueService
{
    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<OrderIssueService> _logger;

    public OrderIssueService(
        ISqlSugarClient db,
        CurrentUserAccessor currentUser,
        ILogger<OrderIssueService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ============================================================
    // 列表(按 OrderId)
    // ============================================================

    /// <summary>
    /// 列出指定订单的所有问题(分页,IsDeleted=0)。
    /// 新建的问题排前面(CreatedTime DESC)。
    /// </summary>
    public Task<PagedResult<OrderIssues>> QueryByOrderIdAsync(int orderId, PagedQuery options)
    {
        try
        {
            var query = _db.Queryable<OrderIssues>()
                .Where(i => i.OrderId == orderId && !i.IsDeleted)
                .OrderBy(i => i.CreatedTime, OrderByType.Desc);

            var total = query.Count();
            var items = query
                .Skip((options.PageIndex - 1) * options.PageItems)
                .Take(options.PageItems)
                .ToList();

            return Task.FromResult(new PagedResult<OrderIssues>
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
            _logger.LogError(ex, "Error querying OrderIssues for OrderId={OrderId}", orderId);
            return Task.FromResult(new PagedResult<OrderIssues>());
        }
    }

    // ============================================================
    // 新建
    // ============================================================

    /// <summary>
    /// 在指定订单下新建一个问题。返回 (ok, error)。
    /// 校验:Order 必须存在且未删除;Content 长度合理(1-8000 字符)。
    /// </summary>
    public async Task<(bool ok, string? error)> CreateAsync(int orderId, string content)
    {
        var (authOk, authErr) = _currentUser.RequirePermission(PermissionCodes.OrderIssueAdd);
        if (!authOk)
        {
            _logger.LogWarning(
                "OrderIssueService.CreateAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        if (orderId <= 0) return (false, "订单不存在");
        content = content?.Trim() ?? "";
        if (content.Length == 0) return (false, "问题描述不能为空");
        if (content.Length > 8000) return (false, $"问题描述过长({content.Length}/8000 字符)");

        try
        {
            var orderExists = await _db.Queryable<Orders>()
                .Where(o => o.Id == orderId && !o.IsDeleted)
                .AnyAsync();
            if (!orderExists) return (false, "订单不存在或已被删除");

            var now = DateTime.Now;
            var user = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
            var entity = new OrderIssues
            {
                OrderId = orderId,
                Content = content,
                Status = IssueStatus.Open,        // 默认 open
                CreatedBy = user,
                CreatedTime = now,
                UpdatedBy = user,
                UpdatedTime = now,
                IsDeleted = false,
            };
            var newId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
            _logger.LogInformation(
                "OrderIssue created: id={Id} orderId={OrderId} callerId={CallerId}",
                newId, orderId, _currentUser.GetUserId());
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OrderIssue for OrderId={OrderId}", orderId);
            return (false, "保存失败,请重试");
        }
    }

    // ============================================================
    // 状态变更(open/close)
    // ============================================================

    /// <summary>
    /// (2026-08-14) 关闭问题:只允许 open → close,close 之后再调此方法全部拒绝。
    /// 关闭时写入 ClosedBy / ClosedTime,作为"首次也是唯一一次"的关闭记录。
    /// 返回 (ok, error)。失败时 error 给前端 toast 显示。
    /// </summary>
    public async Task<(bool ok, string? error)> UpdateStatusAsync(int issueId, string newStatus)
    {
        var (authOk, authErr) = _currentUser.RequirePermission(PermissionCodes.OrderIssueEditStatus);
        if (!authOk)
        {
            _logger.LogWarning(
                "OrderIssueService.UpdateStatusAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        // 只接受 "close";其他值直接拒绝(避免误传 "open" 重开)
        if (newStatus != IssueStatus.Close)
            return (false, "只能关闭(open → close),不允许其他操作");

        try
        {
            var user = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
            var now = DateTime.Now;

            // 原子校验:WHERE 子句里加 Status == "open",
            // 如果已经是 close(或不存在/已删除)则影响行数 = 0,不会误改。
            var rows = await _db.Updateable<OrderIssues>()
                .SetColumns(i => new OrderIssues
                {
                    Status = IssueStatus.Close,
                    ClosedBy = user,
                    ClosedTime = now,
                    UpdatedBy = user,
                    UpdatedTime = now,
                })
                .Where(i => i.Id == issueId && !i.IsDeleted && i.Status == IssueStatus.Open)
                .ExecuteCommandAsync();

            if (rows == 0) return (false, "问题已关闭、不存在或已被删除");

            _logger.LogInformation(
                "OrderIssue closed: id={Id} closedBy={ClosedBy} callerId={CallerId}",
                issueId, user, _currentUser.GetUserId());
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing OrderIssue: id={Id}", issueId);
            return (false, "关闭失败,请重试");
        }
    }

    // ============================================================
    // (2026-08-14) 保存解决方法
    // ============================================================

    /// <summary>
    /// 写入问题的解决方法。允许 open / close 状态都保存(关闭后仍可补充)。
    /// 返回 (ok, error)。失败时 error 给前端 toast 显示。
    /// </summary>
    public async Task<(bool ok, string? error)> SaveResolutionAsync(int issueId, string resolution)
    {
        var (authOk, authErr) = _currentUser.RequirePermission(PermissionCodes.OrderIssueEditStatus);
        if (!authOk)
        {
            _logger.LogWarning(
                "OrderIssueService.SaveResolutionAsync denied: callerId={CallerId} reason={Reason}",
                _currentUser.GetUserId(), authErr);
            return (false, authErr);
        }

        resolution = resolution?.Trim() ?? "";
        if (resolution.Length == 0) return (false, "解决方法不能为空");
        if (resolution.Length > 8000) return (false, $"解决方法过长({resolution.Length}/8000 字符)");

        try
        {
            var user = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
            var now = DateTime.Now;

            var rows = await _db.Updateable<OrderIssues>()
                .SetColumns(i => new OrderIssues
                {
                    Resolution = resolution,
                    UpdatedBy = user,
                    UpdatedTime = now,
                })
                .Where(i => i.Id == issueId && !i.IsDeleted)
                .ExecuteCommandAsync();

            if (rows == 0) return (false, "问题不存在或已被删除");

            _logger.LogInformation(
                "OrderIssue resolution saved: id={Id} callerId={CallerId}",
                issueId, _currentUser.GetUserId());
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving OrderIssue resolution: id={Id}", issueId);
            return (false, "保存失败,请重试");
        }
    }

    // ============================================================
    // 辅助
    // ============================================================

    /// <summary>查 OrderNumber 显示用(问题汇总页面顶部展示)。</summary>
    public async Task<int?> GetOrderNumberAsync(int orderId)
    {
        return await _db.Queryable<Orders>()
            .Where(o => o.Id == orderId && !o.IsDeleted)
            .Select(o => (int?)o.OrderNumber)
            .FirstAsync();
    }

    /// <summary>(2026-08-14) 查订单基础信息(OrderNumber / Power / RatedVoltage / IsReactor),
    /// 用于问题汇总页头部展示。返回 null 表示订单不存在或已删除。</summary>
    public async Task<Orders?> GetOrderBasicInfoAsync(int orderId)
    {
        return await _db.Queryable<Orders>()
            .Where(o => o.Id == orderId && !o.IsDeleted)
            .Select(o => new Orders
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Power = o.Power,
                RatedVoltage = o.RatedVoltage,
                IsReactor = o.IsReactor,
            })
            .FirstAsync();
    }

    /// <summary>查 OrderId 对应的订单是否存在(用于问题汇总页面校验)。</summary>
    public async Task<bool> OrderExistsAsync(int orderId)
    {
        return await _db.Queryable<Orders>()
            .Where(o => o.Id == orderId && !o.IsDeleted)
            .AnyAsync();
    }
}