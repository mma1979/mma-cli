import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import type { AddPropertyPayload, ManifestEntity, ManifestRelation } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { StatusPill } from "../../components/shared/StatusPill";
import { EmptyState } from "../../components/shared/EmptyState";
import { PropertyEditor } from "../entities/PropertyEditor";

export interface InspectorPanelProps {
  entity: ManifestEntity | null;
  relations: ManifestRelation[];
  busy?: boolean;
  errorMessage?: string | undefined;
  onClose: () => void;
  onAddProperty: (payload: AddPropertyPayload) => void;
  onRemoveProperty: (propertyName: string) => void;
  onRemoveEntity: (entityName: string) => void;
  onSelectEntity: (name: string) => void;
}

export function InspectorPanel({
  entity,
  relations,
  busy = false,
  errorMessage,
  onClose,
  onAddProperty,
  onRemoveProperty,
  onRemoveEntity,
  onSelectEntity
}: InspectorPanelProps) {
  const [adding, setAdding] = useState(false);

  if (!entity) {
    return (
      <aside aria-label="Inspector" className="flex w-80 shrink-0 flex-col border-l border-border bg-card">
        <div className="p-4">
          <EmptyState title="Nothing selected" description="Pick an entity on the canvas or in the rail." />
        </div>
      </aside>
    );
  }

  const entityRelations = relations.filter(
    (relation) => relation.parentEntity === entity.name || relation.childEntity === entity.name
  );

  return (
    <aside
      aria-label="Inspector"
      data-testid="inspector"
      className="flex w-80 shrink-0 flex-col overflow-y-auto border-l border-border bg-card"
    >
      <header className="flex items-start justify-between gap-2 border-b border-border px-3 py-2">
        <div className="min-w-0">
          <h2 className="truncate text-sm font-semibold" title={entity.name}>
            {entity.name}
          </h2>
          <p className="mt-0.5 font-mono text-[10px] text-muted-foreground">
            pk {entity.pkType} · {entity.hasService ? "service" : "no service"} ·{" "}
            {entity.hasApi ? "api" : "no api"}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          <StatusPill status={entity.applied ? "applied" : "draft"} />
          <Button size="icon" variant="ghost" aria-label="Close inspector" onClick={onClose}>
            ×
          </Button>
        </div>
      </header>

      <section className="space-y-2 border-b border-border p-3">
        <div className="flex items-center justify-between">
          <h3 className="text-[10px] uppercase tracking-wide text-muted-foreground">Properties</h3>
          {adding ? null : (
            <Button size="sm" variant="ghost" onClick={() => setAdding(true)} data-testid="inspector-add-property">
              <Plus size={12} /> Add
            </Button>
          )}
        </div>

        {entity.properties.length === 0 ? (
          <p className="text-[11px] text-muted-foreground">No properties yet.</p>
        ) : (
          <ul className="divide-y divide-border/60">
            {entity.properties.map((property) => (
              <li key={property.name} className="flex items-center gap-2 py-1 text-[11px]">
                <span className="min-w-0 flex-1 truncate">{property.name}</span>
                <span className="font-mono text-[10px] text-muted-foreground">
                  {property.dataType}
                  {property.nullable ? "?" : ""}
                </span>
                <Button
                  size="icon"
                  variant="ghost"
                  aria-label={`Remove ${property.name}`}
                  disabled={busy}
                  onClick={() => onRemoveProperty(property.name)}
                >
                  <Trash2 size={11} className="text-danger" />
                </Button>
              </li>
            ))}
          </ul>
        )}

        {adding ? (
          <PropertyEditor
            entityName={entity.name}
            existingNames={entity.properties.map((property) => property.name)}
            busy={busy}
            errorMessage={errorMessage}
            onSubmit={(payload) => {
              onAddProperty(payload);
              setAdding(false);
            }}
            onCancel={() => setAdding(false)}
          />
        ) : null}
      </section>

      <section className="space-y-2 border-b border-border p-3">
        <h3 className="text-[10px] uppercase tracking-wide text-muted-foreground">Relations</h3>
        {entityRelations.length === 0 ? (
          <p className="text-[11px] text-muted-foreground">No relations.</p>
        ) : (
          <ul className="space-y-1">
            {entityRelations.map((relation) => (
              <li key={relation.name} className="rounded border border-border px-2 py-1 text-[11px]">
                <div className="flex items-center justify-between gap-2">
                  <span className="truncate">
                    {relation.parentEntity} → {relation.childEntity}
                  </span>
                  <StatusPill status={relation.applied ? "applied" : "draft"} />
                </div>
                <p className="mt-0.5 font-mono text-[10px] text-muted-foreground">
                  {relation.foreignKey} · {relation.relationType} · {relation.deleteBehavior}
                </p>
              </li>
            ))}
          </ul>
        )}

        <div className="flex flex-wrap gap-1">
          {entityRelations
            .flatMap((relation) => [relation.parentEntity, relation.childEntity])
            .filter((name, index, all) => name !== entity.name && all.indexOf(name) === index)
            .map((name) => (
              <button
                key={name}
                type="button"
                className="rounded border border-border px-1.5 py-0.5 text-[10px] hover:bg-muted"
                onClick={() => onSelectEntity(name)}
              >
                {name}
              </button>
            ))}
        </div>
      </section>

      <footer className="mt-auto flex items-center justify-between gap-2 p-3">
        <span className="text-[10px] text-muted-foreground">
          {entity.applied ? "Applied to disk" : "Draft only"}
        </span>
        <Button
          size="sm"
          variant="danger"
          disabled={busy}
          onClick={() => onRemoveEntity(entity.name)}
          data-testid="inspector-remove-entity"
        >
          <Trash2 size={12} /> Remove
        </Button>
      </footer>
    </aside>
  );
}
