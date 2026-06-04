using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;
using System.Text.RegularExpressions;

namespace BootstrapBlazor.OnlyServer1.Data
{
    public class OrderDataImport
    {
        private readonly ILogger<OrderDataImport> _logger;

        public OrderDataImport(ILogger<OrderDataImport> logger)
        {
            _logger = logger;
        }

        public async Task ImportOrdersFromSqlFile(ISqlSugarClient db, string sqlFilePath)
        {
            _logger.LogInformation("开始从SQL文件导入订单数据...");

            try
            {
                if (!File.Exists(sqlFilePath))
                {
                    _logger.LogError("SQL文件不存在：{FilePath}", sqlFilePath);
                    return;
                }

                var sqlContent = File.ReadAllText(sqlFilePath);
                var orders = ParseSqlInsertStatements(sqlContent);

                _logger.LogInformation("解析到 {Count} 条订单数据", orders.Count);

                var importedCount = 0;
                var skippedCount = 0;

                // 一次性检查已存在的订单编号（OrderNumber）
                var existingOrders = await db.Queryable<Orders>().Select(o => new { o.OrderNumber, o.IsReactor }).ToListAsync();
                var existingOrderDict = existingOrders.ToDictionary(o => o.OrderNumber, o => o.IsReactor);

                var ordersToInsert = new List<Orders>();
                var ordersToUpdate = new List<Orders>();

                foreach (var order in orders)
                {
                    if (existingOrderDict.ContainsKey(order.OrderNumber))
                    {
                        // 订单已存在，检查IsReactor是否正确
                        if (!existingOrderDict[order.OrderNumber])
                        {
                            // IsReactor不正确，需要更新
                            order.Id = db.Queryable<Orders>().Where(o => o.OrderNumber == order.OrderNumber).Select(o => o.Id).First();
                            ordersToUpdate.Add(order);
                            _logger.LogInformation("更新订单：OrderNumber={OrderNumber}, IsReactor将被设置为True", order.OrderNumber);
                        }
                        else
                        {
                            _logger.LogInformation("跳过订单：OrderNumber={OrderNumber}, IsReactor已经是True", order.OrderNumber);
                        }
                        skippedCount++;
                    }
                    else
                    {
                        // 订单不存在，需要添加
                        ordersToInsert.Add(order);
                    }
                }

                // 批量插入新订单
                if (ordersToInsert.Count > 0)
                {
                    _logger.LogInformation("准备导入 {Count} 条新订单数据", ordersToInsert.Count);
                    await db.Insertable(ordersToInsert).ExecuteCommandAsync();
                    importedCount = ordersToInsert.Count;
                    _logger.LogInformation("批量导入成功：{ImportedCount} 条订单数据", importedCount);
                }
                else
                {
                    _logger.LogInformation("没有新订单数据需要导入");
                }

                // 批量更新IsReactor
                if (ordersToUpdate.Count > 0)
                {
                    _logger.LogInformation("准备更新 {Count} 条订单的IsReactor字段", ordersToUpdate.Count);
                    foreach (var order in ordersToUpdate)
                    {
                        await db.Updateable<Orders>()
                            .SetColumns(o => o.IsReactor == true)
                            .Where(o => o.Id == order.Id)
                            .ExecuteCommandAsync();
                    }
                    _logger.LogInformation("批量更新成功：{UpdatedCount} 条订单的IsReactor字段", ordersToUpdate.Count);
                }

                _logger.LogInformation("订单数据导入完成！成功导入：{ImportedCount} 条，更新：{UpdatedCount} 条，跳过：{SkippedCount} 条", 
                    importedCount, ordersToUpdate.Count, skippedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入订单数据时发生错误：{Message}", ex.Message);
            }
        }

        public async Task ImportReactorOrdersFromSqlFile(ISqlSugarClient db, string sqlFilePath)
        {
            _logger.LogInformation("开始从ReactorOrder.sql文件导入电抗器订单数据...");

            try
            {
                if (!File.Exists(sqlFilePath))
                {
                    _logger.LogError("SQL文件不存在：{FilePath}", sqlFilePath);
                    return;
                }

                var sqlContent = File.ReadAllText(sqlFilePath);
                var reactorOrders = ParseReactorSqlInsertStatements(sqlContent);

                _logger.LogInformation("解析到 {Count} 条电抗器订单数据", reactorOrders.Count);

                var importedCount = 0;
                var updatedCount = 0;
                var skippedCount = 0;

                // 一次性检查已存在的订单编号（OrderNumber）
                var existingOrders = await db.Queryable<Orders>().Select(o => new { o.OrderNumber, o.IsReactor }).ToListAsync();
                var existingOrderDict = existingOrders.ToDictionary(o => o.OrderNumber, o => o.IsReactor);

                var ordersToInsert = new List<Orders>();
                var ordersToUpdate = new List<Orders>();

                foreach (var order in reactorOrders)
                {
                    if (existingOrderDict.ContainsKey(order.OrderNumber))
                    {
                        // 订单已存在，检查IsReactor是否已经是True
                        if (!existingOrderDict[order.OrderNumber])
                        {
                            // IsReactor是False，需要更新为True
                            order.Id = db.Queryable<Orders>().Where(o => o.OrderNumber == order.OrderNumber).Select(o => o.Id).First();
                            ordersToUpdate.Add(order);
                            _logger.LogInformation("更新订单：OrderNumber={OrderNumber}, IsReactor将被设置为True", order.OrderNumber);
                        }
                        else
                        {
                            _logger.LogInformation("跳过订单：OrderNumber={OrderNumber}, IsReactor已经是True", order.OrderNumber);
                        }
                        skippedCount++;
                    }
                    else
                    {
                        // 订单不存在，需要添加
                        order.IsReactor = true; // 确保IsReactor为True
                        ordersToInsert.Add(order);
                    }
                }

                // 批量插入新订单
                if (ordersToInsert.Count > 0)
                {
                    _logger.LogInformation("准备导入 {Count} 条新电抗器订单数据", ordersToInsert.Count);
                    await db.Insertable(ordersToInsert).ExecuteCommandAsync();
                    importedCount = ordersToInsert.Count;
                    _logger.LogInformation("批量导入成功：{ImportedCount} 条电抗器订单数据", importedCount);
                }
                else
                {
                    _logger.LogInformation("没有新电抗器订单数据需要导入");
                }

                // 批量更新IsReactor为True
                if (ordersToUpdate.Count > 0)
                {
                    _logger.LogInformation("准备更新 {Count} 条订单的IsReactor字段为True", ordersToUpdate.Count);
                    foreach (var order in ordersToUpdate)
                    {
                        await db.Updateable<Orders>()
                            .SetColumns(o => o.IsReactor == true)
                            .Where(o => o.Id == order.Id)
                            .ExecuteCommandAsync();
                    }
                    updatedCount = ordersToUpdate.Count;
                    _logger.LogInformation("批量更新成功：{UpdatedCount} 条订单的IsReactor字段", updatedCount);
                }

                _logger.LogInformation("电抗器订单数据导入完成！成功导入：{ImportedCount} 条，更新：{UpdatedCount} 条，跳过：{SkippedCount} 条", 
                    importedCount, updatedCount, skippedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入电抗器订单数据时发生错误：{Message}", ex.Message);
            }
        }

        private List<Orders> ParseSqlInsertStatements(string sqlContent)
        {
            var orders = new List<Orders>();

            // 使用正则表达式匹配INSERT语句
            // 格式：INSERT INTO "Orders" (Id, OrderNumber, Power, RatedVoltage, CreatedBy, CreatedTime) VALUES (1, 123456, 220, 123, '宋程', '2024-06-03 15:23:48.3471354');
            var pattern = @"INSERT INTO ""Orders"" \(Id, OrderNumber, Power, RatedVoltage, CreatedBy, CreatedTime\) VALUES \((\d+), (\d+), (\d+), (\d+), '([^']*)', '([^']*)'\);";
            var matches = Regex.Matches(sqlContent, pattern);

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    try
                    {
                        var createdBy = match.Groups[5].Value;
                        var createdTime = ParseDateTime(match.Groups[6].Value);

                        var order = new Orders
                        {
                            OrderNumber = int.Parse(match.Groups[2].Value),
                            Power = int.Parse(match.Groups[3].Value),
                            RatedVoltage = int.Parse(match.Groups[4].Value),
                            CreatedBy = string.IsNullOrEmpty(createdBy) ? string.Empty : createdBy,
                            CreatedTime = createdTime,
                            IsReactor = false,
                            UpdatedBy = string.IsNullOrEmpty(createdBy) ? string.Empty : createdBy,
                            UpdatedTime = createdTime
                        };

                        orders.Add(order);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("解析订单数据失败：{Line}, 错误：{Error}", match.Value, ex.Message);
                    }
                }
            }

            return orders;
        }

        private List<Orders> ParseReactorSqlInsertStatements(string sqlContent)
        {
            var orders = new List<Orders>();

            // 使用正则表达式匹配ReactorOrder的INSERT语句
            // 格式：INSERT INTO ReactorOrder (Id, OrderNumber, Power, Ratedvoltage, CreatedBy, CreatedTime) VALUES (51, 813001, 150, 400, '郭运波', '2024-09-23 11:39:15.2234846');
            var pattern = @"INSERT INTO ReactorOrder \(Id, OrderNumber, Power, Ratedvoltage, CreatedBy, CreatedTime\) VALUES \((\d+), (\d+), (\d+), (\d+), '([^']*)', '([^']*)'\);";
            var matches = Regex.Matches(sqlContent, pattern);

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    try
                    {
                        var createdBy = match.Groups[5].Value;
                        var createdTime = ParseDateTime(match.Groups[6].Value);

                        var order = new Orders
                        {
                            OrderNumber = int.Parse(match.Groups[2].Value),
                            Power = int.Parse(match.Groups[3].Value),
                            RatedVoltage = int.Parse(match.Groups[4].Value),
                            CreatedBy = string.IsNullOrEmpty(createdBy) ? string.Empty : createdBy,
                            CreatedTime = createdTime,
                            IsReactor = true, // 电抗器订单，IsReactor为True
                            UpdatedBy = string.IsNullOrEmpty(createdBy) ? string.Empty : createdBy,
                            UpdatedTime = createdTime
                        };

                        orders.Add(order);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("解析电抗器订单数据失败：{Line}, 错误：{Error}", match.Value, ex.Message);
                    }
                }
            }

            return orders;
        }

        private DateTime ParseDateTime(string dateTimeStr)
        {
            // 解析格式：2024-06-03 15:23:48.3471354
            if (DateTime.TryParse(dateTimeStr, out DateTime result))
            {
                return result;
            }

            // 如果解析失败，返回当前时间
            _logger.LogWarning("无法解析时间字符串：{DateTimeStr}, 使用当前时间", dateTimeStr);
            return DateTime.Now;
        }
    }
}