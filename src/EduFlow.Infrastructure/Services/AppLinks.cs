using EduFlow.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace EduFlow.Infrastructure.Services;

public sealed class AppLinks(IConfiguration configuration) : IAppLinks
{
    private string BaseUrl => (configuration["App:PublicWebUrl"] ?? "http://localhost:5173").TrimEnd('/');

    public string ParentInviteUrl(string phone, string token) =>
        $"{BaseUrl}/accept-invite?phone={Uri.EscapeDataString(phone)}&token={Uri.EscapeDataString(token)}";

    public string ParentResetUrl(string phone, string token) =>
        $"{BaseUrl}/reset-password?phone={Uri.EscapeDataString(phone)}&token={Uri.EscapeDataString(token)}";
}
