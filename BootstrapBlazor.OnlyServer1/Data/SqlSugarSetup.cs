using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Data
{
    /// <summary>
    /// SqlSugar 客户端 + 启动期 schema 比对的注册容器。
    ///
    /// 注意:本项目不负责建库 / 建表 / 灌种子数据 —— 那些由独立导入程序完成。
    /// 这里只负责:
    ///   1. 创建 ISqlSugarClient(线程安全的 SqlSugarScope)
    ///   2. 注册 AOP 钩子,把执行的 SQL 输出到 ILogger(走 NLog 通道)
    /// </summary>
    public class SqlSugarSetup : IDisposable
    {
        public ISqlSugarClient Db { get; }

        public SqlSugarSetup(IConfiguration configuration, ILogger<SqlSugarSetup> logger)
        {
            
            var connectionString = configuration.GetConnectionString("sqlServerConnection");
            //var connectionString = configuration.GetConnectionString("sqlServerConnectionStringOffice");
            //var connectionString = configuration.GetConnectionString("sqlServerConnectionStringHomeSA");

            var config = new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new ConfigureExternalServices
                {
                    EntityService = (property, column) =>
                    {
                        if (column.IsPrimarykey)
                        {
                            column.IsNullable = false;
                        }
                    }
                }
            };

            Db = new SqlSugarScope(config, db =>
            {
                db.Aop.OnLogExecuting = (sql, pars) =>
                {
                    // (2026-08-06 P0 优化)Debug 默认被 Default=Information 关掉;
                    // 但 pars.ToDictionary + SerializeObject 在 LogDebug 调用之前
                    // 就会跑,等于没开 Debug 也浪费一次字典构造 + JSON 序列化。
                    // 守门后:Debug 关 → 立即返回,零分配。
                    // 排查时把 appsettings.json "Logging:LogLevel:Default" 临时改 Debug 即可,无需重启服务。
                    if (!logger.IsEnabled(LogLevel.Debug)) return;

                    // 把参数序列化成 JSON 拼到 SQL 后面,方便排查"参数值到底是啥"
                    var parsJson = db.Utilities.SerializeObject(
                        pars.ToDictionary(p => p.ParameterName, p => p.Value));
                    logger.LogDebug("SQL: {Sql} {Parameters}", sql, parsJson);
                };
            });
        }

        public void Dispose()
        {
            Db?.Dispose();
        }
    }
}
