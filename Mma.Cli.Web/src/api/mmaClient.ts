import {
  ApiError,
  BrowseFsResponse,
  CodeGenApplyResult,
  CodeGenPreview,
  CreateProjectRequest,
  HealthInfo,
  ProjectSchemaResponse,
  ProjectSummary,
  SaveSchemaResponse,
} from '../types/mma';
import { SchemaModel } from '../types/schema';

export class MmaApiError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(message: string, code: string, status: number) {
    super(message);
    this.name = 'MmaApiError';
    this.code = code;
    this.status = status;
  }
}

async function apiFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    credentials: 'same-origin',
    ...init,
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(init?.headers ?? {}),
    },
  });

  if (response.status === 204) {
    return undefined as T;
  }

  let data: unknown = null;
  try {
    data = await response.json();
  } catch {
    data = null;
  }

  if (!response.ok) {
    const err = data as Partial<ApiError> | null;
    throw new MmaApiError(
      err?.error || `Request failed (${response.status}).`,
      err?.code || 'request_failed',
      response.status
    );
  }

  return data as T;
}

export async function bootstrapSession(): Promise<{ health: HealthInfo; queryPath: string | null }> {
  const url = new URL(window.location.href);
  const token = url.searchParams.get('token');
  const queryPath = url.searchParams.get('path');

  if (token) {
    await apiFetch<void>('/api/session/exchange', {
      method: 'POST',
      body: JSON.stringify({ token }),
    });
    url.searchParams.delete('token');
    window.history.replaceState({}, document.title, url.pathname + url.search + url.hash);
  }

  const health = await apiFetch<HealthInfo>('/api/health');
  return { health, queryPath: queryPath || health.initialPath || null };
}

export const mmaClient = {
  getHealth: () => apiFetch<HealthInfo>('/api/health'),

  browseFs: (path?: string) =>
    apiFetch<BrowseFsResponse>(
      path ? `/api/fs/browse?path=${encodeURIComponent(path)}` : '/api/fs/browse'
    ),

  mkdirFs: (parentPath: string, name: string) =>
    apiFetch<{ path: string }>('/api/fs/mkdir', {
      method: 'POST',
      body: JSON.stringify({ parentPath, name }),
    }),

  listProjects: () => apiFetch<ProjectSummary[]>('/api/projects'),

  createProject: (payload: CreateProjectRequest) =>
    apiFetch<ProjectSummary>('/api/projects', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  importProject: (solutionPath: string) =>
    apiFetch<ProjectSummary>('/api/projects/import', {
      method: 'POST',
      body: JSON.stringify({ solutionPath }),
    }),

  deleteProject: (projectId: number) =>
    apiFetch<void>(`/api/projects/${projectId}`, { method: 'DELETE' }),

  getProjectSchema: (projectId: number) =>
    apiFetch<ProjectSchemaResponse>(`/api/projects/${projectId}/schema`),

  saveProjectSchema: (projectId: number, schema: SchemaModel) =>
    apiFetch<SaveSchemaResponse>(`/api/projects/${projectId}/schema`, {
      method: 'PUT',
      body: JSON.stringify(schema),
    }),

  previewCodeGen: (projectId: number) =>
    apiFetch<CodeGenPreview>(`/api/projects/${projectId}/generate/preview`, {
      method: 'POST',
    }),

  applyCodeGen: (projectId: number, runEfMigration: boolean, migrationName?: string) =>
    apiFetch<CodeGenApplyResult>(`/api/projects/${projectId}/generate/apply`, {
      method: 'POST',
      body: JSON.stringify({
        runEfMigration,
        migrationName: migrationName?.trim() || null,
      }),
    }),
};
