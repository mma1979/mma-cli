import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api/client";
import { errorMessage, isApiProblem } from "../../api/problem";
import { queryKeys } from "../../api/queryKeys";
import type { BrowseDirectoryResponse, DatabaseProviderKind, DatabaseSchemaSnapshot, DbImportApplyResult, DbImportRequest, DbImportTestResult, ProjectDetail } from "../../api/types";
import { DATABASE_PROVIDERS } from "../../api/types";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { Select } from "../../components/primitives/select";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { LogTerminal } from "../../components/shared/LogTerminal";
import { Spinner } from "../../components/shared/Spinner";

export function DbImportWizard() {
  const { projectId: rawId } = useParams<{ projectId: string }>();
  const projectId = Number.parseInt(rawId ?? "", 10);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [provider, setProvider] = useState<DatabaseProviderKind>("Sqlite");
  const connectionStringRef = useRef("");
  const connectionStringInput = useRef<HTMLInputElement>(null);
  const [hasConnectionString, setHasConnectionString] = useState(false);
  const [serverOrPath, setServerOrPath] = useState("");
  const [database, setDatabase] = useState("");
  const [username, setUsername] = useState("");
  const passwordRef = useRef("");
  const passwordInput = useRef<HTMLInputElement>(null);
  const [port, setPort] = useState("");
  const [sqlServerAuth, setSqlServerAuth] = useState<"WindowsIntegrated" | "SqlPassword" | "ActiveDirectoryIntegrated">("SqlPassword");
  const [trustServerCertificate, setTrustServerCertificate] = useState(false);
  const [step, setStep] = useState<"connect" | "introspect" | "review" | "apply">("connect");
  const clearCredentialInputs = (): void => {
    if (connectionStringInput.current) connectionStringInput.current.value = "";
    if (passwordInput.current) passwordInput.current.value = "";
  };
  const clearCredentials = (): void => {
    connectionStringRef.current = "";
    passwordRef.current = "";
    setHasConnectionString(false);
    clearCredentialInputs();
  };
  useEffect(() => () => {
    connectionStringRef.current = "";
    passwordRef.current = "";
    if (connectionStringInput.current) connectionStringInput.current.value = "";
    if (passwordInput.current) passwordInput.current.value = "";
  }, []);
  const [snapshot, setSnapshot] = useState<DatabaseSchemaSnapshot | null>(null);
  const [selectedTables, setSelectedTables] = useState<string[]>([]);
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const [includeSystemTables, setIncludeSystemTables] = useState(false);
  const [applyImmediately, setApplyImmediately] = useState(false);
  const [jobId, setJobId] = useState<string | null>(null);

  const roots = useQuery({
    queryKey: ["filesystem-roots"],
    queryFn: ({ signal }) => api.get<BrowseDirectoryResponse>("/api/v1/fs/browse?path=", signal),
    enabled: provider === "Sqlite",
    retry: 0
  });
  const request = (): DbImportRequest => {
    const connectionString = connectionStringRef.current.trim();
    return {
      provider,
      ...(connectionString ? { connectionString } : {}),
      ...(!connectionString && serverOrPath ? {
        parameters: {
          serverOrPath,
          ...(database ? { database } : {}),
          ...(username ? { username } : {}),
          ...(passwordRef.current ? { password: passwordRef.current } : {}),
          ...(port ? { port: Number(port) } : {}),
          sqlServerAuth,
          trustServerCertificate
        }
      } : {}),
      filters: { includeSystemTables, ...(Object.keys(overrides).length ? { tableNameOverrides: overrides } : {}) }
    };
  };
  const test = useMutation({
    mutationFn: () => api.post<DbImportTestResult>("/api/v1/db-import/test", request()),
    onSuccess: (result) => {
      if (result.connected) {
        clearCredentialInputs();
        setStep("introspect");
      }
    }
  });
  const introspect = useMutation({
    mutationFn: () => api.post<DatabaseSchemaSnapshot>("/api/v1/db-import/introspect", request()),
    onSuccess: (result) => {
      setSnapshot(result);
      setSelectedTables(result.tables.map((table) => table.tableName));
      clearCredentials();
      setStep("review");
    }
  });
  const apply = useMutation({
    mutationFn: () => api.post<DbImportApplyResult>(`/api/v1/projects/${projectId}/db-import/apply`, {
      provider,
      snapshot,
      selectedTables,
      filters: { includeSystemTables },
      ...(Object.keys(overrides).length ? { tableEntityOverrides: overrides } : {}),
      applyImmediately
    }),
    onSuccess: (result) => {
      queryClient.setQueryData<ProjectDetail>(queryKeys.project(projectId), (current) => current ? {
        ...current,
        manifest: result.manifest,
        manifestHash: result.contentHash
      } : current);
      void queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });
      if (result.jobId) {
        setJobId(result.jobId);
      }
      clearCredentials();
      setStep("apply");
    }
  });
  if (!Number.isFinite(projectId)) return <ErrorBanner message="Invalid project id" />;
  const failure = test.error ?? introspect.error ?? apply.error;
  const apiProblem = isApiProblem(failure) ? failure : null;
  const invalidPort = Boolean(port) && (!Number.isInteger(Number(port)) || Number(port) < 1 || Number(port) > 65535);
  const pathOutsideRoots = apiProblem?.errorCode === "PATH_OUTSIDE_ALLOWLIST";
  const failMessage = invalidPort
    ? "Port must be a whole number from 1 to 65535."
    : pathOutsideRoots
    ? `That folder is outside the allowed workspace roots${serverOrPath ? `: ${serverOrPath}` : ""}`
    : failure ? errorMessage(failure) : null;
  const selectedCount = selectedTables.length;

  return (
    <section className="flex h-full min-h-0 flex-col">
      <header className="flex items-center gap-3 border-b border-border px-4 py-3">
        <Button size="icon" variant="ghost" aria-label="Back to project" onClick={() => navigate(`/projects/${projectId}`)}><ArrowLeft size={15} /></Button>
        <div><h1 className="text-sm font-semibold">Import database</h1><p className="text-xs text-muted-foreground">Connect, inspect, select tables, then apply.</p></div>
      </header>
      <div className="min-h-0 flex-1 space-y-4 overflow-auto p-4">
        <ol className="flex flex-wrap gap-2 text-xs" aria-label="Import steps">
          {(["connect", "introspect", "review", "apply"] as const).map((name, index) => (
            <li key={name} aria-current={step === name ? "step" : undefined} className={`rounded border px-2 py-1 ${step === name ? "border-primary text-foreground" : "border-border text-muted-foreground"}`}>
              {index + 1}. {name[0]?.toUpperCase()}{name.slice(1)}
            </li>
          ))}
        </ol>
        {failMessage ? <ErrorBanner message={failMessage} {...(pathOutsideRoots ? { detail: `Allowed roots: ${roots.data?.allowedRoots.join(", ") ?? "Loading roots…"}` } : {})} /> : null}
        {step === "connect" ? (
          <section className="max-w-3xl space-y-3" aria-label="Connect to database">
            <h2 className="text-sm font-semibold">1. Connect</h2>
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="space-y-1 text-xs"><span>Provider</span><Select aria-label="Provider" value={provider} onChange={(event) => { setProvider(event.target.value as DatabaseProviderKind); setSnapshot(null); clearCredentials(); test.reset(); introspect.reset(); }}>
                {DATABASE_PROVIDERS.map((item) => <option key={item} value={item}>{item}</option>)}
              </Select></label>
              <label className="space-y-1 text-xs"><span>Connection string (optional)</span><Input ref={connectionStringInput} aria-label="Connection string" autoComplete="off" onChange={(event) => { connectionStringRef.current = event.target.value; setHasConnectionString(Boolean(event.target.value.trim())); }} /></label>
              <label className="space-y-1 text-xs"><span>Server or SQLite path</span><Input aria-label="Server or SQLite path" value={serverOrPath} onChange={(event) => setServerOrPath(event.target.value)} /></label>
              <label className="space-y-1 text-xs"><span>Database</span><Input aria-label="Database" value={database} onChange={(event) => setDatabase(event.target.value)} /></label>
              <label className="space-y-1 text-xs"><span>Username</span><Input aria-label="Username" autoComplete="off" value={username} onChange={(event) => setUsername(event.target.value)} /></label>
              <label className="space-y-1 text-xs"><span>Port</span><Input aria-label="Port" inputMode="numeric" value={port} onChange={(event) => setPort(event.target.value)} aria-invalid={invalidPort || undefined} /></label>
              <label className="space-y-1 text-xs"><span>SQL Server authentication</span><Select aria-label="SQL Server authentication" value={sqlServerAuth} onChange={(event) => setSqlServerAuth(event.target.value as typeof sqlServerAuth)}>
                <option value="SqlPassword">SQL password</option><option value="WindowsIntegrated">Windows integrated</option><option value="ActiveDirectoryIntegrated">Active Directory integrated</option>
              </Select></label>
              <label className="space-y-1 text-xs"><span>Password</span><Input ref={passwordInput} aria-label="Password" type="password" autoComplete="new-password" onChange={(event) => { passwordRef.current = event.target.value; }} /></label>
              <label className="flex items-center gap-2 text-xs sm:col-span-2"><input type="checkbox" checked={trustServerCertificate} onChange={(event) => setTrustServerCertificate(event.target.checked)} />Trust server certificate</label>
            </div>
            {provider === "Sqlite" && roots.data?.allowedRoots.length ? <p className="text-xs text-muted-foreground">SQLite files must be under: {roots.data.allowedRoots.join(", ")}</p> : null}
            <Button variant="outline" disabled={test.isPending || invalidPort || (!hasConnectionString && !serverOrPath)} onClick={() => test.mutate()}>{test.isPending ? "Testing…" : "Test connection"}</Button>
            {test.data?.connected ? <span role="status" className="ml-2 text-xs">Connected to {test.data.provider}</span> : null}
          </section>
        ) : null}
        {step === "introspect" ? (
          <section className="max-w-3xl space-y-3" aria-label="Introspect database">
            <h2 className="text-sm font-semibold">2. Introspect</h2>
            <p className="text-xs text-muted-foreground">Connection verified. Read the schema before choosing tables to import.</p>
            <label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={includeSystemTables} onChange={(event) => setIncludeSystemTables(event.target.checked)} />Include system tables</label>
            <div className="flex gap-2"><Button variant="outline" onClick={() => { clearCredentials(); test.reset(); introspect.reset(); setStep("connect"); }}>Back</Button><Button disabled={introspect.isPending} onClick={() => introspect.mutate()}>{introspect.isPending ? "Inspecting…" : "Introspect database"}</Button></div>
            {introspect.isPending ? <Spinner label="Reading database schema…" /> : null}
          </section>
        ) : null}
        {step === "review" && snapshot ? (
          <section aria-label="Database tables" className="max-w-4xl space-y-3 rounded border border-border p-3">
            <h2 className="text-sm font-semibold">3. Review {selectedCount} of {snapshot.tables.length} tables</h2>
            <div className="space-y-2">
              {snapshot.tables.map((table) => {
                const selected = selectedTables.includes(table.tableName);
                return <div key={`${table.schemaName}.${table.tableName}`} className="grid gap-2 border-b border-border pb-2 sm:grid-cols-[minmax(0,1fr)_minmax(10rem,0.7fr)]">
                  <label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={selected} onChange={() => setSelectedTables((current) => selected ? current.filter((name) => name !== table.tableName) : [...current, table.tableName])} /><span>{table.schemaName}.{table.tableName} <span className="text-muted-foreground">({table.columns.length} columns)</span></span></label>
                  <Input aria-label={`Entity name for ${table.tableName}`} value={overrides[table.tableName] ?? table.entityName} onChange={(event) => setOverrides((current) => ({ ...current, [table.tableName]: event.target.value }))} />
                </div>;
              })}
            </div>
            {snapshot.relations.length ? <p className="text-xs text-muted-foreground">Detected relations: {snapshot.relations.map((relation) => `${relation.parentEntity} → ${relation.childEntity}`).join(", ")}</p> : null}
            <label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={applyImmediately} onChange={(event) => setApplyImmediately(event.target.checked)} />Apply generated changes immediately</label>
            <div className="flex gap-2"><Button variant="outline" onClick={() => { clearCredentials(); test.reset(); introspect.reset(); setStep("connect"); }}>Change connection</Button><Button disabled={!selectedCount || apply.isPending} onClick={() => apply.mutate()}>{apply.isPending ? "Applying…" : "Apply selected tables"}</Button></div>
          </section>
        ) : null}
        {step === "apply" ? (
          <section className="space-y-3" aria-label="Apply database import">
            <h2 className="text-sm font-semibold">4. Apply</h2>
            <p role="status" className="text-xs">The selected tables were added to the project manifest.</p>
            {jobId ? <LogTerminal jobId={jobId} onClose={() => setJobId(null)} /> : null}
          </section>
        ) : null}
      </div>
    </section>
  );
}
