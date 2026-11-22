import { useState } from "react";
import type { FormEvent } from "react";
import { SUPPORTED_PROPERTY_TYPES } from "../../api/types";
import type { AddPropertyPayload } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { Select } from "../../components/primitives/select";

type RequiredState = "unset" | "true" | "false";

const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;

export interface PropertyEditorProps {
  entityName: string;
  existingNames: string[];
  busy?: boolean;
  errorMessage?: string | undefined;
  onSubmit: (payload: AddPropertyPayload) => void;
  onCancel: () => void;
}

export function PropertyEditor({
  entityName,
  existingNames,
  busy = false,
  errorMessage,
  onSubmit,
  onCancel
}: PropertyEditorProps) {
  const [name, setName] = useState("");
  const [dataType, setDataType] = useState<string>(SUPPORTED_PROPERTY_TYPES[3]);
  const [nullable, setNullable] = useState(false);
  const [isForeignKey, setIsForeignKey] = useState(false);
  const [maxLength, setMaxLength] = useState("");
  const [required, setRequired] = useState<RequiredState>("unset");
  const [localError, setLocalError] = useState<string | null>(null);

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    const trimmed = name.trim();

    if (!trimmed) {
      setLocalError("Property name is required.");
      return;
    }

    if (!IDENTIFIER.test(trimmed)) {
      setLocalError("Property name must be a valid C# identifier.");
      return;
    }

    if (existingNames.some((existing) => existing.toLowerCase() === trimmed.toLowerCase())) {
      setLocalError(`Entity ${entityName} already has a property named ${trimmed}.`);
      return;
    }

    let parsedMaxLength: number | undefined;
    if (maxLength.trim()) {
      if (!/^-?\d+$/.test(maxLength.trim())) {
        setLocalError("Max length must be a whole number.");
        return;
      }
      const value = Number.parseInt(maxLength.trim(), 10);
      if (!Number.isSafeInteger(value) || value <= 0) {
        setLocalError("Max length must be greater than zero.");
        return;
      }
      parsedMaxLength = value;
    }

    setLocalError(null);

    const payload: AddPropertyPayload = {
      name: trimmed,
      dataType,
      nullable,
      isForeignKey
    };

    if (parsedMaxLength !== undefined) {
      payload.maxLength = parsedMaxLength;
    }

    if (required !== "unset") {
      payload.required = required === "true";
    }

    onSubmit(payload);
  };

  const message = localError ?? errorMessage ?? null;

  return (
    <form onSubmit={submit} className="space-y-3 rounded-md border border-border bg-muted/40 p-3">
      <p className="text-[11px] font-medium">Add property to {entityName}</p>

      <div className="grid grid-cols-2 gap-2">
        <label className="space-y-1">
          <span className="text-[10px] uppercase text-muted-foreground">Name</span>
          <Input
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="Title"
            className="h-8 text-xs"
            data-testid="property-name"
            invalid={Boolean(localError)}
          />
        </label>

        <label className="space-y-1">
          <span className="text-[10px] uppercase text-muted-foreground">Type</span>
          <Select
            value={dataType}
            onChange={(event) => setDataType(event.target.value)}
            className="h-8 text-xs"
            data-testid="property-type"
          >
            {SUPPORTED_PROPERTY_TYPES.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </Select>
        </label>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <label className="space-y-1">
          <span className="text-[10px] uppercase text-muted-foreground">Max length</span>
          <Input
            value={maxLength}
            onChange={(event) => setMaxLength(event.target.value)}
            placeholder="optional"
            inputMode="numeric"
            className="h-8 text-xs"
            data-testid="property-max-length"
          />
        </label>

        <label className="space-y-1">
          <span className="text-[10px] uppercase text-muted-foreground">Required</span>
          <Select
            value={required}
            onChange={(event) => setRequired(event.target.value as RequiredState)}
            className="h-8 text-xs"
            data-testid="property-required"
          >
            <option value="unset">Unset</option>
            <option value="true">Required</option>
            <option value="false">Optional</option>
          </Select>
        </label>
      </div>

      <div className="flex flex-wrap items-center gap-4 text-[11px]">
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={nullable}
            onChange={(event) => setNullable(event.target.checked)}
            data-testid="property-nullable"
          />
          Nullable
        </label>
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={isForeignKey}
            onChange={(event) => setIsForeignKey(event.target.checked)}
            data-testid="property-fk"
          />
          Foreign key
        </label>
      </div>

      {message ? (
        <p role="alert" className="text-[11px] text-danger" data-testid="property-error">
          {message}
        </p>
      ) : null}

      <div className="flex items-center justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={onCancel} disabled={busy}>
          Cancel
        </Button>
        <Button size="sm" type="submit" disabled={busy} data-testid="property-submit">
          {busy ? "Saving…" : "Add property"}
        </Button>
      </div>
    </form>
  );
}
