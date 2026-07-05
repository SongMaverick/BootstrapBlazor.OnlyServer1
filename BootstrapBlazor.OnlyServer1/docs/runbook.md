# 运维手册 (RUNBOOK)

本文档面向运维人员,记录**常见故障处理**、**日志位置**、**应急步骤**。

## 关键路径

| 资源 | 路径 |
|------|------|
| 应用根目录 | `D:\Apps\BootstrapBlazor.OnlyServer1\` |
| 应用日志 | `D:\Apps\BootstrapBlazor.OnlyServer1\logs\*.log` |
| 数据库日志表 | `stclmd.dbo.Logs` |
| DataProtection 密钥 | `<应用根>\keys\` 或 `DataProtection:KeyDirectory` 配置项 |
| IIS 站点配置 | IIS 管理器 → 站点 "BootstrapBlazor" |
| 配置文件 | `<应用根>\appsettings.{Environment}.json` |

## 服务状态检查

### IIS 站点是否运行?

```powershell
# PowerShell
Get-WebSite -Name "BootstrapBlazor" | Select-Object State, Status
# State = Started, Status = Running → 正常
```

### 应用是否在监听?

```powershell
Test-NetConnection -ComputerName localhost -Port 8373
# TcpTestSucceeded : True → 正常
```

### 数据库连通?

```powershell
# 任意机器
sqlcmd -S "SE-XXXXX\SQLEXPRESS" -d stclmd -Q "SELECT 1"
# 返回 1 → 正常
```

### 进程是否存活?

```powershell
Get-Process -Name w3wp | Where-Object {$_.MainWindowTitle -like "*BootstrapBlazor*"}
# 或 Task Manager 查看 w3wp.exe 进程
```

## 常见故障

### F1: 用户登录后立刻被踢出

**症状**: 用户访问页面后立刻跳转到登录页,反复如此。

**根因排查**:

1. **DataProtection 密钥丢失**
   ```powershell
   Test-Path "D:\Apps\BootstrapBlazor.OnlyServer1\keys"
   Get-ChildItem "D:\Apps\BootstrapBlazor.OnlyServer1\keys" | Measure-Object
   ```
   - 目录不存在或为空 → 密钥丢失,Cookie 无法解密
   - 修复:从备份恢复 `keys/` 目录,或临时禁用 DataProtection(改 `Program.cs`)
   - **生产**:设置 `DataProtection:KeyDirectory` 指向**应用外的持久盘**

2. **应用池身份无 keys 读权限**
   ```powershell
   icacls "D:\Apps\BootstrapBlazor.OnlyServer1\keys"
   # 确认 IIS AppPool\BootstrapBlazor 有读/写
   ```

3. **IIS 应用池回收后新密钥生成**
   - 现象:`keys/` 目录只有 1 个 xml,但应用刚启动
   - 解决:确保 `keys/` 不在应用目录(改 `DataProtection:KeyDirectory`)

### F2: 数据库连接失败

**症状**: 页面 500 错误,NLog 报 `SqlException: Cannot open database "stclmd"`。

**排查**:
```powershell
# 1. SQL Server 服务是否运行
Get-Service -Name MSSQL$SQLEXPRESS
# Running → 正常

# 2. 防火墙是否拦截 1433 / dynamic port
Test-NetConnection -ComputerName SE-XXXXX -Port 1433

# 3. 账号密码是否过期
sqlcmd -S "SE-XXXXX\SQLEXPRESS" -U sa -P "<password>" -Q "SELECT 1"
```

**修复**: 更新 `appsettings.Production.json` 的连接字符串,重启 IIS。

### F3: NLog 不写库

**症状**: `Logs` 表无新记录,但应用日志文件有内容。

**排查**:
```sql
-- 检查 dbTarget 配置
SELECT TOP 10 * FROM Logs ORDER BY Id DESC;
-- 若 Level 列有 'ERROR' 且 Message 含 'Cannot insert' → NLog 自身写库失败
```

**修复**:
1. 检查 NLog.config 的 `dbProvider` / `connectionString` 是否正确
2. 检查 `Logs` 表是否存在(DbMigrator 没跑或失败)
3. 检查 NLog 账号对 `Logs` 表有 INSERT 权限

### F4: 页面 500 错误

**症状**: 浏览器看到空白页或 ASP.NET 默认错误页。

**排查步骤**:

1. **看 NLog 文件**
   ```
   D:\Apps\BootstrapBlazor.OnlyServer1\logs\fallback-YYYY-MM-DD.log
   ```
   或 `Logs` 表最近 10 条 ERROR。

2. **常见 500 错误**:
   | 错误关键字 | 原因 | 修复 |
   |----------|------|------|
   | `SqlException: Invalid column name` | Models 与 DB schema 不一致 | 跑 `DbMigrator --schema` |
   | `SqlException: Cannot insert duplicate key` | UNIQUE 冲突 | 检查应用层去重逻辑 |
   | `Object reference not set` | 空引用 | 查 NLog 堆栈,联系开发 |
   | `The model item passed is null` | 视图模型为 null | 查 NLog 堆栈 |

3. **应急**:重启 IIS 应用池
   ```powershell
   Restart-WebAppPool -Name "BootstrapBlazor"
   ```

### F5: 应用启动慢 / 卡死

**症状**: 首次访问页面 5 秒以上才响应。

**排查**:
```powershell
# 1. CPU / 内存
Get-Process -Name w3wp | Select-Object CPU, WorkingSet

# 2. 数据库慢查询
SELECT TOP 10
    total_elapsed_time / execution_count AS avg_ms,
    text
FROM sys.dm_exec_query_stats
CROSS APPLY sys.dm_exec_sql_text(sql_handle)
ORDER BY avg_ms DESC;
```

**修复**:
- 内存不足 → 加机器内存或重启
- 慢查询 → 联系开发优化

### F6: Cookie 过期时间太短

**症状**: 用户频繁需要重新登录。

**排查**:
```csharp
// Program.cs 的 AddIdentityCookies 配置
// 默认 CookieAuthScheme 的过期时间是 IdentityConstants.ApplicationScheme 默认值
```

**修复**: 修改 `Program.cs`:
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromHours(8);  // 默认 14 天
    options.SlidingExpiration = true;
});
```

## 应急操作

### 重启应用池

```powershell
# 优雅重启(关闭现有连接)
Restart-WebAppPool -Name "BootstrapBlazor"

# 强制重启(立即杀掉进程)
%windir%\system32\inetsrv\appcmd recycle apppool /apppool.name:"BootstrapBlazor"
```

### 回滚发布版本

```powershell
# 1. 备份当前版本
Copy-Item "D:\Apps\BootstrapBlazor.OnlyServer1" "D:\Backup\BootstrapBlazor_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Recurse

# 2. 解压历史版本到应用目录
Expand-Archive "D:\Releases\BootstrapBlazor_2.0.0.zip" -DestinationPath "D:\Apps\BootstrapBlazor.OnlyServer1" -Force

# 3. 重启应用池
Restart-WebAppPool -Name "BootstrapBlazor"
```

### 数据库紧急备份

```sql
-- 完整备份(每天跑)
BACKUP DATABASE [stclmd]
TO DISK = 'D:\Backups\stclmd_full_20260705.bak'
WITH INIT, COMPRESSION;

-- 事务日志备份(每小时跑)
BACKUP LOG [stclmd]
TO DISK = 'D:\Backups\stclmd_log_2026070500.trn'
WITH INIT;
```

## 日志查询技巧

### NLog 关键字搜索

```powershell
# 查找错误
Select-String -Path "D:\Apps\BootstrapBlazor.OnlyServer1\logs\*.log" -Pattern "ERROR|Exception"

# 查找特定用户操作
Select-String -Path "D:\Apps\BootstrapBlazor.OnlyServer1\logs\*.log" -Pattern "WorkerNumber=87100"

# 查找数据库错误
Select-String -Path "D:\Apps\BootstrapBlazor.OnlyServer1\logs\*.log" -Pattern "SqlException"
```

### DB Logs 表查询

```sql
-- 最近 1 小时 ERROR
SELECT TOP 100 * FROM Logs
WHERE Logged > DATEADD(HOUR, -1, GETUTCDATE())
  AND Level = 'ERROR'
ORDER BY Logged DESC;

-- 特定 logger(类名)
SELECT * FROM Logs
WHERE Logger = 'BootstrapBlazor.OnlyServer1.Services.OrdersService'
ORDER BY Logged DESC;
```

## 联系开发

| 故障类型 | 联系方式 |
|---------|---------|
| SQL/数据库问题 | DBA on-call |
| 应用崩溃/异常 | 应用 on-call |
| 紧急回滚 | 运维 + 应用 on-call 双确认 |