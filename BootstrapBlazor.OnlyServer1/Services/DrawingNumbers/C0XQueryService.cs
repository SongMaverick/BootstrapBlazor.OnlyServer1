using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.ViewModels;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;

/// <summary>
/// 9 张 C0X 表(C01~C09)的只读查询 + ViewModel 映射。
/// 无状态:纯读,不带锁、不写入。可以高度并发。
/// Scoped 生命周期(为日后按用户加缓存留余地)。
/// </summary>
public class C0XQueryService
{
    private readonly ISqlSugarClient _db;
    private readonly IDrawingTypeRegistry _registry;
    private readonly ILogger<C0XQueryService> _logger;

    public C0XQueryService(
        ISqlSugarClient db,
        IDrawingTypeRegistry registry,
        ILogger<C0XQueryService> logger)
    {
        _db = db;
        _registry = registry;
        _logger = logger;
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
        // (2026-07-08 Review #L-7) c0x.DrawNumber/DrawName → DrawingNumber/DrawingName
        string tempDrawingNumber = c0x.DrawingNumber.ToString("D5");

        return new DrawingNumberViewModel
        {
            Id = c0x.Id,
            DrawingType = drawingType,
            DrawingName = c0x.DrawingName,
            CreatedBy = c0x.CreatedBy,
            CreatedTime = c0x.CreatedTime,
            UpdatedBy = c0x.UpdatedBy,
            UpdatedTime = c0x.UpdatedTime,
            IsDeleted = c0x.IsDeleted,
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

    /// <summary>
    /// 按 drawingType + drawNumber 查 C0X 表,返回匹配项。
    /// </summary>
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
            var c0x = await handler.FindByDrawingNumberAsync(_db, drawingNumber);
            if (c0x == null) return null;
            return await DrawingNumber2DrawingNumberViewModelAsync(c0x, drawingType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindDrawingNumberByTypeAndNumberAsync failed: {Type} {Number}", drawingType, drawingNumber);
            return null;
        }
    }
}
