using DotNetEnv;
using EduFlow.Api.ExceptionHandling;
using EduFlow.Domain;
using Serilog;

LoadEnvFile();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = ProductName.Value, Version = "v1" });
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.MapHealthChecks("/health");

app.MapGet("/api/info", () => Results.Ok(new { product = ProductName.Value }))
    .WithName("GetProductInfo")
    .WithTags("System")
    .WithOpenApi();

if (builder.Configuration.GetValue<bool>("Diagnostics:ThrowOnGet"))
{
    app.MapGet("/api/diagnostics/throw", ThrowDiagnostic)
        .WithTags("Diagnostics")
        .ExcludeFromDescription();
}

app.Run();

static IResult ThrowDiagnostic() => throw new InvalidOperationException("diagnostic failure");

static void LoadEnvFile()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            Env.Load(candidate);
            return;
        }

        directory = directory.Parent;
    }
}

public partial class Program;
