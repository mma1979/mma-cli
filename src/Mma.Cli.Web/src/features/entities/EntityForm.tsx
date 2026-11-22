import { useState } from "react";
import type { FormEvent } from "react";
import type { AddEntityPayload } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { Select } from "../../components/primitives/select";

const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;
const PK_TYPES = ["Guid", "int", "long", "string"] as const;

export interface EntityFormProps {
  existingNames: string[];
  busy?: boolean;
  errorMessage?: string | undefined;
  onSubmit: (payload: AddEntityPayload) => void;
  onCancel: () => void;
}

export function EntityForm({
  existingNames,
  busy = false,
  errorMessage,
  onSubmit,
  onCancel
}: EntityFormProps) {
  const [name, setName] = useState("");
  const [pkType, setPkType] = useState<string>("Guid");
  const [hasService, setHasService] = useState(true);
  const [hasApi, setHasApi] = useState(true);
  const [localError, setLocalError] = useState<string | null>(null);

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    const trimmed = name.trim();

    if (!trimmed) {
      setLocalError("Entity name is required.");
      return;
    }

    if (!IDENTIFIER.test(trimmed)) {
      setLocalError("Entity name must be a valid C# identifier.");
      return;
    }

    if (existingNames.some((existing) => existing.toLowerCase() === trimmed.toLowerCase())) {
      setLocalError(`An entity named ${trimmed} already exists.`);
      return;
    }

    setLocalError(null);
    onSubmit({ name: trimmed, pkType, hasService, hasApi });
  };

  const message = localError ?? errorMessage ?? null;

  return (
    <form onSubmit={submit} className="space-y-3">
      <label className="block space-y-1">
        <span className="text-[10px] uppercase text-muted-foreground">Entity name</span>
        <Input
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="Order"
          data-testid="entity-name"
          invalid={Boolean(localError)}
          autoFocus
        />
      </label>

      <label className="block space-y-1">
        <span className="text-[10px] uppercase text-muted-foreground">Primary key type</span>
        <Select
          value={pkType}
          onChange={(event) => setPkType(event.target.value)}
          data-testid="entity-pk-type"
        >
          {PK_TYPES.map((type) => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </Select>
      </label>

      <div className="flex flex-wrap items-center gap-4 text-xs">
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={hasService}
            onChange={(event) => setHasService(event.target.checked)}
            data-testid="entity-has-service"
          />
          Generate service
        </label>
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={hasApi}
            onChange={(event) => setHasApi(event.target.checked)}
            data-testid="entity-has-api"
          />
          Generate API
        </label>
      </div>

      {message ? (
        <p role="alert" className="text-[11px] text-danger" data-testid="entity-error">
          {message}
        </p>
      ) : null}

      <div className="flex items-center justify-end gap-2">
        <Button variant="outline" onClick={onCancel} disabled={busy}>
          Cancel
        </Button>
        <Button type="submit" disabled={busy} data-testid="entity-submit">
          {busy ? "Saving…" : "Add entity"}
        </Button>
      </div>
    </form>
  );
}
