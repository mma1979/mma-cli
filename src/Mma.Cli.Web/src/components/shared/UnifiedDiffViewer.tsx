import { useEffect, useMemo, useState } from "react";
import { parsePatch } from "diff";
import type { Highlighter, ThemedToken } from "shiki";
import { cn } from "../../lib/cn";

type RowKind = "add" | "del" | "context" | "hunk" | "meta";

interface Row {
  index: number;
  kind: RowKind;
  text: string;
}

function buildRows(diffText: string): Row[] {
  const rows: Row[] = [];
  let index = 0;

  let patches;
  try {
    patches = parsePatch(diffText);
  } catch {
    return diffText.split("\n").map((line) => ({
      index: index++,
      kind: "context" as RowKind,
      text: line
    }));
  }

  for (const patch of patches) {
    for (const hunk of patch.hunks) {
      rows.push({
        index: index++,
        kind: "hunk",
        text: `@@ -${hunk.oldStart},${hunk.oldLines} +${hunk.newStart},${hunk.newLines} @@`
      });

      for (const line of hunk.lines) {
        const prefix = line.charAt(0);
        const kind: RowKind =
          prefix === "+"
            ? "add"
            : prefix === "-"
              ? "del"
              : prefix === "\\"
                ? "meta"
                : "context";
        rows.push({ index: index++, kind, text: line });
      }
    }
  }

  if (rows.length === 0) {
    return diffText.split("\n").map((line) => ({
      index: index++,
      kind: "context" as RowKind,
      text: line
    }));
  }

  return rows;
}

function useHighlighter(enabled: boolean): Highlighter | null {
  const [highlighter, setHighlighter] = useState<Highlighter | null>(null);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    let cancelled = false;

    void import("shiki")
      .then(async (shiki) => {
        const instance = await shiki.createHighlighter({
          themes: ["github-dark"],
          langs: ["csharp"]
        });
        if (!cancelled) {
          setHighlighter(instance);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setHighlighter(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return highlighter;
}

export interface UnifiedDiffViewerProps {
  diffText: string;
  fileName?: string;
  highlight?: boolean;
}

export function UnifiedDiffViewer({ diffText, fileName, highlight = true }: UnifiedDiffViewerProps) {
  const rows = useMemo(() => buildRows(diffText), [diffText]);
  const highlighter = useHighlighter(highlight);

  const tokensByRow = useMemo(() => {
    if (!highlighter) {
      return null;
    }

    const map = new Map<number, ThemedToken[]>();
    for (const row of rows) {
      if (row.kind === "hunk" || row.kind === "meta") {
        continue;
      }

      const content = row.text.slice(1);
      if (!content.trim()) {
        continue;
      }

      try {
        const result = highlighter.codeToTokens(content, { lang: "csharp", theme: "github-dark" });
        const tokens = result.tokens[0];
        if (tokens) {
          map.set(row.index, tokens);
        }
      } catch {
        // Highlighting is best-effort; the plain text row still renders.
      }
    }

    return map;
  }, [highlighter, rows]);

  const added = rows.filter((row) => row.kind === "add").length;
  const removed = rows.filter((row) => row.kind === "del").length;

  return (
    <section aria-label={fileName ? `Unified diff for ${fileName}` : "Unified diff"} className="min-w-0">
      <header className="flex items-center justify-between gap-3 border-b border-border bg-muted px-3 py-1.5 text-[11px] text-muted-foreground">
        <span className="truncate font-mono">{fileName ?? "diff"}</span>
        <span className="shrink-0 font-mono">
          <span className="text-success">+{added}</span> <span className="text-danger">−{removed}</span>
        </span>
      </header>

      <div className="overflow-x-auto bg-card py-1">
        {rows.map((row) => {
          const tokens = tokensByRow?.get(row.index);
          return (
            <div
              key={row.index}
              className={cn(
                "diff-line",
                row.kind === "add" && "diff-add",
                row.kind === "del" && "diff-del",
                row.kind === "hunk" && "diff-hunk"
              )}
            >
              {row.kind === "hunk" ? (
                <span>{row.text}</span>
              ) : (
                <>
                  <span className="select-none text-muted-foreground">{row.text.charAt(0)}</span>
                  {tokens ? (
                    tokens.map((token, tokenIndex) => (
                      <span key={tokenIndex} style={{ color: token.color }}>
                        {token.content}
                      </span>
                    ))
                  ) : (
                    <span>{row.text.slice(1) || " "}</span>
                  )}
                </>
              )}
            </div>
          );
        })}
      </div>
    </section>
  );
}
