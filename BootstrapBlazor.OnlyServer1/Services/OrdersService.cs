using System.Collections.Concurrent;
using System.Linq.Expressions;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
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

        // 缓存:key = OrderId,value = 是否存在未删除的图号
        private readonly ConcurrentDictionary<int, bool> _hasDrawingNumberCache = new();

        public OrdersService(
            ISqlSugarClient db,
            C0XQueryService c0xQuery,
            IDrawingTypeRegistry registry,
            ILogger<OrdersService> logger)
            : base(db, logger)
        {
            _db = db;
            _c0xQuery = c0xQuery;
            _registry = registry;
            _logger = logger;
        }

        // BaseQueryService 配置
        protected override Expression<Func<Orders, bool>> NotDeletedPredicate()
            => o => !o.IsDeleted;

        protected override Expression<Func<Orders, bool>>? BuildSearchPredicate(string keyword)
            => o => o.OrderNumber.ToString().Contains(keyword) || o.CreatedBy.Contains(keyword);

        protected override string DefaultSortField() => "CreatedTime";

        // ===== 查询 =====

        public async Task<Orders?> GetByIdAsync(int orderId)
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

        /// <summary>
        /// 带过滤条件的订单查询(按 OrderNumber / CreatedBy / IsReactor 三字段筛选)。
        /// 三个 filter 参数均可为 null/空,表示不过滤该字段。
        /// </summary>
        public async Task<PagedResult<Orders>> SearchAsync(
            PagedQuery options,
            string? orderNumberKeyword,
            string? createdByKeyword,
            string? reactorFilter)
        {
            try
            {
                var query = _db.Queryable<Orders>().Where(o => !o.IsDeleted);

                if (!string.IsNullOrWhiteSpace(orderNumberKeyword))
                {
                    var kw = orderNumberKeyword.Trim();
                    query = query.Where(o => o.OrderNumber.ToString().Contains(kw));
                }
                if (!string.IsNullOrWhiteSpace(createdByKeyword))
                {
                    var kw = createdByKeyword.Trim();
                    query = query.Where(o => o.CreatedBy.Contains(kw));
                }
                if (!string.IsNullOrWhiteSpace(reactorFilter) && bool.TryParse(reactorFilter, out var isReactor))
                {
                    query = query.Where(o => o.IsReactor == isReactor);
                }

                if (!string.IsNullOrEmpty(options.SortName))
                {
                    query = options.SortOrder == SortDirection.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderByDescending(o => o.OrderNumber);
                }

                var total = query.Count();
                var items = query.Skip((options.PageIndex - 1) * options.PageItems).Take(options.PageItems).ToList();

                return new PagedResult<Orders>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.FilterCount > 0,
                    IsSorted = !string.IsNullOrEmpty(options.SortName),
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.HasSearchModel
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching orders");
                return new PagedResult<Orders>();
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