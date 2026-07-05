# 部署指南 (IIS)

> 目标环境: IIS 10 (Windows Server)
> 数据库: SQL Server
> 应用托管: ASP.NET Core 8 in-process hosting

## 1. 先决条件

| 组件 | 来源 | 验证 |
|------|------|------|
| **.NET 8 Hosting Bundle** | https://dotnet.microsoft.com/download/dotnet/8.0 | `dotnet --list-runtimes` 看到 `Microsoft.AspNetCore.App 8.x` |
| **IIS WebSocket Protocol** | Server Manager → Web Server → Add Role Services → WebSocket Protocol | IIS → 站点 → WebSocket Protocols 模块可见 |
| **ASP.NET Core Module V2** | 随 Hosting Bundle 自动装 | `Get-ChildItem IIS:\Modules` 看到 `AspNetCoreModuleV2` |

## 2. 编译 & 发布

```powershell
cd BootstrapBlazor.OnlyServer1
dotnet publish -c Release -o D:\Publish\BootstrapBlazor `
  -p:PublishSingleFile=false -p:PublishReadyToRun=true
```

**publish 产物必须包含**:
```
BootstrapBlazor.OnlyServer1.dll
BootstrapBlazor.OnlyServer1deps.json
BootstrapBlazor.OnlyServer1.runtimeconfig.json
NLog.config
web.config                              # ← 已固化进项目根(csproj CopyToOutput)
```

## 3. IIS AppPool 配置

| 项 | 值 | 原因 |
|----|---|------|
| Name | `BootstrapBlazor` | 任意命名 |
| .NET CLR version | **No Managed Code** | ASP.NET Core 用 in-process hosting,不需要 CLR |
| Managed Pipeline Mode | Integrated | |
| **Start Mode** | `AlwaysRunning` | 否则空闲超时后 Worker 关闭,首请求 502.5 |
| **Idle Time-out (minutes)** | `0` | **禁用空闲超时**,否则 Blazor Server 长连接 20 分钟断 |
| **Idle Time-out Action** | `No Action` | 配合上条 |
| **Rapid-Fail Protection** | Enabled(默认) | 进程崩了 IIS 不会无限重启 |

## 4. IIS 站点配置

| 项 | 值 |
|----|---|
| Site name | `BootstrapBlazor` |
| Physical path | `D:\inetpub\wwwroot\BootstrapBlazor`(先部署,再建站点) |
| Binding | `http`:80(建议另加 `https`:443,看内网策略) |
| Application Pool | `BootstrapBlazor` |

## 5. AppPool 环境变量

**Application Pool → Advanced Settings → Environment Variables:**

| 变量名 | 值 | 说明 |
|--------|---|------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | 决定 appsettings 加载 |
| `DataProtection__KeyDirectory` | `D:\BootstrapBlazor\keys` | 可选。覆盖 appsettings 默认 `<app-root>\keys`。建议指独立盘 |
| `ConnectionStrings__sqlServerConnectionStringOffice` | `Server=...;uid=...;password=...` | 注意 `__` 双下划线转义 . 嵌套键 |

> **敏感配置**: 生产环境**不要**在 `appsettings.json` 写明文密码,改用环境变量。
> Web 项目默认用 `sqlServerConnectionStringHomeSA`(本地),生产应通过环境变量覆盖为 `sqlServerConnectionStringOffice` 或 `sqlServerConnectionStringSA`。

## 6. 文件夹权限

部署后首次启动前,运维执行(替换 `<app-root>` 为实际路径):

```powershell
# 日志目录:IIS_IUSRS 写权限 + 自动创建
New-Item -ItemType Directory -Force -Path "<app-root>\logs"
icacls "<app-root>\logs" /grant "IIS_IUSRS:(OI)(CI)M" /T

# DataProtection 密钥目录
New-Item -ItemType Directory -Force -Path "<app-root>\keys"
icacls "<app-root>\keys" /grant "IIS_IUSRS:(OI)(CI)M" /T
```

如果不想给 `logs/` 应用写权限,可以改用 NLog config 的固定绝对路径 + 给文件夹预授权。

## 7. 数据库初始化(首次部署)

```powershell
# 用 SA 账号跑 DbMigrator
dotnet run --project BootstrapBlazor.DbMigrator -- --drop --schema --seed --migrate --source .

# 生产环境绝对不要传 --drop(会删表)
# 已有库只跑增量: --schema(检测后 ADD COLUMN / 加索引,不丢数据)
dotnet run --project BootstrapBlazor.DbMigrator -- --schema --seed
```

详见 [database.md](database.md#迁移流程)。

## 8. 部署后首次访问验证

```powershell
# 看 IIS 站点是否起来
Get-WebSite -Name "BootstrapBlazor" | Select State, PhysicalPath

# 看 AppPool 是否运行
Get-WebAppPoolState -Name "BootstrapBlazor"

# 拉首页验证 200
Invoke-WebRequest http://localhost/ -UseBasicParsing | Select StatusCode, StatusDescription
```

预期: 返回 HTML 首页(`Bootstrap Blazor Server App` 标题),不是 500 / 502.5 / 503。

如遇 502.5:
1. 查 stdout 日志 `<app-root>\logs\stdout_*.log`
2. 查 Windows Application Event Log(来源 = `IIS AspNetCore Module V2`)
3. 确认 .NET 8 Hosting Bundle 已装

## 9. Blazor Server + SignalR 特别注意

### 9.1 WebSocket 必需

已固化在 `web.config` 的 `<webSocket enabled="true"/>`。前提是 IIS 装了 **WebSocket Protocol** 角色服务。

验证: 站点主页打开后浏览器 Network 面板应看到 `ws://` 连接。

### 9.2 长连接存活

`App.razor` 已配 `<ReconnectorOutlet ReconnectInterval="5000"/>`,客户端掉线 5 秒后自动重连。

### 9.3 反向代理(若有)

如果 IIS 前面挂了 ARR / Nginx / 网关,需在 `Program.cs` 加:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = 
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
// 在 builder.Build() 之后:
app.UseForwardedHeaders();
```

当前项目未启用 — 若部署架构有代理层,必须加。

## 10. DataProtection 密钥

服务器运行时会在 `<DataProtection:KeyDirectory>` 自动生成 `key-<guid>.xml` 文件。

- **首次部署**: 会自动生成新 key。
- **后续部署 / 升级**: 保留旧 key → 用户登录态不丢;删 key = 全员强制重新登录。
- **滚动**: 默认 90 天后自动生成新 key,旧的保留到最近 2 个。

跨服务器拷贝时一致: 保持 `SetApplicationName("BootstrapBlazor.OnlyServer1")` 一致即可(已写在 `Program.cs`)。

## 11. 升级 / 发布流程

```powershell
# 1. 编译发布
dotnet publish -c Release -o D:\Publish\BootstrapBlazor

# 2. 备份当前
$bak = "D:\Publish\BootstrapBlazor_BAK_{0:yyyyMMddHHmm}" -f (Get-Date)
Copy-Item D:\inetpub\wwwroot\BootstrapBlazor $bak -Recurse

# 3. 停 AppPool(让 SignalR 长连接断掉)
Stop-WebAppPool -Name "BootstrapBlazor" -ErrorAction SilentlyContinue

# 4. 拷新产物(保留 keys/ 和 nlog/DB 连接配置)
Copy-Item D:\Publish\BootstrapBlazor\* D:\inetpub\wwwroot\BootstrapBlazor -Recurse -Force

# 5. 启 AppPool
Start-WebAppPool -Name "BootstrapBlazor"

# 6. 验证
Invoke-WebRequest http://localhost/ -UseBasicParsing | Select StatusCode
```

如果本次发布**只改前端/业务代码**,**不需要跑 DbMigrator**;只改 Models schema 时才需要 `--schema`。

## 12. 诊断入口

| 入口 | 看什么 |
|------|-------|
| `<app-root>\logs\stdout_*.log` | 进程 stdout/stderr(崩溃回溯) |
| `<app-root>\logs\fallback-*.log` | NLog 本地兜底(DB target 异常时) |
| SQL Server `Logs` 表 | NLog DB target 主输出 |
| IIS AppPool → Advanced → 日志 | IIS 侧请求日志 |
| Windows Event Viewer → Application | .NET 异常 + AspNetCore Module 错误 |
| `curl http://localhost/_framework/blazor.web.js` | 静态资源 200 = Blazor wiring OK |

## 13. 故障排查快速参考

| 现象 | 看 | 原因 / 修复 |
|------|---|------------|
| 502.5 | stdout log + Event Log | 多半是 .NET 8 Hosting Bundle 没装 |
| 首请求慢 | AppPool Start Mode | 改成 `AlwaysRunning` |
| 20 分钟断连 | AppPool Idle Timeout | 改为 0 |
| 30 秒首连断 | WebSocket 没装 | 装 WebSocket Protocol 模块 |
| 所有人登录页 401 | DataProtection key 没了 | 删了 `keys/` 目录会导致全员重登录 |
| NLog 写不进文件 | `logs/` 权限 | 给 IIS_IUSRS 加 M 权限 |
| Cookie 失效 | 服务器迁移未拷 `keys/` | 同步 DataProtection 密钥目录 |
| SqlException 列名无效 | `Logs` 表或 schema 不匹配 | 跑 `DbMigrator --schema` |
| SqlSugar 卡死 1-2 分钟 | `Logs` 表 / 索引缺失 | 见 ADR 0005,确保 `IX_Orders_OrderNumber` 存在 |