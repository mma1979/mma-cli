import { Column, DatabaseEngine, Relation, SchemaModel, Table } from '../types/schema';

export type ValidationSeverity = 'error' | 'warning';

export type ValidationRuleCode =
  | 'orphaned-table'
  | 'circular-dependency-non-null'
  | 'duplicate-index-name'
  | 'empty-index'
  | 'missing-primary-key'
  | 'duplicate-table-name'
  | 'duplicate-column-name'
  | 'duplicate-constraint-name'
  | 'fk-type-mismatch'
  | 'set-null-on-not-null';

export interface ValidationIssue {
  id: string;
  code: ValidationRuleCode;
  severity: ValidationSeverity;
  category: string;
  title: string;
  message: string;
  suggestion: string;
  tableId?: string;
  columnId?: string;
  relationId?: string;
  indexId?: string;
  relatedTableIds?: string[];
  relatedRelationIds?: string[];
}

export interface SchemaValidationResult {
  issues: ValidationIssue[];
  errorCount: number;
  warningCount: number;
  byTableId: Map<string, ValidationIssue[]>;
  byRelationId: Map<string, ValidationIssue[]>;
}

/**
 * Resolves which side of a relation holds the foreign key column vs the referenced parent column.
 * Returns null for N:M relations (which materialize as a separate junction table).
 */
function resolveFkEndpoints(
  rel: Relation,
  tableMap: Map<string, Table>
): {
  fkTable: Table;
  fkColumn: Column;
  refTable: Table;
  refColumn: Column;
} | null {
  if (rel.cardinality === 'N:M') return null;

  const fkTableId = rel.cardinality === 'N:1' ? rel.sourceTableId : rel.targetTableId;
  const fkColId = rel.cardinality === 'N:1' ? rel.sourceColumnId : rel.targetColumnId;
  const refTableId = rel.cardinality === 'N:1' ? rel.targetTableId : rel.sourceTableId;
  const refColId = rel.cardinality === 'N:1' ? rel.targetColumnId : rel.sourceColumnId;

  const fkTable = tableMap.get(fkTableId);
  const refTable = tableMap.get(refTableId);
  if (!fkTable || !refTable) return null;

  const fkColumn = fkTable.columns.find((c) => c.id === fkColId);
  const refColumn = refTable.columns.find((c) => c.id === refColId);
  if (!fkColumn || !refColumn) return null;

  return { fkTable, fkColumn, refTable, refColumn };
}

/**
 * Normalizes SQL column types into comparable canonical type families
 * so compatible types (e.g. bigserial <-> bigint, serial <-> integer, int <-> integer) match cleanly.
 */
function normalizeTypeFamily(rawType: string): string {
  const cleaned = rawType.trim().toLowerCase().replace(/\s+/g, ' ');
  const base = cleaned.split('(')[0].trim();

  if (['serial', 'int', 'int4', 'integer', 'mediumint'].includes(base)) return 'int32';
  if (['bigserial', 'bigint', 'int8'].includes(base)) return 'int64';
  if (['smallserial', 'smallint', 'int2', 'tinyint'].includes(base)) return 'int16';
  if (['uuid', 'uniqueidentifier'].includes(base)) return 'uuid';
  if (
    ['varchar', 'nvarchar', 'char', 'nchar', 'character varying', 'character', 'text', 'ntext', 'clob'].includes(
      base
    )
  ) {
    return 'string';
  }
  if (['numeric', 'decimal', 'money', 'smallmoney'].includes(base)) return 'decimal';
  if (['real', 'float', 'float4', 'float8', 'double', 'double precision'].includes(base)) {
    return 'float';
  }
  if (['bool', 'boolean', 'bit'].includes(base)) return 'boolean';
  if (
    [
      'timestamp',
      'timestamptz',
      'timestamp with time zone',
      'timestamp without time zone',
      'datetime',
      'datetime2',
      'datetimeoffset',
      'smalldatetime',
    ].includes(base)
  ) {
    return 'timestamp';
  }
  if (['date'].includes(base)) return 'date';
  if (['json', 'jsonb'].includes(base)) return 'json';
  if (['bytea', 'blob', 'varbinary', 'binary'].includes(base)) return 'binary';

  return base;
}

function getEffectiveSchemaScope(table: Table, engine: DatabaseEngine): string {
  const raw = (table.schema || '').trim().toLowerCase();
  if (engine === 'postgresql') return raw || 'public';
  if (engine === 'mssql') return raw || 'dbo';
  return raw || '__default__';
}

interface DirectedDepEdge {
  fromTableId: string; // Child table holding non-null FK
  toTableId: string; // Parent table referenced by FK
  relation: Relation;
  fkColumn: Column;
}

/**
 * Finds elementary cycles in the non-null foreign key dependency graph.
 * Uses Tarjan's Strongly Connected Components plus shortest cycle extraction per SCC
 * so every distinct non-null dependency cycle is reported cleanly without exponential blowup.
 */
function detectNonNullDependencyCycles(
  tables: Table[],
  edges: DirectedDepEdge[]
): { tableIds: string[]; relationIds: string[]; isSelfLoop: boolean }[] {
  const adjacency = new Map<string, DirectedDepEdge[]>();
  tables.forEach((t) => adjacency.set(t.id, []));
  edges.forEach((edge) => {
    const list = adjacency.get(edge.fromTableId);
    if (list) list.push(edge);
  });

  const cycles: { tableIds: string[]; relationIds: string[]; isSelfLoop: boolean }[] = [];

  // 1. Direct self-referencing non-null FKs (A -> A)
  const reportedSelfLoops = new Set<string>();
  for (const edge of edges) {
    if (edge.fromTableId === edge.toTableId && !reportedSelfLoops.has(edge.fromTableId)) {
      reportedSelfLoops.add(edge.fromTableId);
      cycles.push({
        tableIds: [edge.fromTableId, edge.fromTableId],
        relationIds: [edge.relation.id],
        isSelfLoop: true,
      });
    }
  }

  // 2. Tarjan's SCC for multi-table cycles (size >= 2)
  let indexCounter = 0;
  const indices = new Map<string, number>();
  const lowlink = new Map<string, number>();
  const stack: string[] = [];
  const onStack = new Set<string>();
  const sccs: string[][] = [];

  const strongConnect = (v: string) => {
    indices.set(v, indexCounter);
    lowlink.set(v, indexCounter);
    indexCounter++;
    stack.push(v);
    onStack.add(v);

    for (const edge of adjacency.get(v) || []) {
      const w = edge.toTableId;
      if (w === v) continue;
      if (!indices.has(w)) {
        strongConnect(w);
        lowlink.set(v, Math.min(lowlink.get(v)!, lowlink.get(w)!));
      } else if (onStack.has(w)) {
        lowlink.set(v, Math.min(lowlink.get(v)!, indices.get(w)!));
      }
    }

    if (lowlink.get(v) === indices.get(v)) {
      const component: string[] = [];
      let w: string;
      do {
        w = stack.pop()!;
        onStack.delete(w);
        component.push(w);
      } while (w !== v);
      if (component.length >= 2) {
        sccs.push(component);
      }
    }
  };

  for (const table of tables) {
    if (!indices.has(table.id)) {
      strongConnect(table.id);
    }
  }

  // Extract a concrete directed cycle path inside each SCC using BFS from a representative node
  for (const scc of sccs) {
    const sccSet = new Set(scc);
    const start = scc[0];

    const queue: { node: string; path: string[]; relIds: string[] }[] = [
      { node: start, path: [start], relIds: [] },
    ];
    const visited = new Set<string>();
    let found: { tableIds: string[]; relationIds: string[] } | null = null;

    while (queue.length > 0 && !found) {
      const current = queue.shift()!;
      for (const edge of adjacency.get(current.node) || []) {
        const next = edge.toTableId;
        if (next === current.node || !sccSet.has(next)) continue;

        if (next === start) {
          found = {
            tableIds: [...current.path, start],
            relationIds: [...current.relIds, edge.relation.id],
          };
          break;
        }
        if (!visited.has(next)) {
          visited.add(next);
          queue.push({
            node: next,
            path: [...current.path, next],
            relIds: [...current.relIds, edge.relation.id],
          });
        }
      }
    }

    if (found) {
      cycles.push({
        tableIds: found.tableIds,
        relationIds: found.relationIds,
        isSelfLoop: false,
      });
    }
  }

  return cycles;
}

export function validateSchema(schema: SchemaModel): SchemaValidationResult {
  const engine: DatabaseEngine = schema.engine || 'postgresql';
  const issues: ValidationIssue[] = [];
  const tableMap = new Map<string, Table>();
  schema.tables.forEach((t) => tableMap.set(t.id, t));

  // 1. Duplicate Table Names (within same schema scope)
  const tablesByScopedName = new Map<string, Table[]>();
  for (const table of schema.tables) {
    const normalizedName = table.name.trim().toLowerCase();
    if (!normalizedName) continue;
    const scope = getEffectiveSchemaScope(table, engine);
    const key = `${scope}.${normalizedName}`;
    if (!tablesByScopedName.has(key)) tablesByScopedName.set(key, []);
    tablesByScopedName.get(key)!.push(table);
  }
  for (const [, dupTables] of tablesByScopedName) {
    if (dupTables.length > 1) {
      const sample = dupTables[0];
      issues.push({
        id: `dup-table-${dupTables.map((t) => t.id).join('-')}`,
        code: 'duplicate-table-name',
        severity: 'error',
        category: 'Naming',
        title: `Duplicate table name "${sample.name}"`,
        message: `${dupTables.length} tables share the identifier "${sample.name}" in the same schema.`,
        suggestion: 'Rename duplicate tables so each table has a unique identifier.',
        tableId: dupTables[1].id,
        relatedTableIds: dupTables.map((t) => t.id),
      });
    }
  }

  // 2. Per-table checks: Missing PK & Duplicate Column Names
  for (const table of schema.tables) {
    const hasPk = table.columns.some((c) => c.isPrimaryKey);
    if (table.columns.length > 0 && !hasPk) {
      issues.push({
        id: `missing-pk-${table.id}`,
        code: 'missing-primary-key',
        severity: 'warning',
        category: 'Integrity',
        title: `Missing primary key on "${table.name}"`,
        message: `Table "${table.name}" has no primary key column defined.`,
        suggestion: 'Mark an identifier column as PK to ensure unique row addressing and replication safety.',
        tableId: table.id,
      });
    }

    const colNames = new Map<string, Column[]>();
    for (const col of table.columns) {
      const key = col.name.trim().toLowerCase();
      if (!key) continue;
      if (!colNames.has(key)) colNames.set(key, []);
      colNames.get(key)!.push(col);
    }
    for (const [, dupCols] of colNames) {
      if (dupCols.length > 1) {
        issues.push({
          id: `dup-col-${table.id}-${dupCols[0].name.toLowerCase()}`,
          code: 'duplicate-column-name',
          severity: 'error',
          category: 'Naming',
          title: `Duplicate column "${table.name}.${dupCols[0].name}"`,
          message: `Table "${table.name}" defines column "${dupCols[0].name}" ${dupCols.length} times.`,
          suggestion: 'Rename or remove the duplicate column definition.',
          tableId: table.id,
          columnId: dupCols[1].id,
        });
      }
    }
  }

  // 3. Orphaned Tables (isolated tables when schema contains > 1 table)
  if (schema.tables.length > 1) {
    const connectedTableIds = new Set<string>();
    for (const rel of schema.relations) {
      if (tableMap.has(rel.sourceTableId) && tableMap.has(rel.targetTableId)) {
        connectedTableIds.add(rel.sourceTableId);
        connectedTableIds.add(rel.targetTableId);
      }
    }

    for (const table of schema.tables) {
      if (!connectedTableIds.has(table.id)) {
        issues.push({
          id: `orphan-${table.id}`,
          code: 'orphaned-table',
          severity: 'warning',
          category: 'Structure',
          title: `Orphaned table "${table.name}"`,
          message: `Table "${table.name}" has no incoming or outgoing foreign key relations.`,
          suggestion: 'Connect it to a related table or verify whether it is intentionally standalone.',
          tableId: table.id,
        });
      }
    }
  }

  // 4. Duplicate Index Names & Empty Indexes
  const indexesByNameScope = new Map<
    string,
    { table: Table; indexId: string; indexName: string }[]
  >();
  const indexesByTableAndName = new Map<
    string,
    { table: Table; indexId: string; indexName: string }[]
  >();

  for (const table of schema.tables) {
    const validColIds = new Set(table.columns.map((c) => c.id));
    const tableIndexes = table.indexes || [];

    for (const idx of tableIndexes) {
      const trimmedName = idx.name.trim();
      const validCols = idx.columns.filter((cid) => validColIds.has(cid));

      if (!trimmedName || validCols.length === 0) {
        issues.push({
          id: `empty-idx-${table.id}-${idx.id}`,
          code: 'empty-index',
          severity: 'warning',
          category: 'Indexes',
          title: `Incomplete index on "${table.name}"`,
          message: !trimmedName
            ? `An index on "${table.name}" has an empty name.`
            : `Index "${trimmedName}" on "${table.name}" has no columns selected.`,
          suggestion: !trimmedName
            ? 'Give the index a unique descriptive name (e.g. idx_table_column).'
            : 'Select at least one column for this index or remove it.',
          tableId: table.id,
          indexId: idx.id,
        });
      }

      if (!trimmedName) continue;
      const lower = trimmedName.toLowerCase();
      const tableKey = `${table.id}::${lower}`;
      if (!indexesByTableAndName.has(tableKey)) indexesByTableAndName.set(tableKey, []);
      indexesByTableAndName.get(tableKey)!.push({
        table,
        indexId: idx.id,
        indexName: trimmedName,
      });

      const scope = getEffectiveSchemaScope(table, engine);
      const scopeKey = `${scope}::${lower}`;
      if (!indexesByNameScope.has(scopeKey)) indexesByNameScope.set(scopeKey, []);
      indexesByNameScope.get(scopeKey)!.push({
        table,
        indexId: idx.id,
        indexName: trimmedName,
      });
    }
  }

  // Report same-table duplicate index names first
  for (const [, entries] of indexesByTableAndName) {
    if (entries.length > 1) {
      const first = entries[0];
      issues.push({
        id: `dup-idx-tbl-${first.table.id}-${first.indexName.toLowerCase()}`,
        code: 'duplicate-index-name',
        severity: 'error',
        category: 'Indexes',
        title: `Duplicate index name "${first.indexName}"`,
        message: `Table "${first.table.name}" defines ${entries.length} indexes named "${first.indexName}".`,
        suggestion: 'Rename each index using a distinct table and column prefix (e.g. idx_table_col).',
        tableId: first.table.id,
        indexId: entries[1].indexId,
      });
    }
  }

  // Report cross-table duplicate index names within the same schema namespace
  for (const [scopeKey, entries] of indexesByNameScope) {
    const distinctTableIds = Array.from(new Set(entries.map((e) => e.table.id)));
    if (distinctTableIds.length > 1) {
      const sample = entries[0];
      const tableNames = distinctTableIds
        .map((id) => tableMap.get(id)?.name ?? id)
        .join(', ');
      const isSchemaGlobalError = engine === 'postgresql' || engine === 'general';

      issues.push({
        id: `dup-idx-schema-${scopeKey}`,
        code: 'duplicate-index-name',
        severity: isSchemaGlobalError ? 'error' : 'warning',
        category: 'Indexes',
        title: `Duplicate index name "${sample.indexName}"`,
        message: `Index name "${sample.indexName}" is reused across multiple tables (${tableNames}).`,
        suggestion: isSchemaGlobalError
          ? 'PostgreSQL and standard SQL require unique index names per schema. Prefix with the table name.'
          : 'Prefix index names with each table name to prevent cross-engine migration collisions.',
        tableId: entries[1].table.id,
        indexId: entries[1].indexId,
        relatedTableIds: distinctTableIds,
      });
    }
  }

  // 5. Relations checks: Duplicate FK names, SET NULL on NOT NULL, FK Type Mismatches, Non-null Dep Graph
  const relByName = new Map<string, Relation[]>();
  const nonNullDepEdges: DirectedDepEdge[] = [];

  for (const rel of schema.relations) {
    const trimmedRelName = rel.name.trim().toLowerCase();
    if (trimmedRelName) {
      if (!relByName.has(trimmedRelName)) relByName.set(trimmedRelName, []);
      relByName.get(trimmedRelName)!.push(rel);
    }

    const endpoints = resolveFkEndpoints(rel, tableMap);
    if (!endpoints) continue;
    const { fkTable, fkColumn, refTable, refColumn } = endpoints;

    // Collect non-null FK dependency edge (fkTable depends on refTable)
    if (!fkColumn.isNullable) {
      nonNullDepEdges.push({
        fromTableId: fkTable.id,
        toTableId: refTable.id,
        relation: rel,
        fkColumn,
      });
    }

    // ON DELETE / ON UPDATE SET NULL on NOT NULL FK column
    if (
      !fkColumn.isNullable &&
      (rel.onDelete === 'SET NULL' || rel.onUpdate === 'SET NULL')
    ) {
      const actionLabel =
        rel.onDelete === 'SET NULL' && rel.onUpdate === 'SET NULL'
          ? 'ON DELETE / ON UPDATE SET NULL'
          : rel.onDelete === 'SET NULL'
          ? 'ON DELETE SET NULL'
          : 'ON UPDATE SET NULL';
      issues.push({
        id: `set-null-nn-${rel.id}`,
        code: 'set-null-on-not-null',
        severity: 'error',
        category: 'Integrity',
        title: `${actionLabel} on NOT NULL column`,
        message: `Relation "${rel.name}" sets "${fkTable.name}.${fkColumn.name}" to NULL, but the column is NOT NULL.`,
        suggestion: `Make "${fkTable.name}.${fkColumn.name}" nullable or change the referential action to CASCADE / RESTRICT.`,
        tableId: fkTable.id,
        columnId: fkColumn.id,
        relationId: rel.id,
      });
    }

    // Incompatible data type families between FK and referenced column
    const fkFamily = normalizeTypeFamily(fkColumn.type);
    const refFamily = normalizeTypeFamily(refColumn.type);
    if (fkFamily !== refFamily) {
      issues.push({
        id: `fk-type-${rel.id}`,
        code: 'fk-type-mismatch',
        severity: 'warning',
        category: 'Types',
        title: `Foreign key type mismatch on "${fkTable.name}.${fkColumn.name}"`,
        message: `"${fkTable.name}.${fkColumn.name}" (${fkColumn.type}) references "${refTable.name}.${refColumn.name}" (${refColumn.type}).`,
        suggestion: 'Align the foreign key column data type with the referenced parent column type.',
        tableId: fkTable.id,
        columnId: fkColumn.id,
        relationId: rel.id,
      });
    }
  }

  for (const [, dupRels] of relByName) {
    if (dupRels.length > 1) {
      issues.push({
        id: `dup-fk-name-${dupRels[0].name.toLowerCase()}`,
        code: 'duplicate-constraint-name',
        severity: 'warning',
        category: 'Naming',
        title: `Duplicate constraint name "${dupRels[0].name}"`,
        message: `${dupRels.length} foreign key relations share the constraint name "${dupRels[0].name}".`,
        suggestion: 'Give each foreign key constraint a distinct name to avoid DDL execution errors.',
        relationId: dupRels[1].id,
        relatedRelationIds: dupRels.map((r) => r.id),
      });
    }
  }

  // 6. Circular Dependencies Without Nullable Foreign Keys
  const nonNullCycles = detectNonNullDependencyCycles(schema.tables, nonNullDepEdges);
  for (const cycle of nonNullCycles) {
    const pathNames = cycle.tableIds.map((id) => tableMap.get(id)?.name ?? id);
    const uniqueTableIds = Array.from(new Set(cycle.tableIds));

    issues.push({
      id: `cycle-nn-${uniqueTableIds.sort().join('-')}-${cycle.isSelfLoop ? 'self' : 'loop'}`,
      code: 'circular-dependency-non-null',
      severity: 'error',
      category: 'Dependencies',
      title: cycle.isSelfLoop
        ? `Non-null self-reference on "${pathNames[0]}"`
        : `Circular dependency without nullable FK`,
      message: cycle.isSelfLoop
        ? `Table "${pathNames[0]}" has a NOT NULL foreign key referencing itself, blocking root row insertion.`
        : `Non-nullable foreign keys form a cycle: ${pathNames.join(' → ')}.`,
      suggestion:
        'Make at least one foreign key column in the cycle nullable (or deferrable) so initial rows can be inserted.',
      tableId: cycle.tableIds[0],
      relationId: cycle.relationIds[0],
      relatedTableIds: uniqueTableIds,
      relatedRelationIds: cycle.relationIds,
    });
  }

  // Sort: errors first, then warnings
  issues.sort((a, b) => {
    if (a.severity !== b.severity) return a.severity === 'error' ? -1 : 1;
    return a.title.localeCompare(b.title);
  });

  const byTableId = new Map<string, ValidationIssue[]>();
  const byRelationId = new Map<string, ValidationIssue[]>();

  const pushTableIssue = (tid: string, issue: ValidationIssue) => {
    if (!byTableId.has(tid)) byTableId.set(tid, []);
    const list = byTableId.get(tid)!;
    if (!list.includes(issue)) list.push(issue);
  };

  const pushRelationIssue = (rid: string, issue: ValidationIssue) => {
    if (!byRelationId.has(rid)) byRelationId.set(rid, []);
    const list = byRelationId.get(rid)!;
    if (!list.includes(issue)) list.push(issue);
  };

  let errorCount = 0;
  let warningCount = 0;

  for (const issue of issues) {
    if (issue.severity === 'error') errorCount++;
    else warningCount++;

    if (issue.tableId) pushTableIssue(issue.tableId, issue);
    if (issue.relatedTableIds) {
      issue.relatedTableIds.forEach((tid) => pushTableIssue(tid, issue));
    }
    if (issue.relationId) pushRelationIssue(issue.relationId, issue);
    if (issue.relatedRelationIds) {
      issue.relatedRelationIds.forEach((rid) => pushRelationIssue(rid, issue));
    }
  }

  return {
    issues,
    errorCount,
    warningCount,
    byTableId,
    byRelationId,
  };
}
