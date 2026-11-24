import React, { useCallback, useEffect, useState } from 'react';
import {
  AlertTriangle,
  ArrowRight,
  Boxes,
  FolderInput,
  FolderOpen,
  Layers,
  Loader2,
  Moon,
  Plus,
  Sun,
  Trash2,
} from 'lucide-react';
import { mmaClient } from '../../api/mmaClient';
import { HealthInfo, ProjectSummary } from '../../types/mma';
import { CreateSolutionModal } from './CreateSolutionModal';
import { ImportSolutionModal } from './ImportSolutionModal';

interface SolutionsHubProps {
  health: HealthInfo | null;
  theme: 'light' | 'dark';
  onToggleTheme: () => void;
  onOpenProject: (project: ProjectSummary) => void;
  onOpenScratchpad: () => void;
}

export const SolutionsHub: React.FC<SolutionsHubProps> = ({
  health,
  theme,
  onToggleTheme,
  onOpenProject,
  onOpenScratchpad,
}) => {
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);

  const loadProjects = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await mmaClient.listProjects();
      setProjects(list);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load solutions.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadProjects();
  }, [loadProjects]);

  const handleRemove = async (e: React.MouseEvent, project: ProjectSummary) => {
    e.stopPropagation();
    try {
      await mmaClient.deleteProject(project.id);
      setProjects((prev) => prev.filter((p) => p.id !== project.id));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to unregister solution.');
    }
  };

  return (
    <div className="min-h-screen w-screen bg-slate-50 dark:bg-[#0B1120] text-slate-900 dark:text-slate-100 flex flex-col">
      <header className="border-b border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-[#111827]/90 backdrop-blur-md">
        <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-blue-600 text-white flex items-center justify-center shadow-sm">
              <Boxes className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-sm font-bold tracking-tight">MMA CLI Studio</h1>
                <span className="px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-[10px] font-mono text-slate-600 dark:text-slate-300">
                  v{health?.version ?? '5.0.0'}
                </span>
                {health?.mode === 'local-cli' && (
                  <span className="px-1.5 py-0.5 rounded bg-emerald-50 dark:bg-emerald-950/60 text-[10px] font-medium text-emerald-700 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800">
                    Local CLI
                  </span>
                )}
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Visual Schema Designer &amp; ASP.NET Core Code Generator
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={onOpenScratchpad}
              className="h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 text-xs font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 flex items-center gap-1.5 cursor-pointer"
            >
              <Layers className="w-3.5 h-3.5" />
              Scratchpad
            </button>

            <button
              type="button"
              onClick={() => setImportOpen(true)}
              className="h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 text-xs font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 flex items-center gap-1.5 cursor-pointer"
            >
              <FolderInput className="w-3.5 h-3.5" />
              Import Solution
            </button>

            <button
              type="button"
              onClick={() => setCreateOpen(true)}
              className="h-9 px-3.5 rounded-lg bg-blue-600 hover:bg-blue-700 text-white text-xs font-semibold flex items-center gap-1.5 shadow-xs cursor-pointer"
            >
              <Plus className="w-3.5 h-3.5" />
              New Solution
            </button>

            <button
              type="button"
              onClick={onToggleTheme}
              title="Toggle theme"
              className="w-9 h-9 rounded-lg border border-slate-200 dark:border-slate-700 flex items-center justify-center text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
            >
              {theme === 'dark' ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
            </button>
          </div>
        </div>
      </header>

      <main className="flex-1 max-w-6xl w-full mx-auto px-6 py-8">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h2 className="text-lg font-semibold">Solutions</h2>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Open a registered solution in the visual ERD designer or scaffold a new one.
            </p>
          </div>
        </div>

        {error && (
          <div className="mb-4 px-4 py-3 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-xs text-rose-700 dark:text-rose-300">
            {error}
          </div>
        )}

        {loading ? (
          <div className="py-20 flex items-center justify-center gap-2 text-sm text-slate-500">
            <Loader2 className="w-4 h-4 animate-spin" />
            Loading registered solutions...
          </div>
        ) : projects.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-slate-300 dark:border-slate-700 bg-white/70 dark:bg-slate-900/40 p-12 text-center">
            <FolderOpen className="w-10 h-10 mx-auto text-slate-400 mb-3" />
            <h3 className="text-sm font-semibold mb-1">No solutions registered yet</h3>
            <p className="text-xs text-slate-500 dark:text-slate-400 max-w-md mx-auto mb-5">
              Create a new ASP.NET Core solution, import an existing <code>.sln</code>/<code>.slnx</code> directory, or open the standalone Scratchpad to design schemas first.
            </p>
            <div className="flex items-center justify-center gap-2.5">
              <button
                type="button"
                onClick={() => setCreateOpen(true)}
                className="h-9 px-4 rounded-lg bg-blue-600 hover:bg-blue-700 text-white text-xs font-semibold flex items-center gap-1.5 cursor-pointer"
              >
                <Plus className="w-3.5 h-3.5" />
                Create Solution
              </button>
              <button
                type="button"
                onClick={() => setImportOpen(true)}
                className="h-9 px-4 rounded-lg border border-slate-200 dark:border-slate-700 text-xs font-medium hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
              >
                Import Existing
              </button>
              <button
                type="button"
                onClick={onOpenScratchpad}
                className="h-9 px-4 rounded-lg border border-slate-200 dark:border-slate-700 text-xs font-medium hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
              >
                Open Scratchpad
              </button>
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {projects.map((project) => (
              <div
                key={project.id}
                onClick={() => onOpenProject(project)}
                className="group rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#111827] p-4 shadow-xs hover:border-blue-500/70 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between gap-4"
              >
                <div>
                  <div className="flex items-start justify-between gap-2 mb-1.5">
                    <h3 className="text-sm font-semibold text-slate-900 dark:text-slate-100 truncate">
                      {project.name}
                    </h3>
                    <span className="px-2 py-0.5 rounded-full text-[10px] font-medium bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300 shrink-0">
                      {project.mapper}
                    </span>
                  </div>

                  <p
                    className="text-[11px] font-mono text-slate-500 dark:text-slate-400 truncate"
                    title={project.path}
                  >
                    {project.path}
                  </p>

                  {!project.existsOnDisk && (
                    <div className="mt-2 inline-flex items-center gap-1 text-[11px] text-amber-600 dark:text-amber-400">
                      <AlertTriangle className="w-3 h-3" />
                      Folder not found on disk
                    </div>
                  )}
                </div>

                <div className="pt-3 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between">
                  <div className="flex items-center gap-2 text-[11px]">
                    <span className="text-slate-500 dark:text-slate-400">
                      {project.entityCount} {project.entityCount === 1 ? 'entity' : 'entities'}
                    </span>
                    {project.pendingCount > 0 ? (
                      <span className="px-1.5 py-0.5 rounded bg-amber-50 dark:bg-amber-950/50 text-amber-700 dark:text-amber-300 font-medium">
                        {project.pendingCount} pending
                      </span>
                    ) : (
                      <span className="px-1.5 py-0.5 rounded bg-emerald-50 dark:bg-emerald-950/50 text-emerald-700 dark:text-emerald-300 font-medium">
                        Synced
                      </span>
                    )}
                  </div>

                  <div className="flex items-center gap-1">
                    <button
                      type="button"
                      onClick={(e) => void handleRemove(e, project)}
                      title="Unregister solution (keeps files on disk)"
                      className="p-1.5 rounded-lg text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-950/40 cursor-pointer"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                    <span className="inline-flex items-center gap-1 text-xs font-medium text-blue-600 dark:text-blue-400 group-hover:translate-x-0.5 transition-transform">
                      Open <ArrowRight className="w-3.5 h-3.5" />
                    </span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </main>

      <CreateSolutionModal
        open={createOpen}
        onCreated={(created) => {
          setCreateOpen(false);
          onOpenProject(created);
        }}
        onClose={() => setCreateOpen(false)}
      />

      <ImportSolutionModal
        open={importOpen}
        onImported={(imported) => {
          setImportOpen(false);
          onOpenProject(imported);
        }}
        onClose={() => setImportOpen(false)}
      />
    </div>
  );
};
