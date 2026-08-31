using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Colors = QuestPDF.Helpers.Colors;

namespace BootstrapBlazor.OnlyServer1.Services.OrderManagement;

/// <summary>
/// (2026-08-27) 订单问题汇总 PDF 生成器。
///
/// 调用方:`Program.cs` 的最小 API `/api/orders/{orderId:int}/issues-pdf`。
/// 返回 byte[] 让浏览器保存为 PDF。
///
/// Schema:OrderIssues → 字段 Content / Resolution / Status / CreatedBy / CreatedTime / ClosedBy / ClosedTime
/// 用户要求 PDF 列:
///   - Id(只读,问题标识)
///   - 问题描述 = Content
///   - 解决办法 = Resolution(open 状态显示「—」)
///   - 创建人 = CreatedBy
///   - 添加人 = CreatedBy(用户确认与创建人重复)
/// </summary>
public class OrderIssuePdfService
{
    private readonly OrdersService _ordersService;
    private readonly OrderIssueService _orderIssueService;
    private readonly ILogger<OrderIssuePdfService> _logger;

    public OrderIssuePdfService(
        OrdersService ordersService,
        OrderIssueService orderIssueService,
        ILogger<OrderIssuePdfService> logger)
    {
        _ordersService = ordersService;
        _orderIssueService = orderIssueService;
        _logger = logger;
    }

    /// <summary>
    /// 生成该订单下所有问题的 PDF。返回 null 表示订单不存在或已删除。
    /// </summary>
    public async Task<byte[]?> GenerateOrderIssuesPdfAsync(int orderId)
    {
        var order = await _ordersService.FindByIdAsync(orderId);
        if (order == null)
        {
            _logger.LogWarning("OrderIssuePdfService: 订单 {OrderId} 不存在或已删除", orderId);
            return null;
        }

        // 一次性拉全量;单订单问题数通常 < 100,PageItems=10000 兜底
        var issuesResult = await _orderIssueService.QueryByOrderIdAsync(orderId,
            new PagedQuery { PageIndex = 1, PageItems = 10000 });
        var issues = issuesResult.Items ?? new List<OrderIssues>();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                // Windows 系统字体「微软雅黑」;Server 2016+ 默认安装。
                // 若部署到 Linux 需改嵌入 Noto Sans CJK SC TTF,见 plan 风险点 #2
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily("Microsoft YaHei"));

                page.Header().Element(c => ComposeHeader(c, order));
                page.Content().Element(c => ComposeContent(c, issues));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8));
                    t.Span("第 ");
                    t.CurrentPageNumber();
                    t.Span(" 页 / 共 ");
                    t.TotalPages();
                    t.Span(" 页");
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, Orders order)
    {
        container.Column(col =>
        {
            col.Item().Text("问题汇总报告")
                .FontSize(16).Bold();

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"订单号: {order.OrderNumber}");
                row.RelativeItem().Text($"容量: {order.Power} kVA");
                row.RelativeItem().Text($"额定电压: {order.RatedVoltage} kV");
            });

            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Text($"类型: {(order.IsReactor ? "电抗器" : "变压器")}");
                row.RelativeItem().Text($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm}");
                row.RelativeItem().Text($"创建者: {order.CreatedBy}");
            });

            col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
        });
    }

    private static void ComposeContent(IContainer container, List<OrderIssues> issues)
    {
        container.PaddingTop(8).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(35);  // Id
                c.RelativeColumn(3);   // 问题描述
                c.RelativeColumn(3);   // 解决办法
                c.ConstantColumn(70);  // 创建人
                c.ConstantColumn(70);  // 添加人
            });

            // 表头
            table.Header(h =>
            {
                h.Cell().Element(HeaderCellStyle).Text("Id").Bold();
                h.Cell().Element(HeaderCellStyle).Text("问题描述").Bold();
                h.Cell().Element(HeaderCellStyle).Text("解决办法").Bold();
                h.Cell().Element(HeaderCellStyle).Text("创建人").Bold();
                h.Cell().Element(HeaderCellStyle).Text("添加人").Bold();
            });

            if (issues.Count == 0)
            {
                table.Cell().ColumnSpan(5).Element(EmptyCellStyle)
                    .Text("该订单暂无问题记录").AlignCenter();
                return;
            }

            foreach (var issue in issues)
            {
                table.Cell().Element(BodyCellStyle).Text(issue.Id.ToString());
                table.Cell().Element(BodyCellStyle).Text(issue.Content ?? "");
                table.Cell().Element(BodyCellStyle)
                    .Text(string.IsNullOrWhiteSpace(issue.Resolution) ? "—" : issue.Resolution);
                table.Cell().Element(BodyCellStyle).Text(issue.CreatedBy ?? "");
                // (2026-08-27) 用户确认「添加人」列 = 重复 CreatedBy
                table.Cell().Element(BodyCellStyle).Text(issue.CreatedBy ?? "");
            }
        });
    }

    private static IContainer HeaderCellStyle(IContainer c) =>
        c.Background(Colors.Grey.Lighten2)
         .Border(0.5f).BorderColor(Colors.Grey.Medium)
         .Padding(5).AlignCenter();

    private static IContainer BodyCellStyle(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
         .Padding(5).AlignLeft();

    private static IContainer EmptyCellStyle(IContainer c) =>
        c.Padding(20).AlignCenter();
}
