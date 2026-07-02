# 编号本 (BootstrapBlazor.OnlyServer1)

内部办公 Web 程序:管理变压器/电抗器订单的图号分配与查询。

- **技术栈**:.NET 8 + Blazor Server + BootstrapBlazor 8.x + SqlSugar 5.1.4 + SQL Server
- **认证**:ASP.NET Core Identity(`IdentityUser<int>` + SqlSugar 自定义 Store)
- **日志**:NLog(写 DB `Logs` 表 + 文件兜底)
- **DB**:SQL Server Express,单实例办公网

---

## 1. 启动与配置

### 1.1 配置(`appsettings.json`)

```json
{
  "Urls": "http://localhost:8373",                     // ← Web 监听端口
  "ConnectionStrings": {
    "sqlServerConnectionStringOffice": "...",          // ← 业务库(MechanicalDesign 账号,无 DDL 权限)
    "sqlServerConnectionStringSA":     "..."            // ← 管理员账号(可建表)
  }
}
```

**注意**:DbMigrator 用的是它自己的 `appsettings.json`(`BootstrapBlazor.DbMigrator/appsettings.json`),**和 Web 项目不共享**。

### 1.2 启动方式

```powershell
# 方式 1:VS2022 F5 / Ctrl+F5
# 方式 2:命令行
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.OnlyServer1
```

### 1.3 首次部署步骤

```cmd
# 1. 先跑 DbMigrator 建表 + 灌数据
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.DbMigrator --drop --schema --seed --migrate --source "D:\D7_codeBase\BootstrapBlazor.OnlyServer1"

# 2. 启动 Web
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.OnlyServer1

# 3. 浏览器访问 http://localhost:8373,登录
#    员工号:1  密码:Admin@123
```

---

## 2. 架构总览

```
BootstrapBlazor.OnlyServer1/
├── Program.cs                          ← DI 注册、Identity 配置、中间件管道
├── Data/SqlSugarSetup.cs               ← SqlSugarScope 单例 + AOP 日志
├── Models/                             ← EF/SqlSugar 实体
│   ├── Users.cs / Roles.cs             ← Identity 标准实体(继承 IdentityUser<int>/Role<int>)
│   ├── Orders.cs / OrdersItem.cs       ← 订单主表 / 占位行表(目前未使用)
│   ├── C0XBase.cs + C01.cs..C09.cs     ← 9 张图号表(共享 schema,各自建表)
│   ├── DrawingNumberSequence.cs        ← 图号序列表(原子分配用)
│   ├── DrawingNames.cs                 ← 图名基础数据
│   ├── DrawingNumberViewModel.cs       ← 图号查询结果 ViewModel
│   └── Logs.cs                         ← NLog dbTarget 输出表
│
├── Services/
│   ├── Identity/                       ← ASP.NET Core Identity 的 SqlSugar 实现
│   ├── DrawingTypes/                   ← 9 张 C0X 表的统一抽象(handler + registry)
│   ├── DrawingNumberService.cs         ← 图号 Service(主要业务入口)
│   ├── DrawingNumberAllocator.cs       ← 图号原子分配器(原子 UPDATE...OUTPUT)
│   ├── OrderLockProvider.cs            ← 同订单并发互斥(进程内)
│   ├── DrawingNumberInsertResult.cs    ← 批量插入结果 DTO
│   ├── UsersService.cs / OrdersService.cs ← 用户/订单 Service
│   └── ...
│
├── Controllers/AccountController.cs    ← /Account/Login 与 /Account/Logout(MVC Controller)
│
├── Components/
│   ├── App.razor / Routes.razor         ← Blazor 路由根
│   ├── Shared/                          ← MainLayout + LoginLayout
│   └── Pages/                           ← 所有业务页面(12 个)
│
├── Migrations/001_IdentitySchema.sql    ← Identity schema 参考 DDL
│
└── NLog.config                          ← 日志配置(写 DB + 文件)
```

### 2.1 数据流向(以"添加图号"为例)

```
用户操作 → 页面(.razor)
   ↓ 调用
Service(UsersService / OrdersService / DrawingNumberService)
   ↓ 走
DrawingTypeHandler(9 个之一)
   ↓ 调用
IDrawingNumberAllocator(原子 UPDATE...OUTPUT)
   ↓ 写
SQL Server(数据库)
   ↓
回包 ViewModel → 页面渲染
```

---

## 3. 数据库表(全部)

> 完整建表由 `BootstrapBlazor.DbMigrator` 的 Code First 自动生成,这里只列字段语义。

### 3.1 Identity(5 张)

| 表 | PK | 关键字段 |
|---|---|---|
| `Users` | `Id INT IDENTITY` | `UserName`, `NormalizedUserName`, `PasswordHash`, `Name`, `WorkerNumber`, `GID`, `CreateTime`, `IsDelete`, `Email`, `PhoneNumber` 等 |
| `Roles` | `Id` | `Name`, `NormalizedName`, `ConcurrencyStamp` |
| `UserRoles` | `(UserId, RoleId)` 复合 PK | 多对多关联 |
| `UserClaims` | `Id` | 预留,当前未使用 |
| `RoleClaims` | `Id` | 预留,当前未使用 |

### 3.2 业务表(11 张)

| 表 | 说明 |
|---|---|
| `Orders` | 订单主表(`IsReactor=true` 是电抗器) |
| `OrdersItem` | 订单 ID 重映射表(预留,当前未使用) |
| `DrawingNames` | 图名基础数据,字段:`Part`(类别) + `ComponentName`(图纸名称) |
| `DrawingNumberSequence` | **图号序列表**,每张 C0X 一行,字段:`DrawingType` PK, `CurrentMax` |
| `C01`..`C09` | 9 张图号主表,字段:`Id` PK, `DrawNumber` UNIQUE, `DrawName`, `OrderId`, `CreatedBy/Time`, `UpdatedBy/Time`, `IsDelete` |
| `Logs` | NLog 系统日志(异常 + Info+) |

**DrawNumber 规则**:每张 C0X 表的 DrawNumber 由 `DrawingNumberSequence` 原子分配,通过 `UPDATE...OUTPUT` 单条 SQL 实现(`Services/DrawingNumberAllocator.cs`)。**绝不可能重复**。

### 3.3 索引

| 表 | 索引 |
|---|---|
| `Users` | `IX_Users_NormalizedUserName` UNIQUE, `IX_Users_WorkerNumber` UNIQUE |
| `Roles` | `IX_Roles_NormalizedName` UNIQUE |
| `C01..C09` | `IX_C0X_DrawNumber` UNIQUE(每张) |

---

## 4. 页面清单(12 个)

### 4.1 登录 / 登出

#### `/login` — Login.razor

- **路由**: `/login`
- **Layout**: `LoginLayout`(独立登录页布局)
- **功能**:员工号 + 密码登录表单(form post 到 `/Account/Login`)
- **依赖**:无(纯 HTML form)
- **错误显示**:从 `TempData["ErrorMsg"]` 读取(由 `AccountController` 写入)
- **注意**:
  - 失败 5 次会锁定账号 5 分钟(`Lockout.MaxFailedAccessAttempts`)
  - 锁定信息由 `Identity` 标准管道处理

#### `/logout` — LogoutPage.razor

- **路由**: `/logout`
- **功能**:占位页(真正的登出通过 Layout 顶部的"退出登录"按钮 form post 到 `/Account/Logout`)
- **行为**:直接 NavigateTo("/login", forceLoad: true)

### 4.2 主布局

#### MainLayout.razor + MainLayout.razor.cs

- **Layout**:所有业务页面共用
- **顶部**:
  - 左侧"编号本"标题
  - 右侧用户头像下拉菜单(用户名 + GID)
  - 下拉菜单项:修改密码、个人中心、设置、通知、退出登录
- **左侧菜单**(`GetIconSideMenuItems`):
  - 首页、添加图号、订单列表、查询图号、订单图号列表、图名列表、人员列表(7 项)
- **抽屉**(右侧):布局调整(左右/上下)、固定页头/页脚、显示页脚、多标签/单页
- **修改密码弹窗**:任意页面都可点"修改密码"触发

#### 修改密码弹窗

- **触发**:Layout 顶部下拉菜单"修改密码"
- **流程**:
  1. 旧密码 + 新密码 + 确认新密码
  2. 校验:旧密码正确(Identity 校验)、新密码 ≥ 6 位、两次输入一致
  3. 调用 `UsersService.ChangePasswordAsync(userId, oldPassword, newPassword)`
  4. 成功 → Toast 提示并关闭弹窗
  5. 失败 → 显示具体错误(如"旧密码不正确")
- **代码**:`MainLayout.razor.cs:113-165`

### 4.3 业务页面

#### `/` — 首页(Components/Pages/Index.razor)

- **路由**: `/`
- **功能**:静态欢迎页"欢迎使用编号本 / 编号本版本 2.0.0"
- **依赖**:无

---

#### `/user-management` — 人员列表

**文件**: `Components/Pages/UserManagement.razor`

- **路由**: `/user-management`
- **布局**: MainLayout
- **数据源**: `UsersService.QueryAsync(options)`
- **依赖服务**: `UsersService`
- **功能**:
  - 分页表格(10/20/40 每页)
  - 字段:Id / Name / WorkerNumber / GID / CreateTime
  - **新增用户**:点"+" → 弹"设置初始密码"模态框 → 填写员工号/姓名/GID/初始密码 → 调用 `UsersService.SaveAsync(user, password, Add)`
  - **编辑用户**:行内编辑 → 调用 `UsersService.SaveAsync(user, null, Update)`(不修改密码)
  - **删除用户**:多选 + 软删除(`IsDelete=true`)→ `UsersService.SoftDeleteAsync(users)`
- **密码列隐藏**:表格中 `PasswordHash` 列 `Visible="false"`,改密走专门的"修改密码"功能
- **搜索**:表格顶部搜索框(按 Name / GID 模糊匹配)
- **关键代码**: `UserManagement.razor:96-191`

---

#### `/orders` — 订单列表

**文件**: `Components/Pages/Orders.razor`

- **路由**: `/orders`
- **数据源**: `OrdersService.QueryAsync(options)` + `PreloadHasDrawingNumberCacheAsync` 预热缓存
- **依赖服务**: `OrdersService`, `DrawingNumberService`(间接)
- **功能**:
  - 分页表格(10/20/40)
  - 字段:Id / 订单号 / 容量(MW) / 额定电压 / 类型(电抗器/变压器徽章) / 创建者 / 创建时间
  - **新增**:弹出新增表单 → `OrdersService.SaveAsync(order, Add)`
  - **编辑**:行内编辑(SERIALIZABLE 事务,防并发重号)
  - **删除**:多选 + 软删除(`IsDeleted=true`)
- **工具栏按钮**:
  - **添加图号**:必须先勾选 1 个订单 → 跳转 `/add-drawing-numbers/{OrderId}`
  - **查看图号**:必须先勾选 1 个订单 → 跳转 `/order-drawing-numbers/{OrderId}`
- **编辑限制**:订单下已有图号时,**编辑/删除按钮被禁用**(`HasDrawingNumber` 回调)
  - 防止改了订单号导致图号 ID 引用错位
- **并发防重**:保存时用 `SERIALIZABLE` 事务 + 重查,避免同一订单号并发重复

---

#### `/add-drawing-numbers-select` — 选择订单添加图号

**文件**: `Components/Pages/AddDrawingNumbersSelect.razor`

- **路由**: `/add-drawing-numbers-select`(无参数)
- **数据源**: `OrdersService.SearchAsync(options, orderNumber, createdBy, isReactor)`
- **依赖服务**: `OrdersService`
- **功能**:
  - 筛选区:订单号(模糊)、创建者(模糊)、是否电抗器(下拉)
  - 订单表格(单选模式 `ClickToSelect`)
  - 选中后显示订单摘要(订单号 + 容量 + 类型徽章)
  - 底部两个按钮:
    - **添加图号** → 跳转 `/add-drawing-numbers/{OrderId}`
    - **查看图号列表** → 跳转 `/order-drawing-numbers/{OrderId}`
- **用途**:替代 `/orders` 页面的"添加图号"流程,提供更精确的订单搜索能力

---

#### `/add-drawing-numbers/{orderId:int}` — 添加图号(主页面)

**文件**: `Components/Pages/AddDrawingNumbers.razor`

- **路由**: `/add-drawing-numbers/{OrderId}` (int 路由参数)
- **数据源**:
  - `OrdersService.GetByIdAsync(OrderId)` 加载订单
  - `DrawingNumberService.GetAllDrawingNamesAsync()` 加载所有图名
  - `DrawingNumberService.GetAddedDrawingNumbersByOrderIdAsync(OrderId)` 加载该订单已添加的图号
- **依赖服务**: `DrawingNumberService`, `OrdersService`
- **功能**:
  - 按 `Part`(图名类别)分组显示所有可添加的图名
  - 已添加的图名勾选框被禁用并打"已添加"标记
  - **添加按钮**:批量提交所有勾选的图名 → `DrawingNumberService.AddDrawingNumbersAsync(OrderId, names[])`
  - **更改顺序添加**:弹窗显示当前勾选的图名,可在文本框里调整顺序,提交后图号按文本框顺序分配
  - **结果弹窗**:成功/部分成功/失败明细(失败原因:已添加/订单不存在/订单类型无法识别/未登录)
- **分组样式**:
  - 4 个老 Part(ESL-01/02/03/Core)用固定 Bootstrap 配色
  - 新 Part 用 hash 分配颜色(同 Part 颜色稳定)
- **并发防重**:同订单串行化(`OrderLockProvider` 按 OrderId 维度)
  - 同一订单内两个请求会**互锁**,保证"图名重复检查"准确
- **图号分配**:每次插入前 `IDrawingNumberAllocator.AllocateAsync(drawingType)` 原子分配下一个 DrawNumber

---

#### `/order-drawing-numbers/{orderId:int}` — 订单图号列表

**文件**: `Components/Pages/OrderDrawingNumbers.razor`

- **路由**: `/order-drawing-numbers/{orderId}`
- **数据源**:
  - `OrdersService.GetByIdAsync(OrderId)` 加载订单
  - `DrawingNumberService.GetAddedDrawingNumberViewModelsByOrderIdAsync(OrderId)` 加载 ViewModel 列表
- **依赖服务**: `DrawingNumberService`, `OrdersService`, `CurrentUserAccessor`, `ToastService`
- **功能**:
  - 表格展示该订单下所有图号
  - 字段:图号 / 图名 / 创建者 / 创建时间 / 修改者 / 修改时间 / 操作
  - **添加图号**:右上角按钮 → 跳转 `/add-drawing-numbers/{OrderId}`
  - **修改图号**:行内"修改"按钮 → **弹窗**(不是新页面) → 只能改 DrawName → `DrawingNumberService.UpdateCX0DrawingNumberByIdAsync`
- **修改弹窗字段**:
  - 图号(只读,显示如 "C04 17 001")
  - 图纸名称(可编辑)
  - 主键 ID(只读,灰色小字)
- **注意**:图号(DrawNumber)**不能改**,只能改 DrawName;改完跳回列表页
- **响应式刷新**:监听 `NavigationManager.LocationChanged`,切回本页时自动 `LoadDataAsync()`

---

#### `/update-drawing-number/{id:int}/{drawingType}` — 单独修改图号页

**文件**: `Components/Pages/UpdateDrawingNumber.razor`

- **路由**: `/update-drawing-number/{id}/{drawingType}` (如 `/update-drawing-number/123/C04`)
- **数据源**: `DrawingNumberService.FindDrawingNumberByIdAsync(id, drawingType)`
- **依赖服务**: `DrawingNumberService`, `CurrentUserAccessor`, `ToastService`
- **功能**:
  - 显示编号(图号主键 Id)、图号(DrawingTypeNumber)、图纸名称(可编辑)
  - 保存 → `DrawingNumberService.UpdateCX0DrawingNumberByIdAsync(c0x, drawingType)`
  - 成功跳回 `/order-drawing-numbers/{OrderId}`
- **注意**:这个独立页当前代码里**没有任何按钮调用**(只有 `OrderDrawingNumbers.razor` 弹窗式修改),但保留为备选入口

---

#### `/find-drawing-number` — 查询图号

**文件**: `Components/Pages/FindDrawingNumber.razor`

- **路由**: `/find-drawing-number`
- **依赖服务**: `DrawingNumberService`, `CurrentUserAccessor`
- **功能**:
  - 三个输入:前三位(下拉 C01-C09)+ 中段 2 位 + 末段 3 位
  - 例:输入 `C04` + `17` + `001` → 查 DrawNumber=17001
  - 点击"查询" → `DrawingNumberService.FindDrawingNumberByTypeAndNumberAsync(type, number)`
  - 结果弹窗显示:图号 / 订单号 / 图名 / 创建者 / 创建时间 / 修改者 / 修改时间 / 状态
  - "查无此号"提示(可能是占位行被过滤掉,也可能是真不存在)
- **校验**:中段必须是 2 位数字、末段必须是 3 位数字,正则校验
- **图号过滤**:通过 handler 的 `FindByDrawNumberAsync` 自动过滤 `IsDelete=true` 的占位行

---

#### `/drawing-names` — 图名列表

**文件**: `Components/Pages/DrawingNameList.razor`

- **路由**: `/drawing-names`
- **依赖服务**: `DrawingNumberService`
- **功能**:
  - 表格展示所有图名(包括已删除的,加删除线样式)
  - 字段:编号 / 图纸名称 / 图名类别(Part) / 创建者 / 创建时间 / 修改者 / 修改时间 / 状态(可用/已删除)
  - **添加图名**:右上角"添加图名"按钮 → 弹窗 → 填写 ComponentName + 选择 Part → `DrawingNumberService.AddDrawingNameAsync(name, part)`
  - **删除**:软删除(`Isdelete=true`)→ `DrawingNumberService.DeleteDrawingNameAsync(id)`,浏览器原生 confirm 确认
- **Part 下拉选项**:
  - 优先用 DB 现有数据(`GetDistinctPartsAsync`)
  - DB 为空时回退到 4 个老 Part:ESL-01/02/03/Core
- **添加后刷新**:成功添加新 Part 后立即刷新下拉,避免必须刷新页面才能选

---

### 4.4 系统页面

#### `/error` — 错误页(Components/Pages/Error.razor)

- ASP.NET Core 默认错误页。显示在生产/开发环境不同的提示。

#### `/access-denied` — 无权限页(Components/Pages/AccessDenied.razor)

- **路由**: `/access-denied`
- **功能**:用户已登录但权限不足时显示。可点"返回首页"。

---

## 5. 服务层详解

### 5.1 `SqlSugarSetup.cs`

- **作用**:构造 SqlSugar `SqlSugarScope` 单例 + 注册 AOP SQL 日志
- **AOP**:所有 SQL 执行时通过 `ILogger.LogDebug` 输出,带参数 JSON 序列化
- **连接**:从 `ConnectionStrings.sqlServerConnectionStringOffice` 读取

### 5.2 `Services/Identity/`(ASP.NET Core Identity 自定义 Store)

#### `SqlSugarUserStore.cs`

完整实现 ASP.NET Core Identity 的 `IUserStore<TUser>` 标准接口集:

- `IUserStore<Users>` — Create/Update/Delete/FindById/FindByName
- `IUserPasswordStore<Users>` — PasswordHash 读写
- `IUserSecurityStampStore<Users>` — SecurityStamp 读写
- `IUserLockoutStore<Users>` — AccessFailedCount / LockoutEnd / LockoutEnabled
- `IUserEmailStore<Users>` — Email / NormalizedEmail / EmailConfirmed
- `IUserClaimStore<Users>` — Get/Add/Replace/Remove/GetUsersForClaim
- `IUserLoginStore<Users>` — 预留,未实现(空操作)
- `IUserTwoFactorStore<Users>` — TwoFactorEnabled
- `IUserPhoneNumberStore<Users>` — PhoneNumber
- **`IUserRoleStore<Users>` — AddToRole/RemoveFromRole/GetRoles/IsInRole/GetUsersInRole** ← 关键
- `IQueryableUserStore<Users>` — 暴露 `IQueryable<Users>`

**重要内部类**:
- `UserClaimRow` — `UserClaims` 表的轻量映射
- `UserRoleRow` — `UserRoles` 表的轻量映射(只 UserId + RoleId)

#### `SqlSugarRoleStore.cs`

完整实现 `IRoleStore<Roles>` + `IRoleClaimStore<Roles>`,含 `RoleClaimRow` 内部类。

#### `DisplayNameClaimsFactory.cs`

极简 ClaimsPrincipal 工厂,继承 `UserClaimsPrincipalFactory<Users>`,**仅追加业务字段**(DisplayName、 GID)。角色 claim 由 Identity 标准管道通过 `IUserRoleStore` 自动写入,无需手写。

#### `CurrentUserAccessor.cs`

从 `IHttpContextAccessor` 取当前 ClaimsPrincipal,暴露便捷方法:

- `GetUserId()` — 返回 int(0 表示未登录)
- `GetUserName()` — Identity.Name
- `GetDisplayName()` — 业务字段 Name
- `GetGID()` — 业务字段 GID

#### `ClaimsPrincipalExtensions.cs`

`DisplayNameClaim` 和 `GIDClaim` 常量定义。

### 5.3 `Services/DrawingTypes/`(9 张 C0X 表抽象)

#### `IDrawingTypeHandler.cs`

每种 drawingType 行为的接口:
- `DrawingType` — `"C01"` .. `"C09"`
- `SeedDrawingNumber` — 该类型的图号种子值(分配器首次初始化用)
- `CreateNewEntity` — 创建新 C0XBase 实例(handler 必须 `new TEntity`)
- `GetExistingDrawNamesAsync` / `GetAllByOrderIdAsync` / `QueryExistingOrderIdsAsync` / `FindByIdAsync` / `FindByDrawNumberAsync` — 查询
- `InsertAsync` / `UpdateDrawNameAsync` — 写入

#### `DrawingTypeHandlerBase<TEntity>.cs`

泛型基类,实现所有 IDrawingTypeHandler 方法,子类只需声明 `DrawingType` 和 `SeedDrawingNumber`。

#### `IDrawingTypeRegistry` / `DrawingTypeRegistry.cs`

- 9 个 handler 的注册表(`Dictionary<string, IDrawingTypeHandler>`)
- `GetDrawingType(Orders)` — 根据订单属性推算 drawingType:
  - `IsReactor` → `C08`
  - Power 区间 → `C02..C07`(2-10 / 10-40 / 40-100 / 100-200 / 200-400 / 400+)
  - 其它返回 null(无法识别)
- `GetSeedDrawingNumber(drawingType)` — 给分配器初始化用

#### 9 个 Handler(`C01Handler.cs` .. `C09Handler.cs`)

每个只有 ~10 行,声明 `DrawingType` 和 `SeedDrawingNumber`:

```csharp
internal sealed class C04Handler : DrawingTypeHandlerBase<C04>
{
    public override string DrawingType => "C04";
    public override int SeedDrawingNumber => 17000;
}
```

### 5.4 `DrawingNumberAllocator.cs`

**图号分配器**,最关键的并发组件。

**接口**: `IDrawingNumberAllocator.AllocateAsync(drawingType, ct) → int`

**实现**:
1. 单条 SQL:`UPDATE DrawingNumberSequence SET CurrentMax = CurrentMax + 1 OUTPUT inserted.CurrentMax WHERE DrawingType = @type`
2. SQL Server 行锁保证并发安全(同 drawingType 串行,不同 drawingType 并行)
3. 首次调用某 type → 用 `SeedDrawingNumber + 1` 兜底初始化

### 5.5 `OrderLockProvider.cs`

进程内按 OrderId 维度的互斥锁(`ConcurrentDictionary<int, SemaphoreSlim>`)。防止同订单并发插入重名。

**限制**:只在单 Web 实例内有效。多实例部署需切换到 SERIALIZABLE 事务。

### 5.6 `DrawingNumberService.cs`

**图号 Service,主要业务入口**。所有页面调用的核心服务。

| 方法 | 用途 |
|---|---|
| `GetAllDrawingNamesAsync()` | 取所有图名(图名管理 + 添加图号页) |
| `GetAddedDrawingNumberViewModelsByOrderIdAsync(orderId)` | 取某订单所有图号 ViewModel(订单图号列表页) |
| `GetAddedDrawingNumbersByOrderIdAsync(orderId)` | 取某订单所有 C0XBase 实体 |
| `AddDrawingNumbersAsync(orderId, names[], ct)` | 批量添加图号(返回 DrawingNumberInsertResult) |
| `AddSingleDrawingNumberCoreAsync(...)` | 单条插入核心(供 AddDrawingNumbersAsync 内部调用) |
| `DrawingNumber2DrawingNumberViewModelAsync(c0x, drawingType)` | C0XBase → ViewModel |
| `ToViewModel(c0x, orderNumber, drawingType)` | 纯转换,不查库 |
| `FindDrawingNumberByIdAsync(id, drawingType)` | 按 Id 查一条 C0X |
| `UpdateCX0DrawingNumberByIdAsync(c0x, drawingType)` | 改 DrawName |
| `FindDrawingNumberByTypeAndNumberAsync(type, number)` | 按 drawingType + DrawNumber 查(ViewModel) |
| `GetAllDrawingNamesIncludingDeletedAsync()` | 取所有图名(含已删除) |
| `GetDistinctPartsAsync()` | 取所有不重复的 Part 类别 |
| `AddDrawingNameAsync(name, part)` | 添加图名 |
| `DeleteDrawingNameAsync(id)` | 软删除图名 |

### 5.7 `DrawingNumberInsertResult.cs`

批量插入结果 DTO:
- `Success: List<string>` — 成功图名
- `Failures: List<DrawingNumberFailure>` — 每条失败原因
- `AllSucceeded` / `NoneSucceeded` — 便捷判断
- `DrawingNumberFailure(Name, Reason)` — record 类型

### 5.8 `OrdersService.cs`

订单 Service。
- `QueryAsync(options)` — 列表查询
- `GetByIdAsync(orderId)` — 按 Id 查询
- `SearchAsync(options, orderNumberKw, createdByKw, reactorFilter)` — 多条件搜索
- `SaveAsync(order, changedType)` — 保存(Add/Update),SERIALIZABLE 事务防重号
- `SoftDeleteAsync(orders)` — 批量软删除
- `HasDrawingNumbersAsync(orderId)` — 服务端权威检查
- `PreloadHasDrawingNumberCacheAsync(orders)` — 批量预热缓存(供 Orders 页面"编辑/删除"按钮禁用判断)
- `BatchQueryExistingDrawingOrderIdsAsync(drawingType, orderIds)` — 按 drawingType 批量查
- `TryGetHasDrawingNumber(orderId, out has)` / `InvalidateDrawingNumberCache()` — 缓存读写

### 5.9 `UsersService.cs`

用户 Service。
- `QueryAsync(options)` — 列表查询
- `NewUserAsync()` — 创建空对象(供弹窗用)
- `SaveAsync(user, rawPassword, changedType)` — 保存(新增时传密码,编辑时不传)
- `SoftDeleteAsync(users)` — 批量软删除(`IsDelete=true`)
- `ChangePasswordAsync(userId, oldPassword, newPassword)` — 改密(Identity 标准 ChangePasswordAsync)

---

## 6. Controllers

### `Controllers/AccountController.cs`

标准 MVC Controller,处理登录/登出表单提交(不走 Blazor):

- `POST /Account/Login` — 接收 WorkerNumber + Password + ReturnUrl,调用 `SignInManager.PasswordSignInAsync`,失败 5 次锁定
- `POST /Account/Logout` — `[Authorize]`,调用 `SignInManager.SignOutAsync`

---

## 7. 关键数据流详解

### 7.1 登录流程

```
用户访问任意页面
   ↓
[Authorize] 拦截
   ↓ (未登录)
重定向到 /Account/Login?returnUrl=...
   ↓
用户提交表单
   ↓
AccountController.Login (POST)
   ↓
SignInManager.PasswordSignInAsync
   ↓
Identity 标准:SqlSugarUserStore.FindByName → PasswordSignIn
   ↓
成功 → 设置 Cookie → 重定向到 ReturnUrl
   ↓
Blazor 应用启动 → CustomUserClaimsPrincipalFactory(实际是 DisplayNameClaimsFactory)
   ↓ (注入额外 claim: DisplayName / GID / Roles)
MainLayout 加载用户信息(显示头像)
```

### 7.2 添加图号流程(以"添加图号"页为例)

```
用户进入 /add-drawing-numbers/{OrderId}
   ↓
页面加载:OnInitializedAsync
   ↓ 并发:
   - OrdersService.GetByIdAsync(OrderId)
   - DrawingNumberService.GetAllDrawingNamesAsync()
   - DrawingNumberService.GetAddedDrawingNumbersByOrderIdAsync(OrderId)
   ↓
按 Part 分组渲染 checkbox 列表
   ↓
用户勾选 + 点击"添加"
   ↓
OrderLockProvider.AcquireAsync(OrderId)  ← 同订单互斥
   ↓
foreach drawingName:
   - 已存在? → 失败
   - 调用 IDrawingNumberAllocator.AllocateAsync(drawingType) → 原子 DrawNumber
   - handler.CreateNewEntity + 设 DrawNumber + handler.InsertAsync
   ↓
OrderLockProvider.Release()
   ↓
返回 DrawingNumberInsertResult
   ↓
页面显示结果弹窗(成功/失败明细)
```

### 7.3 查询图号流程

```
用户输入 C04 + 17 + 001
   ↓
校验格式(正则)
   ↓
合并成 17001(int)
   ↓
DrawingNumberService.FindDrawingNumberByTypeAndNumberAsync("C04", 17001)
   ↓
registry.Resolve("C04") → C04Handler
   ↓
handler.FindByDrawNumberAsync(_db, 17001)
   ↓
SELECT TOP 1 ... FROM C04 WHERE DrawNumber = 17001 AND IsDelete = 0
   ↓
(若是占位行 IsDelete=1,自动过滤)
   ↓
DrawingNumber2DrawingNumberViewModelAsync(c0x, "C04")
   ↓
构造 DrawingTypeNumber = "C04 17 001"
   ↓
页面弹窗显示完整信息
```

---

## 8. 关键设计原则

| 原则 | 实现 |
|---|---|
| **图号不可重复** | `IX_C0X_DrawNumber` UNIQUE 索引兜底 |
| **图号原子分配** | 单条 SQL `UPDATE...OUTPUT`,数据库行锁保证 |
| **同订单串行** | `OrderLockProvider` 进程内互斥(单实例) |
| **业务主键与 PK 分离** | `DrawNumber` 持久化字段,`Id` 只作物理 PK |
| **软删除** | `IsDelete` / `Isdelete` / `IsDeleted` 标志位,不真删 |
| **占位行** | C04/C07 Id=1..N 跳号被占位行填满(`IsDelete=true`),保证图号连续 |
| **NLog 全量接管** | 所有 `_logger.LogXxx` 走 NLog(写 DB `Logs` 表) |

---

## 9. 故障排查速查

### 9.1 启动问题

| 症状 | 排查 |
|---|---|
| 启动报"连接失败" | 检查 `appsettings.json` 的连接字符串,确认 SQL Server 可达 |
| 报"表不存在" | 跑 DbMigrator: `dotnet run --project BootstrapBlazor.DbMigrator -- --schema` |
| 报"Login failed for user" | 确认连接字符串里的账号密码 |
| 端口 8373 被占用 | 改 `appsettings.json` 的 `Urls` 字段 |

### 9.2 登录问题

| 症状 | 排查 |
|---|---|
| 提示"账号已锁定" | 等 5 分钟,或 SSMS 跑 `UPDATE Users SET LockoutEnd=NULL, AccessFailedCount=0 WHERE UserName='1'` |
| 提示"员工号或密码错误" | 确认 `WorkerNumber=1` 对应 `UserName='1'`(Users.sql 第 1 行) |
| 登录后菜单显示"审计日志" | 当前已移除此菜单,不应再出现;若出现说明 MainLayout 未更新 |
| 登录后看不到任何菜单 | 检查 `MainLayout.GetIconSideMenuItems()`,确认登录用户 DisplayName/Name 字段有值 |

### 9.3 业务功能问题

#### 添加图号失败

1. **提示"订单不存在"**:订单被删除,刷新订单列表
2. **提示"订单类型无法识别"**:订单 Power=0 或 IsReactor 状态异常,编辑订单修正
3. **提示"已添加过该图号"**:订单下已有同名图号,刷新"订单图号列表"确认
4. **图号分配失败**:NLog 看异常,通常是 `UNIQUE` 冲突(序列与已有数据不一致,跑 DbMigrator `--migrate` 重新同步)
5. **同一订单并发添加**:被 `OrderLockProvider` 阻塞(等待,不会失败)

#### 修改图号失败

1. **找不到图号**:检查 Id/DrawingType 路由参数;占位行(`IsDelete=1`)被过滤
2. **修改后页面空白**:刷新列表页;检查浏览器 console 错误

#### 查询图号失败

1. **"查无此号"**:图号在数据库里不存在,或被占位行占用;用"订单图号列表"反查
2. **弹窗不显示**:可能 NLog 抛异常但被 catch,看 `find-drawing-number` 的 ILogger 输出

#### 图名列表问题

1. **看不到图名**:检查 `DrawingNames.Isdelete` 字段(默认 false),确认数据库连接
2. **添加失败**:"图纸名称不能为空"/"图名类别不能为空"/"添加失败请重试"
3. **Part 下拉没新加的**:刷新页面(`/drawing-names` 重新进入,触发 `LoadPartOptionsAsync`)

### 9.4 数据库问题

```sql
-- 看 DrawingNumberSequence 状态(决定下次分配的图号)
SELECT * FROM DrawingNumberSequence ORDER BY DrawingType;

-- 看每张 C0X 表的图号范围
SELECT 'C04' AS [表], MIN(DrawNumber) AS 最小, MAX(DrawNumber) AS 最大, COUNT(*) AS 总数 FROM C04
UNION ALL SELECT 'C05', MIN(DrawNumber), MAX(DrawNumber), COUNT(*) FROM C05;

-- 看占位行数量(不应过多,否则图号显示有"空洞")
SELECT 'C04' AS [表], SUM(CASE WHEN IsDelete=1 THEN 1 ELSE 0 END) AS 占位行数 FROM C04;

-- 找具体图号
SELECT * FROM C04 WHERE DrawNumber = 17001;

-- 看操作日志(系统异常 / 启动)
SELECT TOP 50 Logged, Level, Message FROM Logs ORDER BY Logged DESC;
```

### 9.5 性能问题

| 症状 | 排查 |
|---|---|
| 添加图号慢 | `OrderLockProvider` 同订单阻塞(等),不应超过 1s;看 NLog `Application/Performance` |
| 订单列表首屏慢 | `OnQueryAsync` 调 `PreloadHasDrawingNumberCacheAsync`,正常 < 200ms |
| NLog 写 DB 慢 | 异步 + 文件兜底,DB 慢时降级到文件;看 `NLog.config` `queueLimit=10000` |

---

## 10. 修改代码前必读

### 10.1 改图号分配逻辑 → 改 `DrawingNumberAllocator.cs`

### 10.2 改 9 张 C0X 表 schema → 改 `C0XBase.cs`(9 个子类自动同步)

### 10.3 加新页面 → 在 `Components/Pages/` 加 `.razor`,主布局会自动出现在菜单

### 10.4 改用户字段 → 改 `Models/Users.cs`(继承 `IdentityUser<int>`,已 override 所有字段)

### 10.5 加新的图表类型(比如 C10) →

1. `Models/` 加 `C10.cs`(继承 `C0XBase`)
2. `Services/DrawingTypes/` 加 `C10Handler.cs`
3. `Services/DrawingTypes/DrawingTypeRegistry.cs` 构造函数加 `new C10Handler()`
4. DbMigrator 加 `SafeCreate<C10>(db, "C10")` + EnsureUniqueIndex + MigrateDrawingNumbersAsync
5. SQL 加 `IX_C10_DrawNumber` UNIQUE 索引(可选 DbMigrator 自动加)

### 10.6 改 Identity 行为 → 改 `Program.cs` 的 `AddIdentityCore<Users>(...)` options

---

## 11. 相关项目

- **`BootstrapBlazor.DbMigrator/`** — 数据迁移工具,见 `BootstrapBlazor.DbMigrator/README.md`
- **数据迁移命令**(PowerShell / CMD 兼容):见 DbMigrator README 顶部"快速参考"

---

## 12. 常见任务速查

### 12.1 重置管理员密码(忘记密码时)

```sql
-- 把管理员密码改成 Admin@123(等于重新跑 seed)
DELETE FROM Users WHERE UserName = '1';
-- 然后跑 DbMigrator --seed 重建
```

或用 Identity 标准方式(SSMS):

```sql
-- 用任意密码生成工具生成 PBKDF2 hash,然后:
UPDATE Users SET PasswordHash = '你的PBKDF2hash', SecurityStamp = NEWID() WHERE UserName = '1';
```

### 12.2 新增角色

当前代码**未实现角色管理 UI**。需要时:
1. SSMS 跑 `INSERT INTO Roles (Name, NormalizedName, ConcurrencyStamp) VALUES ('Xxx', 'XXX', NEWID())`
2. 关联: `INSERT INTO UserRoles (UserId, RoleId) SELECT u.Id, r.Id FROM Users u, Roles r WHERE u.UserName='X' AND r.NormalizedName='XXX'`
3. 添加 `[Authorize(Roles = "Xxx")]` 到目标 Razor 页面
4. 代码中用 `User.IsInRole("Xxx")` 判断

### 12.3 备份数据库

```sql
BACKUP DATABASE stclmd TO DISK = 'D:\backup\stclmd_20260101.bak' WITH FORMAT;
```

---

## 13. 版本历史

| 版本 | 说明 |
|---|---|
| 2.0.0 | 当前版本 — 解耦 DrawNumber、删除审计日志、Identity 完整 IUserRoleStore、补全 Email/Phone 迁移 |

---

## 14. 联系 / 反馈

数据库 / 部署问题:看 `BootstrapBlazor.DbMigrator/README.md` 和本文档"故障排查"章节。

业务逻辑 / 代码问题:看对应页面的 `.razor` 文件(每页都有详细注释)和本文档"页面清单"。