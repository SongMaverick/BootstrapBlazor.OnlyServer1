using System.Text;

namespace BootstrapBlazor.DbMigrator.Sql
{
    /// <summary>
    /// 自动检测 SQL 文件编码(优先 UTF-8,失败 fallback GB18030 兼容 GBK/GB2312)。
    /// 解决老数据库导出的 SQL 文件中文乱码问题。
    /// </summary>
    public static class EncodingHelper
    {
        /// <summary>读取整个文件,自动选编码</summary>
        public static string ReadAllText(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            var encoding = DetectEncoding(bytes, filePath);
            return encoding.GetString(bytes);
        }

        /// <summary>检测编码:UTF-8 BOM → 合法 UTF-8 → GB18030</summary>
        public static Encoding DetectEncoding(byte[] bytes, string? hint = null)
        {
            // 1. UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8;

            // 2. UTF-16 BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            // 3. 尝试 UTF-8(严格检查)
            if (IsValidUtf8(bytes))
                return Encoding.UTF8;

            // 4. 默认 GB18030(兼容 GBK、GB2312,中文环境老导出最常用)
            return Encoding.GetEncoding("GB18030");
        }

        /// <summary>
        /// 验证 byte 序列是否为合法 UTF-8。
        /// 严格检查:UTF-8 的多字节序列必须完整且合法,不能出现半字符。
        /// </summary>
        private static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                int expectedLen;
                if (b < 0x80) { i++; continue; }
                else if ((b & 0xE0) == 0xC0) expectedLen = 2;
                else if ((b & 0xF0) == 0xE0) expectedLen = 3;
                else if ((b & 0xF8) == 0xF0) expectedLen = 4;
                else return false;

                if (i + expectedLen > bytes.Length) return false;
                for (int j = 1; j < expectedLen; j++)
                    if ((bytes[i + j] & 0xC0) != 0x80) return false;
                i += expectedLen;
            }
            return true;
        }
    }
}