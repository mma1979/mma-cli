import { useEffect, useState } from "react";
import type { ApplyPayload } from "../../api/types";
import { Dialog } from "../../components/primitives/dialog";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";

export interface ApplyDialogProps {
  open: boolean;
  busy?: boolean;
  errorMessage?: string | undefined;
  onClose: () => void;
  onApply: (payload: ApplyPayload) => void;
}

export function ApplyDialog({
  open,
  busy = false,
  errorMessage,
  onClose,
  onApply
}: ApplyDialogProps) {
  const [runBuild, setRunBuild] = useState(false);
  const [createMigration, setCreateMigration] = useState(false);
  const [migrationName, setMigrationName] = useState("");
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setRunBuild(false);
      setCreateMigration(false);
      setMigrationName("");
      setLocalError(null);
    }
  }, [open]);

  const submit = (): void => {
    if (createMigration && !migrationName.trim()) {
      setLocalError("A migration name is required.");
      return;
    }
    setLocalError(null);

    const payload: ApplyPayload = { runBuild, createMigration };
    if (createMigration) {
      payload.migrationName = migrationName.trim();
    }
    onApply(payload);
  };

  const message = localError ?? errorMessage ?? null;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Apply pending changes"
      description="Writes the generated files into the solution."
      testId="apply-dialog"
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={busy} data-testid="apply-submit">
            {busy ? "Applying…" : "Apply"}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-xs">
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={runBuild}
            onChange={(event) => setRunBuild(event.target.checked)}
            data-testid="apply-run-build"
          />
          Run a build after applying
        </label>

        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={createMigration}
            onChange={(event) => setCreateMigration(event.target.checked)}
            data-testid="apply-create-migration"
          />
          Create an EF Core migration
        </label>

        {createMigration ? (
          <label className="block space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">Migration name</span>
            <Input
              value={migrationName}
              onChange={(event) => setMigrationName(event.target.value)}
              placeholder="InitialCreate"
              data-testid="apply-migration-name"
              invalid={Boolean(localError)}
            />
          </label>
        ) : null}

        {message ? (
          <p role="alert" className="text-[11px] text-danger">
            {message}
          </p>
        ) : null}
      </div>
    </Dialog>
  );
}
