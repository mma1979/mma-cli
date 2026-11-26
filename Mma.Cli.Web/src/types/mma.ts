import { SchemaModel } from './schema';

export interface ApiError {
  error: string;
  code: string;
  traceId?: string;
}

export interface HealthInfo {
  status: string;
  version: string;
  mode: 'local-cli' | 'standalone';
  requiresAuth: boolean;
  initialPath?: string | null;
}

export interface BrowseFsResponse {
  currentPath: string;
  parentPath?: string | null;
  directories: string[];
  hasSolutionFile: boolean;
  isMmaSolution: boolean;
}

export interface ProjectSummary {
  id: number;
  name: string;
  path: string;
  mapper: 'AutoMapper' | 'Mapster' | string;
  existsOnDisk: boolean;
  entityCount: number;
  pendingCount: number;
  updatedAtUtc?: string | null;
}

export interface CreateProjectRequest {
  name: string;
  parentDirectory: string;
  mapper: 'AutoMapper' | 'Mapster';
  runInitialMigration: boolean;
}

export interface EntitySyncStatus {
  entityName: string;
  applied: boolean;
  pendingPropertyCount: number;
  appliedPropertyCount: number;
}

export interface SchemaSyncState {
  totalEntities: number;
  appliedEntities: number;
  pendingEntities: number;
  pendingProperties: number;
  pendingRelations: number;
  entities: EntitySyncStatus[];
}

export interface ProjectSchemaResponse {
  project: ProjectSummary;
  schema: SchemaModel;
  syncState: SchemaSyncState;
}

export interface SaveSchemaResponse {
  updatedAtUtc: string;
  syncState: SchemaSyncState;
}

export interface PendingEntity {
  entityName: string;
  pkType: string;
  hasService: boolean;
  hasApi: boolean;
  serviceMethods: string[];
}

export interface PendingProperty {
  entityName: string;
  columnName: string;
  dataType: string;
  nullable: boolean;
}

export interface PendingRelation {
  parentEntity: string;
  childEntity: string;
  foreignKey: string;
  foreignKeyType: string;
}

export interface CodeGenPreview {
  pendingEntities: PendingEntity[];
  pendingProperties: PendingProperty[];
  pendingRelations: PendingRelation[];
  equivalentCommands: string[];
}

export interface CodeGenStepLog {
  kind: 'entity' | 'property' | 'relation' | 'ef-migration' | string;
  target: string;
  succeeded: boolean;
  message: string;
}

export interface CodeGenApplyResult {
  succeeded: boolean;
  steps: CodeGenStepLog[];
  syncState: SchemaSyncState;
}
