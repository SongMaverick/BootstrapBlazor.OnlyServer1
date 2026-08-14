-- ============================================================
-- 004_AddOrderIssuesClosedByAndTime.sql
-- (2026-08-14) OrderIssues 新增 ClosedBy / ClosedTime 两列
-- ============================================================
-- 原因:支持"只能关闭一次,关闭后不能再修改"业务规则。
--   - ClosedBy NVARCHAR(100) NULL:关闭人用户名
--   - ClosedTime DATETIME NULL:关闭时间
--   两列都允许 NULL:open 状态下两列为 NULL,close 时由 Service 一次性写入,
--   关闭后这两列就是"首次也是唯一一次"的关闭记录,不再变动。
--
-- 安全性:
--   * 用 IF NOT EXISTS 前置检查,幂等可重跑
--   * 仅 ADD COLUMN 不 DROP 任何已有数据
--   * 不影响 OrderIssues 表的现有数据(原有问题记录 NULL,符合预期)
--
-- ⚠ 部署顺序(强约束):
--   1) 先发代码(已加 ClosedBy / ClosedTime 字段)
--   2) 再跑这个 SQL(添加这两列)
--   反过来跑:已关闭的问题读不到 ClosedBy / ClosedTime 会显示 NULL,
--   但前端会用 "—" 占位,不会崩溃;open 的问题天然就是 NULL,无影响。
-- ============================================================

USE [stclmd];  -- 改成实际数据库名
GO

-- 1) ClosedBy
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('OrderIssues') AND name = 'ClosedBy'
)
BEGIN
    ALTER TABLE OrderIssues ADD ClosedBy NVARCHAR(100) NULL;
    PRINT '+ Added ClosedBy column';
END
ELSE
BEGIN
    PRINT '= ClosedBy column already exists, skipped';
END
GO

-- 2) ClosedTime
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('OrderIssues') AND name = 'ClosedTime'
)
BEGIN
    ALTER TABLE OrderIssues ADD ClosedTime DATETIME NULL;
    PRINT '+ Added ClosedTime column';
END
ELSE
BEGIN
    PRINT '= ClosedTime column already exists, skipped';
END
GO

PRINT 'Done. Migration 004 applied.';
