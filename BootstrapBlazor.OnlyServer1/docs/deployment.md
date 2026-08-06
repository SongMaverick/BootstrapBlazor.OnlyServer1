# 部署指南 (IIS)

> 目标环境: IIS 10 (Windows Server 2016+)
> 数据库: SQL Server
> 应用托管: ASP.NET Core 8 in-process hosting (ASP.NET Core Module V2)
> 部署工具: `scripts/publish-iis.ps1` + `scripts/create-iis-site.ps1`
> 访问入口: `https://<服务器 IP>:8373`

> **(2026-08-03 IIS 迁移)** 本文先前描述的 IIS 部署仍然适用,但补正:
> - 站点 / 应用池名从 `BootstrapBlazor` 改为 `DrawingNumber` / `DrawingNumberAppPool`
> - 端口从 80/443 改为自定义 8373(IIS 默认端口已被占用)
> - 不再用 `dotnet publish` 手写命令,改用 `scripts/publish-iis.ps1`
> - 不再手工 icacls,改用 `scripts/create-iis-site.ps1` 自动设
> - HTTPS 终结由 IIS 站点绑定,**不再需要** Kestrel 节点 / PFX 文件
>
> 给 IT 的**完整操作手册**见 [deploy-it-runbook.md](deploy-it-runbook.md)。

---

## 1. 先决条件

| 组件 | 来源 | 验证 |
|------|------|------|
| **.NET 8 Hosting Bundle** | https://dotnet.microsoft.com/download/dotnet/8.0 | `(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release` ≥ `528040` |
| **IIS WebSocket Protocol** | `Install-WindowsFeature Web-WebSocket` | `Get-WindowsFeature Web-WebSocket` → `Installed` |
| **ASP.NET Core Module V2** | 随 Hosting Bundle 自动装 | `Get-WebGlobalModule -Name AspNetCoreModuleV2` |
| **HTTPS 证书** | LocalMachine\My | `Get-ChildItem Cert:\LocalMachine\My \| ? NotAfter -gt (Get-Date)` |

---

## 2. 编译 & 发布

**开发机**运行 `scripts/publish-iis.ps1`:

```powershell
cd BootstrapBlazor.OnlyServer1\scripts
.\publish-iis.ps1
# 默认输出到 D:\Publish\DrawingNumber\IIS\
```

**publish 产物**(framework-dependent,约 25 MB):
```
BootstrapBlazor.OnlyServer1.dll
BootstrapBlazor.OnlyServer1.deps.json
BootstrapBlazor.OnlyServer1.runtimeconfig.json
NLog.config
web.config
wwwroot/
appsettings.json
appsettings.Production.json    # IIS 路径下可为空占位
```

**关键差异**(对比 Windows Service 时代):
- 没有 `BootstrapBlazor.OnlyServer1.exe`(IIS 用 `dotnet .\BootstrapBlazor.OnlyServer1.dll` 加载)
- 没有 `certs/` 目录(HTTPS 终结在 IIS,不在应用里)
- 不带 `runtimes/` 子目录(依赖服务器 Hosting Bundle)

---

## 3. 服务器部署

**服务器管理员 PowerShell**:

```powershell
# 1. 解压 publish 产物到 IIS 站点物理路径
Expand-Archive -Path 'C:\Users\IT\Downloads\DrawingNumber_IIS.zip' `
               -DestinationPath 'D:\inetpub\wwwroot\DrawingNumber\' -Force

# 2. 跑 create-iis-site.ps1(一次性建 AppPool + Site + Bindings + 权限)
cd D:\inetpub\wwwroot\DrawingNumber\scripts
.\create-iis-site.ps1 `
    -InstallDir 'D:\inetpub\wwwroot\DrawingNumber' `
    -CertThumbprint 'ABCDEF1234567890ABCDEF1234567890ABCDEF12'

# 3. (跨服务器搬站保留登录态时才用)
#    .\create-iis-site.ps1 ... -CopyKeysFrom '\\oldserver\D$\...\keys'
```

`create-iis-site.ps1` 自动做的事(详见脚本注释):
- AppPool:`NoManagedCode` / `AlwaysRunning` / `IdleTimeout=0` / `PeriodicRestart=0` / `ApplicationPoolIdentity`
- AppPool 环境变量:`ASPNETCORE_ENVIRONMENT=Production`(强制覆盖)
- Site:`DrawingNumber`,物理路径 = `InstallDir`
- HTTP binding:`*:8373:`
- HTTPS binding:`*:8373:` + 证书 thumbprint
- `keys/` `logs/` 自动建 + `icacls IIS_IUSRS:(OI)(CI)M`
- 预热首请求

---

## 4. AppPool 配置(`create-iis-site.ps1` 自动设置,这里只是文档)

| 项 | 值 | 原因 |
|----|---|------|
| Name | `DrawingNumberAppPool` | 任意命名 |
| .NET CLR version | **No Managed Code** | ASP.NET Core 用 in-process hosting |
| Managed Pipeline Mode | Integrated | |
| **Start Mode** | `AlwaysRunning` | 否则空闲超时后 Worker 关闭,首请求 502.5 |
| **Idle Time-out (minutes)** | `0` | **禁用空闲超时**,否则 Blazor Server 长连接 20 分钟断 |
| **PeriodicRestart.Time** | `00:00:00` | **关键**:禁用 29h 周期回收,否则每 29 小时 SignalR 全员断连 |
| **Rapid-Fail Protection** | Enabled(默认) | 进程崩了 IIS 不会无限重启 |
| **Identity** | ApplicationPoolIdentity | 默认;访问 `keys/` `logs/` 走 IIS_IUSRS 组 |

---

## 5. AppPool 环境变量

`create-iis-site.ps1` 自动注入 `ASPNETCORE_ENVIRONMENT=Production`。

如需覆盖其他配置,**用 IIS AppPool → Advanced Settings → Environment Variables**(避免密码进 git):

| 变量名 | 值 | 说明 |
|--------|---|------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | 已由脚本注入;**误设为 Development 会启用 DetailedErrors,500 错误回显完整堆栈** |
| `DataProtection__KeyDirectory` | `D:\DrawingNumber\keys` | 可选。覆盖 appsettings 默认 `<app-root>\keys`。建议指独立盘 |
| `ConnectionStrings__sqlServerConnectionStringOffice` | `Server=...;uid=...;password=...` | 注意 `__` 双下划线转义 . 嵌套键 |

> **敏感配置**: 生产环境**不要**在 `appsettings.json` 写明文密码,改用环境变量。

---

## 6. 文件夹权限(`create-iis-site.ps1` 自动设置)

| 目录 | 权限 | 原因 |
|---|---|---|
| `<InstallDir>\keys\` | `IIS_IUSRS:(OI)(CI)M` | DataProtection 启动时生成 / 读 key-<guid>.xml |
| `<InstallDir>\logs\` | `IIS_IUSRS:(OI)(CI)M` | NLog fallback + ANCM stdout 日志 |
| `<InstallDir>\wwwroot\` | 默认(读) | 静态资源 |

如果想手动验证 / 修复:`icacls "<app-root>\keys" /grant "IIS_IUSRS:(OI)(CI)M" /T`。

---

## 7. 数据库初始化(首次部署)

```powershell
# 用 SA 账号跑 DbMigrator
dotnet run --project BootstrapBlazor.DbMigrator -- --drop --schema --seed --migrate --source .

# 生产环境绝对不要传 --drop(会删表)
# 已有库只跑增量: --schema(检测后 ADD COLUMN / 加索引,不丢数据)
dotnet run --project BootstrapBlazor.DbMigrator -- --schema --seed
```

详见 [database.md](database.md#迁移流程)。

---

## 8. 部署后首次访问验证

```powershell
# AppPool 是否启动
Get-WebAppPoolState -Name 'DrawingNumberAppPool'

# 站点状态
Get-Website -Name 'DrawingNumber' | Select State, PhysicalPath, Bindings

# HTTP 应 301 跳 HTTPS(IIS rewrite 规则)
$resp = Invoke-WebRequest 'http://localhost:8373/' -MaximumRedirection 0 -ErrorAction SilentlyContinue
$resp.StatusCode   # 301
$resp.Headers.Location   # https://...

# HTTPS 应 200
Invoke-WebRequest 'https://localhost:8373/' -UseBasicParsing -SkipCertificateCheck | Select StatusCode
```

预期: HTTP 301 → HTTPS 200,看到登录页。

如遇 502.5 / 503:
1. 查 stdout 日志 `<InstallDir>\logs\stdout_*.log`
2. 查 Windows Application Event Log(来源 = `IIS AspNetCore Module V2`)
3. 确认 .NET 8 Hosting Bundle 已装

---

## 9. Blazor Server + SignalR 特别注意

### 9.1 WebSocket 必需

已固化在 `web.config` 的 `<webSocket enabled="true"/>`。前提是 IIS 装了 **WebSocket Protocol** 角色服务。

验证: 站点主页打开后浏览器 Network 面板应看到 `ws://` 连接。

### 9.2 长连接存活

`App.razor` 已配 `<ReconnectorOutlet ReconnectInterval="5000"/>`,客户端掉线 5 秒后自动重连。

AppPool 的 `PeriodicRestart.Time = 00:00:00` 禁用 29h 周期回收(见第 4 节)。

### 9.3 HTTP→HTTPS 重定向

`web.config` 的 `<rewrite>` 规则在 IIS 层 301 跳转,**不需要** `Program.cs` 配 `UseHttpsRedirection`(ANCM in-process 下永远不触发)。

### 9.4 反向代理(若有)

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

---

## 10. DataProtection 密钥

服务器运行时会在 `<DataProtection:KeyDirectory>`(默认 `<InstallDir>\keys\`)自动生成 `key-<guid>.xml` 文件。

- **首次部署**: 会自动生成新 key。
- **后续部署 / 升级**: 保留旧 key → 用户登录态不丢;删 key = 全员强制重新登录。
- **跨服务器搬站**: 用 `create-iis-site.ps1 -CopyKeysFrom <path>` 把旧 key 拷过来,保留登录态。
- **滚动**: 默认 90 天后自动生成新 key,旧的保留到最近 2 个。

`Program.cs:131` 显式 `SetApplicationName("BootstrapBlazor.OnlyServer1")`,跨服务器保持一致即可。

---

## 11. 升级 / 发布流程

开发机:

```powershell
cd BootstrapBlazor.OnlyServer1\scripts
.\publish-iis.ps1 -OutputDir 'D:\Publish\DrawingNumber\IIS'
```

服务器(管理员 PowerShell):

```powershell
# 1. 备份当前(保留 keys/ logs/)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item 'D:\inetpub\wwwroot\DrawingNumber' `
          "D:\inetpub\wwwroot\DrawingNumber_BAK_$stamp" -Recurse -Force

# 2. 停 AppPool(让 SignalR 长连接断掉,释放文件锁)
Restart-WebAppPool -Name 'DrawingNumberAppPool'

# 3. 用新产物覆盖(跳过 keys/ logs/)
robocopy 'D:\Publish\DrawingNumber\IIS' 'D:\inetpub\wwwroot\DrawingNumber' `
        /MIR /XD keys logs /NP

# 4. Recycle 加载新 .dll
Restart-WebAppPool -Name 'DrawingNumberAppPool'

# 5. 验证
Get-WebAppPoolState -Name 'DrawingNumberAppPool'
Invoke-WebRequest 'https://localhost:8373/' -UseBasicParsing -SkipCertificateCheck | Select StatusCode
```

如果本次发布**只改前端/业务代码**,**不需要跑 DbMigrator**;只改 Models schema 时才需要 `--schema`。

---

## 12. 诊断入口

| 入口 | 看什么 |
|------|-------|
| `<InstallDir>\logs\stdout_*.log` | ANCM stdout/stderr(启动崩溃回溯) |
| `<InstallDir>\logs\fallback-*.log` | NLog 本地兜底(DB target 异常时) |
| SQL Server `Logs` 表 | NLog DB target 主输出 |
| IIS AppPool → Advanced → 日志 | IIS 侧请求日志 |
| Windows Event Viewer → Application | .NET 异常 + AspNetCore Module 错误 |
| `curl http://localhost:8373/_framework/blazor.web.js` | 静态资源 200 = Blazor wiring OK |

---

## 13. 故障排查快速参考

| 现象 | 看 | 原因 / 修复 |
|------|---|------------|
| 502.5 | stdout log + Event Log | 多半是 .NET 8 Hosting Bundle 没装 / 版本过低 |
| 首请求慢(5-15s) | AppPool Start Mode | 改成 `AlwaysRunning`;或装 Application Initialization 角色服务 |
| 20 分钟断连 | AppPool Idle Timeout | 改为 `0`(脚本已自动设) |
| 29 小时全员断连 | AppPool PeriodicRestart | 改为 `00:00:00`(脚本已自动设) |
| 30 秒首连断 | WebSocket 没装 | `Install-WindowsFeature Web-WebSocket` |
| 所有人登录页 401 | DataProtection key 没了 | 删了 `keys/` 目录会导致全员重登录;跨服务器搬站用 `-CopyKeysFrom` |
| NLog 写不进文件 | `logs/` 权限 | 重跑 `create-iis-site.ps1`(脚本自动 icacls) |
| Cookie Secure 标记失效 | appsettings 没切 Production | 检查 AppPool 环境变量 `ASPNETCORE_ENVIRONMENT=Production` |
| HTTP 不跳 HTTPS | web.config rewrite 规则丢失 | 检查 `web.config /<system.webServer>/<rewrite>` 是否存在 |
| SqlException 列名无效 | `Logs` 表或 schema 不匹配 | 跑 `DbMigrator --schema` |
| SqlSugar 卡死 1-2 分钟 | `Logs` 表 / 索引缺失 | 见 ADR 0005,确保 `IX_Orders_OrderNumber` 存在 |

完整运维手册(给 IT):[deploy-it-runbook.md](deploy-it-runbook.md)
