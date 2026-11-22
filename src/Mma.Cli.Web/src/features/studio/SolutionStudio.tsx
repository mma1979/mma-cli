import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, FileCode2, ScanSearch, Terminal } from "lucide-react";
import { api } from "../../api/client";
import { errorMessage } from "../../api/problem";
import { queryKeys } from "../../api/queryKeys";
import type { AddRelationPayload, PlanDiff, ProjectDetail } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Tabs } from "../../components/primitives/tabs";
import { Badge } from "../../components/primitives/badge";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { Spinner } from "../../components/shared/Spinner";
import { LogTerminal } from "../../components/shared/LogTerminal";
import { Dialog } from "../../components/primitives/dialog";
import { shortHash } from "../../lib/format";
import { useWorkspaceStore } from "../../stores/workspaceStore";
import { EntityRail } from "./EntityRail";
import { InspectorPanel } from "./InspectorPanel";
import { useManifestMutations } from "./useManifestMutations";
import { EntityForm } from "../entities/EntityForm";
import { EntityTableView } from "../entities/EntityTableView";
import { ErdCanvas } from "../erd/ErdCanvas";
import { RelationDrawer } from "../relations/RelationDrawer";
import { PlanDiffDrawer } from "../migrations/PlanDiffDrawer";
import { ApplyDialog } from "../migrations/ApplyDialog";
import { MigrationPanel } from "../migrations/MigrationPanel";
import { useToast } from "../../components/primitives/toast";

export function SolutionStudio() {
  const { projectId: rawId } = useParams<{ projectId: string }>();
  const projectId = Number.parseInt(rawId ?? "", 10);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();

  const activeView = useWorkspaceStore((state) => state.activeView);
  const setView = useWorkspaceStore((state) => state.setView);
  const selectedEntity = useWorkspaceStore((state) => state.selectedEntity);
  const selectEntity = useWorkspaceStore((state) => state.selectEntity);
  const setProject = useWorkspaceStore((state) => state.setProject);
  const planOpen = useWorkspaceStore((state) => state.planOpen);
  const setPlanOpen = useWorkspaceStore((state) => state.setPlanOpen);
  const terminalJobId = useWorkspaceStore((state) => state.terminalJobId);
  const openTerminal = useWorkspaceStore((state) => state.openTerminal);
  const closeTerminal = useWorkspaceStore((state) => state.closeTerminal);
  const addRelationMode = useWorkspaceStore((state) => state.addRelationMode);
  const relationParent = useWorkspaceStore((state) => state.relationParent);
  const startAddRelation = useWorkspaceStore((state) => state.startAddRelation);
  const setRelationParent = useWorkspaceStore((state) => state.setRelationParent);
  const cancelAddRelation = useWorkspaceStore((state) => state.cancelAddRelation);

  const [addEntityOpen, setAddEntityOpen] = useState(false);
  const [relationChild, setRelationChild] = useState<string | null>(null);
  const [applyOpen, setApplyOpen] = useState(false);

  const valid = Number.isFinite(projectId);

  useEffect(() => {
    if (valid) {
      setProject(projectId);
    }
  }, [projectId, setProject, valid]);

  const project = useQuery({
    queryKey: valid ? queryKeys.project(projectId) : (["project", "invalid"] as const),
    queryFn: ({ signal }) => api.get<ProjectDetail>(`/api/v1/projects/${projectId}`, signal),
    enabled: valid,
    retry: 0,
    staleTime: 5_000
  });

  const plan = useQuery({
    queryKey: valid ? queryKeys.plan(projectId) : (["plan", "invalid"] as const),
    queryFn: () => api.post<PlanDiff>(`/api/v1/projects/${projectId}/plan-diff`),
    enabled: valid && planOpen,
    retry: 0,
    staleTime: 0
  });

  const mutations = useManifestMutations(valid ? projectId : -1);

  if (!valid) {
    return (
      <div className="p-8">
        <ErrorBanner message="Invalid project id" detail={rawId ?? "(missing)"} />
      </div>
    );
  }

  if (project.isPending) {
    return (
      <div className="p-8">
        <Spinner label="Loading project…" />
      </div>
    );
  }

  if (project.isError || !project.data) {
    return (
      <div className="space-y-3 p-8">
        <ErrorBanner
          message="Could not load the project"
          detail={errorMessage(project.error)}
          onRetry={() => void project.refetch()}
        />
        <Button variant="outline" onClick={() => navigate("/")}>
          <ArrowLeft size={13} /> Back to workspaces
        </Button>
      </div>
    );
  }

  const detail = project.data;
  const manifest = detail.manifest;
  const entity = manifest.entities.find((item) => item.name === selectedEntity) ?? null;
  const validation = detail.validation;

  const handleSelectEntity = (name: string): void => {
    if (addRelationMode) {
      if (!relationParent) {
        setRelationParent(name);
        return;
      }
      if (relationParent !== name) {
        setRelationChild(name);
        selectEntity(name);
      }
      return;
    }
    selectEntity(name);
  };

  const handleRelationSubmit = (payload: AddRelationPayload): void => {
    mutations.addRelation.mutate(payload, {
      onSuccess: () => {
        setRelationChild(null);
        cancelAddRelation();
      }
    });
  };

  const handleApply = (payload: {
    runBuild: boolean;
    createMigration: boolean;
    migrationName?: string;
  }): void => {
    const planHash = plan.data?.planHash;
    mutations.apply.mutate(
      { ...payload, ...(planHash ? { planHash } : {}) },
      {
        onSuccess: (result) => {
          setApplyOpen(false);
          setPlanOpen(false);
          openTerminal(result.jobId);
        },
        onError: (error) => {
          if (errorMessage(error).toLowerCase().includes("manifest")) {
            setApplyOpen(false);
            setPlanOpen(false);
            void queryClient.invalidateQueries({ queryKey: queryKeys.plan(projectId) });
          }
        }
      }
    );
  };

  const relationError = mutations.addRelation.error;
  const mutationError =
    mutations.addEntity.error ??
    mutations.addProperty.error ??
    mutations.removeEntity.error ??
    mutations.removeProperty.error ??
    mutations.removeRelation.error;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex flex-wrap items-center gap-3 border-b border-border bg-card px-4 py-2">
        <Button size="icon" variant="ghost" aria-label="Back to workspaces" onClick={() => navigate("/")}>
          <ArrowLeft size={15} />
        </Button>

        <div className="min-w-0">
          <h1 className="truncate text-sm font-semibold" title={detail.solutionInfo.solutionName}>
            {detail.solutionInfo.solutionName}
          </h1>
          <p className="truncate font-mono text-[10px] text-muted-foreground" title={detail.solutionInfo.solutionPath}>
            {detail.solutionInfo.projectsPath}
          </p>
        </div>

        <Badge tone="info">{manifest.mapper}</Badge>
        <Badge title={detail.manifestHash} data-testid="manifest-hash">manifest {shortHash(detail.manifestHash)}</Badge>
        {detail.workspace.pendingChangesCount > 0 ? (
          <Badge tone="warning">{detail.workspace.pendingChangesCount} pending</Badge>
        ) : null}
        {detail.workspace.hasDrift ? <Badge tone="danger">Drift</Badge> : null}

        <div className="ml-auto flex items-center gap-2">
          <Button size="sm" variant="outline" onClick={() => navigate(`/projects/${projectId}/templates`)}>
            <FileCode2 size={13} /> Templates
          </Button>
          <Button size="sm" variant="outline" onClick={() => navigate(`/projects/${projectId}/db-import`)}>
            <ScanSearch size={13} /> Import database
          </Button>
          <Button size="sm" onClick={() => setPlanOpen(true)} data-testid="preview-changes">
            <Terminal size={13} /> Preview changes
          </Button>
        </div>
      </header>

      {!validation.isValid ? (
        <div className="border-b border-danger/40 bg-danger/10 px-4 py-2">
          <p className="text-[11px] font-medium text-danger">
            Manifest validation failed ({validation.issues.length} issue
            {validation.issues.length === 1 ? "" : "s"})
          </p>
          <ul className="mt-1 space-y-0.5">
            {validation.issues.slice(0, 5).map((issue, index) => (
              <li key={`${issue.code}-${index}`} className="text-[10px] text-muted-foreground">
                <span className="font-mono text-danger">{issue.severity}</span> {issue.code}:{" "}
                {issue.message}
                {issue.target ? ` (${issue.target})` : ""}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <div className="flex items-center gap-2 border-b border-border px-3 py-1.5">
        <Tabs
          label="Workspace view"
          value={activeView}
          onValueChange={(value) => setView(value === "table" ? "table" : "erd")}
          items={[
            { value: "erd", label: "ERD" },
            { value: "table", label: "Table" }
          ]}
        />
        {mutationError ? (
          <p role="alert" className="ml-2 truncate text-[11px] text-danger">
            {errorMessage(mutationError)}
          </p>
        ) : null}
      </div>

      <div className="flex min-h-0 flex-1">
        <EntityRail
          entities={manifest.entities}
          relations={manifest.relations}
          selectedEntity={selectedEntity}
          onSelectEntity={handleSelectEntity}
        />

        <div className="min-w-0 flex-1">
          {activeView === "erd" ? (
            <ErdCanvas
              entities={manifest.entities}
              relations={manifest.relations}
              selectedEntity={selectedEntity}
              addRelationMode={addRelationMode}
              relationParent={relationParent}
              onSelectEntity={handleSelectEntity}
              onAddEntity={() => setAddEntityOpen(true)}
              onStartAddRelation={startAddRelation}
              onCancelAddRelation={cancelAddRelation}
            />
          ) : (
            <EntityTableView
              entities={manifest.entities}
              selectedEntity={selectedEntity}
              busy={mutations.addProperty.isPending || mutations.removeProperty.isPending}
              errorMessage={mutations.addProperty.error ? errorMessage(mutations.addProperty.error) : undefined}
              onSelectEntity={selectEntity}
              onAddProperty={(entityName, payload) =>
                mutations.addProperty.mutate({ entityName, payload })
              }
              onRemoveProperty={(entityName, propertyName) =>
                mutations.removeProperty.mutate({ entityName, propertyName })
              }
              onRemoveEntity={(entityName) => {
                mutations.removeEntity.mutate(entityName);
                selectEntity(null);
              }}
            />
          )}
        </div>

        {activeView === "erd" && selectedEntity ? (
          <InspectorPanel
            entity={entity}
            relations={manifest.relations}
            busy={mutations.addProperty.isPending || mutations.removeProperty.isPending}
            errorMessage={mutations.addProperty.error ? errorMessage(mutations.addProperty.error) : undefined}
            onClose={() => selectEntity(null)}
            onAddProperty={(payload) => {
              if (entity) {
                mutations.addProperty.mutate({ entityName: entity.name, payload });
              }
            }}
            onRemoveProperty={(propertyName) => {
              if (entity) {
                mutations.removeProperty.mutate({ entityName: entity.name, propertyName });
              }
            }}
            onRemoveEntity={(entityName) => {
              mutations.removeEntity.mutate(entityName);
              selectEntity(null);
            }}
            onSelectEntity={selectEntity}
          />
        ) : null}
      </div>

      {terminalJobId ? (
        <div className="border-t border-border p-2">
          <LogTerminal jobId={terminalJobId} onClose={closeTerminal} />
        </div>
      ) : null}

      <MigrationPanel projectId={projectId} onJobStarted={openTerminal} />

      <Dialog
        open={addEntityOpen}
        onClose={() => setAddEntityOpen(false)}
        title="Add entity"
        description="Entities become generated models, services, and endpoints."
        testId="add-entity-dialog"
      >
        <EntityForm
          existingNames={manifest.entities.map((item) => item.name)}
          busy={mutations.addEntity.isPending}
          errorMessage={mutations.addEntity.error ? errorMessage(mutations.addEntity.error) : undefined}
          onCancel={() => setAddEntityOpen(false)}
          onSubmit={(payload) => {
            mutations.addEntity.mutate(payload, {
              onSuccess: () => {
                setAddEntityOpen(false);
                selectEntity(payload.name);
              }
            });
          }}
        />
      </Dialog>

      <RelationDrawer
        open={relationChild !== null}
        entities={manifest.entities}
        parentEntity={relationParent}
        childEntity={relationChild}
        busy={mutations.addRelation.isPending}
        errorMessage={relationError ? errorMessage(relationError) : undefined}
        onClose={() => {
          setRelationChild(null);
          cancelAddRelation();
        }}
        onSubmit={handleRelationSubmit}
      />

      <PlanDiffDrawer
        open={planOpen}
        plan={plan.data ?? null}
        loading={plan.isFetching}
        error={plan.error}
        applyBusy={mutations.apply.isPending}
        applyError={mutations.apply.error}
        onClose={() => setPlanOpen(false)}
        onRefresh={() => void plan.refetch()}
        onApply={() => {
          if (!plan.data?.hasChanges) {
            toast.push("Nothing to apply", "info");
            return;
          }
          setApplyOpen(true);
        }}
      />

      <ApplyDialog
        open={applyOpen}
        busy={mutations.apply.isPending}
        errorMessage={mutations.apply.error ? errorMessage(mutations.apply.error) : undefined}
        onClose={() => setApplyOpen(false)}
        onApply={handleApply}
      />
    </div>
  );
}
