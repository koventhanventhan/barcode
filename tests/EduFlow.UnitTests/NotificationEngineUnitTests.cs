using EduFlow.Application.Notifications;
using FluentAssertions;

namespace EduFlow.UnitTests;

public sealed class NotificationEngineUnitTests
{
    private readonly SmsSegmentCalculator _calculator = new();

    [Fact]
    public void SmsSegmentCalculator_handles_empty_and_English_GSM7_boundaries()
    {
        var empty = _calculator.Calculate(string.Empty);
        empty.Encoding.Should().Be("GSM-7");
        empty.IsUnicode.Should().BeFalse();
        empty.CharacterCount.Should().Be(0);
        empty.Segments.Should().Be(0);
        empty.PerSegmentLimit.Should().Be(160);
        empty.RemainingInCurrentSegment.Should().Be(160);

        var singleShort = _calculator.Calculate("Royal Vidyalaya: Nimal Perera marked present at 04:05 PM.");
        singleShort.Encoding.Should().Be("GSM-7");
        singleShort.IsUnicode.Should().BeFalse();
        singleShort.Segments.Should().Be(1);
        singleShort.PerSegmentLimit.Should().Be(160);

        var exact160 = _calculator.Calculate(new string('A', 160));
        exact160.Encoding.Should().Be("GSM-7");
        exact160.CharacterCount.Should().Be(160);
        exact160.Segments.Should().Be(1);
        exact160.PerSegmentLimit.Should().Be(160);
        exact160.RemainingInCurrentSegment.Should().Be(0);

        var multipart161 = _calculator.Calculate(new string('A', 161));
        multipart161.Encoding.Should().Be("GSM-7");
        multipart161.CharacterCount.Should().Be(161);
        multipart161.Segments.Should().Be(2);
        multipart161.PerSegmentLimit.Should().Be(153);
        multipart161.RemainingInCurrentSegment.Should().Be(306 - 161);

        var exact306 = _calculator.Calculate(new string('B', 306));
        exact306.Segments.Should().Be(2);
        exact306.RemainingInCurrentSegment.Should().Be(0);

        var multipart307 = _calculator.Calculate(new string('B', 307));
        multipart307.Segments.Should().Be(3);
        multipart307.PerSegmentLimit.Should().Be(153);
        multipart307.RemainingInCurrentSegment.Should().Be(459 - 307);
    }

    [Fact]
    public void SmsSegmentCalculator_handles_Tamil_and_Sinhala_Unicode_boundaries()
    {
        var tamilSample = "றோயல் வித்தியாலயம்: நிமல் பெரேரா இன்று வகுப்பில் வருகை தந்தார்.";
        var tamilCalc = _calculator.Calculate(tamilSample);
        tamilCalc.Encoding.Should().Be("UCS-2");
        tamilCalc.IsUnicode.Should().BeTrue();
        tamilCalc.CharacterCount.Should().Be(tamilSample.Length);
        tamilCalc.Segments.Should().Be(1);
        tamilCalc.PerSegmentLimit.Should().Be(70);

        var sinhalaSample = "රෝයල් විද්‍යාලය: නිමල් පෙරේරා අද පන්තියට පැමිණි බව සටහන් විය.";
        var sinhalaCalc = _calculator.Calculate(sinhalaSample);
        sinhalaCalc.Encoding.Should().Be("UCS-2");
        sinhalaCalc.IsUnicode.Should().BeTrue();
        sinhalaCalc.CharacterCount.Should().Be(sinhalaSample.Length);
        sinhalaCalc.Segments.Should().Be(1);
        sinhalaCalc.PerSegmentLimit.Should().Be(70);

        var tamil70 = new string('அ', 70);
        var calc70 = _calculator.Calculate(tamil70);
        calc70.Encoding.Should().Be("UCS-2");
        calc70.Segments.Should().Be(1);
        calc70.PerSegmentLimit.Should().Be(70);
        calc70.RemainingInCurrentSegment.Should().Be(0);

        var tamil71 = new string('அ', 71);
        var calc71 = _calculator.Calculate(tamil71);
        calc71.Encoding.Should().Be("UCS-2");
        calc71.Segments.Should().Be(2);
        calc71.PerSegmentLimit.Should().Be(67);
        calc71.RemainingInCurrentSegment.Should().Be(134 - 71);

        var sinhala134 = new string('අ', 134);
        var calc134 = _calculator.Calculate(sinhala134);
        calc134.Encoding.Should().Be("UCS-2");
        calc134.Segments.Should().Be(2);
        calc134.RemainingInCurrentSegment.Should().Be(0);

        var sinhala135 = new string('අ', 135);
        var calc135 = _calculator.Calculate(sinhala135);
        calc135.Encoding.Should().Be("UCS-2");
        calc135.Segments.Should().Be(3);
        calc135.PerSegmentLimit.Should().Be(67);
        calc135.RemainingInCurrentSegment.Should().Be(201 - 135);
    }

    [Fact]
    public void QuietHoursCalculator_evaluates_Asia_Colombo_window_correctly()
    {
        const int start21 = 21 * 60;
        const int end06 = 6 * 60;

        // 20:30 Colombo = 15:00 UTC
        var beforeQuietUtc = new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
        QuietHoursCalculator.IsWithinQuietHours(beforeQuietUtc, "Asia/Colombo", start21, end06).Should().BeFalse();

        // 22:00 Colombo = 16:30 UTC
        var eveningQuietUtc = new DateTimeOffset(2026, 11, 17, 16, 30, 0, TimeSpan.Zero);
        QuietHoursCalculator.IsWithinQuietHours(eveningQuietUtc, "Asia/Colombo", start21, end06).Should().BeTrue();
        var endFromEvening = QuietHoursCalculator.GetQuietHoursEndUtc(eveningQuietUtc, "Asia/Colombo", start21, end06);
        // Next morning 06:00 Colombo = 00:30 UTC on 2026-11-18
        endFromEvening.Should().Be(new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero));

        // 04:00 Colombo on 2026-11-18 = 22:30 UTC on 2026-11-17
        var earlyMorningQuietUtc = new DateTimeOffset(2026, 11, 17, 22, 30, 0, TimeSpan.Zero);
        QuietHoursCalculator.IsWithinQuietHours(earlyMorningQuietUtc, "Asia/Colombo", start21, end06).Should().BeTrue();
        var endFromMorning = QuietHoursCalculator.GetQuietHoursEndUtc(earlyMorningQuietUtc, "Asia/Colombo", start21, end06);
        endFromMorning.Should().Be(new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero));

        // 06:00 Colombo = 00:30 UTC
        var exactEndUtc = new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero);
        QuietHoursCalculator.IsWithinQuietHours(exactEndUtc, "Asia/Colombo", start21, end06).Should().BeFalse();
    }

    [Fact]
    public void NotificationTemplateRenderer_replaces_variables_case_insensitively_with_sample_fallback()
    {
        var template = "{instituteName}: {studentName} paid {amount} for {className}.";
        var rendered = NotificationTemplateRenderer.Render(
            template,
            new Dictionary<string, string>
            {
                ["INSTITUTEname"] = "Royal Academy",
                ["studentName"] = "Kavindi Perera"
            },
            fallbackToSample: true);

        rendered.Should().Be("Royal Academy: Kavindi Perera paid LKR 3,500.00 for 2026 O/L Combined Maths.");
    }
}
