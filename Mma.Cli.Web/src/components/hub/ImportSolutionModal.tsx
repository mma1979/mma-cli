import React, { useCallback, useState } from 'react';
import { FolderInput, Loader2, X } from 'lucide-react';
import { mmaClient } from '../../api/mmaClient';
import { BrowseFsResponse, ProjectSummary } from '../../types/mma';
import { FolderBrowser } from './FolderBrowser';

interface ImportSolutionModalProps {
  open: boolean;
  onImported: (project: ProjectSummary) => void;
  onClose: () => void;
}

export const ImportSolutionModal: React.FC<ImportSolutionModalProps> = ({
  open,
  onImported,
  onClose,
}) => {
  const [selectedPath, setSelectedPath] = useState('');
  const [hasSolutionFile, setHasSolutionFile] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSelectPath = useCallback((path: string, info: BrowseFsResponse) => {
    setSelectedPath(path);
    setHasSolutionFile(info.hasSolutionFile);
  }, []);

  if (!open) return null;

  const handleImport = async () => {
    if (!selectedPath) return;
    setSubmitting(true);
    setError(null);
    try {
      const imported = await mmaClient.importProject(selectedPath);
      onImported(imported);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to import solution.');
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
              Import Existing Solution
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Select a directory containing a <code>.sln</code> or <code>.slnx</code> file (and optional <code>.mma/</code> metadata).
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

        <div className="p-5 space-y-4">
          <FolderBrowser allowCreateFolder={false} onSelectPath={handleSelectPath} />

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
              type="button"
              onClick={handleImport}
              disabled={submitting || !hasSolutionFile}
              className="h-9 px-4 rounded-lg bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 text-white text-xs font-semibold flex items-center gap-1.5 cursor-pointer"
            >
              {submitting ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              ) : (
                <FolderInput className="w-3.5 h-3.5" />
              )}
              {submitting ? 'Importing...' : 'Import Solution'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
