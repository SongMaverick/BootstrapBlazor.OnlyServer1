using System.Text;
using BootstrapBlazor.OnlyServer1.Components;
using BootstrapBlazor.OnlyServer1.Data;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NLog.Web;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// NLog 接管 ASP.NET Core 的 ILogger 系统
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
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
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
    options.User.RequireUniqueEmail = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddUserStore<SqlSugarUserStore>()
.AddRoles<Roles>()
.AddRoleStore<SqlSugarRoleStore>()
.AddRoleManager<RoleManager<Roles>>()
.AddSignInManager()
.AddDefaultTokenProviders()
.AddClaimsPrincipalFactory<DisplayNameClaimsFactory>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ============================================================
// 业务 Service
// ============================================================
builder.Services.AddScoped<DrawingNumberService>();
builder.Services.AddScoped<IDrawingNumberAllocator, DrawingNumberAllocator>();
builder.Services.AddScoped<OrdersService>();
builder.Services.AddScoped<UsersService>();
builder.Services.AddScoped<DepartmentsService>();
builder.Services.AddScoped<PermissionsService>();
builder.Services.AddScoped<RolesService>();

builder.Services.AddSingleton<IDrawingTypeRegistry, DrawingTypeRegistry>();
builder.Services.AddSingleton<OrderLockProvider>();

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