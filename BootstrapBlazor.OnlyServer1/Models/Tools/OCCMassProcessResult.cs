namespace BootstrapBlazor.OnlyServer1.Models.Tools;

public sealed record OCCMassProcessResult(
    byte[] Content,
    string FileName,
    string ContentType,
    string Version,
    int UpdatedRowCount);
