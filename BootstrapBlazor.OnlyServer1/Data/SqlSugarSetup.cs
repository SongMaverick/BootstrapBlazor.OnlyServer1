using System.Text.RegularExpressions;
using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Data
{
    public class SqlSugarSetup
    {
        // SQL 文件路径(项目根目录)
        private const string DrawingNameSqlPath = "DrawingName.sql";

        // 幂等标记 row(执行成功后写入,下次启动检测到则跳过)
        private const int SeedMarkerId = 999999;
        private const string SeedMarkerName = "__DRAWING_NAMES_SEED_DONE__";

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
                typeof(DrawingNames),
                typeof(DrawingNumber),
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

                // 初始化图号名称数据(从 DrawingName.sql 读取真实业务数据)
                InitializeDrawingNamesFromSql(db);

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

        /// <summary>
        /// 从 DrawingName.sql 读取真实业务数据,全量重置 DrawingNames 表。
        ///
        /// 幂等性:成功执行后会插入一行 (Id=SeedMarkerId, ComponentName=SeedMarkerName)
        /// 作为标记,下次启动检测到该标记则跳过整个流程。
        ///
        /// 警告:这段代码会 DELETE 现有 DrawingNames 表的全部数据!
        /// 跑过一次确认数据正确后,请注释掉 InitializeDrawingNamesFromSql(db) 这一行,
        /// 防止以后每次启动都重置数据。
        /// </summary>
        private static void InitializeDrawingNamesFromSql(ISqlSugarClient db)
        {
            try
            {
                // 幂等检查:如果标记行已存在,跳过整个流程
                var markerExists = db.Queryable<DrawingNames>()
                    .Where(d => d.Id == SeedMarkerId && d.ComponentName == SeedMarkerName)
                    .Any();
                if (markerExists)
                {
                    Console.WriteLine("[DrawingNames seed] marker row already exists, skip reseed.");
                    return;
                }

                // 安全日志:打印当前表里有多少条数据,执行后会被清空
                var existingCount = db.Queryable<DrawingNames>().Count();
                Console.WriteLine($"[DrawingNames seed] current DrawingNames row count: {existingCount} (will be truncated and reseeded)");

                // 定位 SQL 文件:优先尝试项目根目录(运行时工作目录)
                string? sqlPath = ResolveSqlPath();
                if (sqlPath == null || !File.Exists(sqlPath))
                {
                    Console.WriteLine($"[DrawingNames seed] SQL file not found, skip. Tried: {sqlPath ?? "(null)"}");
                    return;
                }

                Console.WriteLine($"[DrawingNames seed] reading SQL file: {sqlPath}");
                var sqlContent = File.ReadAllText(sqlPath);

                var drawingNames = ParseDrawingNamesSql(sqlContent);
                if (drawingNames.Count == 0)
                {
                    Console.WriteLine("[DrawingNames seed] no rows parsed from SQL, abort.");
                    return;
                }

                Console.WriteLine($"[DrawingNames seed] parsed {drawingNames.Count} rows from SQL");

                // 包在事务里:DELETE + RESEED + INSERT 全部成功才提交
                db.Ado.BeginTran();
                try
                {
                    // 1. 清空表 + 重置自增(SqlSugar 的 TruncateTable 会自动重置 IDENTITY)
                    db.DbMaintenance.TruncateTable<DrawingNames>();
                    Console.WriteLine("[DrawingNames seed] table truncated, identity reset");

                    // 2. 用原生 SQL 执行批量 INSERT + SET IDENTITY_INSERT ON/OFF,
                    //    因为 SqlSugar 5.1.4 的 ORM API 不允许直接对自增主键写入,
                    //    我们手动拼 SQL 绕开这个限制。
                    db.Ado.ExecuteCommand(BuildSeedInsertSql(drawingNames));

                    Console.WriteLine($"[DrawingNames seed] inserted {drawingNames.Count} rows");

                    // 3. 写入幂等标记(SqlServer 自增表手动写 Id,必须 SET IDENTITY_INSERT)
                    var marker = new DrawingNames
                    {
                        Id = SeedMarkerId,
                        Part = "__SEED__",
                        ComponentName = SeedMarkerName,
                        CreatedBy = "System",
                        CreatedTime = DateTime.Now,
                        UpdatedBy = "System",
                        UpdatedTime = DateTime.Now,
                        Isdelete = true   // 标记行本身置为"已删除",过滤后用户看不到
                    };
                    db.Ado.ExecuteCommand(BuildSeedInsertSql(new List<DrawingNames> { marker }));

                    db.Ado.CommitTran();
                    Console.WriteLine("[DrawingNames seed] DONE. Remember to comment out InitializeDrawingNamesFromSql() call.");
                }
                catch (Exception ex)
                {
                    db.Ado.RollbackTran();
                    Console.WriteLine($"[DrawingNames seed] FAILED, rolled back: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DrawingNames seed] outer error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 构建一个 SET IDENTITY_INSERT ON + 多行 INSERT VALUES + SET IDENTITY_INSERT OFF 的 SQL 块,
        /// 用于把带显式 Id 的 DrawingNames 列表写入 SqlServer 自增表。
        /// </summary>
        private static string BuildSeedInsertSql(List<DrawingNames> items)
        {
            if (items == null || items.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SET IDENTITY_INSERT [DrawingNames] ON;");

            // 分批:每 100 行一个 INSERT 语句,避免单条 SQL 过长
            const int batchSize = 100;
            for (int i = 0; i < items.Count; i += batchSize)
            {
                var batch = items.Skip(i).Take(batchSize);
                sb.Append("INSERT INTO [DrawingNames] (Id, Part, ComponentName, CreatedBy, CreatedTime, UpdatedBy, UpdatedTime, Isdelete) VALUES ");
                sb.AppendLine(string.Join(", ", batch.Select(FormatRowForSql)));
            }

            sb.AppendLine("SET IDENTITY_INSERT [DrawingNames] OFF;");
            return sb.ToString();
        }

        /// <summary>
        /// 把单个 DrawingNames 转成 SQL VALUES 子句,如 (1, 'ESL-01', '油箱', '宋程', '2024-05-29 14:29:01.379', '宋程', '2024-05-29 14:29:01.379', 0)
        /// </summary>
        private static string FormatRowForSql(DrawingNames d)
        {
            return $"({d.Id}, {SqlStr(d.Part)}, {SqlStr(d.ComponentName)}, {SqlStr(d.CreatedBy)}, " +
                   $"{SqlDateTime(d.CreatedTime)}, {SqlStr(d.UpdatedBy)}, {SqlDateTime(d.UpdatedTime)}, " +
                   $"{(d.Isdelete ? 1 : 0)})";
        }

        private static string SqlStr(string? s)
        {
            // 把单引号转义成两个单引号,外面再包单引号
            return "'" + (s ?? "").Replace("'", "''") + "'";
        }

        private static string SqlDateTime(DateTime dt)
        {
            // SqlServer 兼容格式: yyyy-MM-dd HH:mm:ss.fff
            return "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + "'";
        }

        /// <summary>
        /// 解析 DrawingName.sql,提取每行 INSERT 的 8 个字段。
        /// 格式:INSERT INTO DrawingName (Id, Part, ComponentName, CreatedBy,
        ///        CreatedTime, UpdatedBy, UpdatedTime, Isdelete) VALUES (...);
        /// </summary>
        private static List<DrawingNames> ParseDrawingNamesSql(string sql)
        {
            var result = new List<DrawingNames>();

            // 匹配 VALUES 后到 ); 之间的全部内容(贪婪,但因为 VALUES 在行尾所以安全)
            // 这里用行级正则:^INSERT INTO DrawingName .* VALUES \((.*)\);$
            var lineRegex = new Regex(
                @"^\s*INSERT\s+INTO\s+DrawingName\s*\([^)]+\)\s*VALUES\s*\((.*?)\)\s*;?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var valueRegex = new Regex(
                @"^(.*?)(?:,(?![^()]*\))(.*))?$",
                RegexOptions.Singleline); // 占位用,实际切分走 SplitValues

            var lines = sql.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) continue;

                var match = lineRegex.Match(line);
                if (!match.Success)
                {
                    Console.WriteLine($"[DrawingNames seed] WARN: line does not match INSERT pattern, skip:\n  {line}");
                    continue;
                }

                var valuesRaw = match.Groups[1].Value;
                var fields = SplitSqlValues(valuesRaw);
                if (fields.Count != 8)
                {
                    Console.WriteLine($"[DrawingNames seed] WARN: expected 8 fields, got {fields.Count}, skip line:\n  {line}");
                    continue;
                }

                // fields: 0=Id, 1=Part, 2=ComponentName, 3=CreatedBy,
                //         4=CreatedTime, 5=UpdatedBy, 6=UpdatedTime, 7=Isdelete
                try
                {
                    var item = new DrawingNames
                    {
                        Id = int.Parse(fields[0]),
                        Part = fields[1],
                        ComponentName = fields[2],
                        CreatedBy = fields[3],
                        CreatedTime = ParseSqlDateTime(fields[4]),
                        UpdatedBy = fields[5],
                        UpdatedTime = ParseSqlDateTime(fields[6]),
                        Isdelete = fields[7] == "1" || fields[7].Equals("true", StringComparison.OrdinalIgnoreCase)
                    };
                    result.Add(item);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DrawingNames seed] WARN: parse error on line, skip:\n  {line}\n  Error: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 拆分 SQL VALUES (...) 中的字段,正确处理单引号字符串和逗号在字符串内的情形。
        /// </summary>
        private static List<string> SplitSqlValues(string valuesPart)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inString = false;

            for (int i = 0; i < valuesPart.Length; i++)
            {
                char c = valuesPart[i];

                if (c == '\'')
                {
                    inString = !inString;
                    // 不把单引号本身加入 current(除非是转义 '')
                    continue;
                }

                if (c == ',' && !inString)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString().Trim());
            }

            return result;
        }

        /// <summary>
        /// 解析 SQL 时间字面量,例如 '2024-05-29 14:29:01.3796292'
        /// </summary>
        private static DateTime ParseSqlDateTime(string s)
        {
            // 去掉可能残留的单引号(SplitSqlValues 已剥离,但防御一下)
            s = s.Trim().Trim('\'');

            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal, out var dt))
            {
                return dt;
            }
            // 解析失败兜底:用当前时间,不抛
            return DateTime.Now;
        }

        /// <summary>
        /// 定位 SQL 文件路径。优先级:
        ///   1. 环境变量 DRAWING_NAME_SQL_PATH(开发期覆盖用)
        ///   2. 当前工作目录下的 DrawingName.sql
        ///   3. 应用基目录向上找 DrawingName.sql(应对不同启动方式)
        /// </summary>
        private static string? ResolveSqlPath()
        {
            var env = Environment.GetEnvironmentVariable("DRAWING_NAME_SQL_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

            var cwd = Directory.GetCurrentDirectory();
            var direct = Path.Combine(cwd, DrawingNameSqlPath);
            if (File.Exists(direct)) return direct;

            var baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, DrawingNameSqlPath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
