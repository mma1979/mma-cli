import React, { useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  ArrowLeft,
  ChevronDown,
  ChevronRight,
  Database,
  FileCode2,
  GitFork,
  KeyRound,
  Link2,
  Loader2,
  Search,
  Server,
  ShieldCheck,
  X,
} from 'lucide-react';
import {
  DEFAULT_LIVE_DATABASE_ENGINE,
  isLiveDatabaseEngineSupported,
  LiveDatabaseEngine,
  SUPPORTED_LIVE_DATABASE_ENGINES,
} from '../config/featureFlags';
import { ENGINE_PROFILES } from '../data/engines';
import {
  DatabaseEngine,
  normalizeTableCodeGen,
  Relation,
  Table,
} from '../types/schema';
import { parseSqlDumpToSchema } from '../utils/sqlParser';

type LiveEngine = LiveDatabaseEngine;

export interface DatabaseImportPayload {
  databaseName: string;
  engine: DatabaseEngine;
  tables: Table[];
  relations: Relation[];
  catalogTables?: Table[];
  mode: 'merge' | 'replace';
}

interface ImportDatabaseModalProps {
  open: boolean;
  existingTables: Table[];
  isMmaProject?: boolean;
  onImport: (payload: DatabaseImportPayload) => void;
  onClose: () => void;
}

function normalizeTableKey(table: Pick<Table, 'schema' | 'name'>): string {
  const s = (table.schema || '').trim().toLowerCase();
  const n = table.name.trim().toLowerCase();
  return `${s}.${n}`;
}

const DEFAULT_PORTS: Record<LiveEngine, string> = {
  postgresql: '5432',
  mysql: '3306',
  mssql: '1433',
};

const DEFAULT_SCHEMAS: Record<LiveEngine, string> = {
  postgresql: 'public',
  mysql: '',
  mssql: 'dbo',
};

export interface DatabaseIntrospectionWarning {
  code: string;
  message: string;
}

interface DiscoveredDatabase {
  databaseName: string;
  engine: DatabaseEngine;
  tables: Table[];
  relations: Relation[];
  warnings: DatabaseIntrospectionWarning[];
}

export const ImportDatabaseModal: React.FC<ImportDatabaseModalProps> = ({
  open,
  existingTables,
  isMmaProject = false,
  onImport,
  onClose,
}) => {
  const hasExistingTables = existingTables.length > 0;
  const [step, setStep] = useState<'connect' | 'select'>('connect');
  const [sourceTab, setSourceTab] = useState<'live' | 'sql'>('live');

  // Live connection form state
  const [liveEngine, setLiveEngine] = useState<LiveEngine>(DEFAULT_LIVE_DATABASE_ENGINE);
  const [connectionUrl, setConnectionUrl] = useState('');
  const [host, setHost] = useState('');
  const [port, setPort] = useState(DEFAULT_PORTS[DEFAULT_LIVE_DATABASE_ENGINE]);
  const [database, setDatabase] = useState('');
  const [schemaFilter, setSchemaFilter] = useState(
    DEFAULT_SCHEMAS[DEFAULT_LIVE_DATABASE_ENGINE]
  );
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [ssl, setSsl] = useState(true);
  const [excludeMmaSystemTables, setExcludeMmaSystemTables] = useState(isMmaProject);
  const [connecting, setConnecting] = useState(false);

  // SQL dump form state
  const [sqlText, setSqlText] = useState('');
  const [sqlFileName, setSqlFileName] = useState('');
  const sqlFileRef = useRef<HTMLInputElement>(null);

  const [error, setError] = useState<string | null>(null);

  // Step 2: Discovered tables & selection state
  const [discovered, setDiscovered] = useState<DiscoveredDatabase | null>(null);
  const [selectedTableIds, setSelectedTableIds] = useState<Set<string>>(new Set());
  const [expandedTableId, setExpandedTableId] = useState<string | null>(null);
  const [tableQuery, setTableQuery] = useState('');
  const [importMode, setImportMode] = useState<'merge' | 'replace'>(
    hasExistingTables ? 'merge' : 'replace'
  );

  const fkCountByTable = useMemo(() => {
    const counts = new Map<string, number>();
    if (!discovered) return counts;
    for (const r of discovered.relations) {
      counts.set(r.targetTableId, (counts.get(r.targetTableId) || 0) + 1);
    }
    return counts;
  }, [discovered]);

  const filteredTables = useMemo(() => {
    if (!discovered) return [];
    const q = tableQuery.trim().toLowerCase();
    if (!q) return discovered.tables;
    return discovered.tables.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        t.schema.toLowerCase().includes(q) ||
        t.columns.some((c) => c.name.toLowerCase().includes(q))
    );
  }, [discovered, tableQuery]);

  const existingTableKeySet = useMemo(() => {
    const set = new Set<string>();
    for (const t of existingTables) {
      set.add(normalizeTableKey(t));
    }
    return set;
  }, [existingTables]);

  const existingTableNameCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const t of existingTables) {
      const name = t.name.trim().toLowerCase();
      counts.set(name, (counts.get(name) || 0) + 1);
    }
    return counts;
  }, [existingTables]);

  const isTableOnCanvas = (table: Table): boolean => {
    if (existingTableKeySet.has(normalizeTableKey(table))) return true;
    const name = table.name.trim().toLowerCase();
    if (!table.schema?.trim() && existingTableNameCounts.get(name) === 1) return true;
    return false;
  };

  const discoveredExistingIds = useMemo(() => {
    const ids = new Set<string>();
    if (!discovered) return ids;
    for (const t of discovered.tables) {
      if (isTableOnCanvas(t)) {
        ids.add(t.id);
      }
    }
    return ids;
  }, [discovered, existingTableKeySet, existingTableNameCounts]);

  const selectedRelationsCount = useMemo(() => {
    if (!discovered) return 0;
    if (importMode === 'merge' && hasExistingTables) {
      const effectiveIds = new Set<string>([...selectedTableIds, ...discoveredExistingIds]);
      return discovered.relations.filter(
        (r) => effectiveIds.has(r.sourceTableId) && effectiveIds.has(r.targetTableId)
      ).length;
    }
    return discovered.relations.filter(
      (r) => selectedTableIds.has(r.sourceTableId) && selectedTableIds.has(r.targetTableId)
    ).length;
  }, [discovered, selectedTableIds, discoveredExistingIds, importMode, hasExistingTables]);

  if (!open) return null;

  const handleEngineSwitch = (next: LiveEngine) => {
    if (!isLiveDatabaseEngineSupported(next)) return;
    setLiveEngine(next);
    setPort(DEFAULT_PORTS[next]);
    setSchemaFilter(DEFAULT_SCHEMAS[next]);
    setError(null);
  };

  const handleParseConnectionUrl = (raw: string) => {
    setConnectionUrl(raw);
    const trimmed = raw.trim();
    if (!trimmed) return;
    try {
      const normalized = trimmed.replace(/^postgres:\/\//i, 'postgresql://');
      const parsed = new URL(normalized);
      const protocol = parsed.protocol.replace(':', '').toLowerCase();

      let parsedEngine: LiveEngine | null = null;
      if (protocol === 'postgresql') {
        parsedEngine = 'postgresql';
      } else if (protocol === 'mysql') {
        parsedEngine = 'mysql';
      } else if (protocol === 'mssql' || protocol === 'sqlserver') {
        parsedEngine = 'mssql';
      }

      if (parsedEngine) {
        if (!isLiveDatabaseEngineSupported(parsedEngine)) {
          const supportedLabels = SUPPORTED_LIVE_DATABASE_ENGINES.map(
            (e) => ENGINE_PROFILES[e].label
          ).join(', ');
          setError(
            `${ENGINE_PROFILES[parsedEngine].label} is currently disabled. Supported database types: ${supportedLabels}.`
          );
          return;
        }
        setLiveEngine(parsedEngine);
        setPort(parsed.port || DEFAULT_PORTS[parsedEngine]);
        setSchemaFilter(DEFAULT_SCHEMAS[parsedEngine]);
      }

      if (parsed.hostname) setHost(parsed.hostname);
      if (parsed.pathname && parsed.pathname.length > 1) {
        setDatabase(decodeURIComponent(parsed.pathname.slice(1)));
      }
      if (parsed.username) setUsername(decodeURIComponent(parsed.username));
      if (parsed.password) setPassword(decodeURIComponent(parsed.password));
      const sslMode = parsed.searchParams.get('sslmode') || parsed.searchParams.get('encrypt');
      if (sslMode === 'disable' || sslMode === 'false') setSsl(false);
      else if (sslMode) setSsl(true);
      setError(null);
    } catch {
      // Wait until user finishes typing a valid URL
    }
  };

  const clearTransientSecrets = () => {
    setPassword('');
    setConnectionUrl('');
  };

  const handleDismiss = () => {
    clearTransientSecrets();
    setStep('connect');
    setError(null);
    onClose();
  };

  const handleBackToConnect = () => {
    clearTransientSecrets();
    setStep('connect');
    setError(null);
  };

  const enterTableSelection = (data: DiscoveredDatabase) => {
    setDiscovered(data);
    if (hasExistingTables) {
      const newIds = data.tables
        .filter((t) => !isTableOnCanvas(t))
        .map((t) => t.id);
      setSelectedTableIds(
        new Set(newIds.length > 0 ? newIds : data.tables.map((t) => t.id))
      );
    } else {
      setSelectedTableIds(new Set(data.tables.map((t) => t.id)));
    }
    setExpandedTableId(null);
    setTableQuery('');
    setImportMode(hasExistingTables ? 'merge' : 'replace');
    setError(null);
    setStep('select');
  };

  const handleConnectLive = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isLiveDatabaseEngineSupported(liveEngine)) {
      const supportedLabels = SUPPORTED_LIVE_DATABASE_ENGINES.map(
        (eng) => ENGINE_PROFILES[eng].label
      ).join(', ');
      setError(
        `${ENGINE_PROFILES[liveEngine].label} is currently disabled. Supported database types: ${supportedLabels}.`
      );
      return;
    }
    setError(null);
    setConnecting(true);
    try {
      const response = await fetch('/api/db/introspect', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          engine: liveEngine,
          host: host.trim(),
          port: Number(port),
          database: database.trim(),
          username: username.trim(),
          password,
          schemaFilter: schemaFilter.trim(),
          ssl,
          excludeMmaSystemTables,
        }),
      });
      const data = await response.json();
      if (!response.ok) {
        setError(data?.error || 'Could not connect to database.');
        return;
      }
      const tables: Table[] = Array.isArray(data.tables)
        ? data.tables.map((t: Table) => ({
            ...t,
            codeGen: normalizeTableCodeGen(t.codeGen),
          }))
        : [];
      const warnings: DatabaseIntrospectionWarning[] = Array.isArray(data.warnings)
        ? data.warnings.filter(
            (w: unknown): w is DatabaseIntrospectionWarning =>
              Boolean(w) &&
              typeof (w as DatabaseIntrospectionWarning).code === 'string' &&
              typeof (w as DatabaseIntrospectionWarning).message === 'string'
          )
        : [];
      enterTableSelection({
        databaseName: data.database || database.trim(),
        engine: data.engine || liveEngine,
        tables,
        relations: Array.isArray(data.relations) ? data.relations : [],
        warnings,
      });
    } catch {
      setError('Network error reaching the introspection server. Please check your connection and try again.');
    } finally {
      clearTransientSecrets();
      setConnecting(false);
    }
  };

  const handleSqlFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setSqlFileName(file.name.replace(/\.sql$/i, ''));
    const reader = new FileReader();
    reader.onload = () => {
      setSqlText(String(reader.result ?? ''));
      setError(null);
    };
    reader.readAsText(file);
  };

  const handleAnalyzeSqlDump = () => {
    setError(null);
    const { result, error: parseErr } = parseSqlDumpToSchema(
      sqlText,
      sqlFileName || 'imported_database'
    );
    if (parseErr || !result) {
      setError(parseErr || 'Failed to parse SQL DDL.');
      return;
    }
    enterTableSelection({
      ...result,
      warnings: [],
    });
  };

  const toggleTableSelection = (id: string) => {
    setSelectedTableIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleIncludeRelatedTables = () => {
    if (!discovered) return;
    setSelectedTableIds((prev) => {
      const next = new Set(prev);
      let changed = true;
      while (changed) {
        changed = false;
        for (const r of discovered.relations) {
          if (next.has(r.sourceTableId) && !next.has(r.targetTableId)) {
            next.add(r.targetTableId);
            changed = true;
          } else if (next.has(r.targetTableId) && !next.has(r.sourceTableId)) {
            next.add(r.sourceTableId);
            changed = true;
          }
        }
      }
      return next;
    });
  };

  const handleConfirmImport = () => {
    if (!discovered || selectedTableIds.size === 0) return;
    const chosenTables = discovered.tables.filter((t) => selectedTableIds.has(t.id));
    const effectiveRelationTableIds =
      importMode === 'merge' && hasExistingTables
        ? new Set<string>([...selectedTableIds, ...discoveredExistingIds])
        : selectedTableIds;
    const chosenRelations = discovered.relations.filter(
      (r) =>
        effectiveRelationTableIds.has(r.sourceTableId) &&
        effectiveRelationTableIds.has(r.targetTableId)
    );

    onImport({
      databaseName: discovered.databaseName,
      engine: discovered.engine,
      tables: chosenTables,
      relations: chosenRelations,
      catalogTables: discovered.tables,
      mode: importMode,
    });
    handleDismiss();
  };

  const inputClass =
    'w-full px-2.5 py-2 text-xs bg-slate-50 dark:bg-slate-900/90 border border-slate-200 dark:border-slate-700 rounded-md text-slate-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:border-blue-500 transition-colors';
  const labelClass = 'block text-[11px] font-medium text-slate-600 dark:text-slate-400 mb-1';

  return (
    <div
      onClick={handleDismiss}
      className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-[2px] flex items-center justify-center p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-2xl max-h-[88vh] bg-white dark:bg-[#0F172A] border border-slate-200 dark:border-slate-700 rounded-xl shadow-2xl flex flex-col overflow-hidden"
      >
        {/* Header */}
        <div className="px-5 py-3.5 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between gap-4">
          <div className="flex items-center gap-2.5 min-w-0">
            {step === 'select' && (
              <button
                type="button"
                onClick={handleBackToConnect}
                title="Back to connection"
                aria-label="Back to connection"
                className="p-1 text-slate-500 hover:text-slate-900 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
              >
                <ArrowLeft className="w-4 h-4" />
              </button>
            )}
            <Database className="w-4 h-4 text-blue-600 dark:text-blue-400 shrink-0" />
            <div className="min-w-0">
              <h2 className="text-sm font-semibold text-slate-900 dark:text-slate-100 truncate">
                {step === 'connect'
                  ? 'Import from Existing Database'
                  : `Select Tables from ${discovered?.databaseName}`}
              </h2>
            </div>
          </div>

          <button
            type="button"
            onClick={handleDismiss}
            aria-label="Close modal"
            className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* ================= STEP 1: CONNECT OR SQL DUMP ================= */}
        {step === 'connect' && (
          <>
            {/* Source Tabs */}
            <div className="px-5 pt-4">
              <div className="grid grid-cols-2 gap-1 p-1 bg-slate-100 dark:bg-slate-900 rounded-lg">
                <button
                  type="button"
                  onClick={() => {
                    setSourceTab('live');
                    setError(null);
                  }}
                  className={`flex items-center justify-center gap-1.5 py-1.5 px-3 text-xs font-medium rounded-md transition-colors cursor-pointer whitespace-nowrap ${
                    sourceTab === 'live'
                      ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  <Server className="w-3.5 h-3.5" />
                  <span>Live Database Connection</span>
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setSourceTab('sql');
                    setError(null);
                  }}
                  className={`flex items-center justify-center gap-1.5 py-1.5 px-3 text-xs font-medium rounded-md transition-colors cursor-pointer whitespace-nowrap ${
                    sourceTab === 'sql'
                      ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  <FileCode2 className="w-3.5 h-3.5" />
                  <span>SQL Dump / DDL Script</span>
                </button>
              </div>
            </div>

            {sourceTab === 'live' ? (
              <form
                onSubmit={handleConnectLive}
                className="flex-1 overflow-y-auto studio-scroll flex flex-col"
              >
                <div className="p-5 space-y-4 flex-1">
                  {/* Engine Selector */}
                  <div>
                    <label className={labelClass}>Database Engine</label>
                    <div
                      className={`grid ${
                        SUPPORTED_LIVE_DATABASE_ENGINES.length === 1
                          ? 'grid-cols-1'
                          : SUPPORTED_LIVE_DATABASE_ENGINES.length === 2
                          ? 'grid-cols-2'
                          : 'grid-cols-3'
                      } gap-1.5`}
                    >
                      {SUPPORTED_LIVE_DATABASE_ENGINES.map((eng) => (
                        <button
                          key={eng}
                          type="button"
                          onClick={() => handleEngineSwitch(eng)}
                          className={`py-2 px-3 text-xs font-medium rounded-md border transition-colors cursor-pointer ${
                            liveEngine === eng
                              ? 'bg-blue-50 dark:bg-blue-950/60 border-blue-600 text-blue-700 dark:text-blue-300'
                              : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:border-slate-300 dark:hover:border-slate-600'
                          }`}
                        >
                          {ENGINE_PROFILES[eng].label}
                        </button>
                      ))}
                    </div>
                  </div>

                  {/* Quick Connection URL Paste */}
                  <div>
                    <label className={labelClass}>
                      Connection URI <span className="text-slate-400 font-normal">(optional auto-fill)</span>
                    </label>
                    <input
                      type="text"
                      value={connectionUrl}
                      onChange={(e) => handleParseConnectionUrl(e.target.value)}
                      placeholder={
                        liveEngine === 'postgresql'
                          ? 'postgresql://user:password@db.example.com:5432/mydb?sslmode=require'
                          : liveEngine === 'mysql'
                          ? 'mysql://user:password@db.example.com:3306/mydb'
                          : 'mssql://user:password@db.example.com:1433/mydb'
                      }
                      spellCheck={false}
                      className={`${inputClass} font-mono`}
                    />
                  </div>

                  {/* Host & Port */}
                  <div className="grid grid-cols-4 gap-3">
                    <div className="col-span-3">
                      <label className={labelClass}>Host</label>
                      <input
                        type="text"
                        required
                        value={host}
                        onChange={(e) => setHost(e.target.value)}
                        placeholder="db.your-domain.com"
                        spellCheck={false}
                        className={`${inputClass} font-mono`}
                      />
                    </div>
                    <div>
                      <label className={labelClass}>Port</label>
                      <input
                        type="number"
                        required
                        min={1}
                        max={65535}
                        value={port}
                        onChange={(e) => setPort(e.target.value)}
                        className={`${inputClass} font-mono tabular-nums`}
                      />
                    </div>
                  </div>

                  {/* Database & Optional Schema */}
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className={labelClass}>Database Name</label>
                      <input
                        type="text"
                        required
                        value={database}
                        onChange={(e) => setDatabase(e.target.value)}
                        placeholder="production_db"
                        spellCheck={false}
                        className={`${inputClass} font-mono`}
                      />
                    </div>
                    <div>
                      <label className={labelClass}>
                        Schema Filter{' '}
                        <span className="text-slate-400 font-normal">
                          ({liveEngine === 'mysql' ? 'not used in MySQL' : 'blank for all'})
                        </span>
                      </label>
                      <input
                        type="text"
                        disabled={liveEngine === 'mysql'}
                        value={schemaFilter}
                        onChange={(e) => setSchemaFilter(e.target.value)}
                        placeholder={liveEngine === 'mssql' ? 'dbo' : 'public'}
                        spellCheck={false}
                        className={`${inputClass} font-mono disabled:opacity-50`}
                      />
                    </div>
                  </div>

                  {/* Credentials */}
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className={labelClass}>Username</label>
                      <input
                        type="text"
                        required
                        value={username}
                        onChange={(e) => setUsername(e.target.value)}
                        placeholder={liveEngine === 'mssql' ? 'sa' : 'readonly_user'}
                        spellCheck={false}
                        autoComplete="username"
                        className={`${inputClass} font-mono`}
                      />
                    </div>
                    <div>
                      <label className={labelClass}>Password</label>
                      <input
                        type="password"
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        placeholder="••••••••"
                        autoComplete="current-password"
                        className={`${inputClass} font-mono`}
                      />
                    </div>
                  </div>

                  <div className="flex flex-wrap items-center justify-between gap-3 pt-1">
                    <div className="flex flex-wrap items-center gap-4">
                      <label className="flex items-center gap-2 text-xs text-slate-700 dark:text-slate-300 cursor-pointer select-none">
                        <input
                          type="checkbox"
                          checked={ssl}
                          onChange={(e) => setSsl(e.target.checked)}
                          className="rounded border-slate-300 dark:border-slate-600 text-blue-600 focus:ring-blue-500"
                        />
                        <span>Use SSL / TLS encryption</span>
                      </label>

                      <label className="flex items-center gap-2 text-xs text-slate-700 dark:text-slate-300 cursor-pointer select-none">
                        <input
                          type="checkbox"
                          checked={excludeMmaSystemTables}
                          onChange={(e) => setExcludeMmaSystemTables(e.target.checked)}
                          className="rounded border-slate-300 dark:border-slate-600 text-blue-600 focus:ring-blue-500"
                        />
                        <span>Exclude MMA system tables &amp; audit columns</span>
                      </label>
                    </div>

                    <div className="flex items-center gap-1.5 text-[11px] text-slate-500 dark:text-slate-400">
                      <ShieldCheck className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400 shrink-0" />
                      <span>Read-only inspection · credentials never stored</span>
                    </div>
                  </div>

                  {error && (
                    <div className="px-3 py-2.5 text-xs text-rose-700 dark:text-rose-300 bg-rose-50 dark:bg-rose-950/50 border border-rose-200 dark:border-rose-900 rounded-md leading-relaxed">
                      {error}
                    </div>
                  )}
                </div>

                <div className="px-5 py-3.5 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-[#0F172A] flex items-center justify-between gap-3">
                  <span className="text-[11px] text-slate-500 dark:text-slate-400">
                    For local or firewalled databases, use the SQL Dump / DDL tab.
                  </span>
                  <div className="flex items-center gap-2 shrink-0">
                    <button
                      type="button"
                      onClick={handleDismiss}
                      className="px-3.5 py-2 text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
                    >
                      Cancel
                    </button>
                    <button
                      type="submit"
                      disabled={connecting}
                      className="flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-60 rounded-md transition-colors cursor-pointer whitespace-nowrap"
                    >
                      {connecting && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                      <span>{connecting ? 'Connecting...' : 'Fetch Tables'}</span>
                    </button>
                  </div>
                </div>
              </form>
            ) : (
              <div className="flex-1 overflow-y-auto studio-scroll flex flex-col">
                <div className="p-5 space-y-3.5 flex-1">
                  <div className="flex items-center justify-between gap-3">
                    <p className="text-xs text-slate-600 dark:text-slate-400 leading-relaxed">
                      Upload a schema dump (<code className="font-mono">.sql</code> from <code className="font-mono">pg_dump</code>, <code className="font-mono">mysqldump</code>, or SSMS) or paste DDL below to pick tables.
                    </p>
                    <input
                      ref={sqlFileRef}
                      type="file"
                      accept=".sql,.ddl,.txt"
                      onChange={handleSqlFileUpload}
                      className="hidden"
                    />
                    <button
                      type="button"
                      onClick={() => sqlFileRef.current?.click()}
                      className="px-3 py-1.5 text-xs font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-md transition-colors whitespace-nowrap shrink-0 cursor-pointer"
                    >
                      Choose .sql File
                    </button>
                  </div>

                  <textarea
                    value={sqlText}
                    onChange={(e) => {
                      setSqlText(e.target.value);
                      setError(null);
                    }}
                    rows={12}
                    spellCheck={false}
                    placeholder={`CREATE TABLE users (\n  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),\n  email VARCHAR(255) NOT NULL UNIQUE\n);\n\nCREATE TABLE invoices (\n  id BIGSERIAL PRIMARY KEY,\n  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,\n  amount NUMERIC(10,2) NOT NULL\n);`}
                    className="w-full p-3.5 font-mono text-xs bg-slate-50 dark:bg-[#0B1120] border border-slate-200 dark:border-slate-700 rounded-lg text-slate-800 dark:text-slate-200 focus:outline-none focus:border-blue-500 studio-scroll"
                  />

                  {error && (
                    <div className="px-3 py-2 text-xs text-rose-700 dark:text-rose-300 bg-rose-50 dark:bg-rose-950/50 border border-rose-200 dark:border-rose-900 rounded-md">
                      {error}
                    </div>
                  )}
                </div>

                <div className="px-5 py-3.5 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-[#0F172A] flex items-center justify-between gap-3">
                  <span className="text-[11px] text-slate-500 dark:text-slate-400">
                    Supports PostgreSQL, MSSQL, MySQL, and ANSI SQL DDL.
                  </span>
                  <div className="flex items-center gap-2 shrink-0">
                    <button
                      type="button"
                      onClick={handleDismiss}
                      className="px-3.5 py-2 text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
                    >
                      Cancel
                    </button>
                    <button
                      type="button"
                      onClick={handleAnalyzeSqlDump}
                      disabled={!sqlText.trim()}
                      className="px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-40 rounded-md transition-colors cursor-pointer whitespace-nowrap"
                    >
                      Inspect Tables
                    </button>
                  </div>
                </div>
              </div>
            )}
          </>
        )}

        {/* ================= STEP 2: SELECT TABLES ================= */}
        {step === 'select' && discovered && (
          <>
            {/* Filter & Bulk Selection Bar */}
            <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/40 space-y-2.5">
              <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-slate-500 dark:text-slate-400">
                <div className="flex items-center gap-2 tabular-nums">
                  <span className="font-semibold text-slate-800 dark:text-slate-200">
                    {ENGINE_PROFILES[discovered.engine].label}
                  </span>
                  <span aria-hidden="true">·</span>
                  <span>
                    {selectedTableIds.size} of {discovered.tables.length} tables selected
                  </span>
                  <span aria-hidden="true">·</span>
                  <span>{selectedRelationsCount} relations</span>
                </div>

                <div className="flex items-center gap-2.5 text-[11.5px]">
                  <button
                    type="button"
                    onClick={() =>
                      setSelectedTableIds(new Set(discovered.tables.map((t) => t.id)))
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
                    onClick={() => setSelectedTableIds(new Set())}
                    className="text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:underline cursor-pointer"
                  >
                    None
                  </button>
                  {discovered.relations.length > 0 && selectedTableIds.size > 0 && (
                    <>
                      <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
                        /
                      </span>
                      <button
                        type="button"
                        onClick={handleIncludeRelatedTables}
                        title="Also select tables connected by foreign keys"
                        className="text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
                      >
                        Include related
                      </button>
                    </>
                  )}
                </div>
              </div>

              <div className="relative">
                <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
                <input
                  type="text"
                  value={tableQuery}
                  onChange={(e) => setTableQuery(e.target.value)}
                  placeholder="Filter discovered tables or columns..."
                  className="w-full pl-8 pr-3 py-1.5 text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-md text-slate-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:border-blue-500"
                />
              </div>
            </div>

            {/* Non-blocking Catalog Warnings */}
            {discovered.warnings.length > 0 && (
              <div
                role="status"
                aria-label="Introspection warnings"
                className="px-5 py-3 border-b border-amber-200 dark:border-amber-900/70 bg-amber-50/90 dark:bg-amber-950/40"
              >
                <div className="flex items-start gap-2.5">
                  <AlertTriangle className="w-4 h-4 text-amber-600 dark:text-amber-400 shrink-0 mt-0.5" />
                  <div className="min-w-0 flex-1 space-y-1">
                    <p className="text-xs font-semibold text-amber-900 dark:text-amber-200">
                      {discovered.warnings.length}{' '}
                      {discovered.warnings.length === 1 ? 'catalog warning' : 'catalog warnings'}{' '}
                      (import can still proceed)
                    </p>
                    <ul className="space-y-1 max-h-24 overflow-y-auto studio-scroll pr-1">
                      {discovered.warnings.map((warning, idx) => (
                        <li
                          key={`${warning.code}-${idx}`}
                          className="text-[11px] text-amber-800 dark:text-amber-300 leading-snug"
                        >
                          <span className="font-mono font-semibold mr-1.5">[{warning.code}]</span>
                          <span>{warning.message}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </div>
            )}

            {/* Table Checklist */}
            <div className="flex-1 overflow-y-auto studio-scroll divide-y divide-slate-100 dark:divide-slate-800/80">
              {discovered.tables.length === 0 ? (
                <div className="px-5 py-12 text-center space-y-1.5">
                  <p className="text-xs font-medium text-slate-700 dark:text-slate-200">
                    Connected successfully to {discovered.databaseName}, but no user tables were found.
                  </p>
                  <p className="text-[11px] text-slate-500 dark:text-slate-400">
                    Try adjusting the schema filter or inspect another database.
                  </p>
                </div>
              ) : filteredTables.length === 0 ? (
                <div className="px-5 py-10 text-center text-xs text-slate-400 dark:text-slate-500">
                  No tables match "{tableQuery}"
                </div>
              ) : (
                filteredTables.map((table) => {
                  const checked = selectedTableIds.has(table.id);
                  const expanded = expandedTableId === table.id;
                  const pkCount = table.columns.filter((c) => c.isPrimaryKey).length;
                  const fkCount = fkCountByTable.get(table.id) || 0;

                  return (
                    <div key={table.id} className="bg-white dark:bg-[#0F172A]">
                      <div className="px-5 py-2.5 flex items-center justify-between gap-3 hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                        <div className="flex items-center gap-3 min-w-0">
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={() => toggleTableSelection(table.id)}
                            aria-label={`Select table ${table.name}`}
                            className="rounded border-slate-300 dark:border-slate-600 text-blue-600 focus:ring-blue-500 cursor-pointer shrink-0"
                          />
                          <button
                            type="button"
                            onClick={() =>
                              setExpandedTableId(expanded ? null : table.id)
                            }
                            className="flex items-center gap-1.5 min-w-0 text-left cursor-pointer"
                          >
                            {expanded ? (
                              <ChevronDown className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                            ) : (
                              <ChevronRight className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                            )}
                            <span className="font-mono text-xs font-semibold text-slate-900 dark:text-slate-100 truncate">
                              {table.schema ? `${table.schema}.` : ''}
                              {table.name}
                            </span>
                            {discoveredExistingIds.has(table.id) && (
                              <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800/70 shrink-0">
                                On canvas
                              </span>
                            )}
                          </button>
                        </div>

                        <div className="flex items-center gap-3 text-[11px] font-mono tabular-nums text-slate-500 dark:text-slate-400 shrink-0">
                          <span>{table.columns.length} cols</span>
                          {pkCount > 0 && (
                            <span className="text-amber-600 dark:text-amber-400">
                              {pkCount} PK
                            </span>
                          )}
                          {fkCount > 0 && (
                            <span className="text-blue-600 dark:text-blue-400 flex items-center gap-0.5">
                              <GitFork className="w-3 h-3" />
                              {fkCount} FK
                            </span>
                          )}
                        </div>
                      </div>

                      {/* Expandable Column Preview */}
                      {expanded && (
                        <div className="px-12 pb-3 pt-1 bg-slate-50/70 dark:bg-slate-900/50 border-t border-slate-100 dark:border-slate-800/70">
                          <div className="space-y-1">
                            {table.columns.map((col) => (
                              <div
                                key={col.id}
                                className="flex items-center justify-between gap-2 text-[11px] font-mono"
                              >
                                <div className="flex items-center gap-1.5 min-w-0">
                                  {col.isPrimaryKey ? (
                                    <KeyRound className="w-3 h-3 text-amber-600 dark:text-amber-400 shrink-0" />
                                  ) : (
                                    <Link2 className="w-3 h-3 opacity-0 shrink-0" />
                                  )}
                                  <span className="text-slate-800 dark:text-slate-200 truncate">
                                    {col.name}
                                  </span>
                                </div>
                                <div className="flex items-center gap-2 text-slate-500 dark:text-slate-400 shrink-0">
                                  <span>{col.type}</span>
                                  {!col.isNullable && (
                                    <span className="text-[10px] text-slate-400">NN</span>
                                  )}
                                </div>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  );
                })
              )}
            </div>

            {/* Footer with Merge / Replace Mode */}
            <div className="px-5 py-3.5 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-[#0F172A] flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-1 p-0.5 bg-slate-100 dark:bg-slate-900 rounded-md">
                <button
                  type="button"
                  onClick={() => setImportMode('merge')}
                  className={`px-2.5 py-1 text-xs font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                    importMode === 'merge'
                      ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  Merge into canvas
                </button>
                <button
                  type="button"
                  onClick={() => setImportMode('replace')}
                  className={`px-2.5 py-1 text-xs font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                    importMode === 'replace'
                      ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  Replace schema
                </button>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <button
                  type="button"
                  onClick={handleDismiss}
                  className="px-3.5 py-2 text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleConfirmImport}
                  disabled={selectedTableIds.size === 0}
                  className="px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-40 rounded-md transition-colors cursor-pointer whitespace-nowrap"
                >
                  Import {selectedTableIds.size}{' '}
                  {selectedTableIds.size === 1 ? 'Table' : 'Tables'}
                </button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
};
