using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;
using System.Text.RegularExpressions;

namespace BootstrapBlazor.OnlyServer1.Data
{
    public class OrdersItemImport
    {
        private readonly ILogger<OrdersItemImport> _logger;

        public OrdersItemImport(ILogger<OrdersItemImport> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 从 Order.sql 和 ReactorOrder.sql 导入订单映射关系
        /// </summary>
        public async Task ImportOrdersItemMapping(ISqlSugarClient db, string orderSqlFilePath, string reactorOrderSqlFilePath)
        {
            _logger.LogInformation("开始导入订单映射关系...");

            try
            {
                // 读取普通订单数据
                var normalOrders = new List<(int OldOrderId, int OrderNumber)>();
                if (File.Exists(orderSqlFilePath))
                {
                    var orderSqlContent = File.ReadAllText(orderSqlFilePath);
                    normalOrders = ParseOrderSqlFile(orderSqlContent);
                    _logger.LogInformation("从 Order.sql 解析到 {Count} 个订单", normalOrders.Count);
                }
                else
                {
                    _logger.LogWarning("Order.sql 文件不存在：{FilePath}", orderSqlFilePath);
                }

                // 读取电抗器订单数据
                var reactorOrders = new List<(int OldOrderId, int OrderNumber)>();
                if (File.Exists(reactorOrderSqlFilePath))
                {
                    var reactorSqlContent = File.ReadAllText(reactorOrderSqlFilePath);
                    reactorOrders = ParseOrderSqlFile(reactorSqlContent);
                    _logger.LogInformation("从 ReactorOrder.sql 解析到 {Count} 个电抗器订单", reactorOrders.Count);
                }
                else
                {
                    _logger.LogWarning("ReactorOrder.sql 文件不存在：{FilePath}", reactorOrderSqlFilePath);
                }

                // 获取数据库中所有订单的 Id 和 OrderNumber 映射
                var orderMappings = await db.Queryable<Orders>()
                    .Select(o => new { o.Id, o.OrderNumber, o.IsReactor })
                    .ToListAsync();

                var dbOrderNumberToInfo = orderMappings.ToDictionary(
                    o => o.OrderNumber, 
                    o => new { o.Id, o.IsReactor }
                );

                _logger.LogInformation("数据库中共有 {Count} 个订单", dbOrderNumberToInfo.Count);

                // 构建映射关系
                var ordersItemList = new List<OrdersItem>();
                var mappedCount = 0;
                var skippedCount = 0;
                var notFoundCount = 0;

                // 处理普通订单（IsReactor = false）
                foreach (var (oldOrderId, orderNumber) in normalOrders)
                {
                    if (dbOrderNumberToInfo.TryGetValue(orderNumber, out var orderInfo))
                    {
                        // 检查是否已存在映射
                        var existingMapping = await db.Queryable<OrdersItem>()
                            .Where(oi => oi.OldOrderId == oldOrderId && oi.NewOrderId == orderInfo.Id)
                            .FirstAsync();

                        if (existingMapping == null)
                        {
                            ordersItemList.Add(new OrdersItem
                            {
                                OldOrderId = oldOrderId,
                                NewOrderId = orderInfo.Id,
                                IsReactor = false,
                                CreatedTime = DateTime.Now
                            });
                            mappedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    else
                    {
                        notFoundCount++;
                        _logger.LogWarning("数据库中未找到订单号：{OrderNumber}", orderNumber);
                    }
                }

                // 处理电抗器订单（IsReactor = true）
                foreach (var (oldOrderId, orderNumber) in reactorOrders)
                {
                    if (dbOrderNumberToInfo.TryGetValue(orderNumber, out var orderInfo))
                    {
                        // 检查是否已存在映射
                        var existingMapping = await db.Queryable<OrdersItem>()
                            .Where(oi => oi.OldOrderId == oldOrderId && oi.NewOrderId == orderInfo.Id)
                            .FirstAsync();

                        if (existingMapping == null)
                        {
                            ordersItemList.Add(new OrdersItem
                            {
                                OldOrderId = oldOrderId,
                                NewOrderId = orderInfo.Id,
                                IsReactor = true,
                                CreatedTime = DateTime.Now
                            });
                            mappedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    else
                    {
                        notFoundCount++;
                        _logger.LogWarning("数据库中未找到电抗器订单号：{OrderNumber}", orderNumber);
                    }
                }

                // 批量插入映射数据
                if (ordersItemList.Count > 0)
                {
                    _logger.LogInformation("准备插入 {Count} 条订单映射数据", ordersItemList.Count);
                    await db.Insertable(ordersItemList).ExecuteCommandAsync();
                    _logger.LogInformation("订单映射数据导入成功：{Count} 条", ordersItemList.Count);
                }
                else
                {
                    _logger.LogInformation("没有新的订单映射数据需要导入");
                }

                _logger.LogInformation("订单映射关系导入完成！成功导入：{MappedCount} 条，跳过：{SkippedCount} 条，未找到：{NotFoundCount} 条", 
                    mappedCount, skippedCount, notFoundCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入订单映射关系时发生错误：{Message}", ex.Message);
            }
        }

        /// <summary>
        /// 解析SQL文件中的订单信息（OldOrderId 和 OrderNumber）
        /// </summary>
        private List<(int OldOrderId, int OrderNumber)> ParseOrderSqlFile(string sqlContent)
        {
            var orders = new List<(int OldOrderId, int OrderNumber)>();

            // 匹配 INSERT INTO 语句中的 Id 和 OrderNumber 值
            // 格式：INSERT INTO "Orders" (Id, OrderNumber, ...) VALUES (1, 123456, ...)
            // 或：INSERT INTO ReactorOrder (Id, OrderNumber, ...) VALUES (51, 813001, ...)
            var pattern = @"INSERT INTO\s+(?:""?Orders""?|ReactorOrder)\s*\(\s*Id\s*,\s*OrderNumber\s*,[^)]+\)\s*VALUES\s*\(\s*(\d+)\s*,\s*(\d+)\s*,";
            var matches = Regex.Matches(sqlContent, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var oldOrderId = int.Parse(match.Groups[1].Value);
                    var orderNumber = int.Parse(match.Groups[2].Value);
                    orders.Add((oldOrderId, orderNumber));
                }
            }

            return orders;
        }
    }
}
