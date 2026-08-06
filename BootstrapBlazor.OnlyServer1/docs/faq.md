# 常见问题 (FAQ)

## 启动 / 部署

### Q: `dotnet build` 报错 "未包含 'IsDelete' 的定义"
**A**: Models 已经统一改为 `IsDeleted` / `CreatedTime`(ed 结尾),但 **DbMigrator/Program.cs** 里部分旧代码还在用 `IsDelete` / `CreateTime`(无 ed)。

需要把 DbMigrator 中所有引用 Models 字段名的位置同步更新。详见 [docs/database.md](database.md#已知数据问题)。

### Q: 启动后浏览器访问 404
**A**: 先确认 `dotnet run --project BootstrapBlazor.DbMigrator -- --schema` 已执行,**数据库表已建好**。否则 SqlSugar AOP 找不到表会抛异常。

### Q: `dotnet run -- --drop --schema --seed` 在生产库能跑吗?
**A**: ❌ 绝对不能。`--drop` 会**删除所有业务表**。生产环境**只跑 `--schema`(增量演进)**,不传 `--drop`。

## 登录

### Q: 默认管理员账号是什么?
**A**:
- 员工号: `1`
- 密码: `Admin@123`(由 `SeedIdentityAsync` 灌种,可通过 `--admin-password` 覆盖)

⚠️ **首次登录后立即改密**。

### Q: 登录后菜单不显示业务项?
**A**: 检查以下几点:
1. Cookie 是否带 `Permission` claim?用浏览器开发者工具查看 Application → Cookies → .AspNetCore.Identity.Application
2. 用户是否有角色关联?查 `UserRoles` 表
3. 角色是否有权限码?查 `RolePermissions` 表
4. 权限码 `IsActive` 是否为 true?查 `Permissions` 表

### Q: 改密码提示"旧密码不正确"
**A**: AspNetCore Identity 默认密码策略是 PBKDF2 哈希。常见错误:
- 大小写不对(密码区分大小写)
- 输入了中文全角字符

### Q: 登录后页面刷新就掉登录?
**A**: DataProtection 密钥丢失。每次 IIS 应用池回收会重新生成,所有 Cookie 失效。

修复:
- 默认 `keys/` 在应用根,`create-iis-site.ps1` 已自动加 `IIS_IUSRS:(OI)(CI)M`,理论上不会丢
- 若用了独立盘,设置 `DataProtection:KeyDirectory` 为**持久盘**(配合 AppPool 环境变量 `DataProtection__KeyDirectory`)
- 确认 `keys/` 目录有读/写权限给 IIS_IUSRS 组(`icacls "<InstallDir>\keys"` 查看)
- **禁用 29h 周期回收**:确认 AppPool `PeriodicRestart.Time = 00:00:00`(`create-iis-site.ps1` 已自动设)

## 业务操作

### Q: 新建订单点了保存,1-2 分钟才反应?
**A**: 老 bug,已在 v2.0 修复(ADR 0005)。旧版本用 `SERIALIZABLE` 事务,范围锁导致等锁超时。

如果还在出现:
1. 确认已运行 `DbMigrator --schema` 加 `IX_Orders_OrderNumber` filtered unique index
2. 确认 `OrdersService.SaveAsync` 已改用 `BeginTran()`(默认隔离级)

### Q: 添加图号时部分失败,部分成功?
**A**: 这是预期行为,业务上允许同订单多图号批次提交。**部分失败**在 UI 上弹 Modal 列出所有失败原因;**全失败**留在原页面错误区。

### Q: 改图号时提示"不允许修改"?
**A**: 订单下已有图号时,**编辑/删除订单按钮被禁用**(`Orders.razor` 的 `HasDrawingNumber` 回调)。防止改了订单号导致图号引用错位。

修复方法:先删除该订单下所有图号,再编辑订单。

### Q: C04 表的图号为什么从 17001 开始,不是 1?
**A**: 历史遗留,`appsettings.json` 的 `BusinessRules.DrawingType.Seeds.C04 = 17000`。这个种子值是迁移老库时按"业务图号 = MinDrawingNumber + Id"公式回填用的。

## 数据库

### Q: 我改了 Models 的字段,数据库需要同步吗?
**A**: 需要。`DbMigrator` 的 `EvolveSchemaAsync` 会自动检测并 `ALTER TABLE ADD` 新列,**但不会改列类型/删除列**。

- 加列:运行 `dotnet run --project BootstrapBlazor.DbMigrator -- --schema`
- 改类型:手动 `ALTER TABLE ALTER COLUMN`
- 删列:手动 `ALTER TABLE DROP COLUMN`(确保无应用代码引用)

### Q: 我手动改了数据库,代码里怎么生效?
**A**: SqlSugar Code First **只在建表时读 Models**。手动改的列名/类型需要 Models 也同步修改,否则 SqlSugar 生成的 SQL 与实际 schema 不匹配会报错。

## 调试

### Q: SQL 不打印到日志?
**A**: `Data/SqlSugarSetup.cs` 注册的 AOP `OnLogExecuting` 用 `LogDebug` 级别。`appsettings.json` 的 `Logging.LogLevel.Default` 默认 `Information`,需要临时改为 `Debug` 才能看到 SQL。

或者改 NLog.config 加一个 Console target 直接打 SQL 到 stdout。

### Q: 怎么本地复现生产问题?
**A**:
1. 用相同 `appsettings.Production.json` 连接生产 DB(只读账号)
2. 浏览器开发者工具 → Application → Cookies 复制 .AspNetCore.Identity.Application
3. 用 Production 环境变量 `ASPNETCORE_ENVIRONMENT=Production` 启动

⚠️ 不要在生产库上调试写操作。

### Q: NLog 写的日志我看不到?
**A**: 看 `Logs` 表:
```sql
SELECT TOP 100 * FROM Logs ORDER BY Id DESC;
```
或 `bootstrapblazor.*.log` 文件(应用根目录)。