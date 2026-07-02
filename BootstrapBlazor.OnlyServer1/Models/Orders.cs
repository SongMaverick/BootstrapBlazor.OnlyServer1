using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models {
    public class Orders {

        /// <summary>
        /// 序号 自增(Order / ReactorOrder 合并,Id 用新自增值,保证唯一)
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 订单号
        /// </summary>
        public int OrderNumber { get; set; }

        /// <summary>
        /// 容量
        /// </summary>
        public int Power { get; set; }

        /// <summary>
        /// 额定电压
        /// </summary>
        public int RatedVoltage { get; set; }

        /// <summary>
        /// 是否为电抗器
        /// </summary>
        public bool IsReactor { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string CreatedBy { get; set; } = "";

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedTime { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public string UpdatedBy { get; set; } = "";

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; } = false;
    }
}