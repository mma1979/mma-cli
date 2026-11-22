import { expect, test } from "@playwright/test";

const authSession = {
  authenticated: true,
  csrfToken: "e2e-csrf",
  userId: "e2e-user",
  displayName: "E2E User"
};

const project = {
  workspace: {
    id: 1,
    name: "Demo",
    solutionPath: "D:/work/Demo.sln",
    mapper: "AutoMapper",
    createdAtUtc: "2026-01-01T00:00:00Z",
    lastOpenedAtUtc: "2026-01-01T00:00:00Z",
    entityCount: 1,
    relationCount: 0,
    pendingChangesCount: 0,
    existsOnDisk: true,
    hasDrift: false
  },
  solutionInfo: {
    solutionName: "Demo",
    solutionPath: "D:/work/Demo.sln",
    projectsPath: "D:/work/Demo",
    solutionFilePath: "D:/work/Demo.sln",
    solutionFormat: "sln",
    detectedMapper: "AutoMapper"
  },
  manifest: {
    schemaVersion: 2,
    solutionName: "Demo",
    mapper: "AutoMapper",
    solutionFormat: "sln",
    entities: [{ name: "Order", pkType: "Guid", hasService: true, hasApi: true, applied: true, properties: [] }],
    relations: [],
    updatedAtUtc: "2026-01-01T00:00:00Z"
  },
  manifestHash: "e2e-manifest-hash",
  validation: { isValid: true, solutionName: "Demo", issues: [] }
};

test("settings remains a Phase 5 placeholder without provider requests", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ json: authSession }));
  let providerRequests = 0;
  await page.route("**/api/v1/providers**", async (route) => {
    providerRequests += 1;
    await route.fulfill({ status: 404, json: { title: "Not found" } });
  });

  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: "Settings" })).toBeVisible();
  await expect(page.getByText("Provider & MCP settings arrive in Phase 5")).toBeVisible();
  expect(providerRequests).toBe(0);
});

test("duplicate entity names are rejected before a request is sent", async ({ page }) => {
  await page.route("**/api/v1/auth/session", (route) => route.fulfill({ json: authSession }));
  let entityPosts = 0;
  await page.route("**/api/v1/projects/1", (route) => route.fulfill({ json: project }));
  await page.route("**/api/v1/projects/1/entities", async (route) => {
    if (route.request().method() === "POST") entityPosts += 1;
    await route.fulfill({ status: 200, json: { manifest: project.manifest, contentHash: "hash" } });
  });
  await page.route("**/api/v1/health", (route) => route.fulfill({ json: { status: "Healthy", version: "1", environment: "Test", dotnetEfAvailable: true, timestampUtc: "now" } }));

  await page.goto("/projects/1");
  await page.getByRole("button", { name: "Add entity" }).first().click();
  await page.getByTestId("entity-name").fill("order");
  await page.getByTestId("entity-submit").click();
  await expect(page.getByTestId("entity-error")).toContainText("already exists");
  expect(entityPosts).toBe(0);
});

test("creates a workspace, adds an entity and property, previews, then applies changes", async ({ page }) => {
  await page.goto("/");
  await page.getByTestId("new-solution").click();
  await page.getByTestId("new-solution-browse").click();
  await expect(page.getByTestId("directory-picker")).toBeVisible();
  await page.getByTestId("directory-use-current").click();
  const solutionName = `E2eWorkspace${Date.now()}`;
  await page.getByTestId("new-solution-name").fill(solutionName);
  await page.getByTestId("new-solution-submit").click();

  await expect(page.getByRole("heading", { name: solutionName })).toBeVisible({ timeout: 60_000 });
  await page.getByRole("button", { name: "Add entity" }).first().click();
  await page.getByTestId("entity-name").fill("Order");
  await page.getByTestId("entity-submit").click();
  await expect(page.getByTestId("erd-node-Order")).toBeVisible();
  await page.getByTestId("entity-rail-Order").click();
  await page.getByTestId("inspector-add-property").click();
  await page.getByTestId("property-name").fill("Title");
  await page.getByTestId("property-submit").click();
  await expect(page.getByText("Title").first()).toBeVisible();

  await page.getByRole("button", { name: "Add entity" }).click();
  await page.getByTestId("entity-name").fill("OrderItem");
  await page.getByTestId("entity-submit").click();
  await expect(page.getByTestId("erd-node-OrderItem")).toBeVisible();
  await page.getByRole("button", { name: "Add relation" }).click();
  await page.getByTestId("entity-rail-Order").click();
  await page.getByTestId("entity-rail-OrderItem").click();
  await expect(page.getByTestId("relation-drawer")).toBeVisible();
  await page.getByTestId("relation-submit").click();
  await expect(page.getByText("Order → OrderItem")).toBeVisible();

  const manifestHashBeforeApply = await page.getByTestId("manifest-hash").getAttribute("title");
  await page.getByTestId("preview-changes").click();
  await expect(page.getByTestId("plan-diff-drawer")).toBeVisible();
  await expect(page.getByTestId("plan-diff-drawer")).toContainText("Order.cs");
  await page.getByTestId("plan-apply").click();
  await page.getByTestId("apply-submit").click();
  await expect(page.getByTestId("job-terminal")).toBeVisible();
  await expect(page.getByTestId("job-terminal")).toHaveAttribute("data-status", "done", { timeout: 60_000 });
  await expect(page.getByTestId("erd-node-Order").getByText("Applied")).toBeVisible();
  await expect(page.getByTestId("erd-node-OrderItem").getByText("Applied")).toBeVisible();
  await expect.poll(() => page.getByTestId("manifest-hash").getAttribute("title")).not.toBe(manifestHashBeforeApply);
});
