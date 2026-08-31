using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Models.BusinessRules;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Services.Drawing
{
    /// <summary>
    /// DrawingType registry:解析 drawingType 字符串 → 对应 IDrawingTypeHandler。
    /// 业务规则(Power 区间 → drawingType、Reactor 类型、Seed 值)集中从
    /// <see cref="DrawingTypeOptions"/> 读取,放于 appsettings.json。
    /// 添加新规则(例如细化区间)无需改代码。
    /// </summary>
    public interface IDrawingTypeRegistry
    {
        IReadOnlyList<string> AllTypes { get; }
        IDrawingTypeHandler? Resolve(string drawingType);

        /// <summary>根据订单属性推算对应的 drawingType(电抗器 / Power 区间)。null 表示无法识别。</summary>
        string? GetDrawingType(Orders order);

        /// <summary>取得该 drawingType 的图号种子值(分配器首次初始化时使用)。缺省 0。</summary>
        int GetSeedDrawingNumber(string drawingType);
    }

    public class DrawingTypeRegistry : IDrawingTypeRegistry
    {
        private readonly Dictionary<string, IDrawingTypeHandler> _handlers;
        private readonly DrawingTypeOptions _options;

        public DrawingTypeRegistry(IOptions<DrawingTypeOptions> options)
        {
            _options = options.Value;
            _handlers = new IDrawingTypeHandler[]
            {
                new C01Handler(),
                new C02Handler(),
                new C03Handler(),
                new C04Handler(),
                new C05Handler(),
                new C06Handler(),
                new C07Handler(),
                new C08Handler(),
                new C09Handler(),
            }.ToDictionary(h => h.DrawingType);
        }

        public IReadOnlyList<string> AllTypes => _handlers.Keys.ToList();

        public IDrawingTypeHandler? Resolve(string drawingType)
        {
            if (string.IsNullOrEmpty(drawingType)) return null;
            _handlers.TryGetValue(drawingType, out var handler);
            return handler;
        }

        public string? GetDrawingType(Orders order)
        {
            // 电抗器订单:固定映射(配置项)
            if (order.IsReactor)
            {
                return _options.ReactorType;
            }

            // Power 区间映射:遍历 PowerRanges,找第一条 Min ≤ Power 且 (Max 为 null 或 Power ≤ Max) 的规则
            foreach (var range in _options.PowerRanges)
            {
                if (range.Min > order.Power) continue;
                if (range.Max.HasValue && order.Power > range.Max.Value) continue;
                return range.Type;
            }

            return null;
        }

        public int GetSeedDrawingNumber(string drawingType)
        {
            if (_options.Seeds.TryGetValue(drawingType, out var seed))
            {
                return seed;
            }
            return 0;
        }
    }
}
