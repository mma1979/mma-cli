import React, { useEffect, useMemo, useState } from 'react';
import {
  ArrowDown,
  ArrowDownLeft,
  ArrowLeftRight,
  ArrowUp,
  ArrowUpRight,
  Code2,
  Copy,
  GitFork,
  KeyRound,
  Plus,
  SlidersHorizontal,
  Trash2,
  X,
} from 'lucide-react';
import { resolveSupportedDatabaseEngine } from '../config/featureFlags';
import {
  ENGINE_PROFILES,
  MYSQL_CHARSETS,
  MYSQL_STORAGE_ENGINES,
  SUPPORTED_ENGINE_LIST,
} from '../data/engines';
import { TABLE_ACCENT_PALETTE } from '../data/templates';
import {
  Cardinality,
  Column,
  CrudMethod,
  DatabaseEngine,
  MysqlStorageEngine,
  normalizeTableCodeGen,
  ReferentialAction,
  Relation,
  SchemaModel,
  SelectionState,
  Table,
  TableCodeGenConfig,
  TableIndex,
} from '../types/schema';

interface InspectorPanelProps {
  schema: SchemaModel;
  selection: SelectionState;
  onSelect: (sel: SelectionState) => void;
  onEngineChange: (engine: DatabaseEngine) => void;
  onUpdateSchemaMeta: (patch: Partial<Pick<SchemaModel, 'name' | 'description'>>) => void;
  onUpdateTable: (tableId: string, patch: Partial<Table>) => void;
  onDuplicateTable: (tableId: string) => void;
  onDeleteTable: (tableId: string) => void;
  onAddColumn: (tableId: string) => void;
  onUpdateColumn: (tableId: string, columnId: string, patch: Partial<Column>) => void;
  onDeleteColumn: (tableId: string, columnId: string) => void;
  onMoveColumn: (tableId: string, columnId: string, direction: 'up' | 'down') => void;
  onCreateRelation: (
    sourceTableId: string,
    sourceColumnId: string,
    targetTableId: string,
    targetColumnId: string,
    cardinality?: Cardinality
  ) => void;
  onUpdateRelation: (relationId: string, patch: Partial<Relation>) => void;
  onDeleteRelation: (relationId: string) => void;
  onClose: () => void;
}

const CARDINALITIES: Cardinality[] = ['1:1', '1:N', 'N:1', 'N:M'];

const CRUD_METHOD_OPTIONS: {
  key: CrudMethod;
  label: string;
  hint: string;
  httpMethod: string;
  routeSuffix: string;
}[] = [
  { key: 'getAll', label: 'getAll', hint: 'Fetch all records', httpMethod: 'GET', routeSuffix: '' },
  { key: 'select', label: 'select', hint: 'Query with filters / projection', httpMethod: 'POST', routeSuffix: '/select' },
  { key: 'find', label: 'find', hint: 'Find single record by ID', httpMethod: 'GET', routeSuffix: '/:id' },
  { key: 'add', label: 'add', hint: 'Insert new record', httpMethod: 'POST', routeSuffix: '' },
  { key: 'update', label: 'update', hint: 'Update existing record', httpMethod: 'PUT', routeSuffix: '/:id' },
  { key: 'delete', label: 'delete', hint: 'Delete record by ID', httpMethod: 'DELETE', routeSuffix: '/:id' },
];

export const InspectorPanel: React.FC<InspectorPanelProps> = ({
  schema,
  selection,
  onSelect,
  onEngineChange,
  onUpdateSchemaMeta,
  onUpdateTable,
  onDuplicateTable,
  onDeleteTable,
  onAddColumn,
  onUpdateColumn,
  onDeleteColumn,
  onMoveColumn,
  onCreateRelation,
  onUpdateRelation,
  onDeleteRelation,
  onClose,
}) => {
  const [quickRelOpen, setQuickRelOpen] = useState(false);
  const [newRelSourceTable, setNewRelSourceTable] = useState('');
  const [newRelSourceCol, setNewRelSourceCol] = useState('');
  const [newRelTargetTable, setNewRelTargetTable] = useState('');
  const [newRelTargetCol, setNewRelTargetCol] = useState('');
  const [newRelCard, setNewRelCard] = useState<Cardinality>('1:N');
  const [customTypeColId, setCustomTypeColId] = useState<string | null>(null);
  const [tableTab, setTableTab] = useState<'properties' | 'relations' | 'codegen'>('properties');

  // Switch back to Properties tab whenever a specific column is selected on the canvas
  useEffect(() => {
    if (selection.type === 'column') {
      setTableTab('properties');
    }
  }, [selection]);

  const activeEngineId = resolveSupportedDatabaseEngine(schema.engine);
  const engineProfile = ENGINE_PROFILES[activeEngineId];

  const tableMap = useMemo(() => {
    const map = new Map<string, Table>();
    schema.tables.forEach((t) => map.set(t.id, t));
    return map;
  }, [schema.tables]);

  const selectedTable =
    selection.type === 'table' || selection.type === 'column'
      ? tableMap.get(selection.tableId) ?? null
      : null;

  const selectedColumn =
    selection.type === 'column' && selectedTable
      ? selectedTable.columns.find((c) => c.id === selection.columnId) ?? null
      : null;

  const selectedRelation =
    selection.type === 'relation'
      ? schema.relations.find((r) => r.id === selection.relationId) ?? null
      : null;

  const tableRelationsBreakdown = useMemo(() => {
    if (!selectedTable) {
      return {
        outgoing: [] as {
          relation: Relation;
          localColName: string;
          remoteTableName: string;
          remoteColName: string;
          isSelfRef: boolean;
        }[],
        incoming: [] as {
          relation: Relation;
          localColName: string;
          remoteTableName: string;
          remoteColName: string;
        }[],
        manyToMany: [] as {
          relation: Relation;
          localColName: string;
          remoteTableName: string;
          remoteColName: string;
        }[],
        totalCount: 0,
      };
    }

    const outgoing: {
      relation: Relation;
      localColName: string;
      remoteTableName: string;
      remoteColName: string;
      isSelfRef: boolean;
    }[] = [];
    const incoming: {
      relation: Relation;
      localColName: string;
      remoteTableName: string;
      remoteColName: string;
    }[] = [];
    const manyToMany: {
      relation: Relation;
      localColName: string;
      remoteTableName: string;
      remoteColName: string;
    }[] = [];

    for (const r of schema.relations) {
      if (r.sourceTableId !== selectedTable.id && r.targetTableId !== selectedTable.id) {
        continue;
      }

      const srcTable = tableMap.get(r.sourceTableId);
      const tgtTable = tableMap.get(r.targetTableId);
      const srcCol = srcTable?.columns.find((c) => c.id === r.sourceColumnId);
      const tgtCol = tgtTable?.columns.find((c) => c.id === r.targetColumnId);

      if (r.cardinality === 'N:M') {
        const isSource = r.sourceTableId === selectedTable.id;
        manyToMany.push({
          relation: r,
          localColName: (isSource ? srcCol?.name : tgtCol?.name) ?? '?',
          remoteTableName: (isSource ? tgtTable?.name : srcTable?.name) ?? '?',
          remoteColName: (isSource ? tgtCol?.name : srcCol?.name) ?? '?',
        });
        continue;
      }

      // Determine which side holds the FK column vs referenced parent column
      const fkTableId = r.cardinality === 'N:1' ? r.sourceTableId : r.targetTableId;
      const fkColName = (r.cardinality === 'N:1' ? srcCol?.name : tgtCol?.name) ?? '?';
      const refTable = r.cardinality === 'N:1' ? tgtTable : srcTable;
      const refColName = (r.cardinality === 'N:1' ? tgtCol?.name : srcCol?.name) ?? '?';
      const fkTable = r.cardinality === 'N:1' ? srcTable : tgtTable;

      if (fkTableId === selectedTable.id) {
        outgoing.push({
          relation: r,
          localColName: fkColName,
          remoteTableName: refTable?.name ?? '?',
          remoteColName: refColName,
          isSelfRef: r.sourceTableId === r.targetTableId,
        });
      } else {
        incoming.push({
          relation: r,
          localColName: refColName,
          remoteTableName: fkTable?.name ?? '?',
          remoteColName: fkColName,
        });
      }
    }

    return {
      outgoing,
      incoming,
      manyToMany,
      totalCount: outgoing.length + incoming.length + manyToMany.length,
    };
  }, [selectedTable, schema.relations, tableMap]);

  const openQuickRelationForm = (preselectedSourceTableId?: string) => {
    const srcTbl =
      (preselectedSourceTableId && tableMap.get(preselectedSourceTableId)) ||
      schema.tables[0];
    const tgtTbl =
      schema.tables.find((t) => t.id !== srcTbl?.id) || srcTbl;

    setNewRelSourceTable(srcTbl?.id ?? '');
    setNewRelSourceCol(
      srcTbl?.columns.find((c) => c.isPrimaryKey)?.id ?? srcTbl?.columns[0]?.id ?? ''
    );
    setNewRelTargetTable(tgtTbl?.id ?? '');
    setNewRelTargetCol(tgtTbl?.columns[0]?.id ?? '');
    setNewRelCard('1:N');
    setQuickRelOpen(true);
  };

  const handleSubmitQuickRelation = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newRelSourceTable || !newRelSourceCol || !newRelTargetTable || !newRelTargetCol) {
      return;
    }
    onCreateRelation(
      newRelSourceTable,
      newRelSourceCol,
      newRelTargetTable,
      newRelTargetCol,
      newRelCard
    );
    setQuickRelOpen(false);
  };

  const inputClass =
    'w-full px-2.5 py-1.5 text-xs bg-slate-50 dark:bg-slate-900/90 border border-slate-200 dark:border-slate-700/90 rounded-md text-slate-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:border-blue-500 transition-colors';

  const labelClass = 'block text-[11px] font-medium text-slate-500 dark:text-slate-400 mb-1';

  return (
    <aside className="w-[332px] shrink-0 h-full bg-white dark:bg-[#0F172A] border-l border-slate-200 dark:border-slate-800 flex flex-col z-20">
      {/* Inspector Header */}
      <div className="h-11 px-4 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between shrink-0">
        <span className="text-xs font-semibold text-slate-800 dark:text-slate-200">
          {selectedRelation
            ? 'Relation Properties'
            : selectedTable
            ? 'Table Properties'
            : 'Schema Inspector'}
        </span>
        <button
          type="button"
          onClick={onClose}
          title="Close inspector"
          aria-label="Close inspector"
          className="p-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
        >
          <X className="w-3.5 h-3.5" />
        </button>
      </div>

      {/* Scrollable Body */}
      <div className="flex-1 overflow-y-auto studio-scroll">
        {/* ================= RELATION INSPECTOR ================= */}
        {selectedRelation && (
          <div className="divide-y divide-slate-200 dark:divide-slate-800">
            <div className="p-4 space-y-3.5">
              <div>
                <label className={labelClass}>Constraint Name</label>
                <input
                  type="text"
                  value={selectedRelation.name}
                  onChange={(e) =>
                    onUpdateRelation(selectedRelation.id, { name: e.target.value })
                  }
                  spellCheck={false}
                  className={`${inputClass} font-mono`}
                />
              </div>

              <div>
                <label className={labelClass}>Cardinality</label>
                <div className="grid grid-cols-4 gap-1 p-1 bg-slate-100 dark:bg-slate-900 rounded-md">
                  {CARDINALITIES.map((card) => (
                    <button
                      key={card}
                      type="button"
                      onClick={() =>
                        onUpdateRelation(selectedRelation.id, { cardinality: card })
                      }
                      className={`py-1.5 text-xs font-mono font-medium rounded transition-colors cursor-pointer ${
                        selectedRelation.cardinality === card
                          ? 'bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 shadow-xs'
                          : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                      }`}
                    >
                      {card}
                    </button>
                  ))}
                </div>
              </div>
            </div>

            {/* Endpoints */}
            <div className="p-4 space-y-3.5">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-slate-800 dark:text-slate-200">
                  Connected Columns
                </span>
                <button
                  type="button"
                  onClick={() =>
                    onUpdateRelation(selectedRelation.id, {
                      sourceTableId: selectedRelation.targetTableId,
                      sourceColumnId: selectedRelation.targetColumnId,
                      targetTableId: selectedRelation.sourceTableId,
                      targetColumnId: selectedRelation.sourceColumnId,
                    })
                  }
                  className="flex items-center gap-1 text-[11px] font-medium text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
                >
                  <ArrowLeftRight className="w-3 h-3" />
                  <span>Swap ends</span>
                </button>
              </div>

              <div className="space-y-2.5">
                <div>
                  <label className={labelClass}>Source Table</label>
                  <select
                    value={selectedRelation.sourceTableId}
                    onChange={(e) => {
                      const nextTable = tableMap.get(e.target.value);
                      onUpdateRelation(selectedRelation.id, {
                        sourceTableId: e.target.value,
                        sourceColumnId: nextTable?.columns[0]?.id ?? '',
                      });
                    }}
                    className={`${inputClass} font-mono`}
                  >
                    {schema.tables.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className={labelClass}>Source Column</label>
                  <select
                    value={selectedRelation.sourceColumnId}
                    onChange={(e) =>
                      onUpdateRelation(selectedRelation.id, {
                        sourceColumnId: e.target.value,
                      })
                    }
                    className={`${inputClass} font-mono`}
                  >
                    {(tableMap.get(selectedRelation.sourceTableId)?.columns ?? []).map(
                      (c) => (
                        <option key={c.id} value={c.id}>
                          {c.name} ({c.type})
                        </option>
                      )
                    )}
                  </select>
                </div>

                <div className="pt-1">
                  <label className={labelClass}>Target Table</label>
                  <select
                    value={selectedRelation.targetTableId}
                    onChange={(e) => {
                      const nextTable = tableMap.get(e.target.value);
                      onUpdateRelation(selectedRelation.id, {
                        targetTableId: e.target.value,
                        targetColumnId: nextTable?.columns[0]?.id ?? '',
                      });
                    }}
                    className={`${inputClass} font-mono`}
                  >
                    {schema.tables.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className={labelClass}>Target Column</label>
                  <select
                    value={selectedRelation.targetColumnId}
                    onChange={(e) =>
                      onUpdateRelation(selectedRelation.id, {
                        targetColumnId: e.target.value,
                      })
                    }
                    className={`${inputClass} font-mono`}
                  >
                    {(tableMap.get(selectedRelation.targetTableId)?.columns ?? []).map(
                      (c) => (
                        <option key={c.id} value={c.id}>
                          {c.name} ({c.type})
                        </option>
                      )
                    )}
                  </select>
                </div>
              </div>
            </div>

            {/* Referential Actions */}
            <div className="p-4 space-y-3">
              <span className="text-xs font-semibold text-slate-800 dark:text-slate-200 block">
                Referential Integrity
              </span>
              <div className="grid grid-cols-2 gap-2.5">
                <div>
                  <label className={labelClass}>On Delete</label>
                  <select
                    value={selectedRelation.onDelete}
                    onChange={(e) =>
                      onUpdateRelation(selectedRelation.id, {
                        onDelete: e.target.value as ReferentialAction,
                      })
                    }
                    className={`${inputClass} font-mono`}
                  >
                    {engineProfile.referentialActions.map((a) => (
                      <option key={a} value={a}>
                        {a}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className={labelClass}>On Update</label>
                  <select
                    value={selectedRelation.onUpdate}
                    onChange={(e) =>
                      onUpdateRelation(selectedRelation.id, {
                        onUpdate: e.target.value as ReferentialAction,
                      })
                    }
                    className={`${inputClass} font-mono`}
                  >
                    {engineProfile.referentialActions.map((a) => (
                      <option key={a} value={a}>
                        {a}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            </div>

            {/* Delete Relation */}
            <div className="p-4">
              <button
                type="button"
                onClick={() => onDeleteRelation(selectedRelation.id)}
                className="w-full py-2 px-3 text-xs font-medium text-rose-600 dark:text-rose-400 border border-rose-200 dark:border-rose-900/70 hover:bg-rose-50 dark:hover:bg-rose-950/40 rounded-md flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
              >
                <Trash2 className="w-3.5 h-3.5" />
                <span>Delete Relation</span>
              </button>
            </div>
          </div>
        )}

        {/* ================= TABLE & COLUMN INSPECTOR ================= */}
        {selectedTable && !selectedRelation && (
          <div>
            {/* Table Inspector Tabs: Properties | Relations | Code Gen */}
            <div className="px-4 py-2.5 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/40">
              <div className="grid grid-cols-3 gap-0.5 p-0.5 bg-slate-200/70 dark:bg-slate-800 rounded-md">
                <button
                  type="button"
                  onClick={() => setTableTab('properties')}
                  className={`flex items-center justify-center gap-1 py-1.5 px-1.5 text-[11.5px] font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                    tableTab === 'properties'
                      ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  <SlidersHorizontal className="w-3.5 h-3.5 shrink-0" />
                  <span>Properties</span>
                </button>
                <button
                  type="button"
                  onClick={() => setTableTab('relations')}
                  className={`flex items-center justify-center gap-1 py-1.5 px-1.5 text-[11.5px] font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                    tableTab === 'relations'
                      ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  <GitFork className="w-3.5 h-3.5 shrink-0" />
                  <span>Relations</span>
                  {tableRelationsBreakdown.totalCount > 0 && (
                    <span className="font-mono text-[10px] tabular-nums opacity-70">
                      {tableRelationsBreakdown.totalCount}
                    </span>
                  )}
                </button>
                <button
                  type="button"
                  onClick={() => setTableTab('codegen')}
                  className={`flex items-center justify-center gap-1 py-1.5 px-1.5 text-[11.5px] font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                    tableTab === 'codegen'
                      ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  <Code2 className="w-3.5 h-3.5 shrink-0" />
                  <span>Code Gen</span>
                </button>
              </div>
            </div>

            {tableTab === 'relations' ? (
              (() => {
                const { outgoing, incoming, manyToMany, totalCount } = tableRelationsBreakdown;

                const renderInlineRelationCard = (
                  r: Relation,
                  endpointLabel: string,
                  metaLabel?: string
                ) => (
                  <div
                    key={r.id}
                    className="p-3 rounded-md border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40 space-y-2.5"
                  >
                    {/* Header: Endpoint Mapping + Quick Focus / Delete */}
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="font-mono text-[11.5px] font-semibold text-slate-900 dark:text-slate-100 truncate">
                          {endpointLabel}
                        </div>
                        <div className="flex items-center gap-1.5 mt-0.5 text-[10.5px] text-slate-500 dark:text-slate-400 min-w-0">
                          <span className="font-mono truncate">{r.name || 'unnamed_fk'}</span>
                          {metaLabel && (
                            <>
                              <span aria-hidden="true">·</span>
                              <span className="shrink-0 text-blue-600 dark:text-blue-400 font-medium">
                                {metaLabel}
                              </span>
                            </>
                          )}
                        </div>
                      </div>
                      <div className="flex items-center gap-0.5 shrink-0">
                        <button
                          type="button"
                          onClick={() => onSelect({ type: 'relation', relationId: r.id })}
                          title="Inspect full relation properties"
                          aria-label={`Inspect ${r.name}`}
                          className="p-1 text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
                        >
                          <ArrowUpRight className="w-3.5 h-3.5" />
                        </button>
                        <button
                          type="button"
                          onClick={() => onDeleteRelation(r.id)}
                          title="Delete relation"
                          aria-label={`Delete ${r.name}`}
                          className="p-1 text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-950/40 rounded transition-colors cursor-pointer"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </div>

                    {/* Inline Cardinality Switcher */}
                    <div>
                      <label className="block text-[10.5px] font-medium text-slate-500 dark:text-slate-400 mb-1">
                        Cardinality
                      </label>
                      <div className="grid grid-cols-4 gap-1 p-0.5 bg-slate-200/60 dark:bg-slate-900 rounded">
                        {CARDINALITIES.map((card) => (
                          <button
                            key={card}
                            type="button"
                            onClick={() => onUpdateRelation(r.id, { cardinality: card })}
                            className={`py-1 text-[11px] font-mono font-medium rounded transition-colors cursor-pointer ${
                              r.cardinality === card
                                ? 'bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 shadow-xs'
                                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                            }`}
                          >
                            {card}
                          </button>
                        ))}
                      </div>
                    </div>

                    {/* Inline Referential Actions */}
                    <div className="grid grid-cols-2 gap-2">
                      <div>
                        <label className="block text-[10.5px] font-medium text-slate-500 dark:text-slate-400 mb-1">
                          On Delete
                        </label>
                        <select
                          value={r.onDelete}
                          onChange={(e) =>
                            onUpdateRelation(r.id, {
                              onDelete: e.target.value as ReferentialAction,
                            })
                          }
                          aria-label={`On delete action for ${r.name}`}
                          className={`${inputClass} font-mono text-[11px] py-1`}
                        >
                          {engineProfile.referentialActions.map((a) => (
                            <option key={a} value={a}>
                              {a}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className="block text-[10.5px] font-medium text-slate-500 dark:text-slate-400 mb-1">
                          On Update
                        </label>
                        <select
                          value={r.onUpdate}
                          onChange={(e) =>
                            onUpdateRelation(r.id, {
                              onUpdate: e.target.value as ReferentialAction,
                            })
                          }
                          aria-label={`On update action for ${r.name}`}
                          className={`${inputClass} font-mono text-[11px] py-1`}
                        >
                          {engineProfile.referentialActions.map((a) => (
                            <option key={a} value={a}>
                              {a}
                            </option>
                          ))}
                        </select>
                      </div>
                    </div>
                  </div>
                );

                return (
                  <div className="divide-y divide-slate-200 dark:divide-slate-800">
                    {/* Table Context & Quick Add */}
                    <div className="px-4 py-3 flex items-center justify-between gap-2">
                      <div className="min-w-0">
                        <div className="font-mono text-xs font-semibold text-slate-900 dark:text-slate-100 truncate">
                          {selectedTable.name}
                        </div>
                        <div className="text-[11px] text-slate-500 dark:text-slate-400 tabular-nums">
                          {outgoing.length} outgoing · {incoming.length} incoming
                          {manyToMany.length > 0 ? ` · ${manyToMany.length} M:N` : ''}
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => openQuickRelationForm(selectedTable.id)}
                        className="flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium text-blue-600 dark:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-950/50 rounded-md transition-colors cursor-pointer whitespace-nowrap shrink-0"
                      >
                        <Plus className="w-3.5 h-3.5" />
                        <span>Add Relation</span>
                      </button>
                    </div>

                    {totalCount === 0 ? (
                      <div className="p-6 text-center space-y-2.5">
                        <div className="text-xs font-semibold text-slate-800 dark:text-slate-200">
                          No foreign key relations yet
                        </div>
                        <p className="text-[11.5px] text-slate-500 dark:text-slate-400 leading-relaxed">
                          Drag from any column port on the canvas or click Add Relation to connect{' '}
                          <span className="font-mono text-slate-700 dark:text-slate-300">
                            {selectedTable.name}
                          </span>{' '}
                          with another table.
                        </p>
                        <button
                          type="button"
                          onClick={() => openQuickRelationForm(selectedTable.id)}
                          className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-md transition-colors cursor-pointer"
                        >
                          <Plus className="w-3.5 h-3.5" />
                          <span>Add Relation</span>
                        </button>
                      </div>
                    ) : (
                      <>
                        {/* Outgoing Foreign Keys */}
                        <div className="p-4 space-y-2.5">
                          <div className="flex items-center justify-between">
                            <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-800 dark:text-slate-200">
                              <ArrowUpRight className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400" />
                              <span>Outgoing Foreign Keys</span>
                            </span>
                            <span className="font-mono text-[11px] tabular-nums text-slate-400 dark:text-slate-500">
                              {outgoing.length}
                            </span>
                          </div>
                          {outgoing.length === 0 ? (
                            <p className="text-[11.5px] text-slate-400 dark:text-slate-500">
                              No outgoing foreign keys from this table.
                            </p>
                          ) : (
                            <div className="space-y-2.5">
                              {outgoing.map((item) =>
                                renderInlineRelationCard(
                                  item.relation,
                                  `${item.localColName} → ${item.remoteTableName}.${item.remoteColName}`,
                                  item.isSelfRef ? 'Self-ref' : undefined
                                )
                              )}
                            </div>
                          )}
                        </div>

                        {/* Incoming Foreign Keys */}
                        <div className="p-4 space-y-2.5">
                          <div className="flex items-center justify-between">
                            <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-800 dark:text-slate-200">
                              <ArrowDownLeft className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" />
                              <span>Incoming Foreign Keys</span>
                            </span>
                            <span className="font-mono text-[11px] tabular-nums text-slate-400 dark:text-slate-500">
                              {incoming.length}
                            </span>
                          </div>
                          {incoming.length === 0 ? (
                            <p className="text-[11.5px] text-slate-400 dark:text-slate-500">
                              No other tables reference this table.
                            </p>
                          ) : (
                            <div className="space-y-2.5">
                              {incoming.map((item) =>
                                renderInlineRelationCard(
                                  item.relation,
                                  `${item.remoteTableName}.${item.remoteColName} → ${item.localColName}`
                                )
                              )}
                            </div>
                          )}
                        </div>

                        {/* Many-to-Many Links (if any) */}
                        {manyToMany.length > 0 && (
                          <div className="p-4 space-y-2.5">
                            <div className="flex items-center justify-between">
                              <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-800 dark:text-slate-200">
                                <ArrowLeftRight className="w-3.5 h-3.5 text-indigo-600 dark:text-indigo-400" />
                                <span>Many-to-Many Links</span>
                              </span>
                              <span className="font-mono text-[11px] tabular-nums text-slate-400 dark:text-slate-500">
                                {manyToMany.length}
                              </span>
                            </div>
                            <div className="space-y-2.5">
                              {manyToMany.map((item) =>
                                renderInlineRelationCard(
                                  item.relation,
                                  `${item.localColName} ↔ ${item.remoteTableName}.${item.remoteColName}`,
                                  'Junction table'
                                )
                              )}
                            </div>
                          </div>
                        )}
                      </>
                    )}
                  </div>
                );
              })()
            ) : tableTab === 'codegen' ? (
              (() => {
                const codeGen = normalizeTableCodeGen(selectedTable.codeGen);
                const updateCodeGen = (next: TableCodeGenConfig) => {
                  onUpdateTable(selectedTable.id, { codeGen: next });
                };
                const enabledCount = CRUD_METHOD_OPTIONS.filter(
                  (m) => codeGen.serviceMethods[m.key]
                ).length;

                return (
                  <div className="divide-y divide-slate-200 dark:divide-slate-800">
                    {/* Table Context */}
                    <div className="px-4 py-3 flex items-center justify-between gap-2">
                      <span className="text-[11px] font-medium text-slate-500 dark:text-slate-400">
                        Target Table
                      </span>
                      <span className="font-mono text-xs font-semibold text-slate-900 dark:text-slate-100 truncate">
                        {selectedTable.name}
                      </span>
                    </div>

                    {/* Service Layer Config */}
                    <div className="p-4 space-y-3.5">
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="text-xs font-semibold text-slate-900 dark:text-slate-100">
                            Add Service
                          </div>
                          <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 leading-snug">
                            Generate a service layer with data access methods for this table.
                          </p>
                        </div>
                        <button
                          type="button"
                          role="switch"
                          aria-checked={codeGen.addService}
                          aria-label="Toggle add service"
                          onClick={() => {
                            const nextAddService = !codeGen.addService;
                            updateCodeGen({
                              ...codeGen,
                              addService: nextAddService,
                              addApiController: nextAddService ? codeGen.addApiController : false,
                            });
                          }}
                          className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors cursor-pointer ${
                            codeGen.addService
                              ? 'bg-blue-600'
                              : 'bg-slate-200 dark:bg-slate-700'
                          }`}
                        >
                          <span
                            className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition-transform ${
                              codeGen.addService ? 'translate-x-4.5' : 'translate-x-1'
                            }`}
                          />
                        </button>
                      </div>

                      {codeGen.addService && (
                        <div className="pt-1 space-y-2.5">
                          <div className="flex items-center justify-between">
                            <span className="text-[11px] font-medium text-slate-600 dark:text-slate-300 tabular-nums">
                              CRUD Functions ({enabledCount}/{CRUD_METHOD_OPTIONS.length})
                            </span>
                            <div className="flex items-center gap-2 text-[11px]">
                              <button
                                type="button"
                                onClick={() =>
                                  updateCodeGen({
                                    ...codeGen,
                                    serviceMethods: {
                                      getAll: true,
                                      select: true,
                                      find: true,
                                      add: true,
                                      update: true,
                                      delete: true,
                                    },
                                  })
                                }
                                className="text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
                              >
                                Select all
                              </button>
                              <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
                                /
                              </span>
                              <button
                                type="button"
                                onClick={() =>
                                  updateCodeGen({
                                    ...codeGen,
                                    serviceMethods: {
                                      getAll: false,
                                      select: false,
                                      find: false,
                                      add: false,
                                      update: false,
                                      delete: false,
                                    },
                                  })
                                }
                                className="text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200 hover:underline cursor-pointer"
                              >
                                Clear
                              </button>
                            </div>
                          </div>

                          <div className="divide-y divide-slate-100 dark:divide-slate-800/80 border border-slate-200 dark:border-slate-800 rounded-md overflow-hidden">
                            {CRUD_METHOD_OPTIONS.map((method) => {
                              const checked = codeGen.serviceMethods[method.key];
                              return (
                                <label
                                  key={method.key}
                                  className="px-3 py-2 flex items-center justify-between gap-2.5 bg-white dark:bg-[#0F172A] hover:bg-slate-50 dark:hover:bg-slate-800/40 cursor-pointer transition-colors"
                                >
                                  <div className="flex items-center gap-2.5 min-w-0">
                                    <input
                                      type="checkbox"
                                      checked={checked}
                                      onChange={() =>
                                        updateCodeGen({
                                          ...codeGen,
                                          serviceMethods: {
                                            ...codeGen.serviceMethods,
                                            [method.key]: !checked,
                                          },
                                        })
                                      }
                                      className="rounded border-slate-300 dark:border-slate-600 text-blue-600 focus:ring-blue-500 cursor-pointer"
                                    />
                                    <div className="min-w-0">
                                      <div className="font-mono text-xs font-medium text-slate-900 dark:text-slate-100">
                                        {method.label}
                                      </div>
                                      <div className="text-[11px] text-slate-500 dark:text-slate-400 truncate">
                                        {method.hint}
                                      </div>
                                    </div>
                                  </div>
                                </label>
                              );
                            })}
                          </div>
                        </div>
                      )}
                    </div>

                    {/* API Controller Layer Config */}
                    <div className={`p-4 space-y-3 ${!codeGen.addService ? 'opacity-60' : ''}`}>
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="text-xs font-semibold text-slate-900 dark:text-slate-100">
                            Add API Controller
                          </div>
                          <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 leading-snug">
                            {codeGen.addService
                              ? 'Expose REST controller routes mapped to this table.'
                              : 'Requires Add Service to be enabled first.'}
                          </p>
                        </div>
                        <button
                          type="button"
                          role="switch"
                          disabled={!codeGen.addService}
                          aria-checked={codeGen.addService && codeGen.addApiController}
                          aria-label="Toggle add API controller"
                          onClick={() => {
                            if (!codeGen.addService) return;
                            updateCodeGen({
                              ...codeGen,
                              addApiController: !codeGen.addApiController,
                            });
                          }}
                          className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${
                            !codeGen.addService
                              ? 'bg-slate-200 dark:bg-slate-700 cursor-not-allowed'
                              : codeGen.addApiController
                              ? 'bg-blue-600 cursor-pointer'
                              : 'bg-slate-200 dark:bg-slate-700 cursor-pointer'
                          }`}
                        >
                          <span
                            className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition-transform ${
                              codeGen.addService && codeGen.addApiController
                                ? 'translate-x-4.5'
                                : 'translate-x-1'
                            }`}
                          />
                        </button>
                      </div>

                      {codeGen.addService && codeGen.addApiController && (
                        <div className="pt-1 space-y-1.5">
                          <div className="text-[11px] font-medium text-slate-500 dark:text-slate-400">
                            Controller Base Path
                          </div>
                          <div className="font-mono text-xs text-slate-800 dark:text-slate-200 bg-slate-50 dark:bg-slate-900/90 border border-slate-200 dark:border-slate-800 rounded-md px-2.5 py-1.5 truncate">
                            /api/{selectedTable.name}
                          </div>

                          {codeGen.addService && enabledCount > 0 && (
                            <div className="pt-1.5 space-y-1">
                              {CRUD_METHOD_OPTIONS.filter(
                                (m) => codeGen.serviceMethods[m.key]
                              ).map((m) => (
                                <div
                                  key={m.key}
                                  className="flex items-center justify-between gap-2 font-mono text-[11px] text-slate-600 dark:text-slate-400"
                                >
                                  <span className="truncate">
                                    <span className="font-semibold text-blue-600 dark:text-blue-400 inline-block w-13">
                                      {m.httpMethod}
                                    </span>
                                    /api/{selectedTable.name}
                                    {m.routeSuffix}
                                  </span>
                                  <span className="text-slate-400 dark:text-slate-500 shrink-0">
                                    {m.label}
                                  </span>
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                );
              })()
            ) : (
              <div className="divide-y divide-slate-200 dark:divide-slate-800">
                {/* Table Meta */}
                <div className="p-4 space-y-3">
              {engineProfile.supportsSchemaNamespace ? (
                <div className="grid grid-cols-3 gap-2">
                  <div className="col-span-2">
                    <label className={labelClass}>Table Name</label>
                    <input
                      type="text"
                      value={selectedTable.name}
                      onChange={(e) =>
                        onUpdateTable(selectedTable.id, { name: e.target.value })
                      }
                      spellCheck={false}
                      className={`${inputClass} font-mono font-medium`}
                    />
                  </div>
                  <div>
                    <label className={labelClass}>Schema</label>
                    <input
                      type="text"
                      value={selectedTable.schema}
                      placeholder={engineProfile.defaultSchema || 'optional'}
                      onChange={(e) =>
                        onUpdateTable(selectedTable.id, { schema: e.target.value })
                      }
                      spellCheck={false}
                      className={`${inputClass} font-mono`}
                    />
                  </div>
                </div>
              ) : (
                <div className="space-y-3">
                  <div>
                    <label className={labelClass}>Table Name</label>
                    <input
                      type="text"
                      value={selectedTable.name}
                      onChange={(e) =>
                        onUpdateTable(selectedTable.id, { name: e.target.value })
                      }
                      spellCheck={false}
                      className={`${inputClass} font-mono font-medium`}
                    />
                  </div>
                  {engineProfile.supportsStorageEngine && (
                    <div className="grid grid-cols-2 gap-2">
                      <div>
                        <label className={labelClass}>Storage Engine</label>
                        <select
                          value={selectedTable.storageEngine || 'InnoDB'}
                          onChange={(e) =>
                            onUpdateTable(selectedTable.id, {
                              storageEngine: e.target.value as MysqlStorageEngine,
                            })
                          }
                          className={`${inputClass} font-mono`}
                        >
                          {MYSQL_STORAGE_ENGINES.map((eng) => (
                            <option key={eng} value={eng}>
                              {eng}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className={labelClass}>Charset</label>
                        <select
                          value={selectedTable.charset || 'utf8mb4'}
                          onChange={(e) =>
                            onUpdateTable(selectedTable.id, { charset: e.target.value })
                          }
                          className={`${inputClass} font-mono`}
                        >
                          {MYSQL_CHARSETS.map((cs) => (
                            <option key={cs} value={cs}>
                              {cs}
                            </option>
                          ))}
                        </select>
                      </div>
                    </div>
                  )}
                </div>
              )}

              <div>
                <label className={labelClass}>Header Accent</label>
                <div className="flex items-center gap-2 pt-0.5">
                  {TABLE_ACCENT_PALETTE.map((swatch) => (
                    <button
                      key={swatch.id}
                      type="button"
                      title={swatch.label}
                      aria-label={`Set table color to ${swatch.label}`}
                      onClick={() =>
                        onUpdateTable(selectedTable.id, { color: swatch.id })
                      }
                      style={{ backgroundColor: swatch.dotColor }}
                      className={`w-5 h-5 rounded-full transition-transform cursor-pointer ${
                        selectedTable.color === swatch.id
                          ? 'ring-2 ring-offset-2 ring-slate-900 dark:ring-white dark:ring-offset-[#0F172A] scale-110'
                          : 'opacity-80 hover:opacity-100'
                      }`}
                    />
                  ))}
                </div>
              </div>

              <div>
                <label className={labelClass}>Table Comment</label>
                <input
                  type="text"
                  value={selectedTable.comment}
                  onChange={(e) =>
                    onUpdateTable(selectedTable.id, { comment: e.target.value })
                  }
                  placeholder="Optional SQL documentation comment"
                  className={inputClass}
                />
              </div>

              <div className="flex items-center gap-2 pt-1">
                <button
                  type="button"
                  onClick={() => onDuplicateTable(selectedTable.id)}
                  className="flex-1 py-1.5 px-2.5 text-xs font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200/80 dark:hover:bg-slate-700 rounded-md flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
                >
                  <Copy className="w-3.5 h-3.5" />
                  <span>Duplicate</span>
                </button>
                <button
                  type="button"
                  onClick={() => onDeleteTable(selectedTable.id)}
                  className="flex-1 py-1.5 px-2.5 text-xs font-medium text-rose-600 dark:text-rose-400 border border-rose-200 dark:border-rose-900/60 hover:bg-rose-50 dark:hover:bg-rose-950/40 rounded-md flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  <span>Delete</span>
                </button>
              </div>
            </div>

            {/* Columns Manager */}
            <div className="p-4">
              <div className="flex items-center justify-between mb-2.5">
                <span className="text-xs font-semibold text-slate-800 dark:text-slate-200">
                  Columns ({selectedTable.columns.length})
                </span>
                <button
                  type="button"
                  onClick={() => onAddColumn(selectedTable.id)}
                  className="flex items-center gap-1 px-2 py-1 text-xs font-medium text-blue-600 dark:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-950/50 rounded transition-colors cursor-pointer"
                >
                  <Plus className="w-3.5 h-3.5" />
                  <span>Add Column</span>
                </button>
              </div>

              <div className="divide-y divide-slate-100 dark:divide-slate-800/80 border border-slate-200 dark:border-slate-800 rounded-md overflow-hidden">
                {selectedTable.columns.map((col, idx) => {
                  const isFocused = selectedColumn?.id === col.id;
                  return (
                    <div key={col.id} className="bg-white dark:bg-[#0F172A]">
                      {/* Compact Column Row Header */}
                      <div
                        onClick={() =>
                          onSelect(
                            isFocused
                              ? { type: 'table', tableId: selectedTable.id }
                              : {
                                  type: 'column',
                                  tableId: selectedTable.id,
                                  columnId: col.id,
                                }
                          )
                        }
                        className={`px-2.5 py-2 flex items-center justify-between gap-2 cursor-pointer transition-colors ${
                          isFocused
                            ? 'bg-blue-50/90 dark:bg-blue-950/50'
                            : 'hover:bg-slate-50 dark:hover:bg-slate-800/40'
                        }`}
                      >
                        <div className="flex items-center gap-1.5 min-w-0">
                          {col.isPrimaryKey ? (
                            <KeyRound className="w-3 h-3 text-amber-600 dark:text-amber-400 shrink-0" />
                          ) : (
                            <span className="w-3 text-center text-[10px] font-mono text-slate-400 shrink-0">
                              {idx + 1}
                            </span>
                          )}
                          <span className="font-mono text-xs font-medium text-slate-900 dark:text-slate-100 truncate">
                            {col.name}
                          </span>
                        </div>
                        <span className="font-mono text-[11px] text-slate-500 dark:text-slate-400 truncate max-w-[96px]">
                          {col.type}
                        </span>
                      </div>

                      {/* Expanded Column Property Editor */}
                      {isFocused && (
                        <div className="px-3 py-3 bg-slate-50/70 dark:bg-slate-900/50 border-t border-slate-200/80 dark:border-slate-800 space-y-3">
                          <div className="grid grid-cols-2 gap-2">
                            <div>
                              <label className={labelClass}>Column Name</label>
                              <input
                                type="text"
                                value={col.name}
                                onChange={(e) =>
                                  onUpdateColumn(selectedTable.id, col.id, {
                                    name: e.target.value,
                                  })
                                }
                                spellCheck={false}
                                className={`${inputClass} font-mono`}
                              />
                            </div>
                            <div>
                              <div className="flex items-center justify-between mb-1">
                                <label className="text-[11px] font-medium text-slate-500 dark:text-slate-400">
                                  Data Type
                                </label>
                                <span className="text-[10px] text-slate-400 dark:text-slate-500">
                                  {engineProfile.shortLabel}
                                </span>
                              </div>
                              {customTypeColId === col.id ? (
                                <div className="flex items-center gap-1">
                                  <input
                                    type="text"
                                    list="sql-data-types"
                                    value={col.type}
                                    autoFocus
                                    onChange={(e) =>
                                      onUpdateColumn(selectedTable.id, col.id, {
                                        type: e.target.value,
                                      })
                                    }
                                    onBlur={() => setCustomTypeColId(null)}
                                    onKeyDown={(e) => {
                                      if (e.key === 'Enter' || e.key === 'Escape') {
                                        setCustomTypeColId(null);
                                      }
                                    }}
                                    spellCheck={false}
                                    className={`${inputClass} font-mono`}
                                  />
                                </div>
                              ) : (
                                <select
                                  value={col.type}
                                  onChange={(e) => {
                                    if (e.target.value === '__custom__') {
                                      setCustomTypeColId(col.id);
                                      return;
                                    }
                                    onUpdateColumn(selectedTable.id, col.id, {
                                      type: e.target.value,
                                    });
                                  }}
                                  className={`${inputClass} font-mono`}
                                >
                                  {!engineProfile.dataTypes.includes(col.type) && col.type && (
                                    <option value={col.type}>{col.type}</option>
                                  )}
                                  {engineProfile.dataTypes.map((t) => (
                                    <option key={t} value={t}>
                                      {t}
                                    </option>
                                  ))}
                                  <option value="__custom__">Custom type...</option>
                                </select>
                              )}
                            </div>
                          </div>

                          {/* Constraint Toggle Grid */}
                          <div>
                            <label className={labelClass}>Constraints</label>
                            <div className="grid grid-cols-4 gap-1">
                              {[
                                {
                                  key: 'pk',
                                  label: 'PK',
                                  active: col.isPrimaryKey,
                                  onToggle: () =>
                                    onUpdateColumn(selectedTable.id, col.id, {
                                      isPrimaryKey: !col.isPrimaryKey,
                                      isNullable: !col.isPrimaryKey ? false : col.isNullable,
                                    }),
                                },
                                {
                                  key: 'nn',
                                  label: 'NOT NULL',
                                  active: !col.isNullable,
                                  onToggle: () =>
                                    onUpdateColumn(selectedTable.id, col.id, {
                                      isNullable: !col.isNullable,
                                    }),
                                },
                                {
                                  key: 'uq',
                                  label: 'UNIQUE',
                                  active: col.isUnique,
                                  onToggle: () =>
                                    onUpdateColumn(selectedTable.id, col.id, {
                                      isUnique: !col.isUnique,
                                    }),
                                },
                                {
                                  key: 'ai',
                                  label: engineProfile.autoIncrementLabel,
                                  title: engineProfile.autoIncrementHint,
                                  active: col.isAutoIncrement,
                                  onToggle: () =>
                                    onUpdateColumn(selectedTable.id, col.id, {
                                      isAutoIncrement: !col.isAutoIncrement,
                                    }),
                                },
                              ].map((flag) => (
                                <button
                                  key={flag.key}
                                  type="button"
                                  title={'title' in flag ? flag.title : undefined}
                                  onClick={flag.onToggle}
                                  className={`py-1.5 px-1 text-[10px] font-mono font-semibold rounded border transition-colors cursor-pointer truncate ${
                                    flag.active
                                      ? 'bg-blue-600 text-white border-blue-600'
                                      : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:border-slate-300 dark:hover:border-slate-600'
                                  }`}
                                >
                                  {flag.label}
                                </button>
                              ))}
                            </div>
                          </div>

                          <div className="grid grid-cols-2 gap-2">
                            <div>
                              <label className={labelClass}>Default Value</label>
                              <input
                                type="text"
                                value={col.defaultValue}
                                onChange={(e) =>
                                  onUpdateColumn(selectedTable.id, col.id, {
                                    defaultValue: e.target.value,
                                  })
                                }
                                placeholder="e.g. now(), 0"
                                spellCheck={false}
                                className={`${inputClass} font-mono`}
                              />
                            </div>
                            <div>
                              <label className={labelClass}>Check Expr</label>
                              <input
                                type="text"
                                value={col.checkConstraint}
                                onChange={(e) =>
                                  onUpdateColumn(selectedTable.id, col.id, {
                                    checkConstraint: e.target.value,
                                  })
                                }
                                placeholder="e.g. qty > 0"
                                spellCheck={false}
                                className={`${inputClass} font-mono`}
                              />
                            </div>
                          </div>

                          <div>
                            <label className={labelClass}>Column Comment</label>
                            <input
                              type="text"
                              value={col.comment}
                              onChange={(e) =>
                                onUpdateColumn(selectedTable.id, col.id, {
                                  comment: e.target.value,
                                })
                              }
                              placeholder="Optional description"
                              className={inputClass}
                            />
                          </div>

                          {/* Reorder & Remove Column */}
                          <div className="flex items-center justify-between pt-1">
                            <div className="flex items-center gap-1">
                              <button
                                type="button"
                                disabled={idx === 0}
                                onClick={() => onMoveColumn(selectedTable.id, col.id, 'up')}
                                title="Move column up"
                                className="p-1 text-slate-500 hover:text-slate-900 dark:hover:text-white disabled:opacity-30 rounded hover:bg-slate-200/70 dark:hover:bg-slate-800 cursor-pointer"
                              >
                                <ArrowUp className="w-3.5 h-3.5" />
                              </button>
                              <button
                                type="button"
                                disabled={idx === selectedTable.columns.length - 1}
                                onClick={() => onMoveColumn(selectedTable.id, col.id, 'down')}
                                title="Move column down"
                                className="p-1 text-slate-500 hover:text-slate-900 dark:hover:text-white disabled:opacity-30 rounded hover:bg-slate-200/70 dark:hover:bg-slate-800 cursor-pointer"
                              >
                                <ArrowDown className="w-3.5 h-3.5" />
                              </button>
                            </div>
                            <button
                              type="button"
                              onClick={() => onDeleteColumn(selectedTable.id, col.id)}
                              className="flex items-center gap-1 text-[11px] font-medium text-rose-600 dark:text-rose-400 hover:underline cursor-pointer"
                            >
                              <Trash2 className="w-3 h-3" />
                              <span>Remove column</span>
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
              <datalist id="sql-data-types">
                {engineProfile.dataTypes.map((t) => (
                  <option key={t} value={t} />
                ))}
              </datalist>
            </div>

            {/* Indexes Manager */}
            <div className="p-4 space-y-2.5">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-slate-800 dark:text-slate-200 tabular-nums">
                  Indexes ({(selectedTable.indexes || []).length})
                </span>
                <button
                  type="button"
                  onClick={() => {
                    const existing = selectedTable.indexes || [];
                    const defaultCol =
                      selectedTable.columns.find((c) => !c.isPrimaryKey) ||
                      selectedTable.columns[0];
                    const nextIdx: TableIndex = {
                      id: `idx-${Date.now()}`,
                      name: `idx_${selectedTable.name}_${defaultCol?.name || existing.length + 1}`,
                      columns: defaultCol ? [defaultCol.id] : [],
                      isUnique: false,
                    };
                    onUpdateTable(selectedTable.id, {
                      indexes: [...existing, nextIdx],
                    });
                  }}
                  className="flex items-center gap-1 px-2 py-1 text-xs font-medium text-blue-600 dark:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-950/50 rounded transition-colors cursor-pointer"
                >
                  <Plus className="w-3.5 h-3.5" />
                  <span>Add Index</span>
                </button>
              </div>

              {(selectedTable.indexes || []).length === 0 ? (
                <p className="text-xs text-slate-400 dark:text-slate-500">
                  No custom indexes defined on this table.
                </p>
              ) : (
                <div className="space-y-2">
                  {(selectedTable.indexes || []).map((idx) => {
                    const updateIndex = (patch: Partial<TableIndex>) => {
                      const nextList = (selectedTable.indexes || []).map((item) =>
                        item.id === idx.id ? { ...item, ...patch } : item
                      );
                      onUpdateTable(selectedTable.id, { indexes: nextList });
                    };

                    const removeIndex = () => {
                      const nextList = (selectedTable.indexes || []).filter(
                        (item) => item.id !== idx.id
                      );
                      onUpdateTable(selectedTable.id, { indexes: nextList });
                    };

                    const toggleColumn = (colId: string) => {
                      const exists = idx.columns.includes(colId);
                      const nextCols = exists
                        ? idx.columns.filter((c) => c !== colId)
                        : [...idx.columns, colId];
                      updateIndex({ columns: nextCols });
                    };

                    return (
                      <div
                        key={idx.id}
                        className="p-2.5 rounded-md border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40 space-y-2"
                      >
                        <div className="flex items-center gap-1.5">
                          <input
                            type="text"
                            value={idx.name}
                            onChange={(e) => updateIndex({ name: e.target.value })}
                            placeholder="idx_table_column"
                            aria-label="Index name"
                            spellCheck={false}
                            className={`${inputClass} font-mono flex-1 min-w-0`}
                          />
                          <button
                            type="button"
                            onClick={() => updateIndex({ isUnique: !idx.isUnique })}
                            title="Toggle unique index"
                            className={`px-2 py-1.5 text-[10px] font-mono font-semibold rounded border transition-colors cursor-pointer shrink-0 ${
                              idx.isUnique
                                ? 'bg-blue-600 text-white border-blue-600'
                                : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:border-slate-300 dark:hover:border-slate-600'
                            }`}
                          >
                            UNIQUE
                          </button>
                          <button
                            type="button"
                            onClick={removeIndex}
                            title="Remove index"
                            aria-label={`Remove index ${idx.name}`}
                            className="p-1.5 text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 rounded transition-colors cursor-pointer shrink-0"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>

                        <div className="flex flex-wrap gap-1">
                          {selectedTable.columns.map((col) => {
                            const selected = idx.columns.includes(col.id);
                            return (
                              <button
                                key={col.id}
                                type="button"
                                onClick={() => toggleColumn(col.id)}
                                className={`px-2 py-0.5 text-[11px] font-mono rounded border transition-colors cursor-pointer ${
                                  selected
                                    ? 'bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300 border-blue-300 dark:border-blue-700'
                                    : 'bg-white dark:bg-slate-900 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-800 hover:text-slate-800 dark:hover:text-slate-200'
                                }`}
                              >
                                {col.name}
                              </button>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            {/* Compact Shortcut to Relations Tab */}
            <div className="p-4 flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-xs font-semibold text-slate-800 dark:text-slate-200 tabular-nums">
                  Relations ({tableRelationsBreakdown.totalCount})
                </div>
                <div className="text-[11px] text-slate-500 dark:text-slate-400 tabular-nums">
                  {tableRelationsBreakdown.outgoing.length} outgoing ·{' '}
                  {tableRelationsBreakdown.incoming.length} incoming
                </div>
              </div>
              <button
                type="button"
                onClick={() => setTableTab('relations')}
                className="flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium text-blue-600 dark:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-950/50 rounded-md transition-colors cursor-pointer whitespace-nowrap shrink-0"
              >
                <span>Manage</span>
                <ArrowUpRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
            )}
          </div>
        )}

        {/* ================= SCHEMA OVERVIEW (NOTHING SELECTED) ================= */}
        {!selectedTable && !selectedRelation && (
          <div className="divide-y divide-slate-200 dark:divide-slate-800">
            <div className="p-4 space-y-3">
              <div>
                <label className={labelClass}>Schema Name</label>
                <input
                  type="text"
                  value={schema.name}
                  onChange={(e) => onUpdateSchemaMeta({ name: e.target.value })}
                  spellCheck={false}
                  className={`${inputClass} font-mono`}
                />
              </div>

              <div>
                <label className={labelClass}>Database Engine</label>
                <div
                  className={`grid ${
                    SUPPORTED_ENGINE_LIST.length === 1 ? 'grid-cols-1' : 'grid-cols-2'
                  } gap-1 p-1 bg-slate-100 dark:bg-slate-900 rounded-md`}
                >
                  {SUPPORTED_ENGINE_LIST.map((profile) => {
                    const active = profile.id === activeEngineId;
                    return (
                      <button
                        key={profile.id}
                        type="button"
                        onClick={() => onEngineChange(profile.id)}
                        className={`py-1.5 px-2 text-xs font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                          active
                            ? 'bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 shadow-xs'
                            : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                        }`}
                      >
                        {profile.label}
                      </button>
                    );
                  })}
                </div>
                <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-1.5 leading-snug">
                  {engineProfile.description}
                </p>
              </div>

              <div>
                <label className={labelClass}>Description</label>
                <textarea
                  rows={3}
                  value={schema.description}
                  onChange={(e) => onUpdateSchemaMeta({ description: e.target.value })}
                  placeholder="Describe this database architecture..."
                  className={`${inputClass} resize-none`}
                />
              </div>

              <div className="pt-1 flex items-center gap-3 text-xs text-slate-500 dark:text-slate-400 tabular-nums">
                <span>
                  <strong className="font-semibold text-slate-900 dark:text-slate-100">
                    {schema.tables.length}
                  </strong>{' '}
                  tables
                </span>
                <span aria-hidden="true">·</span>
                <span>
                  <strong className="font-semibold text-slate-900 dark:text-slate-100">
                    {schema.tables.reduce((acc, t) => acc + t.columns.length, 0)}
                  </strong>{' '}
                  columns
                </span>
                <span aria-hidden="true">·</span>
                <span>
                  <strong className="font-semibold text-slate-900 dark:text-slate-100">
                    {schema.relations.length}
                  </strong>{' '}
                  relations
                </span>
              </div>
            </div>

            <div className="p-4 space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-slate-800 dark:text-slate-200">
                  Create Relation
                </span>
                {schema.tables.length > 0 && !quickRelOpen && (
                  <button
                    type="button"
                    onClick={() => openQuickRelationForm()}
                    className="flex items-center gap-1 text-xs font-medium text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
                  >
                    <GitFork className="w-3.5 h-3.5" />
                    <span>New link</span>
                  </button>
                )}
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 leading-relaxed">
                Click any table, column, or relation line on the canvas to inspect and edit its properties. Drag from any column side port to wire a foreign key relation.
              </p>
            </div>

            <div className="p-4 space-y-2">
              <span className="text-xs font-semibold text-slate-800 dark:text-slate-200 block">
                Workspace Shortcuts
              </span>
              <div className="space-y-1.5 text-xs text-slate-500 dark:text-slate-400">
                <div className="flex items-center justify-between">
                  <span>Pan canvas</span>
                  <span className="font-mono text-[11px] text-slate-700 dark:text-slate-300">
                    Drag background
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span>Zoom workspace</span>
                  <span className="font-mono text-[11px] text-slate-700 dark:text-slate-300">
                    Mouse wheel
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span>Undo / Redo</span>
                  <span className="font-mono text-[11px] text-slate-700 dark:text-slate-300">
                    Ctrl+Z / Ctrl+Shift+Z
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span>Delete selection</span>
                  <span className="font-mono text-[11px] text-slate-700 dark:text-slate-300">
                    Delete / Backspace
                  </span>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Quick Relation Modal Inline Section */}
        {quickRelOpen && schema.tables.length > 0 && (
          <form
            onSubmit={handleSubmitQuickRelation}
            className="m-4 p-3.5 rounded-lg border border-blue-200 dark:border-blue-800/80 bg-blue-50/40 dark:bg-blue-950/30 space-y-2.5"
          >
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-900 dark:text-slate-100">
                New Relation
              </span>
              <button
                type="button"
                onClick={() => setQuickRelOpen(false)}
                className="p-0.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>

            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className={labelClass}>Source Table</label>
                <select
                  value={newRelSourceTable}
                  onChange={(e) => {
                    const t = tableMap.get(e.target.value);
                    setNewRelSourceTable(e.target.value);
                    setNewRelSourceCol(t?.columns[0]?.id ?? '');
                  }}
                  className={`${inputClass} font-mono`}
                >
                  {schema.tables.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className={labelClass}>Source Column</label>
                <select
                  value={newRelSourceCol}
                  onChange={(e) => setNewRelSourceCol(e.target.value)}
                  className={`${inputClass} font-mono`}
                >
                  {(tableMap.get(newRelSourceTable)?.columns ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className={labelClass}>Target Table</label>
                <select
                  value={newRelTargetTable}
                  onChange={(e) => {
                    const t = tableMap.get(e.target.value);
                    setNewRelTargetTable(e.target.value);
                    setNewRelTargetCol(t?.columns[0]?.id ?? '');
                  }}
                  className={`${inputClass} font-mono`}
                >
                  {schema.tables.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className={labelClass}>Target Column</label>
                <select
                  value={newRelTargetCol}
                  onChange={(e) => setNewRelTargetCol(e.target.value)}
                  className={`${inputClass} font-mono`}
                >
                  {(tableMap.get(newRelTargetTable)?.columns ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div>
              <label className={labelClass}>Cardinality</label>
              <div className="grid grid-cols-4 gap-1">
                {CARDINALITIES.map((c) => (
                  <button
                    key={c}
                    type="button"
                    onClick={() => setNewRelCard(c)}
                    className={`py-1 text-xs font-mono rounded border cursor-pointer ${
                      newRelCard === c
                        ? 'bg-blue-600 text-white border-blue-600'
                        : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700'
                    }`}
                  >
                    {c}
                  </button>
                ))}
              </div>
            </div>

            <button
              type="submit"
              className="w-full py-1.5 px-3 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-md transition-colors cursor-pointer"
            >
              Create Relation
            </button>
          </form>
        )}
      </div>
    </aside>
  );
};
