import { describe, expect, it } from "vitest";
import en from "@/i18n/en.json";
import si from "@/i18n/si.json";
import ta from "@/i18n/ta.json";
import { ProductName } from "./brand";

describe("ProductName", () => {
  it("is EduFlow", () => {
    expect(ProductName).toBe("EduFlow");
  });
});

describe("locale catalogs", () => {
  it("share the home strings", () => {
    expect(Object.keys(ta.home).sort()).toEqual(Object.keys(en.home).sort());
    expect(Object.keys(si.home).sort()).toEqual(Object.keys(en.home).sort());
    expect(ta._status).toBe("TODO-i18n");
    expect(si._status).toBe("TODO-i18n");
  });
});
