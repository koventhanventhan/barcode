using System.Text;
using System.Text.RegularExpressions;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Common.Security;

public static partial class InputSanitizer
{
    public static bool ContainsDangerousPayload(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        if (input.IndexOf('\0') >= 0
            || input.Contains("\\u0000", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = input.Contains("\\u003", StringComparison.OrdinalIgnoreCase)
            ? UnicodeBracketEscapePattern().Replace(
                input,
                match => match.Value.EndsWith("c", StringComparison.OrdinalIgnoreCase) ? "<" : ">")
            : input;

        return DangerousHtmlPattern().IsMatch(normalized);
    }

    public static void ValidateNoDangerousPayload(string? input, string fieldName = "Input")
    {
        if (ContainsDangerousPayload(input))
        {
            throw new DomainValidationException(
                "UNSAFE_INPUT",
                $"{fieldName} contains disallowed script or control characters.");
        }
    }

    public static string SanitizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        ValidateNoDangerousPayload(input);

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is '\r' or '\n' or '\t' || !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(
        @"<\s*/?\s*(?:script|iframe|object|embed|applet|meta)\b|javascript\s*:|vbscript\s*:|on(?:error|load|click|mouseover|focus)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousHtmlPattern();

    [GeneratedRegex(@"\\u003[ce]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeBracketEscapePattern();
}
