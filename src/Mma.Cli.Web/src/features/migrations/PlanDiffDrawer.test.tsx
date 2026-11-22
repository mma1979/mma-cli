import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { PlanDiff } from "../../api/types";
import { ToastProvider } from "../../components/primitives/toast";
import { PlanDiffDrawer } from "./PlanDiffDrawer";

const plan: PlanDiff = {
  planHash: "0123456789abcdef0123456789abcdef",
  manifestHash: "fedcba9876543210",
  hasChanges: false,
  fileChanges: []
};

describe("PlanDiffDrawer", () => {
  it("shows the plan hash and disables apply for an empty plan", () => {
    render(
      <ToastProvider>
        <PlanDiffDrawer
          open
          plan={plan}
          loading={false}
          onClose={vi.fn()}
          onApply={vi.fn()}
          onRefresh={vi.fn()}
        />
      </ToastProvider>
    );

    expect(screen.getByTestId("plan-hash")).toHaveTextContent("0123456789abcdef");
    expect(screen.getByText("No pending changes.")).toBeInTheDocument();
    expect(screen.getByTestId("plan-apply")).toBeDisabled();
  });
});
