using System.Text;
using EduFlow.Application.Academics;
using EduFlow.Infrastructure.Documents;
using FluentAssertions;

namespace EduFlow.UnitTests;

public sealed class AcademicRulesTests
{
    [Fact]
    public void Student_codes_are_sequential_and_ignore_unrelated_values()
    {
        StudentCodes.Next(["S000002", "OTHER", "S000010", "S000003"]).Should().Be(11);
        StudentCodes.Format(11).Should().Be("S000011");
        StudentCodes.Next([]).Should().Be(1);
    }

    [Fact]
    public void Csv_reader_keeps_quoted_commas_and_row_numbers()
    {
        var csv = "fullName,address\n\"Fernando, Kamal\",\"12, Main Street\"\n";
        var rows = CsvTable.Read(csv);
        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2);
        rows[0].Cells["fullname"].Should().Be("Fernando, Kamal");
        rows[0].Cells["address"].Should().Be("12, Main Street");
    }

    [Fact]
    public void Import_rejects_a_missing_name_and_keeps_a_valid_row()
    {
        var rows = new List<TabularRow>
        {
            new(2, new Dictionary<string, string>
            {
                ["fullname"] = "Kamal Fernando",
                ["guardianname"] = "Rani Fernando",
                ["guardianphone"] = "0771111111",
                ["address"] = "12 Main Street",
                ["educationlevelcode"] = "PRIMARY"
            }),
            new(3, new Dictionary<string, string>
            {
                ["fullname"] = "",
                ["guardianname"] = "Rani Fernando",
                ["guardianphone"] = "0771111112",
                ["address"] = "12 Main Street",
                ["educationlevelcode"] = "PRIMARY"
            })
        };
        var levelId = Guid.NewGuid();
        var catalog = new ImportCatalog(
            new Dictionary<string, Guid> { ["PRIMARY"] = levelId },
            new Dictionary<string, (Guid, Guid)>(),
            new HashSet<string>());

        var (valid, errors) = StudentImportRules.Validate(rows, catalog, new DateOnly(2026, 1, 6));

        valid.Should().ContainSingle();
        valid[0].EducationLevelId.Should().Be(levelId);
        errors.Should().Contain(error => error.RowNumber == 3 && error.Field == "fullName");
    }

    [Fact]
    public void Code128_payload_is_stable_per_student_code()
    {
        var encoder = new ZxingBarcodeEncoder();
        var first = encoder.EncodeCode128("S000001");
        var again = encoder.EncodeCode128("S000001");
        var second = encoder.EncodeCode128("S000002");

        first.Should().Equal(again);
        first.Should().NotEqual(second);
        first.Should().Contain(true);
    }

    [Fact]
    public void Id_card_pdf_uses_the_student_code_as_the_barcode_text()
    {
        var encoder = new ZxingBarcodeEncoder();
        var pdf = new AcademicPdfService().BuildIdCards(
        [
            new IdCardContent(
                "Royal Vidyalaya",
                "#1F4E46",
                null,
                null,
                "Nimal Perera",
                "S000001",
                "Ordinary Level",
                "2026 O/L",
                encoder.EncodeCode128("S000001"))
        ],
        eightUp: true);

        Encoding.ASCII.GetString(pdf[..5]).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(500);
    }
}
