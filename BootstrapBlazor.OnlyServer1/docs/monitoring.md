# 监控告警

> 当前项目无正式监控体系(APM/Prometheus 等),本文档记录**最低必备的人工/简单监控项**。

## 应用层关键指标

### 1. 进程存活

**指标**: w3wp.exe 进程是否在运行

**检查方式**:

```powershell
# 手动
Get-Process -Name w3wp | Where-Object { $_.Id -eq (Get-WebAppPoolState -Name "BootstrapBlazor").WorkerProcesses[0].ProcessId }
```

**告警**: 进程消失超过 5 分钟。

### 2. 端口监听

**指标**: 8373 / 80 (IIS 绑定) 端口是否监听

```powershell
Test-NetConnection -ComputerName localhost -Port 8373
```

**告警**: 连续 3 次失败。

### 3. HTTP 可达性

**指标**: 首页返回 200

```powershell
$status = (Invoke-WebRequest http://localhost/ -UseBasicParsing).StatusCode
# $status 应为 200
```

**告警**: 连续 3 次非 200。

### 4. Blazor 静态资源

**指标**: `_framework/blazor.web.js` 可访问(说明 Blazor wiring 正常)

```powershell
(Invoke-WebRequest http://localhost/_framework/blazor.web.js -UseBasicParsing).StatusCode
```

## 数据库层

### 1. DB 连通性

每 5 分钟一次:

```sql
SELECT 1
```

**告警**: 超时。

### 2. 慢查询

```sql
-- 平均耗时 Top 10
SELECT TOP 10
    total_elapsed_time / execution_count / 1000 AS avg_ms,
    execution_count,
    SUBSTRING(text, 1, 200) AS query_text
FROM sys.dm_exec_query_stats
CROSS APPLY sys.dm_exec_sql_text(sql_handle)
WHERE execution_count > 10
ORDER BY avg_ms DESC;
```

**告警**: 平均 > 1000ms。

### 3. UNIQUE 冲突频率

```sql
-- 最近 1 小时 UNIQUE 冲突次数
SELECT COUNT(*) AS conflict_count
FROM Logs
WHERE Logged > DATEADD(HOUR, -1, GETUTCDATE())
  AND Message LIKE '%unique key%'
  AND Message LIKE '%IX_%';
```

**告警**: 1 小时内 > 50 次(可能是前端重复提交)。

### 4. 表空间

```sql
-- 各表行数 / 占用空间
SELECT
    t.NAME AS TableName,
    p.rows AS RowCounts,
    SUM(a.total_pages) * 8 / 1024.0 AS TotalSpaceMB
FROM sys.tables t
INNER JOIN sys.indexes i ON t.OBJECT_ID = i.object_id
INNER JOIN sys.partitions p ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE t.is_ms_shipped = 0
GROUP BY t.NAME, p.Rows
ORDER BY TotalSpaceMB DESC;
```

**告警**: 单表 > 1 GB 或磁盘剩余 < 20%。

## 业务层关键指标

### 1. 登录失败率

```sql
-- 最近 1 小时登录失败次数
SELECT COUNT(*) AS fail_count
FROM Logs
WHERE Logged > DATEADD(HOUR, -1, GETUTCDATE())
  AND Message LIKE '%登录失败%';
```

**告警**: 1 小时内 > 100 次(可能密码爆破)。

### 2. 添加图号成功率

```sql
-- 最近 1 小时添加图号成功率
SELECT
    SUM(CASE WHEN Message LIKE '%added drawing number%' THEN 1 ELSE 0 END) AS success,
    SUM(CASE WHEN Level = 'ERROR' AND Message LIKE '%drawing number%' THEN 1 ELSE 0 END) AS failed
FROM Logs
WHERE Logged > DATEADD(HOUR, -1, GETUTCDATE());
```

**告警**: 失败率 > 10%。

### 3. 保存订单耗时

应用层日志里 `Adding new order: {OrderNumber}` 前后可加 stopwatch,记录 P95 耗时:

> 当前未实现,如需可加 NLog `LogInformation` 配 `Stopwatch`,后写入 Logs 表自定义列。

## NLog 日志告警规则

### ERROR 级日志告警

任何 ERROR 级日志都该被人工/自动处理。

```sql
SELECT * FROM Logs WHERE Level = 'ERROR' AND Logged > DATEADD(HOUR, -1, GETUTCDATE())
ORDER BY Logged DESC;
```

**建议**: 接到告警后查 NLog 文件或 DB,定位异常类型:

| 异常关键词 | 根因 | 处理 |
|----------|------|------|
| `SqlException: Invalid column name` | schema 不匹配 | 跑 DbMigrator --schema |
| `SqlException: Cannot insert duplicate key` | 应用层去重失效 | 查 service 调用方 |
| `ObjectDisposedException` | Dispose 提前 | 查组件生命周期 |
| `LockTimeout` / `deadlock` | DB 锁等待 | 见 ADR 0005 |
| `UnauthorizedAccessException` | 文件/目录权限 | 检查 IIS_IUSRS 权限 |

### 慢操作告警

应用启动 / 单次请求 > 5 秒记 WARN。

当前未实现,可加 [NLog `AsyncTargetWrapper` + `BufferingTargetWrapper` 配合慢日志收集](https://nlog-project.org/)。

## 推荐监控体系(后续可上)

按"够用 → 完善"分阶段:

| 阶段 | 工具 | 投入 | 收益 |
|------|------|------|------|
| **当前** | 人工 PowerShell 巡检 | 0 | 最低保障 |
| **短期** | Windows Task Scheduler + 自定义 PowerShell 脚本 | 1 人天 | 自动邮件告警 |
| **中期** | Prometheus + Grafana + [windows_exporter](https://github.com/prometheus-community/windows_exporter) | 3 人天 | 全指标可视化 |
| **长期** | Application Insights / Elastic APM | 1 人周 | 链路追踪 + 业务监控 |

## 日志保留策略

| 来源 | 保留时长 | 清理方式 |
|------|---------|---------|
| `Logs` 表 | 90 天 | SQL Agent Job 每周 `DELETE WHERE Logged < DATEADD(DAY, -90, GETUTCDATE())` |
| `logs/*.log` 文件 | 30 天 | NLog `archiveAboveSize` + `maxArchiveFiles` 自动归档 |
| IIS 日志 | 30 天 | IIS 日志清理脚本 |