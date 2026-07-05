# 0005 - 用 UNIQUE INDEX 替代 SERIALIZABLE 事务防重

## 状态

已实施 (2026-07-05)

## 背景

`OrdersService.SaveAsync` 早期用 `IsolationLevel.Serializable` 事务 + 应用层重名预检,实测:
- **新建订单点了保存,1-2 分钟才反应**
- 用户体验非常差

## 根因分析

SERIALIZABLE 隔离级别用**范围锁**(range locks)。SQL Server 默认 `lock_timeout = -1`(无限等待)。

即使只是**普通 SELECT**(不只写),只要落在 SERIALIZABLE 事务持有的 OrderNumber 范围内,也会被阻塞。死锁检测器**只在线索互锁成环时**触发(约 5 秒),但"等另一个事务释放锁"会**无限等下去**,直到连接超时或客户端主动断开。

## 评估过的方案

### 方案 A: 保留 SERIALIZABLE + 设置 lock_timeout=5s
- 优点: 改动最小
- 缺点: 5 秒后报错给用户,体验仍然不好;SERIALIZABLE 本身的范围锁开销仍在

### 方案 B: 改用默认隔离级(READ COMMITTED)+ 应用层重名预检
- 优点: 没有范围锁
- 缺点: **理论上有 TOCTOU 竞态**(读后写之间另一个事务可能插入相同 OrderNumber)

### 方案 C: 改用默认隔离级 + DB 层 UNIQUE INDEX 约束 ✅
- 优点: DB 唯一性保证;应用层 catch SqlException 2627/2601;无锁等待
- 缺点: 需要 DbMigrator 加索引;需要修改应用层重名检测逻辑

## 决策

选 **方案 C**。

实现:

1. **DbMigrator**: 新增 `EnsureFilteredUniqueIndex` 工具方法,给 `Orders.OrderNumber` 加 filtered unique index:
   ```sql
   CREATE UNIQUE INDEX IX_Orders_OrderNumber 
   ON Orders (OrderNumber) 
   WHERE IsDeleted = 0;
   ```
   - 用 **filtered index**:只约束 `IsDeleted = 0` 的活跃行
   - 软删订单保留原 OrderNumber,允许业务重新使用同一订单号

2. **OrdersService.SaveAsync**:
   - 去掉 `_db.Ado.BeginTran(IsolationLevel.Serializable)` → `_db.Ado.BeginTran()`(默认 READ COMMITTED)
   - 去掉应用层 `isDuplicate` 预检
   - INSERT/UPDATE 时 `catch SqlException 2627/2601`,翻译成"订单号 X 已存在"

3. **IsDeadlockOrTimeout 死代码删除**(不再需要)

## 后果

### 正面
- 保存耗时从 **1-2 分钟降到 < 100ms**
- DB 层保证唯一性,无 TOCTOU 竞态
- BootstrapBlazor.Table 自带的 spinner 能正常显示反馈

### 负面 / 风险
- 已部署环境需跑 `DbMigrator --schema` 加新索引
- 如果现有库 `Orders.OrderNumber` 已有重复数据(filtered index 仍会拒绝),需要先清理

### 中和
- `IsDuplicateOrderNumberError` 同时检测错误号(2627/2601)+ 关键字("unique key" / "duplicate key" / 索引名),防止错误消息本地化导致匹配失效

## 关键 SQL 错误号速查

| 错误号 | 含义 |
|--------|------|
| **2627** | UNIQUE KEY 约束违反 |
| **2601** | 不能在具有唯一索引的对象中插入重复键 |
| 1205 | 死锁 |
| -2 | 查询超时 |

## 相关

- `Services/OrdersService.cs:SaveAsync` — 重构后实现
- `BootstrapBlazor.DbMigrator/Program.cs` — `EnsureAllIndexes` + `EnsureFilteredUniqueIndex`
- 性能对比:用户报告"点击保存,1-2 分钟才反应" → 现 "< 100ms"