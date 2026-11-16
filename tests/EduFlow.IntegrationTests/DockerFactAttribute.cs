using System.Diagnostics;

namespace EduFlow.IntegrationTests;

public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(ProbeDocker);

    public DockerFactAttribute()
    {
        Timeout = 180_000;
        if (!DockerAvailable.Value)
        {
            Skip = "Docker is not available.";
        }
    }

    private static bool ProbeDocker()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!process.Start())
            {
                return false;
            }

            if (!process.WaitForExit(5_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
