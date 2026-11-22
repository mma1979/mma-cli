import { useMemo } from "react";
import { Copy, FilePlus2, FileMinus2, FilePen } from "lucide-react";
import type { FileChangeKind, PlanDiff, PlanFileDiff } from "../../api/types";
import { ApiProblem } from "../../api/problem";
import { Dialog } from "../../components/primitives/dialog";
import { Button } from "../../components/primitives/button";
import { Spinner } from "../../components/shared/Spinner";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { UnifiedDiffViewer } from "../../components/shared/UnifiedDiffViewer";
import { useDiffStore } from "../../stores/diffStore";
import { useToast } from "../../components/primitives/toast";
import { cn } from "../../lib/cn";

const KIND_LABEL: Record<FileChangeKind, string> = {
  Create: "Created",
  Modify: "Modified",
  Delete: "Deleted"
};

const KIND_TONE: Record<FileChangeKind, string> = {
  Create: "text-success",
  Modify: "text-primary",
  Delete: "text-danger"
};

const KIND_ICON: Record<FileChangeKind, typeof FilePlus2> = {
  Create: FilePlus2,
  Modify: FilePen,
  Delete: FileMinus2
};

export interface PlanDiffDrawerProps {
  open: boolean;
  plan: PlanDiff | null;
  loading: boolean;
  error?: unknown;
  applyBusy?: boolean;
  applyError?: unknown;
  onClose: () => void;
  onApply: () => void;
  onRefresh: () => void;
}

export function PlanDiffDrawer({
  open,
  plan,
  loading,
  error,
  applyBusy = false,
  applyError,
  onClose,
  onApply,
  onRefresh
}: PlanDiffDrawerProps) {
  const selectedFile = useDiffStore((state) => state.selectedFile);
  const selectFile = useDiffStore((state) => state.selectFile);
  const toast = useToast();

  const grouped = useMemo(() => {
    const groups: Record<FileChangeKind, PlanFileDiff[]> = {
      Create: [],
      Modify: [],
      Delete: []
    };
    for (const change of plan?.fileChanges ?? []) {
      groups[change.kind].push(change);
    }
    return groups;
  }, [plan]);

  const activeFile =
    plan?.fileChanges.find((change) => change.relativePath === selectedFile) ??
    plan?.fileChanges[0] ??
    null;

  const conflict =
    applyError instanceof ApiProblem && applyError.errorCode === "MANIFEST_CONFLICT"
      ? applyError
      : null;

  const canApply = Boolean(plan?.hasChanges) && !loading && !applyBusy;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Pending changes"
      description="Generated files that the next apply will write."
      size="xl"
      testId="plan-diff-drawer"
      footer={
        <>
          <Button variant="ghost" onClick={onRefresh} disabled={loading || applyBusy}>
            Refresh
          </Button>
          <Button variant="outline" onClick={onClose} disabled={applyBusy}>
            Close
          </Button>
          <Button onClick={onApply} disabled={!canApply} data-testid="plan-apply">
            {applyBusy ? "Applying…" : "Apply changes"}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {loading ? <Spinner label="Computing plan…" /> : null}

        {error ? (
          <ErrorBanner message="Could not compute the plan" detail={String(error)} onRetry={onRefresh} />
        ) : null}

        {conflict ? (
          <ErrorBanner
            message="The manifest changed since this plan was computed"
            detail="Preview the changes again before applying."
          />
        ) : null}

        {plan ? (
          <div className="flex flex-wrap items-center gap-2 text-[11px]">
            <span className="text-muted-foreground">plan hash</span>
            <code className="rounded bg-muted px-1.5 py-0.5 font-mono" data-testid="plan-hash">
              {plan.planHash.slice(0, 16)}
            </code>
            <Button
              size="sm"
              variant="ghost"
              aria-label="Copy plan hash"
              onClick={() => {
                void navigator.clipboard?.writeText(plan.planHash).then(
                  () => toast.push("Plan hash copied", "info"),
                  () => toast.push("Clipboard unavailable", "error")
                );
              }}
            >
              <Copy size={12} />
            </Button>
            <span className="ml-auto text-muted-foreground">
              {plan.fileChanges.length} file{plan.fileChanges.length === 1 ? "" : "s"}
            </span>
          </div>
        ) : null}

        {plan && !plan.hasChanges ? (
          <p className="rounded-md border border-border bg-muted/50 px-3 py-6 text-center text-xs text-muted-foreground">
            No pending changes.
          </p>
        ) : null}

        {plan && plan.hasChanges ? (
          <div className="grid gap-3 lg:grid-cols-[260px_minmax(0,1fr)]">
            <ul className="max-h-[52vh] overflow-y-auto rounded-md border border-border">
              {(["Create", "Modify", "Delete"] as FileChangeKind[]).map((kind) => {
                const files = grouped[kind];
                if (files.length === 0) {
                  return null;
                }
                const Icon = KIND_ICON[kind];
                return (
                  <li key={kind}>
                    <p className="flex items-center gap-1.5 bg-muted px-2 py-1 text-[10px] font-medium uppercase text-muted-foreground">
                      <Icon size={11} className={KIND_TONE[kind]} aria-hidden />
                      {KIND_LABEL[kind]} ({files.length})
                    </p>
                    <ul>
                      {files.map((file) => (
                        <li key={file.relativePath}>
                          <button
                            type="button"
                            data-testid={`plan-file-${file.relativePath}`}
                            className={cn(
                              "w-full truncate px-2 py-1 text-left font-mono text-[10px] hover:bg-muted",
                              activeFile?.relativePath === file.relativePath && "bg-muted"
                            )}
                            title={file.fullPath}
                            onClick={() => selectFile(file.relativePath)}
                          >
                            {file.relativePath}
                          </button>
                        </li>
                      ))}
                    </ul>
                  </li>
                );
              })}
            </ul>

            <div className="min-w-0 max-h-[52vh] overflow-auto rounded-md border border-border">
              {activeFile ? (
                <UnifiedDiffViewer diffText={activeFile.unifiedDiff} fileName={activeFile.relativePath} />
              ) : (
                <p className="p-4 text-xs text-muted-foreground">Select a file to see its diff.</p>
              )}
            </div>
          </div>
        ) : null}
      </div>
    </Dialog>
  );
}
