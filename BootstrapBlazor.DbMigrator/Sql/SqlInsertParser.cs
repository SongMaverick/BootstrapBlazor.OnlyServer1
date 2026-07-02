using System.Text.RegularExpressions;

namespace BootstrapBlazor.DbMigrator.Sql
{
    /// <summary>
    /// 通用 SQL INSERT 解析器。
    /// 支持格式:
    ///   INSERT INTO "TableName" (col1, col2, ...) VALUES (val1, val2, ...);
    ///   INSERT INTO TableName (col1, ...) VALUES (val1, ...);
    ///   INSERT [dbo].[TableName] (col1, ...) VALUES (val1, ...);
    ///   INSERT [dbo].[TableName] ([col1], ...) VALUES (val1, ...);
    ///
    /// 不支持:多行 VALUES、SELECT INTO、BULK INSERT 等复杂语句。
    /// </summary>
    public static class SqlInsertParser
    {
        // 匹配整条 INSERT 语句:table 部分 + column 部分 + value 部分
        // RegexOptions.Singleline 让 . 匹配换行
        // 不要求 ; 结尾,允许任意空白
        // 注意: 贪婪模式匹配 VALUES 后面的内容直到下一个 INSERT 或文件结束
        private static readonly Regex InsertRegex = new(
            @"INSERT\s+(?:INTO\s+)?(?<table>[\[\]""\w\.]+?)\s*\((?<cols>[^)]+)\)\s*VALUES\s*\((?<vals>(?:[^()]|\([^()]*\))*)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<SqlInsertRow> ParseFile(string sqlFilePath, string? forceTableName = null)
        {
            var content = EncodingHelper.ReadAllText(sqlFilePath);
            return ParseText(content, forceTableName);
        }

        public static List<SqlInsertRow> ParseText(string sql, string? forceTableName = null)
        {
            var rows = new List<SqlInsertRow>();
            // 移除 USE/CREATE TABLE/GO 等非 INSERT 语句(简单过滤,匹配新行起首关键字)
            var cleanSql = Regex.Replace(sql,
                @"^\s*(USE\s+\[?\w+\]?|GO|/\*.*?\*/|--[^\n]*|//[^\n]*)",
                "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline);

            // 用 Regex.Matches 直接找所有 INSERT...VALUES(...) 块
            // 比按行解析更可靠:支持单行无分号 / 多行有分号 / 任意空白
            var matches = InsertRegex.Matches(cleanSql);

            foreach (Match m in matches)
            {
                var tableName = ExtractTableName(m.Groups["table"].Value);
                var colsRaw = m.Groups["cols"].Value;
                var valsRaw = m.Groups["vals"].Value;

                var columns = SplitCsv(colsRaw).Select(c => c.Trim().Trim('[', ']', '"')).ToList();
                var values = SplitValues(valsRaw);

                if (columns.Count != values.Count) continue;

                var row = new SqlInsertRow { TableName = tableName };
                for (int i = 0; i < columns.Count; i++)
                    row[columns[i]] = values[i];
                rows.Add(row);
            }

            return rows;
        }

        private static SqlInsertRow? ParseSingleStatement(string stmt)
        {
            var m = InsertRegex.Match(stmt);
            if (!m.Success) return null;

            var tableName = ExtractTableName(m.Groups["table"].Value);
            var colsRaw = m.Groups["cols"].Value;
            var valsRaw = m.Groups["vals"].Value;

            var columns = SplitCsv(colsRaw).Select(c => c.Trim().Trim('[', ']', '"')).ToList();
            var values = SplitValues(valsRaw);

            if (columns.Count != values.Count)
            {
                // 列数与值数不匹配,跳过(可能是 CREATE TABLE 或 USE 之类的语句混入了)
                return null;
            }

            var row = new SqlInsertRow { TableName = tableName };
            for (int i = 0; i < columns.Count; i++)
                row[columns[i]] = values[i];
            return row;
        }

        private static string ExtractTableName(string raw)
        {
            // raw 可能是 "TableName" / [dbo].[TableName] / dbo.TableName / TableName
            var cleaned = raw.Replace("[", "").Replace("]", "").Replace("\"", "");
            var parts = cleaned.Split('.');
            return parts[^1].Trim();
        }

        /// <summary>拆分列定义(逗号分隔,但要尊重括号嵌套,简单实现:按 , 拆)</summary>
        private static List<string> SplitCsv(string s)
        {
            var result = new List<string>();
            int depth = 0;
            bool inStr = false;
            char strCh = '\0';
            var cur = new System.Text.StringBuilder();
            foreach (var c in s)
            {
                if (inStr)
                {
                    cur.Append(c);
                    if (c == strCh) inStr = false;
                    continue;
                }
                if (c == '\'' || c == '"') { inStr = true; strCh = c; cur.Append(c); continue; }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (c == ',' && depth == 0)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(c);
            }
            if (cur.Length > 0) result.Add(cur.ToString());
            return result;
        }

        /// <summary>拆分 VALUES 部分,正确处理字符串内的逗号</summary>
        private static List<object?> SplitValues(string s)
        {
            var result = new List<object?>();
            int depth = 0;
            bool inStr = false;
            char strCh = '\0';
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\' && i + 1 < s.Length) { cur.Append(c); cur.Append(s[i + 1]); i++; continue; }
                    cur.Append(c);
                    if (c == strCh) inStr = false;
                    continue;
                }
                if (c == '\'' || c == '"') { inStr = true; strCh = c; cur.Append(c); continue; }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (c == ',' && depth == 0)
                {
                    result.Add(ParseScalar(cur.ToString()));
                    cur.Clear();
                }
                else cur.Append(c);
            }
            if (cur.Length > 0) result.Add(ParseScalar(cur.ToString()));
            return result;
        }

        /// <summary>解析单个标量:字符串 / NULL / 整数 / 小数 / datetime</summary>
        private static object? ParseScalar(string raw)
        {
            var s = raw.Trim();
            if (s.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null;

            // 字符串: 'xxx' 或 N'xxx'
            if ((s.StartsWith("'") && s.EndsWith("'")) || (s.StartsWith("N'") && s.EndsWith("'")))
            {
                var inner = s.StartsWith("N'") ? s.Substring(2, s.Length - 3) : s.Substring(1, s.Length - 2);
                return inner.Replace("''", "'");
            }

            // datetime: CAST(N'2024-...' AS DateTime) — 提取内部字符串
            var castMatch = Regex.Match(s, @"CAST\(N?'([^']+)' AS DateTime\)", RegexOptions.IgnoreCase);
            if (castMatch.Success)
            {
                if (DateTime.TryParse(castMatch.Groups[1].Value, out var dt)) return dt;
                return castMatch.Groups[1].Value;
            }

            // 整数
            if (long.TryParse(s, out var l)) return l;

            // 小数
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dec)) return dec;

            // 兜底:原字符串
            return s;
        }
    }

    public class SqlInsertRow
    {
        public string TableName { get; set; } = "";
        private readonly Dictionary<string, object?> _data = new();
        public object? this[string col]
        {
            get => _data.TryGetValue(col, out var v) ? v : null;
            set => _data[col] = value;
        }
        public bool HasColumn(string col) => _data.ContainsKey(col);
        public IEnumerable<KeyValuePair<string, object?>> Pairs => _data;
    }
}