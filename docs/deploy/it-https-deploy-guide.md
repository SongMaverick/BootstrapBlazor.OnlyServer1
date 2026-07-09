# IT HTTPS 部署操作手册

> **给运维 IT 的纯 GUI 操作清单**
> 不需要懂代码,跟着点鼠标即可。约 1-2 小时完成。

## 0. 概述

本系统(编号本 / 编号管理系统)需要在 IIS 中以 **HTTPS** 方式对外提供访问。

代码已经改好(2026-07-09),本手册只讲 **IIS 服务器侧**需要做的 3 件事:

1. 加 HTTPS 站点绑定(端口 443,选你已有的证书)
2. 让 HTTP 访问自动跳转到 HTTPS
3. 重启应用 + 验证

**适用范围**:Windows Server 2016 / 2019 / 2022 + IIS 10

---

## 1. 前置条件(检查清单)

打开 PowerShell,逐条验证:

```powershell
# 1) IIS 是否装好
Get-Service -Name W3SVC
# 期望:Running(运行中)

# 2) ASP.NET Core Module V2 是否装好
Get-ChildItem 'IIS:\Modules' | Where-Object {$_.Name -like "*AspNetCore*"}
# 期望:看到 AspNetCoreModuleV2

# 3) WebSocket Protocol 角色服务是否装
Get-WindowsFeature Web-WebSockets
# 期望:Install State = Installed

# 4) .NET 8 Hosting Bundle 是否装
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' | Get-ItemProperty | Select-Object Release
# 期望:Release >= 528040
```

如果 **1 / 2 / 3** 任何一条不通过,需要先装:
- IIS 角色 → 添加"WebSocket Protocol"角色服务
- .NET 8 Hosting Bundle → 从 https://dotnet.microsoft.com/download/dotnet/8.0 下载安装

如果 **4** 不通过,装 .NET 8 Hosting Bundle 后 **重启服务器**(必须)。

---

## 2. 你需要的信息(找齐再开始)

| 信息 | 例子 | 怎么找 |
|------|------|--------|
| **网站域名** | `https://yourcompany.com` | 找你们公司 IT / 网管要 |
| **SSL 证书** | 已安装?通配?单域名? | IIS → 服务器证书 看现有 |
| **应用部署路径** | `D:\Apps\BootstrapBlazor.OnlyServer1` | 上次部署的位置 |
| **数据库连接串** | `Server=...;uid=...;password=...` | 找开发要,或从 `appsettings.Production.json` 找 |

**SSL 证书**这一项,你们公司有几种可能:

| 情况 | 怎么办 |
|------|--------|
| A. 证书已经装在 IIS 上(其他站点在用) | ✅ 直接用,选这个证书 |
| B. 有 .pfx 文件还没装 | 看下面 "3.5 安装新证书" |
| C. 没有证书,需要申请 | ⚠️ 找公司出钱买,或用 Let's Encrypt(免费但需公网域名) |

---

## 3. 部署步骤(GUI 为主,PowerShell 兜底)

### 3.1 确认代码已部署

```powershell
# 检查应用目录下是否有 web.config
Test-Path "D:\Apps\BootstrapBlazor.OnlyServer1\web.config"
# 期望:True

# 检查 web.config 里有 HSTS 头
Select-String -Path "D:\Apps\BootstrapBlazor.OnlyServer1\web.config" -Pattern "Strict-Transport-Security"
# 期望:找到一行
```

**如果 `Test-Path` 返回 False,说明代码还没部署。先让开发给你发部署包(参考 4.1 步骤)。**

---

### 3.2 给站点加 HTTPS 绑定

**方法 A:IIS GUI(推荐)**

```
1. 打开"IIS 管理器"(开始菜单 → Windows 管理工具 → Internet Information Services (IIS) 管理器)
2. 左侧树形菜单 展开 [你的服务器名] → 站点
3. 右键点击 "BootstrapBlazor" 站点 → "编辑绑定..."
4. 在弹出的"网站绑定"窗口:
   - 点击右侧 "添加..."
5. 弹窗"添加网站绑定":
   - 类型:选 "https"
   - IP 地址:"全部未分配"
   - 端口:443
   - 主机名:填你的域名(比如 yourcompany.com,不填也可以,看你们要求)
   - SSL 证书:从下拉框里选你公司已有的证书
6. 点"确定"
7. 回到"网站绑定"窗口,确认列表里现在有 http(80) 和 https(443) 两条
8. 点"关闭"
```

**方法 B:PowerShell(如果 GUI 失败)**

```powershell
# 把"yourdomain.com"和"证书友好名称"换成你实际的
New-WebBinding -Name "BootstrapBlazor" `
    -Protocol https `
    -Port 443 `
    -HostHeader "yourdomain.com" `
    -SslFlags 1

# 验证绑定
Get-WebBinding -Name "BootstrapBlazor" -Protocol https
```

---

### 3.3 让 HTTP 自动跳 HTTPS

**方法 A:IIS GUI(推荐)**

```
1. IIS 管理器 → 你的站点 "BootstrapBlazor"
2. 中间功能区 找到 "HTTP 重定向"(英文是 HTTP Redirect)图标
   - 如果没看到这个图标,需要先装:
     服务器管理器 → 添加角色 → Web 服务器 → HTTP 重定向
3. 双击打开
4. 勾 ☑ "将请求重定向到此目标"
5. 目标框里填:
   https://yourdomain.com{S}
   (其中 {S} 是 IIS 变量 = 原 URL 的 path+query)
6. 状态码:选 "已找到(302)" 或 "永久(301)"
   - 301 = 永久跳转,SEO 友好
   - 302 = 临时跳转,调试时用
   - 生产建议 301
7. 右侧操作 → "应用"
```

**方法 B:web.config 直接加(备选)**

如果 IIS 没装 HTTP Redirect 模块,直接编辑 `web.config` 也能跳。

在 `<system.webServer>` 里加:

```xml
<httpRedirect enabled="true" 
              destination="https://yourdomain.com" 
              exactDestination="false" 
              childOnly="false" 
              httpResponseStatus="Permanent" />
```

---

### 3.4 重启 IIS 应用池

```
1. IIS 管理器 → 左侧树 → "应用程序池"
2. 找到 "BootstrapBlazor" 应用池(你们之前给这个站点建的应用池)
3. 右键 → "重新启动"
   - 如果菜单是灰的,先点"停止"再点"启动"
4. 等 5-10 秒,刷新 IIS 管理器
```

**PowerShell 兜底**:

```powershell
Restart-WebAppPool -Name "BootstrapBlazor"
# 或硬重启(强制)
%windir%\system32\inetsrv\appcmd recycle apppool /apppool.name:"BootstrapBlazor"
```

---

### 3.5 安装新证书(如果你们还没证书)

**如果你手上只有 .pfx 文件**:

```
1. IIS 管理器 → 最顶层的服务器名(不是站点)
2. 中间功能区 → 双击 "服务器证书"
3. 右侧操作 → "导入..."
4. 弹窗:
   - .pfx 文件路径:浏览到你的 .pfx 文件
   - 密码:填证书的私钥密码(申请证书时设置的)
   - 选择证书存储:选 "个人"
5. 确定
```

**PowerShell 兜底**:

```powershell
$pfxPath = "D:\Certs\yourcompany.pfx"
$pfxPassword = ConvertTo-SecureString -String "证书密码" -Force -AsPlainText
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $pfxPassword
# 然后回到 3.2 步骤,在 SSL 证书下拉框里能看到这个新证书
```

---

## 4. 验证清单(部署完必须跑一遍)

按顺序,任何一条失败先停,看下面"5. 常见问题"。

### 验证 1:HTTP 自动跳 HTTPS

```powershell
# 用 curl 测试,期望返回 301/302 且 Location 是 https://
curl -I http://yourdomain.com
```

期望输出:
```
HTTP/1.1 301 Moved Permanently
Location: https://yourdomain.com/
```

**如果返回 200 而不是 301**:HTTP Redirect 没配好,回 3.3 节。

### 验证 2:HTTPS 能打开页面

```powershell
# 期望 200 OK
curl -I https://yourdomain.com
```

期望输出:
```
HTTP/1.1 200 OK
Strict-Transport-Security: max-age=31536000; includeSubDomains; preload
X-Content-Type-Options: nosniff
X-Frame-Options: SAMEORIGIN
```

### 验证 3:浏览器实际访问

```
1. 打开 Chrome / Edge
2. 访问 http://yourdomain.com
   - 应该自动跳到 https://yourdomain.com
3. 看地址栏:
   - 应该显示 🔒(锁)
   - 证书应该是公司名 / 域名(没有"不安全"红字)
4. 登录:
   - 用户名:1(默认超管)
   - 密码:Admin@123(或者你们改过的)
5. 登录后,多次刷新页面,应该不退出
```

### 验证 4:Cookie 是 Secure 的

```
1. 登录后,在页面上按 F12
2. 切到 "Application" 选项卡(在 Chrome 里)
3. 左侧 "Cookies" → 展开 "https://yourdomain.com"
4. 找到 ".AspNetCore.Identity.Application" 这条
5. 看右侧详情:
   - "Secure" 这列应该 ✅
   - "HttpOnly" 这列应该 ✅
   - "SameSite" 这列应该显示 "Strict"
```

**如果 Secure 不是 ✅**:说明代码配错,联系开发检查 `Program.cs` 的 `ConfigureApplicationCookie`。

### 验证 5:看应用日志

```powershell
# 打开应用日志目录
explorer "D:\Apps\BootstrapBlazor.OnlyServer1\logs"
# 看最新的 .log 文件,有 ERROR 的关注一下
```

期望:**没有 "ERR_" 开头的异常**。常见有的话,看 5.4 节。

---

## 5. 常见问题(踩坑速查)

### 5.1 浏览器一直转圈 / 打不开页面

**症状**:`https://yourdomain.com` 卡在加载状态

**排查**:
```powershell
# 1) 看应用是否在运行
Get-WebAppPoolState -Name "BootstrapBlazor"
# 期望:Started

# 2) 看端口是否在监听
Test-NetConnection -ComputerName localhost -Port 443
# 期望:TcpTestSucceeded = True

# 3) 看 443 binding
Get-WebBinding -Name "BootstrapBlazor" -Protocol https
# 期望:有 binding 信息,SSLFlags = 1
```

**修复**:
- 应用没运行 → 重启应用池(3.4)
- 端口没监听 → 证书没绑好(3.2 重新做)
- SSLFlags = 0 → PowerShell 重新加 binding

### 5.2 登录后立刻被踢出(死循环)

**症状**:输入正确密码,跳转到登录页,再登录,又跳转...

**原因**:
- A. HTTPS 没真正启用,Cookie.SecurePolicy=Always 让 cookie 在 HTTP 下不发
- B. 浏览器 HSTS 缓存混乱(开发期间切了 HTTP/HTTPS)

**修复**:
```powershell
# 1) 清浏览器 Cookie(让浏览器重新拿)
# 浏览器:F12 → Application → Storage → Clear site data

# 2) 如果还不行,临时改成"跟随请求"看是不是真的
# 联系开发,把 Program.cs 里的 SecurePolicy 改成 SameAsRequest 测一遍
# 能登录 = HTTPS 没配好,回 3.2-3.3
# 还不能 = 其他问题

# 3) 改完记得改回 Always
```

### 5.3 证书不受信任(浏览器显示红字"不安全")

**症状**:浏览器地址栏显示 🔓 而非 🔒,点开有"证书错误"

**原因**:证书不匹配域名 / 证书过期 / 证书是自签的

**修复**:
```
1. 双击 .cer 文件看证书信息:
   - 颁发给:必须包含你的域名
   - 有效期到期:必须 > 今天
2. 如果是自签证书(没经过 CA 认证):
   - 浏览器会一直报"不安全"
   - 内部系统可以加"受信任的根证书"到客户端:
     - mmc → 文件 → 添加/删除管理单元 → 证书 → 本地计算机 → 受信任的根证书颁发机构 → 导入
   - 或者花钱买正式证书(推荐)
```

### 5.4 日志里有 "ERR_SSL_PROTOCOL_ERROR"

**排查**:
```powershell
# 1) 确认 SSL Flags
Get-WebBinding -Name "BootstrapBlazor" -Protocol https
# 看 SSLFlags:
#   0 = 没启用 SSL
#   1 = 启用 SSL(本步骤应该有这个)
#   2 = 客户端证书
#   3 = 两者都要

# 2) 重新设
Set-WebBinding -Name "BootstrapBlazor" -BindingInformation "*:443:yourdomain.com" -PropertyName SslFlags -Value 1
```

### 5.5 升级代码后登录页打不开

**症状**:升级 web.dll 后,登录页 500 错误

**修复**:
```powershell
# 1) 重启 AppPool(让新代码生效)
Restart-WebAppPool -Name "BootstrapBlazor"

# 2) 看 stdout 日志
Get-Content "D:\Apps\BootstrapBlazor.OnlyServer1\logs\stdout_*.log" -Tail 50
# 找 "Exception" / "Error" 关键词
```

### 5.6 DataProtection 密钥丢失(全员被踢出)

**症状**:所有人都进不去页面,跳登录页

**原因**:`keys/` 目录被误删 / 应用池回收后没保留

**修复**:
```powershell
# 1) 确认 keys 目录
Test-Path "D:\Apps\BootstrapBlazor.OnlyServer1\keys"
# False = 密钥丢了,从备份恢复(见 docs/disaster-recovery.md)
# True 但空的 = 同上

# 2) 临时方案:全员重新登录
# 删除 keys 目录,应用启动时会自动生成新 key
# 但之前登录的所有人会需要重新登录
```

**预防**:`keys/` 目录应该和应用分离,放到独立盘,见 `docs/deployment.md` 6.1 节。

---

## 6. 部署完成确认

所有验证清单(第 4 节)通过后:

```
□ 验证 1:HTTP → HTTPS 跳转 ✓
□ 验证 2:HTTPS 返回 200 + HSTS 头 ✓
□ 验证 3:浏览器 🔒 + 登录不踢出 ✓
□ 验证 4:Cookie Secure = ✅ ✓
□ 验证 5:日志无 ERR_ ✓
```

把这个清单打印出来,完成后逐项打勾。**任何一项不过,回 5 节查对应问题**。

---

## 7. 联系开发

| 故障 | 谁处理 |
|------|--------|
| IIS 找不到某菜单 / 装不上某角色 | IT 自己(这是 IIS 基础) |
| 代码报错 / 改不了 web.config | 找开发(可能要重新部署) |
| 证书申请 / 续费 / 重新签发 | 公司 IT 网管 / 证书服务商 |
| 数据库连接不上 | DBA / 找开发 |
| 业务功能问题(登录、订单等) | 找开发 |

---

## 8. 回滚(如果 HTTPS 部署后出问题)

如果 HTTPS 部署后整个站挂了,可以快速回滚到 HTTP:

```powershell
# 1) 停应用池
Stop-WebAppPool -Name "BootstrapBlazor"

# 2) 删除 HTTPS binding
Remove-WebBinding -Name "BootstrapBlazor" -BindingInformation "*:443:yourdomain.com"

# 3) 关闭 HTTP Redirect
# IIS GUI → 站点 → HTTP 重定向 → 取消勾选

# 4) 启应用池
Start-WebAppPool -Name "BootstrapBlazor"

# 5) 验证 HTTP 能访问
curl -I http://yourdomain.com
# 期望 200

# 注意:Cookie SecurePolicy=Always 在 HTTP 下登录会死循环
# 临时方案(回滚期间):联系开发把 SecurePolicy 改成 SameAsRequest
```

**回滚 ≠ 解决**。回滚后用 HTTP 跑一段时间,等找到 HTTPS 部署的问题再切回来。

---

## 附录 A:常用 PowerShell 命令速查

```powershell
# 站点管理
Get-Website                                  # 看所有站点
Get-WebBinding -Name "BootstrapBlazor"       # 看 binding
Restart-WebAppPool -Name "BootstrapBlazor"   # 重启应用池
Get-WebAppPoolState -Name "BootstrapBlazor"  # 应用池状态

# 证书管理
Get-ChildItem Cert:\LocalMachine\My          # 看已装证书
Import-PfxCertificate -FilePath <path> -CertStoreLocation Cert:\LocalMachine\My -Password (ConvertTo-SecureString "..." -Force -AsPlainText)

# 日志
Get-Content "D:\Apps\BootstrapBlazor.OnlyServer1\logs\stdout_*.log" -Tail 100
Get-Content "D:\Apps\BootstrapBlazor.OnlyServer1\logs\fallback-*.log" -Tail 100

# 进程
Get-Process -Name w3wp                       # 看 w3wp 进程
Test-NetConnection -ComputerName localhost -Port 443  # 测端口

# 应用配置
Get-WebConfigProperty -Filter "/system.webServer/httpProtocol/customHeaders" -Name "."
```

---

## 附录 B:需要开发配合的事(应急联系)

如果在 IIS 上搞不定,可能需要开发介入(回到代码层面):

| 现象 | 开发要改什么 |
|------|------------|
| 登录死循环 | 临时改 `Cookie.SecurePolicy = SameAsRequest` |
| 静态资源 404 | 检查 `web.config` 的 handlers 段 |
| 应用启动慢 | 改 `ASPNETCORE_ENVIRONMENT=Production` |
| Cookie 丢失 | 检查 `DataProtection:KeyDirectory` 配置 |

**联系开发前**,把以下信息截图发过去:
1. 浏览器 F12 → Network 截图(第一个请求)
2. `D:\Apps\BootstrapBlazor.OnlyServer1\logs\stdout_*.log` 最后 50 行
3. IIS 应用池名称、绑定的 .NET CLR 版本(应"No Managed Code")

---

**手册版本**:v1.0 (2026-07-09)
**适用代码版本**:commit `2948639` 及之后
**更新人**:IT 部署完成后,在本手册底部加一行"已部署 / 部署人 / 日期"
