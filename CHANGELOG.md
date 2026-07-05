# 变更日志

本项目的所有重要变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/)。

## [Unreleased]

### Changed
- **重构**:用 `IX_Orders_OrderNumber` filtered unique index 替代 SERIALIZABLE 事务防重,保存耗时从 1-2 分钟降到 < 100ms (ADR 0005)
- **重构**:Models 目录全部统一为 file-scoped namespace (ADR 0003)
- **重构**:N+1 查询修复 — `RolesService` 新增批量查询方法,`Roles.razor` / `UserRoles.razor` 单页查询数从 20-80 降到 2
- **清理**:删除死页面 `UpdateDrawingNumber.razor` 和 `AddDrawingNumbersSelect.razor`
- **清理**:删除导航菜单上无效入口(`/add-drawing-numbers`、`/update-drawing-number` 缺路由参数 → 404)

### Fixed
- **fix**:新建订单卡顿 1-2 分钟问题(根因:SERIALIZABLE 范围锁 + 默认 lock_timeout 无限等待)

## [2.0.0] - 2026-07-05

### Added
- ASP.NET Core Identity + 自定义 SqlSugarUserStore/RoleStore
- 9 张 C0X 图号表(C0XBase abstract + 9 个 handler 多态写入)
- DrawingNumberAllocator 原子分配(SQL Server `UPDATE...OUTPUT`)
- OrderLockProvider 同订单进程内互斥
- DisplayNameClaimsFactory 写入业务 Claim(DisplayName/GID/Permission)
- NLog DB target 写 `Logs` 表
- 25+ 个细粒度权限码(PermissionCodes 常量)
- 角色保护:ADMIN 唯一超管拒绝移除

### Changed
- 从 EF Core 迁移到 SqlSugar
- C0X 表废弃 `DrawingType` 列(冗余,可由表名推算)
- 引入 `IX_Orders_OrderNumber` filtered unique index 之前的实现用 SERIALIZABLE

## [1.0.0] - 早期版本

- 订单 CRUD
- 图号分配(图号 = 公式 `MinDrawingNumber + Id`)
- 图名维护
- 用户/部门管理