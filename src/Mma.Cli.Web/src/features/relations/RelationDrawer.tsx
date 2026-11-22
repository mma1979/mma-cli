import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import type { AddRelationPayload, ManifestEntity } from "../../api/types";
import { Dialog } from "../../components/primitives/dialog";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { Select } from "../../components/primitives/select";

const RELATION_TYPES = ["1:N", "1:1", "N:M"] as const;
const DELETE_BEHAVIORS = ["Cascade", "Restrict", "SetNull", "NoAction"] as const;

export interface RelationDrawerProps {
  open: boolean;
  entities: ManifestEntity[];
  parentEntity: string | null;
  childEntity: string | null;
  busy?: boolean;
  errorMessage?: string | undefined;
  onClose: () => void;
  onSubmit: (payload: AddRelationPayload) => void;
}

export function RelationDrawer({
  open,
  entities,
  parentEntity,
  childEntity,
  busy = false,
  errorMessage,
  onClose,
  onSubmit
}: RelationDrawerProps) {
  const [parent, setParent] = useState(parentEntity ?? "");
  const [child, setChild] = useState(childEntity ?? "");
  const [foreignKey, setForeignKey] = useState("");
  const [foreignKeyType, setForeignKeyType] = useState("Guid");
  const [relationType, setRelationType] = useState<string>("1:N");
  const [deleteBehavior, setDeleteBehavior] = useState<string>("Cascade");
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    const nextParent = parentEntity ?? entities[0]?.name ?? "";
    const nextChild = childEntity ?? entities[1]?.name ?? entities[0]?.name ?? "";
    const parentModel = entities.find((entity) => entity.name === nextParent);

    setParent(nextParent);
    setChild(nextChild);
    setForeignKey(nextParent ? `${nextParent}Id` : "");
    setForeignKeyType(parentModel?.pkType ?? "Guid");
    setRelationType("1:N");
    setDeleteBehavior("Cascade");
    setLocalError(null);
  }, [open, parentEntity, childEntity, entities]);

  const submit = (event: FormEvent): void => {
    event.preventDefault();

    if (!parent || !child) {
      setLocalError("Both a parent and a child entity are required.");
      return;
    }

    if (parent === child) {
      setLocalError("A relation needs two different entities.");
      return;
    }

    if (!foreignKey.trim()) {
      setLocalError("Foreign key name is required.");
      return;
    }

    setLocalError(null);
    onSubmit({
      parentEntity: parent,
      childEntity: child,
      foreignKey: foreignKey.trim(),
      foreignKeyType,
      relationType,
      deleteBehavior
    });
  };

  const message = localError ?? errorMessage ?? null;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Add relation"
      description="Parent → child with the foreign key that carries the reference."
      testId="relation-drawer"
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={busy} data-testid="relation-submit">
            {busy ? "Saving…" : "Add relation"}
          </Button>
        </>
      }
    >
      <form onSubmit={submit} className="space-y-3">
        <div className="grid grid-cols-2 gap-2">
          <label className="space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">Parent</span>
            <Select
              value={parent}
              onChange={(event) => {
                const value = event.target.value;
                setParent(value);
                const model = entities.find((entity) => entity.name === value);
                setForeignKey(`${value}Id`);
                setForeignKeyType(model?.pkType ?? "Guid");
              }}
              data-testid="relation-parent"
            >
              {entities.map((entity) => (
                <option key={entity.name} value={entity.name}>
                  {entity.name}
                </option>
              ))}
            </Select>
          </label>

          <label className="space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">Child</span>
            <Select
              value={child}
              onChange={(event) => setChild(event.target.value)}
              data-testid="relation-child"
            >
              {entities.map((entity) => (
                <option key={entity.name} value={entity.name}>
                  {entity.name}
                </option>
              ))}
            </Select>
          </label>
        </div>

        <div className="grid grid-cols-2 gap-2">
          <label className="space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">Foreign key</span>
            <Input
              value={foreignKey}
              onChange={(event) => setForeignKey(event.target.value)}
              data-testid="relation-fk"
            />
          </label>

          <label className="space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">FK type</span>
            <Input
              value={foreignKeyType}
              onChange={(event) => setForeignKeyType(event.target.value)}
              className="font-mono"
              data-testid="relation-fk-type"
            />
          </label>
        </div>

        <div className="grid grid-cols-2 gap-2">
          <label className="space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">Cardinality</span>
            <Select
              value={relationType}
              onChange={(event) => setRelationType(event.target.value)}
              data-testid="relation-type"
            >
              {RELATION_TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </Select>
          </label>

          <label className="space-y-1">
            <span className="text-[10px] uppercase text-muted-foreground">Delete behavior</span>
            <Select
              value={deleteBehavior}
              onChange={(event) => setDeleteBehavior(event.target.value)}
              data-testid="relation-delete-behavior"
            >
              {DELETE_BEHAVIORS.map((behavior) => (
                <option key={behavior} value={behavior}>
                  {behavior}
                </option>
              ))}
            </Select>
          </label>
        </div>

        {message ? (
          <p role="alert" className="text-[11px] text-danger" data-testid="relation-error">
            {message}
          </p>
        ) : null}
      </form>
    </Dialog>
  );
}
