# 变更日志

本项目的所有重要变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/)。

## [Unreleased]

### Changed
- **重构**:用 `IX_Orders_OrderNumber` filtered unique index 替代 SERIALIZABLE 事务防重,保存耗时从 1-2 分钟降到 < 100ms (ADR 0005)
- **重构**:Models 目录全部统一为 file-scoped namespace (ADR 0003)
- **重构**:N+1 查询修复 — `RolesService` 新增批量查询方法,`Roles.razor` / `UserRoles.razor` 单页查询数从 20-80 降到 2
- **重构**:角色/权限变更后 5 分钟内自动续 cookie — 注册 `SecurityStampValidator<Users>` + `ValidationInterval = TimeSpan.FromMinutes(5)`,用户不重登也能拿到新 Permission / DisplayName / Gid claim
- **重构**:`OrderDetails` → `OrderMDDetail` 重命名(类名/服务/表名/索引/视图模型全栈统一),MD = Mechanical Design,与项目「MD 部门」一致;`IX_OrderDetails_OrderId` → `IX_OrderMDDetail_OrderId`。当前为开发期,数据库尚无此表,无需数据迁移
- **清理**:删除死页面 `UpdateDrawingNumber.razor` 和 `AddDrawingNumbersSelect.razor`
- **清理**:删除导航菜单上无效入口(`/add-drawing-numbers`、`/update-drawing-number` 缺路由参数 → 404)

### Fixed
- **fix**:新建订单卡顿 1-2 分钟问题(根因:SERIALIZABLE 范围锁 + 默认 lock_timeout 无限等待)

### Security(2026-07-07 Review #H1 / Medium 全套修复)
- **Added** H1 纵深防御:`CurrentUserAccessor.RequireRole(...)` 入口方法;8 个 Service 文件,12 个 mutation 方法加权限门
- **Fixed** M5 Login CSRF:`Login.razor` 加 `<AntiforgeryToken />` + `AccountController.Login` 加 `[ValidateAntiForgeryToken]`
- **Fixed** M6 `RolesService.SaveAsync` 加 `BeginTran/CommitTran/RollbackTran`,失败回滚保角色权限不丢
- **Fixed** M2 移除 10+ 处 `ex.Message` 直进 UI(UsersService/RolesService/SqlSugarUserStore/SqlSugarRoleStore),改为"操作失败,请查看日志"
- **Fixed** M4 `Program.cs` `SetMinimumLevel(Trace → Information)`,生产关 Trace 输出不再泄漏堆栈到 SQL `Logs` 表
- **Fixed** H3 `OrderBy(string)` 5 处加白名单(`_xxxAllowedSortCols`):OrdersService / UsersService / RolesService / DepartmentsService / BaseQueryService
- **Added** M8 `web.config` 加 `Content-Security-Policy` + `Strict-Transport-Security` 响应头

### Security(2026-07-09 Review #H1-NEW 修复)
- **Fixed** 角色/权限变更后用户不重登就拿不到新权限的隐患:注册 `SecurityStampValidator<Users>` + `ValidationInterval = 5min`,cookie 中 Permission / DisplayName / Gid claim 静默续期(不改用户密码时无感知,改密码时走原路径强制 SignOut)
- **Fixed** M5-NEW Logout CSRF 漏修:`MainLayout.razor:19` form 加 `<AntiforgeryToken />` + `AccountController.Logout` 加 `[ValidateAntiForgeryToken]`
- **Fixed** M4-NEW 静默 return:`PermissionsService.SetRolePermissionsAsync` 改 `Task<bool>`,失败返回 false 而非静默;`Roles.razor:SavePermsAsync` 处理 bool 返回
- **Fixed** L1 `OrderLockProvider._locks` 字典无界增长:加 `MaybeCleanup()` 5 分钟节流式清理空闲 SemaphoreSlim,抢占式 CAS 保证多线程安全
- **Added** M10 `BaseQueryService.DefaultSortField` 抽象方法 + `OrderBy(string)` 调用点加防御性注释,标明"必须返回硬编码常量"约束,防止未来误用
- **Fixed** L4-NEW `RolesService.SetUserRolesAsync` admin 唯一性 TOCTOU 窗口:加业务规则"admin 永不降自己的权",覆盖原 adminCount 校验无法触达的"2+ admin 同时降自己 → 0 admin"场景(防系统锁死)
- **Refactor** L3-NEW `Components/Pages/LogoutPage.razor` → `LogoutRedirect.razor`(命名误导修复:文件实际只重定向不登出,真登出走 MainLayout.razor:19 的 form post);@page "/logout" URL 保持兼容
- **Fixed** P0-3 HTTPS 部署:`Program.cs` 加 `ConfigureApplicationCookie` (SecurePolicy=Always + SameSite=Strict + 8h 过期) + `AddAntiforgery` (SecurePolicy=Always) + 生产环境 `UseHsts()` + `UseHttpsRedirection()`;`web.config` HSTS 头加 `preload` 标记
- **Fixed** P0-3 部署修复回归:`Program.cs` 两个 Cookie 的 `SecurePolicy` 按环境分流——`Development` 走 `SameAsRequest`(配合 `launchSettings.json` 的 `http://localhost:8373`),`Production` 走 `Always`(配合 IIS HTTPS binding)。原先写死 `Always` 会让本地 HTTP 下浏览器拒传 antiforgery / 登录 cookie,表现为"开发模式不能正常启动 / 反复跳登录页";新增防回归注释说明此坑
- **Fixed** P0-3 部署修复回归:`web.config` 的 HSTS 头(`max-age=31536000; preload`)迁出到 `web.Release.config`(`Insert` 转换,Release 构建时才合并)。原写死会让 dev / IIS Express 触发浏览器 HSTS 缓存,缓存后切回 HTTP dev 被浏览器强制升级 HTTPS → 开发模式打不开
- **Fixed** 生产姓名显示为 `?` 的根因:测试/生产 DB collation 不同(测试 `Chinese_PRC_CI_AS` 支持中文 VARCHAR,生产 `SQL_Latin1_General_CP1_CI_AS` 不支持),`Users.sql` 把 Name/GID/Email 等定义为 `varchar` 导致 NVARCHAR→VARCHAR 隐式转换时中文被替换为 `?`。修复路径选 **drop + DbMigrator 重建**(数据未正式上线,可丢):`--drop --schema --seed --migrate --source . --production` 一把搞定,SqlSugar CodeFirst 自动建 NVARCHAR。同时把 `Users.sql` + `DrawingNames.sql` 7+4 个列改为 `nvarchar`(留作 schema 文档,防以后有人手动 SSMS 踩坑);文件从 UTF-16LE 改为 UTF-8 BOM
- **Added** collation 坑复盘(根因/验证/修复方案对比/防再犯清单),并对照正确的 `Migrations/001_IdentitySchema.sql`(全 NVARCHAR)作为后续新表的 schema 模板
- **Removed** 临时考虑的 `002_ConvertChineseColumnsToNvarchar.sql` ALTER 方案(用户确认生产数据可丢,drop + 重建更干净)
- **Fixed** DbMigrator `BackfillUserDepartmentsAsync` 永远不跑导致 DepartmentId 全 NULL 的 bug:从 `if (doSeed)` 改到 `if (doMigrate)`(它操作的是 migrate 出来的 Users,与 --seed 无关);同时默认部门不存在时**自动建一份**(不再静默跳过),并打印"预期回填 N 条 / 实际 N 条"用于排查 0 更新问题
- **Added** 给 IT 的纯 GUI HTTPS 部署操作清单(约 350 行,加 binding / HTTP Redirect / 应用池重启 / 验证 / 6 类常见问题 / 回滚)

### UX(2026-07-07 AddDrawingNumbers UX 多轮迭代)
- **Changed** 卡片布局:`col-md-6`(每行 2 卡)→ `col-12`(每行 1 Part 列表式)
- **Added** Part 卡片可折叠:默认全折叠 + header 整区可点(`cursor: pointer`)
- **Added** 标题栏 `[已选 N]` badge:0 时灰色,N > 0 时橙色 + ✓ 图标
- **Added** Toolbar `⇕ 全部展开/折叠` 单按钮开关,文案随状态切换
- **Changed** 图名水平多列:响应式 `col-12 / col-sm-6 / col-md-4 / col-lg-3 / col-xl-2`(手机 1 / 平板 2 / 笔记本 3 / 标准 4 / 大屏 **6** 列)
- **Added** Card body 同色系浅背景:`LightBgFor()` 映射 8 色 Bootstrap 对应浅色 hex(`#fff3cd` 黄 / `#d1e7dd` 绿 / …)
- **Added** AddDrawingNumbers 页面 UX 决策说明(折叠/响应式/同色系背景)

### Documentation
- **Added** 主项目代码审查报告(2026-07-07,27 项发现,3 Critical / 7 High / 8 Medium / 9 Low)
- **Added** 多项目代码审查报告(2026-07-07,覆盖 Tests/DbMigrator)
- **Added** README.md 引用审查报告
- **Added** AddDrawingNumbers UX 决策文档
- **Added** README-DEPLOY.md 第 7.x 节"部署后必查清单"(M7)

### Test
- **Added** 60+ 单元测试:7 个 Service 纵深防御 + 10 个 OrderBy 白名单 → 当前 **120/120 测试全绿**

### 用户决定不修(上下文已记录,后续可重评估)
- C-1a: `appsettings.json` 含 sa 密码(私有仓库可接受)
- C-1b: `keys/key-*.xml` 入库(同上)
- C-3: 弱密码策略(`RequiredLength:6` + 4 个 Require 全 false)
- H2: Operator IDOR(部门内部信任)
- H5: HTTPS / HSTS(等证书到位)
- H6: `DrawingName.sql`(38KB,根目录,含真实 PII)
- H7: SqlSugarUserStore 全表扫描(用户量未到门槛)
- M1: 异常吞噬(改动量大,回报小)

## [2.0.0] - 2026-07-05

### Added
- ASP.NET Core Identity + 自定义 SqlSugarUserStore/RoleStore
- 9 张 C0X 图号表(C0XBase abstract + 9 个 handler 多态写入)
- DrawingNumberAllocator 原子分配(SQL Server `UPDATE...OUTPUT`)
- OrderLockProvider 同订单进程内互斥
- DisplayNameClaimsFactory 写入业务 Claim(DisplayName/Gid/Permission)
- NLog DB target 写 `Logs` 表
- 25+ 个细粒度权限码(PermissionCodes 常量)
- 角色保护:ADMIN 唯一超管拒绝移除

### Changed
- 从 EF Core 迁移到 SqlSugar
- C0X 表废弃 `DrawingType` 列(冗余,可由表名推算)
- 引入 `IX_Orders_OrderNumber` filtered unique index 之前的实现用 SERIALIZABLE

## [1.0.0] - 早期版本

- 订单 CRUD
- 图号分配(图号 = 公式 `MinDrawingNumber + Id`)
- 图名维护
- 用户/部门管理