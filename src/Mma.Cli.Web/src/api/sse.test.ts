import { afterEach, describe, expect, it, vi } from "vitest";
import { openJobStream, parseSseFrame } from "./sse";
import type { EventSourceLike } from "./sse";

class FakeEventSource implements EventSourceLike {
  listeners = new Map<string, (event: MessageEvent<string>) => void>();
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void { this.listeners.set(type, listener); }
  close(): void { this.closed = true; }
  emit(type: string, data: string): void { this.listeners.get(type)?.({ data } as unknown as MessageEvent<string>); }
}

const frame = (type: string) => JSON.stringify({ eventId: "1", timestamp: "now", type, jobOrSessionId: "job-1", payload: { message: "done" } });

afterEach(() => vi.useRealTimers());

describe("SSE job stream", () => {
  it("parses valid frames and ignores malformed payloads", () => {
    expect(parseSseFrame(frame("log"))).toMatchObject({ eventId: "1", type: "log", jobOrSessionId: "job-1" });
    expect(parseSseFrame("not json")).toBeNull();
    expect(parseSseFrame("{}" )).toBeNull();
  });

  it("closes the source when a done event arrives", () => {
    const source = new FakeEventSource();
    const onDone = vi.fn();
    const onEvent = vi.fn();
    openJobStream("job-1", { onEvent, onDone, createEventSource: () => source });
    source.emit("done", frame("done"));
    expect(onDone).toHaveBeenCalledOnce();
    expect(onEvent).toHaveBeenCalledOnce();
    expect(source.closed).toBe(true);
  });

  it("stops reconnecting after three retries", () => {
    vi.useFakeTimers();
    const sources: FakeEventSource[] = [];
    const onError = vi.fn();
    openJobStream("job-1", {
      onEvent: vi.fn(), onError, retryDelaysMs: [1, 2, 3],
      createEventSource: () => { const source = new FakeEventSource(); sources.push(source); return source; }
    });
    for (const delay of [1, 2, 3]) {
      sources[sources.length - 1]?.onerror?.(new Event("error"));
      vi.advanceTimersByTime(delay);
    }
    sources[sources.length - 1]?.onerror?.(new Event("error"));
    expect(sources).toHaveLength(4);
    expect(onError).toHaveBeenCalledOnce();
  });
});
