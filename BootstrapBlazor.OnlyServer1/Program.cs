using System.Text;
using BootstrapBlazor.OnlyServer1.Components;
using BootstrapBlazor.OnlyServer1.Data;
using BootstrapBlazor.OnlyServer1.Services;
using Microsoft.AspNetCore.SignalR;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddBootstrapBlazor();

builder.Services.AddSingleton<WeatherForecastService>();

// 增加 Table 数据服务操作类
builder.Services.AddTableDemoDataService();

// 配置 SqlSugar
SqlSugarSetup.AddSqlSugarSetup(builder.Services, builder.Configuration);

// 注册认证服务
builder.Services.AddScoped<AuthService>();

// 注册数据导入服务
builder.Services.AddSingleton<UserDataImport>();
builder.Services.AddSingleton<OrderDataImport>();
builder.Services.AddSingleton<OrdersItemImport>();
builder.Services.AddSingleton<C0XDataImport>();

// 增加 SignalR 服务数据传输大小限制配置
builder.Services.Configure<HubOptions>(option => option.MaximumReceiveMessageSize = null);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseResponseCompression();
}

app.UseStaticFiles();

app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// 导入用户数据（仅运行一次）
//var db = app.Services.GetService<ISqlSugarClient>();
//var userImport = app.Services.GetService<UserDataImport>();
//if (db != null && userImport != null)
//{
//    await userImport.ImportUsersFromCode(db);
//}

// 导入普通订单数据（仅运行一次）
//var db = app.Services.GetService<ISqlSugarClient>();
//var orderImport = app.Services.GetService<OrderDataImport>();
//if (db != null && orderImport != null)
//{
//    var sqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Order.sql");
//    await orderImport.ImportOrdersFromSqlFile(db, sqlFilePath);
//}

// 导入电抗器订单数据（仅运行一次）
//var db = app.Services.GetService<ISqlSugarClient>();
//var orderImport = app.Services.GetService<OrderDataImport>();
//if (db != null && orderImport != null)
//{
//    var reactorSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ReactorOrder.sql");
//    await orderImport.ImportReactorOrdersFromSqlFile(db, reactorSqlFilePath);
//}

// 导入订单映射关系（仅运行一次）
//var db = app.Services.GetService<ISqlSugarClient>();
//var ordersItemImport = app.Services.GetService<OrdersItemImport>();
//if (db != null && ordersItemImport != null)
//{
//    var orderSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Order.sql");
//    var reactorOrderSqlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ReactorOrder.sql");
//    await ordersItemImport.ImportOrdersItemMapping(db, orderSqlFilePath, reactorOrderSqlFilePath);
//}

// 导入C04-C08表数据（仅运行一次）
var db = app.Services.GetService<ISqlSugarClient>();
var c0xImport = app.Services.GetService<C0XDataImport>();
if (db != null && c0xImport != null)
{
    var sqlFilesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..");
    await c0xImport.ImportC0XDataAsync(db, sqlFilesPath);
}

app.Run();
