using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models
{
    /// <summary>
    /// C01-C09 图号的公共基类。
    /// abstract:SqlSugar Code First 不会为 abstract 类建表,具体子类各建一张。
    /// 所有 string 字段均有默认值 = "",保证 Code First 生成 NOT NULL 列时 INSERT 不会因 NULL 报错。
    /// </summary>
    public abstract class C0XBase
    {
        /// <summary>
        /// 物理主键 — INT IDENTITY 自增,仅用于数据库内部关联,不参与任何业务计算。
        /// 业务图号由 <see cref="DrawNumber"/> 持久化列承担。
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 业务图号(例如 17001),由 <see cref="Services.DrawingNumberAllocator"/> 在插入前原子分配。
        /// 每张 C0X 表加 UNIQUE 索引,保证全局唯一。
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public int DrawNumber { get; set; }

        /// <summary>图纸名称</summary>
        public string DrawName { get; set; } = "";

        /// <summary>订单号</summary>
        public int OrderId { get; set; }

        /// <summary>创建人</summary>
        public string CreatedBy { get; set; } = "";

        /// <summary>创建时间</summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>更新人</summary>
        public string UpdatedBy { get; set; } = "";

        /// <summary>更新时间</summary>
        public DateTime UpdatedTime { get; set; }

        /// <summary>是否删除</summary>
        public bool IsDelete { get; set; } = false;
    }
}