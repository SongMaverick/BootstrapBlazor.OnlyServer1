using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Common;

/// <summary>
/// NLog 数据库 target 输出表。
///
/// 表结构严格对齐 NLog.config 中 dbTarget 的 INSERT 语句:
///   INSERT INTO dbo.Logs (MachineName, Logged, Level, Message, Logger, Properties, Exception)
///   VALUES (@MachineName, @Logged, @Level, @Message, @Logger, @Properties, @Exception);
///
/// DbMigrator 必须创建此表,否则 Web 程序启动后 NLog 第一条日志 INSERT 会失败。
///
/// 字段说明:
///   - Id         : 自增主键(日志写入量大,避免手动赋值)
///   - MachineName: 客户端机器名(NLog ${machinename})
///   - Logged     : 日志时间(NLog ${date}, DateTime)
///   - Level      : 日志级别(NLog ${level})
///   - Message    : 日志消息主体
///   - Logger     : 发出日志的 logger 名称(通常是类名)
///   - Properties : 自定义事件属性(NLog ${all-event-properties})
///   - Exception  : 异常堆栈(NLog ${exception:tostring})
/// </summary>
[SugarTable("Logs")]
public class Logs
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(IsNullable = true, Length = 255)]
    public string? MachineName { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime Logged { get; set; }

    [SugarColumn(IsNullable = false, Length = 50)]
    public string Level { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = -1)]
    public string? Message { get; set; }

    [SugarColumn(IsNullable = true, Length = 255)]
    public string? Logger { get; set; }

    [SugarColumn(IsNullable = true, Length = -1)]
    public string? Properties { get; set; }

    [SugarColumn(IsNullable = true, Length = -1)]
    public string? Exception { get; set; }
}