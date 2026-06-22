using BootstrapBlazor.OnlyServer1.Models;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Data;

/// <summary>
/// DrawingNames 表的初始化器。
///
/// 当前状态: 空壳 — 旧的硬编码 seed 列表(ESL-01/02/03/Core 下的图名)已移除。
/// 由调用方根据业务需要重新设计初始化逻辑(例如:从 Excel 导入、从 CSV 读、调用别的 Service 等)。
///
/// 注意事项:
///   - 库里现有的 DrawingNames 数据**不会被这个方法自动清理**(只 insert,不 delete/truncate)
///   - 调用方需自行保证"不要重复插入"(例如先 COUNT > 0 跳过 / 用唯一索引去重)
///   - DbContext 由 DI 注入,不要在内部 new SqlSugarClient
/// </summary>
public class DrawingNamesDataInitializer
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<DrawingNamesDataInitializer> _logger;

    public DrawingNamesDataInitializer(ISqlSugarClient db, ILogger<DrawingNamesDataInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 初始化 DrawingNames 表数据。
    /// TODO: 根据新的初始化方案填充此方法。
    /// </summary>
    public async Task InitializeDrawingNamesAsync()
    {
        // TODO: 在此实现新的初始化逻辑
        _logger.LogWarning("DrawingNamesDataInitializer.InitializeDrawingNamesAsync 尚未实现,请补充初始化方案");
        await Task.CompletedTask;
    }
}
