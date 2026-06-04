using System.Security.Cryptography;
using System.Text;

namespace BootstrapBlazor.OnlyServer1.Helpers
{
    public class MD5Helper
    {
        public static string Encrypt(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            
            return sb.ToString();
        }
    }
}
