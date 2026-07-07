# 部署指南 / IIS

> 项目：编号本（内部系统）  
> 目标环境：IIS 10（Windows Server）  
> 数据库：SQL Server（已配置）  
> 应用托管：ASP.NET Core 8 in-process hosting

## 1. 先决条件

IIS 服务器必须安装以下角色服务 / 工具：

| 组件 | 来源 | 验证方式 |
|---|---|---|
| **.NET 8 Hosting Bundle** | https://dotnet.microsoft.com/download/dotnet/8.0 | `dotnet --list-runtimes` 看到 `Microsoft.AspNetCore.App 8.x` |
| **IIS WebSocket Protocol** | Server Manager → Web Server (IIS) → Add Role Services → WebSocket Protocol | IIS → 站点 → WebSocket Protocols 模块可见 |
| **ASP.NET Core Module V2** | 随 Hosting Bundle 自动装 | `Get-ChildItem IIS:\Modules` 看到 `AspNetCoreModuleV2` |

## 2. 编译 & 发布

开发机执行：

```powershell
cd D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.OnlyServer1
dotnet publish -c Release -o D:\Publish\DrawingNumber `
  -p:PublishSingleFile=false -p:PublishReadyToRun=true
```

publish 产物必须包含：
```
BootstrapBlazor.OnlyServer1.dll          # 主程序集
BootstrapBlazor.OnlyServer1deps.json
BootstrapBlazor.OnlyServer1.runtimeconfig.json
NLog.config
web.config                                # ← 已固化进项目根(csproj CopyToOutput)
DrawingName.sql
```

## 3. IIS AppPool 配置

**IIS Manager → Application Pools → Add Application Pool：**

| 项 | 值 | 原因 |
|---|---|---|
| Name | `DrawingNumberAppPool` | 任意命名 |
| .NET CLR version | **No Managed Code** | ASP.NET Core 用 in-process hosting，不需要 CLR |
| Managed Pipeline Mode | Integrated | |
| **Start Mode** | `AlwaysRunning` | 否则空闲超时后 Worker 关闭，首请求 502.5 |
| **Idle Time-out (minutes)** | `0` | **禁用空闲超时**，否则 Blazor Server 长连接 20 分钟断 |
| **Idle Time-out Action** | `No Action` | 配合上条 |
| **Rapid-Fail Protection** | Enabled（默认） | 进程崩了 IIS 不会无限重启 |

## 4. IIS 站点配置

**Add Website：**

| 项 | 值 |
|---|---|
| Site name | `DrawingNumber` |
| Physical path | `D:\inetpub\wwwroot\DrawingNumber`（先部署，再建站点） |
| Binding | `http`:80（建议另加 `https`:443，看内网策略）|
| Application Pool | `DrawingNumberAppPool` |

## 5. AppPool 环境变量

**Application Pool → Advanced Settings → Environment Variables：**

| 变量名 | 值 | 说明 |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | 决定 appsettings 加载 |
| `DataProtection__KeyDirectory` | `D:\DrawingNumber\keys` | 可选。覆盖 appsettings 默认 `<app-root>\keys`。建议指独立盘 |
| `ConnectionStrings__sqlServerConnectionStringOffice` | `Server=...;uid=...;password=...` | 注意 `__` 双下划线转义 . 嵌套键 |

> 注意：本项目暂未把密码从 appsettings.json 迁出（见"待办"）。正式部署前应改用环境变量注入，详情见仓库单独的"密码清理"任务。

> ⚠️ **(2026-07-07 Review #M7)** `ASPNETCORE_ENVIRONMENT` **必须** = `Production`。
> 误设为 `Development` 会启用 `appsettings.Development.json:DetailedErrors=true`,
> 所有 500 错误会向客户端回显完整堆栈(包含文件路径、配置键、SQL 错误细节)。
> 第 7.x 节 "部署后必查清单" 列了上线必查项。

## 6. 文件夹权限

部署后首次启动前，运维执行（替换 `<app-root>` 为实际路径）：

```powershell
# 日志目录：IIS_IUSRS 写权限 + 自动创建
New-Item -ItemType Directory -Force -Path "<app-root>\logs"
icacls "<app-root>\logs" /grant "IIS_IUSRS:(OI)(CI)M" /T

# DataProtection 密钥目录
New-Item -ItemType Directory -Force -Path "<app-root>\keys"
icacls "<app-root>\keys" /grant "IIS_IUSRS:(OI)(CI)M" /T

# stdout 日志目录（web.config 已配置 stdoutLogFile，IIS 会自动创建，但提前建好更稳）
icacls "<app-root>\logs" /grant "IIS_IUSRS:(OI)(CI)M"
```

如果不想给 `logs/` 应用写权限，可以改用 nlog config 的固定绝对路径 + 给文件夹预授权。

## 7. 部署后首次访问验证

```powershell
# 看 IIS 站点是否起来
Get-WebSite -Name "DrawingNumber" | Select State, PhysicalPath

# 看 AppPool 是否运行
Get-WebAppPoolState -Name "DrawingNumberAppPool"

# 拉首页验证 200
Invoke-WebRequest http://localhost/ -UseBasicParsing | Select StatusCode, StatusDescription
```

预期：返回 HTML 首页（`Bootstrap Blazor Server App` 标题），不是 500 / 502.5 / 503。

如遇 502.5：
1. 先查 stdout 日志 `<app-root>\logs\stdout_*.log`
2. 再查 Windows Application Event Log（来源 = `IIS AspNetCore Module V2`）
3. 确认 .NET 8 Hosting Bundle 已装

## 7.x 部署后必查清单(2026-07-07 Review #M7 / 安全基线)

部署完毕后**逐项人工核查**,全部 ✅ 才算上线就绪:

### 安全配置核查

- [ ] **ASPNETCORE_ENVIRONMENT = Production** ⚠️
      IIS AppPool → Advanced Settings → Environment Variables → `ASPNETCORE_ENVIRONMENT=Production`
      *误设为 Development 会启用 `DetailedErrors`,500 错误会向客户端回显完整堆栈*
- [ ] **`/error` 页面验证**
      ```powershell
      # 触发一个 500(临时把 webconfig 里 stdoutLogFile 路径改成根目录只读路径),然后访问 http://服务器/error
      # 不应看到 "An unhandled exception has occurred" 之外的内
      ```
- [ ] **HTTPS 已部署** — 站点至少一个 HTTPS 绑定,`web.config:27-30` 的 HSTS 才能生效
- [ ] **生产密码** 不在 `appsettings.json` 里,只走 IIS AppPool 环境变量(`ConnectionStrings__xxxOffice`)

### 权限审计

- [ ] **操作类 Server 全部走 CurrentUserAccessor 校验**(看 `Services/Identity/CurrentUserAccessor.cs:RequireRole` 注释段)
- [ ] **Login 表单含 antiforgery 字段** — 浏览器 DevTools → Elements → 搜 `__RequestVerificationToken`
- [ ] **DataProtection 密钥不丢** — `keys/` 已存在且 IIS_IUSRS 有读权限;跨服务器迁移时拷过去

### 上线前的最后扫描

```powershell
# 1. 确认无 Trace 级日志泄漏
Get-WebConfigFile "$env:HOMEDRIVE\inetpub\wwwroot\DrawingNumber\web.config" |
  Select-String "stdoutLogEnabled"   # 应为 true
# 2. 确认 appsettings.Production.json 已建(或变量已设)
Test-Path "$env:HOMEDRIVE\inetpub\wwwroot\DrawingNumber\appsettings.Production.json"
# 3. 确认连接串不再含密码
Select-String -Path "$env:HOMEDRIVE\inetpub\wwwroot\DrawingNumber\appsettings.json" -Pattern "password="
#    不应有输出;或者输出仅占位符
```

---

## 8. Blazor Server + SignalR 特别注意

### 8.1 WebSocket 必需
已固化在 `web.config` 的 `<webSocket enabled="true"/>`。前提是 IIS 装了 **WebSocket Protocol** 角色服务。

验证：站点主页打开后浏览器 Network 面板应看到 `ws://` 连接。

### 8.2 长连接存活
`App.razor` 已配 `<ReconnectorOutlet ReconnectInterval="5000"/>`，客户端掉线 5 秒后自动重连。

### 8.3 反向代理（若有）
如果 IIS 前面挂了 ARR / Nginx / 网关，需在 `Program.cs` 加：

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

当前项目未启用——若部署架构有代理层，必须加。

## 9. DataProtection 密钥

服务器运行时会在 `<DataProtection:KeyDirectory>` 自动生成 `key-<guid>.xml` 文件。

- **首次部署**：会自动生成新 key。
- **后续部署 / 升级**：保留旧 key → 用户登录态不丢；删 key = 全员强制重新登录。
- **滚动**：默认 90 天后自动生成新 key，旧的保留到最近 2 个。

跨服务器拷贝时一致：保持 `SetApplicationName("BootstrapBlazor.OnlyServer1")` 一致即可（已写在 `Program.cs`）。

## 10. 诊断 & 监控

| 入口 | 看什么 |
|---|---|
| `<app-root>\logs\stdout_*.log` | 进程 stdout/stderr（崩溃回溯）|
| `<app-root>\logs\fallback-*.log` | NLog 本地兜底（DB target 异常时） |
| SQL Server `Logs` 表 | NLog DB target 主输出（NLog.config 已配） |
| IIS AppPool → Advanced → 日志 | IIS 侧请求日志 |
| Windows Event Viewer → Application | .NET 异常 + AspNetCore Module 错误 |
| `curl http://localhost/_framework/blazor.web.js` | 静态资源 200 = Blazor wiring OK |

## 11. 健康检查 + 升级流程

```powershell
# 1. 编译发布（同上）
dotnet publish -c Release -o D:\Publish\DrawingNumber

# 2. 备份当前
$bak = "D:\Publish\DrawingNumber_BAK_{0:yyyyMMddHHmm}" -f (Get-Date)
Copy-Item D:\inetpub\wwwroot\DrawingNumber $bak -Recurse

# 3. 停 AppPool（让 SignalR 长连接断掉）
Stop-WebAppPool -Name "DrawingNumberAppPool" -ErrorAction SilentlyContinue

# 4. 拷新产物（保留 keys/ 和 nlog/DB 连接配置）
Copy-Item D:\Publish\DrawingNumber\* D:\inetpub\wwwroot\DrawingNumber -Recurse -Force

# 5. 启 AppPool
Start-WebAppPool -Name "DrawingNumberAppPool"

# 6. 验证
Invoke-WebRequest http://localhost/ -UseBasicParsing | Select StatusCode
```

## 12. 待办（部署前必须解决）

- [ ] **密码清理**：`appsettings.json` 含明文 DB 密码入库，应改用环境变量（IIS AppPool Advanced → Environment Variables），并 `git rm --cached appsettings.json`
- [ ] Cookie CSRF：登录 POST 加 `[ValidateAntiForgeryToken]` + 前端表单加 antiforgery token（防 logout CSRF 骚扰）
- [ ] `SignalR KeepAliveInterval/ClientTimeoutInterval` 显式设置

## 13. 故障排查快速参考

| 现象 | 看 | 原因 / 修复 |
|---|---|---|
| 502.5 | stdout log + Event Log | 多半是 .NET 8 Hosting Bundle 没装 |
| 首请求慢 | AppPool Start Mode | 改成 `AlwaysRunning` |
| 20 分钟断连 | AppPool Idle Timeout | 改为 0 |
| 30 秒首连断 | WebSocket 没装 | 装 WebSocket Protocol 模块 |
| 所有人登录页 401 | DataProtection key 没了 | 删了 `keys/` 目录会导致全员重登录 |
| NLog 写不进文件 | `logs/` 权限 | 给 IIS_IUSRS 加 M 权限 |
| Cookie 失效 | 服务器迁移未拷 `keys/` | 同步 DataProtection 密钥目录 |
