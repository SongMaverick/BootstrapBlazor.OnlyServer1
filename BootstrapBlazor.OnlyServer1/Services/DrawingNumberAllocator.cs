using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 图号分配器:为指定 drawingType 原子地分配下一个可用的 DrawNumber。
    ///
    /// 实现:SQL Server 单条 UPDATE...OUTPUT 语句,数据库侧原子自增,无应用层锁、无显式事务。
    /// 第一次调用某 type 时按 handler 的 SeedDrawingNumber 兜底初始化。
    /// </summary>
    public interface IDrawingNumberAllocator
    {
        /// <summary>分配一个 DrawNumber 并返回</summary>
        Task<int> AllocateAsync(string drawingType, CancellationToken ct = default);
    }

    public class DrawingNumberAllocator : IDrawingNumberAllocator
    {
        private readonly ISqlSugarClient _db;
        private readonly IDrawingTypeRegistry _registry;
        private readonly ILogger<DrawingNumberAllocator> _logger;

        public DrawingNumberAllocator(
            ISqlSugarClient db,
            IDrawingTypeRegistry registry,
            ILogger<DrawingNumberAllocator> logger)
        {
            _db = db;
            _registry = registry;
            _logger = logger;
        }

        public async Task<int> AllocateAsync(string drawingType, CancellationToken ct = default)
        {
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