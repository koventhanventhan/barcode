namespace EduFlow.Application.Common.Interfaces;

public interface IAppLinks
{
    string ParentInviteUrl(string phone, string token);

    string ParentResetUrl(string phone, string token);

    string PasswordResetUrl(string email, string token);
}
