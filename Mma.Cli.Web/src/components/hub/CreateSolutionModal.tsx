import React, { useCallback, useState } from 'react';
import { Loader2, Plus, X } from 'lucide-react';
import { mmaClient } from '../../api/mmaClient';
import { BrowseFsResponse, ProjectSummary } from '../../types/mma';
import { FolderBrowser } from './FolderBrowser';

interface CreateSolutionModalProps {
  open: boolean;
  onCreated: (project: ProjectSummary) => void;
  onClose: () => void;
}

export const CreateSolutionModal: React.FC<CreateSolutionModalProps> = ({
  open,
  onCreated,
  onClose,
}) => {
  const [name, setName] = useState('Acme.App');
  const [mapper, setMapper] = useState<'AutoMapper' | 'Mapster'>('AutoMapper');
  const [parentDirectory, setParentDirectory] = useState('');
  const [runInitialMigration, setRunInitialMigration] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSelectPath = useCallback((path: string, _info: BrowseFsResponse) => {
    setParentDirectory(path);
  }, []);

  if (!open) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !parentDirectory.trim()) {
      setError('Solution name and destination folder are required.');
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const created = await mmaClient.createProject({
        name: name.trim(),
        parentDirectory: parentDirectory.trim(),
        mapper,
        runInitialMigration,
      });
      onCreated(created);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create solution.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-slate-950/60 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="w-full max-w-xl rounded-2xl bg-white dark:bg-[#111827] border border-slate-200 dark:border-slate-700 shadow-2xl overflow-hidden">
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
          <div>
            <h2 className="text-base font-semibold text-slate-900 dark:text-slate-100">
              Create New MMA Solution
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Scaffolds a clean ASP.NET Core solution and registers it in your local workspace.
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

        <form onSubmit={handleSubmit} className="p-5 space-y-4">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1">
                Solution Name
              </label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. Acme.Orders"
                required
                className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/40"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1">
                Object Mapper
              </label>
              <select
                value={mapper}
                onChange={(e) => setMapper(e.target.value as 'AutoMapper' | 'Mapster')}
                className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/40"
              >
                <option value="AutoMapper">AutoMapper</option>
                <option value="Mapster">Mapster</option>
              </select>
            </div>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1.5">
              Destination Parent Folder
            </label>
            <FolderBrowser allowCreateFolder onSelectPath={handleSelectPath} />
          </div>

          <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={runInitialMigration}
              onChange={(e) => setRunInitialMigration(e.target.checked)}
              className="rounded border-slate-300 dark:border-slate-600 text-blue-600 focus:ring-blue-500"
            />
            Run initial EF Core migration (`dotnet ef migrations add InitialMigration`)
          </label>

          {error && (
            <div className="px-3 py-2 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-xs text-rose-700 dark:text-rose-300">
              {error}
            </div>
          )}

          <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-200 dark:border-slate-800">
            <button
              type="button"
              onClick={onClose}
              className="h-9 px-4 rounded-lg border border-slate-200 dark:border-slate-700 text-xs font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={submitting || !parentDirectory}
              className="h-9 px-4 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-xs font-semibold flex items-center gap-1.5 cursor-pointer"
            >
              {submitting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Plus className="w-3.5 h-3.5" />}
              {submitting ? 'Scaffolding...' : 'Create Solution'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
