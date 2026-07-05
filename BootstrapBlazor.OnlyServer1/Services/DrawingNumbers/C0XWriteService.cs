using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;

/// <summary>
/// 9 张 C0X 表(C01~C09)的写入逻辑。
/// 包含:
///   - 订单级串行化(OrderLockProvider 同订单间互斥)
///   - DrawNumber 原子分配(IDrawingNumberAllocator)
///   - 当前用户写入鉴权(CurrentUserAccessor)
/// Scoped 生命周期。
/// </summary>
public class C0XWriteService
{
    private readonly ISqlSugarClient _db;
    private readonly IDrawingTypeRegistry _registry;
    private readonly IDrawingNumberAllocator _allocator;
    private readonly OrderLockProvider _orderLock;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<C0XWriteService> _logger;

    public C0XWriteService(
        ISqlSugarClient db,
        IDrawingTypeRegistry registry,
        IDrawingNumberAllocator allocator,
        OrderLockProvider orderLock,
        CurrentUserAccessor currentUser,
        ILogger<C0XWriteService> logger)
    {
        _db = db;
        _registry = registry;
        _allocator = allocator;
        _orderLock = orderLock;
        _currentUser = currentUser;
        _logger = logger;
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

    public async Task<int> UpdateCX0DrawingNumberByIdAsync(C0XBase c0x, string drawingType)
    {
        var handler = _registry.Resolve(drawingType);
        if (handler == null) return 0;
        return await handler.UpdateDrawNameAsync(
            _db, c0x.Id, c0x.DrawName, c0x.UpdatedBy, c0x.UpdatedTime);
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
}
