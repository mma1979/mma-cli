import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../api/client";
import { useAuthStore } from "./authStore";

const fetchMock = vi.fn<typeof fetch>();

describe("authStore unauthorized recovery", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
    useAuthStore.setState({ status: "authenticated", csrfToken: "expired", displayName: "E2E User" });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    useAuthStore.setState({ status: "unknown", csrfToken: null, displayName: "" });
  });

  it("retries session bootstrap once after a protected request returns 401", async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: "Unauthorized", errorCode: "UNAUTHORIZED" }), { status: 401, headers: { "content-type": "application/problem+json" } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ authenticated: true, csrfToken: "fresh", userId: "user-1", displayName: "User" }), { status: 200, headers: { "content-type": "application/json" } }));

    await expect(api.get("/api/v1/private")).rejects.toMatchObject({ status: 401 });
    await vi.waitFor(() => expect(useAuthStore.getState().status).toBe("authenticated"));

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(useAuthStore.getState().csrfToken).toBe("fresh");
  });
});
