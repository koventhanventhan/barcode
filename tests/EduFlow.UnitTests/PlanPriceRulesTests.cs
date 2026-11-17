using EduFlow.Application.SuperAdmin;
using EduFlow.Domain.Exceptions;
using FluentAssertions;

namespace EduFlow.UnitTests;

public sealed class PlanPriceRulesTests
{
    [Fact]
    public void Blank_prices_are_allowed()
    {
        var act = () => PlanPriceRules.EnsureValid(null, null, null, null, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Rejects_negative_and_over_precise_amounts()
    {
        var negative = () => PlanPriceRules.EnsureValid(-1m, null, null, null, null);
        negative.Should().Throw<DomainValidationException>().Which.Code.Should().Be("INVALID_PLAN_PRICE");

        var precise = () => PlanPriceRules.EnsureValid(null, 10.125m, null, null, null);
        precise.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Accepts_two_decimal_lkr_amounts()
    {
        var act = () => PlanPriceRules.EnsureValid(4500m, 45000.50m, 0m, 2.50m, 500m);
        act.Should().NotThrow();
    }
}
