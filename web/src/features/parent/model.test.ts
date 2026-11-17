import { describe, expect, it } from "vitest";
import { formatMinutes, readSelectedChild, weekdayName } from "./model";

describe("parent child selection", () => {
  it("keeps the stored sibling when that child is still linked", () => {
    expect(readSelectedChild(["a", "b"], "b")).toBe("b");
  });

  it("falls back to the first linked child", () => {
    expect(readSelectedChild(["a", "b"], "missing")).toBe("a");
    expect(readSelectedChild([], "a")).toBe("");
  });

  it("formats class times and weekdays", () => {
    expect(weekdayName(1)).toBe("Mon");
    expect(formatMinutes(8 * 60 + 30)).toMatch(/8:30/);
  });
});
