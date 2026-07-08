# 编号本 (BootstrapBlazor.OnlyServer1)

内部办公 Web 程序:管理变压器/电抗器订单的图号分配与查询。

- **技术栈**: .NET 8 + Blazor Server + BootstrapBlazor 8.x + SqlSugar 5.1.4 + SQL Server
- **认证**: ASP.NET Core Identity(`IdentityUser<int>` + SqlSugar 自定义 Store)
- **日志**: NLog(写 DB `Logs` 表 + 文件兜底)
- **部署**: IIS 单实例办公网

## 快速启动

```powershell
# 1. 首次部署:建表 + 灌种子数据 + 迁移历史数据
dotnet run --project BootstrapBlazor.DbMigrator -- --drop --schema --seed --migrate --source .

# 2. 启动 Web
dotnet run --project BootstrapBlazor.OnlyServer1

# 3. 浏览器访问 http://localhost:8373
#    员工号: 1   密码: Admin@123   ← 首次登录后请立即改密
```

## 目录结构

```
BootstrapBlazor.OnlyServer1/
├── Program.cs                     DI 注册、Identity 配置、请求管道
├── Data/SqlSugarSetup.cs          SqlSugarScope 单例 + SQL AOP 日志
├── Models/                        实体(Users/Orders/C0X/DrawingNames/...)
├── Services/                      业务逻辑(Identity/DrawingTypes/Orders/...)
├── Controllers/AccountController.cs   /Account/Login & /Account/Logout
├── Components/                    Blazor 页面(Pages/ + Shared/MainLayout)
├── docs/                          项目文档(架构/数据库/术语/ADR/...)
└── NLog.config                    日志配置

BootstrapBlazor.DbMigrator/        独立的数据库迁移工具
BootstrapBlazor.OnlyServer1.Tests/ 单元测试
```

## 文档导航

| 文档 | 内容 |
|------|------|
| [docs/architecture.md](docs/architecture.md) | 系统架构、数据流、分层职责 |
| [docs/database.md](docs/database.md) | 数据库 ER 图、表清单、字段说明 |
| [docs/glossary.md](docs/glossary.md) | 业务术语统一(避免"图号/图名/DrawNumber"混用) |
| [docs/development.md](docs/development.md) | 开发规范、命名约定、错误处理模式 |
| [docs/configuration.md](docs/configuration.md) | appsettings.json 每一项含义 |
| [docs/deployment.md](docs/deployment.md) | IIS 部署、端口、DataProtection 密钥 |
| [docs/runbook.md](docs/runbook.md) | 运维手册:常见故障、日志位置、应急 |
| [docs/monitoring.md](docs/monitoring.md) | 监控指标、告警阈值 |
| [docs/disaster-recovery.md](docs/disaster-recovery.md) | 备份策略、回滚步骤 |
| [docs/faq.md](docs/faq.md) | 常见问题 |
| [docs/code-review-onlyserver1-2026-07-07.md](docs/code-review-onlyserver1-2026-07-07.md) | **2026-07-07 代码审查报告(仅主项目)** —— 27 项发现 + 已修/未修分类 |
| [docs/code-review-2026-07-07.md](docs/code-review-2026-07-07.md) | 多项目审查报告(覆盖 Tests/DbMigrator) |
| [docs/add-drawing-numbers-ux.md](docs/add-drawing-numbers-ux.md) | AddDrawingNumbers 页面 UX 决策(折叠/响应式/同色系背景) |

## 架构决策(ADR)

| # | 决策 |
|---|------|
| [0001](docs/adr/0001-use-sqlsugar-instead-of-ef-core.md) | 用 SqlSugar 替代 EF Core |
| [0002](docs/adr/0002-custom-identity-store-on-sqlsugar.md) | 自定义 SqlSugarUserStore / RoleStore |
| [0003](docs/adr/0003-file-scoped-namespace-convention.md) | 全项目统一 file-scoped namespace |
| [0004](docs/adr/0004-handle-base-c0x-with-abstract-class.md) | C0XBase abstract + 9 个 handler 多态写入 |
| [0005](docs/adr/0005-replace-serializable-with-unique-index.md) | 用 UNIQUE INDEX 替代 SERIALIZABLE 事务 |

## 角色

| 角色 | 权限 |
|------|------|
| Admin | 全权(用户/部门/角色/订单/图号/图名) |
| Operator | 业务操作(图号分配、订单处理、图名维护) |
| Viewer | 只读(订单查询) |

## 贡献

参见 [CONTRIBUTING.md](CONTRIBUTING.md)。提 PR 前请确保:
- `dotnet build` 0 错误
- 单元测试通过
- 若改动架构/决策,新增对应 ADR