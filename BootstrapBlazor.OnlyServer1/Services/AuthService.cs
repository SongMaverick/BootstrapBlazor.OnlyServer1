using SqlSugar;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Helpers;
using Microsoft.Extensions.Logging;

namespace BootstrapBlazor.OnlyServer1.Services
{
    public class AuthService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<AuthService> _logger;
        
        public Models.Users? CurrentUser { get; private set; }
        
        public bool IsLoggedIn => CurrentUser != null;

        public AuthService(ISqlSugarClient db, ILogger<AuthService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public static string GetUID()
        {
            return System.Environment.UserName;
        }

        public async Task<bool> Login(string username, string password)
        {
            try
            {
                var encryptedPassword = MD5Helper.Encrypt(password);
                
                _logger.LogInformation("尝试登录用户: {Username}, 加密密码: {Password}", username, encryptedPassword);
                
                var user = await _db.Queryable<Models.Users>()
                    .Where(u => u.Name == username && u.Password == encryptedPassword && !u.IsDelete)
                    .FirstAsync();
                
                if (user != null)
                {
                    _logger.LogInformation("登录成功: {Username}", username);
                    CurrentUser = user;
                    return true;
                }
                
                _logger.LogWarning("登录失败，未找到用户或密码错误");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录时发生错误");
                return false;
            }
        }

        public async Task<bool> AutoLoginByGID()
        {
            try
            {
                var gid = GetUID().ToUpper();
                _logger.LogInformation("尝试自动登录，GID: {GID}", gid);
                
                // 先查询所有匹配的用户（包括已删除的）用于调试
                var allUsers = await _db.Queryable<Models.Users>()
                    .Where(u => u.GID == gid)
                    .ToListAsync();
                
                _logger.LogInformation("数据库中匹配GID的用户数: {Count}", allUsers.Count);
                foreach (var u in allUsers)
                {
                    _logger.LogInformation("用户: {Name}, GID: {GID}, IsDelete: {IsDelete}", u.Name, u.GID, u.IsDelete);
                }
                
                // 查询未删除的用户
                var user = await _db.Queryable<Models.Users>()
                    .Where(u => u.GID == gid && !u.IsDelete)
                    .FirstAsync();
                
                if (user != null)
                {
                    _logger.LogInformation("自动登录成功，用户: {Name}", user.Name);
                    CurrentUser = user;
                    return true;
                }
                
                _logger.LogWarning("自动登录失败，未找到未删除的匹配用户");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动登录时发生错误");
                return false;
            }
        }

        public async Task<List<Models.Users>> GetAllUsersForDebug()
        {
            try
            {
                var users = await _db.Queryable<Models.Users>()
                    .OrderBy(u => u.Id)
                    .ToListAsync();
                return users ?? new List<Models.Users>();
            }
            catch
            {
                return new List<Models.Users>();
            }
        }

        public void Logout()
        {
            _logger.LogInformation("用户登出");
            CurrentUser = null;
        }
    }
}
