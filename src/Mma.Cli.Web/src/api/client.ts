import { toApiProblem } from "./problem";

const CSRF_HEADER = "X-Mma-Csrf";

let csrfToken: string | null = null;
let unauthorizedHandler: (() => void) | null = null;
let unauthorizedNotified = false;

export function setCsrfToken(token: string | null): void {
  csrfToken = token;
  unauthorizedNotified = false;
}

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? "GET";
  const headers = new Headers({ Accept: "application/json" });

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (method !== "GET" && method !== "HEAD" && csrfToken) {
    headers.set(CSRF_HEADER, csrfToken);
  }

  const init: RequestInit = { method, credentials: "include", headers };
  if (options.body !== undefined) {
    init.body = JSON.stringify(options.body);
  }
  if (options.signal) {
    init.signal = options.signal;
  }

  const response = await fetch(path, init);

  if (response.status === 401) {
    if (!unauthorizedNotified) {
      unauthorizedNotified = true;
      unauthorizedHandler?.();
    }
    throw await toApiProblem(response);
  }

  if (!response.ok) {
    throw await toApiProblem(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal): Promise<T> =>
    request<T>(path, signal ? { signal } : {}),
  post: <T>(path: string, body?: unknown): Promise<T> => request<T>(path, { method: "POST", body }),
  put: <T>(path: string, body?: unknown): Promise<T> => request<T>(path, { method: "PUT", body }),
  del: <T>(path: string): Promise<T> => request<T>(path, { method: "DELETE" })
};
