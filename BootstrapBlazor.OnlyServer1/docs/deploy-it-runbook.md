# DrawingNumber 系统部署手册(给 IT)— IIS 路径

> **给谁看**:负责把这套系统装到 Windows 服务器上的工程师。
>
> **目标**:让一个会装普通软件、不一定懂 .NET / IIS / HTTPS 的 IT 同事,按本文操作一遍能把系统跑起来,出问题时能自己排查 80% 的常见故障,排查不了的知道该把哪些信息发回来。
>
> **形态**:**IIS + ASP.NET Core Module V2**(`create-iis-site.ps1` 一键创建 AppPool + Site + Bindings)。
>
> **不需要懂的东西**:ASP.NET Core / Kestrel / HTTPS 协议细节 / 证书生成 — 本文只教"做什么、命令是什么、为什么"。

---

## 0. 在开始之前,先确认这 7 件事

| # | 检查项 | 怎么查 | 不通过怎么办 |
|---|---|---|---|
| 1 | 服务器是 **Windows Server 2016 或更新** | `Win + R` → 输入 `winver` → 看版本 | 不支持,换机器 |
| 2 | 你有**本机管理员**权限 | `lusrmgr.msc` → 看自己是否在 Administrators 组 | 联系系统管理员加权限 |
| 3 | 服务器**能 ping 通** SQL Server `JINWAD5A.AD101.SIEMENS-ENERGY.NET` | PowerShell 跑 `Test-NetConnection JINWAD5A.AD101.SIEMENS-ENERGY.NET -Port 1433`,返回 `TcpTestSucceeded: True` 即可 | 联系网络/数据库管理员放行 |
| 4 | **IIS 角色已装** | `Get-WindowsFeature Web-Server` → `Install State: Installed` | `Install-WindowsFeature Web-Server -IncludeManagementTools` |
| 5 | **.NET 8 Hosting Bundle 已装** | `(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release` 返回 ≥ `528040` | https://dotnet.microsoft.com/download/dotnet/8.0 → 选 **ASP.NET Core Runtime 8.x Hosting Bundle** |
| 6 | **IIS WebSocket Protocol 已装**(Blazor Server SignalR 必须) | `Get-WindowsFeature Web-WebSocket` → `Installed` | `Install-WindowsFeature Web-WebSocket` |
| 7 | **HTTPS 证书已在服务器 LocalMachine\My** | `Get-ChildItem Cert:\LocalMachine\My \| ? NotAfter -gt (Get-Date) \| Format-List Subject, Thumbprint, NotAfter` | 若自签:用 IIS Manager → Server Certificates → Create Self-Signed Certificate;若 CA 签:导入 .pfx 到 LocalMachine\My。**记下 Thumbprint 备用** |

**全部通过,继续**。任意一条不过,先解决,再往下走。

---

## 1. 你会拿到什么

项目负责人会通过内网共享 / U 盘 / 邮件给你一个**压缩包**(约 25 MB),名字类似 `DrawingNumber_IIS.zip`。

里面长这样(只是让你认个脸,不用管):
```
DrawingNumber_IIS/
├── BootstrapBlazor.OnlyServer1.dll        ← 主程序集(ANCM 加载这个)
├── BootstrapBlazor.OnlyServer1.deps.json
├── BootstrapBlazor.OnlyServer1.runtimeconfig.json
├── appsettings.json                       ← 公共配置(连数据库的)
├── appsettings.Production.json            ← 生产环境配置(可能为空占位)
├── NLog.config                            ← 日志配置
├── web.config                             ← ANCM + 自定义响应头 + HTTP→HTTPS 重定向
├── DrawingName.sql                        ← 数据库脚本(参考,不用现在跑)
├── wwwroot/                               ← 网页静态资源
├── scripts/
│   ├── publish-iis.ps1                    ← 已在开发机跑过,这里只列出来源
│   ├── create-iis-site.ps1                ← IT 在服务器上跑这个
│   └── remove-iis-site.ps1                ← 卸载时用
└── docs/
    └── deploy-it-runbook.md               ← 本文件
```

**注意**:这套包里**不包含** `BootstrapBlazor.OnlyServer1.exe`(那是 Windows Service 时代的产物,IIS 路径下不需要 — ANCM 通过 `dotnet .\BootstrapBlazor.OnlyServer1.dll` 加载)。

---

## 2. 安装路径约定(强烈建议)

| 项 | 默认值 | 说明 |
|---|---|---|
| 站点物理路径 | `D:\inetpub\wwwroot\DrawingNumber\` | IIS 读取 .dll / web.config 的位置;不要放 C 盘根 |
| AppPool 名 | `DrawingNumberAppPool` | IIS 应用池,所有进程归属 |
| Site 名 | `DrawingNumber` | IIS 站点(一个站点 = 一个 hostname/port) |
| HTTP 端口 | `8373` | 用户确认 IIS 默认端口已被占用,沿用 Kestrel 时代的 8373 |
| HTTPS 端口 | `8373` | 同 HTTP 端口(若要分离,显式传参) |
| DataProtection 密钥目录 | `<站点物理路径>\keys\` | 自动创建,不要手工建 |
| 日志目录 | `<站点物理路径>\logs\` | 自动创建,NLog fallback + ANCM stdout |

**路径里不要有空格或中文**(IIS 对路径兼容性差)。

---

## 3. 解压

### 3.1 操作

把 `DrawingNumber_IIS.zip` 拷到服务器任意位置(比如桌面或 `C:\Users\<你>\Downloads\`),然后:

1. `Win + S` → 输入 `powershell` → 点 "Windows PowerShell"
2. 在 PowerShell 里跑:

```powershell
# 解压到 IIS 默认站点目录(请按需修改盘符)
Expand-Archive -Path "C:\Users\<你>\Downloads\DrawingNumber_IIS.zip" -DestinationPath "D:\inetpub\wwwroot\" -Force
```

**预期看到**:`D:\inetpub\wwwroot\DrawingNumber\BootstrapBlavor.OnlyServer1.dll` 等文件。

### 3.2 怎么看成没成功

```powershell
Test-Path "D:\inetpub\wwwroot\DrawingNumber\BootstrapBlavor.OnlyServer1.dll"
```

输出 `True` 就对了。

---

## 4. 拿到 HTTPS 证书 Thumbprint

> **为什么需要这一步**:`create-iis-site.ps1` 要把 HTTPS 绑定到 IIS 站点上,需要一张服务器上的证书和它的 Thumbprint。

### 4.1 操作

**管理员 PowerShell**:

```powershell
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.NotAfter -gt (Get-Date) } | Format-List Subject, Thumbprint, NotAfter
```

### 4.2 怎么看成没成功

输出类似:
```
Subject      : CN=drawing.example.com
Thumbprint   : ABCDEF1234567890ABCDEF1234567890ABCDEF12
NotAfter     : 2027-08-03 ...
```

**记下 `Thumbprint` 值**(40 字符,无空格)。

### 4.3 出问题怎么办

| 报错信息 | 原因 | 解法 |
|---|---|---|
| "找不到路径 'Cert:\LocalMachine\My'" | 没用管理员 PowerShell | 重开 PowerShell,**右键 → 以管理员身份运行** |
| 输出空(没有任何证书) | 服务器上没装证书 | 用 IIS Manager 申请自签或导入 CA 签的 .pfx |
| 所有证书都已过期 | 证书过期 | 重新申请 / 续期 |

---

## 5. 创建 IIS 站点

> **为什么需要这一步**:`create-iis-site.ps1` 一次性做完 9 件事:
> 1. 校验管理员权限 / WebAdministration 模块 / Hosting Bundle / WebSocket / ANCM
> 2. 创建(或更新)AppPool:`NoManagedCode` + `AlwaysRunning` + `IdleTimeout=0` + `PeriodicRestart=0`
> 3. AppPool 注入环境变量 `ASPNETCORE_ENVIRONMENT=Production`
> 4. 创建(或更新)Site,物理路径 = `InstallDir`
> 5. 创建 HTTP binding(默认 8373)
> 6. 创建 HTTPS binding 并绑定证书(若有 Thumbprint)
> 7. 建 `keys/`(DataProtection)+ `logs/` 目录
> 8. 给 `keys/` `logs/` 加 `IIS_IUSRS:(OI)(CI)M`(应用池身份需要写权限)
> 9. 启动 AppPool + Site + 预热首请求

### 5.1 操作

**管理员 PowerShell**:

```powershell
cd D:\inetpub\wwwroot\DrawingNumber\scripts
.\create-iis-site.ps1 `
    -InstallDir 'D:\inetpub\wwwroot\DrawingNumber' `
    -CertThumbprint 'ABCDEF1234567890ABCDEF1234567890ABCDEF12'
```

> 把 `CertThumbprint` 换成第 4 步拿到的 40 字符 Thumbprint。

### 5.2 命令是干什么的

(参见上方"为什么需要这一步"9 件事清单)

### 5.3 怎么看成没成功

应该看到末尾类似:

```
>>> Pre-flight checks
  + Running as Administrator
  + WebAdministration module loaded
  + .NET 8 Hosting Bundle detected (Release=533320)
  + IIS WebSocket Protocol installed
  + ASP.NET Core Module V2 detected
  + InstallDir exists: D:\inetpub\wwwroot\DrawingNumber
  + Cert found: CN=drawing.example.com (thumb=ABCDEF...)

>>> AppPool: DrawingNumberAppPool
  + AppPool created
    .NET CLR     = No Managed Code
    Start Mode   = AlwaysRunning
    Idle Timeout = 00:00:00 (disabled)
    PeriodicRestart.Time = 00:00:00 (disabled — avoids 29h SignalR drop)
    Identity     = ApplicationPoolIdentity
    EnvVar       = ASPNETCORE_ENVIRONMENT=Production (forced)

>>> Site: DrawingNumber
  + Site created (physicalPath=..., AppPool=DrawingNumberAppPool)

>>> Bindings
  + HTTP binding *:8373: created
  + HTTPS binding *:8373: created (cert=ABCDEF...)

>>> DataProtection keys/
  + Created D:\inetpub\wwwroot\DrawingNumber\keys
  + ACL: IIS_IUSRS:(OI)(CI)M on ...\keys

>>> logs/
  + Created D:\inetpub\wwwroot\DrawingNumber\logs
  + ACL: IIS_IUSRS:(OI)(CI)M on ...\logs

>>> Starting AppPool + Site
  + AppPool state : Started
  + Site state    : Started

>>> Warm-up
  + HTTP  warmup OK (status=200)
  + HTTPS warmup OK (status=200)

DONE. Site is ready.
  AppPool : DrawingNumberAppPool (Started)
  Site    : DrawingNumber    (Started)
  URL     : https://localhost:8373/
```

**关键指标:`AppPool state : Started` + `HTTPS warmup OK`**。

### 5.4 出问题怎么办

| 报错信息 | 原因 | 解法 |
|---|---|---|
| "Must run as Administrator" | 没以管理员身份运行 | 重开 PowerShell 管理员 |
| "WebAdministration module not found" | 没装 IIS 管理工具 | `Install-WindowsFeature Web-Mgmt-Tools` |
| ".NET 8 Hosting Bundle not detected" | 没装 Hosting Bundle | 装第 0 步第 5 条 |
| "IIS WebSocket Protocol role not installed" | 没装 WebSocket | `Install-WindowsFeature Web-WebSocket` |
| "Cert thumbprint not found" | Thumbprint 拼错 / 证书不在 LocalMachine\My | 重跑第 4 步确认 |
| "InstallDir not found" | 没把 zip 解到 `InstallDir` | 回第 3 步重新解压 |
| "web.config missing" | publish 产物不完整 | 联系项目负责人 |
| 预热失败(HTTP warmup failed) | 端口冲突 / 应用池启动失败 | 看 [第 8 章 故障排查](#8-故障排查) |

---

## 6. 验证

全部命令都在**普通 PowerShell**(不需要管理员)里跑。

### 6.1 应用池在运行吗

```powershell
Get-WebAppPoolState -Name 'DrawingNumberAppPool'
```

期望 `Value: Started`。

### 6.2 站点在跑吗

```powershell
Get-Website -Name 'DrawingNumber' | Select-Object State, PhysicalPath, Bindings
```

期望 `State: Started`,物理路径正确,Bindings 含 http:8373: 和 https:8373:。

### 6.3 HTTP 是否被自动重定向到 HTTPS

```powershell
# 不带 -k(不带 SkipCertificateCheck)时,HTTP 应 301 跳 HTTPS
$resp = Invoke-WebRequest 'http://localhost:8373/' -MaximumRedirection 0 -ErrorAction SilentlyContinue
$resp.StatusCode    # 期望 301
$resp.Headers.Location   # 期望 https://...
```

### 6.4 浏览器能打开吗

在服务器本机打开 Edge/Chrome,地址栏输入:

```
https://localhost:8373
```

**首次访问**:若证书是自签或 IT CA,会弹"您的连接不是私密连接" → 点 "高级" → "继续前往 localhost(不安全)"。**这是正常的**,不是配置错。

### 6.5 登录试试

| 字段 | 值 |
|---|---|
| 员工号 | `1` |
| 密码 | `Admin@123` |

登录成功 → 看到系统主页 → **立刻去改密码**(右上角用户菜单 → 修改密码)。

**改密码是必须的**,默认密码所有人都知道。

### 6.6 完整验证清单(打 ✅ 才算部署完成)

| # | 检查项 | 期望 | 怎么验 |
|---|---|---|---|
| 1 | AppPool 在运行 | Started | `Get-WebAppPoolState -Name DrawingNumberAppPool` |
| 2 | Site 在运行 | Started | `Get-Website -Name DrawingNumber` |
| 3 | HTTP→HTTPS 跳转 | 301 | `curl http://localhost:8373/ -I` |
| 4 | 本机能访问 | 登录页 | `https://localhost:8373` |
| 5 | 内网用户机能访问 | 登录页 | 用户机浏览器输 `https://<服务器 IP>:8373` |
| 6 | 登录能用 | 进入主页 | 员工号 `1` / `Admin@123` |
| 7 | 默认密码改了 | 新密码能登 | 退出 → 用新密码重登 |
| 8 | 数据库能看到数据 | 列表有内容 | 主页或某个业务页面 |
| 9 | 安全响应头生效 | CSP/X-Frame-Options 等都在 | DevTools → Network → Headers → Response |
| 10 | 日志有写入 | fallback 文件在 | `Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\logs\fallback-*.log` |
| 11 | 自动恢复生效 | Recycle 后仍能访问 | `Restart-WebAppPool -Name DrawingNumberAppPool` 后等 10s 仍 200 |
| 12 | DataProtection key 生成 | keys/ 有文件 | `Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\keys\key-*.xml` |

---

## 7. 告诉最终用户的信息

部署完成后,**把以下内容发给使用系统的同事**:

```
DrawingNumber 系统访问地址:https://<服务器 IP>:8373

首次访问浏览器会弹"不安全"警告(若用了自签 / IT CA):
→ 点 "高级" → "继续前往 <服务器 IP>(不安全)" 即可

默认账号(登录后请立即修改密码):
→ 员工号:1
→ 初始密码:Admin@123
```

---

## 8. 故障排查

### 8.1 AppPool 起不来(`Get-WebAppPoolState` 显示 Stopped)

**怎么看错误**:

1. `Win + R` → 输入 `eventvwr.msc` → 回车
2. 左边 "Windows 日志" → "应用程序"
3. 找**红色错误条目**,来源是 `IIS AspNetCore Module V2` / `.NET Runtime` / `Application Error` 的优先看
4. 看 "常规" 选项卡里的描述文字

**最常见原因**:

| 原因 | 怎么确认 | 解法 |
|---|---|---|
| 数据库连不上 | 错误信息含 `connection` / `SqlSugar` / `Login failed` | `Test-NetConnection JINWAD5A.AD101.SIEMENS-ENERGY.NET -Port 1433`,不通找网络/数据库管理员 |
| Hosting Bundle 没装 / 版本低 | 错误信息含 `Microsoft.AspNetCore.App` / `0x80070002` | 装最新版 .NET 8 Hosting Bundle |
| `keys/` 写不进去 | 错误信息含 `UnauthorizedAccess` + `keys\` | 重跑 `create-iis-site.ps1`,脚本会自动修权限 |
| web.config 不合法 | 错误信息含 `HTTP Error 500.19` | 检查 web.config 语法(XML 是否闭合) |
| ANCM 不匹配 .NET 版本 | 错误信息含 `ANCM` / `502.5` | 装匹配 .NET 8 的 ANCM(随 Hosting Bundle 一起) |

### 8.2 端口没在监听

Kestrel(在 w3wp.exe 里)启动失败但 AppPool 还在。查日志:

```powershell
# ANCM stdout 日志(应用启动时崩溃回溯)
Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\logs\stdout_*.log | Sort LastWriteTime -Descending | Select -First 1 | Get-Content -Tail 50

# NLog 兜底文件日志
Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\logs\fallback-*.log | Sort LastWriteTime -Descending | Select -First 1 | Get-Content -Tail 50
```

把最后 50 行截图发给项目负责人。

### 8.3 用户机访问"无法连接"

按顺序排查:

```powershell
# 1. 站点在跑吗
Get-Website -Name DrawingNumber

# 2. 端口在听吗(在用户机上跑)
Test-NetConnection <服务器 IP> -Port 8373
# 期望 TcpTestSucceeded: True
```

**IIS 走 `http.sys` 内核驱动,Windows 默认放行 HTTP/HTTPS,无需额外防火墙规则**。若用户机不通,九成是网络问题(中间路由器 / VPN / 公司防火墙),不是 IIS 配置问题。

### 8.4 浏览器证书错误

| 错误信息 | 原因 | 解法 |
|---|---|---|
| `NET::ERR_CERT_AUTHORITY_INVALID` | 用户机没信任证书 | 把证书导入到用户机的"受信任的根证书颁发机构" |
| `NET::ERR_CERT_SAN_MISMATCH` | 证书里没含你访问的 IP/域名 | 重新申请证书,确认服务器 IP 在 SAN 里 |
| `NET::ERR_CERT_DATE_INVALID` | 证书过期了 | 重新申请 / 续期证书,更新 Thumbprint |

### 8.5 登录页能开,登录提交后 500

查日志(同 8.2 那条命令),最常见:

- DB 账号被锁 / 密码过期 → 联系数据库管理员
- `keys/` 目录写权限丢了 → 重跑 `create-iis-site.ps1`(幂等)
- 应用首次启动慢(连 DB 建表 / 迁移数据),等 10-30 秒再试

### 8.6 升级后所有人被踢重登

**原因**:升级时 `keys/` 目录被覆盖了,旧的 DataProtection key 丢了。

**预防**:升级脚本永远用 `robocopy ... /XD keys logs`(见第 9 章)。

**补救**:全员重新登录一次即可,后续登录态就稳了。

---

## 9. 升级(有新版本时)

> 项目负责人会给你一个新的 zip 文件。整个流程预计 5-10 分钟,期间用户**会短暂掉线**(最长 1 分钟,取决于 AppPool 启动速度)。

### 9.1 操作

**管理员 PowerShell**:

```powershell
# 1. 备份当前版本(回滚用,保留 keys/logs 也一起)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "D:\inetpub\wwwroot\DrawingNumber" -Destination "D:\inetpub\wwwroot\DrawingNumber_BAK_$stamp" -Recurse -Force

# 2. 解新版本到临时目录
Expand-Archive -Path "C:\Users\<你>\Downloads\DrawingNumber_v2.zip" -DestinationPath "D:\inetpub\wwwroot\DrawingNumber_temp_$stamp\" -Force

# 3. (可选)Recycle AppPool 让 SignalR 长连接断掉,避免文件锁
Restart-WebAppPool -Name DrawingNumberAppPool

# 4. 用 robocopy 把新版本覆盖到目标目录
#    显式排除 keys/ logs/(持久化数据不能丢)
robocopy "D:\inetpub\wwwroot\DrawingNumber_temp_$stamp\DrawingNumber" "D:\inetpub\wwwroot\DrawingNumber" /MIR /XD keys logs /NP
Remove-Item "D:\inetpub\wwwroot\DrawingNumber_temp_$stamp" -Recurse -Force

# 5. Recycle AppPool 加载新版本
Restart-WebAppPool -Name DrawingNumberAppPool

# 6. 验证
Get-WebAppPoolState -Name DrawingNumberAppPool
Invoke-WebRequest https://localhost:8373 -UseBasicParsing -SkipCertificateCheck | Select StatusCode
```

### 9.2 命令是干什么的

| 行 | 作用 |
|---|---|
| `Copy-Item ... BAK_$stamp` | 把旧版本整个备份到 `DrawingNumber_BAK_<时间戳>\`,出问题可以还原 |
| `Restart-WebAppPool` | 关闭当前 w3wp.exe,释放文件锁,期间用户访问会断 |
| `Expand-Archive ... temp_$stamp` | 先解到**临时**目录,避免直接覆盖现有文件锁 |
| `robocopy ... /XD keys logs` | 把新版本的 .dll / wwwroot 覆盖过来,**跳过** 2 个持久化目录 |
| `Restart-WebAppPool` | 启动新 w3wp.exe,加载新 .dll |
| `Remove-Item ... temp_$stamp` | 清掉临时解压目录 |

### 9.3 怎么看成没成功

`Invoke-WebRequest` 返回 200,用户访问正常,登录态保留(没全员被踢)。

### 9.4 升级出问题要回滚

**管理员 PowerShell**:

```powershell
# 1. 停 AppPool
Stop-WebAppPool -Name DrawingNumberAppPool -ErrorAction SilentlyContinue

# 2. 删除当前目录(注意:keys/ logs/ 会被一起删)
Remove-Item -Path "D:\inetpub\wwwroot\DrawingNumber" -Recurse -Force

# 3. 把备份改回原名
Rename-Item -Path "D:\inetpub\wwwroot\DrawingNumber_BAK_<时间戳>" -NewName "D:\inetpub\wwwroot\DrawingNumber"

# 4. 启动 AppPool
Start-WebAppPool -Name DrawingNumberAppPool
```

### 9.5 备份目录太多要清理

升级成功稳定运行一周后,旧备份可以删:

```powershell
Remove-Item -Path "D:\inetpub\wwwroot\DrawingNumber_BAK_<时间戳>" -Recurse -Force
```

---

## 10. 卸载

系统不用了要彻底拆掉:

**管理员 PowerShell**:

```powershell
cd D:\inetpub\wwwroot\DrawingNumber\scripts
.\remove-iis-site.ps1 -Confirm:$false   # 删 Site + AppPool

# 下面这行是删整个文件夹(包括 logs 和 keys),确认不需要历史数据再执行
Remove-Item -Path "D:\inetpub\wwwroot\DrawingNumber" -Recurse -Force
```

会做的:
- Stop + Remove `DrawingNumber` 这个 Site
- Remove `DrawingNumberAppPool` 这个 AppPool
- **不动** `keys/` `logs/` 目录(运维手动决定保留或删除)

**不会**自动删文件——上面 `Remove-Item` 是手动加的。

---

## 11. 日常运维速查

| 想做的事 | 命令 |
|---|---|
| 看 AppPool 状态 | `Get-WebAppPoolState -Name DrawingNumberAppPool` |
| 看 Site 状态 | `Get-Website -Name DrawingNumber` |
| 重启 AppPool(等同 Windows Service 的"重启服务") | `Restart-WebAppPool -Name DrawingNumberAppPool` |
| 启动 AppPool | `Start-WebAppPool -Name DrawingNumberAppPool` |
| 停止 AppPool | `Stop-WebAppPool -Name DrawingNumberAppPool` |
| 看最近 ANCM stdout 日志 | `Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\logs\stdout_*.log \| Sort LastWriteTime -Descending \| Select -First 1 \| Get-Content -Tail 50` |
| 看最近 NLog 兜底日志 | `Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\logs\fallback-*.log \| Sort LastWriteTime -Descending \| Select -First 1 \| Get-Content -Tail 50` |
| 看 Event Viewer 错误 | `Get-EventLog -LogName Application -Newest 30 -EntryType Error` |
| 看端口监听 | `netstat -ano \| findstr :8373` |
| 数据库连通性测试 | `Test-NetConnection JINWAD5A.AD101.SIEMENS-ENERGY.NET -Port 1433` |
| 重新跑创建脚本(幂等) | `.\create-iis-site.ps1 -InstallDir '...' -CertThumbprint '...'` |

---

## 12. 联系项目负责人时,务必附上这些信息

**80% 的故障,光看以下信息就能定位**:

1. **完整报错文字** / 截图(错误框 / PowerShell 输出)
2. **是第几步出问题**(第 0/3/5/6/8 章的第几小节)
3. 下面这条命令的输出:

   ```powershell
   Get-WebAppPoolState -Name DrawingNumberAppPool | Format-List; `
   Get-Website -Name DrawingNumber | Format-List; `
   Get-ChildItem D:\inetpub\wwwroot\DrawingNumber\logs\fallback-*.log -ErrorAction SilentlyContinue | Sort LastWriteTime -Descending | Select -First 1 | Get-Content -Tail 50
   ```
4. Event Viewer 里最近的红色错误条目截图

**缺这些信息,负责人没法远程定位,只能让你升级或回滚。**

---

## 附:Windows 基础操作

### 怎么"以管理员身份"打开 PowerShell

- **方法 A**:开始菜单 → 输入 `powershell` → 在 "Windows PowerShell" 上**右键** → "以管理员身份运行"
- **方法 B**:`Win + X` → "Windows PowerShell(管理员)"

**判断对了没**:窗口标题栏最左边写 "**管理员:** Windows PowerShell"。

### 怎么打开 Event Viewer

`Win + R` → 输入 `eventvwr.msc` → 回车。

### 怎么复制 PowerShell 输出

1. 在 PowerShell 窗口里**右键标题栏 → 属性 → 选项** 里勾 "快速编辑模式"(只需要勾一次)
2. 之后:**鼠标左键选中要复制的内容** → 按 `Enter` 或**右键** → 内容就到剪贴板了
3. 粘贴到邮件/聊天窗口: `Ctrl + V`

---

**任何步骤卡住超过 10 分钟,直接截图 + 上面"联系负责人"的 4 项信息发回去,不要硬试。**
