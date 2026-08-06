# 灾备与回滚

## 备份策略

### 1. 数据库备份

| 备份类型 | 频率 | 保留时长 | 存储位置 |
|---------|------|---------|---------|
| 完整备份 | 每天 02:00 | 30 天 | `D:\Backups\Daily\` |
| 差异备份 | 每 6 小时 | 7 天 | `D:\Backups\Diff\` |
| 事务日志备份 | 每小时 | 7 天 | `D:\Backups\Log\` |

**SQL Server Agent Job 设置**:

```sql
-- 完整备份 Job
BACKUP DATABASE [stclmd]
TO DISK = N'D:\Backups\Daily\stclmd_full_20260705.bak'
WITH COMPRESSION, INIT, NAME = N'Daily Full Backup';

-- 差异备份 Job
BACKUP DATABASE [stclmd]
TO DISK = N'D:\Backups\Diff\stclmd_diff_2026070502.bak'
WITH DIFFERENTIAL, COMPRESSION, INIT;

-- 事务日志备份 Job
BACKUP LOG [stclmd]
TO DISK = N'D:\Backups\Log\stclmd_log_2026070514.trn'
WITH COMPRESSION, INIT;
```

### 2. 应用文件备份

| 文件 | 备份频率 | 保留时长 |
|------|---------|---------|
| `appsettings.Production.json` | 每次修改前 | 永久(纳入版本控制) |
| `keys/` (DataProtection) | 每天 | 永久(异地保存) |
| `web.config` | 每次修改前 | 永久(纳入版本控制) |
| 应用二进制 | 每次发布前 | 30 天(`BootstrapBlazor_BAK_YYYYMMDDHHMM/`) |

### 3. 异地备份

```powershell
# 每周日 04:00 把本地备份同步到 NAS / 对象存储
$source = "D:\Backups"
$dest = "\\NAS\Backups\BootstrapBlazor"
robocopy $source $dest /MIR /Z /LOG+:"D:\BackupLogs\sync_$(Get-Date -Format 'yyyyMMdd').log"
```

## 回滚策略

### 1. 数据库回滚

#### 时点恢复(PITR)

```sql
-- 把数据库恢复到指定时间点(假定今天 14:30 误删了重要数据)
RESTORE DATABASE [stclmd]
FROM DISK = N'D:\Backups\Daily\stclmd_full_20260705.bak'
WITH NORECOVERY;

RESTORE LOG [stclmd]
FROM DISK = N'D:\Backups\Log\stclmd_log_2026070514.trn'
WITH STOPAT = N'2026-07-05 14:29:00', RECOVERY;
```

#### 整库回滚(灾难)

```sql
-- 1. 把当前 DB 改名(避免覆盖)
ALTER DATABASE [stclmd] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [stclmd] MODIFY NAME = [stclmd_corrupted_20260705];
ALTER DATABASE [stclmd_corrupted_20260705] SET MULTI_USER;

-- 2. 从备份恢复
RESTORE DATABASE [stclmd]
FROM DISK = N'D:\Backups\Daily\stclmd_full_20260704.bak'
WITH RECOVERY;
```

### 2. 应用回滚

#### 二进制回滚(只回滚代码,DB 不动)

**(2026-08-03 IIS 迁移后)** 路径与脚本名:

```powershell
# 1. 备份当前版本(保留 keys/ logs/)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$bak = "D:\inetpub\wwwroot\DrawingNumber_BAK_$stamp"
Copy-Item D:\inetpub\wwwroot\DrawingNumber $bak -Recurse -Force

# 2. Recycle AppPool(释放文件锁)
Restart-WebAppPool -Name "DrawingNumberAppPool"

# 3. 用 robocopy 拷历史版本(跳过 keys/ logs/)
robocopy "D:\Publish\DrawingNumber\IIS_v1.0.0" "D:\inetpub\wwwroot\DrawingNumber" /MIR /XD keys logs /NP

# 4. Recycle AppPool 加载旧 .dll
Restart-WebAppPool -Name "DrawingNumberAppPool"

# 5. 验证
Invoke-WebRequest https://localhost:8373/ -UseBasicParsing -SkipCertificateCheck | Select StatusCode
```

#### DB schema 回滚(回滚到旧版本)

如果新发布的 schema 变更破坏了旧代码:

```powershell
# 1. 回滚应用版本(同 2.1)
# 2. 用旧的 DbMigrator 二进制跑(如果保留)或手动 ALTER TABLE
#    当前项目无自动 schema down 脚本,需手工编写

# 反向迁移示例:删 IX_Orders_OrderNumber 索引(回到 SERIALIZABLE 防重)
sqlcmd -S "JINWAD5A.AD101.SIEMENS-ENERGY.NET" -d stcl_md -Q "DROP INDEX IX_Orders_OrderNumber ON Orders"
```

⚠️ **schema 回滚是高风险操作**,生产前务必先在测试环境验证。

### 3. DataProtection 密钥丢失回滚

如果 `keys/` 被误删:

```powershell
# 1. 停 AppPool
Stop-WebAppPool -Name "DrawingNumberAppPool"

# 2. 从备份恢复 keys/
Copy-Item "D:\Backup\Keys_20260704\*" "D:\inetpub\wwwroot\DrawingNumber\keys" -Recurse -Force

# 3. 启 AppPool
Start-WebAppPool -Name "DrawingNumberAppPool"
```

如果**没有 keys 备份**:
- 全员会被强制重新登录(预期行为)
- 或用跨服务器迁移方式:`create-iis-site.ps1 -CopyKeysFrom <旧 keys 路径>` 重建

## 灾难恢复等级

| 等级 | 场景 | RTO(恢复时间目标) | RPO(数据丢失上限) |
|------|------|------------------|------------------|
| **L1 应用崩溃** | w3wp.exe 退出 | 5 分钟(IIS 自动重启) | 0 |
| **L2 应用错乱** | 部署了错误版本 | 30 分钟(二进制回滚) | 0 |
| **L3 DB 误操作** | 误删 / 误改数据 | 2 小时(PITR) | < 1 小时 |
| **L4 DB 损坏** | 硬盘故障 / 误 truncate | 24 小时(从异地备份恢复) | < 24 小时 |
| **L5 整机故障** | 服务器宕机 | 24 小时(灾备机房启动) | < 24 小时 |
| **L6 站点级灾难** | 机房不可用 | 72 小时 | < 24 小时 |

## 应急响应清单

### 数据库被误操作

1. **立即停止 AppPool**(防止进一步写入扩大损害)
2. **锁定数据库单用户模式**:
   ```sql
   ALTER DATABASE [stclmd] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
   ```
3. **评估损害范围**(查 Logs 表、查受影响行)
4. **执行 PITR**(如上述)
5. **启动 AppPool**
6. **通知相关业务方**

### 应用持续崩溃

1. **停止 AppPool**(防止 Rapid-Fail Protection 无限重启)
2. **查看 stdout 日志**(`<app-root>\logs\stdout_*.log`)
3. **看 Windows Event Log**:
   ```powershell
   Get-EventLog -LogName Application -Source "IIS AspNetCore Module V2" -Newest 20
   ```
4. **回滚到上一版本**(二进制回滚)
5. **复现问题,定位根因**

### 灾难(整库丢失)

1. **不要慌,先评估**:是只丢 DB 还是连服务器一起没
2. **从异地备份恢复**(24 小时 RPO)
3. **重新部署应用**(如果服务器也没了)
4. **跑 DbMigrator --schema --seed**(不要 --drop,只演进)
5. **迁移数据**(如果有 Users.sql 等)
6. **通知所有用户重新登录**(DataProtection 密钥换了)

## 关键 SLA

| 指标 | 目标 |
|------|------|
| **应用可用性** | 99%(每月停机 < 7.2 小时) |
| **DB 备份成功率** | 100% |
| **数据丢失上限** | < 1 小时(配合每小时事务日志备份) |
| **回滚时间** | < 30 分钟(应用) / < 2 小时(DB) |