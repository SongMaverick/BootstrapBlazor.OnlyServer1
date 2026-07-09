using System.Text;
using BootstrapBlazor.OnlyServer1.Components;
using BootstrapBlazor.OnlyServer1.Data;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.BusinessRules;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.DrawingNames;
using BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NLog.Web;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// NLog 接管 ASP.NET Core 的 ILogger 系统
// 2026-07-07 Review #M4:Trace 级别在生产也输出,会泄漏敏感信息进 SQL `Logs` 表。
// 生产建议 Information(默认);开发期可临时改 Trace 排查。
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
builder.Host.UseNLog();

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();

builder.Services.AddAntiforgery(options =>
{
    // (2026-07-09 P0-3 HTTPS 部署)Antiforgery token Cookie 强制只在 HTTPS 连接上发送
    // 配合 ConfigureApplicationCookie 一起,防中间人嗅探 / 降级攻击
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddBootstrapBlazor();

// SqlSugar 客户端 + 启动期 SQL 日志 AOP(走 ILogger → NLog)
builder.Services.AddSingleton<SqlSugarSetup>();
builder.Services.AddSingleton<ISqlSugarClient>(sp => sp.GetRequiredService<SqlSugarSetup>().Db);

// HttpContext 访问器:供 CurrentUserAccessor / ClaimsPrincipal 扩展读取
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserAccessor>();

// ============================================================
// ASP.NET Core Identity(标准管道 + 自定义 SqlSugar Store)
// ============================================================
builder.Services.AddIdentityCore<Users>(options =>
{
    // Identity 配置从 appsettings.json "Identity" 节绑定 — 改强度/锁定策略不动代码
    builder.Configuration.GetSection("Identity").Bind(options);
})
.AddUserStore<SqlSugarUserStore>()
.AddRoles<Roles>()
.AddRoleStore<SqlSugarRoleStore>()
.AddRoleManager<RoleManager<Roles>>()
.AddSignInManager()
.AddDefaultTokenProviders()
.AddClaimsPrincipalFactory<DisplayNameClaimsFactory>();

// ============================================================
// SecurityStamp validator(2026-07-09 Review #H1-NEW 修复):
// 默认 ValidationInterval=30min 太慢,改成 5min,确保角色/权限变更后
// 用户在 5 分钟内自动续上新的 Permission claim,不必主动重登。
//
// 原理(每 5 分钟,SignInManager 自动跑):
//   1) 读 cookie 里的 SecurityStamp 与 DB 当前 Stamp 比对
//   2) Stamp 一致 → 调 CreateUserPrincipalAsync → 我们的
//      DisplayNameClaimsFactory 重新查 UserRoles × RolePermissions × Permissions
//      → 拿到最新的权限码 → 静默续写 cookie(ReplacePrincipal + ShouldRenew)
//   3) Stamp 不一致(改了密码/启停 2FA 等)→ 强制 SignOut,用户必须重登
//
// 已知行为:
//   - 同一会话每 5 分钟触发一次(不是每次请求)
//   - 触发时多查 3 张表(都已建索引),性能开销可忽略
//   - 不依赖用户主动重登,适合 24/7 工业场景
//   - 修改 Gid / DisplayName / Permission 都不需要额外操作,
//     5 分钟内自动续期
//
// 不删这段配置、不删这个注释。后续如需"权限变更立即踢人"再叠一层
// UpdateSecurityStampAsync 即可(在 RolesService.AddUserToRoleAsync 等
// 变更后调一次,触发强制 SignOut)。
// ============================================================
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(5);
});
builder.Services.AddScoped<SecurityStampValidator<Users>>();

// 持久化 DataProtection 密钥到应用根下 keys/ 目录,避免 IIS 应用池回收 / 服务器
// 重启后所有用户 cookie 失效 → 全员被强制重新登录。路径可在 appsettings 用
// "DataProtection:KeyDirectory" 覆盖指向独立持久盘。
var keyDir = builder.Configuration["DataProtection:KeyDirectory"];
if (string.IsNullOrWhiteSpace(keyDir))
    keyDir = Path.Combine(builder.Environment.ContentRootPath, "keys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDir))
    .SetApplicationName("BootstrapBlazor.OnlyServer1");

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    // ============================================================
    // (2026-07-09 P0-3 HTTPS 部署)Cookie 安全配置:
    //   - SecurePolicy=Always:Cookie 只在 HTTPS 连接上发送,HTTP 下不发
    //     (防中间人嗅探、防 HTTPS 降级到 HTTP 后被窃取登录态)
    //   - SameSite=Strict:防 CSRF 跨站带 Cookie(配合 antiforgery)
    //   - ExpireTimeSpan=8h:工作时段内有效,下班即失效
    //     (工厂场景:工人上班登录,下班关电脑,8 小时够用)
    //   - SlidingExpiration=true:活跃用户自动续期
    // ============================================================
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ============================================================
// 业务 Service
// ============================================================
builder.Services.AddScoped<C0XQueryService>();
builder.Services.AddScoped<C0XWriteService>();
builder.Services.AddScoped<DrawingNameService>();
builder.Services.AddScoped<IDrawingNumberAllocator, DrawingNumberAllocator>();
builder.Services.AddScoped<OrdersService>();
builder.Services.AddScoped<UsersService>();
builder.Services.AddScoped<DepartmentsService>();
builder.Services.AddScoped<PermissionsService>();
builder.Services.AddScoped<RolesService>();

builder.Services.AddSingleton<IDrawingTypeRegistry, DrawingTypeRegistry>();
builder.Services.AddSingleton<OrderLockProvider>();

// 业务规则:DrawingTypeOptions(Power → C0X 映射、电抗器类型、种子值)从 appsettings 绑定
builder.Services.Configure<BootstrapBlazor.OnlyServer1.Models.BusinessRules.DrawingTypeOptions>(
    builder.Configuration.GetSection("BusinessRules:DrawingType"));

builder.Services.Configure<HubOptions>(option => option.MaximumReceiveMessageSize = 64 * 1024);

// ResponseCompression:压缩 Blazor Server 的 SignalR / 静态资源传输
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error");

    // ============================================================
    // (2026-07-09 P0-3 HTTPS 部署)
    //   - UseHsts:输出 Strict-Transport-Security 响应头
    //     告诉浏览器"未来 1 年内,即使你输入 http://... 也强制升级 https://"
    //   - UseHttpsRedirection:HTTP 请求 302 跳 HTTPS(双保险,
    //     即使 IIS 没配 HTTP Redirect 也能跳)
    // 开发环境不启用(否则本地 http://localhost 会无限重定向)
    // ============================================================
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseResponseCompression();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapGet("/Account/Login", (HttpContext ctx) => Results.Redirect("/login" + ctx.Request.QueryString.Value));

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapControllers();

// 应用停止时,强制 flush NLog 队列(避免最后一批日志丢失)
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    LogManager.Shutdown();
});

app.Run();