using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models
{
    public class DrawingNumber
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        public int C0xId { get; set; }

        public int DrawNumber { get; set; }

        public string DrawName { get; set; }

        public string DrawingType { get; set; }

        public int OrderId { get; set; }

        public string CreatedBy { get; set; }

        public DateTime CreatedTime { get; set; }

        public string UpdatedBy { get; set; }

        public DateTime UpdatedTime { get; set; }

        public bool IsDelete { get; set; } = false;
    }
}
