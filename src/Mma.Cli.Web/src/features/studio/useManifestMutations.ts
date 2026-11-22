import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queryKeys";
import { shortHash } from "../../lib/format";
import type {
  AddEntityPayload,
  AddPropertyPayload,
  AddRelationPayload,
  ApplyProjectResult,
  ApplyPayload,
  ManifestUpdate,
  ProjectDetail
} from "../../api/types";
import { useToast } from "../../components/primitives/toast";

function useApplyManifest(projectId: number) {
  const queryClient = useQueryClient();

  return (update: ManifestUpdate): void => {
    queryClient.setQueryData<ProjectDetail>(queryKeys.project(projectId), (current) =>
      current ? { ...current, manifest: update.manifest, manifestHash: update.contentHash } : current
    );
    void queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });
    void queryClient.invalidateQueries({ queryKey: queryKeys.plan(projectId) });
  };
}

export function useManifestMutations(projectId: number) {
  const applyManifest = useApplyManifest(projectId);
  const queryClient = useQueryClient();
  const toast = useToast();

  const addEntity = useMutation({
    mutationFn: (payload: AddEntityPayload) =>
      api.post<ManifestUpdate>(`/api/v1/projects/${projectId}/entities`, payload),
    onSuccess: (update) => {
      applyManifest(update);
      toast.push(`Entity saved (${shortHash(update.contentHash)})`, "success");
    }
  });

  const removeEntity = useMutation({
    mutationFn: (entityName: string) =>
      api.del<ManifestUpdate>(
        `/api/v1/projects/${projectId}/entities/${encodeURIComponent(entityName)}`
      ),
    onSuccess: (update) => {
      applyManifest(update);
      toast.push(`Entity removed (${shortHash(update.contentHash)})`, "success");
    }
  });

  const addProperty = useMutation({
    mutationFn: (input: { entityName: string; payload: AddPropertyPayload }) =>
      api.post<ManifestUpdate>(
        `/api/v1/projects/${projectId}/entities/${encodeURIComponent(input.entityName)}/properties`,
        input.payload
      ),
    onSuccess: (update) => {
      applyManifest(update);
      toast.push(`Property saved (${shortHash(update.contentHash)})`, "success");
    }
  });

  const removeProperty = useMutation({
    mutationFn: (input: { entityName: string; propertyName: string }) =>
      api.del<ManifestUpdate>(
        `/api/v1/projects/${projectId}/entities/${encodeURIComponent(
          input.entityName
        )}/properties/${encodeURIComponent(input.propertyName)}`
      ),
    onSuccess: (update) => {
      applyManifest(update);
      toast.push(`Property removed (${shortHash(update.contentHash)})`, "success");
    }
  });

  const addRelation = useMutation({
    mutationFn: (payload: AddRelationPayload) =>
      api.post<ManifestUpdate>(`/api/v1/projects/${projectId}/relations`, payload),
    onSuccess: (update) => {
      applyManifest(update);
      toast.push(`Relation saved (${shortHash(update.contentHash)})`, "success");
    }
  });

  const removeRelation = useMutation({
    mutationFn: (relationName: string) =>
      api.del<ManifestUpdate>(
        `/api/v1/projects/${projectId}/relations/${encodeURIComponent(relationName)}`
      ),
    onSuccess: (update) => {
      applyManifest(update);
      toast.push(`Relation removed (${shortHash(update.contentHash)})`, "success");
    }
  });

  const apply = useMutation({
    mutationFn: (payload: ApplyPayload) =>
      api.post<ApplyProjectResult>(`/api/v1/projects/${projectId}/apply`, payload),
    onSuccess: async (result) => {
      queryClient.setQueryData<ProjectDetail>(queryKeys.project(projectId), (current) =>
        current ? { ...current, manifest: result.manifest } : current
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.project(projectId) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });
      await queryClient.invalidateQueries({ queryKey: queryKeys.plan(projectId) });
      toast.push(
        `Applied ${result.createdFiles.length} created / ${result.modifiedFiles.length} modified`,
        "success"
      );
    }
  });

  return { addEntity, removeEntity, addProperty, removeProperty, addRelation, removeRelation, apply };
}
