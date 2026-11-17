using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.ExceptionHandling;

public sealed class GlobalExceptionHandler(IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is AppException appException)
        {
            httpContext.Response.StatusCode = appException.StatusCode;

            object payload = appException switch
            {
                FeatureDisabledException fde => new
                {
                    code = fde.Code,
                    feature = fde.Feature,
                    message = fde.Message
                },
                FeatureNotInPlanException fnip => new
                {
                    code = fnip.Code,
                    feature = fnip.Feature,
                    message = fnip.Message
                },
                PlanLimitReachedException plre => new
                {
                    code = plre.Code,
                    limit = plre.Limit,
                    max = plre.Max,
                    message = plre.Message
                },
                AccountLockedException ale => new
                {
                    code = ale.Code,
                    lockoutEnd = ale.LockoutEnd,
                    message = ale.Message
                },
                _ => new
                {
                    code = appException.Code,
                    message = appException.Message
                }
            };

            await httpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
            return true;
        }

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://datatracker.ietf.org/doc/html/rfc7807",
            Instance = httpContext.Request.Path,
            Detail = environment.IsDevelopment() ? exception.Message : null
        };

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
