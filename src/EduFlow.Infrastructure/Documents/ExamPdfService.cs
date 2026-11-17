using System.Globalization;
using EduFlow.Application.Exams;
using EduFlow.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduFlow.Infrastructure.Documents;

public sealed class ExamPdfService : IExamDocumentService
{
    static ExamPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildReportCards(IReadOnlyList<ReportCardPdfContent> cards)
    {
        return Document.Create(container =>
        {
            foreach (var card in cards)
            {
                var primary = SafeColor(card.PrimaryHex);
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(style => style.FontSize(10));
                    page.Header().Row(row =>
                    {
                        if (card.Logo is { Length: > 8 })
                        {
                            row.ConstantItem(48).Height(48).PaddingRight(8).Image(card.Logo).FitArea();
                        }

                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(card.InstituteName).FontSize(16).SemiBold().FontColor(primary);
                            if (!string.IsNullOrWhiteSpace(card.InstituteAddress))
                            {
                                col.Item().Text(card.InstituteAddress).FontSize(8).FontColor(Colors.Grey.Darken2);
                            }

                            if (!string.IsNullOrWhiteSpace(card.InstitutePhone))
                            {
                                col.Item().Text(card.InstitutePhone).FontSize(8).FontColor(Colors.Grey.Darken2);
                            }
                        });

                        row.ConstantItem(150).AlignRight().Column(col =>
                        {
                            col.Item().Text("REPORT CARD").FontSize(12).Bold().FontColor(primary);
                            col.Item().Text(card.ExamName).FontSize(9).SemiBold();
                            col.Item().Text(card.ExamKind).FontSize(8).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrWhiteSpace(card.ExamWhen))
                            {
                                col.Item().Text(card.ExamWhen).FontSize(8).FontColor(Colors.Grey.Darken2);
                            }
                        });
                    });

                    page.Content().PaddingTop(12).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().BorderBottom(1).BorderColor(primary);
                        col.Item().Row(row =>
                        {
                            if (card.Photo is { Length: > 8 })
                            {
                                row.ConstantItem(64).Height(72).PaddingRight(8).Image(card.Photo).FitArea();
                            }

                            row.RelativeItem().Column(left =>
                            {
                                left.Spacing(2);
                                left.Item().Text($"{card.StudentName} ({card.StudentCode})").FontSize(12).SemiBold();
                                left.Item().Text($"Batch: {card.BatchName}");
                                left.Item().Text($"Level: {card.LevelName}");
                                if (!string.IsNullOrWhiteSpace(card.Room))
                                {
                                    left.Item().Text($"Room: {card.Room}");
                                }
                            });
                        });

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            table.Header(header =>
                            {
                                foreach (var heading in new[] { "Subject", "Marks", "Max", "Grade", "Rank" })
                                {
                                    header.Cell().Background(primary).Padding(4).Text(heading).FontColor(Colors.White).FontSize(9).SemiBold();
                                }
                            });

                            foreach (var line in card.Subjects)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.SubjectName);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.IsAbsent ? "Absent" : FormatMark(line.Marks));
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(FormatMark(line.MaxMarks));
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.Grade ?? "—");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.Rank?.ToString(CultureInfo.InvariantCulture) ?? "—");
                            }
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"Total {FormatMark(card.TotalMarks)} / {FormatMark(card.MaxMarks)}").SemiBold();
                            row.RelativeItem().Text($"Percentage {card.Percentage.ToString("0.00", CultureInfo.InvariantCulture)}%");
                            row.RelativeItem().Text($"Grade {card.Grade}").SemiBold();
                        });
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"Batch rank {card.BatchRank?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
                            row.RelativeItem().Text($"Level rank {card.LevelRank?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
                            row.RelativeItem().Text($"Attendance {FormatAttendance(card.AttendancePercent)}");
                        });

                        col.Item().PaddingTop(6).Text("Teacher remarks").FontSize(9).SemiBold().FontColor(primary);
                        col.Item().MinHeight(36).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(card.TeacherRemarks ?? " ");
                        col.Item().Text("Principal remarks").FontSize(9).SemiBold().FontColor(primary);
                        col.Item().MinHeight(36).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(card.PrincipalRemarks ?? " ");
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                        if (card.ShowProductName)
                        {
                            text.Span($"{ProductName.Value}  ·  ");
                        }

                        text.Span(card.InstituteName);
                    });
                });
            }
        }).GeneratePdf();
    }

    private static string FormatMark(decimal? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";

    private static string FormatAttendance(decimal? value) =>
        value is null ? "—" : $"{value.Value.ToString("0.##", CultureInfo.InvariantCulture)}%";

    private static string SafeColor(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#')
        {
            return hex;
        }

        return "#1F4E46";
    }
}
