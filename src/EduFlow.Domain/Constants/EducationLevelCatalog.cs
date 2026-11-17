namespace EduFlow.Domain.Constants;

public static class EducationLevelCatalog
{
    public sealed record Definition(string Code, string Name, string Description, int SortOrder);

    public static readonly IReadOnlyList<Definition> All =
    [
        new("PRIMARY", "Primary", "Grades 1–5", 1),
        new("JUNIOR", "Junior Secondary", "Grades 6–9", 2),
        new("OL", "Ordinary Level", "Grades 10–11", 3),
        new("AL", "Advanced Level", "Grades 12–13", 4),
        new("OTHER", "Other programmes", "English, IT, and short courses", 5)
    ];
}
