using System.Text;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Exceptions;
using FluentAssertions;

namespace EduFlow.UnitTests;

public sealed class SvgSanitizerTests
{
    [Fact]
    public void ValidateAndSanitize_accepts_clean_svg()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\"><circle cx=\"16\" cy=\"16\" r=\"14\" fill=\"#0F766E\"/></svg>"));

        using var output = SvgSanitizer.ValidateAndSanitize(input);
        var sanitized = Encoding.UTF8.GetString(output.ToArray());

        sanitized.Should().Contain("<svg");
        sanitized.Should().Contain("<circle");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><rect width=\"10\" height=\"10\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"javascript:alert(1)\"><rect width=\"10\" height=\"10\"/></a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><foreignObject><div>x</div></foreignObject></svg>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg>&xxe;</svg>")]
    public void ValidateAndSanitize_rejects_unsafe_svg(string payload)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var act = () => SvgSanitizer.ValidateAndSanitize(input);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("INVALID_ASSET_TYPE");
    }
}
