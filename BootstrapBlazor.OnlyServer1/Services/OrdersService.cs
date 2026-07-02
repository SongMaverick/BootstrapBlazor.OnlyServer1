using System.Collections.Concurrent;
using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 订单 Service:封装 Orders.razor 的全部数据访问 + SERIALIZABLE 事务去重 + 缓存。
    /// Scoped 生命周期,每个 circuit 一份独立 cache。
    /// </summary>
    public class OrdersService
    {
        private readonly ISqlSugarClient _db;
        private readonly DrawingNumberService _drawingNumberService;
        private readonly IDrawingTypeRegistry _registry;
        private readonly ILogger<OrdersService> _logger;

        // 缓存:key = OrderId,value = 是否存在未删除的图号
        private readonly ConcurrentDictionary<int, bool> _hasDrawingNumberCache = new();

        public OrdersService(
            ISqlSugarClient db,
            DrawingNumberService drawingNumberService,
            IDrawingTypeRegistry registry,
            ILogger<OrdersService> logger)
        {
            _db = db;
            _drawingNumberService = drawingNumberService;
            _registry = registry;
            _logger = logger;
        }

        // ===== 查询 =====

        public async Task<QueryData<Orders>> QueryAsync(QueryPageOptions options)
        {
            try
            {
                var query = _db.Queryable<Orders>().Where(o => !o.IsDeleted);

                if (!string.IsNullOrEmpty(options.SearchText))
                {
                    query = query.Where(o => o.OrderNumber.ToString().Contains(options.SearchText)
                                          || o.CreatedBy.Contains(options.SearchText));
                }

                if (!string.IsNullOrEmpty(options.SortName))
                {
                    query = options.SortOrder == SortOrder.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderByDescending(o => o.CreatedTime);
                }

                var total = query.Count();
                var items = query.Skip((options.PageIndex - 1) * options.PageItems).Take(options.PageItems).ToList();

                return new QueryData<Orders>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.Filters.Any(),
                    IsSorted = !string.IsNullOrEmpty(options.SortName),
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.SearchModel != null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying orders");
                return new QueryData<Orders>();
            }
        }

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
        /// 带过滤条件的查询(用于 AddDrawingNumbersSelect 页的 OrderNumber / CreatedBy / IsReactor 筛选)。
        /// 三个 filter 参数均可为 null/空,表示不过滤该字段。
        /// </summary>
        public async Task<QueryData<Orders>> SearchAsync(
            QueryPageOptions options,
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
                    query = options.SortOrder == SortOrder.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    query = query.OrderByDescending(o => o.OrderNumber);
                }

                var total = query.Count();
                var items = query.Skip((options.PageIndex - 1) * options.PageItems).Take(options.PageItems).ToList();

                return new QueryData<Orders>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.Filters.Any(),
                    IsSorted = !string.IsNullOrEmpty(options.SortName),
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.SearchModel != null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching orders");
                return new QueryData<Orders>();
            }
        }

        // ===== 保存(SERIALIZABLE 事务去重) =====

        /// <summary>
        /// 保存订单。返回 (ok, error):
        ///   - ok=true:成功
        ///   - ok=false:被业务规则拦截,error 是用户可读的中文消息
        /// 内部:
        ///   1. 验证 OrderNumber > 0
        ///   2. 兜底:已存在图号时不允许修改(Update 分支)
        ///   3. SERIALIZABLE 事务内重查 + 写入,防止 OrderNumber 并发重复
        ///   4. 死锁/超时返回友好提示,让用户重试
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
                var drawings = await _drawingNumberService.GetAddedDrawingNumbersByOrderIdAsync(order.Id);
                if (drawings != null && drawings.Count > 0)
                {
                    _logger.LogWarning("Order {OrderNumber} (Id={Id}) has {Count} drawing numbers, modification not allowed",
                        order.OrderNumber, order.Id, drawings.Count);
                    return (false, $"订单 {order.OrderNumber} 下存在 {drawings.Count} 个图号,不允许修改。请先删除相关图号后再修改订单信息。");
                }
            }

            try
            {
                _db.Ado.BeginTran(System.Data.IsolationLevel.Serializable);
                try
                {
                    if (changedType == ItemChangedType.Add)
                    {
                        var isDuplicate = await _db.Queryable<Orders>()
                            .Where(o => o.OrderNumber == order.OrderNumber && !o.IsDeleted)
                            .AnyAsync();
                        if (isDuplicate)
                        {
                            _db.Ado.RollbackTran();
                            _logger.LogWarning("Duplicate OrderNumber: {OrderNumber}", order.OrderNumber);
                            return (false, $"订单号 {order.OrderNumber} 已存在,请使用其他订单号");
                        }

                        _logger.LogInformation("Adding new order: {OrderNumber}", order.OrderNumber);
                        order.CreatedTime = DateTime.Now;
                        order.UpdatedTime = DateTime.Now;
                        order.IsDeleted = false;
                        order.UpdatedBy = order.CreatedBy;
                        await _db.Insertable(order).ExecuteReturnIdentityAsync();
                    }
                    else
                    {
                        var isDuplicate = await _db.Queryable<Orders>()
                            .Where(o => o.OrderNumber == order.OrderNumber && !o.IsDeleted && o.Id != order.Id)
                            .AnyAsync();
                        if (isDuplicate)
                        {
                            _db.Ado.RollbackTran();
                            return (false, $"订单号 {order.OrderNumber} 已被其他订单占用");
                        }

                        _logger.LogInformation("Updating order: {OrderNumber}", order.OrderNumber);
                        order.UpdatedTime = DateTime.Now;
                        await _db.Updateable(order).ExecuteCommandAsync();
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
            catch (SqlSugarException ex) when (IsDeadlockOrTimeout(ex))
            {
                _logger.LogWarning(ex, "Save order deadlock/timeout, OrderNumber={N}", order.OrderNumber);
                return (false, "系统繁忙,请稍后重试");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving order");
                return (false, "保存失败,请重试");
            }
        }

        private static bool IsDeadlockOrTimeout(SqlSugarException ex)
        {
            var msg = ex.Message ?? string.Empty;
            return msg.Contains("1205")
                || msg.Contains("-2")
                || msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase);
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
                var list = await _drawingNumberService.GetAddedDrawingNumbersByOrderIdAsync(orderId);
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