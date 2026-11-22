import { useEffect, useRef, useState } from "react";
import { openJobStream } from "../../api/sse";
import type { SseEnvelope } from "../../api/types";
import { Badge } from "../primitives/badge";
import { Button } from "../primitives/button";

type TerminalStatus = "connecting" | "streaming" | "done" | "error";

interface Entry {
  id: number;
  type: string;
  text: string;
}

function describeEnvelope(envelope: SseEnvelope): string {
  const payload = envelope.payload;

  if (typeof payload === "string") {
    return payload;
  }

  if (payload && typeof payload === "object") {
    const record = payload as Record<string, unknown>;
    for (const key of ["message", "detail", "line", "log", "stage"]) {
      const value = record[key];
      if (typeof value === "string" && value.trim()) {
        return value;
      }
    }
    try {
      return JSON.stringify(payload);
    } catch {
      return envelope.type;
    }
  }

  return envelope.type;
}

export interface LogTerminalProps {
  jobId: string;
  title?: string;
  onClose: () => void;
}

export function LogTerminal({ jobId, title = "Job output", onClose }: LogTerminalProps) {
  const [entries, setEntries] = useState<Entry[]>([]);
  const [status, setStatus] = useState<TerminalStatus>("connecting");
  const [stickToBottom, setStickToBottom] = useState(true);
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setEntries([]);
    setStatus("connecting");

    const close = openJobStream(jobId, {
      onEvent: (envelope) => {
        setStatus("streaming");
        setEntries((current) => [
          ...current,
          { id: current.length + 1, type: envelope.type, text: describeEnvelope(envelope) }
        ]);
      },
      onDone: () => setStatus("done"),
      onError: (message) => {
        setStatus("error");
        setEntries((current) => [
          ...current,
          { id: current.length + 1, type: "error", text: message }
        ]);
      }
    });

    return close;
  }, [jobId]);

  useEffect(() => {
    if (stickToBottom) {
      endRef.current?.scrollIntoView?.({ block: "end" });
    }
  }, [entries, stickToBottom]);

  const tone = status === "done" ? "success" : status === "error" ? "danger" : "info";

  return (
    <section
      data-testid="job-terminal"
      data-status={status}
      aria-label={title}
      className="flex h-56 flex-col rounded-md border border-border bg-card"
    >
      <header className="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
        <div className="flex items-center gap-2">
          <h3 className="text-xs font-semibold">{title}</h3>
          <Badge tone={tone}>{status}</Badge>
          <span className="font-mono text-[10px] text-muted-foreground">{jobId.slice(0, 12)}</span>
        </div>
        <div className="flex items-center gap-1">
          <Button
            size="sm"
            variant="ghost"
            aria-pressed={stickToBottom}
            onClick={() => setStickToBottom((current) => !current)}
          >
            {stickToBottom ? "Stick" : "Free"}
          </Button>
          <Button size="sm" variant="ghost" onClick={onClose}>
            Close
          </Button>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-3 py-2 font-mono text-[11px] leading-5">
        {entries.length === 0 ? (
          <p className="text-muted-foreground">Waiting for events…</p>
        ) : (
          entries.map((entry) => (
            <p
              key={entry.id}
              className={
                entry.type === "error"
                  ? "text-danger"
                  : entry.type === "progress"
                    ? "text-primary"
                    : "text-foreground"
              }
            >
              {entry.text}
            </p>
          ))
        )}
        <div ref={endRef} />
      </div>
    </section>
  );
}
