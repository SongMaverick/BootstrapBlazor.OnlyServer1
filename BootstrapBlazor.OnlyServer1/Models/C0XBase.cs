using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models {
    public class C0XBase {

        /// <summary>
        /// 表序号
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]//数据库是自增才配自增 
        public int Id { get; set; }

        /// <summary>
        /// 图号（由 MinDrawingNumber + Id 实时计算，不映射数据库列）
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public int DrawNumber { get; set; }

        /// <summary>
        /// 图纸名称
        /// </summary>
        public string DrawName { get; set; }

        /// <summary>
        /// 图纸类型
        /// </summary>
        public string DrawingType { get; set; }

        /// <summary>
        /// 订单号
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string CreatedBy { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public string UpdatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedTime { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDelete { get; set; } = false;
    }
}
