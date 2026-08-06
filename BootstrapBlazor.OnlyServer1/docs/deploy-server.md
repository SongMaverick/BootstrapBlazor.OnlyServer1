> ⚠️ **本文档已废弃(2026-08-03)**
>
> 项目已迁移到 **IIS + ASP.NET Core Module V2** 部署,本文描述的 Kestrel 自承载 + Windows Service 形态不再使用。
>
> 新的部署流程请看 **[deploy-it-runbook.md](deploy-it-runbook.md)** — 包含 IIS 角色前置、create-iis-site.ps1 用法、AppPool/证书/端口配置。
>
> 本文件保留作为历史参考,**不要按本文操作**,所有相关脚本(install-windows-service.ps1 / uninstall-windows-service.ps1 / generate-cert.ps1)已从 `scripts/` 删除。

---

# 服务器直接部署(Windows Service + 自签 HTTPS)【已废弃,仅留历史】

> 项目:DrawingNumber(内部工业系统)
> 目标环境:Windows Server(单机部署,内网用户访问)
> 数据库:SQL Server `JINWAD5A.AD101.SIEMENS-ENERGY.NET`(`sqlServerConnection` 不动)
> 运行形态:Windows Service,`LocalSystem` 启动,自动恢复,开机自启
> 访问入口:`https://140.231.237.180:8373`

> 跟 `README-DEPLOY.md`(IIS 部署路径)是平行方案,可任选其一。本方案适合「服务器不想装 IIS / 不想再装 ASP.NET Core Hosting Bundle」的场景。

---

## 1. 架构对比(为什么不需要 IIS 了)

| 组件 | IIS 路径(`README-DEPLOY.md`) | 本路径(本文) |
|---|---|---|
| HTTP 终结器 | IIS → ASP.NET Core Module V2 | Kestrel(自承载) |
| 进程托管 | IIS AppPool | Windows Service |
| HTTPS 终结器 | IIS HTTPS binding(443 端口) | Kestrel HTTPS(8373 端口) |
| 证书 | IIS 计算机证书/PFX 选择 | 直接读 PFX 文件(`Kestrel:Endpoints:Https:Certificate`) |
| 端口控制 | IIS Site binding | Kestrel `Kestrel:Endpoints:Https:Url` |
| WebSocket | 需装 IIS WebSocket 角色 | Kestrel 自带 |
| 反向代理 | 可选 | 无(直接对外) |

---

## 2. 关键决策与锁定参数

| 项 | 值 | 出处 |
|---|---|---|
| 服务名 | `DrawingNumber` | `Program.cs:UseWindowsService(o => o.ServiceName = "DrawingNumber")` |
| 安装路径 | `D:\Applications\MXB\DrawingNumber\web_publish\` | `install-windows-service.ps1` 默认 |
| HTTPS URL | `https://0.0.0.0:8373` | `appsettings.Production.json:Kestrel.Endpoints.Https.Url` |
| DP key 目录 | `<ContentRootPath>\keys` | `Program.cs:128` 默认 |
| PFX 路径 | `certs\server.pfx` | `appsettings.Production.json:Kestrel.Endpoints.Https.Certificate.Path` |
| PFX 密码 | 写在 `appsettings.Production.json` 内(用户决定不用 env 变量) | 同上 |
| 环境变量 | `ASPNETCORE_ENVIRONMENT=Production` | `install-windows-service.ps1` 注入到服务注册表 |
| 服务账号 | `LocalSystem` | `install-windows-service.ps1:sc create obj=` |
| 启动类型 | 自动(`start= auto`) | 同上 |
| 自恢复 | restart/0 / restart/30000 / restart/60000 | 同上 |
| 防火墙 | 入站 TCP 8373 | 同上 |
| DB 连接串 | `sqlServerConnection`(不动) | `Data\SqlSugarSetup.cs:21` |

---

## 3. 部署步骤

### 3.1 在开发机上重新发布(self-contained,免装 Runtime)

```powershell
cd D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.OnlyServer1\scripts
.\publish-server.ps1
# 默认输出到 D:\Publish\DrawingNumber\BootstrapBlazor.OnlyServer1.exe
```

产物约 80 MB(含 native SQLite / SChannel),目标服务器不需要预装 .NET 8。

### 3.2 拷贝到服务器(任选一种)

```powershell
# 直接 robocopy 到服务器 D:\Applications\MXB\DrawingNumber\web_publish\
# (走 D 盘的 admin 共享 D$,需要服务器端开放 C$ 同款权限;
#  或者用 IT 自己开的共享文件夹)
robocopy "D:\Publish\DrawingNumber" "\\<服务器>\D$\Applications\MXB\DrawingNumber\web_publish" /MIR /Z /NP

# 或打包后 scp / 共享盘
Compress-Archive "D:\Publish\DrawingNumber" "D:\Publish\DrawingNumber.zip"
```

服务器上确保 `D:\Applications\MXB\DrawingNumber\web_publish\` 下能看到 `BootstrapBlazor.OnlyServer1.exe` 和 `appsettings.json` 等。

### 3.3 在服务器上生成自签证书

服务器上以**管理员身份**运行 PowerShell:

```powershell
cd D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\generate-cert.ps1
# 默认密码 Pfx@2026!TCID#K7m9xQpLwRtY,要改用 -Password '...'
```

**生成内容**:LocalMachine\My 一张自签证书 → 导出 PFX 到 `D:\Applications\MXB\DrawingNumber\web_publish\certs\server.pfx` → 从 LocalMachine\My 删除(只剩 PFX)。

**为什么还要手工导入到 Trusted Root**(用户机):自签证书默认不被任何客户端信任,浏览器会弹「不安全」。**三种解决方案**按可操作性选:

- **方案 A:每台用户机导一次证书**(自签方案)
  1. 把 `certs\server.pfx` 拷到用户机
  2. 双击 PFX → 证书导入向导 → 下一步 → 下一步 → 选「将所有证书放入下列存储」→ 受信任的根证书颁发机构 → 完成

- **方案 B:若有内网 CA**(推荐)
  联系 AD CS 管理员签一张给 `DrawingNumber` 服务 → 用户机已信任 Root → 零操作

- **方案 C:反向代理**(不推荐)
  IIS / nginx 做 TLS termination + Kestrel 只 listen 127.0.0.1,等于又走回 IIS 路径

### 3.4 注册 Windows 服务 + 启动

服务器上以**管理员身份**:

```powershell
cd D:\Applications\MXB\DrawingNumber\web_publish\scripts
.\install-windows-service.ps1
```

做了什么:
- 自动建 `D:\Applications\MXB\DrawingNumber\web_publish\logs\` + `keys\`(DP)+ `certs\`(HTTPS PFX)目录
- 写 ACL:keys/ 只读、certs/ Admins 可改、logs/ 全权
- `sc.exe create DrawingNumber ... obj= LocalSystem start= auto`
- 注入 `ASPNETCORE_ENVIRONMENT=Production` 到服务的注册表
- 设置失败恢复:0s / 30s / 60s 重启
- 防火墙放行 TCP 8373
- `sc.exe start DrawingNumber`,输出 `Status: Running`

### 3.5 验证

```powershell
# 服务状态
Get-Service DrawingNumber
# 端口监听
netstat -ano | findstr :8373
# 首屏测试(在本机)
curl -k https://localhost:8373
# 用户机测试
# 浏览器:https://140.231.237.180:8373
# 登录:WorkerNumber=1 / Admin@123  — 改密码!
```

---

## 4. 故障排查

| 现象 | 排查点 |
|---|---|
| 服务起不来 / Stopped | Windows Event Viewer → Application 来源=DotNet / DrawingNumber |
| 端口未监听 8373 | `logs\fallback-*.log`(无文件先看 Event Log);Kestrel 启动失败可能在 fallback log |
| 浏览器报 `NET::ERR_CERT_SAN_MISMATCH` | 用户机地址栏输的是 `https://140.231.237.180:8373`,但证书 SAN 不含这个 IP → 重新跑 `generate-cert.ps1`,确认 IP 在 -DnsName 列表 |
| 浏览器报 `NET::ERR_CERT_AUTHORITY_INVALID` | 没把 PFX 导入 Trusted Root(见 3.3 方案 A) |
| 登录页能开,提交表单 500 | 详情查 logs,最常见:DB 连不上(防火墙/`sqlServerConnection` 值)/DP key 写不进 `keys\`(权限) |
| DataProtection key 不断重新生成 | `keys\` 写权限丢失 → 服务主动每 30 分钟生成新 key,全员 8h 后被踢 |
| 重启后用户都被踢重登 | `keys\` 目录被删掉 / 换机器后未同步 → 补救:全员重登一次 |

---

## 5. 升级流程

**项目约定**:服务 binPath 指向稳定路径 `D:\Applications\MXB\DrawingNumber\web_publish\`,不带日期戳。所以升级 = 用新 EXE/dll/wwwroot **覆盖** 这个目录,**保留** keys/ certs/ logs/ 三个持久化子目录(登录态、HTTPS 证书、历史日志)。

```powershell
# 1) 重新发布(开发机)
.\scripts\publish-server.ps1
# 默认输出到 D:\Publish\DrawingNumber\

# 2) 备份当前激活版本(回滚用)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item "D:\Applications\MXB\DrawingNumber\web_publish" "D:\Applications\MXB\DrawingNumber\web_publish_BAK_$stamp" -Recurse -Force

# 3) 停服务(短暂掉线,5-15 秒)
sc.exe stop DrawingNumber

# 4) 用 robocopy 把新 EXE/dll/wwwroot 覆盖过去,跳过持久化目录
#    /XD 排除目录:keys(DP key)/ certs(HTTPS PFX)/ logs(历史日志)
#    /XF 排除文件:*.pfx(双保险)
robocopy "D:\Publish\DrawingNumber" "D:\Applications\MXB\DrawingNumber\web_publish" /MIR /XD keys certs logs /XF *.pfx /NP

# 5) 启服务
sc.exe start DrawingNumber
Get-Service DrawingNumber
```

**回滚**:用备份的 `_BAK_<时间戳>\` 反向 robocopy 回来 + 重启服务:
```powershell
sc.exe stop DrawingNumber
robocopy "D:\Applications\MXB\DrawingNumber\web_publish_BAK_<时间戳>" "D:\Applications\MXB\DrawingNumber\web_publish" /MIR /XD keys certs logs /XF *.pfx /NP
sc.exe start DrawingNumber
```

**Note**:若想用"轻量 Junction 等价物"(零停机/秒级回滚),改用 `[sc config ... binPath=]` 切换路径到 `web_publish_<日期>\`,本文档保持"覆盖式升级"以求脚本简单、避免 junction 维护成本。

---

## 6. 卸载

```powershell
.\scripts\uninstall-windows-service.ps1
```

会:停止并删除服务、删除防火墙规则、清掉注册表里的环境变量。**不会**删除 `D:\Applications\MXB\DrawingNumber\web_publish\` 下的 EXE / 配置 / 日志 / PFX。

---

## 7. 监控 & 运维

| 入口 | 用途 |
|---|---|
| `Get-Service DrawingNumber` | 服务存活 |
| Windows Event Viewer → Application | 启动崩溃、托管异常 |
| `logs\fallback-*.log` | NLog 兜底日志(主日志在 SQL `Logs` 表) |
| `keys\*.xml` | DataProtection key,文件修改时间反映 key 轮转节奏 |

报警建议:服务停止 / 日志无新增 > 30 min / DP key 文件消失 → 通知。

---

## 8. 跟原 IIS 路径对比时的几个「坑」

1. **Cookie 行为变化**:`appsettings.Production.json` 让 env=Production,Cookie / Antiforgery 走 `SecurePolicy=Always`,**纯 HTTPS 能用**,**HTTP 直接 500**。这是预期内,不是 bug。
2. **`web.config` 不再生效**:Kestrel 不读它。program.cs 的 `app.UseHttpsRedirection()` 在 HTTP→HTTPS 跳转上仍然生效,但 HTTPS 重定向**只在有 HTTP 终结点时起作用**;本方案无 HTTP 终结点,这个调用是 no-op。
3. **`web.Release.config` 也不会被合并**:Web Deploy 路径专属。我们没用 Web Deploy。
4. **dotnet `Urls` 环境变量**:不建议再设。冲突时 appsettings 里的 `Kestrel.Endpoints` 优先。
5. **首次启动慢**:Kestrel 启动 ~3-5 秒。WebApplicationOptions + R2R 编译减小了首屏 RPS 损耗。
6. **HSTS 已启用**:浏览器一旦连过一次,1 年内强制 HTTPS。本方案纯 HTTPS,HSTS 是加分项。
7. **Dev 模式不受影响**:开 VS 按 F5 → `launchSettings.json` 设置 `ASPNETCORE_ENVIRONMENT=Development` → `appsettings.Production.json` 不被加载 → HTTP 8373 + 明文 cookie + 详细错误页,**开发调试零干扰**。
