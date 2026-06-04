-- 将所有Orders记录的IsDeleted设置为false
UPDATE Orders SET IsDeleted = 0;

-- 查看更新结果
SELECT COUNT(*) AS TotalOrders, 
       SUM(CASE WHEN IsDeleted = 0 THEN 1 ELSE 0 END) AS UndeletedCount,
       SUM(CASE WHEN IsDeleted = 1 THEN 1 ELSE 0 END) AS DeletedCount
FROM Orders;
