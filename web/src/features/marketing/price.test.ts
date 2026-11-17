import { describe, expect, it } from "vitest";
import { comparisonCoversEveryFeature } from "./comparison";
import { addonPrice, formatMonthlyPrice, monthsFree, monthsFreeBadge, parseOptionalMoney } from "./price";

describe("formatMonthlyPrice", () => {
  it("leaves a missing price for the contact label", () => {
    expect(formatMonthlyPrice(null, "LKR")).toBeNull();
    expect(formatMonthlyPrice(undefined, "LKR")).toBeNull();
  });

  it("prints the currency and the amount", () => {
    expect(formatMonthlyPrice(4500, "LKR")).toBe("LKR 4,500");
  });
});

describe("plan price maths", () => {
  it("computes whole months free from monthly and yearly prices", () => {
    expect(monthsFree(4500, 45000)).toBe(2);
    expect(monthsFree(4500, 4500 * 12)).toBeNull();
    expect(monthsFree(null, 45000)).toBeNull();
    expect(monthsFree(4500, null)).toBeNull();
  });

  it("uses one badge when every plan saves the same number of months", () => {
    expect(
      monthsFreeBadge([
        { priceMonthlyLkr: 4500, priceYearlyLkr: 45000 },
        { priceMonthlyLkr: 9000, priceYearlyLkr: 90000 },
      ]),
    ).toEqual({ count: 2, varied: false });
  });

  it("marks the badge as varied when the saving differs", () => {
    expect(
      monthsFreeBadge([
        { priceMonthlyLkr: 4500, priceYearlyLkr: 45000 },
        { priceMonthlyLkr: 9000, priceYearlyLkr: 99000 },
      ]),
    ).toEqual({ count: 2, varied: true });
  });

  it("quotes one add-on price, or the lowest when plans disagree", () => {
    expect(addonPrice([2.5, 2.5, null])).toEqual({ kind: "exact", amount: 2.5 });
    expect(addonPrice([2, 3])).toEqual({ kind: "from", amount: 2 });
    expect(addonPrice([null, undefined])).toEqual({ kind: "none" });
  });

  it("parses a blank money field as unset and rejects junk", () => {
    expect(parseOptionalMoney("")).toBeNull();
    expect(parseOptionalMoney(" 1500.50 ")).toBe(1500.5);
    expect(parseOptionalMoney("-1")).toBeNaN();
    expect(parseOptionalMoney("12.345")).toBeNaN();
  });
});

describe("comparison groups", () => {
  it("lists every plan feature once", () => {
    expect(comparisonCoversEveryFeature()).toBe(true);
  });
});
