import { describe, expect, it } from "vitest";
import { formatMonthlyPrice } from "./price";

describe("formatMonthlyPrice", () => {
  it("leaves a missing price for the contact label", () => {
    expect(formatMonthlyPrice(null, "LKR")).toBeNull();
    expect(formatMonthlyPrice(undefined, "LKR")).toBeNull();
  });

  it("prints the currency and the amount", () => {
    expect(formatMonthlyPrice(4500, "LKR")).toBe("LKR 4,500");
  });
});
