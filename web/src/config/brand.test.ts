import { describe, expect, it } from "vitest";
import { ProductName } from "./brand";

describe("ProductName", () => {
  it("is Admino", () => {
    expect(ProductName).toBe("Admino");
  });
});
