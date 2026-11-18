using System.Globalization;
using EduFlow.Application.Academics;
using EduFlow.Application.Attendance;
using EduFlow.Application.Fees;
using EduFlow.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduFlow.Infrastructure.Documents;

public sealed class FeePdfService : IFeeDocumentService
{
    static FeePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildReceiptPdf(ReceiptPdfContent content)
    {
        var primary = SafeColor(content.PrimaryHex);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Row(row =>
                {
                    if (content.Logo is { Length: > 8 })
                    {
                        row.ConstantItem(42).Height(42).PaddingRight(8).Image(content.Logo).FitArea();
                    }

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(content.InstituteName).FontSize(14).SemiBold().FontColor(primary);
                        if (!string.IsNullOrWhiteSpace(content.InstituteAddress))
                        {
                            col.Item().Text(content.InstituteAddress!).FontSize(8).FontColor(Colors.Grey.Darken2);
                        }
                        if (!string.IsNullOrWhiteSpace(content.InstitutePhone))
                        {
                            col.Item().Text(content.InstitutePhone!).FontSize(8).FontColor(Colors.Grey.Darken2);
                        }
                    });

                    row.ConstantItem(155).AlignRight().Column(col =>
                    {
                        col.Item().Text(content.IsReprint ? "REPRINT RECEIPT" : "OFFICIAL RECEIPT").FontSize(11).Bold().FontColor(primary);
                        if (content.IsReprint)
                        {
                            col.Item().Text($"DUPLICATE COPY #{Math.Max(1, content.ReprintNumber)}").FontSize(7).Bold().FontColor(Colors.Red.Darken2);
                        }
                        col.Item().Text(content.ReceiptNumber).FontSize(10).SemiBold();
                        col.Item().Text($"Date: {AttendanceRules.FormatColombo(content.PaidAt)}").FontSize(8).FontColor(Colors.Grey.Darken2);
                    });
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().BorderBottom(1).BorderColor(primary);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Spacing(2);
                            left.Item().Text(t =>
                            {
                                t.Span("Student: ").SemiBold();
                                t.Span($"{content.StudentName} ({content.StudentCode})");
                            });
                            left.Item().Text(t =>
                            {
                                t.Span("Guardian: ").SemiBold();
                                t.Span(content.GuardianName);
                            });
                            if (!string.IsNullOrWhiteSpace(content.BatchName))
                            {
                                left.Item().Text(t =>
                                {
                                    t.Span("Batch: ").SemiBold();
                                    t.Span(content.BatchName!);
                                });
                            }
                        });

                        row.RelativeItem().Column(right =>
                        {
                            right.Spacing(2);
                            right.Item().Text(t =>
                            {
                                t.Span("Invoice: ").SemiBold();
                                t.Span($"{content.InvoiceNumber} ({content.BillingPeriod})");
                            });
                            right.Item().Text(t =>
                            {
                                t.Span("Method: ").SemiBold();
                                t.Span(content.PaymentMethod);
                            });
                            if (!string.IsNullOrWhiteSpace(content.ReferenceNumber))
                            {
                                right.Item().Text(t =>
                                {
                                    t.Span("Reference: ").SemiBold();
                                    t.Span(content.ReferenceNumber!);
                                });
                            }
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Description").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Invoice Total").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Amount Paid").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Balance Due").SemiBold();
                        });

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(content.Title);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight()
                            .Text(FeeRules.FormatCurrency(content.InvoiceNetAmount, content.Currency));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight()
                            .Text(FeeRules.FormatCurrency(content.AmountPaid, content.Currency)).SemiBold();
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight()
                            .Text(FeeRules.FormatCurrency(content.InvoiceBalanceAfter, content.Currency));
                    });

                    if (content.CreditAppliedAmount > 0m)
                    {
                        col.Item().Text($"Advance credit applied to invoice: {FeeRules.FormatCurrency(content.CreditAppliedAmount, content.Currency)}")
                            .FontSize(8)
                            .SemiBold()
                            .FontColor(Colors.Grey.Darken2);
                    }

                    if (!string.IsNullOrWhiteSpace(content.Notes))
                    {
                        col.Item().Text($"Notes: {content.Notes}").FontSize(8).FontColor(Colors.Grey.Darken2);
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Status: {content.Status}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.RelativeItem().AlignRight().Text(content.IsReprint ? "Reprinted duplicate receipt" : "Computer-generated receipt").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    public byte[] BuildPlatformInvoicePdf(PlatformInvoicePdfContent content)
    {
        var primary = SafeColor(content.PrimaryHex);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(content.PlatformName).FontSize(16).Bold().FontColor(primary);
                        col.Item().Text("Platform Subscription Invoice").FontSize(10).FontColor(Colors.Grey.Darken2);
                    });

                    row.ConstantItem(180).AlignRight().Column(col =>
                    {
                        col.Item().Text(content.InvoiceNumber).FontSize(13).Bold().FontColor(primary);
                        col.Item().Text($"Status: {content.Status.ToUpperInvariant()}").FontSize(9).SemiBold();
                        col.Item().Text($"Issue Date: {content.IssueDate:yyyy-MM-dd}").FontSize(9);
                        col.Item().Text($"Due Date: {content.DueDate:yyyy-MM-dd}").FontSize(9);
                    });
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Spacing(12);
                    col.Item().BorderBottom(1).BorderColor(primary);

                    col.Item().Column(billTo =>
                    {
                        billTo.Spacing(2);
                        billTo.Item().Text("Billed To:").SemiBold();
                        billTo.Item().Text(content.TenantName).FontSize(11).SemiBold();
                        billTo.Item().Text($"Subdomain: {content.TenantSubdomain}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        if (!string.IsNullOrWhiteSpace(content.TenantEmail))
                        {
                            billTo.Item().Text($"Email: {content.TenantEmail}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        }
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Plan / Description").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Months").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Extension Days").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Amount").SemiBold();
                        });

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6)
                            .Text($"{content.PlanName} Plan Subscription");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                            .Text(content.PeriodMonths.ToString(CultureInfo.InvariantCulture));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                            .Text(content.DaysToExtend.ToString(CultureInfo.InvariantCulture));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                            .Text(FeeRules.FormatCurrency(content.Amount, content.Currency)).SemiBold();
                    });

                    if (content.PaidAt.HasValue)
                    {
                        col.Item().Text($"Paid At: {content.PaidAt.Value:yyyy-MM-dd HH:mm} UTC · Reference: {content.PaymentReference ?? "-"}")
                            .FontSize(9)
                            .SemiBold();
                    }

                    if (!string.IsNullOrWhiteSpace(content.Notes))
                    {
                        col.Item().Text($"Notes: {content.Notes}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span($"{content.PlatformName} · Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    public byte[] BuildFeeReportPdf(string instituteName, string primaryHex, IReadOnlyList<FeeReportExportRow> rows)
    {
        var primary = SafeColor(primaryHex);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.Header().Row(row =>
                {
                    row.RelativeItem().Text($"{instituteName} · Fee Invoices Report").FontSize(13).SemiBold().FontColor(primary);
                    row.ConstantItem(140).AlignRight().Text($"Count: {rows.Count}").FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(65);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(75);
                        columns.ConstantColumn(75);
                        columns.ConstantColumn(75);
                        columns.ConstantColumn(65);
                        columns.ConstantColumn(65);
                    });

                    table.Header(header =>
                    {
                        foreach (var title in new[] { "Invoice", "Code", "Student", "Batch", "Period", "Net", "Paid", "Balance", "Due Date", "Status" })
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).FontSize(8).SemiBold();
                        }
                    });

                    foreach (var row in rows)
                    {
                        WriteCell(table, row.InvoiceNumber);
                        WriteCell(table, row.StudentCode);
                        WriteCell(table, row.StudentName);
                        WriteCell(table, row.BatchName);
                        WriteCell(table, row.BillingPeriod);
                        WriteCell(table, row.NetAmount.ToString("N2", CultureInfo.InvariantCulture));
                        WriteCell(table, row.PaidAmount.ToString("N2", CultureInfo.InvariantCulture));
                        WriteCell(table, row.BalanceAmount.ToString("N2", CultureInfo.InvariantCulture));
                        WriteCell(table, row.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        WriteCell(table, row.Status);
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    public byte[] BuildCashierClosingPdf(CashierClosingPdfContent content)
    {
        var primary = SafeColor(content.PrimaryHex);
        var report = content.Report;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Row(row =>
                {
                    if (content.Logo is { Length: > 8 })
                    {
                        row.ConstantItem(40).Height(40).PaddingRight(8).Image(content.Logo).FitArea();
                    }

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(content.InstituteName).FontSize(14).SemiBold().FontColor(primary);
                        col.Item().Text("Daily Cashier Closing Report").FontSize(10).SemiBold();
                    });

                    row.ConstantItem(160).AlignRight().Column(col =>
                    {
                        col.Item().Text($"Date: {report.Date:yyyy-MM-dd}").FontSize(10).SemiBold();
                        col.Item().Text($"Transactions: {report.TotalTransactions}").FontSize(8).FontColor(Colors.Grey.Darken2);
                        col.Item().Text($"Net Total: {FeeRules.FormatCurrency(report.GrandTotalCollected, report.Currency)}").FontSize(9).Bold().FontColor(primary);
                    });
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().BorderBottom(1).BorderColor(primary);

                    col.Item().Text("Summary by Payment Method").FontSize(10).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(95);
                            columns.ConstantColumn(95);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(95);
                        });
                        table.Header(header =>
                        {
                            foreach (var h in new[] { "Method", "Count", "Invoices", "Advance", "Refunded", "Net Collected" })
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(h).FontSize(8).SemiBold();
                            }
                        });
                        foreach (var m in report.ByMethod)
                        {
                            WriteCell(table, m.Method);
                            WriteCell(table, m.TransactionCount.ToString(CultureInfo.InvariantCulture));
                            WriteCell(table, FeeRules.FormatCurrency(m.InvoicePaymentsTotal, report.Currency));
                            WriteCell(table, FeeRules.FormatCurrency(m.AdvanceDepositsTotal, report.Currency));
                            WriteCell(table, FeeRules.FormatCurrency(m.RefundedTotal, report.Currency));
                            WriteCell(table, FeeRules.FormatCurrency(m.NetCollectedTotal, report.Currency));
                        }
                    });

                    col.Item().Text("Summary by Cashier / User").FontSize(10).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(45);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(90);
                        });
                        table.Header(header =>
                        {
                            foreach (var h in new[] { "Cashier", "Count", "Cash", "Bank", "PayHere", "Advance", "Net Collected" })
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(h).FontSize(8).SemiBold();
                            }
                        });
                        foreach (var u in report.ByUser)
                        {
                            WriteCell(table, u.CashierName);
                            WriteCell(table, u.TransactionCount.ToString(CultureInfo.InvariantCulture));
                            WriteCell(table, u.CashTotal.ToString("N2", CultureInfo.InvariantCulture));
                            WriteCell(table, u.BankTransferTotal.ToString("N2", CultureInfo.InvariantCulture));
                            WriteCell(table, u.PayHereTotal.ToString("N2", CultureInfo.InvariantCulture));
                            WriteCell(table, u.AdvanceDepositsTotal.ToString("N2", CultureInfo.InvariantCulture));
                            WriteCell(table, FeeRules.FormatCurrency(u.NetCollectedTotal, report.Currency));
                        }
                    });

                    if (report.Items.Count > 0)
                    {
                        col.Item().Text("Transactions").FontSize(10).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(75);
                                columns.RelativeColumn(1.5f);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(70);
                                columns.RelativeColumn(1.2f);
                                columns.ConstantColumn(80);
                            });
                            table.Header(header =>
                            {
                                foreach (var h in new[] { "Ref / Receipt", "Student", "Type", "Method", "Cashier", "Net Amount" })
                                {
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(h).FontSize(8).SemiBold();
                                }
                            });
                            foreach (var item in report.Items)
                            {
                                WriteCell(table, item.ReferenceCode);
                                WriteCell(table, $"{item.StudentName} ({item.StudentCode})");
                                WriteCell(table, item.Kind);
                                WriteCell(table, item.Method);
                                WriteCell(table, item.CashierName);
                                WriteCell(table, FeeRules.FormatCurrency(item.NetAmount, report.Currency));
                            }
                        });
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void WriteCell(TableDescriptor table, string value) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(value).FontSize(8);

    private static Color SafeColor(string hex)
    {
        try
        {
            return Color.FromHex(hex);
        }
        catch
        {
            return Color.FromHex(BrandingProfile.DefaultPrimaryColor);
        }
    }
}
