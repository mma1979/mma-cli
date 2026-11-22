import { beforeEach, describe, expect, it } from "vitest";
import { useWorkspaceStore } from "./workspaceStore";

describe("workspaceStore", () => {
  beforeEach(() => useWorkspaceStore.getState().setProject(1));

  it("clears project-specific selection when the project changes", () => {
    useWorkspaceStore.getState().selectEntity("Order");
    useWorkspaceStore.getState().selectRelation("OrderItems");
    useWorkspaceStore.getState().setPlanOpen(true);
    useWorkspaceStore.getState().setProject(2);
    expect(useWorkspaceStore.getState().selectedEntity).toBeNull();
    expect(useWorkspaceStore.getState().selectedRelation).toBeNull();
    expect(useWorkspaceStore.getState().planOpen).toBe(false);
  });

  it("retains the active view when switching projects", () => {
    useWorkspaceStore.getState().setView("table");
    useWorkspaceStore.getState().setProject(2);
    expect(useWorkspaceStore.getState().activeView).toBe("table");
  });
});
