# 0003 - 全项目统一 file-scoped namespace

## 状态

已实施 (2026-07-05)

## 背景

项目早期文件使用旧式 namespace 块:

```csharp
namespace BootstrapBlazor.OnlyServer1.Models
{
    public class C01 : C0XBase
    {
    }
}
```

后期新建文件开始用 file-scoped namespace(C# 10+):

```csharp
namespace BootstrapBlazor.OnlyServer1.Models;

public class C01 : C0XBase
{
}
```

两种风格并存,代码风格不一致。

## 评估过的方案

### 方案 A: 维持现状
- 优点: 无改动
- 缺点: 风格不一致;后续代码贡献者无所适从

### 方案 B: 全项目统一为 file-scoped ✅
- 优点: 现代风格,少一层缩进,文件更扁平;`.NET 6+` SDK 默认支持
- 缺点: 一次性改动 13 个 Models 文件,review 噪音

### 方案 C: 全项目回退到旧式 namespace
- 不推荐,与现代 C# 趋势背道而驰

## 决策

选 **方案 B**。

理由:
1. C# 10+ 是当前 SDK 的默认能力(项目 `TargetFramework=net8.0`)
2. 减少一行 `namespace Foo { }` 包裹,每文件少 3 行噪音
3. 不再支持"一个文件多 namespace"(本项目没这种用法)

## 后果

### 正面
- 13 个 Models 文件统一风格
- 与项目其他目录(Identity / BusinessRules / ViewModels / Services)早已统一

### 负面
- 一次性 PR 涉及 13 个文件,但内容改动很小,review 简单

### 改动文件
- `Models/C01.cs` ~ `C09.cs`(9 个)
- `Models/C0XBase.cs`, `Users.cs`, `Roles.cs`
- `Models/DrawingNames.cs`, `Orders.cs`, `OrdersItem.cs`
- `Models/Departments.cs`, `Logs.cs`
- `Models/DrawingNumberSequence.cs`, `DrawingNumberViewModel.cs`
- `Models/Permissions.cs`, `RolePermissions.cs`

## 相关

- PR: "统一 file-scoped namespace"
- 后续新文件必须用 file-scoped 风格(在 CONTRIBUTING.md 写明)