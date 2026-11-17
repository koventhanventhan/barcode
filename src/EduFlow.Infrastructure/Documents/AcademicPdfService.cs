using EduFlow.Application.Academics;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduFlow.Infrastructure.Documents;

public sealed class AcademicPdfService : IAcademicDocumentService
{
    static AcademicPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildIdCards(IReadOnlyList<IdCardContent> cards, bool eightUp)
    {
        if (cards.Count == 0)
        {
            throw new DomainValidationException("NO_STUDENTS", "Select at least one student.");
        }

        return Document.Create(container =>
        {
            if (!eightUp)
            {
                foreach (var card in cards)
                {
                    container.Page(page =>
                    {
                        page.Size(95, 60, Unit.Millimetre);
                        page.Margin(4);
                        page.Content().Element(element => DrawCard(element, card, compact: false));
                    });
                }

                return;
            }

            for (var offset = 0; offset < cards.Count; offset += 8)
            {
                var slice = cards.Skip(offset).Take(8).ToList();
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(8);
                    page.Content().Column(column =>
                    {
                        column.Spacing(2);
                        for (var row = 0; row < 4; row++)
                        {
                            column.Item().Height(68, Unit.Millimetre).Row(line =>
                            {
                                line.Spacing(2);
                                for (var col = 0; col < 2; col++)
                                {
                                    var index = (row * 2) + col;
                                    line.RelativeItem().Element(element =>
                                    {
                                        if (index < slice.Count)
                                        {
                                            DrawCard(element, slice[index], compact: true);
                                        }
                                    });
                                }
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    public byte[] BuildLabels(IReadOnlyList<IdCardContent> cards)
    {
        if (cards.Count == 0)
        {
            throw new DomainValidationException("NO_STUDENTS", "Select at least one student.");
        }

        return Document.Create(container =>
        {
            for (var offset = 0; offset < cards.Count; offset += 24)
            {
                var slice = cards.Skip(offset).Take(24).ToList();
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(8);
                    page.Content().Column(column =>
                    {
                        column.Spacing(1);
                        for (var row = 0; row < 8; row++)
                        {
                            column.Item().Height(34, Unit.Millimetre).Row(line =>
                            {
                                line.Spacing(2);
                                for (var col = 0; col < 3; col++)
                                {
                                    var index = (row * 3) + col;
                                    line.RelativeItem().Element(element =>
                                    {
                                        if (index < slice.Count)
                                        {
                                            DrawLabel(element, slice[index]);
                                        }
                                    });
                                }
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    public byte[] BuildStudentDirectory(string instituteName, string primaryHex, IReadOnlyList<DirectoryRow> rows)
    {
        var primary = SafeColor(primaryHex);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.Header().Text(instituteName).FontSize(14).SemiBold().FontColor(primary);
                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(70);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.ConstantColumn(55);
                    });
                    table.Header(header =>
                    {
                        foreach (var title in new[] { "Code", "Student", "Guardian phone", "Level", "Status" })
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).FontSize(9).SemiBold();
                        }
                    });
                    foreach (var row in rows)
                    {
                        WriteCell(table, row.StudentCode);
                        WriteCell(table, row.FullName);
                        WriteCell(table, row.GuardianPhone);
                        WriteCell(table, row.LevelName);
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

    private static void DrawCard(IContainer container, IdCardContent card, bool compact)
    {
        var primary = SafeColor(card.PrimaryHex);
        container.Border(1).BorderColor(primary).Padding(compact ? 4 : 6).Column(column =>
        {
            column.Spacing(2);
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(text =>
                {
                    text.Item().Text(card.InstituteName).FontSize(compact ? 7 : 8).SemiBold().FontColor(primary);
                    text.Item().Text(card.StudentName).FontSize(compact ? 10 : 12).SemiBold();
                    text.Item().Text(card.StudentCode).FontSize(9);
                    text.Item().Text($"{card.LevelName} · {card.BatchNames}").FontSize(8).FontColor(Colors.Grey.Darken2);
                });
                row.ConstantItem(compact ? 36 : 46).Height(compact ? 42 : 52).Element(photo => DrawPhoto(photo, card));
            });
            column.Item().Element(element => DrawBarcode(element, card.BarcodeModules, compact ? 10 : 12));
        });
    }

    private static void DrawLabel(IContainer container, IdCardContent card)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Column(column =>
        {
            column.Item().Text(card.StudentName).FontSize(8).SemiBold();
            column.Item().Text(card.StudentCode).FontSize(7);
            column.Item().PaddingTop(1).Element(element => DrawBarcode(element, card.BarcodeModules, 12));
        });
    }

    private static void DrawPhoto(IContainer container, IdCardContent card)
    {
        if (card.Photo is { Length: > 8 })
        {
            container.Image(card.Photo).FitArea();
            return;
        }

        var initials = string.Concat(card.StudentName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
        container.Background(Colors.Grey.Lighten3).AlignCenter().AlignMiddle().Text(string.IsNullOrEmpty(initials) ? "?" : initials).FontSize(11);
    }

    private static void DrawBarcode(IContainer container, bool[] modules, float heightMm)
    {
        if (modules.Length == 0)
        {
            return;
        }

        container.Height(heightMm, Unit.Millimetre).Row(row =>
        {
            var index = 0;
            while (index < modules.Length)
            {
                var black = modules[index];
                var start = index;
                while (index < modules.Length && modules[index] == black)
                {
                    index++;
                }

                var item = row.RelativeItem(index - start);
                if (black)
                {
                    item.Background(Colors.Black);
                }
            }
        });
    }

    private static void WriteCell(TableDescriptor table, string value) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(value).FontSize(9);

    private static Color SafeColor(string hex)
    {
        try
        {
            return Color.FromHex(hex);
        }
        catch (Exception)
        {
            return Color.FromHex(BrandingProfile.DefaultPrimaryColor);
        }
    }
}
