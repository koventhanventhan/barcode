using EduFlow.Application.Marketing;
using FluentAssertions;

namespace EduFlow.UnitTests;

public class TransactionalEmailTests
{
    [Fact]
    public void Password_reset_and_welcome_escape_untrusted_text()
    {
        var brand = new EmailBrand("<script>", "#1F4E46", false);
        var reset = TransactionalEmail.PasswordReset(
            brand,
            "A&B",
            "token-1",
            "https://admino.lk/reset-password?token=token-1",
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));

        reset.Subject.Should().Contain("<script>");
        reset.HtmlBody.Should().NotContain("<script>");
        reset.HtmlBody.Should().Contain("&lt;script&gt;");
        reset.HtmlBody.Should().Contain("A&amp;B");
        reset.PlainTextBody.Should().Contain("token-1");

        var welcome = TransactionalEmail.Welcome(brand, "Nimal", "royal", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        welcome.HtmlBody.Should().Contain("royal");
        welcome.PlainTextBody.Should().Contain("14-day");
    }
}
