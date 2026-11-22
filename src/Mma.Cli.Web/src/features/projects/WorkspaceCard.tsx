import { Link } from "react-router-dom";
import { ArrowRight, FolderOpen } from "lucide-react";
import type { WorkspaceSummary } from "../../api/types";
import { Badge } from "../../components/primitives/badge";
import { StatusPill } from "../../components/shared/StatusPill";
import { pluralize, truncateMiddle } from "../../lib/format";

export interface WorkspaceCardProps {
  workspace: WorkspaceSummary;
}

export function WorkspaceCard({ workspace }: WorkspaceCardProps) {
  const missing = !workspace.existsOnDisk;

  return (
    <article
      data-testid={`workspace-card-${workspace.id}`}
      className="flex flex-col gap-3 rounded-lg border border-border bg-card p-4"
    >
      <header className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 className="truncate text-sm font-semibold" title={workspace.name}>
            {workspace.name}
          </h2>
          <p
            className="mt-1 truncate font-mono text-[10px] text-muted-foreground"
            title={workspace.solutionPath}
          >
            {truncateMiddle(workspace.solutionPath, 46)}
          </p>
        </div>
        <Badge tone="info">{workspace.mapper}</Badge>
      </header>

      <div className="flex flex-wrap items-center gap-1.5">
        <Badge>{pluralize(workspace.entityCount, "entity", "entities")}</Badge>
        <Badge>{pluralize(workspace.relationCount, "relation")}</Badge>
        {workspace.pendingChangesCount > 0 ? (
          <Badge tone="warning">{workspace.pendingChangesCount} pending</Badge>
        ) : null}
        {workspace.hasDrift ? <StatusPill status="drift" /> : null}
        {missing ? <Badge tone="danger">Missing on disk</Badge> : null}
      </div>

      <footer className="mt-auto flex items-center justify-between">
        <span className="text-[10px] text-muted-foreground">
          updated {new Date(workspace.lastOpenedAtUtc).toLocaleDateString()}
        </span>
        {missing ? (
          <span className="text-[10px] text-muted-foreground">Unavailable</span>
        ) : (
          <Link
            to={`/projects/${workspace.id}`}
            className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
          >
            Open <ArrowRight size={12} />
          </Link>
        )}
      </footer>
    </article>
  );
}

export function WorkspaceCardIcon() {
  return <FolderOpen size={16} aria-hidden />;
}
