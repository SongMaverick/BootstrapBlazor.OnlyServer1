using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Services.DrawingTypes
{
    /// <summary>
    /// DrawingType registry:解析 drawingType 字符串 → 对应 IDrawingTypeHandler。
    /// 同时集中 Orders → DrawingType 的映射逻辑。
    /// </summary>
    public interface IDrawingTypeRegistry
    {
        IReadOnlyList<string> AllTypes { get; }
        IDrawingTypeHandler? Resolve(string drawingType);

        /// <summary>根据订单属性推算对应的 drawingType(电抗器/C02~C07)。null 表示无法识别</summary>
        string? GetDrawingType(Orders order);

        /// <summary>取得该 drawingType 的图号种子值(分配器首次初始化时使用)</summary>
        int GetSeedDrawingNumber(string drawingType);
    }

    public class DrawingTypeRegistry : IDrawingTypeRegistry
    {
        private readonly Dictionary<string, IDrawingTypeHandler> _handlers;

        public DrawingTypeRegistry()
        {
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
            if (order.IsReactor) return "C08";
            if (order.Power > 2 && order.Power <= 10) return "C02";
            if (order.Power > 10 && order.Power < 40) return "C03";
            if (order.Power >= 40 && order.Power < 100) return "C04";
            if (order.Power >= 100 && order.Power < 200) return "C05";
            if (order.Power >= 200 && order.Power < 400) return "C06";
            if (order.Power >= 400) return "C07";
            return null;
        }

        public int GetSeedDrawingNumber(string drawingType)
        {
            var handler = Resolve(drawingType);
            return handler?.SeedDrawingNumber ?? 0;
        }
    }
}