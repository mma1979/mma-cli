import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { UnifiedDiffViewer } from "./UnifiedDiffViewer";

const patch = (oldName: string, newName: string, removed: string, added: string) =>
  `diff --git a/${oldName} b/${newName}\n--- a/${oldName}\n+++ b/${newName}\n@@ -1 +1 @@\n-${removed}\n+${added}`;

describe("UnifiedDiffViewer", () => {
  it.each([
    ["create", patch("/dev/null", "New.cs", "", "class New {}")],
    ["modify", patch("Old.cs", "Old.cs", "class Old {}", "class Updated {}")],
    ["delete", patch("Old.cs", "/dev/null", "class Old {}", "")]
  ])("renders a %s patch in plain text", (_kind, diffText) => {
    render(<UnifiedDiffViewer diffText={diffText} highlight={false} />);
    expect(screen.getByRole("region", { name: "Unified diff" })).toBeInTheDocument();
  });

  it("counts added and removed lines without loading the highlighter", () => {
    render(<UnifiedDiffViewer diffText={patch("A.cs", "A.cs", "before", "after")} highlight={false} />);
    expect(screen.getByText("+1")).toBeInTheDocument();
    expect(screen.getByText("−1")).toBeInTheDocument();
    expect(screen.getByText("after")).toBeInTheDocument();
  });
});
