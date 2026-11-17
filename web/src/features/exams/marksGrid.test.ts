import { describe, expect, it } from "vitest";
import { applyMarkPaste, cellKey } from "./marksGrid";

describe("marks grid paste", () => {
  it("fills cells from a spreadsheet paste and treats ABS as absent", () => {
    const students = ["s1", "s2"];
    const subjects = ["maths", "science"];
    const next = applyMarkPaste(students, subjects, 0, 0, "90\t80\nABS\t70\n", {});
    expect(next[cellKey("s1", "maths")]).toEqual({ marks: "90", absent: false });
    expect(next[cellKey("s1", "science")]).toEqual({ marks: "80", absent: false });
    expect(next[cellKey("s2", "maths")]).toEqual({ marks: "", absent: true });
    expect(next[cellKey("s2", "science")]).toEqual({ marks: "70", absent: false });
  });
});
