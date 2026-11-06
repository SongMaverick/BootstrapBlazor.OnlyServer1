namespace BootstrapBlazor.OnlyServer1.Models.Tools;

public sealed class OCCMassWeightItem
{
    public required int SourceRow { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public double Value { get; set; }
}
