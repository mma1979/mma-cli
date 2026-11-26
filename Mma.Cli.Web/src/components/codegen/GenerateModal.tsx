import React, { useEffect, useState } from 'react';
import {
  CheckCircle2,
  Clipboard,
  ClipboardCheck,
  Loader2,
  Play,
  Terminal,
  X,
  XCircle,
} from 'lucide-react';
import { mmaClient } from '../../api/mmaClient';
import {
  CodeGenApplyResult,
  CodeGenPreview,
  ProjectSummary,
  SchemaSyncState,
} from '../../types/mma';

interface GenerateModalProps {
  open: boolean;
  project: ProjectSummary;
  onBeforePreview: () => Promise<void>;
  onApplied: (syncState: SchemaSyncState) => void;
  onClose: () => void;
}

export const GenerateModal: React.FC<GenerateModalProps> = ({
  open,
  project,
  onBeforePreview,
  onApplied,
  onClose,
}) => {
  const [loading, setLoading] = useState(false);
  const [applying, setApplying] = useState(false);
  const [preview, setPreview] = useState<CodeGenPreview | null>(null);
  const [result, setResult] = useState<CodeGenApplyResult | null>(null);
  const [runEfMigration, setRunEfMigration] = useState(false);
  const [migrationName, setMigrationName] = useState('');
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setResult(null);
    setError(null);
    setLoading(true);

    (async () => {
      try {
        await onBeforePreview();
        const data = await mmaClient.previewCodeGen(project.id);
        if (!cancelled) setPreview(data);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load codegen preview.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [open, project.id, onBeforePreview]);

  if (!open) return null;

  const totalPending = preview
    ? preview.pendingEntities.length +
      preview.pendingProperties.length +
      preview.pendingRelations.length
    : 0;

  const handleCopyCommands = async () => {
    if (!preview || preview.equivalentCommands.length === 0) return;
    try {
      await navigator.clipboard.writeText(preview.equivalentCommands.join('\n'));
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      // Ignore clipboard failure
    }
  };

  const handleApply = async () => {
    setApplying(true);
    setError(null);
    try {
      const applyRes = await mmaClient.applyCodeGen(project.id, runEfMigration, migrationName);
      setResult(applyRes);
      onApplied(applyRes.syncState);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Code generation failed.');
    } finally {
      setApplying(false);
    }
  };

  return (
    <div
      onPointerDown={(e) => e.stopPropagation()}
      className="fixed inset-0 z-50 bg-slate-950/60 backdrop-blur-xs flex items-center justify-center p-4"
    >
      <div className="w-full max-w-2xl rounded-2xl bg-white dark:bg-[#111827] border border-slate-200 dark:border-slate-700 shadow-2xl overflow-hidden flex flex-col max-h-[85vh]">
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
          <div>
            <h2 className="text-base font-semibold text-slate-900 dark:text-slate-100">
              Generate &amp; Migrate — {project.name}
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400 font-mono truncate max-w-lg">
              {project.path}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="p-5 overflow-y-auto space-y-4 flex-1">
          {loading ? (
            <div className="py-12 flex items-center justify-center gap-2 text-sm text-slate-500">
              <Loader2 className="w-4 h-4 animate-spin" />
              Computing pending schema changes...
            </div>
          ) : error ? (
            <div className="px-3.5 py-2.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-xs text-rose-700 dark:text-rose-300">
              {error}
            </div>
          ) : result ? (
            <div className="space-y-3">
              <div
                className={`px-3.5 py-2.5 rounded-xl border text-xs font-medium flex items-center gap-2 ${
                  result.succeeded
                    ? 'bg-emerald-50 dark:bg-emerald-950/40 border-emerald-200 dark:border-emerald-800 text-emerald-700 dark:text-emerald-300'
                    : 'bg-amber-50 dark:bg-amber-950/40 border-amber-200 dark:border-amber-800 text-amber-700 dark:text-amber-300'
                }`}
              >
                {result.succeeded ? (
                  <CheckCircle2 className="w-4 h-4 shrink-0" />
                ) : (
                  <XCircle className="w-4 h-4 shrink-0" />
                )}
                <span>
                  {result.succeeded
                    ? `Generation completed cleanly (${result.steps.length} steps).`
                    : 'Generation finished with errors. Succeeded steps were preserved.'}
                </span>
              </div>

              <div className="rounded-xl border border-slate-200 dark:border-slate-800 divide-y divide-slate-100 dark:divide-slate-800 max-h-64 overflow-y-auto">
                {result.steps.map((step, i) => (
                  <div key={`${step.kind}-${step.target}-${i}`} className="px-3 py-2 flex items-start gap-2 text-xs">
                    {step.succeeded ? (
                      <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500 mt-0.5 shrink-0" />
                    ) : (
                      <XCircle className="w-3.5 h-3.5 text-rose-500 mt-0.5 shrink-0" />
                    )}
                    <div className="min-w-0">
                      <div className="font-mono font-medium text-slate-800 dark:text-slate-200">
                        [{step.kind}] {step.target}
                      </div>
                      <div className="text-slate-500 dark:text-slate-400 break-words">{step.message}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ) : preview && totalPending === 0 ? (
            <div className="py-10 text-center space-y-2">
              <CheckCircle2 className="w-9 h-9 text-emerald-500 mx-auto" />
              <div className="text-sm font-semibold">All schema changes are already applied</div>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Add new tables, columns, or relations on the visual canvas to generate code.
              </p>
            </div>
          ) : (
            preview && (
              <>
                <div className="grid grid-cols-3 gap-2.5 text-center">
                  <div className="rounded-xl bg-slate-50 dark:bg-slate-900/70 border border-slate-200 dark:border-slate-800 p-3">
                    <div className="text-lg font-bold text-blue-600 dark:text-blue-400">
                      {preview.pendingEntities.length}
                    </div>
                    <div className="text-[11px] text-slate-500">New Entities</div>
                  </div>
                  <div className="rounded-xl bg-slate-50 dark:bg-slate-900/70 border border-slate-200 dark:border-slate-800 p-3">
                    <div className="text-lg font-bold text-indigo-600 dark:text-indigo-400">
                      {preview.pendingProperties.length}
                    </div>
                    <div className="text-[11px] text-slate-500">New Properties</div>
                  </div>
                  <div className="rounded-xl bg-slate-50 dark:bg-slate-900/70 border border-slate-200 dark:border-slate-800 p-3">
                    <div className="text-lg font-bold text-emerald-600 dark:text-emerald-400">
                      {preview.pendingRelations.length}
                    </div>
                    <div className="text-[11px] text-slate-500">New Relations</div>
                  </div>
                </div>

                {preview.pendingEntities.length > 0 && (
                  <div className="rounded-xl border border-slate-200 dark:border-slate-800 divide-y divide-slate-100 dark:divide-slate-800/80 overflow-hidden">
                    {preview.pendingEntities.map((ent) => (
                      <div
                        key={ent.entityName}
                        className="px-3 py-2 flex items-center justify-between gap-2 text-xs bg-white dark:bg-slate-900/40"
                      >
                        <div className="font-mono font-medium text-slate-800 dark:text-slate-200 truncate">
                          {ent.entityName}{' '}
                          <span className="text-slate-400 dark:text-slate-500">({ent.pkType})</span>
                        </div>
                        <div className="flex items-center gap-1.5 shrink-0 text-[11px]">
                          {ent.hasService ? (
                            <span className="px-2 py-0.5 rounded-full bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300 border border-blue-200/70 dark:border-blue-800/60">
                              Service ({ent.serviceMethods.length}/6)
                            </span>
                          ) : (
                            <span className="px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
                              No Service
                            </span>
                          )}
                          {ent.hasService && ent.hasApi ? (
                            <span className="px-2 py-0.5 rounded-full bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border border-emerald-200/70 dark:border-emerald-800/60">
                              API
                            </span>
                          ) : (
                            <span className="px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
                              No API
                            </span>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <div className="flex items-center gap-1.5 text-xs font-medium text-slate-700 dark:text-slate-300">
                      <Terminal className="w-3.5 h-3.5" />
                      Equivalent CLI Commands
                    </div>
                    <button
                      type="button"
                      onClick={() => void handleCopyCommands()}
                      className="inline-flex items-center gap-1 text-[11px] text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
                    >
                      {copied ? <ClipboardCheck className="w-3.5 h-3.5" /> : <Clipboard className="w-3.5 h-3.5" />}
                      {copied ? 'Copied' : 'Copy'}
                    </button>
                  </div>
                  <pre className="p-3 rounded-xl bg-slate-950 text-slate-100 text-[11px] font-mono overflow-x-auto max-h-48">
                    {preview.equivalentCommands.join('\n')}
                  </pre>
                </div>

                <div className="rounded-xl border border-slate-200 dark:border-slate-800 p-3 space-y-2">
                  <label className="flex items-center gap-2 text-xs font-medium text-slate-700 dark:text-slate-200 cursor-pointer select-none">
                    <input
                      type="checkbox"
                      checked={runEfMigration}
                      onChange={(e) => setRunEfMigration(e.target.checked)}
                      className="rounded border-slate-300 dark:border-slate-600 text-blue-600 focus:ring-blue-500"
                    />
                    Also run EF Core migration (`dotnet ef migrations add`)
                  </label>
                  {runEfMigration && (
                    <input
                      type="text"
                      value={migrationName}
                      onChange={(e) => setMigrationName(e.target.value)}
                      placeholder="Optional migration name (defaults to timestamped name)"
                      className="w-full h-8 px-2.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs font-mono text-slate-900 dark:text-slate-100"
                    />
                  )}
                </div>
              </>
            )
          )}
        </div>

        <div className="px-5 py-3.5 border-t border-slate-200 dark:border-slate-800 flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="h-9 px-4 rounded-lg border border-slate-200 dark:border-slate-700 text-xs font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
          >
            Close
          </button>
          {!result && totalPending > 0 && (
            <button
              type="button"
              onClick={() => void handleApply()}
              disabled={applying || loading}
              className="h-9 px-4 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-xs font-semibold flex items-center gap-1.5 cursor-pointer"
            >
              {applying ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Play className="w-3.5 h-3.5" />}
              {applying ? 'Generating...' : `Apply ${totalPending} Change${totalPending === 1 ? '' : 's'}`}
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
