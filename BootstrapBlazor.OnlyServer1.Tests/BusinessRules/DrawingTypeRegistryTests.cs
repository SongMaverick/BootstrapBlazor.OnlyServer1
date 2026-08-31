using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Models.BusinessRules;
using BootstrapBlazor.OnlyServer1.Services.Drawing;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Tests.BusinessRules;

/// <summary>
/// DrawingTypeRegistry.GetDrawingType 的边界用例。
/// 这套配置在 appsettings.json 的 "BusinessRules.DrawingType.PowerRanges" 节,
/// 改配置后这些测试应继续全绿,作为业务规则的回归保护。
/// </summary>
public class DrawingTypeRegistryTests
{
    private static DrawingTypeOptions BuildDefaultOptions() => new()
    {
        ReactorType = "C08",
        PowerRanges = new()
        {
            new() { Min = 0,   Max = 2,    Type = null },
            new() { Min = 3,   Max = 10,   Type = "C02" },
            new() { Min = 11,  Max = 39,   Type = "C03" },
            new() { Min = 40,  Max = 99,   Type = "C04" },
            new() { Min = 100, Max = 199,  Type = "C05" },
            new() { Min = 200, Max = 399,  Type = "C06" },
            new() { Min = 400,                Type = "C07" }
        },
        Seeds = new()
        {
            ["C01"] = 0, ["C02"] = 0, ["C03"] = 0,
            ["C04"] = 17000, ["C05"] = 34000, ["C06"] = 22893,
            ["C07"] = 2992, ["C08"] = 0, ["C09"] = 0
        }
    };

    private static DrawingTypeRegistry BuildRegistry(DrawingTypeOptions? opts = null)
    {
        var options = Options.Create(opts ?? BuildDefaultOptions());
        return new DrawingTypeRegistry(options);
    }

    private static Orders MakeOrder(int power, bool isReactor = false) => new()
    {
        OrderNumber = 1,
        Power = power,
        RatedVoltage = 10,
        IsReactor = isReactor,
        CreatedBy = "test",
        UpdatedBy = "test",
        CreatedTime = DateTime.Now,
        UpdatedTime = DateTime.Now
    };

    // ===== Reactor 路径 =====

    [Fact]
    public void GetDrawingType_IsReactor_ReturnsReactorType_RegardlessOfPower()
    {
        var reg = BuildRegistry();
        Assert.Equal("C08", reg.GetDrawingType(MakeOrder(power: 0, isReactor: true)));
        Assert.Equal("C08", reg.GetDrawingType(MakeOrder(power: 500, isReactor: true)));
    }

    [Fact]
    public void GetDrawingType_ReactorType_Configurable()
    {
        var opts = BuildDefaultOptions();
        opts.ReactorType = "C09";
        var reg = BuildRegistry(opts);
        Assert.Equal("C09", reg.GetDrawingType(MakeOrder(0, isReactor: true)));
    }

    // ===== Power 区间边界 =====

    [Theory]
    [InlineData(0, null)]       // < Min of any range → null
    [InlineData(1, null)]
    [InlineData(2, null)]       // boundary: exactly at Max of [0,2]
    [InlineData(3, "C02")]      // boundary: Min of [3,10]
    [InlineData(7, "C02")]      // mid
    [InlineData(10, "C02")]     // boundary: Max of [3,10]
    [InlineData(11, "C03")]     // boundary: Min of [11,39]
    [InlineData(20, "C03")]     // mid
    [InlineData(39, "C03")]     // boundary: Max of [11,39]
    [InlineData(40, "C04")]     // boundary: Min of [40,99]
    [InlineData(80, "C04")]     // mid
    [InlineData(99, "C04")]     // boundary: Max of [40,99]
    [InlineData(100, "C05")]    // boundary: Min of [100,199]
    [InlineData(150, "C05")]    // mid
    [InlineData(199, "C05")]    // boundary: Max of [100,199]
    [InlineData(200, "C06")]    // boundary: Min of [200,399]
    [InlineData(300, "C06")]    // mid
    [InlineData(399, "C06")]    // boundary: Max of [200,399]
    [InlineData(400, "C07")]    // boundary: Min of [400,+∞)
    [InlineData(500, "C07")]    // mid
    [InlineData(99999, "C07")]  // very high
    public void GetDrawingType_PowerBoundaries(int power, string? expectedType)
    {
        var reg = BuildRegistry();
        var order = MakeOrder(power, isReactor: false);
        Assert.Equal(expectedType, reg.GetDrawingType(order));
    }

    // ===== Seeds =====

    [Theory]
    [InlineData("C01", 0)]
    [InlineData("C04", 17000)]
    [InlineData("C05", 34000)]
    [InlineData("C06", 22893)]
    [InlineData("C07", 2992)]
    [InlineData("C99", 0)]    // 未配置的 drawingType 兜底 0
    [InlineData("", 0)]       // 空串兜底 0
    public void GetSeedDrawingNumber_ReturnsConfiguredOrZero(string drawingType, int expected)
    {
        var reg = BuildRegistry();
        Assert.Equal(expected, reg.GetSeedDrawingNumber(drawingType));
    }

    [Fact]
    public void GetSeedDrawingNumber_SeedsAreConfigurable()
    {
        var opts = BuildDefaultOptions();
        opts.Seeds["C04"] = 99999;
        var reg = BuildRegistry(opts);
        Assert.Equal(99999, reg.GetSeedDrawingNumber("C04"));
    }

    // ===== AllTypes =====

    [Fact]
    public void AllTypes_ContainsAllNineHandlers()
    {
        var reg = BuildRegistry();
        Assert.Equal(9, reg.AllTypes.Count);
        Assert.Contains("C01", reg.AllTypes);
        Assert.Contains("C09", reg.AllTypes);
    }

    // ===== Resolve =====

    [Fact]
    public void Resolve_KnownType_ReturnsHandler()
    {
        var reg = BuildRegistry();
        Assert.NotNull(reg.Resolve("C04"));
        Assert.Equal("C04", reg.Resolve("C04")!.DrawingType);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Z99")]
    public void Resolve_UnknownOrEmpty_ReturnsNull(string? drawingType)
    {
        var reg = BuildRegistry();
        Assert.Null(reg.Resolve(drawingType));
    }

    // ===== 规则表变更的回归保护 =====

    [Fact]
    public void GetDrawingType_RuleReplaced_ReturnsNewType()
    {
        // 模拟运维改 PowerRanges:把 C02 区段替换成 C02A/C02B
        var opts = BuildDefaultOptions();
        opts.PowerRanges = new()
        {
            new() { Min = 0,   Max = 2,    Type = null },
            new() { Min = 3,   Max = 5,    Type = "C02A" },
            new() { Min = 6,   Max = 10,   Type = "C02B" },
            new() { Min = 11,  Max = 39,   Type = "C03" }
            // 其余同默认
        };
        var reg = BuildRegistry(opts);

        Assert.Equal("C02A", reg.GetDrawingType(MakeOrder(3)));
        Assert.Equal("C02A", reg.GetDrawingType(MakeOrder(5)));
        Assert.Equal("C02B", reg.GetDrawingType(MakeOrder(6)));
        Assert.Equal("C02B", reg.GetDrawingType(MakeOrder(10)));
    }
}
