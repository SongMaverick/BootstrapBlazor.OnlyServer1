# L-7 部署手册 · C0X 领域词重命名(DrawName/DrawNumber → DrawingName/DrawingNumber)

## 背景

按 `docs/naming-consistency-2026-07-08.md` L-7 项,9 张 C0X 表(C01~C09)的 `DrawName` / `DrawNumber` 列重命名为 `DrawingName` / `DrawingNumber`,同步重命名 C# 属性。

**关键风险**:
- web 代码和 DB 列名必须同步切换,任何状态不一致都会导致查询 500
- 9 张表 + 9 个唯一索引 + 18 列,需要原子事务保证
- 必须提供可回滚路径

## 范围

- **DB 改动**:9 张表 × 2 列 = 18 列 sp_rename;9 个唯一索引 drop+recreate
- **代码改动**:`C0XBase.DrawName/DrawNumber` → `DrawingName/DrawingNumber`(~15 文件)
- **数据改动**:无(列重命名不改数据)

## 前置条件

1. **完整数据库备份**(必做)
2. **DbMigrator 新版已编译**(含 `--rename-c0x-drawingname` / `--rollback-c0x-drawingname` / `--check-c0x-state` 3 个 mode)
3. **web 新代码已编译**(C# 用 `DrawingNumber` / `DrawingName`)
4. **维护窗口已通告**(总停机 5~15 秒,但通报按 30 分钟算)

## 协调部署流程

```bash
# === 步骤 1 · DBA 备份 ===
sqlcmd -S <server> -d <db> -Q "BACKUP DATABASE [OnlyServer1] TO DISK = 'D:\Backup\pre_l7_<YYYYMMDD_HHMMSS>.bak' WITH INIT"

# === 步骤 2 · 停止 web 应用 ===
# IIS:
Stop-WebAppPool -Name "OnlyServer1"
# Docker:
docker stop onlyserver1-web
# k8s:
kubectl scale deploy onlyserver1-web --replicas=0

# === 步骤 3 · 跑 DB 迁移(原子事务,失败自动回滚) ===
cd D:\D7_codeBase\BootstrapBlazor.OnlyServer1
dotnet run --project BootstrapBlazor.DbMigrator -- --rename-c0x-drawingname
# 期望输出:
#   ✓ C01:重命名完成 (DrawNumber→DrawingNumber, DrawName→DrawingName)
#   ✓ C02:重命名完成 ...
#   ... 9 张表
#   >>> 所有 9 张表重命名已提交

# === 步骤 4 · 验证 DB 状态 ===
dotnet run --project BootstrapBlazor.DbMigrator -- --check-c0x-state
# 期望输出:
#   ✓ C01:新名(已迁移)
#   ... 9 张表
#   >>> 状态:已迁移,无需操作

# === 步骤 5 · 部署新 web 代码 ===
# 把编译产物发布到 web 目录(IIS / Docker image / k8s deployment)
# 此版本 C# 用 DrawingNumber/DrawingName,与新 DB 列名匹配

# === 步骤 6 · 启动 web 应用 ===
Start-WebAppPool -Name "OnlyServer1"
# 或 docker run / kubectl scale --replicas=1

# === 步骤 7 · 烟测 ===
# 1. 登录系统
# 2. 进入"订单图号列表"页,确认数据加载正常
# 3. 点开一个图号详情,确认 DrawingName 显示正确
# 4. 进入"添加图号"页,确认图号分配器正常工作
# 5. 编辑图号名称,确认保存成功
```

## 失败回滚

```bash
# === 场景 A:步骤 3 失败(迁移异常) ===
# 迁移会自动回滚(原子事务),无需操作。后续步骤不执行。
# 启动原 web 即可,系统回到迁移前状态。
Start-WebAppPool -Name "OnlyServer1"

# === 场景 B:步骤 7 发现 web 端 bug ===
# 1. 停止 web
Stop-WebAppPool -Name "OnlyServer1"

# 2. 回滚 DB
dotnet run --project BootstrapBlazor.DbMigrator -- --rollback-c0x-drawingname
# 期望输出:9 张表回滚完成
# 验证:
dotnet run --project BootstrapBlazor.DbMigrator -- --check-c0x-state
# 期望:9 张表都是"旧名(待迁移)"

# 3. 部署回旧 web 代码
# (从 git 历史 checkout 迁移前的 commit,build & publish)

# 4. 启动旧 web
Start-WebAppPool -Name "OnlyServer1"

# === 场景 C:web 端已启动但用户报告 500 错误 ===
# 立即停止 web,跑回滚(场景 B),恢复服务优先
```

## Round-trip 验证(部署后,可选)

```bash
# 部署后,在测试库(staging)上跑 round-trip,确保模式无 bug:
# 注意:生产 DB 不要跑这套,会扰乱索引统计信息
dotnet run --project BootstrapBlazor.DbMigrator -- --check-c0x-state
dotnet run --project BootstrapBlazor.DbMigrator -- --rename-c0x-drawingname
dotnet run --project BootstrapBlazor.DbMigrator -- --check-c0x-state
dotnet run --project BootstrapBlazor.DbMigrator -- --rollback-c0x-drawingname
dotnet run --project BootstrapBlazor.DbMigrator -- --check-c0x-state
# 5 步应在 staging 上无错误往返
```

## 端到端验证清单

部署完成后,逐项验证:

- [ ] 应用启动正常(无 SqlSugar 列映射错误)
- [ ] 订单列表页加载正常
- [ ] 订单图号列表页加载正常
- [ ] 新增图号功能正常
- [ ] 编辑图号名称功能正常
- [ ] 跨订单图号查询(图号查询页)正常
- [ ] DrawingNumberSequence 初始化(数据库日志:`CurrentMax=<N>`)
- [ ] 用户管理、角色管理、用户-角色管理 3 页正常(无关,但顺手验证)

## 关键文件参考

- DbMigrator 新模式实现:`BootstrapBlazor.DbMigrator/Program.cs` `RenameC0XColumnsAsync` / `CheckC0XStateAsync` / `ColumnExists` / `IndexExists`
- C# 属性定义:`BootstrapBlazor.OnlyServer1/Models/C0XBase.cs`
- 命名一致性报告:`docs/naming-consistency-2026-07-08.md` L-7
- 实施计划:`C:\Users\z0048byz\.claude\plans\velvety-watching-adleman.md` Phase B

## 风险与缓解

| 风险 | 缓解 |
|---|---|
| 9 张表索引重建时锁表 | `sp_rename` 是元数据操作(<1ms),索引 drop+recreate 在小表瞬时完成。大表建议维护窗口 |
| 部署顺序错(代码先于 DB) | 步骤明确"先停 web → 迁 DB → 启 web",停机窗口 5~15 秒 |
| 索引名 sp_rename 后未改 | 迁移脚本先 drop 旧索引,再 sp_rename 列,最后重建为新名索引 |
| 旧 SQL 文件 / 外部 ETL 消费 DrawName 列名 | 文档标注 deprecated;B.3 已同步 C04~C08.sql 改 DrawingName |
| web 端忘记用新 C# 编译部署 | 步骤 5 强约束:必须用含 `DrawingNumber` / `DrawingName` 的二进制 |
| 回滚后 web 端忘了 deploy 旧版 | 部署手册场景 B 第 3 步明确:从 git checkout 旧 commit 重新 build |

## 联系人

- 代码:`@CodeReview` 或在 PR 留言
- 部署:`@DevOps`
- 数据库备份:`@DBA`
