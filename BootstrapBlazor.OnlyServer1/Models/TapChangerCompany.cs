using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models {
    public class TapChangerCompany {

        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        
        public string Name { get; set; } = "";

        public string CreatedBy { get; set; }
        public DateTime CreatedTime { get; set; }

        public bool IsDeleted { get; set; }
    }
}
