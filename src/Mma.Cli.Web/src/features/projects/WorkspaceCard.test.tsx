import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import type { WorkspaceSummary } from "../../api/types";
import { WorkspaceCard } from "./WorkspaceCard";

const workspace: WorkspaceSummary = {
  id: 7,
  name: "Orders",
  solutionPath: "D:/work/Orders.sln",
  mapper: "Mapster",
  createdAtUtc: "2026-01-01T00:00:00Z",
  lastOpenedAtUtc: "2026-01-01T00:00:00Z",
  entityCount: 2,
  relationCount: 1,
  pendingChangesCount: 3,
  existsOnDisk: false,
  hasDrift: true
};

describe("WorkspaceCard", () => {
  it("renders textual drift, pending and missing-on-disk states", () => {
    render(<MemoryRouter><WorkspaceCard workspace={workspace} /></MemoryRouter>);

    expect(screen.getByText("Drift")).toBeInTheDocument();
    expect(screen.getByText("3 pending")).toBeInTheDocument();
    expect(screen.getByText("Missing on disk")).toBeInTheDocument();
    expect(screen.getByText("Unavailable")).toBeInTheDocument();
  });
});
