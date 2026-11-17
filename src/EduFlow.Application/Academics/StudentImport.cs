using System.Globalization;
using System.Text;

namespace EduFlow.Application.Academics;

public sealed record TabularRow(int RowNumber, IReadOnlyDictionary<string, string> Cells);

public sealed record StudentImportError(int RowNumber, string Field, string Message);

public sealed record ParsedStudentDraft(
    int RowNumber,
    string FullName,
    string GuardianName,
    string GuardianPhone,
    string? GuardianPhoneSecondary,
    string Address,
    string? City,
    Guid EducationLevelId,
    IReadOnlyList<Guid> BatchIds,
    string Gender,
    DateOnly? DateOfBirth,
    DateOnly AdmissionDate,
    string Status);

public sealed record ImportCatalog(
    IReadOnlyDictionary<string, Guid> EnabledLevels,
    IReadOnlyDictionary<string, (Guid Id, Guid LevelId)> Batches,
    IReadOnlySet<string> ExistingIdentities);

public static class ImportColumns
{
    public static readonly string[] TemplateHeaders =
    [
        "fullName",
        "guardianName",
        "guardianPhone",
        "guardianPhoneSecondary",
        "address",
        "city",
        "educationLevelCode",
        "batchCode",
        "gender",
        "dateOfBirth",
        "admissionDate",
        "status"
    ];

    public static readonly string[] Required = ["fullname", "guardianname", "guardianphone", "address", "educationlevelcode"];

    public static string Normalize(string header) =>
        new(header.Trim().ToLowerInvariant().Where(c => c is not (' ' or '_' or '-')).ToArray());

    public static IReadOnlyList<string> Missing(IReadOnlyList<TabularRow> rows)
    {
        var keys = rows.SelectMany(r => r.Cells.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Required.Where(key => !keys.Contains(key)).ToList();
    }

    public static string Cell(IReadOnlyDictionary<string, string> cells, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (cells.TryGetValue(key, out var value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}

public static class CsvTable
{
    public static IReadOnlyList<TabularRow> Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return Read(reader.ReadToEnd());
    }

    public static IReadOnlyList<TabularRow> Read(string text)
    {
        var matrix = Parse(text);
        if (matrix.Count == 0)
        {
            return [];
        }

        var headers = matrix[0].Select(ImportColumns.Normalize).ToList();
        var rows = new List<TabularRow>();
        for (var i = 1; i < matrix.Count; i++)
        {
            var source = matrix[i];
            if (source.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
            {
                if (string.IsNullOrWhiteSpace(headers[c]))
                {
                    continue;
                }

                cells[headers[c]] = c < source.Count ? source[c].Trim() : string.Empty;
            }

            rows.Add(new TabularRow(i + 1, cells));
        }

        return rows;
    }

    public static byte[] Write(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.AppendLine(string.Join(',', headers.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (quoted)
            {
                if (current == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(current);
                }

                continue;
            }

            if (current == '"')
            {
                quoted = true;
                continue;
            }

            if (current == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if (current == '\r')
            {
                continue;
            }

            if (current == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
                continue;
            }

            cell.Append(current);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}

public static class StudentImportRules
{
    public static (IReadOnlyList<ParsedStudentDraft> Valid, IReadOnlyList<StudentImportError> Errors) Validate(
        IReadOnlyList<TabularRow> rows,
        ImportCatalog catalog,
        DateOnly today)
    {
        var valid = new List<ParsedStudentDraft>();
        var errors = new List<StudentImportError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var rowErrors = new List<StudentImportError>();
            var fullName = ImportColumns.Cell(row.Cells, "fullname");
            var guardianName = ImportColumns.Cell(row.Cells, "guardianname");
            var guardianPhone = ImportColumns.Cell(row.Cells, "guardianphone");
            var secondary = ImportColumns.Cell(row.Cells, "guardianphonesecondary", "guardianphone2");
            var address = ImportColumns.Cell(row.Cells, "address");
            var city = ImportColumns.Cell(row.Cells, "city");
            var levelCode = ImportColumns.Cell(row.Cells, "educationlevelcode", "level").ToUpperInvariant();
            var batchField = ImportColumns.Cell(row.Cells, "batchcode", "batch");
            var gender = ImportColumns.Cell(row.Cells, "gender");
            var dobText = ImportColumns.Cell(row.Cells, "dateofbirth", "dob");
            var admissionText = ImportColumns.Cell(row.Cells, "admissiondate");
            var status = ImportColumns.Cell(row.Cells, "status");

            if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200)
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "fullName", "Enter a student name up to 200 characters."));
            }

            if (string.IsNullOrWhiteSpace(guardianName) || guardianName.Length > 200)
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "guardianName", "Enter the guardian name."));
            }

            if (!ContactRules.IsPhone(guardianPhone))
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "guardianPhone", "Enter a guardian phone with 9 to 15 digits."));
            }

            if (!string.IsNullOrWhiteSpace(secondary) && !ContactRules.IsPhone(secondary))
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "guardianPhoneSecondary", "The second phone number is not valid."));
            }

            if (string.IsNullOrWhiteSpace(address) || address.Length > 300)
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "address", "Enter an address up to 300 characters."));
            }

            if (city.Length > 80)
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "city", "City must be 80 characters or fewer."));
            }

            if (!catalog.EnabledLevels.TryGetValue(levelCode, out var levelId))
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "educationLevelCode", $"'{levelCode}' is not an enabled education level."));
            }

            var batchIds = new List<Guid>();
            if (!string.IsNullOrWhiteSpace(batchField))
            {
                foreach (var part in batchField.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var code = part.ToUpperInvariant();
                    if (!catalog.Batches.TryGetValue(code, out var batch))
                    {
                        rowErrors.Add(new StudentImportError(row.RowNumber, "batchCode", $"Batch '{code}' was not found."));
                        continue;
                    }

                    if (levelId != Guid.Empty && batch.LevelId != levelId)
                    {
                        rowErrors.Add(new StudentImportError(row.RowNumber, "batchCode", $"Batch '{code}' belongs to a different education level."));
                        continue;
                    }

                    batchIds.Add(batch.Id);
                }
            }

            var genderValue = string.IsNullOrWhiteSpace(gender) ? "Unspecified" : gender;
            if (!Enum.TryParse<Domain.Enums.StudentGender>(genderValue, true, out _))
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "gender", "Gender must be Female, Male, Other, or Unspecified."));
            }

            DateOnly? dob = null;
            if (!string.IsNullOrWhiteSpace(dobText))
            {
                if (!DateOnly.TryParse(dobText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
                {
                    rowErrors.Add(new StudentImportError(row.RowNumber, "dateOfBirth", "Use the date format yyyy-MM-dd."));
                }
                else
                {
                    dob = parsedDob;
                }
            }

            var admission = today;
            if (!string.IsNullOrWhiteSpace(admissionText))
            {
                if (!DateOnly.TryParse(admissionText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedAdmission))
                {
                    rowErrors.Add(new StudentImportError(row.RowNumber, "admissionDate", "Use the date format yyyy-MM-dd."));
                }
                else
                {
                    admission = parsedAdmission;
                }
            }

            var statusValue = string.IsNullOrWhiteSpace(status) ? "Active" : status;
            if (!Enum.TryParse<Domain.Enums.StudentStatus>(statusValue, true, out var parsedStatus))
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "status", "Status must be Active, Inactive, or Left."));
            }
            else
            {
                statusValue = parsedStatus.ToString();
            }

            var identity = string.IsNullOrWhiteSpace(fullName) || !ContactRules.IsPhone(guardianPhone)
                ? null
                : ContactRules.Identity(fullName, guardianPhone);
            if (identity is not null && (catalog.ExistingIdentities.Contains(identity) || seen.Contains(identity)))
            {
                rowErrors.Add(new StudentImportError(row.RowNumber, "fullName", "A student with this name and guardian phone is already listed."));
            }

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
                continue;
            }

            if (identity is not null)
            {
                seen.Add(identity);
            }

            valid.Add(new ParsedStudentDraft(
                row.RowNumber,
                fullName,
                guardianName,
                guardianPhone,
                string.IsNullOrWhiteSpace(secondary) ? null : secondary,
                address,
                string.IsNullOrWhiteSpace(city) ? null : city,
                levelId,
                batchIds,
                genderValue,
                dob,
                admission,
                statusValue));
        }

        return (valid, errors);
    }
}
