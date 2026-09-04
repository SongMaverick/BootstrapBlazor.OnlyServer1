using System.Collections.Concurrent;
using System.Linq.Expressions;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Services.Common;
using BootstrapBlazor.OnlyServer1.Services.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.OrderManagement
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

        /// <summary>
        /// 按订单号精确查一条订单。给 /order-management 的"按订单号跳转"用。
        /// orderNumber 无效或未命中返回 null,已软删订单不返回。
        /// </summary>
        public async Task<Orders?> FindByOrderNumberAsync(int orderNumber)
        {
            if (orderNumber <= 0) return null;
            try
            {
                return await _db.Queryable<Orders>()
                    .Where(o => o.OrderNumber == orderNumber && !o.IsDeleted)
                    .FirstAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding order by OrderNumber {OrderNumber}", orderNumber);
                return null;
            }
        }

        /// <summary>
        /// (2026-09-03) /order-management 左侧主从表的列表查询。
        /// 与默认 QueryAsync(CreatedTime desc) 不同:这里 LEFT JOIN OrderDesignPhases,
        /// 按 Phase1SubmitDate desc 排 —— 让"已进入一次提交环节"的订单靠前,
        /// 还没提交的(NULL)自然沉到列表底部(SQL Server 默认 NULLS LAST)。
        ///
        /// 保持与 QueryAsync 同样的入参/返回 (PagedResult<Orders>),以便直接替换调用点。
        /// 作用域:仅 OrderManagement 左侧表用,不动 BaseQueryService / AllowedSortCols / DefaultSortField。
        ///
        /// (2026-09-04) 兼容本地/旧测试库:OrderDesignPhases 表可能还没建(没跑过
        /// BootstrapBlazor.OrderManagementSetup --install)。表缺失时 SQL 抛
        /// "Invalid object name 'OrderDesignPhases'",此时回退到 QueryAsync(CreatedTime desc),
        /// 列表能正常显示,排序退化为默认。已建表的库(已跑过 Setup)走新逻辑按 Phase1SubmitDate 排。
        ///
        /// (2026-09-04) 加 year + month 筛选:订单管理页「按年/月筛选」下拉用,4 种组合:
        ///   - 都不传(全 null):不筛选
        ///   - 只传 year:Phase1SubmitDate 落在该年(用 [Jan 1, next Jan 1) 闭开区间,索引友好)
        ///   - 只传 month:Phase1SubmitDate 的月份等于 month(任意年;用 MONTH() 函数,牺牲索引换简洁)
        ///   - year + month:Phase1SubmitDate 落在该年该月([月初, 下月初) 闭开区间,索引友好)
        /// month-only 走 MONTH() 是因为"任意年 8 月"没法用单个日期范围表达;订单管理数据量不大,性能可接受。
        /// 不传也兼容(向后兼容老调用点)。
        /// </summary>
        public async Task<PagedResult<Orders>> GetMasterListAsync(PagedQuery options, int? year = null, int? month = null)
        {
            try
            {
                // LeftJoin 是一对一扩展,OrderDesignPhases 表有 IX_OrderDesignPhases_OrderId (WHERE IsDeleted=0)
                // 过滤唯一索引,所以每个 Orders 最多匹配一行;不会出现重复行打乱分页。
                var query = Db.Queryable<Orders>()
                    .LeftJoin<OrderDesignPhases>((o, p) => o.Id == p.OrderId && !p.IsDeleted)
                    .Where(o => !o.IsDeleted);

                // (2026-09-04) 按年/月筛选:3 种组合的 WHERE 分支。
                // year+month 同时给:用 [月初, 下月初) 闭开区间(索引友好,走 Phase1SubmitDate 范围扫描)
                // 只给 year:[Jan 1, next Jan 1) 闭开区间(同样索引友好)
                // 只给 month:用 MONTH() 函数,牺牲索引换"任意年该月"语义
                if (year.HasValue && month.HasValue)
                {
                    var start = new DateTime(year.Value, month.Value, 1);
                    var end = start.AddMonths(1);
                    query = query.Where((o, p) => p.Phase1SubmitDate != null && p.Phase1SubmitDate >= start && p.Phase1SubmitDate < end);
                }
                else if (year.HasValue)
                {
                    var start = new DateTime(year.Value, 1, 1);
                    var end = start.AddYears(1);
                    query = query.Where((o, p) => p.Phase1SubmitDate != null && p.Phase1SubmitDate >= start && p.Phase1SubmitDate < end);
                }
                else if (month.HasValue)
                {
                    // MONTH() 不会走索引,但订单管理数据量不大可接受;非空判断避免 NULL 月份误命中
                    var m = month.Value;
                    query = query.Where((o, p) => p.Phase1SubmitDate != null && p.Phase1SubmitDate.Value.Month == m);
                }

                // 关键字搜索(沿用 BaseQueryService 的搜索语义:订单号 / 创建者)
                var kw = options.SearchText;
                if (!string.IsNullOrEmpty(kw))
                {
                    query = query.Where((o, p) => o.OrderNumber.ToString().Contains(kw) || o.CreatedBy.Contains(kw));
                }

                // 计数在 OrderBy/Select 前取,SqlSugar 会自动改写为 COUNT(*)
                var total = query.Count();
                var items = query
                    // 强类型重载,不走字符串拼接,无 SQL 注入风险
                    .OrderBy((o, p) => p.Phase1SubmitDate, OrderByType.Desc)
                    // (2026-09-04) 投影到 Orders,把 Phase1SubmitDate 填到 [SugarColumn(IsIgnore=true)] 虚拟列,
                    // UI 层 TableColumn 直接 @bind-Field="@context.Phase1SubmitDate" 即可显示,不用换 Table TItem。
                    // 这里不能 Select((o, p) => o) — 那样 Phase1SubmitDate 永远 null。
                    .Select((o, p) => new Orders
                    {
                        Id = o.Id,
                        OrderNumber = o.OrderNumber,
                        Power = o.Power,
                        RatedVoltage = o.RatedVoltage,
                        IsReactor = o.IsReactor,
                        CreatedBy = o.CreatedBy,
                        UpdatedTime = o.UpdatedTime,
                        UpdatedBy = o.UpdatedBy,
                        CreatedTime = o.CreatedTime,
                        IsDeleted = o.IsDeleted,
                        Phase1SubmitDate = p.Phase1SubmitDate,
                    })
                    .Skip((options.PageIndex - 1) * options.PageItems)
                    .Take(options.PageItems)
                    .ToList();

                return new PagedResult<Orders>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.FilterCount > 0 || year.HasValue || month.HasValue, // (2026-09-04) 年/月筛选也算 filtered
                    IsSorted = true, // 固定排序,不响应前端 SortName
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.HasSearchModel,
                };
            }
            catch (Exception ex) when (IsMissingPhasesTable(ex))
            {
                // (2026-09-04) OrderDesignPhases 表缺失 — 回退到默认排序,不抛、不返回空,
                // 列表至少能正常展示,只是排序回到 CreatedTime desc。等库跑过 Setup 就走新逻辑。
                _logger.LogWarning(
                    "GetMasterListAsync: OrderDesignPhases 表不存在,回退到 QueryAsync 默认排序。库未跑 OrderManagementSetup --install,建议补跑一次以启用新排序。");
                return await QueryAsync(options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetMasterListAsync (OrderManagement master list)");
                return new PagedResult<Orders>();
            }
        }

        /// <summary>
        /// (2026-09-04) 判定异常是否由 OrderDesignPhases 表缺失引起。
        /// 消息文本匹配 Microsoft.Data.SqlClient.SqlException 的 "Invalid object name 'xxx'" 格式。
        /// 用 message 匹配而不是查 sys.objects,避免每次分页查询都多一次往返;
        /// happy path 0 成本,坏 path 第一次失败后回退,后续直接走 QueryAsync。
        /// </summary>
        private static bool IsMissingPhasesTable(Exception ex)
        {
            const string marker = "Invalid object name 'OrderDesignPhases'";
            if (ex.Message?.Contains(marker) == true) return true;
            if (ex.InnerException?.Message?.Contains(marker) == true) return true;
            return false;
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
                            order.Id = await _db.Insertable(order).ExecuteReturnIdentityAsync();
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

                // (2026-08-17 重构)新建订单时,best-effort 给「订单项目信息」建一条空行(OrderInfo)。
                // 与之前的 OrderMDDetail 同款设计:放在事务提交之后 + 异常吞掉,
                // 确保下单主流程不受「订单管理」表结构未就绪的影响。
                // OrderDesignPhases / OrderImportantComponents 不自动建 —— 它们是更细颗粒度的数据,
                // 不是每个订单都需要,用户首次手动编辑时再创建即可。
                if (changedType == ItemChangedType.Add)
                {
                    await TryCreateEmptyOrderInfoAsync(order.Id, order.CreatedBy);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving order: OrderNumber={N}", order.OrderNumber);
                return (false, "保存失败,请重试");
            }
        }

        /// <summary>
        /// (2026-08-17 重构)给新建的订单补一条空的「订单项目信息」行(OrderInfo)。
        ///
        /// best-effort:任何异常只记日志,不向上抛、不影响下单结果。
        /// 幂等:已存在未删除的 OrderInfo 时直接返回,不会重复插入
        /// (真正的唯一性由 IX_OrderInfo_OrderId (WHERE IsDeleted=0) 兜底)。
        /// </summary>
        private async Task TryCreateEmptyOrderInfoAsync(int orderId, string createdBy)
        {
            if (orderId <= 0) return;

            try
            {
                var exists = await _db.Queryable<OrderInfo>()
                    .Where(i => i.OrderId == orderId && !i.IsDeleted)
                    .AnyAsync();
                if (exists) return;

                var now = DateTime.Now;
                await _db.Insertable(new OrderInfo
                {
                    OrderId = orderId,
                    UnitCount = 1,
                    CreatedBy = createdBy,
                    CreatedTime = now,
                    UpdatedBy = createdBy,
                    UpdatedTime = now,
                    IsDeleted = false,
                }).ExecuteCommandAsync();

                _logger.LogInformation("Empty OrderInfo created for new order {OrderId}", orderId);
            }
            catch (Exception ex)
            {
                // 不影响下单;用户首次编辑项目信息时由 OrderInfoService 兜底补建
                _logger.LogError(ex,
                    "TryCreateEmptyOrderInfoAsync failed for order {OrderId} (下单已成功,OrderInfo 行将在首次编辑时补建)",
                    orderId);
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
