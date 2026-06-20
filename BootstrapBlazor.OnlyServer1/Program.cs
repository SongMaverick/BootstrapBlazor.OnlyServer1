using System.Text;
using BootstrapBlazor.OnlyServer1.Components;
using BootstrapBlazor.OnlyServer1.Data;
using BootstrapBlazor.OnlyServer1.Services;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NLog.Web;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// NLog 接管 ASP.NET Core 的 ILogger 系统
// ClearProviders 干掉默认的 Console/Debug/EventSource 等 provider,
// 之后所有 ILogger<T> 输出的日志都会走 NLog(由 NLog.config 决定怎么落)
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
builder.Host.UseNLog();

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddBootstrapBlazor();

builder.Services.AddSingleton<WeatherForecastService>();

builder.Services.AddTableDemoDataService();

SqlSugarSetup.AddSqlSugarSetup(builder.Services, builder.Configuration);

builder.Services.AddScoped<AuthService>();

builder.Services.AddSingleton<UserDataImport>();
builder.Services.AddSingleton<OrderDataImport>();
builder.Services.AddSingleton<OrdersItemImport>();
builder.Services.AddSingleton<C0XDataImport>();

builder.Services.AddScoped<DrawingNumberService>();
builder.Services.AddScoped<DrawingNamesDataInitializer>();

builder.Services.Configure<HubOptions>(option => option.MaximumReceiveMessageSize = null);

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error");
    app.UseResponseCompression();
}

app.UseStaticFiles();

app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// 应用停止时,强制 flush NLog 队列(避免最后一批日志丢失)
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    LogManager.Shutdown();   // 同步等待所有异步 target flush 完毕
});

// var db = app.Services.GetService<ISqlSugarClient>();
// var orderImport = app.Services.GetService<OrderDataImport>();
// if (db != null && orderImport != null) {
//     var reactorSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Order.sql");
//     await orderImport.ImportOrdersFromSqlFile(db, reactorSqlFilePath);
// }

// var db = app.Services.GetService<ISqlSugarClient>();
// var orderImport = app.Services.GetService<OrderDataImport>();
// if (db != null && orderImport != null) {
//     var reactorSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ReactorOrder.sql");
//     await orderImport.ImportReactorOrdersFromSqlFile(db, reactorSqlFilePath);
// }

// var db = app.Services.GetService<ISqlSugarClient>();
// var ordersItemImport = app.Services.GetService<OrdersItemImport>();
// if (db != null && ordersItemImport != null) {
//     var orderSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Order.sql");
//     var reactorOrderSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ReactorOrder.sql");
//     await ordersItemImport.ImportOrdersItemMapping(db, orderSqlFilePath, reactorOrderSqlFilePath);
// }

// var db = app.Services.GetService<ISqlSugarClient>();
// var c0xImport = app.Services.GetService<C0XDataImport>();
// if (db != null && c0xImport != null) {
//     var sqlFilesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..");
//     await c0xImport.ImportC0XDataAsync(db, sqlFilesPath);
// }

app.Run();
