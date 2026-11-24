import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { bootstrapSession, mmaClient } from './api/mmaClient';
import { Canvas } from './components/Canvas';
import { GenerateModal } from './components/codegen/GenerateModal';
import { ExportModal, ExportModalMode } from './components/ExportModal';
import { SolutionsHub } from './components/hub/SolutionsHub';
import {
  DatabaseImportPayload,
  ImportDatabaseModal,
} from './components/ImportDatabaseModal';
import { InspectorPanel } from './components/InspectorPanel';
import { OutlineDrawer } from './components/OutlineDrawer';
import { StatusBar } from './components/StatusBar';
import { Toolbar } from './components/Toolbar';
import {
  DEFAULT_DATABASE_ENGINE,
  isDatabaseEngineSupported,
  resolveSupportedDatabaseEngine,
} from './config/featureFlags';
import { convertSchemaToEngine, ENGINE_PROFILES } from './data/engines';
import { SCHEMA_TEMPLATES, SchemaTemplate, TABLE_ACCENT_PALETTE } from './data/templates';
import {
  EntitySyncStatus,
  HealthInfo,
  ProjectSummary,
  SchemaSyncState,
} from './types/mma';
import {
  Cardinality,
  Column,
  DatabaseEngine,
  normalizeTableCodeGen,
  Relation,
  SchemaModel,
  SelectionState,
  Table,
  ViewportState,
} from './types/schema';
import { computeAutoLayout, getTableHeight, TABLE_WIDTH } from './utils/geometry';
import { validateSchema, ValidationIssue } from './utils/validator';

const THEME_KEY = 'schemaforge_theme_v1';
const MAX_HISTORY = 40;

function cloneModel(model: SchemaModel): SchemaModel {
  return JSON.parse(JSON.stringify(model));
}

function createEmptySchemaModel(): SchemaModel {
  return {
    id: `schema-${Date.now()}`,
    name: 'untitled_schema',
    engine: DEFAULT_DATABASE_ENGINE,
    description: '',
    updatedAt: new Date().toISOString(),
    tables: [],
    relations: [],
  };
}

function loadInitialTheme(): 'light' | 'dark' {
  try {
    const stored = localStorage.getItem(THEME_KEY);
    if (stored === 'dark' || stored === 'light') return stored;
  } catch {
    // Ignore
  }
  return 'light';
}

export default function App() {
  const [schema, setSchema] = useState<SchemaModel>(createEmptySchemaModel);
  const [past, setPast] = useState<SchemaModel[]>([]);
  const [future, setFuture] = useState<SchemaModel[]>([]);

  const [selection, setSelection] = useState<SelectionState>({ type: 'none' });

  const [viewport, setViewport] = useState<ViewportState>({ x: 36, y: 28, zoom: 0.94 });
  const [snapGrid, setSnapGrid] = useState(true);
  const [showOutline, setShowOutline] = useState(false);
  const [showInspector, setShowInspector] = useState(false);
  const [exportModalMode, setExportModalMode] = useState<ExportModalMode>(null);
  const [exportLiveWindow, setExportLiveWindow] = useState(false);
  const [importDbModalOpen, setImportDbModalOpen] = useState(false);
  const [generateModalOpen, setGenerateModalOpen] = useState(false);
  const [theme, setTheme] = useState<'light' | 'dark'>(loadInitialTheme);

  const [viewMode, setViewMode] = useState<'booting' | 'hub' | 'workspace' | 'scratchpad'>('booting');
  const [health, setHealth] = useState<HealthInfo | null>(null);
  const [activeProject, setActiveProject] = useState<ProjectSummary | null>(null);
  const [syncState, setSyncState] = useState<SchemaSyncState | null>(null);
  const [savingSchema, setSavingSchema] = useState(false);
  const hydratedSchemaRef = useRef(false);
  const schemaRef = useRef(schema);
  schemaRef.current = schema;

  const canvasContainerRef = useRef<HTMLDivElement | null>(null);
  const outlineSearchInputRef = useRef<HTMLInputElement | null>(null);

  const validation = useMemo(() => validateSchema(schema), [schema]);
  const totalColumns = useMemo(
    () => schema.tables.reduce((acc, t) => acc + t.columns.length, 0),
    [schema.tables]
  );
  const totalIndexes = useMemo(
    () => schema.tables.reduce((acc, t) => acc + (t.indexes?.length ?? 0), 0),
    [schema.tables]
  );

  const entitySyncMap = useMemo(() => {
    if (!syncState) return undefined;
    const map = new Map<string, EntitySyncStatus>();
    for (const item of syncState.entities) {
      map.set(item.entityName.toLowerCase(), item);
    }
    return map;
  }, [syncState]);

  const openProjectWorkspace = useCallback(async (project: ProjectSummary) => {
    hydratedSchemaRef.current = false;
    const response = await mmaClient.getProjectSchema(project.id);
    setActiveProject(response.project);
    setSyncState(response.syncState);
    setSchema(response.schema);
    setPast([]);
    setFuture([]);
    setSelection({ type: 'none' });
    setViewMode('workspace');
    setTimeout(() => {
      hydratedSchemaRef.current = true;
    }, 0);
  }, []);

  const persistActiveSchema = useCallback(async () => {
    if (!activeProject) return;
    setSavingSchema(true);
    try {
      const saved = await mmaClient.saveProjectSchema(activeProject.id, schemaRef.current);
      setSyncState(saved.syncState);
    } finally {
      setSavingSchema(false);
    }
  }, [activeProject]);

  // Bootstrap session token exchange & optional initialPath auto-open
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const { health: h, queryPath } = await bootstrapSession();
        if (cancelled) return;
        setHealth(h);

        if (queryPath) {
          try {
            const imported = await mmaClient.importProject(queryPath);
            if (!cancelled) {
              await openProjectWorkspace(imported);
              return;
            }
          } catch {
            // Fall through to Solutions Hub if initial path is not a solution
          }
        }

        if (!cancelled) setViewMode('hub');
      } catch {
        if (!cancelled) setViewMode('scratchpad');
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [openProjectWorkspace]);

  // Debounced auto-save when editing a bound MMA project
  useEffect(() => {
    if (viewMode !== 'workspace' || !activeProject || !hydratedSchemaRef.current) return;
    const handle = window.setTimeout(() => {
      void persistActiveSchema();
    }, 800);
    return () => window.clearTimeout(handle);
  }, [schema, viewMode, activeProject, persistActiveSchema]);

  // Sync Tailwind v4 dark class on <html>
  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'dark') {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
    try {
      localStorage.setItem(THEME_KEY, theme);
    } catch {
      // Ignore
    }
  }, [theme]);

  // Commit a schema mutation onto the undo stack
  const commitSchema = useCallback((updater: (prev: SchemaModel) => SchemaModel) => {
    setSchema((prev) => {
      const next = updater(prev);
      if (next === prev) return prev;
      setPast((h) => [...h.slice(-(MAX_HISTORY - 1)), prev]);
      setFuture([]);
      return { ...next, updatedAt: new Date().toISOString() };
    });
  }, []);

  const handleUndo = useCallback(() => {
    setPast((prevPast) => {
      if (prevPast.length === 0) return prevPast;
      const previous = prevPast[prevPast.length - 1];
      setSchema((curr) => {
        setFuture((f) => [curr, ...f.slice(0, MAX_HISTORY - 1)]);
        return previous;
      });
      return prevPast.slice(0, -1);
    });
  }, []);

  const handleRedo = useCallback(() => {
    setFuture((prevFuture) => {
      if (prevFuture.length === 0) return prevFuture;
      const next = prevFuture[0];
      setSchema((curr) => {
        setPast((p) => [...p.slice(-(MAX_HISTORY - 1)), curr]);
        return next;
      });
      return prevFuture.slice(1);
    });
  }, []);

  // Engine Switching (converts data types, defaults, table schemas, and referential actions)
  const handleEngineChange = useCallback(
    (nextEngine: DatabaseEngine) => {
      if (!isDatabaseEngineSupported(nextEngine)) return;
      commitSchema((prev) => convertSchemaToEngine(prev, nextEngine));
    },
    [commitSchema]
  );

  // Table Actions
  const handleAddTable = useCallback(() => {
    const el = canvasContainerRef.current;
    const centerScreenX = el ? el.clientWidth / 2 : 520;
    const centerScreenY = el ? el.clientHeight / 2 : 340;
    const worldX = Math.round((centerScreenX - viewport.x) / viewport.zoom - TABLE_WIDTH / 2);
    const worldY = Math.round((centerScreenY - viewport.y) / viewport.zoom - 90);

    const engineProfile = ENGINE_PROFILES[resolveSupportedDatabaseEngine(schema.engine)];
    const suffix = schema.tables.length + 1;
    const newTableId = `tbl-${Date.now()}`;
    const newIdColId = `col-${Date.now()}-id`;
    const newCreatedColId = `col-${Date.now()}-created`;
    const color =
      TABLE_ACCENT_PALETTE[schema.tables.length % TABLE_ACCENT_PALETTE.length].id;

    // Offset slightly if another table sits at the exact spot
    const offset = (schema.tables.length % 5) * 24;

    const newTable: Table = {
      id: newTableId,
      name: `table_${suffix}`,
      schema: engineProfile.defaultSchema,
      storageEngine: engineProfile.supportsStorageEngine ? 'InnoDB' : undefined,
      charset: engineProfile.supportsStorageEngine ? 'utf8mb4' : undefined,
      codeGen: normalizeTableCodeGen(),
      color,
      comment: '',
      x: Math.max(20, worldX + offset),
      y: Math.max(20, worldY + offset),
      columns: [
        {
          id: newIdColId,
          name: 'id',
          type: engineProfile.defaultIdType,
          isPrimaryKey: true,
          isNullable: false,
          isUnique: true,
          isAutoIncrement: engineProfile.defaultIdAutoIncrement,
          defaultValue: engineProfile.defaultIdDefault,
          checkConstraint: '',
          comment: '',
        },
        {
          id: newCreatedColId,
          name: 'created_at',
          type: engineProfile.defaultTimestampType,
          isPrimaryKey: false,
          isNullable: false,
          isUnique: false,
          isAutoIncrement: false,
          defaultValue: engineProfile.defaultTimestampDefault,
          checkConstraint: '',
          comment: '',
        },
      ],
    };

    commitSchema((prev) => ({
      ...prev,
      tables: [...prev.tables, newTable],
    }));
    setSelection({ type: 'table', tableId: newTableId });
    setShowInspector(true);
  }, [schema.engine, schema.tables.length, viewport, commitSchema]);

  const handleUpdateTable = useCallback(
    (tableId: string, patch: Partial<Table>) => {
      commitSchema((prev) => ({
        ...prev,
        tables: prev.tables.map((t) => (t.id === tableId ? { ...t, ...patch } : t)),
      }));
    },
    [commitSchema]
  );

  // Live 60fps drag update without polluting history stack
  const handleMoveTableLive = useCallback((tableId: string, x: number, y: number) => {
    setSchema((prev) => ({
      ...prev,
      tables: prev.tables.map((t) => (t.id === tableId ? { ...t, x, y } : t)),
    }));
  }, []);

  // Commit final drag movement onto undo stack once pointer releases
  const handleCommitTableMove = useCallback(
    (tableId: string, startX: number, startY: number, endX: number, endY: number) => {
      setSchema((curr) => {
        const snapshotBefore: SchemaModel = {
          ...curr,
          tables: curr.tables.map((t) =>
            t.id === tableId ? { ...t, x: startX, y: startY } : t
          ),
        };
        setPast((h) => [...h.slice(-(MAX_HISTORY - 1)), snapshotBefore]);
        setFuture([]);
        return {
          ...curr,
          updatedAt: new Date().toISOString(),
          tables: curr.tables.map((t) =>
            t.id === tableId ? { ...t, x: endX, y: endY } : t
          ),
        };
      });
    },
    []
  );

  const handleDuplicateTable = useCallback(
    (tableId: string) => {
      const source = schema.tables.find((t) => t.id === tableId);
      if (!source) return;
      const now = Date.now();
      const copyId = `tbl-${now}`;
      const colIdMap = new Map<string, string>();
      const duplicatedCols = source.columns.map((c, idx) => {
        const nextId = `col-${now}-${idx}`;
        colIdMap.set(c.id, nextId);
        return { ...c, id: nextId };
      });
      const duplicated: Table = {
        ...source,
        id: copyId,
        name: `${source.name}_copy`,
        x: source.x + 40,
        y: source.y + 40,
        columns: duplicatedCols,
        indexes: (source.indexes || []).map((ix, idx) => ({
          ...ix,
          id: `idx-${now}-${idx}`,
          name: `${ix.name}_copy`,
          columns: ix.columns.map((cid) => colIdMap.get(cid) ?? cid),
        })),
      };
      commitSchema((prev) => ({
        ...prev,
        tables: [...prev.tables, duplicated],
      }));
      setSelection({ type: 'table', tableId: copyId });
    },
    [schema.tables, commitSchema]
  );

  const handleDeleteTable = useCallback(
    (tableId: string) => {
      commitSchema((prev) => ({
        ...prev,
        tables: prev.tables.filter((t) => t.id !== tableId),
        relations: prev.relations.filter(
          (r) => r.sourceTableId !== tableId && r.targetTableId !== tableId
        ),
      }));
      setSelection((sel) =>
        (sel.type === 'table' || sel.type === 'column') && sel.tableId === tableId
          ? { type: 'none' }
          : sel
      );
    },
    [commitSchema]
  );

  // Column Actions
  const handleAddColumn = useCallback(
    (tableId: string) => {
      const targetTable = schema.tables.find((t) => t.id === tableId);
      if (!targetTable) return;
      const engineProfile = ENGINE_PROFILES[schema.engine || 'postgresql'];
      const colId = `col-${Date.now()}`;
      const newCol: Column = {
        id: colId,
        name: `column_${targetTable.columns.length + 1}`,
        type: engineProfile.defaultStringType,
        isPrimaryKey: false,
        isNullable: true,
        isUnique: false,
        isAutoIncrement: false,
        defaultValue: '',
        checkConstraint: '',
        comment: '',
      };

      commitSchema((prev) => ({
        ...prev,
        tables: prev.tables.map((t) =>
          t.id === tableId ? { ...t, columns: [...t.columns, newCol] } : t
        ),
      }));
      setSelection({ type: 'column', tableId, columnId: colId });
      setShowInspector(true);
    },
    [schema.engine, schema.tables, commitSchema]
  );

  const handleUpdateColumn = useCallback(
    (tableId: string, columnId: string, patch: Partial<Column>) => {
      commitSchema((prev) => ({
        ...prev,
        tables: prev.tables.map((t) =>
          t.id === tableId
            ? {
                ...t,
                columns: t.columns.map((c) =>
                  c.id === columnId ? { ...c, ...patch } : c
                ),
              }
            : t
        ),
      }));
    },
    [commitSchema]
  );

  const handleDeleteColumn = useCallback(
    (tableId: string, columnId: string) => {
      commitSchema((prev) => ({
        ...prev,
        tables: prev.tables.map((t) =>
          t.id === tableId
            ? {
                ...t,
                columns: t.columns.filter((c) => c.id !== columnId),
                indexes: t.indexes?.map((ix) => ({
                  ...ix,
                  columns: ix.columns.filter((cid) => cid !== columnId),
                })),
              }
            : t
        ),
        relations: prev.relations.filter(
          (r) =>
            !(r.sourceTableId === tableId && r.sourceColumnId === columnId) &&
            !(r.targetTableId === tableId && r.targetColumnId === columnId)
        ),
      }));
      setSelection((sel) =>
        sel.type === 'column' && sel.columnId === columnId
          ? { type: 'table', tableId }
          : sel
      );
    },
    [commitSchema]
  );

  const handleMoveColumn = useCallback(
    (tableId: string, columnId: string, direction: 'up' | 'down') => {
      commitSchema((prev) => ({
        ...prev,
        tables: prev.tables.map((t) => {
          if (t.id !== tableId) return t;
          const idx = t.columns.findIndex((c) => c.id === columnId);
          if (idx === -1) return t;
          const swapIdx = direction === 'up' ? idx - 1 : idx + 1;
          if (swapIdx < 0 || swapIdx >= t.columns.length) return t;
          const copy = [...t.columns];
          const [moved] = copy.splice(idx, 1);
          copy.splice(swapIdx, 0, moved);
          return { ...t, columns: copy };
        }),
      }));
    },
    [commitSchema]
  );

  // Relation Actions
  const handleCreateRelation = useCallback(
    (
      sourceTableId: string,
      sourceColumnId: string,
      targetTableId: string,
      targetColumnId: string,
      cardinality: Cardinality = '1:N'
    ) => {
      if (sourceTableId === targetTableId && sourceColumnId === targetColumnId) {
        return;
      }

      // Avoid exact duplicate relation
      const duplicate = schema.relations.find(
        (r) =>
          (r.sourceTableId === sourceTableId &&
            r.sourceColumnId === sourceColumnId &&
            r.targetTableId === targetTableId &&
            r.targetColumnId === targetColumnId) ||
          (r.sourceTableId === targetTableId &&
            r.sourceColumnId === targetColumnId &&
            r.targetTableId === sourceTableId &&
            r.targetColumnId === sourceColumnId)
      );
      if (duplicate) {
        setSelection({ type: 'relation', relationId: duplicate.id });
        setShowInspector(true);
        return;
      }

      const srcTable = schema.tables.find((t) => t.id === sourceTableId);
      const tgtTable = schema.tables.find((t) => t.id === targetTableId);
      const tgtCol = tgtTable?.columns.find((c) => c.id === targetColumnId);

      const newRelId = `rel-${Date.now()}`;
      const newRelation: Relation = {
        id: newRelId,
        name: `fk_${tgtTable?.name ?? 'target'}_${tgtCol?.name ?? srcTable?.name ?? 'ref'}`,
        sourceTableId,
        sourceColumnId,
        targetTableId,
        targetColumnId,
        cardinality,
        onDelete: 'CASCADE',
        onUpdate: 'CASCADE',
      };

      commitSchema((prev) => ({
        ...prev,
        relations: [...prev.relations, newRelation],
      }));
      setSelection({ type: 'relation', relationId: newRelId });
      setShowInspector(true);
    },
    [schema.relations, schema.tables, commitSchema]
  );

  const handleUpdateRelation = useCallback(
    (relationId: string, patch: Partial<Relation>) => {
      commitSchema((prev) => ({
        ...prev,
        relations: prev.relations.map((r) =>
          r.id === relationId ? { ...r, ...patch } : r
        ),
      }));
    },
    [commitSchema]
  );

  const handleDeleteRelation = useCallback(
    (relationId: string) => {
      commitSchema((prev) => ({
        ...prev,
        relations: prev.relations.filter((r) => r.id !== relationId),
      }));
      setSelection((sel) =>
        sel.type === 'relation' && sel.relationId === relationId
          ? { type: 'none' }
          : sel
      );
    },
    [commitSchema]
  );

  // Auto-Layout
  const handleAutoLayout = useCallback(() => {
    commitSchema((prev) => ({
      ...prev,
      tables: computeAutoLayout(prev.tables, prev.relations),
    }));
    setViewport({ x: 36, y: 28, zoom: 0.94 });
  }, [commitSchema]);

  // Focus & Pan smoothly to a specific table (used by Outline drawer)
  const handleFocusTable = useCallback(
    (tableId: string) => {
      const table = schema.tables.find((t) => t.id === tableId);
      if (!table) return;
      setSelection({ type: 'table', tableId });
      setShowInspector(true);

      const el = canvasContainerRef.current;
      if (!el) return;
      const centerX = table.x + TABLE_WIDTH / 2;
      const centerY = table.y + getTableHeight(table) / 2;
      setViewport((prev) => ({
        ...prev,
        x: el.clientWidth / 2 - centerX * prev.zoom,
        y: el.clientHeight / 2 - centerY * prev.zoom,
      }));
    },
    [schema.tables]
  );

  // Focus & Pan smoothly to a specific relation's midpoint (used by Outline drawer)
  const handleFocusRelation = useCallback(
    (relationId: string) => {
      const rel = schema.relations.find((r) => r.id === relationId);
      setSelection({ type: 'relation', relationId });
      setShowInspector(true);
      if (!rel) return;

      const src = schema.tables.find((t) => t.id === rel.sourceTableId);
      const tgt = schema.tables.find((t) => t.id === rel.targetTableId);
      const el = canvasContainerRef.current;
      if (!src || !tgt || !el) return;

      const midX = (src.x + tgt.x + TABLE_WIDTH) / 2;
      const midY = (src.y + getTableHeight(src) / 2 + tgt.y + getTableHeight(tgt) / 2) / 2;
      setViewport((prev) => ({
        ...prev,
        x: el.clientWidth / 2 - midX * prev.zoom,
        y: el.clientHeight / 2 - midY * prev.zoom,
      }));
    },
    [schema.relations, schema.tables]
  );

  // Focus & pan directly to the element referenced by a validation issue
  const handleFocusValidationIssue = useCallback(
    (issue: ValidationIssue) => {
      if (issue.relationId && !issue.columnId) {
        handleFocusRelation(issue.relationId);
        return;
      }
      if (issue.tableId) {
        handleFocusTable(issue.tableId);
        if (issue.columnId) {
          setSelection({
            type: 'column',
            tableId: issue.tableId,
            columnId: issue.columnId,
          });
        }
        return;
      }
      if (issue.relationId) {
        handleFocusRelation(issue.relationId);
      }
    },
    [handleFocusRelation, handleFocusTable]
  );

  const handleSelectTemplate = useCallback(
    (template: SchemaTemplate) => {
      const currentEngine = resolveSupportedDatabaseEngine(schema.engine);
      const fresh = convertSchemaToEngine(cloneModel(template.model), currentEngine);
      commitSchema(() => fresh);
      setSelection(
        fresh.tables.length > 0
          ? { type: 'table', tableId: fresh.tables[0].id }
          : { type: 'none' }
      );
      setViewport({ x: 36, y: 28, zoom: 0.94 });
    },
    [schema.engine, commitSchema]
  );

  const handleImportSchema = useCallback(
    (imported: SchemaModel) => {
      const targetEngine = resolveSupportedDatabaseEngine(imported.engine);
      const normalized =
        imported.engine === targetEngine
          ? imported
          : convertSchemaToEngine(imported, targetEngine);
      commitSchema(() => normalized);
      setSelection(
        normalized.tables.length > 0
          ? { type: 'table', tableId: normalized.tables[0].id }
          : { type: 'none' }
      );
      setViewport({ x: 36, y: 28, zoom: 0.94 });
    },
    [commitSchema]
  );

  const handleImportFromDatabase = useCallback(
    (payload: DatabaseImportPayload) => {
      if (payload.mode === 'replace' || schema.tables.length === 0) {
        const laidOutTables = computeAutoLayout(payload.tables, payload.relations);
        const rawModel: SchemaModel = {
          id: `schema-db-${Date.now()}`,
          name: payload.databaseName || 'imported_database',
          engine: payload.engine,
          description: `Imported from ${payload.databaseName}`,
          updatedAt: new Date().toISOString(),
          tables: laidOutTables,
          relations: payload.relations,
        };
        const targetEngine = resolveSupportedDatabaseEngine(payload.engine);
        const nextModel =
          rawModel.engine === targetEngine
            ? rawModel
            : convertSchemaToEngine(rawModel, targetEngine);
        commitSchema(() => nextModel);
        setSelection(
          nextModel.tables.length > 0
            ? { type: 'table', tableId: nextModel.tables[0].id }
            : { type: 'none' }
        );
        setViewport({ x: 36, y: 28, zoom: 0.94 });
        return;
      }

      // Merge mode: match existing tables by (schema, name), update re-selected tables in place,
      // add brand-new tables to the right, and wire all relations between existing and new tables.
      const normalizeKey = (t: Pick<Table, 'schema' | 'name'>) =>
        `${(t.schema || '').trim().toLowerCase()}.${t.name.trim().toLowerCase()}`;

      const currentEngine = resolveSupportedDatabaseEngine(schema.engine);
      const converted = convertSchemaToEngine(
        {
          id: 'temp-import',
          name: payload.databaseName,
          engine: payload.engine,
          description: '',
          updatedAt: new Date().toISOString(),
          tables: payload.tables,
          relations: payload.relations,
        },
        currentEngine
      );

      let firstFocusTableId: string | null = null;

      commitSchema((prev) => {
        const existingByKey = new Map<string, Table>();
        const existingByName = new Map<string, Table[]>();
        for (const t of prev.tables) {
          existingByKey.set(normalizeKey(t), t);
          const lowerName = t.name.trim().toLowerCase();
          const list = existingByName.get(lowerName) || [];
          list.push(t);
          existingByName.set(lowerName, list);
        }

        const findMatchingExisting = (candidate: Pick<Table, 'schema' | 'name'>): Table | undefined => {
          const exact = existingByKey.get(normalizeKey(candidate));
          if (exact) return exact;
          const lowerName = candidate.name.trim().toLowerCase();
          const byName = existingByName.get(lowerName);
          if (byName && byName.length === 1 && (!candidate.schema?.trim() || !byName[0].schema?.trim())) {
            return byName[0];
          }
          return undefined;
        };

        const updatedExistingById = new Map<string, Table>();
        const brandNewTables: Table[] = [];

        for (const importedTable of converted.tables) {
          const existing = findMatchingExisting(importedTable);
          if (existing) {
            const existingColsByName = new Map<string, Column>();
            for (const c of existing.columns) {
              existingColsByName.set(c.name.trim().toLowerCase(), c);
            }
            const mergedColumns: Column[] = importedTable.columns.map((col) => {
              const prevCol = existingColsByName.get(col.name.trim().toLowerCase());
              return prevCol ? { ...col, id: prevCol.id } : col;
            });
            updatedExistingById.set(existing.id, {
              ...importedTable,
              id: existing.id,
              x: existing.x,
              y: existing.y,
              color: existing.color,
              codeGen: existing.codeGen ?? importedTable.codeGen,
              columns: mergedColumns,
            });
          } else {
            brandNewTables.push(importedTable);
          }
        }

        const brandNewIds = new Set(brandNewTables.map((t) => t.id));
        const brandNewRelations = converted.relations.filter(
          (r) => brandNewIds.has(r.sourceTableId) && brandNewIds.has(r.targetTableId)
        );
        const laidOutNew = computeAutoLayout(brandNewTables, brandNewRelations);
        const maxExistingX = prev.tables.reduce(
          (max, t) => Math.max(max, t.x + TABLE_WIDTH),
          0
        );
        const offsetX = maxExistingX > 0 ? maxExistingX + 110 : 0;
        const positionedNew = laidOutNew.map((t) => ({
          ...t,
          x: t.x + offsetX,
        }));

        const mergedTables: Table[] = [
          ...prev.tables.map((t) => updatedExistingById.get(t.id) ?? t),
          ...positionedNew,
        ];

        firstFocusTableId = positionedNew[0]?.id ?? Array.from(updatedExistingById.keys())[0] ?? null;

        // Build lookup from final merged tables by id and normalized (schema, name)
        const finalById = new Map<string, Table>();
        const finalByKey = new Map<string, Table>();
        const finalByName = new Map<string, Table[]>();
        for (const t of mergedTables) {
          finalById.set(t.id, t);
          finalByKey.set(normalizeKey(t), t);
          const lowerName = t.name.trim().toLowerCase();
          const list = finalByName.get(lowerName) || [];
          list.push(t);
          finalByName.set(lowerName, list);
        }

        const findFinalTable = (catTable: Table): Table | undefined => {
          const byId = finalById.get(catTable.id);
          if (byId) return byId;
          const byKey = finalByKey.get(normalizeKey(catTable));
          if (byKey) return byKey;
          const lowerName = catTable.name.trim().toLowerCase();
          const byName = finalByName.get(lowerName);
          if (byName && byName.length === 1 && (!catTable.schema?.trim() || !byName[0].schema?.trim())) {
            return byName[0];
          }
          return undefined;
        };

        // Map every discovered catalog table/column ID to its final table/column ID in mergedTables
        const catalogTables = payload.catalogTables && payload.catalogTables.length > 0
          ? payload.catalogTables
          : converted.tables;
        const tableIdMap = new Map<string, string>();
        const columnIdMap = new Map<string, string>();

        for (const catTable of catalogTables) {
          const finalTable = findFinalTable(catTable);
          if (!finalTable) continue;
          tableIdMap.set(catTable.id, finalTable.id);

          const finalColsById = new Map<string, Column>();
          const finalColsByName = new Map<string, Column>();
          for (const c of finalTable.columns) {
            finalColsById.set(c.id, c);
            finalColsByName.set(c.name.trim().toLowerCase(), c);
          }

          for (const catCol of catTable.columns) {
            const matchCol =
              finalColsById.get(catCol.id) ??
              finalColsByName.get(catCol.name.trim().toLowerCase());
            if (matchCol) {
              columnIdMap.set(`${catTable.id}:${catCol.id}`, matchCol.id);
            }
          }
        }

        const relationSignature = (r: Relation): string => {
          const srcCols = r.sourceColumnIds && r.sourceColumnIds.length > 0
            ? r.sourceColumnIds
            : [r.sourceColumnId];
          const tgtCols = r.targetColumnIds && r.targetColumnIds.length > 0
            ? r.targetColumnIds
            : [r.targetColumnId];
          return `${r.sourceTableId}|${srcCols.join(',')}|${r.targetTableId}|${tgtCols.join(',')}`;
        };

        const existingRelationIds = new Set<string>();
        const existingRelationSigs = new Set<string>();
        const keptExistingRelations: Relation[] = [];

        for (const rel of prev.relations) {
          const srcTable = finalById.get(rel.sourceTableId);
          const tgtTable = finalById.get(rel.targetTableId);
          if (!srcTable || !tgtTable) continue;
          const srcColIds = rel.sourceColumnIds && rel.sourceColumnIds.length > 0
            ? rel.sourceColumnIds
            : [rel.sourceColumnId];
          const tgtColIds = rel.targetColumnIds && rel.targetColumnIds.length > 0
            ? rel.targetColumnIds
            : [rel.targetColumnId];
          const srcValid = srcColIds.every((cid) => srcTable.columns.some((c) => c.id === cid));
          const tgtValid = tgtColIds.every((cid) => tgtTable.columns.some((c) => c.id === cid));
          if (!srcValid || !tgtValid) continue;

          keptExistingRelations.push(rel);
          existingRelationIds.add(rel.id);
          existingRelationSigs.add(relationSignature(rel));
        }

        const addedRelations: Relation[] = [];
        for (const rel of converted.relations) {
          const newSourceTableId = tableIdMap.get(rel.sourceTableId);
          const newTargetTableId = tableIdMap.get(rel.targetTableId);
          if (!newSourceTableId || !newTargetTableId) continue;

          const origSrcCols = rel.sourceColumnIds && rel.sourceColumnIds.length > 0
            ? rel.sourceColumnIds
            : [rel.sourceColumnId];
          const origTgtCols = rel.targetColumnIds && rel.targetColumnIds.length > 0
            ? rel.targetColumnIds
            : [rel.targetColumnId];

          const mappedSrcCols = origSrcCols.map((cid) => columnIdMap.get(`${rel.sourceTableId}:${cid}`));
          const mappedTgtCols = origTgtCols.map((cid) => columnIdMap.get(`${rel.targetTableId}:${cid}`));
          if (mappedSrcCols.some((c) => !c) || mappedTgtCols.some((c) => !c)) continue;

          const finalSrcCols = mappedSrcCols as string[];
          const finalTgtCols = mappedTgtCols as string[];

          let relId = rel.id;
          let suffix = 1;
          while (existingRelationIds.has(relId)) {
            relId = `${rel.id}-${suffix++}`;
          }

          const remapped: Relation = {
            ...rel,
            id: relId,
            sourceTableId: newSourceTableId,
            sourceColumnId: finalSrcCols[0],
            targetTableId: newTargetTableId,
            targetColumnId: finalTgtCols[0],
            sourceColumnIds: finalSrcCols.length > 1 ? finalSrcCols : undefined,
            targetColumnIds: finalTgtCols.length > 1 ? finalTgtCols : undefined,
          };

          const sig = relationSignature(remapped);
          if (existingRelationSigs.has(sig)) continue;

          existingRelationIds.add(remapped.id);
          existingRelationSigs.add(sig);
          addedRelations.push(remapped);
        }

        return {
          ...prev,
          tables: mergedTables,
          relations: [...keptExistingRelations, ...addedRelations],
        };
      });

      if (firstFocusTableId) {
        setSelection({ type: 'table', tableId: firstFocusTableId });
        setShowInspector(true);
      }
    },
    [schema.engine, schema.tables, commitSchema]
  );

  // Global Keyboard Shortcuts (Undo, Redo, Delete selection)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const isEditingInput =
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.tagName === 'SELECT' ||
          target.isContentEditable);

      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setShowOutline(true);
        requestAnimationFrame(() => {
          outlineSearchInputRef.current?.focus();
          outlineSearchInputRef.current?.select();
        });
        return;
      }

      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'z') {
        if (isEditingInput) return;
        e.preventDefault();
        if (e.shiftKey) {
          handleRedo();
        } else {
          handleUndo();
        }
        return;
      }

      const isBlockingModalOpen =
        (exportModalMode !== null && !(exportModalMode === 'sql' && exportLiveWindow)) ||
        importDbModalOpen;

      if (
        (e.key === 'Delete' || e.key === 'Backspace') &&
        !isEditingInput &&
        !isBlockingModalOpen
      ) {
        if (selection.type === 'relation') {
          e.preventDefault();
          handleDeleteRelation(selection.relationId);
        } else if (selection.type === 'table') {
          e.preventDefault();
          handleDeleteTable(selection.tableId);
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [
    selection,
    exportModalMode,
    exportLiveWindow,
    importDbModalOpen,
    handleUndo,
    handleRedo,
    handleDeleteRelation,
    handleDeleteTable,
  ]);

  if (viewMode === 'booting') {
    return (
      <div className="h-screen w-screen flex items-center justify-center bg-slate-50 dark:bg-[#0B1120] text-slate-500 dark:text-slate-400 text-sm">
        Connecting to MMA CLI host...
      </div>
    );
  }

  if (viewMode === 'hub') {
    return (
      <SolutionsHub
        health={health}
        theme={theme}
        onToggleTheme={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
        onOpenProject={(proj) => void openProjectWorkspace(proj)}
        onOpenScratchpad={() => {
          setActiveProject(null);
          setSyncState(null);
          setSchema(createEmptySchemaModel());
          setPast([]);
          setFuture([]);
          setSelection({ type: 'none' });
          setViewMode('scratchpad');
        }}
      />
    );
  }

  return (
    <div className="h-screen w-screen flex flex-col overflow-hidden bg-slate-50 dark:bg-[#0B1120] text-slate-900 dark:text-slate-100">
      <main className="flex-1 min-w-0 min-h-0 flex overflow-hidden">
        {showOutline && (
          <OutlineDrawer
            tables={schema.tables}
            relations={schema.relations}
            selection={selection}
            searchInputRef={outlineSearchInputRef}
            onFocusTable={handleFocusTable}
            onSelectRelation={handleFocusRelation}
            onAddTable={handleAddTable}
            onClose={() => setShowOutline(false)}
          />
        )}

        <div className="relative flex-1 min-w-0 h-full flex">
          <Toolbar
            engine={resolveSupportedDatabaseEngine(schema.engine)}
            onEngineChange={handleEngineChange}
            onAddTable={handleAddTable}
            onAutoLayout={handleAutoLayout}
            onUndo={handleUndo}
            onRedo={handleRedo}
            canUndo={past.length > 0}
            canRedo={future.length > 0}
            snapGrid={snapGrid}
            onToggleSnapGrid={() => setSnapGrid((v) => !v)}
            onSelectTemplate={handleSelectTemplate}
            onOpenImportDatabase={() => setImportDbModalOpen(true)}
            onOpenExportSql={() => {
              setExportLiveWindow(false);
              setExportModalMode('sql');
            }}
            onOpenExportJson={() => {
              setExportLiveWindow(false);
              setExportModalMode('json');
            }}
            onOpenExportImage={() => {
              setExportLiveWindow(false);
              setExportModalMode('image');
            }}
            onOpenImportJson={() => {
              setExportLiveWindow(false);
              setExportModalMode('import');
            }}
            theme={theme}
            onToggleTheme={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
            activeProject={activeProject}
            syncState={syncState}
            saving={savingSchema}
            onBackToHub={() => {
              setActiveProject(null);
              setSyncState(null);
              setViewMode('hub');
            }}
            onSaveSchema={() => void persistActiveSchema()}
            onOpenGenerate={() => setGenerateModalOpen(true)}
          />

          <Canvas
            tables={schema.tables}
            relations={schema.relations}
            selection={selection}
            viewport={viewport}
            snapGrid={snapGrid}
            showOutline={showOutline}
            showInspector={showInspector}
            theme={theme}
            validation={validation}
            entitySyncMap={entitySyncMap}
            onViewportChange={setViewport}
            onSelect={(sel) => {
              setSelection(sel);
              if (sel.type !== 'none') setShowInspector(true);
            }}
            onMoveTableLive={handleMoveTableLive}
            onCommitTableMove={handleCommitTableMove}
            onCreateRelation={handleCreateRelation}
            onAddColumn={handleAddColumn}
            onDuplicateTable={handleDuplicateTable}
            onDeleteTable={handleDeleteTable}
            onAddFirstTable={handleAddTable}
            onLoadStarterTemplate={() => handleSelectTemplate(SCHEMA_TEMPLATES[0])}
            onToggleOutline={() => setShowOutline((v) => !v)}
            onToggleInspector={() => setShowInspector((v) => !v)}
            canvasContainerRef={canvasContainerRef}
          />
        </div>

        {showInspector && (
          <InspectorPanel
            schema={schema}
            selection={selection}
            onSelect={setSelection}
            onEngineChange={handleEngineChange}
            onUpdateSchemaMeta={(patch) =>
              commitSchema((prev) => ({ ...prev, ...patch }))
            }
            onUpdateTable={handleUpdateTable}
            onDuplicateTable={handleDuplicateTable}
            onDeleteTable={handleDeleteTable}
            onAddColumn={handleAddColumn}
            onUpdateColumn={handleUpdateColumn}
            onDeleteColumn={handleDeleteColumn}
            onMoveColumn={handleMoveColumn}
            onCreateRelation={handleCreateRelation}
            onUpdateRelation={handleUpdateRelation}
            onDeleteRelation={handleDeleteRelation}
            onClose={() => setShowInspector(false)}
          />
        )}
      </main>

      <StatusBar
        engine={resolveSupportedDatabaseEngine(schema.engine)}
        tableCount={schema.tables.length}
        columnCount={totalColumns}
        relationCount={schema.relations.length}
        indexCount={totalIndexes}
        validation={validation}
        isLiveSqlOpen={exportModalMode === 'sql' && exportLiveWindow}
        onToggleLiveSql={() => {
          if (exportModalMode === 'sql' && exportLiveWindow) {
            setExportLiveWindow(false);
            setExportModalMode(null);
          } else {
            setExportModalMode('sql');
            setExportLiveWindow(true);
          }
        }}
        onFocusIssue={handleFocusValidationIssue}
        onMakeColumnNullable={(tableId, columnId) =>
          handleUpdateColumn(tableId, columnId, { isNullable: true })
        }
      />

      <ExportModal
        mode={exportModalMode}
        schema={schema}
        selection={selection}
        workspaceTheme={theme}
        showInspector={showInspector}
        isLiveWindow={exportLiveWindow}
        onToggleLiveWindow={setExportLiveWindow}
        onModeChange={(nextMode) => {
          if (nextMode !== 'sql') setExportLiveWindow(false);
          setExportModalMode(nextMode);
        }}
        onImportSchema={handleImportSchema}
        onClose={() => {
          setExportLiveWindow(false);
          setExportModalMode(null);
        }}
      />

      <ImportDatabaseModal
        open={importDbModalOpen}
        existingTables={schema.tables}
        isMmaProject={activeProject !== null}
        onImport={handleImportFromDatabase}
        onClose={() => setImportDbModalOpen(false)}
      />

      {activeProject && (
        <GenerateModal
          open={generateModalOpen}
          project={activeProject}
          onBeforePreview={persistActiveSchema}
          onApplied={(nextSync) => setSyncState(nextSync)}
          onClose={() => setGenerateModalOpen(false)}
        />
      )}
    </div>
  );
}
