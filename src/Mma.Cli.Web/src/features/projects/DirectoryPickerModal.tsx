import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowUp, Folder, FileCode2 } from "lucide-react";
import { api } from "../../api/client";
import type { BrowseDirectoryResponse, DirectoryEntry } from "../../api/types";
import { Dialog } from "../../components/primitives/dialog";
import { Button } from "../../components/primitives/button";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { Spinner } from "../../components/shared/Spinner";
import { errorMessage } from "../../api/problem";
import { truncateMiddle } from "../../lib/format";

export interface DirectoryPickerModalProps {
  open: boolean;
  title?: string;
  allowSolutionFiles?: boolean;
  onClose: () => void;
  onSelect: (selection: { path: string; isSolution: boolean }) => void;
}

function browseUrl(path: string | null): string {
  return path ? `/api/v1/fs/browse?path=${encodeURIComponent(path)}` : "/api/v1/fs/browse";
}

export function DirectoryPickerModal({
  open,
  title = "Choose a folder",
  allowSolutionFiles = false,
  onClose,
  onSelect
}: DirectoryPickerModalProps) {
  const [path, setPath] = useState<string | null>(null);

  const browse = useQuery({
    queryKey: ["browse", path],
    queryFn: ({ signal }) => api.get<BrowseDirectoryResponse>(browseUrl(path), signal),
    enabled: open,
    retry: 0,
    staleTime: 2_000
  });

  const data = browse.data;

  const openEntry = (entry: DirectoryEntry): void => {
    if (entry.isDirectory) {
      setPath(entry.fullPath);
      return;
    }

    if (entry.isSolution && allowSolutionFiles) {
      onSelect({ path: entry.fullPath, isSolution: true });
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      size="lg"
      testId="directory-picker"
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button
            data-testid="directory-use-current"
            disabled={!data}
            onClick={() => data && onSelect({ path: data.currentPath, isSolution: false })}
          >
            Use this folder
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex items-center gap-2">
          <Button
            size="icon"
            variant="outline"
            aria-label="Go to parent folder"
            data-testid="directory-up"
            disabled={!data?.parentPath}
            onClick={() => {
              if (data?.parentPath) {
                setPath(data.parentPath);
              }
            }}
          >
            <ArrowUp size={14} />
          </Button>
          <p className="min-w-0 flex-1 truncate font-mono text-[11px] text-muted-foreground" title={data?.currentPath}>
            {data ? truncateMiddle(data.currentPath, 90) : "Loading…"}
          </p>
        </div>

        {data && data.allowedRoots.length > 0 ? (
          <div className="flex flex-wrap items-center gap-1">
            <span className="text-[10px] uppercase text-muted-foreground">Roots</span>
            {data.allowedRoots.map((root) => (
              <button
                key={root}
                type="button"
                className="rounded border border-border px-1.5 py-0.5 font-mono text-[10px] hover:bg-muted"
                onClick={() => setPath(root)}
              >
                {truncateMiddle(root, 28)}
              </button>
            ))}
          </div>
        ) : null}

        {browse.isError ? (
          <ErrorBanner message="Unable to browse this folder" detail={errorMessage(browse.error)} />
        ) : null}

        {browse.isPending ? <Spinner label="Loading folders…" /> : null}

        {data ? (
          <ul className="max-h-72 divide-y divide-border overflow-y-auto rounded-md border border-border">
            {data.entries.length === 0 ? (
              <li className="px-3 py-4 text-center text-xs text-muted-foreground">Empty folder</li>
            ) : (
              data.entries.map((entry) => (
                <li key={entry.fullPath}>
                  <button
                    type="button"
                    data-testid={`directory-entry-${entry.name}`}
                    className="flex w-full items-center gap-2 px-3 py-2 text-left text-xs hover:bg-muted disabled:cursor-default disabled:opacity-60"
                    disabled={!entry.isDirectory && !(entry.isSolution && allowSolutionFiles)}
                    onClick={() => openEntry(entry)}
                  >
                    {entry.isDirectory ? (
                      <Folder size={14} className="text-muted-foreground" aria-hidden />
                    ) : (
                      <FileCode2 size={14} className="text-primary" aria-hidden />
                    )}
                    <span className="truncate">{entry.name}</span>
                    {entry.isSolution ? (
                      <span className="ml-auto rounded bg-primary/10 px-1.5 text-[10px] text-primary">
                        solution
                      </span>
                    ) : null}
                  </button>
                </li>
              ))
            )}
          </ul>
        ) : null}
      </div>
    </Dialog>
  );
}
