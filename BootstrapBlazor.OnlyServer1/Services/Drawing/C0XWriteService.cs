using System.Diagnostics;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Services.OrderManagement;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.Drawing;

/// <summary>
/// 9 张 C0X 表(C01~C09)的写入逻辑。
/// 包含:
///   - 订单级串行化(OrderLockProvider 同订单间互斥)
///   - DrawingNumber 原子分配(IDrawingNumberAllocator)
///   - 当前用户写入鉴权(CurrentUserAccessor)
/// Scoped 生命周期。
/// (2026-07-08 Review #L-7) 注释 DrawNumber → DrawingNumber
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
        // ====================================================================
        // 纵深防御(2026-07-07 Review #H1):
        // 加图号是业务操作:Admin/Operator。
        // 不删这段校验、不删这个注释。
        // ====================================================================
        var (permOk, permErr) = _currentUser.RequireRole(BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Admin,
                                                         BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Operator);
        if (!permOk)
        {
            _logger.LogWarning(
                "C0XWriteService.AddDrawingNumbersAsync denied: callerId={CallerId} orderId={OrderId} reason={Reason}",
                _currentUser.GetUserId(), orderId, permErr);
            var deniedResult = new DrawingNumberInsertResult();
            foreach (var n in drawingNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal) ?? Enumerable.Empty<string>())
                deniedResult.Failures.Add(new(n, "无权操作"));
            return deniedResult;
        }

        var result = new DrawingNumberInsertResult();
        var sw = Stopwatch.StartNew();
        if (drawingNames == null || drawingNames.Length == 0)
        {
            sw.Stop();
            _logger.LogInformation(
                "AddDrawingNumbersAsync 跳过(无输入): callerId={CallerId} orderId={OrderId} elapsedMs={ElapsedMs}",
                _currentUser.GetUserId(), orderId, sw.ElapsedMilliseconds);
            return result;
        }

        var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
        // (2026-08-06)Service 层"Order not found"日志统一由 Razor 入口层
        // (OrderDrawingNumbers.razor / AddDrawingNumbers.razor)打,避免重复。
        // 这里是 WriteService,被 Razor 入口层直接调用 — 入口层一打就够了。
        if (order == null)
        {
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

        sw.Stop();
        _logger.LogInformation(
            "AddDrawingNumbersAsync 完成: callerId={CallerId} orderId={OrderId} type={Type} success={Success} fail={Fail} elapsedMs={ElapsedMs}",
            _currentUser.GetUserId(), orderId, drawingType, result.Success.Count, result.Failures.Count, sw.ElapsedMilliseconds);

        return result;
    }

    public async Task<int> UpdateCX0DrawingNumberByIdAsync(C0XBase c0x, string drawingType)
    {
        // ====================================================================
        // 纵深防御(2026-07-07 Review #H1):
        // 改图号:Admin/Operator。
        // 不删这段校验、不删这个注释。
        // ====================================================================
        var (ok, err) = _currentUser.RequireRole(BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Admin,
                                                BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Operator);
        if (!ok)
        {
            _logger.LogWarning(
                "C0XWriteService.UpdateCX0DrawingNumberByIdAsync denied: callerId={CallerId} c0xId={Id} reason={Reason}",
                _currentUser.GetUserId(), c0x?.Id, err);
            return 0;
        }

        var handler = _registry.Resolve(drawingType);
        if (handler == null) return 0;
        return await handler.UpdateDrawingNameAsync(
            _db, c0x.Id, c0x.DrawingName, c0x.UpdatedBy, c0x.UpdatedTime);
    }

    // 锁内辅助:拉取该订单当前所有未删除的 DrawingName(通过 registry handler)
    // (2026-07-08 Review #L-7) GetExistingDrawNamesAsync → GetExistingDrawingNamesAsync
    private async Task<HashSet<string>> GetExistingDrawingNamesAsync(
        int orderId, string drawingType, CancellationToken ct)
    {
        var handler = _registry.Resolve(drawingType);
        if (handler == null) return new HashSet<string>();
        var names = await handler.GetExistingDrawingNamesAsync(_db, orderId);
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

        // 先原子分配 DrawingNumber,再写入
        c0x.DrawingNumber = await _allocator.AllocateAsync(drawingType);
        int insertedId = await handler.InsertAsync(_db, c0x);

        if (insertedId > 0)
        {
            _logger.LogInformation("User {UserName} added drawing number {DrawingName} (DrawingNumber={DrawingNumber}) to order {OrderNumber}",
                currentUserName, drawingName, c0x.DrawingNumber, order.OrderNumber);
        }

        return insertedId;
    }
}
