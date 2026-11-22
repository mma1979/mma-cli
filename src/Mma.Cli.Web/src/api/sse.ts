import type { SseEnvelope } from "./types";

export interface EventSourceLike {
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void;
  close(): void;
  onerror: ((event: Event) => void) | null;
}

export interface OpenJobStreamOptions {
  onEvent: (event: SseEnvelope) => void;
  onDone?: () => void;
  onError?: (message: string) => void;
  createEventSource?: (url: string) => EventSourceLike;
  retryDelaysMs?: number[];
}

export const SSE_EVENT_TYPES = ["progress", "log", "done", "error", "message"] as const;

export function parseSseFrame(raw: string): SseEnvelope | null {
  if (!raw) {
    return null;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  if (typeof parsed !== "object" || parsed === null) {
    return null;
  }

  const record = parsed as Record<string, unknown>;
  if (typeof record["type"] !== "string") {
    return null;
  }

  const envelope: SseEnvelope = {
    eventId: typeof record["eventId"] === "string" ? record["eventId"] : "",
    timestamp: typeof record["timestamp"] === "string" ? record["timestamp"] : "",
    type: record["type"],
    jobOrSessionId:
      typeof record["jobOrSessionId"] === "string" ? record["jobOrSessionId"] : ""
  };

  if ("payload" in record) {
    envelope.payload = record["payload"];
  }

  return envelope;
}

function payloadMessage(payload: unknown): string {
  if (typeof payload === "string" && payload.trim()) {
    return payload;
  }

  if (payload && typeof payload === "object") {
    const record = payload as Record<string, unknown>;
    for (const key of ["message", "detail", "error"]) {
      const value = record[key];
      if (typeof value === "string" && value.trim()) {
        return value;
      }
    }
  }

  return "The job failed.";
}

export function openJobStream(jobId: string, options: OpenJobStreamOptions): () => void {
  const create =
    options.createEventSource ??
    ((url: string): EventSourceLike => new EventSource(url) as unknown as EventSourceLike);
  const delays = options.retryDelaysMs ?? [500, 1000, 2000];

  let attempts = 0;
  let closed = false;
  let source: EventSourceLike | null = null;
  let timer: ReturnType<typeof setTimeout> | null = null;

  const cleanup = (): void => {
    if (timer) {
      clearTimeout(timer);
      timer = null;
    }
    if (source) {
      source.close();
      source = null;
    }
  };

  const closeWith = (message: string): void => {
    if (closed) {
      return;
    }
    closed = true;
    cleanup();
    options.onError?.(message);
  };

  const finish = (): void => {
    if (closed) {
      return;
    }
    closed = true;
    cleanup();
    options.onDone?.();
  };

  const connect = (): void => {
    if (closed) {
      return;
    }

    const current = create(`/api/v1/jobs/${encodeURIComponent(jobId)}/events`);
    source = current;

    const handle = (event: MessageEvent<string>): void => {
      const envelope = parseSseFrame(event.data);
      if (!envelope) {
        return;
      }

      options.onEvent(envelope);

      if (envelope.type === "done") {
        finish();
      } else if (envelope.type === "error") {
        closeWith(payloadMessage(envelope.payload));
      }
    };

    for (const type of SSE_EVENT_TYPES) {
      current.addEventListener(type, handle);
    }

    current.onerror = (): void => {
      if (closed) {
        return;
      }
      current.close();
      if (source === current) {
        source = null;
      }

      if (attempts >= delays.length) {
        closeWith("Lost connection to the job stream.");
        return;
      }

      const delay = delays[attempts] ?? 1000;
      attempts += 1;
      timer = setTimeout(() => {
        timer = null;
        connect();
      }, delay);
    };
  };

  connect();

  return () => {
    closed = true;
    cleanup();
  };
}
