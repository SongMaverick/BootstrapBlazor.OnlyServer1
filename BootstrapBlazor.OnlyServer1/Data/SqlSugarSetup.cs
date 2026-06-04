
using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Data
{
    public class SqlSugarSetup
    {
        public static void AddSqlSugarSetup(IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("sqlServerConnectionStringOffice");
            //Console.WriteLine($"Using connection string: {connectionString}");

            var config = new ConnectionConfig()
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

            var db = new SqlSugarScope(config, db =>
            {
                db.Aop.OnLogExecuting = (sql, pars) =>
                {
                    Console.WriteLine(sql + "\r\n" + db.Utilities.SerializeObject(pars.ToDictionary(it => it.ParameterName, it => it.Value)));
                };
            });

            services.AddSingleton<ISqlSugarClient>(db);
            services.AddSingleton<UserDataImport>();

            InitDatabase(db);
        }

        public static void InitDatabase(ISqlSugarClient db)
        {
            Console.WriteLine("Starting database initialization...");
            try
            {
                db.DbMaintenance.CreateDatabase();
                Console.WriteLine("Database created/verified");

                db.CodeFirst.InitTables(
                    typeof(Users),
                    typeof(Orders),
                    typeof(OrdersItem),
                    typeof(C01),
                    typeof(C02),
                    typeof(C03),
                    typeof(C04),
                    typeof(C05),
                    typeof(C06),
                    typeof(C07),
                    typeof(C08),
                    typeof(C09)
                );
                Console.WriteLine("Tables initialized");

                // 测试数据库连接和数据
                var userCount = db.Queryable<Users>().Count();
                Console.WriteLine($"Total users in database: {userCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database initialization error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
