# 0004 - 用 abstract C0XBase + 9 个 Handler 多态写入 C0X 表

## 状态

已实施 (v1.0)

## 背景

业务上有 9 张结构相同的图号表(C01~C09),按 Power 区间映射。问题是:

- 9 张表 schema 完全一致(都是 Id/DrawNumber/DrawName/OrderId/...)
- 业务上要根据 `Orders.Power` 决定写哪张表
- 不能用单表 + DrawingType 列(早期就是这么做的,后来为了 Power 区间拆分)

需求: 9 张表 + 多态写入 + 共享公共字段 + 各自建表。

## 评估过的方案

### 方案 A: 9 个独立实体类,各自 CRUD
- 优点: 直白
- 缺点: 9 份重复 CRUD 代码;Service 层 9 个 if/else

### 方案 B: 单表 + DrawingType 列 + WHERE 过滤
- 优点: 1 个实体类
- 缺点: 索引失效(每次查都得带 DrawingType);Power 区间增长时数据倾斜

### 方案 C: abstract C0XBase + 9 个子类 + 泛型 Handler 模板 ✅
- 优点: 9 个子类共享公共字段;`DrawingTypeHandlerBase<TEntity>` 泛型消除 CRUD 样板
- 缺点: 9 个空子类 + 9 个 Handler 注册;需要 `IDrawingTypeRegistry` 解析

## 决策

选 **方案 C**。

实现:
- `Models/C0XBase.cs` — abstract 基类,9 个公共字段
- `Models/C01.cs` ~ `C09.cs` — 9 个空子类(`public class C01 : C0XBase {}`)
- `Services/DrawingTypes/IDrawingTypeHandler.cs` — 9 个方法的契约
- `Services/DrawingTypes/DrawingTypeHandlerBase<TEntity>.cs` — 泛型 CRUD 模板
- `Services/DrawingTypes/C01Handler.cs` ~ `C09Handler.cs` — 9 个空 Handler(只声明 DrawingType 名)
- `Services/DrawingTypes/DrawingTypeRegistry.cs` — 9 个 Handler 静态注册 + Power 区间匹配

## 后果

### 正面
- 9 张表共享 schema,字段名 / 类型 / 索引统一维护
- Service 层 `handler.InsertAsync(c0x)` 不用 if/else
- SqlSugar Code First 不为 abstract 类建表,具体子类各建一张(自动)
- 泛型 Handler 9 行就实现了所有 CRUD

### 负面 / 风险
- 9 个空子类 + 9 个空 Handler 是"显式自描述"(类似手写依赖注入),有"样板感"
- 增删 C0X 需同时:加 Model 子类 + 加 Handler + 在 Registry 注册
- 泛型 `InsertAsync` 内的 `entity is not TEntity typed` 类型断言有少量运行时开销(微秒级,可忽略)

### 中和
- 9 个 Handler 是**显式自描述**:打开 `DrawingTypeRegistry.cs` 就能看到所有支持的类型,不用反射扫 attribute
- 新增 C0X 时,模板文件已有(可以复制 `C09Handler.cs` 改名 5 秒搞定)

## 相关

- `Services/DrawingTypes/` 全部代码
- `Services/DrawingNumbers/C0XWriteService.cs` 调用方
- `Models/C0XBase.cs` + 9 个空子类