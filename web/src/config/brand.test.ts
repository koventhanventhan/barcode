import { describe, expect, it } from "vitest";
import { ProductName } from "./brand";

describe("ProductName", () => {
  it("is EduFlow", () => {
    expect(ProductName).toBe("EduFlow");
  });
});
