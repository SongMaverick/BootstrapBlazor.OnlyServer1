using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Helpers;

namespace BootstrapBlazor.OnlyServer1.Services
{
    public class DrawingNumberService
    {
        private readonly ISqlSugarClient _db;
        private readonly AuthService _authService;
        private readonly ILogger<DrawingNumberService> _logger;

        public DrawingNumberService(ISqlSugarClient db, AuthService authService, ILogger<DrawingNumberService> logger)
        {
            _db = db;
            _authService = authService;
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
            var result = new List<DrawingNumberViewModel>();

            foreach (var c0x in c0xList)
            {
                var vm = await DrawingNumber2DrawingNumberViewModelAsync(c0x);
                result.Add(vm);
            }

            return result;
        }

        public async Task<List<C0XBase>> GetAddedDrawingNumbersByOrderIdAsync(int orderId)
        {
            var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                return new List<C0XBase>();
            }

            var drawingType = MyHelper.GetDrawingType(order);
            if (string.IsNullOrEmpty(drawingType))
            {
                _logger.LogWarning("Could not determine drawing type for order: {OrderId}", orderId);
                return new List<C0XBase>();
            }

            var result = new List<C0XBase>();

            switch (drawingType)
            {
                case "C01":
                    var c01List = await _db.Queryable<C01>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c01List);
                    break;
                case "C02":
                    var c02List = await _db.Queryable<C02>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c02List);
                    break;
                case "C03":
                    var c03List = await _db.Queryable<C03>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c03List);
                    break;
                case "C04":
                    var c04List = await _db.Queryable<C04>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c04List);
                    break;
                case "C05":
                    var c05List = await _db.Queryable<C05>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c05List);
                    break;
                case "C06":
                    var c06List = await _db.Queryable<C06>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c06List);
                    break;
                case "C07":
                    var c07List = await _db.Queryable<C07>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c07List);
                    break;
                case "C08":
                    var c08List = await _db.Queryable<C08>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c08List);
                    break;
                case "C09":
                    var c09List = await _db.Queryable<C09>().Where(n => n.OrderId == orderId && !n.IsDelete).ToListAsync();
                    result.AddRange(c09List);
                    break;
            }

            return result;
        }

        public async Task<int> AddDrawingNumbersAsync(int orderId, string[] drawingNames)
        {
            var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                return 0;
            }

            var drawingType = MyHelper.GetDrawingType(order);
            if (string.IsNullOrEmpty(drawingType))
            {
                _logger.LogWarning("Could not determine drawing type for order: {OrderId}", orderId);
                return 0;
            }

            var currentUser = _authService.CurrentUser;
            if (currentUser == null)
            {
                _logger.LogWarning("Current user not found");
                return 0;
            }

            var successCount = 0;

            foreach (var drawingName in drawingNames)
            {
                try
                {
                    var added = await AddSingleDrawingNumberAsync(order, drawingType, drawingName, currentUser);
                    if (added)
                    {
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding drawing number: {DrawingName}", drawingName);
                }
            }

            return successCount;
        }

        private async Task<bool> AddSingleDrawingNumberAsync(Orders order, string drawingType, string drawingName, Users currentUser)
        {
            var c0x = new C0XBase
            {
                DrawName = drawingName,
                DrawingType = drawingType,
                OrderId = order.Id,
                CreatedTime = DateTime.Now,
                UpdatedTime = DateTime.Now,
                CreatedBy = currentUser.Name,
                UpdatedBy = currentUser.Name,
                IsDelete = false
            };

            int insertedId = 0;

            switch (drawingType)
            {
                case "C01":
                    var c01 = new C01
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c01).ExecuteReturnIdentityAsync();
                    break;
                case "C02":
                    var c02 = new C02
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c02).ExecuteReturnIdentityAsync();
                    break;
                case "C03":
                    var c03 = new C03
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c03).ExecuteReturnIdentityAsync();
                    break;
                case "C04":
                    var c04 = new C04
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c04).ExecuteReturnIdentityAsync();
                    break;
                case "C05":
                    var c05 = new C05
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c05).ExecuteReturnIdentityAsync();
                    break;
                case "C06":
                    var c06 = new C06
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c06).ExecuteReturnIdentityAsync();
                    break;
                case "C07":
                    var c07 = new C07
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c07).ExecuteReturnIdentityAsync();
                    break;
                case "C08":
                    var c08 = new C08
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c08).ExecuteReturnIdentityAsync();
                    break;
                case "C09":
                    var c09 = new C09
                    {
                        DrawName = c0x.DrawName,
                        DrawingType = c0x.DrawingType,
                        OrderId = c0x.OrderId,
                        CreatedTime = c0x.CreatedTime,
                        UpdatedTime = c0x.UpdatedTime,
                        CreatedBy = c0x.CreatedBy,
                        UpdatedBy = c0x.UpdatedBy,
                        IsDelete = c0x.IsDelete
                    };
                    insertedId = await _db.Insertable(c09).ExecuteReturnIdentityAsync();
                    break;
            }

            if (insertedId > 0)
            {
                _logger.LogInformation("User {UserName} added drawing number {DrawingName} to order {OrderNumber}", currentUser.Name, drawingName, order.OrderNumber);
            }

            return insertedId > 0;
        }

        public async Task<DrawingNumberViewModel> DrawingNumber2DrawingNumberViewModelAsync(C0XBase c0x)
        {
            var vm = new DrawingNumberViewModel
            {
                Id = c0x.Id,
                DrawingType = c0x.DrawingType,
                DrawName = c0x.DrawName,
                CreatedBy = c0x.CreatedBy,
                CreatedTime = c0x.CreatedTime,
                UpdatedBy = c0x.UpdatedBy,
                UpdatedTime = c0x.UpdatedTime,
                IsDelete = c0x.IsDelete,
                OrderId = c0x.OrderId
            };

            int beginDrawingNumber = MyHelper.GetMinDrawingNumber(c0x.DrawingType);
            int end5 = beginDrawingNumber + c0x.Id;
            string tempDrawingNumber = end5.ToString();
            if (tempDrawingNumber.Length < 5)
            {
                int loopTime = 5 - tempDrawingNumber.Length;
                for (int i = 0; i < loopTime; i++)
                {
                    tempDrawingNumber = "0" + tempDrawingNumber;
                }
            }
            vm.DrawingTypeNumber =
                c0x.DrawingType + " " + tempDrawingNumber.Substring(0, 2) + " " +
                tempDrawingNumber.Substring(2, 3);

            var order = await _db.Queryable<Orders>().InSingleAsync(c0x.OrderId);
            vm.OrderNumber = order.OrderNumber;

            return vm;
        }

        public async Task<C0XBase?> FindDrawingNumberByIdAsync(int id, string drawingType)
        {
            switch (drawingType)
            {
                case "C01":
                    return await _db.Queryable<C01>().InSingleAsync(id);
                case "C02":
                    return await _db.Queryable<C02>().InSingleAsync(id);
                case "C03":
                    return await _db.Queryable<C03>().InSingleAsync(id);
                case "C04":
                    return await _db.Queryable<C04>().InSingleAsync(id);
                case "C05":
                    return await _db.Queryable<C05>().InSingleAsync(id);
                case "C06":
                    return await _db.Queryable<C06>().InSingleAsync(id);
                case "C07":
                    return await _db.Queryable<C07>().InSingleAsync(id);
                case "C08":
                    return await _db.Queryable<C08>().InSingleAsync(id);
                case "C09":
                    return await _db.Queryable<C09>().InSingleAsync(id);
                default:
                    return null;
            }
        }

        public async Task<int> UpdateCX0DrawingNumberByIdAsync(C0XBase c0x, string drawingType)
        {
            switch (drawingType)
            {
                case "C01":
                    return await _db.Updateable<C01>()
                        .SetColumns(it => new C01 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C02":
                    return await _db.Updateable<C02>()
                        .SetColumns(it => new C02 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C03":
                    return await _db.Updateable<C03>()
                        .SetColumns(it => new C03 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C04":
                    return await _db.Updateable<C04>()
                        .SetColumns(it => new C04 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C05":
                    return await _db.Updateable<C05>()
                        .SetColumns(it => new C05 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C06":
                    return await _db.Updateable<C06>()
                        .SetColumns(it => new C06 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C07":
                    return await _db.Updateable<C07>()
                        .SetColumns(it => new C07 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C08":
                    return await _db.Updateable<C08>()
                        .SetColumns(it => new C08 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                case "C09":
                    return await _db.Updateable<C09>()
                        .SetColumns(it => new C09 { DrawName = c0x.DrawName, UpdatedBy = c0x.UpdatedBy, UpdatedTime = c0x.UpdatedTime })
                        .Where(it => it.Id == c0x.Id)
                        .ExecuteCommandAsync();
                default:
                    return 0;
            }
        }

        // ============================================================
        // 查询图号:按 "前三位 + 后五位" 查 C0X 表,返回第一个匹配的 ViewModel
        // ============================================================
        public async Task<DrawingNumberViewModel?> FindDrawingNumberByTypeAndNumberAsync(string drawingType, int drawingNumber)
        {
            if (string.IsNullOrEmpty(drawingType))
            {
                return null;
            }

            int targetId = drawingNumber - MyHelper.GetMinDrawingNumber(drawingType);

            // targetId 必须 >= 1(Id 是自增主键从 1 起)
            if (targetId < 1)
            {
                return null;
            }

            try
            {
                C0XBase? c0x = drawingType switch
                {
                    "C01" => await _db.Queryable<C01>().InSingleAsync(targetId),
                    "C02" => await _db.Queryable<C02>().InSingleAsync(targetId),
                    "C03" => await _db.Queryable<C03>().InSingleAsync(targetId),
                    "C04" => await _db.Queryable<C04>().InSingleAsync(targetId),
                    "C05" => await _db.Queryable<C05>().InSingleAsync(targetId),
                    "C06" => await _db.Queryable<C06>().InSingleAsync(targetId),
                    "C07" => await _db.Queryable<C07>().InSingleAsync(targetId),
                    "C08" => await _db.Queryable<C08>().InSingleAsync(targetId),
                    "C09" => await _db.Queryable<C09>().InSingleAsync(targetId),
                    _ => null
                };

                if (c0x == null)
                {
                    return null;
                }

                // 反算 DrawNumber 校验(防止非主键路径出错)
                int verifyNumber = MyHelper.GetMinDrawingNumber(drawingType) + c0x.Id;
                if (verifyNumber != drawingNumber)
                {
                    return null;
                }

                return await DrawingNumber2DrawingNumberViewModelAsync(c0x);
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
        /// 新增图名
        /// </summary>
        public async Task<int> AddDrawingNameAsync(string componentName, string part)
        {
            if (string.IsNullOrWhiteSpace(componentName) || string.IsNullOrWhiteSpace(part))
            {
                _logger.LogWarning("AddDrawingNameAsync: invalid input, ComponentName='{Name}', Part='{Part}'", componentName, part);
                return 0;
            }

            var currentUser = _authService.CurrentUser;
            string userName = currentUser?.Name ?? "System";

            var newName = new DrawingNames
            {
                Part = part,
                ComponentName = componentName.Trim(),
                CreatedBy = userName,
                CreatedTime = DateTime.Now,
                UpdatedBy = userName,
                UpdatedTime = DateTime.Now,
                Isdelete = false
            };

            try
            {
                var insertedId = await _db.Insertable(newName).ExecuteReturnIdentityAsync();
                _logger.LogInformation("Added drawing name: {Name} (Part={Part}) by {User}", componentName, part, userName);
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
                var currentUser = _authService.CurrentUser;
                string userName = currentUser?.Name ?? "System";

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
