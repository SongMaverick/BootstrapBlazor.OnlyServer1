# L-8 部署手册 · Users.GID → Users.Gid 重命名

## 背景

按 `docs/naming-consistency-2026-07-08.md` L-8 项,`Users.GID` 列重命名为 `Users.Gid`(同步重命名 C# 属性和 Claim 字符串)。

**关键背景**:本项目**尚未上线**(2026-07-08 确认),无现存用户 cookie/会话,Claim 字符串可一刀切改 `"Gid"`,**无需双 Claim 过渡阶段**。

**简化路径**:
- ✅ C# 属性 + Claim 常量直接改名
- ✅ DB 列通过 DbMigrator `--rename-users-gid` 重命名
- ❌ 跳过 C.1(双 Claim 写/读)
- ❌ 跳过 C.3(过渡代码清理)

## 范围

- **DB 改动**:`Users.GID` → `Users.Gid`(单列 sp_rename,原子事务)
- **代码改动**:
  - `Users.GID` 属性 → `Gid`(~10 文件,11 处引用)
  - `ClaimsPrincipalExtensions.GIDClaim = "GID"` → `GidClaim = "Gid"`
  - `CurrentUserAccessor.GetGID()` → `GetGid()`
- **数据改动**:无(列重命名不改数据)

## 前置条件

1. **完整数据库备份**(必做)
2. **DbMigrator 新版已编译**(含 `--rename-users-gid` / `--rollback-users-gid` / `--check-users-state` 3 个 mode)
3. **web 新代码已编译**(C# 用 `Gid` 属性 + `GidClaim` 常量)
4. **维护窗口已通告**(总停机 5~15 秒)

## 协调部署流程

```bash
# === 步骤 1 · DBA 备份 ===
sqlcmd -S <server> -d <db> -Q "BACKUP DATABASE [OnlyServer1] TO DISK = 'D:\Backup\pre_l8_<YYYYMMDD_HHMMSS>.bak' WITH INIT"

# === 步骤 2 · 停止 web 应用 ===
# IIS:
Stop-WebAppPool -Name "OnlyServer1"
# Docker:
docker stop onlyserver1-web
# k8s:
kubectl scale deploy onlyserver1-web --replicas=0

# === 步骤 3 · 跑 DB 迁移(原子 sp_rename) ===
cd D:\D7_codeBase\BootstrapBlazor.OnlyServer1
dotnet run --project BootstrapBlazor.DbMigrator -- --rename-users-gid
# 期望输出:
#   ✓ Users.GID → Users.Gid (重命名完成)

# === 步骤 4 · 验证 DB 状态 ===
dotnet run --project BootstrapBlazor.DbMigrator -- --check-users-state
# 期望输出:
#   ✓ Users:新名 Gid(已迁移)
#   >>> 状态:已迁移,无需操作

# === 步骤 5 · 部署新 web 代码 ===
# 此版本 C# 用 Gid 属性 + GidClaim 常量,与新 DB 列名匹配

# === 步骤 6 · 启动 web 应用 ===
Start-WebAppPool -Name "OnlyServer1"
# 或 docker run / kubectl scale --replicas=1

# === 步骤 7 · 烟测 ===
# 1. 登录(任意账户:Admin / Operator / Viewer)
# 2. 进入"用户管理"页,确认 Gid 列显示正常
# 3. 搜索某用户的 Gid,确认能搜到
# 4. 进入"角色权限"页,点击某角色的"成员"按钮,确认 Gid 字段显示
# 5. 进入"用户-角色"页,确认每行的 Gid 显示
```

## 失败回滚

```bash
# === 场景 A:步骤 3 失败(迁移异常) ===
# 迁移自动回滚(原子事务),无需操作。后续步骤不执行。
# 启动原 web 即可,系统回到迁移前状态。
Start-WebAppPool -Name "OnlyServer1"

# === 场景 B:步骤 7 发现 web 端 bug ===
# 1. 停止 web
Stop-WebAppPool -Name "OnlyServer1"

# 2. 回滚 DB
dotnet run --project BootstrapBlazor.DbMigrator -- --rollback-users-gid
# 验证:
dotnet run --project BootstrapBlazor.DbMigrator -- --check-users-state
# 期望:✓ Users:旧名 GID(待迁移)

# 3. 部署回旧 web 代码
# (从 git 历史 checkout 迁移前的 commit,build & publish)

# 4. 启动旧 web
Start-WebAppPool -Name "OnlyServer1"

# === 场景 C:web 端已启动但用户报告 500 ===
# 立即停止 web,跑回滚(场景 B),恢复服务优先
```

## Round-trip 验证(部署后,可选)

```bash
# 部署后,在测试库(staging)上跑 round-trip,确保模式无 bug:
# 注意:生产 DB 不要跑这套,会扰乱索引统计信息
dotnet run --project BootstrapBlazor.DbMigrator -- --check-users-state
dotnet run --project BootstrapBlazor.DbMigrator -- --rename-users-gid
dotnet run --project BootstrapBlazor.DbMigrator -- --check-users-state
dotnet run --project BootstrapBlazor.DbMigrator -- --rollback-users-gid
dotnet run --project BootstrapBlazor.DbMigrator -- --check-users-state
# 5 步应在 staging 上无错误往返
```

## 端到端验证清单

部署完成后,逐项验证:

- [ ] 应用启动正常(无 SqlSugar 列映射错误)
- [ ] 登录成功(任意角色)
- [ ] 顶部菜单显示当前用户 Gid/Name 正确
- [ ] 用户管理页:用户列表 + Gid 列显示 + 搜索
- [ ] 角色权限页:点"成员"按钮,弹窗显示 Gid
- [ ] 用户-角色页:用户行的 Gid 列显示
- [ ] 新建用户时 Gid 必填校验生效

## 关键文件参考

- DbMigrator 新模式实现:`BootstrapBlazor.DbMigrator/Program.cs` `RenameUsersGidColumnAsync` / `CheckUsersGidStateAsync`
- C# 属性定义:`BootstrapBlazor.OnlyServer1/Models/Users.cs` `Gid`
- Claim 常量:`BootstrapBlazor.OnlyServer1/Services/Identity/ClaimsPrincipalExtensions.cs` `GidClaim = "Gid"`
- 命名一致性报告:`docs/naming-consistency-2026-07-08.md` L-8
- 实施计划:`C:\Users\z0048byz\.claude\plans\velvety-watching-adleman.md` Phase C

## 风险与缓解

| 风险 | 缓解 |
|---|---|
| 项目上线后做此迁移 | **当前项目未上线,本手册适用**。上线后必须改用 Phase C.1 双 Claim 过渡方案 |
| 部署顺序错(代码先于 DB) | 步骤明确"先停 web → 迁 DB → 启 web",停机窗口 5~15 秒 |
| 索引名 sp_rename 后未改 | 此迁移不动索引(Users 表 GID 无唯一索引),无需重建 |
| web 端忘记用新 C# 编译部署 | 步骤 5 强约束:必须用含 `Gid` 的二进制 |
| 回滚后 web 端忘了 deploy 旧版 | 部署手册场景 B 第 3 步明确:从 git checkout 旧 commit 重新 build |

## 联系人

- 代码:`@CodeReview` 或在 PR 留言
- 部署:`@DevOps`
- 数据库备份:`@DBA`
