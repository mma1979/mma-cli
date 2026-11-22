export interface ProblemPayload {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
  [key: string]: unknown;
}

export class ApiProblem extends Error {
  readonly status: number;
  readonly errorCode: string;
  readonly detail: string | undefined;
  readonly title: string | undefined;

  constructor(status: number, errorCode: string, detail: string | undefined, title: string | undefined) {
    super(detail ?? title ?? `Request failed with status ${status}`);
    this.name = "ApiProblem";
    this.status = status;
    this.errorCode = errorCode;
    this.detail = detail;
    this.title = title;
  }
}

export function isApiProblem(value: unknown): value is ApiProblem {
  return value instanceof ApiProblem;
}

export function errorMessage(value: unknown): string {
  if (value instanceof ApiProblem) {
    return value.detail ?? value.title ?? value.errorCode;
  }
  if (value instanceof Error) {
    return value.message;
  }
  return "Unexpected error.";
}

export async function toApiProblem(response: Response): Promise<ApiProblem> {
  let payload: ProblemPayload | null = null;
  const contentType = response.headers.get("content-type") ?? "";
  try {
    if (contentType.includes("json")) {
      payload = (await response.json()) as ProblemPayload;
    } else {
      const text = await response.text();
      payload = text ? { detail: text } : null;
    }
  } catch {
    payload = null;
  }

  const errorCode =
    payload?.errorCode ?? (response.status === 401 ? "UNAUTHORIZED" : `HTTP_${response.status}`);

  return new ApiProblem(response.status, errorCode, payload?.detail, payload?.title);
}
