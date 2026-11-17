using EduFlow.Application.Parents;
using FluentAssertions;

namespace EduFlow.UnitTests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0771234567", "+94771234567")]
    [InlineData("+94 77 123 4567", "+94771234567")]
    [InlineData("94771234567", "+94771234567")]
    [InlineData("123", "")]
    public void Normalize_sri_lankan_numbers(string raw, string expected)
    {
        PhoneNumber.Normalize(raw).Should().Be(expected);
    }

    [Fact]
    public void Placeholder_email_is_not_a_real_mailbox()
    {
        var email = PhoneNumber.PlaceholderEmail("+94771234567", Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        PhoneNumber.IsPlaceholderEmail(email).Should().BeTrue();
        PhoneNumber.IsPlaceholderEmail("parent@school.lk").Should().BeFalse();
        PhoneNumber.LooksLikePhone("0771234567").Should().BeTrue();
        PhoneNumber.LooksLikePhone("parent@school.lk").Should().BeFalse();
    }
}
