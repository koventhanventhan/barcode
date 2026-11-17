using EduFlow.Application.Exams;
using EduFlow.Domain.Exceptions;
using FluentAssertions;

namespace EduFlow.UnitTests;

public class ExamRulesTests
{
    [Fact]
    public void Default_bands_cover_every_step_and_resolve_boundaries()
    {
        ExamRules.ValidateBands(ExamRules.DefaultBands);
        ExamRules.ResolveGrade(0m, ExamRules.DefaultBands).Should().Be("F");
        ExamRules.ResolveGrade(34.99m, ExamRules.DefaultBands).Should().Be("F");
        ExamRules.ResolveGrade(35m, ExamRules.DefaultBands).Should().Be("S");
        ExamRules.ResolveGrade(50m, ExamRules.DefaultBands).Should().Be("C");
        ExamRules.ResolveGrade(65m, ExamRules.DefaultBands).Should().Be("B");
        ExamRules.ResolveGrade(74.99m, ExamRules.DefaultBands).Should().Be("B");
        ExamRules.ResolveGrade(75m, ExamRules.DefaultBands).Should().Be("A");
        ExamRules.ResolveGrade(100m, ExamRules.DefaultBands).Should().Be("A");
        ExamRules.Percentage(80m, 100m).Should().Be(80.00m);
        ExamRules.Percentage(1m, 3m).Should().Be(33.33m);
    }

    [Fact]
    public void Overlapping_or_gapped_bands_are_rejected()
    {
        var overlap = new[]
        {
            new GradeBandInput("A", 0m, 50m, 1m, 1),
            new GradeBandInput("B", 50m, 100m, 2m, 2)
        };
        var gap = new[]
        {
            new GradeBandInput("F", 0m, 40m, 0m, 1),
            new GradeBandInput("A", 50m, 100m, 4m, 2)
        };

        var overlapAct = () => ExamRules.ValidateBands(overlap);
        var gapAct = () => ExamRules.ValidateBands(gap);
        overlapAct.Should().Throw<DomainValidationException>().Which.Code.Should().Be("GRADE_BANDS_MUST_MEET");
        gapAct.Should().Throw<DomainValidationException>().Which.Code.Should().Be("GRADE_BANDS_MUST_MEET");
    }

    [Fact]
    public void Competition_ranking_shares_ties_and_skips_the_next_place()
    {
        var high = Guid.NewGuid();
        var tieA = Guid.NewGuid();
        var tieB = Guid.NewGuid();
        var low = Guid.NewGuid();
        var absent = Guid.NewGuid();
        var rows = new[]
        {
            new ScoreRow(low, 70m, true),
            new ScoreRow(tieB, 80m, true),
            new ScoreRow(absent, 0m, false),
            new ScoreRow(high, 90m, true),
            new ScoreRow(tieA, 80m, true)
        };

        var ranks = ExamRules.AssignCompetitionRanks(rows).ToDictionary(r => r.Id, r => r.Rank);
        ranks[high].Should().Be(1);
        ranks[tieA].Should().Be(2);
        ranks[tieB].Should().Be(2);
        ranks[low].Should().Be(4);
        ranks[absent].Should().BeNull();
    }

    [Fact]
    public void Reminder_window_uses_days_plus_hours_and_stops_at_the_exam()
    {
        var examAt = new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
        ExamRules.IsReminderDue(examAt.AddHours(-26), examAt, 1, 2, false).Should().BeTrue();
        ExamRules.IsReminderDue(examAt.AddHours(-26).AddMinutes(-1), examAt, 1, 2, false).Should().BeFalse();
        ExamRules.IsReminderDue(examAt, examAt, 1, 2, false).Should().BeFalse();
        ExamRules.IsReminderDue(examAt.AddHours(-1), examAt, 1, 0, true).Should().BeFalse();
        ExamRules.IsReminderDue(examAt.AddHours(-1), examAt, 0, 0, false).Should().BeFalse();
    }
}
