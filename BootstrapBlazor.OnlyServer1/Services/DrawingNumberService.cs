using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 图号 Service:封装 9 张 C0X 表的增删改查 + 图名管理 + 订单级并发控制。
    /// Scoped 生命周期。
    /// </summary>
    public class DrawingNumberService
    {
        private readonly ISqlSugarClient _db;
        private readonly CurrentUserAccessor _currentUser;
        private readonly OrderLockProvider _orderLock;
        private readonly IDrawingTypeRegistry _registry;
        private readonly IDrawingNumberAllocator _allocator;
        private readonly ILogger<DrawingNumberService> _logger;

        public DrawingNumberService(
            ISqlSugarClient db,
            CurrentUserAccessor currentUser,
            OrderLockProvider orderLock,
            IDrawingTypeRegistry registry,
            IDrawingNumberAllocator allocator,
            ILogger<DrawingNumberService> logger)
        {
            _db = db;
            _currentUser = currentUser;
            _orderLock = orderLock;
            _registry = registry;
            _allocator = allocator;
            _logger = logger;
        }

        public async Task<List<DrawingNames>> GetAllDrawingNamesAsync()
        {
            return await _db.Queryable<DrawingNames>()
                .Where(dn => !dn.Isdelete)
                .OrderBy(dn => dn.Id)
                .ToListAsync();
        }

        public async Task<List<DrawingNumberViewModel>> GetAddedDrawingNumberViewModelsByOrderIdAsync(int orderId)
        {
            var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                return new List<DrawingNumberViewModel>();
            }

            var c0xList = await GetAddedDrawingNumbersByOrderIdAsync(orderId);
            var drawingType = _registry.GetDrawingType(order) ?? "";

            // 用纯静态 ToViewModel 转换,避免对 Orders 的 N+1 重复查询
            return c0xList
                .Select(c0x => ToViewModel(c0x, order.OrderNumber, drawingType))
                .ToList();
        }

        public async Task<List<C0XBase>> GetAddedDrawingNumbersByOrderIdAsync(int orderId)
        {
            var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                return new List<C0XBase>();
            }

            var drawingType = _registry.GetDrawingType(order);
            if (string.IsNullOrEmpty(drawingType))
            {
                _logger.LogWarning("Could not determine drawing type for order: {OrderId}", orderId);
                return new List<C0XBase>();
            }

            var handler = _registry.Resolve(drawingType);
            return handler == null ? new List<C0XBase>() : await handler.GetAllByOrderIdAsync(_db, orderId);
        }

        public async Task<DrawingNumberInsertResult> AddDrawingNumbersAsync(
            int orderId, string[] drawingNames, CancellationToken ct = default)
        {
            var result = new DrawingNumberInsertResult();
            if (drawingNames == null || drawingNames.Length == 0) return result;

            var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                foreach (var n in drawingNames.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
                    result.Failures.Add(new(n, "订单不存在"));
                return result;
            }

            var drawingType = _registry.GetDrawingType(order);
            if (string.IsNullOrEmpty(drawingType))
            {
                _logger.LogWarning("Could not determine drawing type for order: {OrderId}", orderId);
                foreach (var n in drawingNames.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
                    result.Failures.Add(new(n, "订单类型无法识别"));
                return result;
            }

            var currentUserName = _currentUser.GetDisplayName();
            if (string.IsNullOrEmpty(currentUserName))
            {
                _logger.LogWarning("Current user not found");
                foreach (var n in drawingNames.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
                    result.Failures.Add(new(n, "未登录"));
                return result;
            }

            // 同订单串行化:防止两个并发请求预查后互穿插入产生重复行
            using (await _orderLock.AcquireAsync(orderId, ct))
            {
                // 锁内重新查询最新已存在集合,确保看到锁释放前的真实状态
                var existingNames = await GetExistingDrawingNamesAsync(orderId, drawingType, ct);

                foreach (var drawingName in drawingNames
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();

                    if (existingNames.Contains(drawingName, StringComparer.Ordinal))
                    {
                        result.Failures.Add(new(drawingName, "已添加过该图号"));
                        continue;
                    }

                    try
                    {
                        var id = await AddSingleDrawingNumberCoreAsync(
                            order, drawingType, drawingName, currentUserName);
                        if (id > 0)
                        {
                            result.Success.Add(drawingName);
                            existingNames.Add(drawingName); // 防本次循环内重复
                        }
                        else
                        {
                            result.Failures.Add(new(drawingName, "插入失败(数据库返回 0)"));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error adding drawing number: {DrawingName}", drawingName);
                        result.Failures.Add(new(drawingName, "服务器异常"));
                    }
                }
            }

            _logger.LogInformation(
                "AddDrawingNumbersAsync: order={Order} type={Type} success={S} fail={F}",
                orderId, drawingType, result.Success.Count, result.Failures.Count);

            return result;
        }

        // 锁内辅助:拉取该订单当前所有未删除的 DrawName(通过 registry handler)
        private async Task<HashSet<string>> GetExistingDrawingNamesAsync(
            int orderId, string drawingType, CancellationToken ct)
        {
            var handler = _registry.Resolve(drawingType);
            if (handler == null) return new HashSet<string>();
            var names = await handler.GetExistingDrawNamesAsync(_db, orderId);
            return new HashSet<string>(names, StringComparer.Ordinal);
        }

        /// <summary>
        /// 旧版包装方法:为单条插入场景保留(目前无外部调用方)。
        /// </summary>
        public async Task<bool> AddSingleDrawingNumberAsync(Orders order, string drawingType, string drawingName, string currentUserName)
        {
            try
            {
                var id = await AddSingleDrawingNumberCoreAsync(order, drawingType, drawingName, currentUserName);
                return id > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddSingleDrawingNumberAsync failed: {DrawingName}", drawingName);
                return false;
            }
        }

        private async Task<int> AddSingleDrawingNumberCoreAsync(Orders order, string drawingType, string drawingName, string currentUserName)
        {
            var handler = _registry.Resolve(drawingType);
            if (handler == null)
            {
                _logger.LogWarning("AddSingleDrawingNumberCoreAsync: no handler for drawingType {DrawingType}", drawingType);
                return 0;
            }

            var now = DateTime.Now;
            var c0x = handler.CreateNewEntity(
                drawingName, drawingType, order.Id, currentUserName, now);

            // 先原子分配 DrawNumber,再写入
            c0x.DrawNumber = await _allocator.AllocateAsync(drawingType);
            int insertedId = await handler.InsertAsync(_db, c0x);

            if (insertedId > 0)
            {
                _logger.LogInformation("User {UserName} added drawing number {DrawingName} (DrawNumber={DrawNumber}) to order {OrderNumber}",
                    currentUserName, drawingName, c0x.DrawNumber, order.OrderNumber);
            }

            return insertedId;
        }

        public async Task<DrawingNumberViewModel> DrawingNumber2DrawingNumberViewModelAsync(C0XBase c0x, string drawingType)
        {
            var order = await _db.Queryable<Orders>().InSingleAsync(c0x.OrderId);
            return ToViewModel(c0x, order?.OrderNumber ?? 0, drawingType);
        }

        /// <summary>
        /// 纯转换:C0X 实体 + Order.OrderNumber + drawingType → ViewModel。
        /// drawingType 由调用方提供(从 handler/registry 取),不再依赖 c0x 自身的 DrawingType 字段(已删除)。
        /// 不查库,供 <see cref="GetAddedDrawingNumberViewModelsByOrderIdAsync"/> 在循环外复用,
        /// 消除原 N+1 问题。
        /// </summary>
        public DrawingNumberViewModel ToViewModel(C0XBase c0x, int orderNumber, string drawingType)
        {
            // 图号直接来自持久化列 DrawNumber,不再做 MinDrawingNumber + Id 公式
            string tempDrawingNumber = c0x.DrawNumber.ToString("D5");

            return new DrawingNumberViewModel
            {
                Id = c0x.Id,
                DrawingType = drawingType,
                DrawName = c0x.DrawName,
                CreatedBy = c0x.CreatedBy,
                CreatedTime = c0x.CreatedTime,
                UpdatedBy = c0x.UpdatedBy,
                UpdatedTime = c0x.UpdatedTime,
                IsDelete = c0x.IsDelete,
                OrderId = c0x.OrderId,
                OrderNumber = orderNumber,
                DrawingTypeNumber =
                    drawingType + " " + tempDrawingNumber.Substring(0, 2) + " " +
                    tempDrawingNumber.Substring(2, 3)
            };
        }

        public async Task<C0XBase?> FindDrawingNumberByIdAsync(int id, string drawingType)
        {
            var handler = _registry.Resolve(drawingType);
            return handler == null ? null : await handler.FindByIdAsync(_db, id);
        }

        public async Task<int> UpdateCX0DrawingNumberByIdAsync(C0XBase c0x, string drawingType)
        {
            var handler = _registry.Resolve(drawingType);
            if (handler == null) return 0;
            return await handler.UpdateDrawNameAsync(
                _db, c0x.Id, c0x.DrawName, c0x.UpdatedBy, c0x.UpdatedTime);
        }

        // ============================================================
        // 查询图号:按 drawingType + drawNumber 查 C0X 表,返回匹配项
        // ============================================================
        public async Task<DrawingNumberViewModel?> FindDrawingNumberByTypeAndNumberAsync(string drawingType, int drawingNumber)
        {
            if (string.IsNullOrEmpty(drawingType))
            {
                return null;
            }

            var handler = _registry.Resolve(drawingType);
            if (handler == null) return null;

            try
            {
                var c0x = await handler.FindByDrawNumberAsync(_db, drawingNumber);
                if (c0x == null) return null;
                return await DrawingNumber2DrawingNumberViewModelAsync(c0x, drawingType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FindDrawingNumberByTypeAndNumberAsync failed: {Type} {Number}", drawingType, drawingNumber);
                return null;
            }
        }

        // ============================================================
        // 图名(基础数据)管理
        // ============================================================

        /// <summary>
        /// 查询所有图名(包括已删除的),用于"图名列表"管理页面
        /// </summary>
        public async Task<List<DrawingNames>> GetAllDrawingNamesIncludingDeletedAsync()
        {
            return await _db.Queryable<DrawingNames>()
                .OrderBy(dn => dn.Id)
                .ToListAsync();
        }

        /// <summary>
        /// 查询所有不重复的 Part 类别(去重 + 排序 + 过滤空值)
        /// 供"添加图名"弹窗下拉框使用,自动跟库里的 Part 同步
        /// </summary>
        public async Task<List<string>> GetDistinctPartsAsync()
        {
            var list = await _db.Queryable<DrawingNames>()
                .Where(dn => !string.IsNullOrWhiteSpace(dn.Part))
                .Select(dn => dn.Part)
                .Distinct()
                .ToListAsync();

            // SqlSugar 的 Distinct 在 List<string> 上可能不生效,做一遍内存去重兜底
            return list
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 新增图名
        /// </summary>
        public async Task<int> AddDrawingNameAsync(string componentName, string part)
        {
            if (string.IsNullOrWhiteSpace(componentName) || string.IsNullOrWhiteSpace(part))
            {
                _logger.LogWarning("AddDrawingNameAsync: invalid input, ComponentName='{Name}', Part='{Part}'", componentName, part);
                return 0;
            }

            var currentUserName = _currentUser.GetDisplayName() ?? "System";

            var newName = new DrawingNames
            {
                Part = part,
                ComponentName = componentName.Trim(),
                CreatedBy = currentUserName,
                CreatedTime = DateTime.Now,
                UpdatedBy = currentUserName,
                UpdatedTime = DateTime.Now,
                Isdelete = false
            };

            try
            {
                var insertedId = await _db.Insertable(newName).ExecuteReturnIdentityAsync();
                _logger.LogInformation("Added drawing name: {Name} (Part={Part}) by {User}", componentName, part, currentUserName);
                return insertedId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddDrawingNameAsync failed: {Name} {Part}", componentName, part);
                return 0;
            }
        }

        /// <summary>
        /// 软删除图名(Isdelete=true)
        /// </summary>
        public async Task<bool> DeleteDrawingNameAsync(int id)
        {
            try
            {
                string userName = _currentUser.GetDisplayName() ?? "System";

                var result = await _db.Updateable<DrawingNames>()
                    .SetColumns(it => new DrawingNames
                    {
                        Isdelete = true,
                        UpdatedBy = userName,
                        UpdatedTime = DateTime.Now
                    })
                    .Where(it => it.Id == id)
                    .ExecuteCommandAsync();

                if (result > 0)
                {
                    _logger.LogInformation("Soft-deleted drawing name id={Id} by {User}", id, userName);
                }
                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteDrawingNameAsync failed: {Id}", id);
                return false;
            }
        }
    }
}