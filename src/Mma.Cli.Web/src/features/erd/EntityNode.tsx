import { Handle, Position } from "@xyflow/react";
import type { Node, NodeProps } from "@xyflow/react";
import { KeyRound, Link2 } from "lucide-react";
import type { EntityNodeData } from "./layout";
import { StatusPill } from "../../components/shared/StatusPill";
import { cn } from "../../lib/cn";

export function EntityNode({ data, selected }: NodeProps<Node<EntityNodeData>>) {
  const { entity } = data;

  return (
    <div
      data-testid={`erd-node-${entity.name}`}
      className={cn(
        "w-60 rounded-lg border bg-card text-left shadow-sm",
        selected ? "border-primary ring-2 ring-primary/30" : "border-border"
      )}
    >
      <Handle type="target" position={Position.Top} className="!bg-border" />

      <header className="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
        <span className="truncate text-xs font-semibold" title={entity.name}>
          {entity.name}
        </span>
        <span className="flex shrink-0 items-center gap-1">
          <span className="rounded bg-muted px-1 font-mono text-[10px] text-muted-foreground">
            {entity.pkType}
          </span>
          <StatusPill status={entity.applied ? "applied" : "draft"} />
        </span>
      </header>

      <ul className="divide-y divide-border/60">
        {entity.properties.length === 0 ? (
          <li className="px-3 py-2 text-[11px] text-muted-foreground">No properties</li>
        ) : (
          entity.properties.map((property) => (
            <li
              key={property.name}
              className="flex items-center gap-1.5 px-3 py-[3px] text-[11px]"
            >
              {property.isForeignKey ? (
                <Link2 size={10} className="shrink-0 text-primary" aria-hidden />
              ) : (
                <span className="w-[10px] shrink-0" aria-hidden />
              )}
              <span className="truncate">{property.name}</span>
              {property.name.toLowerCase() === "id" ? (
                <KeyRound size={10} className="shrink-0 text-warning" aria-label="Primary key" />
              ) : null}
              <span className="ml-auto shrink-0 font-mono text-[10px] text-muted-foreground">
                {property.dataType}
                {property.nullable ? "?" : ""}
              </span>
              {property.maxLength ? (
                <span className="shrink-0 rounded bg-muted px-1 font-mono text-[9px] text-muted-foreground">
                  {property.maxLength}
                </span>
              ) : null}
            </li>
          ))
        )}
      </ul>

      <Handle type="source" position={Position.Bottom} className="!bg-border" />
    </div>
  );
}
