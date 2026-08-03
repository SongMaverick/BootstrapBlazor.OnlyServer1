# DrawingNumber 系统部署手册(给 IT)

> **给谁看**:负责把这套系统装到 Windows 服务器上的工程师。
>
> **目标是**:让一个会装普通软件、不一定懂 .NET / HTTPS / Windows 服务的 IT 同事,按本文操作一遍能把系统跑起来,出问题时能自己排查 80% 的常见故障,排查不了的知道该把哪些信息发回来。
>
> **不需要懂的东西**:IIS、ASP.NET、Kestrel、HTTPS 协议细节——本文只教"做什么、命令是什么、为什么"。

---

## 0. 在开始之前,先确认这 5 件事

| # | 检查项 | 怎么查 | 不通过怎么办 |
|---|---|---|---|
| 1 | 服务器是 **Windows Server 2016 或更新** | `Win + R` → 输入 `winver` → 看版本 | 不支持,换机器 |
| 2 | 你有**本机管理员**权限 | `Win + R` → `lusrmgr.msc` → 看自己是否在 Administrators 组 | 联系系统管理员加权限 |
| 3 | 服务器**能 ping 通** SQL Server `JINWAD5A.AD101.SIEMENS-ENERGY.NET` | PowerShell 跑 `Test-NetConnection JINWAD5A.AD101.SIEMENS-ENERGY.NET -Port 1433`,返回 `TcpTestSucceeded: True` 即可 | 联系网络/数据库管理员放行 |
| 4 | **8373 端口没被占用** | PowerShell 跑 `netstat -ano \| findstr :8373`,没输出就是空 | 找占用进程(看 PID 那列,任务管理器 → 详细信息 找对应进程)或换端口 |
| 5 | 服务器能上**外网**(或能下载 .NET 8 SDK,可选) | 仅用于本地重新生成证书,一般不需要 | 不影响,跳过自签证书的可选项 |

**全部通过,继续**。任意一条不过,先解决,再往下走。

---

## 1. 你会拿到什么

项目负责人会通过内网共享 / U 盘 / 邮件给你一个**压缩包**(约 80 MB),名字类似 `DrawingNumber.zip`。

里面长这样(只是让你认个脸,不用管):

```
DrawingNumber/
├── BootstrapBlazor.OnlyServer1.exe       ← 主程序
├── appsettings.json                      ← 配置(连数据库的)
├── appsettings.Production.json           ← 生产环境配置(端口 8373)
├── NLog.config                           ← 日志配置
├── web.config                            ← 历史遗留,装好的服务用不到
├── DrawingName.sql                       ← 数据库脚本(参考,不用现在跑)
├── wwwroot/                              ← 网页静态资源
├── runtimes/                             ← 自带的 .NET 8 运行时(无需装)
├── scripts/
│   ├── generate-cert.ps1                 ← 生成 HTTPS 证书
│   ├── install-windows-service.ps1       ← 装成 Windows 服务
│   └── uninstall-windows-service.ps1     ← 卸载时用
└── docs/
    └── deploy-it-runbook.md              ← 本文件
```

---

## 2. 安装路径约定(强烈建议)

> **重要修订(2026-07-XX)**:本项目的标准部署目录是项目负责人和 IT 共同约定的 `D:\Applications\MXB\DrawingNumber\web_publish\`(`D:\Applications\<客户代号>\<系统名>\web_publish\` 是公司 IT 部门的多套系统隔离规范)。
>
> 脚本默认参数已经指向这个路径,**不要再改路径**。如果实际部署目录是带日期戳的 `web_publish_<日期>\`,有两种处理方式:
> 1. **推荐**:解压后把目录重命名/移动到 `web_publish\`(后续命令直接默认就行)
> 2. **也行**:用 `.\install-windows-service.ps1 -InstallDir '...实际路径...'` 显式覆盖,但所有运维命令(查日志、回滚等)都要记得换成实际路径

**整套系统装在 `D:\Applications\MXB\DrawingNumber\web_publish\`**。

- 不要再改默认路径(脚本默认就是这个)
- 装在 C 盘 **`C:\`** 上不会出错,但与公司多套系统隔离规范不符,**不推荐**
- 路径里**不要有空格或中文**(Windows 服务对路径兼容性差)

---

## 3. 解压

### 3.1 操作

把 `DrawingNumber.zip` 拷到服务器任意位置(比如桌面或 `C:\Users\<你>\Downloads\`),然后:

1. `Win + S` → 输入 `powershell` → 点 "Windows PowerShell"
2. 在 PowerShell 里跑:

```powershell
# 解压到标准部署根目录下(注意是 D:\Applications\MXB\DrawingNumber\)
Expand-Archive -Path "C:\Users\<你>\Downloads\DrawingNumber.zip" -DestinationPath "D:\Applications\MXB\DrawingNumber\" -Force

# 如果解出来的是 web_publish_<日期>\ 子目录(发布者打了日期戳),执行下面统一路径:
# Move-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish_<日期>\*" `
#          -Destination "D:\Applications\MXB\DrawingNumber\web_publish\" -Force
# Remove-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish_<日期>" -Recurse -Force
```

### 3.2 命令是干什么的

把 zip 里的内容解到 `D:\Applications\MXB\DrawingNumber\`,得到 `D:\Applications\MXB\DrawingNumber\web_publish\`(如果发布者把根目录起名为 `web_publish` 就直接得到;否则可能得到 `web_publish_<日期>\`,用上面的 Move-Item 步骤统一)。

### 3.3 怎么看成没成功

```powershell
Test-Path "D:\Applications\MXB\DrawingNumber\web_publish\BootstrapBlazor.OnlyServer1.exe"
```

输出 `True` 就对了。

---

## 4. 生成 HTTPS 证书

> **为什么需要这一步**:系统强制 HTTPS(纯 HTTP 直接 500)。HTTPS 需要一张证书,跟浏览器访问 `https://...` 时服务器要出示的"身份证"是同一张。

### 4.1 操作

1. **以管理员身份**打开 PowerShell:
   - 开始菜单 → 输入 `powershell` → 在 "Windows PowerShell" 上**右键** → **「以管理员身份运行」**
   - 标题栏会写 "管理员: Windows PowerShell",才是对的
2. 跑:

```powershell
cd D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\generate-cert.ps1
```

3. 如果第一次跑 `.ps1` 报"禁止运行脚本",再跑一次下面这行(输入 `Y` 回车),然后重新跑 `.\generate-cert.ps1`:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 4.2 命令是干什么的

生成一张自签 HTTPS 证书,导出成 `D:\Applications\MXB\DrawingNumber\web_publish\certs\server.pfx`,密码写在配置里、跟 PFX 文件配套。

### 4.3 怎么看成没成功

应该看到末尾类似:

```
>>> DrawingNumber cert generator
  + Created certs folder: D:\Applications\MXB\DrawingNumber\web_publish\certs
  + Cert created in LocalMachine\My (Thumbprint: ABC123...)
  + PFX exported: D:\Applications\MXB\DrawingNumber\web_publish\certs\server.pfx
  + Removed cert from LocalMachine\My (only PFX remains)
  + PFX ACL set (SYSTEM / Administrators read-only)

DONE. Next steps:
  1) Verify appsettings.Production.json:...:Certificate:Password matches 'Pfx@2026!...'
  2) Run .\install-windows-service.ps1
```

**必须看到 `DONE.` 才算成功**。

### 4.4 出问题怎么办

| 报错信息 | 原因 | 解法 |
|---|---|---|
| "因为此系统上禁止运行脚本" | PowerShell 默认策略禁用了 .ps1 | 见 4.1 步的 `Set-ExecutionPolicy` 修复 |
| "Cert creation failed" | Windows 版本太旧不支持新式自签证书 | 截图发给项目负责人 |
| "拒绝访问" | 没以管理员身份运行 | 重新打开 PowerShell,**右键 → 以管理员身份运行** |
| 脚本卡住无输出 | 等待 30 秒,自签证书在某些慢机器上要 5-10 秒 | 等就行,超过 1 分钟再截图 |

---

## 5. 注册成 Windows 服务

> **为什么需要这一步**:Windows 服务能开机自启、出错自动重启、不需要登录桌面就能跑,工厂服务器最合适的运行方式。

### 5.1 操作

还在刚才那个**管理员 PowerShell** 窗口(没关的话),跑:

```powershell
cd D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\install-windows-service.ps1
```

### 5.2 命令是干什么的

一次性做完 6 件事:

1. 建好 `keys\`(登录态密钥)、`certs\`(放证书的,刚才已经有了)、`logs\`(日志)三个目录
2. 给三个目录分别设权限(避免被误删或被攻击者读)
3. 注册一个名叫 `DrawingNumber` 的 Windows 服务,设为**自动启动**
4. 让它崩溃后**自动重启**(0 秒、30 秒、60 秒 三次机会)
5. 告诉服务运行在 **Production 环境**(这才走 HTTPS,不走开发用的 HTTP)
6. 防火墙自动放行 **TCP 8373**
7. 启动服务

### 5.3 怎么看成没成功

应该看到末尾类似:

```
>>> Pre-flight checks
  + D:\Applications\MXB\DrawingNumber\web_publish exists
  + D:\Applications\MXB\DrawingNumber\web_publish\keys exists
  + D:\Applications\MXB\DrawingNumber\web_publish\certs exists
  + D:\Applications\MXB\DrawingNumber\web_publish\logs exists
  ...
  + Inbound TCP 8373 allowed

>>> Starting service
  Status : Running
  CanStop: True

DONE. Service 'DrawingNumber' is running.
```

**关键指标:`Status : Running`**。

### 5.4 出问题怎么办

| 报错信息 | 原因 | 解法 |
|---|---|---|
| "EXE not found" | EXE 不在 `D:\Applications\MXB\DrawingNumber\web_publish\` | 回第 3 步重新解压 |
| "PFX not found" 警告 | 还没生成证书 | 回第 4 步先生成 |
| "拒绝访问" | 没以管理员身份运行 | 重开 PowerShell 管理员 |
| `Status : Stopped` | 服务启动失败 | 看 [第 8 章 故障排查](#8-故障排查) |

---

## 6. 验证

全部命令都在**普通 PowerShell**(不需要管理员)里跑。

### 6.1 服务在运行吗

```powershell
Get-Service DrawingNumber
```

期望 `Status: Running`。

### 6.2 端口在听吗

```powershell
netstat -ano | findstr :8373
```

期望看到 `LISTENING` 状态(类似 `0.0.0.0:8373 ... LISTENING`)。

### 6.3 浏览器能打开吗

在服务器本机打开 Edge/Chrome,地址栏输入:

```
https://localhost:8373
```

**首次访问会弹**"您的连接不是私密连接"——这是因为用了自签证书。点 "**高级**" → "**继续前往 localhost(不安全)**" 就能看到登录页。**这是正常的**,不是配置错。

> 如果连登录页都打不开(空白/超时/拒绝连接),看 [第 8 章](#8-故障排查)。

### 6.4 登录试试

| 字段 | 值 |
|---|---|
| 员工号 | `1` |
| 密码 | `Admin@123` |

登录成功 → 看到系统主页 → **立刻去改密码**(右上角用户菜单 → 修改密码)。

**改密码是必须的**,默认密码所有人都知道。

### 6.5 完整验证清单(打 ✅ 才算部署完成)

| # | 检查项 | 期望 | 怎么验 |
|---|---|---|---|
| 1 | 服务在运行 | Running | `Get-Service DrawingNumber` |
| 2 | 端口监听 | LISTENING | `netstat -ano \| findstr :8373` |
| 3 | 本机能访问 | 登录页 | `https://localhost:8373` |
| 4 | 内网用户机能访问 | 登录页 | 用户机浏览器输 `https://<服务器 IP>:8373` |
| 5 | 登录能用 | 进入主页 | 员工号 `1` / `Admin@123` |
| 6 | 默认密码改了 | 新密码能登 | 退出 → 用新密码重登 |
| 7 | 数据库能看到数据 | 列表有内容 | 主页或某个业务页面 |
| 8 | 防火墙规则生效 | 端口开放 | `Get-NetFirewallRule -DisplayName "DrawingNumber-HTTPS-8373"` |
| 9 | 日志有写入 | fallback 文件在 | `Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\logs\fallback-*.log` |
| 10 | 自动恢复生效 | 手动停后自动起 | `Stop-Service DrawingNumber; Start-Sleep 5; Get-Service DrawingNumber` |

---

## 7. 告诉最终用户的信息

部署完成后,**把以下内容发给使用系统的同事**:

```
DrawingNumber 系统访问地址:https://<服务器 IP>:8373

首次访问浏览器会弹"不安全"警告:
→ 点 "高级" → "继续前往 <服务器 IP>(不安全)" 即可

默认账号(登录后请立即修改密码):
→ 员工号:1
→ 初始密码:Admin@123

证书不安全警告的消除方法(可选,只在自己电脑上做一次):
→ 把 D:\Applications\MXB\DrawingNumber\web_publish\certs\server.pfx 拷到本机
→ 双击 → 下一步 → 下一步 → 选「受信任的根证书颁发机构」→ 完成
→ 以后访问这个地址就不再弹警告了
```

---

## 8. 故障排查

### 8.1 服务起不来(`Get-Service` 显示 Stopped)

**怎么看错误**:

1. `Win + R` → 输入 `eventvwr.msc` → 回车
2. 左边 "Windows 日志" → "应用程序"
3. 找**红色错误条目**,来源是 `.NET Runtime` / `Application Error` / `DrawingNumber` 的优先看
4. 看 "常规" 选项卡里的描述文字

**最常见原因**:

| 原因 | 怎么确认 | 解法 |
|---|---|---|
| 数据库连不上 | 错误信息含 `connection` / `SqlSugar` / `Login failed` | `Test-NetConnection JINWAD5A.AD101.SIEMENS-ENERGY.NET -Port 1433`,不通找网络/数据库管理员 |
| 证书文件读不了 | 错误信息含 `PFX` / `certificate` / `password` | 重新跑第 4 步;确认 `appsettings.Production.json` 里的密码跟脚本默认一致 |
| 端口被占 | 错误信息含 `address already in use` | `netstat -ano \| findstr :8373` 看谁占了,改用别的端口或停掉占用方 |
| `keys/` 写不进去 | 错误信息含 `UnauthorizedAccess` + `keys\` | 重跑第 5 步的 `install-windows-service.ps1`,脚本会自动修权限 |

### 8.2 端口没在听(服务 Running,但 `netstat` 没结果)

Kestrel 启动失败但服务还在,可能是 HTTPS 终结点配置错。查日志:

```powershell
Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\logs\fallback-*.log | Sort LastWriteTime -Descending | Select -First 1 | Get-Content -Tail 50
```

把最后 50 行截图发给项目负责人。

### 8.3 用户机访问"无法连接"

按顺序排查:

```powershell
# 1. 防火墙规则在不在
Get-NetFirewallRule -DisplayName "DrawingNumber-HTTPS-8373"
# 期望 DisplayName 一栏能看到这条,Enabled: True

# 2. 端口从外面能连不(在用户机上跑,不是在服务器上)
Test-NetConnection <服务器 IP> -Port 8373
# 期望 TcpTestSucceeded: True

# 3. 服务器上服务在跑
Get-Service DrawingNumber
```

### 8.4 浏览器证书错误

| 错误信息 | 原因 | 解法 |
|---|---|---|
| `NET::ERR_CERT_AUTHORITY_INVALID` | 用户机没信任证书 | 把 `server.pfx` 导入到用户机的"受信任的根证书颁发机构"(见第 7 章) |
| `NET::ERR_CERT_SAN_MISMATCH` | 证书里没含你访问的 IP/域名 | 重跑第 4 步,确认服务器 IP 在证书 SAN 里 |
| `NET::ERR_CERT_DATE_INVALID` | 证书过期了 | 重跑第 4 步生成新的 |

### 8.5 登录页能开,登录提交后 500

查日志(同 8.2 那条命令),最常见:

- DB 账号被锁 / 密码过期 → 联系数据库管理员
- `keys/` 目录写权限丢了 → 重跑第 5 步
- 应用首次启动慢(连 DB 建表 / 迁移数据),等 10-30 秒再试

---

## 9. 升级(有新版本时)

> 项目负责人会给你一个新的 zip 文件。整个流程预计 5-10 分钟,期间用户**会短暂掉线**(最长 1 分钟)。

### 9.1 操作

**管理员 PowerShell**:

```powershell
# 1. 备份当前版本(回滚用,保留 keys/certs/logs 也一起)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Destination "D:\Applications\MXB\DrawingNumber\web_publish_BAK_$stamp" -Recurse -Force

# 2. 停服务
sc.exe stop DrawingNumber

# 3. 解新版本到临时目录(temp/下)
Expand-Archive -Path "C:\Users\<你>\Downloads\DrawingNumber_v2.zip" -DestinationPath "D:\Applications\MXB\DrawingNumber\web_publish_temp_$stamp\" -Force
#    (如果发布者把 zip 根目录命名为 web_publish,此命令会得到
#     web_publish_temp_<时间戳>\web_publish\,下一条命令据此调整)

# 4. 用 robocopy 把新版本的 EXE/dll/wwwroot 覆盖到 web_publish\
#    显式排除 keys/ certs/ logs/(持久化数据不能丢:登录态 / 证书 / 历史日志)
robocopy "D:\Applications\MXB\DrawingNumber\web_publish_temp_$stamp\web_publish" "D:\Applications\MXB\DrawingNumber\web_publish" /MIR /XD keys certs logs /XF NLog-internal.log *.pfx /NP
Remove-Item "D:\Applications\MXB\DrawingNumber\web_publish_temp_$stamp" -Recurse -Force

# 5. 启服务
sc.exe start DrawingNumber
Get-Service DrawingNumber
```

### 9.2 命令是干什么的

| 行 | 作用 |
|---|---|
| `Copy-Item ... BAK_$stamp` | 把旧版本整个备份到 `D:\Applications\MXB\DrawingNumber\web_publish_BAK_20260727_153000\`(自动带时间戳),出问题可以还原 |
| `sc.exe stop` | 停服务,期间用户访问会 503,**大约 5-15 秒** |
| `Expand-Archive ... temp_$stamp` | 先解到**临时**目录,避免直接解到 web_publish\ 时命中现有文件锁 |
| `robocopy ... /XD keys certs logs` | 把新版本的 EXE/dll/wwwroot 覆盖过来,**跳过** 3 个持久化目录(登录态/HTTPS 证书/历史日志都不会丢) |
| `Remove-Item ... temp_$stamp` | 清掉临时解压目录 |
| `sc.exe start` | 重启服务,新 EXE 生效 |

### 9.3 怎么看成没成功

`Get-Service DrawingNumber` 显示 `Running`,用户访问正常。

### 9.4 升级出问题要回滚

**管理员 PowerShell**:

```powershell
sc.exe stop DrawingNumber
Remove-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Recurse -Force
Rename-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish_BAK_<时间戳>" -NewName "D:\Applications\MXB\DrawingNumber\web_publish"
sc.exe start DrawingNumber
```

### 9.5 备份目录太多要清理

升级成功一周后,旧备份可以删:

```powershell
Remove-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish_BAK_<时间戳>" -Recurse -Force
```

---

## 10. 卸载

系统不用了要彻底拆掉:

**管理员 PowerShell**:

```powershell
cd D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\uninstall-windows-service.ps1     # 删服务 + 防火墙规则 + 注册表环境变量

# 下面这行是删整个文件夹(包括 logs 和 keys),确认不需要历史数据再执行
Remove-Item -Path "D:\Applications\MXB\DrawingNumber\web_publish" -Recurse -Force
```

会做的:

- 停服务并删除 `DrawingNumber` 这个 Windows 服务
- 删除防火墙规则 `DrawingNumber-HTTPS-8373`
- 清理注册表里的环境变量 `ASPNETCORE_ENVIRONMENT=Production`

**不会**自动删文件——上面 `Remove-Item` 是手动加的。

---

## 11. 日常运维速查

| 想做的事 | 命令 |
|---|---|
| 看服务状态 | `Get-Service DrawingNumber` |
| 启动服务 | `Start-Service DrawingNumber` |
| 停止服务 | `Stop-Service DrawingNumber` |
| 重启服务 | `Restart-Service DrawingNumber` |
| 看最近日志 | `Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\logs\fallback-*.log \| Sort LastWriteTime -Descending \| Select -First 1 \| Get-Content -Tail 50` |
| 看 Event Viewer 错误 | `Get-EventLog -LogName Application -Newest 30 -EntryType Error` |
| 看端口监听 | `netstat -ano \| findstr :8373` |
| 看防火墙规则 | `Get-NetFirewallRule -DisplayName "DrawingNumber-HTTPS-8373"` |
| 数据库连通性测试 | `Test-NetConnection JINWAD5A.AD101.SIEMENS-ENERGY.NET -Port 1433` |

---

## 12. 联系项目负责人时,务必附上这些信息

**80% 的故障,光看以下信息就能定位**:

1. **完整报错文字** / 截图(错误框 / PowerShell 输出)
2. **是第几步出问题**(第 3 / 4 / 5 / 6 / 8 章的第几小节)
3. 下面这条命令的输出:

   ```powershell
   Get-Service DrawingNumber | Format-List; `
   netstat -ano | findstr :8373; `
   Get-ChildItem D:\Applications\MXB\DrawingNumber\web_publish\logs\fallback-*.log -ErrorAction SilentlyContinue | Sort LastWriteTime -Descending | Select -First 1 | Get-Content -Tail 50
   ```
4. Event Viewer 里最近的红色错误条目截图

**缺这些信息,负责人没法远程定位,只能让你升级或回滚。**

---

## 附:Windows 基础操作

### 怎么"以管理员身份"打开 PowerShell

- **方法 A**:开始菜单 → 输入 `powershell` → 在 "Windows PowerShell" 上**右键** → "以管理员身份运行"
- **方法 B**:`Win + X` → "Windows PowerShell(管理员)"

**判断对了没**:窗口标题栏最左边写 "**管理员:** Windows PowerShell"。

### 第一次跑 .ps1 脚本被禁

报错 "...因为此系统上禁止运行脚本"。

**管理员 PowerShell** 跑一次:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

输入 `Y` 回车。以后再跑 .ps1 就不报这个错了。

### 怎么打开 Event Viewer

`Win + R` → 输入 `eventvwr.msc` → 回车。

### 怎么复制 PowerShell 输出

1. 在 PowerShell 窗口里**右键标题栏 → 属性 → 选项** 里勾 "快速编辑模式"(只需要勾一次)
2. 之后:**鼠标左键选中要复制的内容** → 按 `Enter` 或**右键** → 内容就到剪贴板了
3. 粘贴到邮件/聊天窗口: `Ctrl + V`

---

**任何步骤卡住超过 10 分钟,直接截图 + 上面"联系负责人"的 4 项信息发回去,不要硬试。**