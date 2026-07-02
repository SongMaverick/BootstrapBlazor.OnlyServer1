namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 单次批量添加图号的结果。
    /// Success 列出成功入库的图名,Failures 列出每条失败原因。
    /// </summary>
    public class DrawingNumberInsertResult
    {
        public List<string> Success { get; } = new();
        public List<DrawingNumberFailure> Failures { get; } = new();
        public bool AllSucceeded => Failures.Count == 0;
        public bool NoneSucceeded => Success.Count == 0;
    }

    public record DrawingNumberFailure(string Name, string Reason);
}
