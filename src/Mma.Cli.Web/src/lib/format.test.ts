import { describe, expect, it } from "vitest";
import { fileName, pluralize, shortHash, truncateMiddle } from "./format";

describe("format helpers", () => {
  it("formats hashes and paths safely", () => {
    expect(shortHash("sha256:1234567890", 4)).toBe("1234");
    expect(shortHash("")).toBe("—");
    expect(fileName("C:\\work\\demo\\project.mma")).toBe("project.mma");
  });

  it("formats labels without losing their boundaries", () => {
    expect(pluralize(1, "entity")).toBe("1 entity");
    expect(pluralize(2, "entity", "entities")).toBe("2 entities");
    expect(truncateMiddle("abcdefghij", 7)).toBe("abc…hij");
  });
});
