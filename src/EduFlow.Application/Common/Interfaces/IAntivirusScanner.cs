namespace EduFlow.Application.Common.Interfaces;

public sealed record AntivirusScanResult(bool IsClean, string? ThreatName = null);

public interface IAntivirusScanner
{
    Task<AntivirusScanResult> ScanAsync(
        Stream content,
        string fileName,
        string? contentType = null,
        CancellationToken cancellationToken = default);
}
