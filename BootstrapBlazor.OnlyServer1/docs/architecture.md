# 架构设计

## 系统拓扑

```mermaid
graph TB
    Browser[浏览器<br/>Blazor Server SignalR]
    IIS[IIS / Kestrel]
    Web[BootstrapBlazor.OnlyServer1<br/>Blazor Server App]
    Migrator[BootstrapBlazor.DbMigrator<br/>独立工具]
    DB[(SQL Server<br/>stclmd 数据库)]
    NLog[NLog<br/>DB target + 文件]

    Browser -->|HTTPS/WS| IIS
    IIS --> Web
    Web -->|SQL| DB
    Web -->|Log| NLog
    NLog -->|INSERT| DB
    Migrator -->|Code First<br/>+ 数据迁移| DB
```

## 分层架构

```mermaid
graph TD
    Pages[Pages/<br/>Razor Components] -->|@inject| Services
    Controllers[Controllers/<br/>AccountController] --> Services
    Shared[Shared/<br/>MainLayout + LoginLayout] --> Pages

    subgraph Services["Services/ (业务层)"]
        IdSvr[Identity/<br/>SqlSugarUserStore]
        BizSvr[OrdersService<br/>UsersService<br/>RolesService<br/>DrawingNameService]
        DrawSvr[DrawingNumbers/<br/>C0XQueryService<br/>C0XWriteService]
        DrawType[DrawingTypes/<br/>Registry + 9 Handler]
        Alloc[DrawingNumberAllocator<br/>原子自增]
        Lock[OrderLockProvider<br/>进程内互斥]
    end

    Services --> Data
    IdSvr --> Data[Data/<br/>SqlSugarSetup<br/>SqlSugarScope 单例]

    Data --> DB[(SQL Server)]
```

### 各层职责

| 层 | 职责 | 不应做什么 |
|----|------|----------|
| **Pages (Razor)** | UI 渲染、用户交互、表单提交 | 不直接写 SQL;不直接 new Service |
| **Shared** | 布局、菜单、登录页布局 | 业务逻辑 |
| **Controllers** | MVC Controller 处理非 Blazor 表单(如 Login form post) | 不含业务逻辑,只调 Service |
| **Services** | 业务逻辑、事务、并发控制、缓存 | 不引用 UI 框架(BootstrapBlazor 类型) |
| **Identity Stores** | ASP.NET Core Identity 存储抽象 | 不写业务 |
| **Data** | SqlSugar 客户端初始化、AOP | 不含 SQL |

## 关键数据流

### 1. 添加图号(并发安全)

```mermaid
sequenceDiagram
    participant U as 用户
    participant P as AddDrawingNumbers.razor
    participant W as C0XWriteService
    participant L as OrderLockProvider
    participant Q as C0XQueryService
    participant A as DrawingNumberAllocator
    participant DB as SQL Server

    U->>P: 勾选图名 + 点"添加"
    P->>W: AddDrawingNumbersAsync(orderId, names[])
    W->>L: AcquireAsync(orderId)
    Note over L: 同订单串行化,<br/>防止并发互穿
    W->>Q: 锁内重查已有 DrawName
    Q->>DB: SELECT DrawName FROM C0X
    DB-->>Q: 已有列表
    loop 每个新图名
        W->>A: AllocateAsync(drawingType)
        A->>DB: UPDATE DrawingNumberSequence<br/>SET CurrentMax=CurrentMax+1<br/>OUTPUT inserted.CurrentMax
        DB-->>A: 新 DrawNumber(原子)
        A-->>W: DrawNumber
        W->>Q: handler.InsertAsync(c0x 实体)
        Q->>DB: INSERT INTO C0X
    end
    W-->>P: 成功/失败列表
    P-->>U: 显示结果 Modal
```

### 2. 保存订单(去重)

```mermaid
sequenceDiagram
    participant P as Orders.razor
    participant S as OrdersService
    participant DB as SQL Server
    participant IX as IX_Orders_OrderNumber<br/>(filtered unique)

    P->>S: SaveAsync(order, Add)
    S->>DB: BEGIN TRAN (READ COMMITTED)
    S->>DB: INSERT Orders VALUES (...)
    alt OrderNumber 冲突
        DB-->>S: SqlException 2627/2601
        Note right of IX: filtered unique index<br/>WHERE IsDeleted=0
        S->>DB: ROLLBACK
        S-->>P: (false, "订单号 X 已存在")
    else 成功
        DB-->>S: OK
        S->>DB: COMMIT
        S-->>P: (true, null)
    end
```

### 3. 登录(Identity + 业务 Claim)

```mermaid
sequenceDiagram
    participant U as 用户
    participant C as AccountController
    participant SM as SignInManager<Users>
    participant US as SqlSugarUserStore
    participant CF as DisplayNameClaimsFactory
    participant PS as PermissionsService

    U->>C: POST /Account/Login (WorkerNumber, Password)
    C->>SM: PasswordSignInAsync(workerNumber, pwd, true, true)
    SM->>US: FindByNameAsync("87100074")
    US->>DB: SELECT * FROM Users WHERE NormalizedUserName='87100074'
    DB-->>US: User
    SM->>SM: PasswordHasher.Verify(hash, pwd)
    SM->>CF: GenerateClaimsAsync(user)
    CF->>PS: GetCodesByUserIdAsync(user.Id)
    PS->>DB: SELECT Permission.Code<br/>FROM UserRoles<br/>JOIN RolePermissions<br/>JOIN Permissions<br/>WHERE UserId=?
    DB-->>PS: 权限码列表
    CF->>CF: identity.AddClaim(<br/>  DisplayName, Gid, Permission×N)
    CF-->>SM: ClaimsIdentity
    SM->>SM: SignIn(写入 Cookie)
    SM-->>C: Succeeded
    C-->>U: 302 → /
```

## 9 张 C0X 表的抽象

```mermaid
classDiagram
    class C0XBase {
        <<abstract>>
        +int Id
        +int DrawNumber
        +string DrawName
        +int OrderId
        +string CreatedBy
        +DateTime CreatedTime
        +string UpdatedBy
        +DateTime UpdatedTime
        +bool IsDeleted
    }
    class C01
    class C02
    class C03
    class C04
    class C05
    class C06
    class C07
    class C08
    class C09

    C0XBase <|-- C01
    C0XBase <|-- C02
    C0XBase <|-- C03
    C0XBase <|-- C04
    C0XBase <|-- C05
    C0XBase <|-- C06
    C0XBase <|-- C07
    C0XBase <|-- C08
    C0XBase <|-- C09

    class IDrawingTypeHandler {
        <<interface>>
        +string DrawingType
        +CreateNewEntity(...)
        +InsertAsync(...)
        +FindByIdAsync(...)
    }
    class DrawingTypeHandlerBase~T~ {
        <<abstract>>
        +T 泛型约束
    }
    class C01Handler
    class C02Handler
    class ...
    class IDrawingTypeRegistry {
        +Resolve(string) IDrawingTypeHandler?
        +GetDrawingType(Orders) string?
        +GetSeedDrawingNumber(string) int
    }
    class DrawingTypeRegistry

    IDrawingTypeHandler <|.. DrawingTypeHandlerBase~T~
    DrawingTypeHandlerBase~T~ <|-- C01Handler
    DrawingTypeHandlerBase~T~ <|-- C02Handler
    IDrawingTypeRegistry <|.. DrawingTypeRegistry
    C01Handler --> C01
    C02Handler --> C02
```

**为什么用 abstract 基类 + 9 个具体子类**:
- SqlSugar Code First **不会**为 abstract 类建表,具体子类各建一张
- 每个子类表名硬编码(`[SugarTable("C01")]` 默认按类名),无需运行时反射
- `DrawingTypeHandlerBase<TEntity>` 模板方法消除 9 个 handler 的样板代码

## 配置外置的运行时可调项

| 配置 | appsettings 节 | 改动时 | 详见 |
|------|---------------|-------|------|
| 图号 Power 区间映射 | `BusinessRules.DrawingType.PowerRanges` | 改区间不动代码 | [configuration.md](configuration.md) |
| 电抗器 DrawingType | `BusinessRules.DrawingType.ReactorType` | 改值不动代码 | [configuration.md](configuration.md) |
| 图号种子值 | `BusinessRules.DrawingType.Seeds` | 改值不动代码 | [configuration.md](configuration.md) |
| Identity 密码策略 | `Identity.Password` | 改强度不动代码 | [configuration.md](configuration.md) |
| Identity 锁定策略 | `Identity.Lockout` | 改阈值不动代码 | [configuration.md](configuration.md) |

## 相关 ADR

- [ADR-0001: 用 SqlSugar 替代 EF Core](adr/0001-use-sqlsugar-instead-of-ef-core.md)
- [ADR-0002: 自定义 SqlSugar Identity Store](adr/0002-custom-identity-store-on-sqlsugar.md)
- [ADR-0003: 全项目统一 file-scoped namespace](adr/0003-file-scoped-namespace-convention.md)
- [ADR-0004: C0XBase abstract 多态](adr/0004-handle-base-c0x-with-abstract-class.md)
- [ADR-0005: 用 UNIQUE INDEX 替代 SERIALIZABLE](adr/0005-replace-serializable-with-unique-index.md)