using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.ViewModels;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 「订单管理」Service:结构设计订单明细(OrderMDDetail)的读写。
    ///
    /// 列表以 Orders 为主体 LEFT JOIN OrderMDDetail —— 尚未填写明细的订单也要出现在列表里,
    /// 所以不能继承 BaseQueryService(它只支持单表 Queryable&lt;T&gt;)。
    /// 这里照 UsersService.QueryAsync 的 LeftJoin + 显式 Select 投影写法手写查询。
    ///
    /// 明细行不单独新增/删除订单:新增订单请走 /orders。本页的"保存"是 upsert
    /// (DetailId == 0 → INSERT,否则 UPDATE),"删除"是清空该订单的明细(软删)。
    ///
    /// Scoped 生命周期,与其他业务 Service 一致。
    /// </summary>
    public class OrderMDDetailService
    {
        private readonly ISqlSugarClient _db;
        private readonly CurrentUserAccessor _currentUser;
        private readonly ILogger<OrderMDDetailService> _logger;

        // (2026-07-07 Review #H3) OrderBy(string) 字符串拼接 SortName 防注入。
        // 值必须是下面 Select 投影里出现过的列名;不在集合内的 SortName 静默忽略,回退默认排序。
        private static readonly HashSet<string> _allowedSortCols =
            new(StringComparer.OrdinalIgnoreCase)
            {
                nameof(OrderMDDetailRow.OrderId),
                nameof(OrderMDDetailRow.OrderNumber),
                nameof(OrderMDDetailRow.Power),
                nameof(OrderMDDetailRow.RatedVoltage),
                nameof(OrderMDDetailRow.ProjectName),
                nameof(OrderMDDetailRow.MarketType),
                nameof(OrderMDDetailRow.UnitCount),
                nameof(OrderMDDetailRow.PrimarySubmitDate),
                nameof(OrderMDDetailRow.PrimaryConfirmDate),
                nameof(OrderMDDetailRow.SecondarySubmitDate),
                nameof(OrderMDDetailRow.SecondaryConfirmDate),
            };

        public OrderMDDetailService(
            ISqlSugarClient db,
            CurrentUserAccessor currentUser,
            ILogger<OrderMDDetailService> logger)
        {
            _db = db;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ===== 查询 =====

        /// <summary>
        /// 分页查询订单明细列表。Orders 为主表,LEFT JOIN 未删除的 OrderMDDetail
        /// 以及两次 LEFT JOIN Users 取一次/二次负责人姓名。
        /// </summary>
        public Task<PagedResult<OrderMDDetailRow>> QueryAsync(PagedQuery options)
        {
            try
            {
                // 注意 join 条件里带上 !d.IsDeleted:软删的明细不能被 join 出来,
                // 否则清空明细后该行仍显示旧数据。
                var query = _db.Queryable<Orders>()
                    .LeftJoin<OrderMDDetail>((o, d) => o.Id == d.OrderId && !d.IsDeleted)
                    .LeftJoin<Users>((o, d, p) => d.PrimaryEngineerId == p.Id)
                    .LeftJoin<Users>((o, d, p, s) => d.SecondaryEngineerId == s.Id)
                    .Where(o => !o.IsDeleted);

                var kw = options.SearchText;
                if (!string.IsNullOrEmpty(kw))
                {
                    query = query.Where((o, d, p, s) =>
                        o.OrderNumber.ToString().Contains(kw)
                        || (d.ProjectName != null && d.ProjectName.Contains(kw))
                        || (p.Name != null && p.Name.Contains(kw))
                        || (s.Name != null && s.Name.Contains(kw)));
                }

                // (2026-08-13) 日期范围筛选:任一为空表示该侧不限制
                //  End 用 AddDays(1) 排他比较 — 包含当天整天(用户选 2026-08-13 = 当天所有时分秒都算)
                //  ⚠ 应用 LEFT JOIN,无明细行(DetailId=0)的订单 d.PrimarySubmitDate 为 NULL,
                //     一旦加 WHERE 就会被排除 — 这是预期行为(筛选"已提交"才合语义)
                if (options.PrimarySubmitStart.HasValue)
                    query = query.Where((o, d) => d.PrimarySubmitDate >= options.PrimarySubmitStart!.Value);
                if (options.PrimarySubmitEnd.HasValue)
                    query = query.Where((o, d) => d.PrimarySubmitDate < options.PrimarySubmitEnd!.Value.AddDays(1));
                if (options.SecondarySubmitStart.HasValue)
                    query = query.Where((o, d) => d.SecondarySubmitDate >= options.SecondarySubmitStart!.Value);
                if (options.SecondarySubmitEnd.HasValue)
                    query = query.Where((o, d) => d.SecondarySubmitDate < options.SecondarySubmitEnd!.Value.AddDays(1));

                if (!string.IsNullOrEmpty(options.SortName) && _allowedSortCols.Contains(options.SortName))
                {
                    query = options.SortOrder == SortDirection.Asc
                        ? query.OrderBy($"{options.SortName} asc")
                        : query.OrderBy($"{options.SortName} desc");
                }
                else
                {
                    // (2026-08-13) 默认排序:一次图纸提交时间降序(新提交在前),NULL 排在最后(SQL Server DESC 默认行为)
                    //    tie-breaker:订单号倒序,新单在前
                    query = query.OrderBy($"{nameof(OrderMDDetailRow.PrimarySubmitDate)} desc")
                                 .OrderBy($"{nameof(OrderMDDetailRow.OrderNumber)} desc");
                }

                var total = query.Count();
                var items = query
                    .Select((o, d, p, s) => new OrderMDDetailRow
                    {
                        DetailId = d.Id,
                        OrderId = o.Id,
                        ProjectName = d.ProjectName,
                        MarketType = d.MarketType,
                        UnitCount = d.UnitCount,
                        PrimaryEngineerId = d.PrimaryEngineerId,
                        SecondaryEngineerId = d.SecondaryEngineerId,
                        PrimarySubmitDate = d.PrimarySubmitDate,
                        PrimaryConfirmDate = d.PrimaryConfirmDate,
                        DesignTool = d.DesignTool,
                        SecondarySubmitDate = d.SecondarySubmitDate,
                        SecondaryConfirmDate = d.SecondaryConfirmDate,
                        IsSplitFrame = d.IsSplitFrame,
                        TestBushingModel = d.TestBushingModel,
                        TestBushingQuantity = d.TestBushingQuantity,
                        SwitchCount = d.SwitchCount,
                        IsWeldedTankRim = d.IsWeldedTankRim,
                        OrderNumber = o.OrderNumber,
                        Power = o.Power,
                        RatedVoltage = o.RatedVoltage,
                        IsReactor = o.IsReactor,
                        PrimaryEngineerName = p.Name,
                        SecondaryEngineerName = s.Name,
                    })
                    .Skip((options.PageIndex - 1) * options.PageItems)
                    .Take(options.PageItems)
                    .ToList();

                // 没有明细行时 UnitCount 会被 join 成 0,展示上按 1 台处理
                foreach (var item in items)
                {
                    if (item.DetailId == 0 && item.UnitCount == 0) item.UnitCount = 1;
                }

                return Task.FromResult(new PagedResult<OrderMDDetailRow>
                {
                    Items = items,
                    TotalCount = total,
                    IsFiltered = options.FilterCount > 0,
                    IsSorted = !string.IsNullOrEmpty(options.SortName),
                    IsSearch = !string.IsNullOrEmpty(options.SearchText) || options.HasSearchModel,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying OrderMDDetail");
                return Task.FromResult(new PagedResult<OrderMDDetailRow>());
            }
        }

        // ===== 保存(upsert) =====

        /// <summary>
        /// 保存订单明细。返回 (ok, error):ok=false 时 error 是用户可读的中文消息。
        /// DetailId == 0 → INSERT 一条新明细;否则 UPDATE 现有明细。
        /// </summary>
        public async Task<(bool ok, string? error)> SaveAsync(OrderMDDetailRow row)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):
            // Razor 层 [Authorize] 只挡外面;Service 必须自己也挡。
            // 防:新 Controller / 任务调度 / 测试代码直接调 Service 绕过 Razor。
            // 不删这段校验、不删这个注释。
            // 规则:Admin 与 Operator 均可维护订单明细(用户 2026-08-10 明确要求)。
            // ====================================================================
            var (authOk, authErr) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
            if (!authOk)
            {
                _logger.LogWarning(
                    "OrderMDDetailService.SaveAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), authErr);
                return (false, authErr);
            }

            if (row.OrderId <= 0)
            {
                return (false, "订单不存在,无法保存明细");
            }

            var (validOk, validErr) = Validate(row);
            if (!validOk) return (false, validErr);

            var operatorName = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
            var now = DateTime.Now;

            try
            {
                // 订单必须存在且未删除
                var orderExists = await _db.Queryable<Orders>()
                    .Where(o => o.Id == row.OrderId && !o.IsDeleted)
                    .AnyAsync();
                if (!orderExists)
                {
                    return (false, "订单不存在或已被删除,请刷新页面后重试");
                }

                if (row.DetailId <= 0)
                {
                    // 并发兜底:另一个会话可能刚给这个订单建了明细。
                    // 真正的唯一性由 IX_OrderMDDetail_OrderId (WHERE IsDeleted=0) 保证,
                    // 这里的预查只是为了给出更友好的提示。
                    var existing = await _db.Queryable<OrderMDDetail>()
                        .Where(d => d.OrderId == row.OrderId && !d.IsDeleted)
                        .FirstAsync();
                    if (existing != null)
                    {
                        return (false, "该订单的明细已被他人创建,请刷新页面后重试");
                    }

                    var entity = ToEntity(row, new OrderMDDetail());
                    entity.CreatedBy = operatorName;
                    entity.CreatedTime = now;
                    entity.UpdatedBy = operatorName;
                    entity.UpdatedTime = now;
                    entity.IsDeleted = false;

                    try
                    {
                        row.DetailId = await _db.Insertable(entity).ExecuteReturnIdentityAsync();
                    }
                    catch (Exception ex) when (IsDuplicateOrderMDDetailError(ex))
                    {
                        _logger.LogWarning("Duplicate OrderId on OrderMDDetail INSERT: {OrderId}", row.OrderId);
                        return (false, "该订单的明细已存在,请刷新页面后重试");
                    }

                    _logger.LogInformation("OrderMDDetail inserted: orderId={OrderId} detailId={DetailId}",
                        row.OrderId, row.DetailId);
                }
                else
                {
                    var entity = await _db.Queryable<OrderMDDetail>()
                        .Where(d => d.Id == row.DetailId && !d.IsDeleted)
                        .FirstAsync();
                    if (entity == null)
                    {
                        return (false, "明细不存在或已被删除,请刷新页面后重试");
                    }

                    ToEntity(row, entity);
                    entity.UpdatedBy = operatorName;
                    entity.UpdatedTime = now;

                    await _db.Updateable(entity).ExecuteCommandAsync();
                    _logger.LogInformation("OrderMDDetail updated: orderId={OrderId} detailId={DetailId}",
                        row.OrderId, row.DetailId);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving OrderMDDetail: orderId={OrderId}", row.OrderId);
                return (false, "保存失败,请重试");
            }
        }

        // ===== 清空明细内容(保留行) =====

        /// <summary>
        /// 清空所选订单的明细内容:业务字段回到初始状态,但**保留明细行本身**。
        ///
        /// 为什么不是软删行(2026-08-10 用户决策):
        ///   新建订单时会自动建一条空明细(OrdersService.TryCreateEmptyOrderDetailAsync),
        ///   前提是"每个订单都有一条明细行"。如果这里把行软删了,该订单就退回"无明细"状态,
        ///   与前提自相矛盾,而且下次编辑又会 INSERT 新行,Id 白白增长。
        ///   所以这里只清内容,行一直在,DetailId 保持不变。
        ///
        /// 订单本身完全不受影响。
        /// </summary>
        public async Task<bool> ClearAsync(IEnumerable<OrderMDDetailRow> rows)
        {
            // ====================================================================
            // 纵深防御(2026-07-07 Review #H1):不删这段校验、不删这个注释。
            // 清空明细是可恢复操作,Admin 与 Operator 均可执行。
            // ====================================================================
            var (ok, err) = _currentUser.RequireRole(RoleNames.Admin, RoleNames.Operator);
            if (!ok)
            {
                _logger.LogWarning(
                    "OrderMDDetailService.ClearAsync denied: callerId={CallerId} reason={Reason}",
                    _currentUser.GetUserId(), err);
                return false;
            }

            // DetailId == 0 的行还没有明细(存量订单未编辑过),没有内容可清
            var ids = rows.Where(r => r.DetailId > 0).Select(r => r.DetailId).Distinct().ToList();
            if (ids.Count == 0) return true;

            var operatorName = _currentUser.GetDisplayName() ?? _currentUser.GetUserName() ?? "";
            var now = DateTime.Now;

            try
            {
                // 只清业务字段;OrderId / 审计字段 / IsDeleted 保持不动
                await _db.Updateable<OrderMDDetail>()
                    .SetColumns(d => new OrderMDDetail
                    {
                        ProjectName = null,
                        MarketType = null,
                        UnitCount = 1,
                        PrimaryEngineerId = null,
                        SecondaryEngineerId = null,
                        PrimarySubmitDate = null,
                        PrimaryConfirmDate = null,
                        DesignTool = null,
                        SecondarySubmitDate = null,
                        SecondaryConfirmDate = null,
                        IsSplitFrame = false,
                        TestBushingModel = null,
                        TestBushingQuantity = null,
                        SwitchCount = null,
                        IsWeldedTankRim = false,
                        UpdatedBy = operatorName,
                        UpdatedTime = now,
                    })
                    .Where(d => ids.Contains(d.Id))
                    .ExecuteCommandAsync();

                _logger.LogInformation("OrderMDDetail cleared: detailIds={Ids}", string.Join(",", ids));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing OrderMDDetail");
                return false;
            }
        }

        // ===== 内部工具 =====

        /// <summary>
        /// 业务校验。放在 Service 而不是只靠 UI,理由同上面的纵深防御注释。
        /// </summary>
        private static (bool ok, string? error) Validate(OrderMDDetailRow row)
        {
            if (row.UnitCount <= 0)
            {
                return (false, "台数必须大于 0");
            }
            if (row.SwitchCount is < 0)
            {
                return (false, "开关数量不能为负数");
            }
            if (row.PrimarySubmitDate.HasValue && row.PrimaryConfirmDate.HasValue
                && row.PrimaryConfirmDate.Value < row.PrimarySubmitDate.Value)
            {
                return (false, "一次图纸确认日期不能早于提交日期");
            }
            if (row.SecondarySubmitDate.HasValue && row.SecondaryConfirmDate.HasValue
                && row.SecondaryConfirmDate.Value < row.SecondarySubmitDate.Value)
            {
                return (false, "二次图纸确认日期不能早于提交日期");
            }
            return (true, null);
        }

        /// <summary>
        /// 把 UI 行的业务字段拷进实体。审计字段由调用方设置,不在这里碰。
        /// </summary>
        private static OrderMDDetail ToEntity(OrderMDDetailRow row, OrderMDDetail entity)
        {
            entity.OrderId = row.OrderId;
            entity.ProjectName = row.ProjectName;
            entity.MarketType = row.MarketType;
            entity.UnitCount = row.UnitCount;
            entity.PrimaryEngineerId = row.PrimaryEngineerId;
            entity.SecondaryEngineerId = row.SecondaryEngineerId;
            entity.PrimarySubmitDate = row.PrimarySubmitDate;
            entity.PrimaryConfirmDate = row.PrimaryConfirmDate;
            entity.DesignTool = row.DesignTool;
            entity.SecondarySubmitDate = row.SecondarySubmitDate;
            entity.SecondaryConfirmDate = row.SecondaryConfirmDate;
            entity.IsSplitFrame = row.IsSplitFrame;
            entity.TestBushingModel = row.TestBushingModel;
            entity.TestBushingQuantity = row.TestBushingQuantity;
            entity.SwitchCount = row.SwitchCount;
            entity.IsWeldedTankRim = row.IsWeldedTankRim;
            return entity;
        }

        /// <summary>
        /// SQL Server 唯一索引冲突检测。做法与 OrdersService.IsDuplicateOrderNumberError 一致:
        /// 先看错误号 2627 / 2601,再用索引名和英文关键字兜底(防消息被本地化后号码匹配失效)。
        /// </summary>
        private static bool IsDuplicateOrderMDDetailError(Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("2627") || msg.Contains("2601")) return true;
            if (msg.Contains("IX_OrderMDDetail_OrderId", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("unique key", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}