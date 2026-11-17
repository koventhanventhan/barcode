using System.Collections.Frozen;
using EduFlow.Application.Common.Interfaces;

namespace EduFlow.Application.Notifications;

public sealed class SmsSegmentCalculator : ISmsSegmentCalculator
{
    private const int GsmSingleLimit = 160;
    private const int GsmMultipartLimit = 153;
    private const int UnicodeSingleLimit = 70;
    private const int UnicodeMultipartLimit = 67;

    private static readonly FrozenSet<char> Gsm7Characters = new HashSet<char>(
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà^{}\\[~]|€\f")
        .ToFrozenSet();

    public SmsSegmentCalculation Calculate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SmsSegmentCalculation(
                Encoding: "GSM-7",
                IsUnicode: false,
                CharacterCount: 0,
                Segments: 0,
                PerSegmentLimit: GsmSingleLimit,
                RemainingInCurrentSegment: GsmSingleLimit);
        }

        var isUnicode = false;
        foreach (var ch in text)
        {
            if (!Gsm7Characters.Contains(ch))
            {
                isUnicode = true;
                break;
            }
        }

        var charCount = text.Length;
        var singleLimit = isUnicode ? UnicodeSingleLimit : GsmSingleLimit;
        var multipartLimit = isUnicode ? UnicodeMultipartLimit : GsmMultipartLimit;
        var encoding = isUnicode ? "UCS-2" : "GSM-7";

        if (charCount <= singleLimit)
        {
            return new SmsSegmentCalculation(
                Encoding: encoding,
                IsUnicode: isUnicode,
                CharacterCount: charCount,
                Segments: 1,
                PerSegmentLimit: singleLimit,
                RemainingInCurrentSegment: singleLimit - charCount);
        }

        var segments = (int)Math.Ceiling((double)charCount / multipartLimit);
        var capacity = segments * multipartLimit;
        return new SmsSegmentCalculation(
            Encoding: encoding,
            IsUnicode: isUnicode,
            CharacterCount: charCount,
            Segments: segments,
            PerSegmentLimit: multipartLimit,
            RemainingInCurrentSegment: capacity - charCount);
    }
}
