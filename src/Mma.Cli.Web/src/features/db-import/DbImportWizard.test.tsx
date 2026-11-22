import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { DbImportWizard } from "./DbImportWizard";
import { ToastProvider } from "../../components/primitives/toast";

const fetchMock = vi.fn<typeof fetch>();
const snapshot = {
  provider: "Sqlite",
  capturedAtUtc: "2026-01-01T00:00:00Z",
  relations: [],
  tables: [
    { schemaName: "main", tableName: "Customer", entityName: "Customer", pkType: "Guid", columns: [] },
    { schemaName: "main", tableName: "Order", entityName: "Order", pkType: "Guid", columns: [] }
  ]
};

function renderWizard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: 0 } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <MemoryRouter initialEntries={["/projects/7/db-import"]}>
          <Routes><Route path="/projects/:projectId/db-import" element={<DbImportWizard />} /></Routes>
        </MemoryRouter>
      </ToastProvider>
    </QueryClientProvider>
  );
}

describe("DbImportWizard", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.includes("/fs/browse")) {
        return new Response(JSON.stringify({ currentPath: "/work", parentPath: null, allowedRoots: ["/work"], entries: [] }), { status: 200 });
      }
      if (url.endsWith("/db-import/test")) {
        return new Response(JSON.stringify({ connected: true, provider: "Sqlite" }), { status: 200 });
      }
      if (url.endsWith("/db-import/introspect")) {
        return new Response(JSON.stringify(snapshot), { status: 200 });
      }
      if (url.endsWith("/db-import/apply")) {
        return new Response(JSON.stringify({ manifest: {}, contentHash: "hash", jobId: null, plan: null }), { status: 200 });
      }
      throw new Error(`Unexpected request: ${init?.method ?? "GET"} ${url}`);
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  it("guides connect → introspect → review → apply and omits credentials from apply", async () => {
    const user = userEvent.setup();
    renderWizard();

    await user.type(screen.getByLabelText("Server or SQLite path"), "/work/data.db");
    await user.type(screen.getByLabelText("Password"), "temporary-secret");
    await user.click(screen.getByRole("button", { name: "Test connection" }));
    await screen.findByRole("button", { name: "Introspect database" });
    await user.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByLabelText("Connection string")).toHaveValue("");
    expect(screen.getByLabelText("Password")).toHaveValue("");
    await user.type(screen.getByLabelText("Password"), "temporary-secret");
    await user.click(screen.getByRole("button", { name: "Test connection" }));
    await user.click(await screen.findByRole("button", { name: "Introspect database" }));
    await expect(screen.findByRole("heading", { name: /Review 2 of 2 tables/ })).resolves.toBeVisible();
    expect(fetchMock.mock.calls.slice(1, 4).map((call) => JSON.parse(String(call[1]?.body)))).toEqual([
      expect.objectContaining({ parameters: expect.objectContaining({ password: "temporary-secret" }) }),
      expect.objectContaining({ parameters: expect.objectContaining({ password: "temporary-secret" }) }),
      expect.objectContaining({ parameters: expect.objectContaining({ password: "temporary-secret" }) })
    ]);

    await user.click(screen.getByRole("button", { name: "Apply selected tables" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "4. Apply" })).toBeVisible());
    const applyCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith("/db-import/apply"));
    expect(applyCall).toBeDefined();
    const applyBody = JSON.parse(String(applyCall?.[1]?.body)) as Record<string, unknown>;
    expect(applyBody).not.toHaveProperty("connectionString");
    expect(applyBody).not.toHaveProperty("parameters");
    expect(JSON.stringify(applyBody)).not.toContain("temporary-secret");
    expect(applyBody["selectedTables"]).toEqual(["Customer", "Order"]);
  });
});
