import { Fragment, useState } from "react";
import { ChevronDown, ChevronRight, Plus, Trash2 } from "lucide-react";
import type { AddPropertyPayload, ManifestEntity } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Table, TBody, TD, TH, THead, TR } from "../../components/primitives/table";
import { StatusPill } from "../../components/shared/StatusPill";
import { ConfirmDialog } from "../../components/shared/ConfirmDialog";
import { PropertyEditor } from "./PropertyEditor";
import { cn } from "../../lib/cn";

export interface EntityTableViewProps {
  entities: ManifestEntity[];
  selectedEntity: string | null;
  busy?: boolean;
  errorMessage?: string | undefined;
  onSelectEntity: (name: string) => void;
  onAddProperty: (entityName: string, payload: AddPropertyPayload) => void;
  onRemoveProperty: (entityName: string, propertyName: string) => void;
  onRemoveEntity: (entityName: string) => void;
}

interface PendingRemoval {
  kind: "entity" | "property";
  entityName: string;
  propertyName?: string;
  applied: boolean;
}

export function EntityTableView({
  entities,
  selectedEntity,
  busy = false,
  errorMessage,
  onSelectEntity,
  onAddProperty,
  onRemoveProperty,
  onRemoveEntity
}: EntityTableViewProps) {
  const [expanded, setExpanded] = useState<string[]>(() =>
    selectedEntity ? [selectedEntity] : []
  );
  const [addingFor, setAddingFor] = useState<string | null>(null);
  const [pendingRemoval, setPendingRemoval] = useState<PendingRemoval | null>(null);

  const toggle = (name: string): void => {
    setExpanded((current) =>
      current.includes(name) ? current.filter((item) => item !== name) : [...current, name]
    );
  };

  const confirmRemoval = (): void => {
    if (!pendingRemoval) {
      return;
    }
    if (pendingRemoval.kind === "entity") {
      onRemoveEntity(pendingRemoval.entityName);
    } else if (pendingRemoval.propertyName) {
      onRemoveProperty(pendingRemoval.entityName, pendingRemoval.propertyName);
    }
    setPendingRemoval(null);
  };

  return (
    <div className="h-full overflow-y-auto p-4">
      <Table>
        <THead>
          <TR>
            <TH className="w-8" />
            <TH>Entity</TH>
            <TH className="w-20">PK</TH>
            <TH className="w-24">Status</TH>
            <TH className="w-20">Props</TH>
            <TH className="w-20">Flags</TH>
            <TH className="w-16" />
          </TR>
        </THead>
        <TBody>
          {entities.length === 0 ? (
            <TR>
              <TD colSpan={7} className="py-8 text-center text-xs text-muted-foreground">
                No entities yet.
              </TD>
            </TR>
          ) : (
            entities.map((entity) => {
              const isOpen = expanded.includes(entity.name);
              const isSelected = entity.name === selectedEntity;

              return (
                <Fragment key={entity.name}>
                  <TR className={cn(isSelected && "bg-muted/60")}>
                    <TD>
                      <button
                        type="button"
                        aria-label={isOpen ? `Collapse ${entity.name}` : `Expand ${entity.name}`}
                        aria-expanded={isOpen}
                        className="rounded p-1 hover:bg-muted"
                        onClick={() => toggle(entity.name)}
                      >
                        {isOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                      </button>
                    </TD>
                    <TD>
                      <button
                        type="button"
                        className="text-left text-xs font-medium hover:underline"
                        onClick={() => onSelectEntity(entity.name)}
                        data-testid={`entity-row-${entity.name}`}
                      >
                        {entity.name}
                      </button>
                    </TD>
                    <TD className="font-mono text-[11px] text-muted-foreground">{entity.pkType}</TD>
                    <TD>
                      <StatusPill status={entity.applied ? "applied" : "draft"} />
                    </TD>
                    <TD className="font-mono text-[11px]">{entity.properties.length}</TD>
                    <TD className="text-[10px] text-muted-foreground">
                      {entity.hasService ? "svc" : "—"} / {entity.hasApi ? "api" : "—"}
                    </TD>
                    <TD>
                      <Button
                        size="icon"
                        variant="ghost"
                        aria-label={`Remove ${entity.name}`}
                        onClick={() =>
                          setPendingRemoval({
                            kind: "entity",
                            entityName: entity.name,
                            applied: entity.applied
                          })
                        }
                      >
                        <Trash2 size={13} className="text-danger" />
                      </Button>
                    </TD>
                  </TR>

                  {isOpen ? (
                    <TR>
                      <TD colSpan={7} className="bg-muted/20">
                        <div className="space-y-2 py-1">
                          <table className="w-full text-[11px]">
                            <thead>
                              <tr className="text-left text-[10px] uppercase text-muted-foreground">
                                <th className="py-1">Property</th>
                                <th className="py-1">Type</th>
                                <th className="py-1">Nullable</th>
                                <th className="py-1">FK</th>
                                <th className="py-1">Max</th>
                                <th className="py-1">Required</th>
                                <th className="py-1">Status</th>
                                <th className="py-1" />
                              </tr>
                            </thead>
                            <tbody>
                              {entity.properties.length === 0 ? (
                                <tr>
                                  <td
                                    colSpan={8}
                                    className="py-2 text-center text-[11px] text-muted-foreground"
                                  >
                                    No properties.
                                  </td>
                                </tr>
                              ) : (
                                entity.properties.map((property) => (
                                  <tr key={property.name} className="border-t border-border/50">
                                    <td className="py-1 font-medium">{property.name}</td>
                                    <td className="py-1 font-mono text-muted-foreground">
                                      {property.dataType}
                                    </td>
                                    <td className="py-1">{property.nullable ? "yes" : "no"}</td>
                                    <td className="py-1">{property.isForeignKey ? "yes" : "no"}</td>
                                    <td className="py-1 font-mono">
                                      {property.maxLength ?? "—"}
                                    </td>
                                    <td className="py-1">
                                      {property.required === null || property.required === undefined
                                        ? "—"
                                        : property.required
                                          ? "required"
                                          : "optional"}
                                    </td>
                                    <td className="py-1">
                                      <StatusPill
                                        status={property.applied ? "applied" : "draft"}
                                      />
                                    </td>
                                    <td className="py-1 text-right">
                                      <Button
                                        size="icon"
                                        variant="ghost"
                                        aria-label={`Remove ${property.name}`}
                                        onClick={() =>
                                          setPendingRemoval({
                                            kind: "property",
                                            entityName: entity.name,
                                            propertyName: property.name,
                                            applied: property.applied
                                          })
                                        }
                                      >
                                        <Trash2 size={12} className="text-danger" />
                                      </Button>
                                    </td>
                                  </tr>
                                ))
                              )}
                            </tbody>
                          </table>

                          {addingFor === entity.name ? (
                            <PropertyEditor
                              entityName={entity.name}
                              existingNames={entity.properties.map((property) => property.name)}
                              busy={busy}
                              errorMessage={errorMessage}
                              onSubmit={(payload) => {
                                onAddProperty(entity.name, payload);
                                setAddingFor(null);
                              }}
                              onCancel={() => setAddingFor(null)}
                            />
                          ) : (
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={() => setAddingFor(entity.name)}
                              data-testid={`add-property-${entity.name}`}
                            >
                              <Plus size={12} /> Add property
                            </Button>
                          )}
                        </div>
                      </TD>
                    </TR>
                  ) : null}
                </Fragment>
              );
            })
          )}
        </TBody>
      </Table>

      <ConfirmDialog
        open={pendingRemoval !== null}
        title={
          pendingRemoval?.kind === "entity"
            ? `Remove ${pendingRemoval.entityName}?`
            : `Remove ${pendingRemoval?.propertyName ?? ""}?`
        }
        message={
          pendingRemoval?.applied
            ? "This item is already applied, so the next apply will schedule the generated code for removal."
            : "This item exists only in the manifest draft. Nothing has been generated for it yet."
        }
        confirmLabel="Remove"
        tone="danger"
        busy={busy}
        onConfirm={confirmRemoval}
        onCancel={() => setPendingRemoval(null)}
      />
    </div>
  );
}
