import { describe, expect, it } from "vitest";
import { calculateSmsSegments, renderNotificationTemplate } from "./sms";

describe("calculateSmsSegments", () => {
  it("calculates GSM-7 single and multipart boundaries", () => {
    expect(calculateSmsSegments("").segments).toBe(0);
    expect(calculateSmsSegments("A".repeat(160))).toMatchObject({
      encoding: "GSM-7",
      isUnicode: false,
      segments: 1,
      perSegmentLimit: 160,
      remainingInCurrentSegment: 0,
    });
    expect(calculateSmsSegments("A".repeat(161))).toMatchObject({
      encoding: "GSM-7",
      isUnicode: false,
      segments: 2,
      perSegmentLimit: 153,
      remainingInCurrentSegment: 145,
    });
    expect(calculateSmsSegments("A".repeat(306)).segments).toBe(2);
    expect(calculateSmsSegments("A".repeat(307)).segments).toBe(3);
  });

  it("calculates Tamil and Sinhala UCS-2 boundaries", () => {
    expect(calculateSmsSegments("அ".repeat(70))).toMatchObject({
      encoding: "UCS-2",
      isUnicode: true,
      segments: 1,
      perSegmentLimit: 70,
      remainingInCurrentSegment: 0,
    });
    expect(calculateSmsSegments("அ".repeat(71))).toMatchObject({
      encoding: "UCS-2",
      isUnicode: true,
      segments: 2,
      perSegmentLimit: 67,
      remainingInCurrentSegment: 63,
    });
    expect(calculateSmsSegments("අ".repeat(134)).segments).toBe(2);
    expect(calculateSmsSegments("අ".repeat(135)).segments).toBe(3);
  });

  it("renders template tokens case-insensitively", () => {
    expect(
      renderNotificationTemplate("{instituteName}: {studentName} present", {
        institutename: "Royal Vidyalaya",
        studentName: "Nimal Perera",
      }),
    ).toBe("Royal Vidyalaya: Nimal Perera present");
  });
});
