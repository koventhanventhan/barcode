using System.Text;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Exceptions;
using EduFlow.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace EduFlow.UnitTests;

public sealed class SecurityHardeningUnitTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Hello <iframe src='evil'></iframe>")]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("bad\0null")]
    public void InputSanitizer_DetectsDangerousPayloads(string payload)
    {
        InputSanitizer.ContainsDangerousPayload(payload).Should().BeTrue();
        var act = () => InputSanitizer.ValidateNoDangerousPayload(payload, "Notes");
        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "UNSAFE_INPUT");
    }

    [Fact]
    public void InputSanitizer_PreservesSafeMultilingualText_AndStripsControlChars()
    {
        var input = "  कविता / தமிழ் / සිංහල — Grade 11 (A/L)\u0007  ";
        var cleaned = InputSanitizer.SanitizeText(input);
        cleaned.Should().Be("कविता / தமிழ் / සිංහල — Grade 11 (A/L)");
    }

    [Fact]
    public void PiiRedactor_MasksEmailPhoneQueryStrings_AndAuditSecrets()
    {
        PiiRedactor.MaskEmail("superadmin@admino.lk").Should().Be("s***n@admino.lk");
        PiiRedactor.MaskPhone("+94771234567").Should().Be("+947***567");

        var redactedQuery = PiiRedactor.RedactQueryString("?email=admin@admino.lk&token=secret123&page=2");
        redactedQuery.Should().Be("?email=[REDACTED]&token=[REDACTED]&page=2");

        var json = PiiRedactor.SerializeAuditDetails(new
        {
            Action = "Configured",
            MerchantSecret = "super-secret-value",
            ApiKey = "notify-key-123",
            Nested = new { PasswordHash = "hash-value", Count = 4 }
        });

        json.Should().NotBeNull();
        json.Should().Contain("\"[REDACTED]\"");
        json.Should().NotContain("super-secret-value");
        json.Should().NotContain("notify-key-123");
        json.Should().NotContain("hash-value");
        json.Should().Contain("\"Count\":4");
    }

    [Fact]
    public async Task FileUploadValidator_And_AntivirusScanner_BlockMalwareAndSpoofedFiles()
    {
        var config = new ConfigurationBuilder().Build();
        var scanner = new SignatureAntivirusScanner(config, new HttpClient());

        // 1. EICAR test signature must be blocked
        var eicarBytes = Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        using var eicarStream = new MemoryStream(eicarBytes);
        var eicarScan = await scanner.ScanAsync(eicarStream, "test.csv", "text/csv");
        eicarScan.IsClean.Should().BeFalse();
        eicarScan.ThreatName.Should().Be("EICAR-Test-Signature");

        var actEicar = () => FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            eicarStream,
            "test.csv",
            "text/csv",
            ".csv",
            "IMPORT_FILE",
            "Invalid CSV",
            scanner);
        await actEicar.Should().ThrowAsync<DomainValidationException>()
            .Where(ex => ex.Code == "FILE_INFECTED");

        // 2. Windows PE executable disguised as PNG must be blocked
        var mzBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 };
        using var mzStream = new MemoryStream(mzBytes);
        var actMz = () => FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            mzStream,
            "avatar.png",
            "image/png",
            ".png",
            "PHOTO_TYPE",
            "Invalid photo",
            scanner);
        await actMz.Should().ThrowAsync<DomainValidationException>()
            .Where(ex => ex.Code == "FILE_INFECTED");

        // 3. Plain text disguised as PNG must fail magic byte verification
        var fakePng = Encoding.ASCII.GetBytes("not-a-real-png-file-header");
        using var fakePngStream = new MemoryStream(fakePng);
        var actSpoof = () => FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            fakePngStream,
            "avatar.png",
            "image/png",
            ".png",
            "PHOTO_TYPE",
            "Invalid photo",
            scanner);
        await actSpoof.Should().ThrowAsync<DomainValidationException>()
            .Where(ex => ex.Code == "PHOTO_TYPE");

        // 4. Valid PNG passes both antivirus and magic byte check
        var validPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        using var validPngStream = new MemoryStream(validPng);
        await FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            validPngStream,
            "../../etc/avatar.png",
            "image/png",
            ".png",
            "PHOTO_TYPE",
            "Invalid photo",
            scanner);

        FileUploadValidator.SanitizeFileName("../../etc/avatar.png").Should().Be("avatar.png");
    }
}
