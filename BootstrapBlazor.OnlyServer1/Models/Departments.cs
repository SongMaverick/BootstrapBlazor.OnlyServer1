using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models
{
    /// <summary>
    /// 部门实体。Users.DepartmentId 引用本表 Id。
    /// FK ON DELETE NO ACTION(默认):有员工引用时禁止硬删,仅可软删(IsDelete=true)。
    /// NormalizedName 用于 UNIQUE 索引(类似 Identity Roles 模式)。
    /// </summary>
    [SugarTable("Departments")]
    public class Departments
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 100)]
        public string Name { get; set; } = "";

        [SugarColumn(IsNullable = false, Length = 100)]
        public string NormalizedName { get; set; } = "";

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? Code { get; set; }

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreateTime { get; set; }

        public bool IsDelete { get; set; }
    }
}
