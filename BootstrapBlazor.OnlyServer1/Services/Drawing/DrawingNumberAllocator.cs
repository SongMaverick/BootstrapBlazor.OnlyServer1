using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 图号分配器:为指定 drawingType 原子地分配下一个可用的 DrawingNumber。
    ///
    /// 实现:SQL Server 单条 UPDATE...OUTPUT 语句,数据库侧原子自增,无应用层锁、无显式事务。
    /// 第一次调用某 type 时按 handler 的 SeedDrawingNumber 兜底初始化。
    ///
    /// (2026-07-08 Review #L-7) 注释 DrawNumber → DrawingNumber,实际代码无 DrawNumber 引用。
    /// </summary>
    public interface IDrawingNumberAllocator
    {
        /// <summary>分配一个 DrawingNumber 并返回</summary>
        Task<int> AllocateAsync(string drawingType, CancellationToken ct = default);
    }

    public class DrawingNumberAllocator : IDrawingNumberAllocator
    {
        private readonly ISqlSugarClient _db;
        private readonly IDrawingTypeRegistry _registry;
        private readonly CurrentUserAccessor _currentUser;
        private readonly ILogger<DrawingNumberAllocator> _logger;

        public DrawingNumberAllocator(
            ISqlSugarClient db,
            IDrawingTypeRegistry registry,
            CurrentUserAccessor currentUser,
            ILogger<DrawingNumberAllocator> logger)
        {
            _db = db;
            _registry = registry;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<int> AllocateAsync(string drawingType, CancellationToken ct = default)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // 图号分配是一个"修改关键资源"的操作:仅 Admin/Operator。
            // Razor 层调用链是 C0XWriteService.AddDrawingNumbersAsync → 这里,
            // 因此正常路径是已经被 Razor 层校验过的;
            // 但若有任何任务 / 测试 / 未来新入口直接调,Service 自己挡。
            // 不删这段校验、不删这个注释。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
            if (!ok)
            {
                _logger.LogWarning(
                    "DrawingNumberAllocator.AllocateAsync denied: callerId={CallerId} drawingType={Type} reason={Reason}",
                    _currentUser.GetUserId(), drawingType, err);
                throw new UnauthorizedAccessException(err);
            }

            if (string.IsNullOrEmpty(drawingType))
                throw new ArgumentException("drawingType 不能为空", nameof(drawingType));

            // 1) 原子自增 + 读取
            var n = await _db.Ado.GetIntAsync(
                "UPDATE DrawingNumberSequence SET CurrentMax = CurrentMax + 1 " +
                "OUTPUT inserted.CurrentMax WHERE DrawingType = @type",
                new SugarParameter("@type", drawingType));

            if (n > 0) return n;

            // 2) 该 type 还没初始化 — 按 SeedDrawingNumber 兜底
            var seed = _registry.GetSeedDrawingNumber(drawingType);
            var newRow = new DrawingNumberSequence
            {
                DrawingType = drawingType,
                CurrentMax = seed + 1
            };

            // 并发兜底:可能两个调用同时走到这里,只会有一个 INSERT 成功,另一个触发主键冲突
            try
            {
                await _db.Insertable(newRow).ExecuteCommandAsync(ct);
                return seed + 1;
            }
            catch (Exception ex)
            {
                // (2026-08-06 P0 优化)Debug 默认关着时,LogDebug(ex,...) 仍会付一次
                // args[] 装箱 / ex 引用复制。生产无意义,加 IsEnabled 守门零成本。
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(ex, "Concurrent init for {Type}, retry increment", drawingType);
                // 重试一次原子 +1(此时行已存在)
                var retried = await _db.Ado.GetIntAsync(
                    "UPDATE DrawingNumberSequence SET CurrentMax = CurrentMax + 1 " +
                    "OUTPUT inserted.CurrentMax WHERE DrawingType = @type",
                    new SugarParameter("@type", drawingType));
                if (retried <= 0)
                    throw new InvalidOperationException(
                        $"图号分配失败:drawingType={drawingType} 初始化后再次分配仍失败", ex);
                return retried;
            }
        }
    }
}