import { describe, expect, it } from "vitest";
import { formatBytes } from "./api";

describe("operations helpers", () => {
  it("formats byte sizes into human-readable units", () => {
    expect(formatBytes(0)).toBe("0 B");
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
    expect(formatBytes(5 * 1024 * 1024)).toBe("5.0 MB");
  });
});
