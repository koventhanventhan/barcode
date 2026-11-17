using System.Text;
using EduFlow.Application.Common.Security;

namespace EduFlow.Api.Middleware;

public sealed class InputSanitizationMiddleware(RequestDelegate next)
{
    private const int MaxInspectedJsonBytes = 512 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (IsJsonMutation(request))
        {
            request.EnableBuffering();
            using var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var buffer = new char[Math.Min((int)(request.ContentLength ?? 65536), MaxInspectedJsonBytes)];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            request.Body.Position = 0;

            if (read > 0)
            {
                var sample = new string(buffer, 0, read);
                if (InputSanitizer.ContainsDangerousPayload(sample))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "UNSAFE_INPUT",
                        message = "Request contains disallowed script or control characters."
                    });
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool IsJsonMutation(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsDelete(request.Method))
        {
            return false;
        }

        return request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
    }
}
