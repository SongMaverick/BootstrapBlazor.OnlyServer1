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

// ============================================================
// 部署形态(2026-08-03 IIS 迁移):
//   - 生产: IIS + ASP.NET Core Module V2 in-process hosting
//     HTTPS 终结由 IIS 完成;应用由 ANCM 加载;web.config 决定 Kestrel/ANCM 行为
//   - dev  : dotnet run / VS F5 → ASP.NET Core 自带 Kestrel 监听 8373
//     (launchSettings.json:applicationUrl=http://localhost:8373)
//   EnvironmentName:
//     - dev 由 launchSettings.json 注入 Development
//     - prod 由 AppPool 环境变量注入 Production(create-iis-site.ps1 设置)
//     - 决定加载 appsettings.json + appsettings.Development.json 或 .Production.json
//   ContentRoot:
//     - IIS 路径下 = 站点物理路径(ANCM 自动设置)
//     - dev 路径下 = 当前工作目录,appsettings.json 必出现
// ============================================================
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
    // (2026-07-09 P0-3 HTTPS 部署)Antiforgery token Cookie 安全配置
    //   ⚠ SecurePolicy 按环境分流:
    //     - Development:SameAsRequest(配合 launchSettings.json 的 http://localhost:8373)
    //     - Production :Always(配合 IIS HTTPS binding)
    //     写死 Always 会让本地 HTTP 下浏览器拒传 cookie → Blazor 表单/CSRF 全废
    //   SameSite=Strict 永远生效(防 CSRF 跨站带 cookie)
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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
// "DataProtection:KeyDirectory" 覆盖指向独立持久盘(create-iis-site.ps1 默认
// 不设置,沿用 ContentRootPath/keys/;需 IIS_IUSRS 对该目录有写权限)。
//
// (2026-08-03 IIS 迁移)HTTPS 服务器证书现在由 IIS 站点绑定,
// appsettings.Production.json:Kestrel 节点已删除,不再有 certs/ 目录。
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
    // (2026-07-09 P0-3 HTTPS 部署)Cookie 安全配置
    //   ⚠ SecurePolicy 按环境分流:
    //     - Development:SameAsRequest(配合 launchSettings.json 的 http://localhost:8373)
    //     - Production :Always(配合 IIS HTTPS binding)
    //     写死 Always 会让本地 HTTP 下浏览器拒传 cookie → 登录态失效,表现为反复跳登录页
    //   其他生产安全项全环境一致(改这里只动 SecurePolicy):
    //     - SameSite=Strict:防 CSRF 跨站带 cookie(配合 antiforgery)
    //     - ExpireTimeSpan=8h:工作时段内有效,下班即失效(工厂场景:工人上班登录,下班关电脑)
    //     - SlidingExpiration=true:活跃用户自动续期
    // ============================================================
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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
builder.Services.AddScoped<OrderMDDetailService>();
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
    // (2026-08-03 IIS 迁移后)
    //   - UseHsts:输出 Strict-Transport-Security 响应头
    //     告诉浏览器"未来 1 年内,即使你输入 http://... 也强制升级 https://"
    //   - **HTTP→HTTPS 重定向不再走 app.UseHttpsRedirection()**
    //     原因:ANCM in-process 模式下,IIS 已做 TLS 终结,请求到应用时
    //     HttpRequest.IsHips 永远 = true,UseHttpsRedirection 永远不会触发。
    //     改用 web.config 的 <rewrite> 规则,在 IIS 层 301 重定向
    //     (见 web.config /<system.webServer>/<rewrite>)
    // 开发环境不启用 HSTS(否则本地 http://localhost 会无限重定向)
    // ============================================================
    app.UseHsts();
}

// ============================================================
// (2026-08-03 IIS 迁移后)中间件层安全响应头 — defense-in-depth
//   - dev/dotnet run:Kestrel 不读 web.config,中间件层是这 4 个头**唯一**来源
//   - IIS 生产:web.config <customHeaders>(L43-52)输出同样的 4 个头,
//     中间件层是冗余输出(同值,浏览器取首个,无害;防御纵深 — 即使某次发布
//     误删了 web.config 或运维手工改 web.config 删了头,中间件层仍兜底)
//   - 改这里:必须同步改 web.config 的 <customHeaders>(参见 web.config:43-52),
//     否则 dev / prod 头值不一致(浏览器容忍同值重复,不容忍不同值)
//   - 放在 UseExceptionHandler 之后:错误页 (/Error) 也带头
//   - 放在 UseResponseCompression 之前:头能进压缩响应
//   - 不重复发 HSTS:已由 app.UseHsts() 处理 (IIS 路径另由 web.Release.config 输出)
// ============================================================
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Frame-Options"]         = "SAMEORIGIN";
    h["X-Content-Type-Options"]  = "nosniff";
    h["Referrer-Policy"]         = "same-origin";
    h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'self';";
    await next();
});

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