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

function leafKeys(value: unknown, prefix = ""): string[] {
  if (value && typeof value === "object" && !Array.isArray(value)) {
    return Object.entries(value as Record<string, unknown>).flatMap(([key, child]) =>
      key === "_status" ? [] : leafKeys(child, prefix ? `${prefix}.${key}` : key),
    );
  }
  return [prefix];
}

describe("locale catalogs", () => {
  it("share every string key", () => {
    expect(leafKeys(ta).sort()).toEqual(leafKeys(en).sort());
    expect(leafKeys(si).sort()).toEqual(leafKeys(en).sort());
    expect(ta._status).toBe("TODO-i18n");
    expect(si._status).toBe("TODO-i18n");
  });
});
