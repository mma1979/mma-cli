import { create } from "zustand";
import { api, setCsrfToken, setUnauthorizedHandler } from "../api/client";
import { ApiProblem } from "../api/problem";
import type { AuthSessionResponse, BootstrapAuthResponse } from "../api/types";

type AuthStatus = "unknown" | "authenticated" | "anonymous";

interface AuthState {
  status: AuthStatus;
  csrfToken: string | null;
  displayName: string;
  bootstrap: () => Promise<void>;
  markAnonymous: () => void;
}

let inflight: Promise<void> | null = null;

export const useAuthStore = create<AuthState>((set, get) => ({
  status: "unknown",
  csrfToken: null,
  displayName: "",

  markAnonymous: (): void => {
    setCsrfToken(null);
    set({ status: "anonymous", csrfToken: null, displayName: "" });
  },

  bootstrap: async (): Promise<void> => {
    if (get().status === "authenticated") {
      return;
    }
    if (inflight) {
      return inflight;
    }

    inflight = (async () => {
      const url = new URL(window.location.href);
      const token = url.searchParams.get("token");

      try {
        if (token) {
          const result = await api.post<BootstrapAuthResponse>("/api/v1/auth/bootstrap", {
            token
          });
          url.searchParams.delete("token");
          window.history.replaceState({}, "", url.toString());
          setCsrfToken(result.csrfToken);
          set({ status: "authenticated", csrfToken: result.csrfToken });
          return;
        }

        const session = await api.get<AuthSessionResponse>("/api/v1/auth/session");
        if (session.authenticated && session.csrfToken) {
          setCsrfToken(session.csrfToken);
          set({
            status: "authenticated",
            csrfToken: session.csrfToken,
            displayName: session.displayName
          });
          return;
        }

        setCsrfToken(null);
        set({ status: "anonymous", csrfToken: null, displayName: session.displayName });
      } catch (error) {
        setCsrfToken(null);
        set({ status: "anonymous", csrfToken: null });
        if (!(error instanceof ApiProblem)) {
          console.error(error);
        }
      } finally {
        inflight = null;
      }
    })();

    return inflight;
  }
}));

setUnauthorizedHandler(() => {
  const auth = useAuthStore.getState();
  auth.markAnonymous();
  void auth.bootstrap();
});
