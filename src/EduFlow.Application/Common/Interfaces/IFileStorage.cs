namespace EduFlow.Application.Common.Interfaces;

public sealed record StoredFile(string Url, long SizeBytes);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(
        string folder,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Stream? OpenRead(string url);

    Task DeleteAsync(string url, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
