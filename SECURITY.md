# 安全政策

## 报告漏洞

如发现安全漏洞,**请勿在 GitHub Issues 公开**,直接邮件发给项目维护者:
📧 security@<your-domain>.local

我们会在 24 小时内确认并在 7 天内修复或给出缓解方案。

## 支持的版本

| 版本 | 支持状态 |
|------|---------|
| 主分支 (`master`) | ✅ 活跃维护 |
| 历史发布(无 tag) | ❌ 不再维护 |

## 已知安全约束

参见 `appsettings.json` 配置项说明 — Identity 密码策略 / 锁定策略 / DataProtection 配置。

## 安全编码规范

参见 [CONTRIBUTING.md](CONTRIBUTING.md) 的"不要做的事"。

特别强调:

- ❌ **不提交任何凭据到 git**(连接字符串密码、API Key 等)
  - 历史教训:`appsettings.json` 曾含明文密码,虽然已修复,但需保持警惕
- ❌ **不写明文日志**(用户密码、token、PII)
- ✅ **跨用户输入必须校验**(用 DataAnnotations + 服务端二次校验)
- ✅ **SQL 走 ORM 参数化**(SqlSugar 默认就是,不要手动拼字符串)

## 依赖安全

本项目主要 NuGet 依赖:

- `BootstrapBlazor` 8.*
- `SqlSugarCore` 5.1.4
- `NLog` 6.*
- `Microsoft.AspNetCore.*` 8.0.*

升级到安全补丁版本请遵循:
1. `dotnet list package --vulnerable --include-transitive`
2. 升级到无漏洞版本
3. 在 PR 描述里写明影响的版本范围