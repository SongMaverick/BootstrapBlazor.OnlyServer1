using BootstrapBlazor.OnlyServer1.Models;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.DrawingTypes
{
    /// <summary>
    /// 每种 drawingType 的具体行为(查/改/插)。C01..C09 各实现一次。
    /// Service 层通过 IDrawingTypeRegistry.Resolve(type)?.MethodAsync(...) 调用。
    /// </summary>
    public interface IDrawingTypeHandler
    {
        /// <summary>"C01" / "C02" / ... / "C09"</summary>
        string DrawingType { get; }

        /// <summary>对应的实体类型(typeof(C01) 等)。SqlSugar Code First / AOP 用</summary>
        Type EntityType { get; }

        /// <summary>
        /// 由 handler 创建具体类型的实体实例。
        /// C0XBase 是 abstract,无法直接 new;handler 必须 new 自己的具体类型(C01/C02/...),
        /// SqlSugar 多态写入才能正确路由到对应表。
        /// </summary>
        C0XBase CreateNewEntity(
            string drawingName, string drawingType, int orderId,
            string createdBy, DateTime createdTime);

        // ===== 查询 =====

        /// <summary>查询该订单当前所有未删除的 DrawingName
        /// (2026-07-08 Review #L-7) DrawName → DrawingName</summary>
        Task<List<string>> GetExistingDrawingNamesAsync(ISqlSugarClient db, int orderId);

        /// <summary>查询该订单当前所有未删除的 C0XBase 完整记录</summary>
        Task<List<C0XBase>> GetAllByOrderIdAsync(ISqlSugarClient db, int orderId);

        /// <summary>查询指定 OrderId 列表里有图号的 OrderId(用于批量预热缓存)</summary>
        Task<List<int>> QueryExistingOrderIdsAsync(ISqlSugarClient db, IReadOnlyCollection<int> orderIds);

        /// <summary>按 Id 查一条 C0XBase</summary>
        Task<C0XBase?> FindByIdAsync(ISqlSugarClient db, int id);

        /// <summary>按 DrawingNumber 查一条 C0XBase(图号查询页用)
        /// (2026-07-08 Review #L-7) DrawNumber → DrawingNumber</summary>
        Task<C0XBase?> FindByDrawingNumberAsync(ISqlSugarClient db, int drawingNumber);

        // ===== 写入 =====

        /// <summary>插入一条新图号,返回新 Id(调用方需提前分配 DrawingNumber 并填入实体)</summary>
        Task<int> InsertAsync(ISqlSugarClient db, C0XBase entity);

        /// <summary>更新图号名称 + UpdatedBy + UpdatedTime
        /// (2026-07-08 Review #L-7) DrawName → DrawingName</summary>
        Task<int> UpdateDrawingNameAsync(ISqlSugarClient db, int id, string newDrawingName, string updatedBy, DateTime updatedTime);
    }
}