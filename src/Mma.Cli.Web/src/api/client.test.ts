import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiProblem } from "./problem";
import { api, setCsrfToken, setUnauthorizedHandler } from "./client";

const fetchMock = vi.fn<typeof fetch>();

describe("API client", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
    setCsrfToken(null);
    setUnauthorizedHandler(null);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("adds the CSRF token to mutating calls but not GET", async () => {
    fetchMock
      .mockResolvedValueOnce(new Response("{}", { status: 200, headers: { "content-type": "application/json" } }))
      .mockResolvedValueOnce(new Response("{}", { status: 200, headers: { "content-type": "application/json" } }));
    setCsrfToken("csrf-value");
    await api.get("/api/v1/workspaces");
    await api.post("/api/v1/workspaces", { name: "Demo" });
    const getInit = fetchMock.mock.calls[0]?.[1];
    const postInit = fetchMock.mock.calls[1]?.[1];
    expect(new Headers(getInit?.headers).has("X-Mma-Csrf")).toBe(false);
    expect(new Headers(postInit?.headers).get("X-Mma-Csrf")).toBe("csrf-value");
    expect(postInit?.credentials).toBe("include");
  });

  it("maps problem JSON to ApiProblem and handles empty 204", async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ status: 400, title: "Bad input", detail: "Name is required", errorCode: "VALIDATION_ERROR" }), { status: 400, headers: { "content-type": "application/problem+json" } }));
    await expect(api.get("/api/v1/workspaces")).rejects.toMatchObject({ errorCode: "VALIDATION_ERROR", detail: "Name is required", status: 400 });
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    await expect(api.del<void>("/api/v1/workspaces/1")).resolves.toBeUndefined();
  });

  it("notifies the unauthorized handler only once", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ title: "Unauthorized" }), { status: 401, headers: { "content-type": "application/problem+json" } }));
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    await expect(api.get("/api/v1/private")).rejects.toBeInstanceOf(ApiProblem);
    await expect(api.get("/api/v1/private")).rejects.toBeInstanceOf(ApiProblem);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
