import React, { useCallback, useEffect, useState } from 'react';
import {
  ArrowUp,
  CheckCircle2,
  Folder,
  FolderPlus,
  Loader2,
  RefreshCw,
  Search,
} from 'lucide-react';
import { mmaClient } from '../../api/mmaClient';
import { BrowseFsResponse } from '../../types/mma';

interface FolderBrowserProps {
  initialPath?: string;
  allowCreateFolder?: boolean;
  onSelectPath: (path: string, info: BrowseFsResponse) => void;
}

export const FolderBrowser: React.FC<FolderBrowserProps> = ({
  initialPath,
  allowCreateFolder = true,
  onSelectPath,
}) => {
  const [pathInput, setPathInput] = useState(initialPath ?? '');
  const [data, setData] = useState<BrowseFsResponse | null>(null);
  const [filter, setFilter] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');

  const loadPath = useCallback(
    async (target?: string) => {
      setLoading(true);
      setError(null);
      try {
        const res = await mmaClient.browseFs(target);
        setData(res);
        setPathInput(res.currentPath);
        onSelectPath(res.currentPath, res);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to browse directory.');
      } finally {
        setLoading(false);
      }
    },
    [onSelectPath]
  );

  useEffect(() => {
    void loadPath(initialPath);
  }, [initialPath, loadPath]);

  const handleNavigateInto = (dirName: string) => {
    if (!data) return;
    const sep = data.currentPath.includes('\\') ? '\\' : '/';
    const nextPath = data.currentPath.endsWith(sep)
      ? `${data.currentPath}${dirName}`
      : `${data.currentPath}${sep}${dirName}`;
    void loadPath(nextPath);
  };

  const handleCreateFolder = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!data || !newFolderName.trim()) return;
    setError(null);
    try {
      const created = await mmaClient.mkdirFs(data.currentPath, newFolderName.trim());
      setNewFolderName('');
      setCreatingFolder(false);
      await loadPath(created.path);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create folder.');
    }
  };

  const visibleDirs = (data?.directories ?? []).filter((d) =>
    d.toLowerCase().includes(filter.trim().toLowerCase())
  );

  return (
    <div className="space-y-2.5">
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => data?.parentPath && void loadPath(data.parentPath)}
          disabled={!data?.parentPath || loading}
          title="Parent directory"
          className="h-9 px-2.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-40 cursor-pointer"
        >
          <ArrowUp className="w-4 h-4" />
        </button>

        <input
          type="text"
          value={pathInput}
          onChange={(e) => setPathInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              void loadPath(pathInput);
            }
          }}
          placeholder="Directory path..."
          className="flex-1 h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs font-mono text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/40"
        />

        <button
          type="button"
          onClick={() => void loadPath(pathInput)}
          disabled={loading}
          title="Load path"
          className="h-9 px-2.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
        >
          <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
        </button>

        {allowCreateFolder && (
          <button
            type="button"
            onClick={() => setCreatingFolder((v) => !v)}
            title="New folder"
            className="h-9 px-2.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer"
          >
            <FolderPlus className="w-4 h-4" />
          </button>
        )}
      </div>

      {creatingFolder && (
        <form onSubmit={handleCreateFolder} className="flex items-center gap-2">
          <input
            type="text"
            value={newFolderName}
            onChange={(e) => setNewFolderName(e.target.value)}
            placeholder="New folder name..."
            autoFocus
            className="flex-1 h-8 px-2.5 rounded-lg border border-blue-400 dark:border-blue-600 bg-white dark:bg-slate-900 text-xs text-slate-900 dark:text-slate-100 focus:outline-none"
          />
          <button
            type="submit"
            className="h-8 px-3 rounded-lg bg-blue-600 hover:bg-blue-700 text-white text-xs font-medium cursor-pointer"
          >
            Create
          </button>
        </form>
      )}

      {data && (data.hasSolutionFile || data.isMmaSolution) && (
        <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800 text-xs text-emerald-700 dark:text-emerald-300">
          <CheckCircle2 className="w-3.5 h-3.5 shrink-0" />
          <span>
            {data.isMmaSolution
              ? 'Detected MMA solution (.sln/.slnx + .mma metadata)'
              : 'Detected .NET solution file (.sln/.slnx)'}
          </span>
        </div>
      )}

      {error && (
        <div className="px-3 py-2 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-xs text-rose-700 dark:text-rose-300">
          {error}
        </div>
      )}

      <div className="relative">
        <Search className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-1/2 -translate-y-1/2" />
        <input
          type="text"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder="Filter folders..."
          className="w-full h-8 pl-8 pr-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/60 text-xs text-slate-800 dark:text-slate-200 focus:outline-none"
        />
      </div>

      <div className="h-48 overflow-y-auto rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-950/60 divide-y divide-slate-100 dark:divide-slate-800/80">
        {loading ? (
          <div className="h-full flex items-center justify-center text-xs text-slate-500 gap-2">
            <Loader2 className="w-4 h-4 animate-spin" />
            Loading directories...
          </div>
        ) : visibleDirs.length === 0 ? (
          <div className="h-full flex items-center justify-center text-xs text-slate-400">
            No subfolders found
          </div>
        ) : (
          visibleDirs.map((dir) => (
            <button
              key={dir}
              type="button"
              onClick={() => handleNavigateInto(dir)}
              className="w-full px-3 py-2 flex items-center gap-2 text-left text-xs text-slate-700 dark:text-slate-200 hover:bg-blue-50/70 dark:hover:bg-blue-950/40 transition-colors cursor-pointer"
            >
              <Folder className="w-3.5 h-3.5 text-amber-500 shrink-0" />
              <span className="truncate font-mono">{dir}</span>
            </button>
          ))
        )}
      </div>
    </div>
  );
};
