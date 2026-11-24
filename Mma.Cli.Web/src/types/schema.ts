export type DatabaseEngine = 'postgresql' | 'mssql' | 'mysql' | 'general';

export type MysqlStorageEngine = 'InnoDB' | 'MyISAM' | 'MEMORY';

export type Cardinality = '1:1' | '1:N' | 'N:1' | 'N:M';

export type ReferentialAction =
  | 'CASCADE'
  | 'SET NULL'
  | 'SET DEFAULT'
  | 'RESTRICT'
  | 'NO ACTION';

export type SqlDialect = DatabaseEngine;

export type TableAccentColor =
  | 'blue'
  | 'indigo'
  | 'emerald'
  | 'amber'
  | 'rose'
  | 'slate'
  | 'cyan';

export interface Column {
  id: string;
  name: string;
  type: string;
  isPrimaryKey: boolean;
  isNullable: boolean;
  isUnique: boolean;
  isAutoIncrement: boolean;
  defaultValue: string;
  checkConstraint: string;
  comment: string;
}

export type CrudMethod = 'getAll' | 'select' | 'find' | 'add' | 'update' | 'delete';

export interface TableCodeGenConfig {
  addService: boolean;
  serviceMethods: Record<CrudMethod, boolean>;
  addApiController: boolean;
}

export function normalizeTableCodeGen(
  config?: Partial<TableCodeGenConfig>
): TableCodeGenConfig {
  const m = config?.serviceMethods;
  const addService = Boolean(config?.addService);
  return {
    addService,
    serviceMethods: {
      getAll: m?.getAll ?? true,
      select: m?.select ?? true,
      find: m?.find ?? true,
      add: m?.add ?? true,
      update: m?.update ?? true,
      delete: m?.delete ?? true,
    },
    addApiController: addService && Boolean(config?.addApiController),
  };
}

export interface TableUniqueConstraint {
  id: string;
  name: string;
  columns: string[];
}

export interface TableCheckConstraint {
  id: string;
  name: string;
  expression: string;
}

export interface TableIndex {
  id: string;
  name: string;
  columns: string[];
  isUnique: boolean;
  method?: string;
  whereClause?: string;
  includeColumns?: string[];
}

export interface Table {
  id: string;
  name: string;
  schema: string;
  storageEngine?: MysqlStorageEngine;
  charset?: string;
  codeGen?: TableCodeGenConfig;
  color: TableAccentColor;
  comment: string;
  x: number;
  y: number;
  columns: Column[];
  indexes?: TableIndex[];
  uniqueConstraints?: TableUniqueConstraint[];
  checkConstraints?: TableCheckConstraint[];
}

export interface Relation {
  id: string;
  name: string;
  sourceTableId: string;
  sourceColumnId: string;
  targetTableId: string;
  targetColumnId: string;
  sourceColumnIds?: string[];
  targetColumnIds?: string[];
  cardinality: Cardinality;
  onDelete: ReferentialAction;
  onUpdate: ReferentialAction;
}

export interface SchemaModel {
  id: string;
  name: string;
  engine: DatabaseEngine;
  description: string;
  updatedAt: string;
  tables: Table[];
  relations: Relation[];
}

export interface ViewportState {
  x: number;
  y: number;
  zoom: number;
}

export type SelectionState =
  | { type: 'none' }
  | { type: 'table'; tableId: string }
  | { type: 'column'; tableId: string; columnId: string }
  | { type: 'relation'; relationId: string };

export interface PendingConnection {
  sourceTableId: string;
  sourceColumnId: string;
  sourceSide: 'left' | 'right';
  startX: number;
  startY: number;
  currentX: number;
  currentY: number;
  hoverTarget?: {
    tableId: string;
    columnId: string;
  };
}
