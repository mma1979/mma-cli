import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queryKeys";
import type { ProjectDetail } from "../../api/types";
import { shortHash } from "../../lib/format";
import { useAuthStore } from "../../stores/authStore";
import { useWorkspaceStore } from "../../stores/workspaceStore";

export function StatusBar() {
  const { projectId } = useParams<{ projectId: string }>();
  const parsedId = projectId ? Number.parseInt(projectId, 10) : null;
  const validId = parsedId !== null && Number.isFinite(parsedId) ? parsedId : null;

  const authStatus = useAuthStore((state) => state.status);
  const terminalJobId = useWorkspaceStore((state) => state.terminalJobId);

  const project = useQuery({
    queryKey: validId === null ? ["project", "none"] : queryKeys.project(validId),
    queryFn: ({ signal }) => api.get<ProjectDetail>(`/api/v1/projects/${validId}`, signal),
    enabled: validId !== null,
    retry: 0,
    staleTime: 5_000
  });

  const pending = project.data?.workspace.pendingChangesCount ?? 0;

  return (
    <footer className="flex h-7 shrink-0 items-center justify-between border-t border-border bg-card px-4 text-[10px] text-muted-foreground">
      <div className="flex items-center gap-4">
        <span>session: {authStatus}</span>
        {project.data ? (
          <>
            <span title={project.data.manifestHash}>
              manifest: {shortHash(project.data.manifestHash)}
            </span>
            <span>{pending} pending</span>
          </>
        ) : (
          <span>no project selected</span>
        )}
      </div>
      <div className="flex items-center gap-4">
        {terminalJobId ? <span className="font-mono">job {terminalJobId.slice(0, 8)}</span> : null}
        <span>local-only · 127.0.0.1</span>
      </div>
    </footer>
  );
}
