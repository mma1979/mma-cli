import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { errorMessage } from "../../api/problem";
import { queryKeys } from "../../api/queryKeys";
import type { CreateWorkspaceResult, GenerationSummary, Mapper } from "../../api/types";
import { MAPPER_OPTIONS } from "../../api/types";
import { Dialog } from "../../components/primitives/dialog";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { Select } from "../../components/primitives/select";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { useToast } from "../../components/primitives/toast";
import { truncateMiddle } from "../../lib/format";
import { DirectoryPickerModal } from "./DirectoryPickerModal";

export interface NewSolutionModalProps {
  open: boolean;
  onClose: () => void;
  onCreated: (workspaceId: number) => void;
}

export function NewSolutionModal({ open, onClose, onCreated }: NewSolutionModalProps) {
  const queryClient = useQueryClient();
  const toast = useToast();

  const [parentDirectory, setParentDirectory] = useState("");
  const [solutionName, setSolutionName] = useState("");
  const [mapper, setMapper] = useState<Mapper>("AutoMapper");
  const [dryRun, setDryRun] = useState(false);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [summary, setSummary] = useState<GenerationSummary | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setSolutionName("");
      setDryRun(false);
      setSummary(null);
      setNameError(null);
    }
  }, [open]);

  const create = useMutation({
    mutationFn: () =>
      api.post<CreateWorkspaceResult>("/api/v1/workspaces", {
        parentDirectory,
        solutionName: solutionName.trim(),
        mapper,
        dryRun
      }),
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });

      if (result.workspace) {
        toast.push(`Created ${result.workspace.name}`, "success");
        onCreated(result.workspace.id);
        onClose();
        return;
      }

      setSummary(result.summary);
      toast.push("Dry run complete — no files were written", "info");
    }
  });

  const submit = (): void => {
    const trimmed = solutionName.trim();
    if (!trimmed) {
      setNameError("Solution name is required.");
      return;
    }
    if (/[\\/]/.test(trimmed)) {
      setNameError("Solution name cannot contain path separators.");
      return;
    }
    if (!parentDirectory) {
      setNameError("Choose a parent folder first.");
      return;
    }

    setNameError(null);
    create.mutate();
  };

  const summaryFiles = summary
    ? [
        ...summary.createdFiles.map((file) => ({ file, kind: "created" as const, marker: "+" })),
        ...summary.modifiedFiles.map((file) => ({ file, kind: "modified" as const, marker: "~" })),
        ...summary.deletedFiles.map((file) => ({ file, kind: "deleted" as const, marker: "−" }))
      ]
    : [];

  return (
    <>
      <Dialog
        open={open}
        onClose={onClose}
        title="New solution"
        description="Scaffold a new MMA solution in an allowed workspace root."
        testId="new-solution-modal"
        footer={
          <>
            <Button variant="outline" onClick={onClose} disabled={create.isPending}>
              Cancel
            </Button>
            <Button data-testid="new-solution-submit" onClick={submit} disabled={create.isPending}>
              {create.isPending ? "Creating…" : dryRun ? "Run dry run" : "Create solution"}
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <div className="space-y-1">
            <span className="text-xs font-medium">Parent folder</span>
            <div className="flex items-center gap-2">
              <Input
                readOnly
                value={parentDirectory ? truncateMiddle(parentDirectory, 52) : ""}
                placeholder="No folder selected"
                title={parentDirectory}
                data-testid="new-solution-parent"
              />
              <Button
                variant="outline"
                data-testid="new-solution-browse"
                onClick={() => setPickerOpen(true)}
              >
                Browse
              </Button>
            </div>
          </div>

          <label className="block space-y-1">
            <span className="text-xs font-medium">Solution name</span>
            <Input
              value={solutionName}
              onChange={(event) => setSolutionName(event.target.value)}
              placeholder="SampleApi"
              invalid={Boolean(nameError)}
              data-testid="new-solution-name"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-xs font-medium">Mapper</span>
            <Select
              value={mapper}
              onChange={(event) => setMapper(event.target.value as Mapper)}
              data-testid="new-solution-mapper"
            >
              {MAPPER_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </Select>
          </label>

          <label className="flex items-center gap-2 text-xs">
            <input
              type="checkbox"
              checked={dryRun}
              onChange={(event) => setDryRun(event.target.checked)}
              data-testid="new-solution-dry-run"
            />
            Dry run (compute the plan without writing files)
          </label>

          {nameError ? <p className="text-xs text-danger">{nameError}</p> : null}

          {create.isError ? (
            <ErrorBanner message="Could not create the solution" detail={errorMessage(create.error)} />
          ) : null}

          {summary ? (
            <div className="rounded-md border border-border bg-muted/50 p-3 text-xs">
              <p className="font-medium">Dry-run summary</p>
              <p className="mt-1 text-muted-foreground">
                {summary.createdFiles.length} created · {summary.modifiedFiles.length} modified ·{" "}
                {summary.deletedFiles.length} deleted
              </p>
              <ul className="mt-2 max-h-40 space-y-0.5 overflow-y-auto font-mono text-[10px]">
                {summaryFiles.map(({ file, kind, marker }) => (
                  <li key={`${kind}-${file}`}>{marker} {file}</li>
                ))}
              </ul>
              {summary.plan?.fileChanges.map((change) => (
                <details key={change.relativePath} className="mt-2 rounded border border-border p-2">
                  <summary className="cursor-pointer font-mono">{change.kind}: {change.relativePath}</summary>
                  <pre className="mt-2 max-h-56 overflow-auto whitespace-pre-wrap font-mono text-[10px]">{change.unifiedDiff}</pre>
                </details>
              ))}
            </div>
          ) : null}
        </div>
      </Dialog>

      <DirectoryPickerModal
        open={pickerOpen}
        title="Choose the parent folder"
        onClose={() => setPickerOpen(false)}
        onSelect={(selection) => {
          setParentDirectory(selection.path);
          setPickerOpen(false);
        }}
      />
    </>
  );
}
