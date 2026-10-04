using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Models.ViewModels;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.OrderManagement;

public class OrderTypeService
{
    private static readonly HashSet<string> ValidTypes = new(StringComparer.Ordinal)
    {
        "相同", "相似", "全新"
    };

    private readonly ISqlSugarClient _db;
    private readonly CurrentUserAccessor _currentUser;
    private readonly ILogger<OrderTypeService> _logger;

    public OrderTypeService(ISqlSugarClient db, CurrentUserAccessor currentUser, ILogger<OrderTypeService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<OrderTypeRow> GetByOrderIdAsync(int orderId)
    {
        try
        {
            var row = await _db.Queryable<Orders>()
                .LeftJoin<OrderTypeInfo>((o, t) => o.Id == t.OrderId && !t.IsDeleted)
                .Where((o, t) => o.Id == orderId && !o.IsDeleted)
                .Select((o, t) => new OrderTypeRow
                {
                    TypeId = t.Id,
                    OrderId = o.Id,
                    OrderType = t.OrderType,
                    ReferenceOrderNumber = t.ReferenceOrderNumber,
                })
                .FirstAsync();

            if (row == null || string.IsNullOrWhiteSpace(row.OrderType))
            {
                return new OrderTypeRow { OrderId = orderId };
            }

            return row;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order type for order {OrderId}", orderId);
            return new OrderTypeRow { OrderId = orderId };
        }
    }

    public async Task<(bool ok, string? error)> SaveAsync(OrderTypeRow row)
    {
        var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
        if (!authOk) return (false, authErr);
        if (row.OrderId <= 0) return (false, "订单不存在");
        if (!ValidTypes.Contains(row.OrderType)) return (false, "订单类型无效");

        var requiresReference = row.OrderType is "相同" or "相似";
        row.ReferenceOrderNumber = string.IsNullOrWhiteSpace(row.ReferenceOrderNumber)
            ? null
            : row.ReferenceOrderNumber.Trim();
        if (requiresReference && string.IsNullOrWhiteSpace(row.ReferenceOrderNumber))
            return (false, "选择相同或相似时必须填写参考订单号");
        if (!requiresReference)
        {
            row.ReferenceOrderNumber = null;
        }

        try
        {
            var orderExists = await _db.Queryable<Orders>()
                .Where(o => o.Id == row.OrderId && !o.IsDeleted)
                .AnyAsync();
            if (!orderExists) return (false, "订单不存在或已被删除");

            var operatorName = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
            var now = DateTime.Now;
            if (row.TypeId > 0)
            {
                await _db.Updateable<OrderTypeInfo>()
                    .SetColumns(t => new OrderTypeInfo
                    {
                        OrderType = row.OrderType,
                        ReferenceOrderNumber = row.ReferenceOrderNumber,
                        UpdatedBy = operatorName,
                        UpdatedTime = now,
                    })
                    .Where(t => t.Id == row.TypeId && !t.IsDeleted)
                    .ExecuteCommandAsync();
            }
            else
            {
                await _db.Insertable(new OrderTypeInfo
                {
                    OrderId = row.OrderId,
                    OrderType = row.OrderType,
                    ReferenceOrderNumber = row.ReferenceOrderNumber,
                    CreatedBy = operatorName,
                    CreatedTime = now,
                    UpdatedBy = operatorName,
                    UpdatedTime = now,
                }).ExecuteCommandAsync();
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving order type for order {OrderId}", row.OrderId);
            if (IsMissingOrderTypeSchema(ex))
            {
                return (false, "订单类型数据表或老系统参考订单字段尚未创建，请先运行 OrderManagementSetup --install");
            }
            return (false, "保存订单类型失败");
        }
    }

    private static bool IsMissingOrderTypeSchema(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("Invalid object name 'OrderTypes'", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Invalid column name 'ReferenceOrderNumber'", StringComparison.OrdinalIgnoreCase);
    }
}
