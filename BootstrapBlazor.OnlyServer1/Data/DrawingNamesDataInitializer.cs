using BootstrapBlazor.OnlyServer1.Models;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Data;

public class DrawingNamesDataInitializer
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<DrawingNamesDataInitializer> _logger;

    public DrawingNamesDataInitializer(ISqlSugarClient db, ILogger<DrawingNamesDataInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeDrawingNamesAsync()
    {
        // 检查是否已经有数据
        var count = await _db.Queryable<DrawingNames>().CountAsync();
        if (count > 0)
        {
            _logger.LogInformation("DrawingNames already initialized, skipping...");
            return;
        }

        _logger.LogInformation("Initializing DrawingNames...");

        var drawingNames = new List<DrawingNames>
        {
            // ESL-01 - 外部
            new() { Part = "ESL-01", ComponentName = "Am,Bm,Cm互感器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "ESL-01", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "HV-0相互感器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "HV-A,C相互感器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "HV-B相互感器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "LV 互感器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "MV-0互感器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "property plate", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "备品备件列表", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "测量控制回路图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "充氮灭火联管", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "储油柜连管焊装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "储油柜梯子", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "低压零相母线箱引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "低压母线箱", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "低压母线箱引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "低压升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "地基图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "法兰(DN800)", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "风控回路原理图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压A,C相升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压B相升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压零相母线箱", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压零相母线箱引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压零相升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压母线箱", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压母线箱II", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压母线箱引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "高压升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "管道阀门图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "管式走线槽", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "柜脚", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "监测装置", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "胶囊式储油柜", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "绝缘油铭牌", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "冷却器连管", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "连管支撑", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "零相接地铜排", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "铭牌", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "母线箱运输", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "平衡套管接地铜排", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "平面布置图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "器身定位", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "散热器安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "散热器联管", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "散热器支架", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "试验布置", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "梯子", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "铁芯接地铜排", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "涂漆技术条件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "外部安装", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "外形尺寸图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "先期运输尺寸图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "箱盖磁屏蔽", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "油温-油位曲线铭牌", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "油箱磁屏蔽", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "运输尺寸图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "中压零相母线箱", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "中压零相母线箱引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "中压零相升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "中压母线箱", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "中压母线箱引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "中压升高座", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-01", ComponentName = "最终外形图", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            // ESL-02 - 内部
            new() { Part = "ESL-02", ComponentName = "器身角环", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "器身绝缘", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "ESL-02", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "低压线圈", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "调压线圈", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "高压线圈", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "平衡线圈", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "中压线圈", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "低压连接件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "低压引线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "调压引线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "调压引线连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "高压和调压引线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "高压引线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "夹件附件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "连接件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "零相连接件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "屏蔽管内部连接", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "屏蔽线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "套管尾部连接件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-02", ComponentName = "中低压引线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            // ESL-03 - 串联电抗器
            new() { Part = "ESL-03", ComponentName = "电抗器装配", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-03", ComponentName = "电抗器线圈", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-03", ComponentName = "电抗器附件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-03", ComponentName = "电抗器引线", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            // ESL-Core - 铁芯
            new() { Part = "ESL-Core", ComponentName = "压板", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "铁芯装配", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "矽钢片", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "铁芯胶皮", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "下夹件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "上夹件", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "大上梁", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "小上梁", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "芯柱吊板", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "边柱吊板", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "下铁轭下拉带", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "下铁轭上拉带", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "上铁轭下拉带", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "上铁轭上拉带", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "下侧梁", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "底脚", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "芯柱绑扎", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "边柱绑扎", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "压板", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "下铁轭护板（芯柱）", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "下铁轭护板（边柱）", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false },
            new() { Part = "ESL-Core", ComponentName = "开关侧梁", CreatedBy = "宋程", CreatedTime = DateTime.Now, UpdatedBy = "宋程", UpdatedTime = DateTime.Now, Isdelete = false }
        };

        await _db.Insertable(drawingNames).ExecuteCommandAsync();
        _logger.LogInformation($"Successfully initialized {drawingNames.Count} DrawingNames");
    }
}