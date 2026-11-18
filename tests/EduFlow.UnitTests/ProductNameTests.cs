using EduFlow.Domain;
using FluentAssertions;

namespace EduFlow.UnitTests;

public sealed class ProductNameTests
{
    [Fact]
    public void Value_is_the_product_name()
    {
        ProductName.Value.Should().Be("Admino");
    }
}
