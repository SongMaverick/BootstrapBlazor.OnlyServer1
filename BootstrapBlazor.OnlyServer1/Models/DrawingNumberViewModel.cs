namespace BootstrapBlazor.OnlyServer1.Models
{
    public class DrawingNumberViewModel
    {
        public int Id { get; set; }

        public string DrawingTypeNumber { get; set; }

        public string DrawName { get; set; }

        public string DrawingType { get; set; }

        public int OrderId { get; set; }

        public int OrderNumber { get; set; }

        public string CreatedBy { get; set; }

        public DateTime CreatedTime { get; set; }

        public string UpdatedBy { get; set; }

        public DateTime UpdatedTime { get; set; }

        public bool IsDelete { get; set; } = false;
    }
}
