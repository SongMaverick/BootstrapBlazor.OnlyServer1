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

builder.Services.AddAntiforgery();

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