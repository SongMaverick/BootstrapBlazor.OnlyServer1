-- ============================================================
-- 003_RenameOrderMDCustomerToProjectName.sql
-- (2026-08-13) OrderMDDetail.Customer → OrderMDDetail.ProjectName 列重命名
-- ============================================================
-- 原因:订单管理里"客户"改名为"项目名称",语义更贴切(本系统是设计方视角,
--   不直接对接最终客户,记录的是项目名)。C# 字段同步改名。
--
-- 安全性:
--   * sp_rename 是 SQL Server 原生原子操作,数据零丢失
--   * 不影响索引 / 约束 / 默认值
--   * 不需要 DROP/RECREATE 表
--   * 执行时间:毫秒级(只更新系统目录)
--
-- ⚠ 部署顺序(强约束):
--   1) 先发代码(已改名 ProjectName)
--   2) 再跑这个 SQL(数据库列改名)
--   反过来跑会瞬间挂(代码找不到列,DB 还有旧列)
-- ============================================================

USE [stcl_md];  -- 改成实际数据库名
GO

-- 1) 前置检查:列必须存在,且新列名不能存在
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'Customer'
)
AND NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'ProjectName'
)
BEGIN
    PRINT '✓ 前置检查通过:OrderMDDetail.Customer 存在,ProjectName 不存在';
END
ELSE
BEGIN
    PRINT '✗ 前置检查失败:';
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'Customer')
        PRINT '  - OrderMDDetail.Customer 列不存在(可能已改名)';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'ProjectName')
        PRINT '  - OrderMDDetail.ProjectName 列已存在(可能重命名过了)';
    PRINT '脚本中止';
    RETURN;
END
GO

-- 2) 数据样本(执行前确认有数据)
PRINT '=== 重命名前的数据样本(前 5 行非空) ===';
SELECT TOP 5 Id, Customer AS old_name
FROM OrderMDDetail
WHERE Customer IS NOT NULL AND Customer <> ''
ORDER BY Id;
PRINT '(以上数据重命名后会自动迁移到 ProjectName 列,内容不变)';
GO

-- 3) 原子重命名
BEGIN TRANSACTION;
BEGIN TRY
    EXEC sp_rename 'dbo.OrderMDDetail.Customer', 'ProjectName', 'COLUMN';
    PRINT '✓ sp_rename 执行成功';

    -- 4) 验证
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'ProjectName')
    AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'Customer')
    BEGIN
        PRINT '✓ 验证通过:列已从 Customer 改为 ProjectName';
        PRINT '=== 重命名后的数据样本(前 5 行非空) ===';
        SELECT TOP 5 Id, ProjectName AS new_name
        FROM OrderMDDetail
        WHERE ProjectName IS NOT NULL AND ProjectName <> ''
        ORDER BY Id;
        COMMIT TRANSACTION;
        PRINT '✓ 事务已提交';
    END
    ELSE
    BEGIN
        PRINT '✗ 验证失败:rollback';
        ROLLBACK TRANSACTION;
    END
END TRY
BEGIN CATCH
    PRINT '✗ 异常:' + ERROR_MESSAGE();
    ROLLBACK TRANSACTION;
END CATCH
GO

-- ============================================================
-- 回滚脚本(万一代码回退,需要把列改回去):
-- ============================================================
-- USE [stcl_md];
-- GO
-- IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'ProjectName')
-- AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderMDDetail') AND name = 'Customer')
-- BEGIN
--     EXEC sp_rename 'dbo.OrderMDDetail.ProjectName', 'Customer', 'COLUMN';
--     PRINT '✓ 已回滚:ProjectName → Customer';
-- END
-- GO