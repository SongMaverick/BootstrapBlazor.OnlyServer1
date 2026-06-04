using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Helpers;

namespace BootstrapBlazor.OnlyServer1.Data
{
    public class UserDataImport
    {
        private readonly ILogger<UserDataImport> _logger;

        public UserDataImport(ILogger<UserDataImport> logger)
        {
            _logger = logger;
        }

        public async Task ImportUsersFromCode(ISqlSugarClient db)
        {
            _logger.LogInformation("开始导入用户数据...");

            try
            {
                var users = GetUsersList();

                _logger.LogInformation("准备导入 {Count} 条用户数据", users.Count);

                var importedCount = 0;
                var skippedCount = 0;

                foreach (var user in users)
                {
                    var existingUser = await db.Queryable<Models.Users>().Where(u => u.WorkerNumber == user.WorkerNumber).FirstAsync();
                    
                    if (existingUser != null)
                    {
                        _logger.LogInformation("跳过已存在的用户：{Name} (工号：{WorkerNumber})", user.Name, user.WorkerNumber);
                        skippedCount++;
                        continue;
                    }

                    user.CreateTime = DateTime.Now;
                    await db.Insertable(user).ExecuteCommandAsync();
                    _logger.LogInformation("成功导入用户：{Name} (工号：{WorkerNumber})", user.Name, user.WorkerNumber);
                    importedCount++;
                }

                _logger.LogInformation("数据导入完成！成功导入：{ImportedCount} 条，跳过：{SkippedCount} 条", importedCount, skippedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入用户数据时发生错误：{Message}", ex.Message);
            }
        }

        private List<Models.Users> GetUsersList()
        {
            var password = MD5Helper.Encrypt("123456");

            return new List<Models.Users>
            {
                new() { Name = "王志刚", Password = password, WorkerNumber = 87100074, GID = "Z000JAZV", IsDelete = false },
                new() { Name = "刘红", Password = password, WorkerNumber = 87100131, GID = "Z000JARJ", IsDelete = false },
                new() { Name = "李咏梅", Password = password, WorkerNumber = 87100132, GID = "Z000JARZ", IsDelete = false },
                new() { Name = "司林杰", Password = password, WorkerNumber = 87100136, GID = "Z001TSJW", IsDelete = false },
                new() { Name = "赵元海", Password = password, WorkerNumber = 87100154, GID = "Z000JAZZ", IsDelete = false },
                new() { Name = "李红杰", Password = password, WorkerNumber = 87100175, GID = "Z000JARW", IsDelete = false },
                new() { Name = "孙红玲", Password = password, WorkerNumber = 87100247, GID = "Z000JB01", IsDelete = false },
                new() { Name = "张金环", Password = password, WorkerNumber = 87100250, GID = "Z000JB0J", IsDelete = false },
                new() { Name = "朱明清", Password = password, WorkerNumber = 87100309, GID = "Z002C9TM", IsDelete = false },
                new() { Name = "惠树武", Password = password, WorkerNumber = 87100313, GID = "Z00169MH", IsDelete = false },
                new() { Name = "梁乐", Password = password, WorkerNumber = 87100330, GID = "Z002C9JC", IsDelete = false },
                new() { Name = "孙强", Password = password, WorkerNumber = 87100336, GID = "Z002CBXT", IsDelete = false },
                new() { Name = "郭运波", Password = password, WorkerNumber = 87100469, GID = "Z002H84X", IsDelete = false },
                new() { Name = "高伟廷", Password = password, WorkerNumber = 87100489, GID = "Z000JASC", IsDelete = false },
                new() { Name = "邢晓静", Password = password, WorkerNumber = 87100501, GID = "Z002N90E", IsDelete = false },
                new() { Name = "王佳玮", Password = password, WorkerNumber = 87100502, GID = "Z002N90V", IsDelete = false },
                new() { Name = "毕海宁", Password = password, WorkerNumber = 87100504, GID = "Z002N91V", IsDelete = false },
                new() { Name = "陈振东", Password = password, WorkerNumber = 87100555, GID = "Z000JASR", IsDelete = false },
                new() { Name = "郑瑞阳", Password = password, WorkerNumber = 87100575, GID = "Z003D33B", IsDelete = false },
                new() { Name = "周剑峰", Password = password, WorkerNumber = 87100712, GID = "Z0047MCY", IsDelete = false },
                new() { Name = "宋程", Password = password, WorkerNumber = 87100721, GID = "Z0048BYZ", IsDelete = false },
                new() { Name = "戚彬", Password = password, WorkerNumber = 87100807, GID = "Z004JUNN", IsDelete = false },
                new() { Name = "胡冬梅", Password = password, WorkerNumber = 87100810, GID = "Z004K9UT", IsDelete = false },
                new() { Name = "崔明月", Password = password, WorkerNumber = 87100811, GID = "Z004KNKD", IsDelete = false },
                new() { Name = "李士升", Password = password, WorkerNumber = 87100817, GID = "Z004M98Z", IsDelete = false },
                new() { Name = "林海琳", Password = password, WorkerNumber = 87100835, GID = "Z004PJSF", IsDelete = false },
                new() { Name = "王芳", Password = password, WorkerNumber = 87100174, GID = "Z000JAYV", IsDelete = false },
                new() { Name = "彭臣", Password = password, WorkerNumber = 87100925, GID = "Z004Z40F", IsDelete = false },
                new() { Name = "段丽媛", Password = password, WorkerNumber = 87100935, GID = "Z004ZHJP", IsDelete = false },
                new() { Name = "张洪雨", Password = password, WorkerNumber = 87100929, GID = "Z004ZDVK", IsDelete = false },
                new() { Name = "房超", Password = password, WorkerNumber = 87100928, GID = "Z004ZPJF", IsDelete = false },
                new() { Name = "董立乔", Password = password, WorkerNumber = 87101063, GID = "Z00531FM", IsDelete = false },
                new() { Name = "张琦", Password = password, WorkerNumber = 87101110, GID = "Z00549SJ", IsDelete = false },
                new() { Name = "刘浩", Password = password, WorkerNumber = 87101128, GID = "Z00551YT", IsDelete = false },
                new() { Name = "张荣岩", Password = password, WorkerNumber = 87101127, GID = "Z00551YS", IsDelete = false },
                new() { Name = "刘佳琛", Password = password, WorkerNumber = 87101151, GID = "Z0055YNY", IsDelete = false }
            };
        }
    }
}
