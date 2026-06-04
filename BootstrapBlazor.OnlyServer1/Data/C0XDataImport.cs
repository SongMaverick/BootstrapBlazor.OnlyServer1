using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;
using System.Text.RegularExpressions;

namespace BootstrapBlazor.OnlyServer1.Data
{
    public class C0XDataImport
    {
        private readonly ILogger<C0XDataImport> _logger;

        public C0XDataImport(ILogger<C0XDataImport> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 迁移C04-C08表的数据
        /// </summary>
        public async Task ImportC0XDataAsync(ISqlSugarClient db, string basePath)
        {
            _logger.LogInformation("开始迁移C04-C08表数据...");

            try
            {
                // 获取数据库中所有订单的旧ID到新ID的映射
                var ordersItemMappings = await db.Queryable<OrdersItem>()
                    .Select(oi => new OrderMapping { OldOrderId = oi.OldOrderId, NewOrderId = oi.NewOrderId })
                    .ToListAsync();
                
                _logger.LogInformation("订单映射关系数量：{Count}", ordersItemMappings.Count);

                // 迁移每个表
                await ImportSingleTableAsync(db, "C04", Path.Combine(basePath, "C04.sql"), ordersItemMappings);
                await ImportSingleTableAsync(db, "C05", Path.Combine(basePath, "C05.sql"), ordersItemMappings);
                await ImportSingleTableAsync(db, "C06", Path.Combine(basePath, "C06.sql"), ordersItemMappings);
                await ImportSingleTableAsync(db, "C07", Path.Combine(basePath, "C07.sql"), ordersItemMappings);
                await ImportSingleTableAsync(db, "C08", Path.Combine(basePath, "C08.sql"), ordersItemMappings);

                _logger.LogInformation("C04-C08表数据迁移完成！");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "迁移C04-C08表数据时发生错误：{Message}", ex.Message);
            }
        }

        /// <summary>
        /// 导入单个表的数据
        /// </summary>
        private async Task ImportSingleTableAsync(ISqlSugarClient db, string tableName, string sqlFilePath, List<OrderMapping> oldToNewOrderIdList)
        {
            _logger.LogInformation("开始导入表 {TableName} ...", tableName);

            try
            {
                if (!File.Exists(sqlFilePath))
                {
                    _logger.LogWarning("SQL文件不存在：{FilePath}", sqlFilePath);
                    return;
                }

                var sqlContent = File.ReadAllText(sqlFilePath);
                var records = ParseInsertStatements(sqlContent, tableName);

                if (records.Count == 0)
                {
                    _logger.LogWarning("从SQL文件中未解析到任何数据：{FilePath}", sqlFilePath);
                    return;
                }

                _logger.LogInformation("从 {TableName}.sql 解析到 {Count} 条数据", tableName, records.Count);

                var importedCount = 0;
                var skippedCount = 0;
                var notFoundCount = 0;

                // 检查表中是否已有数据
                var tableHasData = await CheckTableHasData(db, tableName);

                if (tableHasData)
                {
                    _logger.LogInformation("表 {TableName} 中已有数据，跳过导入", tableName);
                    return;
                }

                foreach (var record in records)
                {
                    // 获取新订单ID（可能有多个，取第一个）
                    var matchingMapping = oldToNewOrderIdList.FirstOrDefault(m => m.OldOrderId == record.OldOrderId);
                    
                    if (matchingMapping == null)
                    {
                        notFoundCount++;
                        _logger.LogWarning("未找到旧订单ID {OldOrderId} 的新订单ID映射", record.OldOrderId);
                        continue;
                    }

                    var newOrderId = matchingMapping.NewOrderId;

                    // 根据表名动态构建INSERT SQL语句
                    try
                    {
                        var sql = $@"INSERT INTO {tableName} (DrawName, DrawingType, OrderId, CreatedBy, CreatedTime, UpdatedBy, UpdatedTime, IsDelete) 
                                     VALUES ('{record.DrawName}', '{record.DrawingType}', {newOrderId}, '{record.CreatedBy}', '{record.CreatedTime:yyyy-MM-dd HH:mm:ss}', '{record.UpdatedBy}', '{record.UpdatedTime:yyyy-MM-dd HH:mm:ss}', {(record.IsDelete ? 1 : 0)})";
                        
                        await db.Ado.ExecuteCommandAsync(sql);
                        importedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("插入数据失败：{Message}", ex.Message);
                        skippedCount++;
                    }
                }

                _logger.LogInformation("表 {TableName} 导入完成！成功导入：{ImportedCount} 条，跳过：{SkippedCount} 条，未找到订单映射：{NotFoundCount} 条", 
                    tableName, importedCount, skippedCount, notFoundCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入表 {TableName} 数据时发生错误", tableName);
            }
        }

        /// <summary>
        /// 解析INSERT语句
        /// </summary>
        private List<C0XRecord> ParseInsertStatements(string sqlContent, string tableName)
        {
            var records = new List<C0XRecord>();

            // 匹配 INSERT INTO 语句
            // 格式：INSERT INTO C04 (Id, DrawName, DrawingType, OrderId, CreatedBy, CreatedTime, UpdatedBy, UpdatedTime, IsDelete) VALUES (7, '矽钢片', 'C04', 30, '陈振东', '2024-10-15 13:45:53.3267203', '陈振东', '2024-10-15 13:45:53.3267542', 0);
            var pattern = $@"INSERT INTO {tableName} \(Id, DrawName, DrawingType, OrderId, CreatedBy, CreatedTime, UpdatedBy, UpdatedTime, IsDelete\) VALUES \((\d+),\s*'([^']*)',\s*'([^']*)',\s*(\d+),\s*'([^']*)',\s*'([^']*)',\s*'([^']*)',\s*'([^']*)',\s*(\d+)\);";
            var matches = Regex.Matches(sqlContent, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var record = new C0XRecord
                    {
                        OldId = int.Parse(match.Groups[1].Value),
                        DrawName = match.Groups[2].Value.Trim(),
                        DrawingType = match.Groups[3].Value.Trim(),
                        OldOrderId = int.Parse(match.Groups[4].Value),
                        CreatedBy = match.Groups[5].Value.Trim(),
                        CreatedTime = ParseDateTime(match.Groups[6].Value),
                        UpdatedBy = match.Groups[7].Value.Trim(),
                        UpdatedTime = ParseDateTime(match.Groups[8].Value),
                        IsDelete = int.Parse(match.Groups[9].Value) == 1
                    };

                    records.Add(record);
                }
            }

            return records;
        }

        /// <summary>
        /// 检查表是否有数据
        /// </summary>
        private async Task<bool> CheckTableHasData(ISqlSugarClient db, string tableName)
        {
            try
            {
                // 使用动态查询检查表是否有数据
                var count = await db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {tableName}");
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析日期时间
        /// </summary>
        private DateTime ParseDateTime(string dateTimeStr)
        {
            if (DateTime.TryParse(dateTimeStr, out DateTime result))
            {
                return result;
            }

            _logger.LogWarning("无法解析日期时间：{DateTimeStr}，使用当前时间", dateTimeStr);
            return DateTime.Now;
        }
    }

    /// <summary>
    /// 订单映射辅助类
    /// </summary>
    public class OrderMapping
    {
        public int OldOrderId { get; set; }
        public int NewOrderId { get; set; }
    }

    /// <summary>
    /// 临时记录类，用于解析SQL数据
    /// </summary>
    public class C0XRecord
    {
        public int OldId { get; set; }
        public string DrawName { get; set; } = "";
        public string DrawingType { get; set; } = "";
        public int OldOrderId { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedTime { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime UpdatedTime { get; set; }
        public bool IsDelete { get; set; }
    }
}
