using EduFlow.Application.Attendance;
using FluentAssertions;

namespace EduFlow.UnitTests;

public class AttendanceRulesTests
{
    [Fact]
    public void Late_starts_after_the_threshold()
    {
        var started = DateTimeOffset.Parse("2026-11-17T10:30:00Z");
        AttendanceRules.IsLate(started, started.AddMinutes(10), 10).Should().BeFalse();
        AttendanceRules.IsLate(started, started.AddMinutes(10).AddSeconds(1), 10).Should().BeTrue();
    }

    [Fact]
    public void Colombo_label_uses_a_clock_time()
    {
        var label = AttendanceRules.FormatColombo(DateTimeOffset.Parse("2026-11-17T10:32:00Z"));
        label.Should().Be("4:02 PM");
    }
}
