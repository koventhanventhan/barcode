using System.Text;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Common.Security;

public static class FileUploadValidator
{
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "upload";
        }

        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return "upload";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (Array.IndexOf(invalid, ch) < 0 && !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "upload" : builder.ToString();
    }

    public static bool MatchesSignature(ReadOnlySpan<byte> bytes, string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => bytes.Length >= 8
                      && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                      && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            ".jpg" or ".jpeg" => bytes.Length >= 3
                                 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".webp" => bytes.Length >= 12
                       && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                       && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            ".pdf" => bytes.Length >= 5
                      && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F' && bytes[4] == (byte)'-',
            ".xlsx" => bytes.Length >= 4
                       && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04,
            ".csv" => bytes.Length > 0 && !ContainsBinaryControlBytes(bytes),
            _ => false
        };
    }

    public static async Task EnsureCleanAndValidSignatureAsync(
        MemoryStream buffer,
        string fileName,
        string? contentType,
        string extension,
        string invalidTypeCode,
        string invalidTypeMessage,
        IAntivirusScanner? antivirusScanner = null,
        CancellationToken cancellationToken = default)
    {
        buffer.Position = 0;
        if (antivirusScanner is not null)
        {
            var scan = await antivirusScanner.ScanAsync(buffer, SanitizeFileName(fileName), contentType, cancellationToken);
            buffer.Position = 0;
            if (!scan.IsClean)
            {
                throw new DomainValidationException(
                    "FILE_INFECTED",
                    $"File upload rejected by security scan ({scan.ThreatName ?? "threat detected"}).");
            }
        }

        if (!HasValidSignature(buffer, extension))
        {
            throw new DomainValidationException(invalidTypeCode, invalidTypeMessage);
        }

        buffer.Position = 0;
    }

    private static bool HasValidSignature(MemoryStream buffer, string extension)
    {
        if (buffer.TryGetBuffer(out var segment) && segment.Array is not null)
        {
            return MatchesSignature(segment.Array.AsSpan(segment.Offset, segment.Count), extension);
        }

        return MatchesSignature(buffer.ToArray(), extension);
    }

    private static bool ContainsBinaryControlBytes(ReadOnlySpan<byte> bytes)
    {
        var checkLength = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < checkLength; i++)
        {
            var b = bytes[i];
            if (b == 0x00)
            {
                return true;
            }
        }

        return false;
    }
}
