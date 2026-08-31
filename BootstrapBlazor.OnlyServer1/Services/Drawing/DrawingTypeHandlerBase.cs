using BootstrapBlazor.OnlyServer1.Models;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services.DrawingTypes
{
    /// <summary>
    /// DrawingType handler 抽象基类 — 用泛型消除 9 个 handler 之间的样板代码。
    /// 子类只需声明 DrawingType 和 SeedDrawingNumber 两个属性。
    /// </summary>
    public abstract class DrawingTypeHandlerBase<TEntity> : IDrawingTypeHandler
        where TEntity : C0XBase, new()
    {
        public abstract string DrawingType { get; }

        public Type EntityType => typeof(TEntity);

        /// <summary>
        /// 创建具体类型的实体实例(C0XBase 是 abstract,无法直接 new;
        /// 必须由各 handler 创建自己的具体类型,SqlSugar 才能正确写入对应表)。
        /// </summary>
        public virtual C0XBase CreateNewEntity(
            string drawingName, string drawingType, int orderId,
            string createdBy, DateTime createdTime)
        {
            return new TEntity
            {
                DrawingName = drawingName,
                OrderId = orderId,
                CreatedBy = createdBy,
                UpdatedBy = createdBy,
                CreatedTime = createdTime,
                UpdatedTime = createdTime,
                IsDeleted = false
            };
        }

        // ===== 查询 =====

        public virtual async Task<List<string>> GetExistingDrawingNamesAsync(ISqlSugarClient db, int orderId)
        {
            return await db.Queryable<TEntity>()
                .Where(n => n.OrderId == orderId && !n.IsDeleted)
                .Select(n => n.DrawingName)
                .ToListAsync();
        }

        public virtual async Task<List<C0XBase>> GetAllByOrderIdAsync(ISqlSugarClient db, int orderId)
        {
            var list = await db.Queryable<TEntity>()
                .Where(n => n.OrderId == orderId && !n.IsDeleted)
                .ToListAsync();
            var result = new List<C0XBase>(list.Count);
            result.AddRange(list);
            return result;
        }

        public virtual async Task<List<int>> QueryExistingOrderIdsAsync(ISqlSugarClient db, IReadOnlyCollection<int> orderIds)
        {
            if (orderIds == null || orderIds.Count == 0) return new List<int>();
            return await db.Queryable<TEntity>()
                .Where(n => orderIds.Contains(n.OrderId) && !n.IsDeleted)
                .Select(n => n.OrderId)
                .ToListAsync();
        }

        public virtual async Task<C0XBase?> FindByIdAsync(ISqlSugarClient db, int id)
        {
            // 过滤 IsDeleted=true(占位行)。反推路径遇到占位行时返回 null,避免用户输入跳号图号时错误命中。
            return await db.Queryable<TEntity>()
                .Where(n => n.Id == id && !n.IsDeleted)
                .FirstAsync();
        }

        public virtual async Task<C0XBase?> FindByDrawingNumberAsync(ISqlSugarClient db, int drawingNumber)
        {
            return await db.Queryable<TEntity>()
                .Where(n => n.DrawingNumber == drawingNumber && !n.IsDeleted)
                .FirstAsync();
        }

        // ===== 写入 =====

        public virtual async Task<int> InsertAsync(ISqlSugarClient db, C0XBase entity)
        {
            // 把 C0XBase 转成具体类型 TEntity,SqlSugar 才能写对应表
            if (entity is not TEntity typed)
            {
                throw new InvalidOperationException(
                    $"Entity type mismatch: expected {typeof(TEntity).Name}, got {entity.GetType().Name}");
            }
            return await db.Insertable(typed).ExecuteReturnIdentityAsync();
        }

        public virtual Task<int> UpdateDrawingNameAsync(ISqlSugarClient db, int id, string newDrawingName, string updatedBy, DateTime updatedTime)
        {
            // 过滤 IsDeleted=true:不允许编辑占位行(占位行的 DrawingName 是占位符,编辑会污染真实数据)
            return db.Updateable<TEntity>()
                .SetColumns(it => new TEntity
                {
                    DrawingName = newDrawingName,
                    UpdatedBy = updatedBy,
                    UpdatedTime = updatedTime
                })
                .Where(it => it.Id == id && !it.IsDeleted)
                .ExecuteCommandAsync();
        }
    }
}