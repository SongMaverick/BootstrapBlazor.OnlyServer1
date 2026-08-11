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

## 文档

不再以独立 markdown 文件维护。架构、部署、运维、FAQ、ADR 等内容需要时写在代码注释或相关文件中。各项目自带的 README 优先(如 `BootstrapBlazor.OrderManagementSetup/README.md`)。

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