# DrawingNumber 部署手册 — Windows Service + Kestrel HTTPS

> **目标读者**:项目负责人(发布)+ IT 部署工程师(安装)
> **目标环境**:Windows Server 2016 或更新,内网用户访问
> **部署形态**:Kestrel 自承载(不依赖 IIS)+ Windows Service(开机自启)
> **访问入口**:`https://<服务器 IP>:8373`

> **如果你看不懂某一步**:
> - 命令报错 → 把**完整报错文字**截图发回项目负责人
> - 不知道"管理员身份运行 PowerShell"怎么做 → 看 [附录 A](#附录-awindows-基础操作)
> - 服务器访问不通 → 看 [附录 B 故障排查](#附录-b故障排查)

---

## 0. 前置条件(给 IT 一次看完)

| 项 | 要求 | 备注 |
|---|---|---|
| 服务器 OS | Windows Server 2016 或更新 | 支持 `New-SelfSignedCertificate` 的 IP-form SAN |
| 管理员账户 | 本机 Administrators 组成员 | 装服务 / 写注册表 / 放防火墙都要 |
| 网络 | 服务器能访问 SQL Server | 内网 OK,跨网段需要 IT 提前放行 |
| 防火墙 | 可新建入站规则 | TCP 8373 |
| .NET 运行时 | **不需要** | 发布包 self-contained(自带运行时,~80MB) |
| IIS | **不需要** | 走 Kestrel,不依赖 IIS |

---

## 1. 项目负责人:用 VS2022 发布应用

### 1.1 操作步骤

1. 在 VS2022 中打开 `BootstrapBlazor.OnlyServer1.sln`
2. 解决方案资源管理器里**右键** `BootstrapBlazor.OnlyServer1` 项目 → **发布(B)...**
3. **目标**:选 **文件夹**
4. **位置**:点 **下一步** 选 `D:\Publish\DrawingNumber`(或别的固定目录)
5. **配置 / 目标**:确保是 **Release** | **net8.0** | **win-x64** | **自包含模式** 打勾
   - 如果"自包含"没打勾,服务器就要装 .NET 8 Hosting Bundle(增加前置条件,建议勾上)
6. 点 **保存** → **发布**
7. 等 1-3 分钟,看到 "发布成功"

### 1.2 给 IT 的内容

把整个 `D:\Publish\DrawingNumber\` 文件夹**整个给 IT**(压缩包 / 内网共享 / U 盘 / 邮件都行,80MB 左右)。

这个文件夹里**已经包含**:
```
BootstrapBlazor.OnlyServer1.exe       ← 主程序
appsettings.json                      ← 公共配置(连接串等)
appsettings.Production.json           ← 生产环境配置(HTTPS/Kestrel)
NLog.config                           ← 日志配置
web.config                            ← IIS 兼容配置(Kestrel 路径下不生效)
DrawingName.sql                       ← 数据库脚本(运维参考)
wwwroot/                              ← 网页静态资源
runtimes/                             ← self-contained 携带的原生库
scripts/                              ← 部署脚本(生成证书、装服务)
docs/                                 ← 部署手册(本文件 + 其他文档)
... 100+ 个 dll 文件
```

**同时告诉 IT**:
- 服务器目标 IP(例:`140.231.237.180`)
- 服务器上要装的路径(建议就用 `D:\Applications\MXB\DrawingNumber\web_publish\`,不用 IT 改)
- 默认管理员账号:`WorkerNumber=1`,初始密码 `Admin@123`(登录后立刻改)

---

## 2. IT:部署到服务器

> 下面所有命令都在 **PowerShell** 里输入。涉及写注册表 / 装服务 / 改防火墙的步骤都要用**管理员身份**。

### 2.1 解压安装包

**这一步做什么**:把项目负责人给的整个文件夹解压到服务器固定目录。

假设 zip 在 `C:\Users\IT\Downloads\DrawingNumber.zip`:

```powershell
# 普通 PowerShell
Expand-Archive -Path "C:\Users\IT\Downloads\DrawingNumber.zip" -DestinationPath "C:\" -Force
```

**预期看到**:
- `D:\Applications\MXB\DrawingNumber\web_publish\BootstrapBlavor.OnlyServer1.exe`
- `D:\Applications\MXB\DrawingNumber\web_publish\scripts\generate-cert.ps1` 等脚本
- `D:\Applications\MXB\DrawingNumber\web_publish\docs\deploy-windows-service.md`(本文件)

---

### 2.2 确认目录结构

**这一步做什么**:看一眼解压结果,确认 EXE、脚本、配置都在位。

```powershell
# 普通 PowerShell —— 切到安装目录
Set-Location D:\Applications\MXB\DrawingNumber\web_publish
Get-ChildItem
```

应该看到 `scripts\` `docs\` `wwwroot\` `runtimes\` 四个目录 + 一堆 dll。

---

### 2.3 生成 HTTPS 证书

**这一步做什么**:自签一张证书,导出 PFX 到 `certs\server.pfx`(Kestrel 启动时加载用)。脚本同时:
- 在 `LocalMachine\My` 创建临时证书
- 导出 PFX 到 `certs\`
- 从 `LocalMachine\My` 删除(只留 PFX)
- 给 PFX 设 ACL(服务可读,管理员可改)

```powershell
# 必须 PowerShell 管理员
Set-Location D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\generate-cert.ps1
```

脚本内部参数:
- `-InstallDir 'D:\Applications\MXB\DrawingNumber\web_publish'`(默认就是这个,IT 若部署在带日期戳的 `web_publish_<日期>\`,请显式传 `-InstallDir`)
- `-Password 'Pfx@2026!TCID#K7m9xQpLwRtY'`(默认就是这个;要和 `appsettings.Production.json` 里的密码匹配)

**预期看到**(关键行):
```
>>> DrawingNumber cert generator
  + Created certs folder: D:\Applications\MXB\DrawingNumber\web_publish\certs
  + Cert created in LocalMachine\My (Thumbprint: ...)
  + PFX exported: D:\Applications\MXB\DrawingNumber\web_publish\certs\server.pfx
  + Removed cert from LocalMachine\My (only PFX remains)
  + PFX ACL set (SYSTEM / Administrators read-only)

DONE. Next steps:
  1) Verify appsettings.Production.json:...:Certificate:Password matches 'Pfx@2026!...'
  2) Run .\install-windows-service.ps1
```

**如果不对**:
- "Cert creation failed" → 可能是 Windows 版本太旧,截图发回负责人
- "管理员权限不够" → 看 [附录 A.2](#a2-管理员身份)

---

### 2.4 安装 Windows 服务

**这一步做什么**:把应用注册成 Windows 服务。脚本会:
- 自动建 `keys\`(DP 密钥)、`certs\`(PFX)、`logs\`(日志)目录(如果之前没有)
- 给三目录分别设 ACL(`keys` 全员只读;`certs` Admins 可改;`logs` 全权)
- 检查 PFX 是否在(如果不在,只警告不退出)
- 删旧服务(如果有)、`sc.exe create` 注册新服务(`LocalSystem`, 开机自启)
- 配失败恢复:0s / 30s / 60s 自动重启
- 注入 `ASPNETCORE_ENVIRONMENT=Production` 到服务注册表
- 打开防火墙入站规则 `DrawingNumber-HTTPS-8373`
- `sc.exe start` 启动服务

```powershell
# 必须 PowerShell 管理员
Set-Location D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\install-windows-service.ps1
```

**预期看到**(关键行):
```
>>> Pre-flight checks
  + D:\Applications\MXB\DrawingNumber\web_publish exists
  + D:\Applications\MXB\DrawingNumber\web_publish\keys exists
  + D:\Applications\MXB\DrawingNumber\web_publish\certs exists
  + D:\Applications\MXB\DrawingNumber\web_publish\logs exists
  + ACLs set on D:\Applications\MXB\DrawingNumber\web_publish\keys (SYSTEM/Admins read-only)
  + ACLs set on D:\Applications\MXB\DrawingNumber\web_publish\certs (SYSTEM read, Admins modify)
  + ACLs set on D:\Applications\MXB\DrawingNumber\web_publish\logs (SYSTEM/Admins full)

>>> Registering Windows Service 'DrawingNumber'
  + Service registered (LocalSystem, start=auto)
  + Failure recovery: restart on crash (0s / 30s / 60s backoff)
  + Setting ASPNETCORE_ENVIRONMENT=Production

>>> Opening firewall port 8373
  + Inbound TCP 8373 allowed

>>> Starting service
  Status : Running
  CanStop: True

DONE. Service 'DrawingNumber' is running.
```

**如果不对**:
- "EXE not found" → 看 [2.2 步](#22-确认目录结构),EXE 应该在 `D:\Applications\MXB\DrawingNumber\web_publish\`
- "PFX not found" 警告 → 回到 [2.3 步](#23-生成-https-证书)先生成 PFX
- "拒绝访问" → 不是管理员,看 [附录 A.2](#a2-管理员身份)
- "Status: Stopped" → 看 [附录 B 故障排查](#附录-b故障排查)

---

## 3. 验证

### 3.1 服务器本机验证

**这一步做什么**:在服务器上确认服务在跑、端口在听、首屏能连。

```powershell
# 普通 PowerShell
Get-Service DrawingNumber       # 应该 Status: Running
netstat -ano | findstr :8373    # 应该看到 LISTENING 0.0.0.0:8373
curl -k https://localhost:8373  # 应该返回 HTML(可能 302 跳登录页)
```

打开 Edge/Chrome 浏览器,地址栏输入 `https://localhost:8373`,**预期看到登录页**(浏览器可能弹"您的连接不是私密连接"警告,因为是自签证书——先点"高级"→"继续前往",登录进得去就行,证书信任在 [第 3.2 步](#32-用户机验证)处理)。

### 3.2 用户机验证

**这一步做什么**:从任意一台用户机(项目负责人那台)访问服务器,确认内网可达。

用户机浏览器输入:`https://<服务器 IP>:8373`(例:`https://140.231.237.180:8373`)

**预期**:
- 浏览器弹"您的连接不是私密连接"窗口(自签证书)
- 点 "**高级**" → "**继续前往 ...(不安全)**"
- 看到登录页

如果想消除证书警告:把 `D:\Applications\MXB\DrawingNumber\web_publish\certs\server.pfx` 拷到用户机,**双击 PFX** → 证书导入向导:
1. 存储位置:**本地计算机**(需管理员)
2. 下一步 → 下一步
3. 证书存储:**将所有证书放入下列存储** → "**受信任的根证书颁发机构**"
4. 下一步 → 完成

### 3.3 登录测试

1. 登录页输入:员工号 `1`,密码 `Admin@123`
2. 登录成功 → 看到系统主页
3. **立刻去"用户管理"改默认管理员密码**(`/user-management`)

### 3.4 完整验证清单(逐项 ✅)

| # | 检查项 | 期望 | 操作 |
|---|---|---|---|
| 1 | 服务在运行 | Running | `Get-Service DrawingNumber` |
| 2 | 端口监听 | LISTENING | `netstat -ano \| findstr :8373` |
| 3 | 首屏 | 200 | 用户机浏览器访问 |
| 4 | 登录 | 成功 | WorkerNumber=1, Admin@123 |
| 5 | 数据库 | OK | 登录后能看到数据 |
| 6 | 改密 | OK | 改成自己的密码 |
| 7 | 退出/登录 | OK | 退出再登录,验证 cookie |
| 8 | Antiforgery | OK | DevTools 搜 `__RequestVerificationToken` |
| 9 | WebSocket | OK | DevTools Network 找 `ws://.../_blazor` |
| 10 | 长连接 | 不掉 | 等 10 分钟不操作刷新仍能用 |
| 11 | 自恢复 | OK | `sc.exe stop DrawingNumber; sleep 5; Get-Service DrawingNumber` 回 Running |
| 12 | 日志 | 有新行 | `Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\logs\fallback-*.log` |

---

## 4. 升级流程

**这一步做什么**:用新版本覆盖 EXE 和 dll,保留数据目录(`keys\` `certs\` `logs\`),服务重启约 10-30 秒。

### 4.1 项目负责人

1. VS2022 重新发布到 `D:\Publish\DrawingNumber\`
2. 把整个文件夹重新给 IT(整个替换,不要保留任何旧文件)

### 4.2 IT

```powershell
# 管理员 PowerShell
# 1. 备份当前版本(出问题可回滚)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Destination "D:\Applications\MXB\DrawingNumber\web_publish_BAK_$stamp" -Recurse -Force

# 2. 停服务
sc.exe stop DrawingNumber

# 3. 用新版本覆盖 EXE 和 dll —— 注意:这条命令会覆盖 EXE 和 dll,
#    但保留 keys/(DP)、certs/(PFX)、logs/(日志)目录,所以用户登录态不丢
Expand-Archive -Path "C:\Users\IT\Downloads\DrawingNumber.zip" -DestinationPath "C:\" -Force

# 4. 启服务
sc.exe start DrawingNumber
Get-Service DrawingNumber
```

**回滚**(出问题要退回旧版本):
```powershell
sc.exe stop DrawingNumber
Remove-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Recurse -Force
Rename-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish_BAK_<时间戳>" -NewName "D:\Applications\MXB\DrawingNumber\web_publish"
sc.exe start DrawingNumber
```

---

## 5. 卸载

**这一步做什么**:完全移除 DrawingNumber 部署(服务、防火墙、注册表项、文件)。

```powershell
# 管理员 PowerShell
Set-Location D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\uninstall-windows-service.ps1   # 删服务 + 防火墙 + 注册表环境变量

# 删文件(可选)
Remove-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Recurse -Force
```

**不会**自动删除:`keys\` `certs\` `logs\` 目录(脚本不动文件),如要连这些一起删,需要单独 `Remove-Item`。

---

## 6. 故障排查

| 现象 | 排查命令 | 修复 |
|---|---|---|
| **服务 Status: Stopped** | `Get-EventLog -LogName Application -Newest 30 -Source "*.NET Runtime","Application Error" \| Format-List` | 看错误信息,**完整截图**发回项目负责人 |
| **端口未监听 8373** | `Get-Service DrawingNumber`<br>`netstat -ano \| findstr :8373` | 服务没起来 → 看上行 |
| **用户机访问"无法连接"** | `Test-NetConnection -ComputerName <服务器 IP> -Port 8373` 应返回 `TcpTestSucceeded: True` | 防火墙: `Get-NetFirewallRule -DisplayName "DrawingNumber-HTTPS-8373"`<br>端口: `netstat -ano \| findstr :8373` |
| **证书错误 NET::ERR_CERT_AUTHORITY_INVALID** | — | 没把 PFX 导入 Trusted Root(见 [3.2 步](#32-用户机验证)) |
| **证书错误 NET::ERR_CERT_SAN_MISMATCH** | — | 用户机地址栏输的 IP 不在证书 SAN 里 → 重跑 `generate-cert.ps1` 确认 IP 在 -DnsName 列表 |
| **登录页能开,登录后 500** | `Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\logs\fallback-*.log \| Sort LastWriteTime Desc \| Select -First 1 \| Get-Content -Tail 30` | 日志最后 30 行截图发回负责人。常见:DB 连不上 / PFX ACL 错 |
| **DataProtection key 反复新生成** | `Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\keys\` 看 `key-<guid>.xml` 是否频繁新生成 | 进程没写权限 → 重装服务: `uninstall-windows-service.ps1` + `install-windows-service.ps1` |
| **服务能起,但用户机访问慢** | DevTools → Network 看 `ws://...` 或 `/_framework/blazor.web.js` | 网络丢包或代理问题,联系 IT |

---

## 附录 A:Windows 基础操作

### A.1 普通 PowerShell

按 `Win + S` → 输入 `powershell` → 点 "Windows PowerShell"。

### A.2 管理员身份

开始菜单 → 输入 `powershell` → 在 "Windows PowerShell" 上**右键** → **以管理员身份运行**。
或:`Win + X` → 选 "Windows PowerShell(管理员)"。
标题栏写 "管理员: Windows PowerShell" 就是。

### A.3 第一次跑 .ps1 脚本被禁

报错 "...因为此系统上禁止运行脚本"。**管理员 PowerShell** 一次就够:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

输入 `Y` 回车。

### A.4 备份整个目录

```powershell
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Destination "D:\Applications\MXB\DrawingNumber\web_publish_BAK_$stamp" -Recurse -Force
```

---

## 联系项目负责人时附带的信息

1. **完整报错文字 / 截图**
2. **是哪一步出问题**(第几步)
3. `Get-Service DrawingNumber` 的输出
4. `netstat -ano | findstr :8373` 的输出
5. `Get-EventLog -LogName Application -Newest 30 -Source "*.NET Runtime","Application Error" | Format-List` 的输出

本机和所有部署相关文件都在 `D:\Applications\MXB\DrawingNumber\web_publish\`(EXE、配置、脚本、本手册)。