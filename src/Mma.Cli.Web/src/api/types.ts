import type { components } from "./schema";

type ApiSchemas = components["schemas"];

export type Mapper = ApiSchemas["MapperKind"];
export type FileChangeKind = ApiSchemas["FileChangeKind"];
export type DatabaseProviderKind = ApiSchemas["DatabaseProviderKind"];
export type SqlServerAuthKind = ApiSchemas["SqlServerAuthKind"];

export type HealthResponse = Omit<ApiSchemas["HealthResponse"], "dotnetVersion"> & {
  dotnetVersion?: string | null;
};
export type BootstrapAuthResponse = ApiSchemas["BootstrapAuthResponse"];
export type AuthSessionResponse = Omit<ApiSchemas["AuthSessionResponse"], "csrfToken"> & {
  csrfToken?: string | null;
};
export type DirectoryEntry = ApiSchemas["DirectoryEntryDto"];
export type BrowseDirectoryResponse = ApiSchemas["BrowseDirectoryResponse"];

export type WorkspaceSummary = Omit<
  ApiSchemas["WorkspaceSummaryDto"],
  "id" | "entityCount" | "relationCount" | "pendingChangesCount"
> & {
  id: number;
  entityCount: number;
  relationCount: number;
  pendingChangesCount: number;
};
export type SolutionInfo = ApiSchemas["SolutionInfo"];
export type ManifestProperty = ApiSchemas["ManifestProperty"];
export type ManifestEntity = Omit<ApiSchemas["ManifestEntity"], "properties"> & {
  properties: ManifestProperty[];
};
export type ManifestRelation = ApiSchemas["ManifestRelation"];
export type ProjectManifest = Omit<
  ApiSchemas["ProjectManifest"],
  "schemaVersion" | "solutionName" | "mapper" | "solutionFormat" | "entities" | "relations" | "updatedAtUtc"
> & {
  schemaVersion: number;
  solutionName: string;
  mapper: string;
  solutionFormat: string;
  entities: ManifestEntity[];
  relations: ManifestRelation[];
  updatedAtUtc: string;
};
export type ValidationIssue = ApiSchemas["ValidationIssue"];
export type ProjectValidationReport = ApiSchemas["ProjectValidationReport"];
export type ProjectDetail = Omit<ApiSchemas["ProjectDetailResponse"], "workspace" | "manifest"> & {
  workspace: WorkspaceSummary;
  manifest: ProjectManifest;
};
export type ManifestUpdate = Omit<ApiSchemas["ManifestUpdateResponse"], "manifest"> & {
  manifest: ProjectManifest;
};
export type PlanFileDiff = Omit<ApiSchemas["PlanFileDiffDto"], "kind"> & {
  kind: FileChangeKind;
};
export type PlanDiff = Omit<ApiSchemas["PlanDiffResponse"], "fileChanges"> & {
  fileChanges: PlanFileDiff[];
};
export type ApplyProjectResult = Omit<ApiSchemas["ApplyProjectResponse"], "manifest"> & {
  manifest: ProjectManifest;
};
export type JobAccepted = ApiSchemas["JobAcceptedResponse"];
export type GenerationSummary = ApiSchemas["GenerationSummary"];
export type CreateWorkspaceResult = Omit<ApiSchemas["CreateWorkspaceResponse"], "workspace"> & {
  workspace: WorkspaceSummary | null;
};
export type RegisterWorkspaceResult = Omit<
  ApiSchemas["RegisterWorkspaceResponse"],
  "workspace" | "manifest" | "manifestHash"
> & {
  workspace: WorkspaceSummary;
  manifest?: ProjectManifest | null;
  manifestHash?: string | null;
};
export type DatabaseConnectionParameters = ApiSchemas["DatabaseConnectionParameters"];
export type SchemaFilterOptions = ApiSchemas["SchemaFilterOptions"];
export type IntrospectedColumn = ApiSchemas["IntrospectedColumn"];
export type IntrospectedRelation = ApiSchemas["IntrospectedRelation"];
export type IntrospectedTable = ApiSchemas["IntrospectedTable"];
export type DatabaseSchemaSnapshot = ApiSchemas["DatabaseSchemaSnapshot"];
export type DbImportTestResult = ApiSchemas["DbImportTestResponse"];
export type DbImportApplyResult = Omit<ApiSchemas["DbImportApplyResponse"], "manifest" | "jobId" | "plan"> & {
  manifest: ProjectManifest;
  jobId?: string | null;
  plan?: PlanDiff | null;
};
export type TemplateItem = ApiSchemas["TemplateItemDto"];
export type TemplateList = ApiSchemas["TemplateListResponse"];
export type UpdateTemplateResult = ApiSchemas["UpdateTemplateResponse"];

export type JobEventType = "progress" | "log" | "done" | "error" | (string & {});
export interface SseEnvelope {
  eventId: string;
  timestamp: string;
  type: JobEventType;
  jobOrSessionId: string;
  payload?: unknown;
}

export type AddEntityPayload = ApiSchemas["AddDraftEntityRequest"];
export type AddPropertyPayload = ApiSchemas["AddDraftPropertyRequest"];
export type AddRelationPayload = ApiSchemas["AddDraftRelationRequest"];
export type ApplyPayload = Omit<ApiSchemas["ApplyProjectRequest"], "planHash" | "migrationName"> & {
  planHash?: string;
  migrationName?: string;
};
export type DbImportRequest = ApiSchemas["DbIntrospectApiRequest"];

export const SUPPORTED_PROPERTY_TYPES = [
  "Guid",
  "int",
  "long",
  "string",
  "decimal",
  "float",
  "bool",
  "DateTime",
  "byte[]",
  "TimeSpan"
] as const;

export type SupportedPropertyType = (typeof SUPPORTED_PROPERTY_TYPES)[number];

export const MAPPER_OPTIONS: Mapper[] = ["AutoMapper", "Mapster"];
export const DATABASE_PROVIDERS: DatabaseProviderKind[] = [
  "SqlServer",
  "PostgreSql",
  "Sqlite",
  "MySql"
];
