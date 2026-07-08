using System.Collections.Concurrent;
using System.Linq.Expressions;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using BootstrapBlazor.OnlyServer1.Services.Queries;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 订单 Service:封装 Orders.razor 的全部数据访问 + SERIALIZABLE 事务去重 + 缓存。
    /// Scoped 生命周期,每个 circuit 一份独立 cache。
    /// </summary>
    public class OrdersService : BaseQueryService<Orders>
    {
        private readonly ISqlSugarClient _db;
        private readonly C0XQueryService _c0xQuery;
        private readonly IDrawingTypeRegistry _registry;
        private readonly ILogger<OrdersService> _logger;
        private readonly CurrentUserAccessor _currentUser;

        // 缓存:key = OrderId,value = 是否存在未删除的图号
        private readonly ConcurrentDictionary<int, bool> _hasDrawingNumberCache = new();

        // (2026-07-07 Review #H3 / 2026-07-08 命名审查 L-4):
        // OrderBy(string) 字符串拼接 SortName 防注入;不在此集合的 SortName 静默忽略,回退默认排序。
        // 之前用本地 HashSet + 自写 SearchAsync 实现,实际 Orders.razor 一直走继承自 BaseQueryService
        // 的 QueryAsync(PagedQuery),AllowedSortCols 是空 → 永远走 "CreatedTime desc" 默认排序,
        // 白名单其实从未生效。L-4 重构:override AllowedSortCols 让继承 QueryAsync 自动套用白名单,
        // 同一处 base.QueryAsync 一次实现能正确用 SortName + 兜底默认。
        private static readonly IReadOnlyCollection<string> _ordersAllowedSortCols =
            new[]
            {
                nameof(Orders.Id),
                nameof(Orders.OrderNumber),
                nameof(Orders.Power),
                nameof(Orders.RatedVoltage),
                nameof(Orders.IsReactor),
                nameof(Orders.CreatedBy),
                nameof(Orders.CreatedTime),
            };

        public OrdersService(
            ISqlSugarClient db,
            C0XQueryService c0xQuery,
            IDrawingTypeRegistry registry,
            CurrentUserAccessor currentUser,
            ILogger<OrdersService> logger)
            : base(db, logger)
        {
            _db = db;
            _c0xQuery = c0xQuery;
            _registry = registry;
            _currentUser = currentUser;
            _logger = logger;
        }

        // BaseQueryService 配置
        protected override Expression<Func<Orders, bool>> NotDeletedPredicate()
            => o => !o.IsDeleted;

        protected override Expression<Func<Orders, bool>>? BuildSearchPredicate(string keyword)
            => o => o.OrderNumber.ToString().Contains(keyword) || o.CreatedBy.Contains(keyword);

        protected override string DefaultSortField() => "CreatedTime";

        // 2026-07-08 命名审查 L-4:override 暴露白名单给继承的 QueryAsync,删除重复 SearchAsync。
        protected override IReadOnlyCollection<string> AllowedSortCols => _ordersAllowedSortCols;

        // ===== 查询 =====

        public async Task<Orders?> FindByIdAsync(int orderId)
        {
            try
            {
                return await _db.Queryable<Orders>().InSingleAsync(orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order {Id}", orderId);
                return null;
            }
        }

        // ===== 保存(默认隔离级 + UNIQUE INDEX 去重) =====

        /// <summary>
        /// 保存订单。返回 (ok, error):
        ///   - ok=true:成功
        ///   - ok=false:被业务规则拦截,error 是用户可读的中文消息
        /// 内部:
        ///   1. 验证 OrderNumber > 0
        ///   2. 兜底:已存在图号时不允许修改(Update 分支)
        ///   3. 默认隔离级(READ COMMITTED)事务内写入,OrderNumber 唯一性靠
        ///      DbMigrator 创建的 filtered unique index(IX_Orders_OrderNumber)约束
        ///   4. INSERT/UPDATE 触发唯一冲突时 catch SqlException 2627/2601,
        ///      翻译成"订单号已存在"友好提示
        ///
        /// 早期版本曾用 SERIALIZABLE 事务 + 应用层重名预检,实测 SERIALIZABLE 的
        /// 范围锁会导致同一 OrderNumber 范围的并发 SELECT 被阻塞,lock_timeout 默认
        /// -1(无限等待),用户体感为"点了保存 1-2 分钟没反应"。
        /// </summary>
        public async Task<(bool ok, string? error)> SaveAsync(Orders order, ItemChangedType changedType)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // Razor 层 [Authorize] 只挡外面;Service 必须自己也挡。
            // 防:新 Controller / 任务调度 / 测试代码直接调 Service 绕过 Razor。
            // 不删这段校验、不删这个注释。
            // 规则:Add=Admin/Operator;Update=Admin 或 该订单的 CreatedBy。
            // ====================================================================
            if (changedType == ItemChangedType.Update)
            {
                var isAdmin = _currentUser.IsInAnyRole(RoleNames.Admin);
                var currentName = _currentUser.GetDisplayName() ?? "";
                var isOwner = !string.IsNullOrEmpty(order.CreatedBy) && order.CreatedBy == currentName;
                if (!isAdmin && !isOwner)
                {
                    _logger.LogWarning(
                        "OrdersService.SaveAsync denied (Update not by creator): callerId={CallerId} callerName={CallerName} orderId={OrderId} orderCreatedBy={CreatedBy}",
                        _currentUser.GetUserId(), currentName, order.Id, order.CreatedBy);
                    return (false, "只有管理员或该订单的创建者才能修改订单");
                }
            }
            else
            {
                var (ok, err) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
                if (!ok)
                {
                    _logger.LogWarning(
                        "OrdersService.SaveAsync denied (Add): callerId={CallerId} reason={Reason}",
                        _currentUser.GetUserId(), err);
                    return (false, err);
                }
            }

            if (order.OrderNumber <= 0)
            {
                return (false, "订单号不能为空");
            }

            // 兜底:编辑订单时,如果该订单下已存在图号则不允许修改
            if (changedType == ItemChangedType.Update)
            {
                var drawings = await _c0xQuery.GetAddedDrawingNumbersByOrderIdAsync(order.Id);
                if (drawings != null && drawings.Count > 0)
                {
                    _logger.LogWarning("Order {OrderNumber} (Id={Id}) has {Count} drawing numbers, modification not allowed",
                        order.OrderNumber, order.Id, drawings.Count);
                    return (false, $"订单 {order.OrderNumber} 下存在 {drawings.Count} 个图号,不允许修改。请先删除相关图号后再修改订单信息。");
                }
            }

            try
            {
                _db.Ado.BeginTran();
                try
                {
                    if (changedType == ItemChangedType.Add)
                    {
                        _logger.LogInformation("Adding new order: {OrderNumber}", order.OrderNumber);
                        order.CreatedTime = DateTime.Now;
                        order.UpdatedTime = DateTime.Now;
                        order.IsDeleted = false;
                        order.UpdatedBy = order.CreatedBy;
                        try
                        {
                            await _db.Insertable(order).ExecuteReturnIdentityAsync();
                        }
                        catch (Exception ex) when (IsDuplicateOrderNumberError(ex))
                        {
                            _db.Ado.RollbackTran();
                            _logger.LogWarning("Duplicate OrderNumber on INSERT: {OrderNumber}", order.OrderNumber);
                            return (false, $"订单号 {order.OrderNumber} 已存在,请使用其他订单号");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Updating order: {OrderNumber}", order.OrderNumber);
                        order.UpdatedTime = DateTime.Now;
                        try
                        {
                            await _db.Updateable(order).ExecuteCommandAsync();
                        }
                        catch (Exception ex) when (IsDuplicateOrderNumberError(ex))
                        {
                            _db.Ado.RollbackTran();
                            _logger.LogWarning("Duplicate OrderNumber on UPDATE: {OrderNumber}", order.OrderNumber);
                            return (false, $"订单号 {order.OrderNumber} 已被其他订单占用");
                        }
                    }

                    _db.Ado.CommitTran();
                }
                catch
                {
                    _db.Ado.RollbackTran();
                    throw;
                }

                InvalidateDrawingNumberCache();
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving order: OrderNumber={N}", order.OrderNumber);
                return (false, "保存失败,请重试");
            }
        }

        /// <summary>
        /// SQL Server 唯一索引冲突检测。SQL Server 错误号:
        ///   2627 — %ls 约束 '%.*ls' 违反 %ls 约束。不能在对象 '%.*ls' 中插入重复键。
        ///   2601 — 不能在具有唯一索引 '%.*ls' 的对象 '%.*ls' 中插入重复键的行。
        /// 这里再附加 IX_Orders_OrderNumber / "unique"/"duplicate"/"duplicate key" 字符串兜底,
        /// 防止错误消息被本地化后错误号匹配失效。
        /// </summary>
        private static bool IsDuplicateOrderNumberError(Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("2627") || msg.Contains("2601")) return true;
            if (msg.Contains("IX_Orders_OrderNumber", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("unique key", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ===== 软删除 =====

        public async Task<bool> SoftDeleteAsync(IEnumerable<Orders> orders)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 软删订单是高权限操作:仅 Admin。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(RoleNames.Admin);
            if (!ok)
            {
                _logger.LogWarning(
                    "OrdersService.SoftDeleteAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return false;
            }

            var orderList = orders.ToList();
            try
            {
                foreach (var order in orderList)
                {
                    _logger.LogInformation("Soft-deleting order: {OrderNumber}", order.OrderNumber);
                    order.IsDeleted = true;
                    await _db.Updateable(order).UpdateColumns(o => new { o.IsDeleted }).ExecuteCommandAsync();
                }
                InvalidateDrawingNumberCache();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting orders");
                return false;
            }
        }

        // ===== 服务端权威检查(直接查 DB,不查 cache) =====

        public async Task<bool> HasDrawingNumbersAsync(int orderId)
        {
            try
            {
                var list = await _c0xQuery.GetAddedDrawingNumbersByOrderIdAsync(orderId);
                return list != null && list.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HasDrawingNumbersAsync failed: orderId={Id}", orderId);
                return false;
            }
        }

        // ===== 批量预查(缓存填充用) =====

        public async Task PreloadHasDrawingNumberCacheAsync(IEnumerable<Orders> orders)
        {
            var uncached = orders.Where(o => !_hasDrawingNumberCache.ContainsKey(o.Id)).ToList();
            if (uncached.Count == 0) return;

            // 按 drawingType 分组(drawingType 是纯计算,不查 DB)
            var byType = uncached
                .Select(o => (Order: o, Type: _registry.GetDrawingType(o)))
                .Where(x => !string.IsNullOrEmpty(x.Type))
                .GroupBy(x => x.Type!);

            foreach (var group in byType)
            {
                var orderIds = group.Select(x => x.Order.Id).ToList();
                HashSet<int> existingIds;
                try
                {
                    existingIds = await BatchQueryExistingDrawingOrderIdsAsync(group.Key, orderIds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BatchQueryExistingDrawingOrderIdsAsync failed: type={Type}", group.Key);
                    existingIds = new HashSet<int>();
                }

                foreach (var id in orderIds)
                {
                    // 查询失败时按"没有图号"处理,允许编辑,避免误伤
                    _hasDrawingNumberCache[id] = existingIds.Contains(id);
                }
            }
        }

        public async Task<HashSet<int>> BatchQueryExistingDrawingOrderIdsAsync(string drawingType, List<int> orderIds)
        {
            if (orderIds == null || orderIds.Count == 0) return new HashSet<int>();

            var handler = _registry.Resolve(drawingType);
            if (handler == null) return new HashSet<int>();

            var raw = await handler.QueryExistingOrderIdsAsync(_db, orderIds);
            return new HashSet<int>(raw);
        }

        // ===== 缓存读写 =====

        public bool TryGetHasDrawingNumber(int orderId, out bool has)
            => _hasDrawingNumberCache.TryGetValue(orderId, out has);

        public void InvalidateDrawingNumberCache() => _hasDrawingNumberCache.Clear();
    }
}