-- ============================================================
-- 001_IdentitySchema.sql
-- ASP.NET Core Identity schema (B3: SqlSugar + 自定义 UserStore)
-- ============================================================
-- 说明:
--   * Users 表合并身份字段 + 业务字段
--   * UserName 存 WorkerNumber 的字符串形式
--   * WorkerNumber 仍保留,便于业务代码直接按数字查询
--   * 角色表 / UserRoles / Claims 表为未来 RBAC 预留,UI 暂不实现
-- ============================================================

-- 1) 重建 Users 表
IF OBJECT_ID('Users', 'U') IS NOT NULL DROP TABLE Users;
CREATE TABLE Users (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    UserName            NVARCHAR(256) NOT NULL,
    NormalizedUserName  NVARCHAR(256) NOT NULL,
    PasswordHash        NVARCHAR(500) NULL,
    SecurityStamp       NVARCHAR(50)  NULL,
    ConcurrencyStamp    NVARCHAR(50)  NULL,
    -- 业务字段
    Name                NVARCHAR(100) NOT NULL,
    WorkerNumber        INT           NOT NULL,
    Gid                 NVARCHAR(50)  NOT NULL,
    CreateTime          DATETIME      NOT NULL DEFAULT GETDATE(),
    IsDelete            BIT           NOT NULL DEFAULT 0,
    -- 锁定字段(为登录失败锁定用)
    LockoutEnd          DATETIME      NULL,
    LockoutEnabled      BIT           NOT NULL DEFAULT 1,
    AccessFailedCount   INT           NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users(NormalizedUserName);
CREATE UNIQUE INDEX IX_Users_WorkerNumber        ON Users(WorkerNumber);

-- 2) 角色表(为未来 RBAC 预留)
IF OBJECT_ID('Roles', 'U') IS NOT NULL DROP TABLE Roles;
CREATE TABLE Roles (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    Name              NVARCHAR(50)  NOT NULL,
    NormalizedName    NVARCHAR(50)  NOT NULL,
    ConcurrencyStamp  NVARCHAR(50)  NULL
);
CREATE UNIQUE INDEX IX_Roles_NormalizedName ON Roles(NormalizedName);

-- 3) 用户-角色关联
IF OBJECT_ID('UserRoles', 'U') IS NOT NULL DROP TABLE UserRoles;
CREATE TABLE UserRoles (
    UserId  INT NOT NULL,
    RoleId  INT NOT NULL,
    PRIMARY KEY (UserId, RoleId),
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
);

-- 4) 用户 Claims(预留,如需存额外信息)
IF OBJECT_ID('UserClaims', 'U') IS NOT NULL DROP TABLE UserClaims;
CREATE TABLE UserClaims (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT NOT NULL,
    ClaimType   NVARCHAR(100) NULL,
    ClaimValue  NVARCHAR(200) NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- 5) 角色 Claims(预留)
IF OBJECT_ID('RoleClaims', 'U') IS NOT NULL DROP TABLE RoleClaims;
CREATE TABLE RoleClaims (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    RoleId      INT NOT NULL,
    ClaimType   NVARCHAR(100) NULL,
    ClaimValue  NVARCHAR(200) NULL,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
);

-- 6) 种子:管理员账号 (WorkerNumber=1, Password=Admin@123)
--    注意:请勿直接在此处插入 PasswordHash 假数据 —— ASP.NET Core Identity 的
--    PasswordHasher(PBKDF2-SHA256)每次生成不同 salt,无法在静态 SQL 中硬编码可用哈希。
--    请改用 BootstrapBlazor.DbMigrator --seed 通道灌管理员,程序会用
--    new PasswordHasher<Users>().HashPassword(...) 生成真正可登录的哈希。
--    上线前请修改默认密码。

-- 7) 种子:基础角色(为未来权限系统预留)
INSERT INTO Roles (Name, NormalizedName, ConcurrencyStamp) VALUES
    ('Admin', 'ADMIN', NEWID()),
    ('User',  'USER',  NEWID());

-- 8) 把管理员关联到 Admin 角色
INSERT INTO UserRoles (UserId, RoleId)
SELECT u.Id, r.Id FROM Users u, Roles r WHERE u.WorkerNumber = 1 AND r.NormalizedName = 'ADMIN';