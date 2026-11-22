import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import type { ManifestEntity, ManifestRelation } from "../../api/types";
import { Input } from "../../components/primitives/input";
import { StatusPill } from "../../components/shared/StatusPill";
import { cn } from "../../lib/cn";

export interface EntityRailProps {
  entities: ManifestEntity[];
  relations: ManifestRelation[];
  selectedEntity: string | null;
  onSelectEntity: (name: string) => void;
}

export function EntityRail({
  entities,
  relations,
  selectedEntity,
  onSelectEntity
}: EntityRailProps) {
  const [filter, setFilter] = useState("");

  const counts = useMemo(() => {
    const map = new Map<string, { properties: number; relations: number }>();
    for (const entity of entities) {
      map.set(entity.name, { properties: entity.properties.length, relations: 0 });
    }
    for (const relation of relations) {
      for (const name of [relation.parentEntity, relation.childEntity]) {
        const current = map.get(name);
        if (current) {
          map.set(name, { ...current, relations: current.relations + 1 });
        }
      }
    }
    return map;
  }, [entities, relations]);

  const visible = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) {
      return entities;
    }
    return entities.filter((entity) => entity.name.toLowerCase().includes(needle));
  }, [entities, filter]);

  return (
    <aside
      aria-label="Entities"
      className="flex w-60 shrink-0 flex-col border-r border-border bg-card"
    >
      <div className="border-b border-border p-2">
        <label className="relative block">
          <span className="sr-only">Filter entities</span>
          <Search
            size={13}
            className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            placeholder="Filter entities"
            className="h-8 pl-7 text-xs"
            data-testid="entity-filter"
          />
        </label>
      </div>

      <ul className="min-h-0 flex-1 overflow-y-auto py-1">
        {visible.length === 0 ? (
          <li className="px-3 py-4 text-center text-[11px] text-muted-foreground">
            {entities.length === 0 ? "No entities" : "No matches"}
          </li>
        ) : (
          visible.map((entity) => {
            const stats = counts.get(entity.name);
            const selected = entity.name === selectedEntity;
            return (
              <li key={entity.name}>
                <button
                  type="button"
                  data-testid={`entity-rail-${entity.name}`}
                  aria-current={selected ? "true" : undefined}
                  className={cn(
                    "flex w-full items-center gap-2 px-3 py-2 text-left text-xs transition-colors",
                    selected ? "bg-muted text-foreground" : "text-muted-foreground hover:bg-muted/60"
                  )}
                  onClick={() => onSelectEntity(entity.name)}
                >
                  <span className="min-w-0 flex-1 truncate">{entity.name}</span>
                  <span className="shrink-0 font-mono text-[10px]">
                    {stats?.properties ?? 0}p · {stats?.relations ?? 0}r
                  </span>
                  <StatusPill status={entity.applied ? "applied" : "draft"} />
                </button>
              </li>
            );
          })
        )}
      </ul>

      <footer className="border-t border-border px-3 py-2 text-[10px] text-muted-foreground">
        {entities.length} entities · {relations.length} relations
      </footer>
    </aside>
  );
}
