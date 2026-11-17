import { afterEach, describe, expect, it } from "vitest";
import { getSelectedBranch, setSelectedBranch } from "./session";

describe("selected branch", () => {
  afterEach(() => setSelectedBranch(null));

  it("stores one branch id and clears the consolidated choice", () => {
    setSelectedBranch("11111111-1111-1111-1111-111111111111");
    expect(getSelectedBranch()).toBe("11111111-1111-1111-1111-111111111111");
    setSelectedBranch(null);
    expect(getSelectedBranch()).toBeNull();
  });
});
