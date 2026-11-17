using System.Text;
using EduFlow.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace EduFlow.Infrastructure.Security;

public sealed class SignatureAntivirusScanner(
    IConfiguration configuration,
    HttpClient httpClient) : IAntivirusScanner
{
    private const string EicarSignature = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public async Task<AntivirusScanResult> ScanAsync(
        Stream content,
        string fileName,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var bytes = memory.GetBuffer();
        var length = (int)memory.Length;
        var builtIn = CheckBuffer(bytes, length);
        if (!builtIn.IsClean)
        {
            return builtIn;
        }

        // 3. Optional external ClamAV / HTTP webhook scanner hook
        var webhookUrl = configuration["Security:Antivirus:WebhookUrl"];
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes, 0, length);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                fileContent.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            form.Add(fileContent, "file", fileName);
            using var response = await httpClient.PostAsync(webhookUrl, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AntivirusScanResult(false, "ExternalScanner.Rejected");
            }
        }

        return new AntivirusScanResult(true);
    }

    private static AntivirusScanResult CheckBuffer(byte[] bytes, int length)
    {
        var span = bytes.AsSpan(0, length);

        // 1. Check executable magic bytes (Windows MZ/PE or Linux ELF)
        if (span.Length >= 2 && span[0] == 0x4D && span[1] == 0x5A)
        {
            return new AntivirusScanResult(false, "Win32.ExecutableHeader");
        }

        if (span.Length >= 4 && span[0] == 0x7F && span[1] == (byte)'E' && span[2] == (byte)'L' && span[3] == (byte)'F')
        {
            return new AntivirusScanResult(false, "ELF.ExecutableHeader");
        }

        // 2. Check EICAR standard antivirus test string and embedded script payloads in the first 64 KB
        var inspectLen = Math.Min(length, 65536);
        var textSample = Encoding.ASCII.GetString(bytes, 0, inspectLen);
        if (textSample.Contains(EicarSignature, StringComparison.Ordinal))
        {
            return new AntivirusScanResult(false, "EICAR-Test-Signature");
        }

        if (textSample.Contains("<?php", StringComparison.OrdinalIgnoreCase)
            || textSample.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            return new AntivirusScanResult(false, "Embedded.ScriptPayload");
        }

        return new AntivirusScanResult(true);
    }
}
