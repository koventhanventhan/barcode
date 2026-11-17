using ClosedXML.Excel;
using EduFlow.Application.Academics;

namespace EduFlow.Infrastructure.Documents;

public sealed class ClosedXmlWorkbookFile : IWorkbookFile
{
    public IReadOnlyList<TabularRow> Read(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        var headers = new List<string>();
        var headerRow = used.FirstRow();
        foreach (var cell in headerRow.Cells())
        {
            headers.Add(cell.GetString());
        }

        var rows = new List<TabularRow>();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var blank = true;
            for (var index = 0; index < headers.Count; index++)
            {
                var cell = row.Cell(index + 1);
                var text = cell.DataType == XLDataType.DateTime
                    ? cell.GetDateTime().ToString("yyyy-MM-dd")
                    : cell.GetString().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    blank = false;
                }

                var header = headers[index];
                if (!string.IsNullOrWhiteSpace(header))
                {
                    cells[header] = text;
                }
            }

            if (!blank)
            {
                rows.Add(new TabularRow(row.RowNumber(), cells));
            }
        }

        return rows;
    }

    public byte[] Write(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(string.IsNullOrWhiteSpace(sheetName) ? "Students" : sheetName);
        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
            sheet.Cell(1, column + 1).Style.Font.Bold = true;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var column = 0; column < row.Count; column++)
            {
                sheet.Cell(rowIndex + 2, column + 1).Value = row[column];
            }
        }

        sheet.Columns().AdjustToContents();
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
