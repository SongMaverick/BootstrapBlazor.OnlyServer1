# AddDrawingNumbers 页面 UX 决策(2026-07-07)

本文档记录 `/add-drawing-numbers/{orderId}` 页面在 2026-07-07 多轮迭代的设计决策,**给未来的维护者看**,避免类似轮子重做或破坏。

---

## 背景

操作员日常任务:给一个订单添加多个图号(如套管、线圈、铁芯等)。
最初页面只有"一行 2 Part"的简陋布局:

```razor
<div class="col-md-6">  <!-- 固定 2 列 -->
    @* Part 卡片头 + body 都常驻显示 *@
</div>
```

用户反馈:"**信息密度太低,加几个图号要滚动半天**"。

---

## 三轮迭代

### 迭代 1 — 响应式列数(2026-07-07)

**改前**: `col-md-6`(每行 2 卡)—— 在大显示器上浪费横向空间。
**改后**: `col-12 col-sm-6 col-lg-4 col-xl-3`(响应式 1/2/3/4 列)。

```razor
@*
    响应式列数(解决"两列内容太多要滚动"的问题):
        手机(<576px)        :1 列  col-12
        平板竖屏(576-768)  :2 列  col-sm-6
        笔记本(768-1200)   :3 列  col-lg-4
        大屏显示器(≥1200)  :4 列  col-xl-3
*@
<div class="col-12 col-sm-6 col-lg-4 col-xl-3">
    <div class="card mb-3 h-100 shadow-sm">@* 等高 + 阴影 *@
        ...
    </div>
</div>
```

**权衡**:4 列在大屏够用,但单 Part 的图名多了还是要滚动(每个卡片 body 内只是单纯垂直列表)。

---

### 迭代 2 — Part 卡片可折叠 + 已选 badge

**问题**:即使 4 列,一个 Part 下的图名有 10+ 个,body 仍然滚动。
**改后**:Part 卡片支持折叠,默认全折叠。

```razor
<div class="card mb-3 shadow-sm">
    <div class="card-header @style.HeaderClass @style.HeaderTextClass user-select-none"
         role="button"
         @onclick="() => ToggleCollapse(part)"
         style="cursor: pointer;">
        <div class="d-flex justify-content-between align-items-center">
            <h5 class="mb-0">@GetPartTitle(part)</h5>
            <div class="d-flex align-items-center small">
                @* 已选数 badge — 折叠/展开状态都可见 *@
                <span class="badge @(selectedCount > 0 ? "bg-warning text-dark" : "bg-light text-dark") me-2">
                    @if (selectedCount > 0) {
                        <i class="fa-solid fa-check me-1"></i>
                    }
                    已选 @selectedCount
                </span>
                <i class="fa-solid @(isCollapsed ? "fa-chevron-right" : "fa-chevron-down")"></i>
            </div>
        </div>
    </div>
    @if (!isCollapsed) {
        <div class="card-body">
            @* 图名列表 *@
        </div>
    }
</div>
```

**ToolBar 加单按钮**:`⇕ 全部展开 / 全部折叠`(按钮文案根据 `AllCollapsed` state 切换)。

**状态管理**:HashSet `string` `_collapsedParts`,默认填所有 Part = 全折叠。点切换 / 一键开关。
**重要**:不用 localStorage 记住上次状态(用户选择"默认全折叠")—— 每次进页面都全折叠,可预测。

---

### 迭代 3 — 图名水平多列

**问题**:某个 Part 展开后,body 内还是单列 checkbox 列表,多图名时滚动。
**改后**:图名水平 grid。

```razor
<div class="row g-2">
    @foreach (var item in items) {
        <div class="col-12 col-sm-6 col-md-4 col-lg-3 col-xl-2">
            <div class="form-check mb-2">
                <input class="form-check-input" type="checkbox" ... />
                <label class="form-check-label" ...>@item.drawingName.ComponentName</label>
            </div>
        </div>
    }
</div>
```

**响应式断点**(同 Part 卡):

| 屏幕宽度 | 图名列数 |
|---|---|
| 手机(< 576px) | 1 列 |
| 平板竖屏(576-768) | 2 列 |
| 中屏(768-992) | 3 列 |
| 标准笔记本(992-1200) | 4 列 |
| **大屏显示器(≥1200)** | **6 列** |

**单一 Part 10 个图名**:大显示器只需要 2 行。

---

### 迭代 4 — Part 同色系浅背景(2026-07-07)

**问题**:Part 卡 header 是黄色(ESL-01)、绿色(ESL-02)等饱和色,但展开后的 body 是白底,**色感断裂**。
**改后**:body 用 header 的同色系浅色 hex,让"色感延续"。

```csharp
// CardStyle 扩展:加 BodyBg 字段,header 同色系浅色
private record CardStyle(string HeaderClass, string HeaderTextClass, string BodyBg);

// bg-warning → 浅黄 #fff3cd 等映射(8 个 Bootstrap 主题色对应浅色 hex)
private static string LightBgFor(string bg) => bg switch
{
    "bg-warning"   => "#fff3cd",   // 淡黄
    "bg-success"   => "#d1e7dd",   // 淡绿
    "bg-info"      => "#cff4fc",   // 淡青
    "bg-secondary" => "#e2e3e5",   // 淡灰
    "bg-primary"   => "#cfe2ff",   // 淡蓝
    "bg-danger"    => "#f8d7da",   // 淡红
    "bg-dark"      => "#d3d3d4",   // 浅深灰
    "bg-light"     => "#fefefe",
    _              => "#f8f9fa"
};
```

```razor
<div class="card-body" style="background-color: @style.BodyBg;">
```

**为什么不直接用 `bg-warning-subtle`?** Bootstrap 5.3+ 才有 subtle 类,BootstrapBlazor 8.x 锁的具体版本未验证。Inline hex 在任何版本下都一致。

---

## 最终形态(代码片段保存)

```razor
@* 卡片外层 — 单 Part 一行,响应式列数变 1 列 (迭代 1+2) *@
<div class="col-12">
    <div class="card mb-3 shadow-sm">
        <div class="card-header @style.HeaderClass @style.HeaderTextClass user-select-none"
             role="button"
             @onclick="() => ToggleCollapse(part)"
             style="cursor: pointer;">
            @* 标题 + 已选 badge + 折叠箭头 *@
        </div>
        @if (!isCollapsed) {
            <div class="card-body" style="background-color: @style.BodyBg;">
                <div class="row g-2">
                    @foreach (var item in items) {
                        <div class="col-12 col-sm-6 col-md-4 col-lg-3 col-xl-2">
                            <div class="form-check mb-2">@* 图名水平多列 *@</div>
                        </div>
                    }
                </div>
            </div>
        }
    </div>
</div>
```

---

## 关键决策表(供未来评估)

| 决策 | 选择 | 拒绝的方案 | 为什么 |
|---|---|---|---|
| 默认折叠 | 全折叠 | 全部展开 / 记住状态 / 部分折叠 | 用户明确选"看 4 标题就够了" |
| 一键开关按钮 | 单按钮(文案切换) | 双按钮 | 减少视觉噪音 |
| 列数断点 | 1/2/3/4/6 响应式 | 固定 4 / 固定 6 | 适配手机 → 大屏 |
| 卡片背景 | inline hex 浅色 | `bg-*-subtle` Bootstrap 类 | 不依赖特定 Bootstrap 版本 |
| 色彩数量 | 8 色循环 | HSL 生成 360 色 | 现有 4 个 fixed + 8 种 hash 循环,撞色风险 Part 数 ≪ 8 时可忽略 |
| 排序优先级 | ESL-01/02/03/Core 在前,其余字典序 | 纯字典序 | 业务上 4 个老 Part 使用频率最高 |

---

## 未来扩展点

如果将来有需求,可以在这基础上加:

1. **搜索过滤**(顶部加搜索框,过滤 `_partGroups`)
2. **每 Part 全选 / 反选快捷**(在 header 加 checkbox 二态切换)
3. **持久化折叠状态**(localStorage,记得上次点开的 Part)
4. **拖拽多选**(左右两栏拖拽,从"可选"到"已选")
5. **图名图片预览**(每图名附小图标或图片占位)
6. **节流渲染**(Part 数 > 30 时虚拟滚动)
7. **HSL 色彩**(Part > 10 时切换,任意不撞)

---

## 关联文件

| 文件 | 内容 |
|---|---|
| `Components/Pages/AddDrawingNumbers.razor` | 本文档描述的页面 |
| `Services/DrawingNumbers/C0XWriteService.cs` | 业务逻辑层(被这页面调) |
| `Services/DrawingNames/DrawingNameService.cs` | 图名数据来源 |
| `Services/Identity/CurrentUserAccessor.cs` | H1 注入的权限校验入口 |
