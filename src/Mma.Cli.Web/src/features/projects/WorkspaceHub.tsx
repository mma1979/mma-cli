import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { Plus, FolderPlus } from "lucide-react";
import { api } from "../../api/client";
import { errorMessage } from "../../api/problem";
import { queryKeys } from "../../api/queryKeys";
import type { WorkspaceSummary } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { EmptyState } from "../../components/shared/EmptyState";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { Spinner } from "../../components/shared/Spinner";
import { NewSolutionModal } from "./NewSolutionModal";
import { RegisterSolutionModal } from "./RegisterSolutionModal";
import { WorkspaceCard } from "./WorkspaceCard";

export function WorkspaceHub() {
  const navigate = useNavigate();
  const [newOpen, setNewOpen] = useState(false);
  const [registerOpen, setRegisterOpen] = useState(false);

  const workspaces = useQuery({
    queryKey: queryKeys.workspaces,
    queryFn: ({ signal }) => api.get<WorkspaceSummary[]>("/api/v1/workspaces", signal),
    retry: 0
  });

  const openProject = (id: number): void => {
    navigate(`/projects/${id}`);
  };

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-6xl px-6 py-8">
        <header className="mb-6 flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold tracking-tight">Workspaces</h1>
            <p className="mt-1 text-xs text-muted-foreground">
              Local MMA solutions registered on this machine.
            </p>
          </div>

          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              data-testid="register-solution"
              onClick={() => setRegisterOpen(true)}
            >
              <FolderPlus size={14} /> Register existing
            </Button>
            <Button data-testid="new-solution" onClick={() => setNewOpen(true)}>
              <Plus size={14} /> New solution
            </Button>
          </div>
        </header>

        {workspaces.isError ? (
          <ErrorBanner
            message="Could not load workspaces"
            detail={errorMessage(workspaces.error)}
            onRetry={() => void workspaces.refetch()}
          />
        ) : null}

        {workspaces.isPending ? (
          <div className="py-10">
            <Spinner label="Loading workspaces…" />
          </div>
        ) : null}

        {workspaces.data && workspaces.data.length === 0 ? (
          <EmptyState
            title="No workspaces yet"
            description="Create a new MMA solution or register one that already exists on disk."
            action={
              <Button onClick={() => setNewOpen(true)}>
                <Plus size={14} /> New solution
              </Button>
            }
          />
        ) : null}

        {workspaces.data && workspaces.data.length > 0 ? (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {workspaces.data.map((workspace) => (
              <WorkspaceCard key={workspace.id} workspace={workspace} />
            ))}
          </div>
        ) : null}
      </div>

      <NewSolutionModal
        open={newOpen}
        onClose={() => setNewOpen(false)}
        onCreated={openProject}
      />
      <RegisterSolutionModal
        open={registerOpen}
        onClose={() => setRegisterOpen(false)}
        onRegistered={openProject}
      />
    </div>
  );
}
