# 配置说明

`appsettings.json` 每一项的含义、默认值、调参影响。

## 主应用 `BootstrapBlazor.OnlyServer1/appsettings.json`

### `Logging`

NLog 接管后此项基本无效,保留为兜底。

| 键 | 默认 | 说明 |
|----|------|------|
| `Logging.LogLevel.Default` | `Information` | 全局默认日志级别 |
| `Logging.LogLevel.Microsoft.AspNetCore` | `Warning` | 框架日志降到 Warning |

### `AllowedHosts`

| 键 | 默认 | 说明 |
|----|------|------|
| `AllowedHosts` | `*` | 允许的主机头(`*` = 任意,生产建议改具体域名) |

### `Urls`

| 键 | 默认 | 说明 |
|----|------|------|
| `Urls` | `http://localhost:8373` | Web 监听地址 |

**生产部署**:此项不生效,实际由 IIS 站点绑定决定。

### `BootstrapBlazorOptions`

| 键 | 默认 | 说明 |
|----|------|------|
| `ToastDelay` | `4000` | Toast 自动关闭延时(ms) |
| `MessageDelay` | `4000` | Message 组件延时 |
| `SwalDelay` | `4000` | SweetAlert 延时 |
| `EnableErrorLogger` | `false` | 客户端错误是否上报到 ILogger |
| `FallbackCulture` | `en` | 回退文化 |
| `SupportedCultures` | `["zh-CN","en-US"]` | 支持的语言 |
| `DefaultCultureInfo` | `zh-CN` | 默认语言 |
| `TableSettings.CheckboxColumnWidth` | `36` | Table 多选列宽度 |
| `IgnoreLocalizerMissing` | `true` | 缺失本地化 key 时不抛错 |
| `StepSettings` | … | 数字步进控件默认值 |

### `ConnectionStrings`

⚠️ **生产环境禁止将明文密码提交到 git**。改用:
- 环境变量 `ConnectionStrings__sqlServerConnectionString=<value>`
- 或 Azure Key Vault / Secret Manager
- 或 `appsettings.Production.json`(加入 `.gitignore`)

| 键 | 用途 |
|----|------|
| `sqlServerConnectionStringOffice` | 业务库(MechanicalDesign 账号,无 DDL 权限) |
| `sqlServerConnectionStringSA` | 管理员账号(可建表) — DbMigrator 使用 |
| `sqlServerConnectionStringHomeSA` | 本地 SA 账号(开发用) — DbMigrator 使用 |

### `BusinessRules.DrawingType` ⭐ 重点

**运行时可调的业务规则,改这里即可,无需动代码**。

| 键 | 类型 | 默认 | 说明 |
|----|------|------|------|
| `ReactorType` | string | `"C08"` | 电抗器(`Orders.IsReactor=true`)对应的 DrawingType |
| `PowerRanges` | array | 见下 | Power → DrawingType 映射规则列表 |
| `Seeds` | dict | 见下 | 每种 DrawingType 的图号种子值(首次分配时用) |

**PowerRanges 匹配规则**:遍历列表,找第一条 `Min ≤ Power` 且 `(Max 为 null 或 Power ≤ Max)` 的规则。

```json
"PowerRanges": [
  { "Min": 0,   "Max": 2,    "Type": null },        // Power ≤ 2 无映射
  { "Min": 3,   "Max": 10,   "Type": "C02" },       // 3~10 MW → C02
  { "Min": 11,  "Max": 39,   "Type": "C03" },
  { "Min": 40,  "Max": 99,   "Type": "C04" },       // 40~99 MW → C04(变压器主力)
  { "Min": 100, "Max": 199,  "Type": "C05" },
  { "Min": 200, "Max": 399,  "Type": "C06" },
  { "Min": 400,              "Type": "C07" }        // ≥ 400 MW
]
```

**Seeds**(业务图号种子值,迁移用):

```json
"Seeds": {
  "C01": 0, "C02": 0, "C03": 0,
  "C04": 17000,   // 历史遗留:40~99 MW 的图号从 17001 开始
  "C05": 34000,
  "C06": 22893,
  "C07": 2992,
  "C08": 0, "C09": 0
}
```

### `Identity` ⭐ 安全策略

**改这里即可,无需动代码**。

```json
"Identity": {
  "Password": {
    "RequiredLength": 6,             // 最小长度
    "RequireDigit": false,            // 必须含数字
    "RequireUppercase": false,
    "RequireLowercase": false,
    "RequireNonAlphanumeric": false   // 必须含特殊字符
  },
  "Lockout": {
    "MaxFailedAccessAttempts": 5,           // 锁定前允许失败次数
    "DefaultLockoutTimeSpan": "00:05:00",   // 锁定时长
    "AllowedForNewUsers": true              // 新用户默认是否启用锁定
  },
  "User": {
    "RequireUniqueEmail": false
  },
  "SignIn": {
    "RequireConfirmedAccount": false  // 是否需要邮箱/手机确认
  }
}
```

**生产建议**:`RequiredLength ≥ 8` + 至少 3 类字符(`RequireDigit`/`RequireUppercase`/`RequireNonAlphanumeric` 至少 2 个 true)。

### `DataProtection:KeyDirectory`

DataProtection 密钥存储目录(cookie 加密用)。

- 默认: `keys/`(应用根目录)
- **生产**: 必须指向**持久盘**,否则 IIS 应用池回收后全员被踢
- 推荐: `D:\PersistentKeys\BootstrapBlazor`

## DbMigrator `BootstrapBlazor.DbMigrator/appsettings.json`

### `ConnectionStrings.SqlServer` / `sqlServerConnectionStringHomeSA`

DbMigrator 必须用**有 DDL 权限**的账号(SA 或 db_owner)。

### `Migration.SourceDirectory`

`--migrate --source` 的默认路径。

## 敏感配置管理

| 等级 | 存储方式 |
|------|---------|
| 🟢 非敏感(`Urls`、`BusinessRules`) | `appsettings.json`(入库) |
| 🟡 内部(连接字符串) | `appsettings.{Environment}.json`(本地)+ 环境变量(生产) |
| 🔴 敏感(密码、API Key) | **只**用环境变量或 Secret Manager,**永不**入库 |

**历史教训**:仓库曾含 `sqlServerConnectionStringSA` 明文密码,虽然已修复并改用环境变量替代,但需要保持警惕。