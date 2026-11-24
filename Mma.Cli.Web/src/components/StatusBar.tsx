import React, { useMemo, useState } from 'react';
import {
  AlertCircle,
  AlertTriangle,
  ArrowUpRight,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Code2,
  X,
} from 'lucide-react';
import { DEFAULT_DATABASE_ENGINE } from '../config/featureFlags';
import { ENGINE_PROFILES } from '../data/engines';
import { DatabaseEngine } from '../types/schema';
import {
  SchemaValidationResult,
  ValidationIssue,
  ValidationSeverity,
} from '../utils/validator';

interface StatusBarProps {
  engine: DatabaseEngine;
  tableCount: number;
  columnCount: number;
  relationCount: number;
  indexCount: number;
  validation: SchemaValidationResult;
  isLiveSqlOpen: boolean;
  onToggleLiveSql: () => void;
  onFocusIssue: (issue: ValidationIssue) => void;
  onMakeColumnNullable?: (tableId: string, columnId: string) => void;
}

type IssueFilter = 'all' | ValidationSeverity;

export const StatusBar: React.FC<StatusBarProps> = ({
  engine,
  tableCount,
  columnCount,
  relationCount,
  indexCount,
  validation,
  isLiveSqlOpen,
  onToggleLiveSql,
  onFocusIssue,
  onMakeColumnNullable,
}) => {
  const [panelOpen, setPanelOpen] = useState(false);
  const [filter, setFilter] = useState<IssueFilter>('all');

  const engineLabel = ENGINE_PROFILES[engine || DEFAULT_DATABASE_ENGINE].label;
  const totalIssues = validation.issues.length;
  const topIssue = validation.issues[0] ?? null;

  const filteredIssues = useMemo(() => {
    if (filter === 'all') return validation.issues;
    return validation.issues.filter((i) => i.severity === filter);
  }, [validation.issues, filter]);

  return (
    <div className="relative shrink-0 z-30 select-none">
      {/* Expandable Validation Diagnostics Drawer */}
      {panelOpen && (
        <div
          role="region"
          aria-label="Schema validation diagnostics"
          className="absolute bottom-full left-0 right-0 bg-white dark:bg-[#0F172A] border-t border-slate-200 dark:border-slate-800 shadow-lg flex flex-col max-h-[280px]"
        >
          {/* Drawer Header */}
          <div className="h-10 px-4 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between gap-4 shrink-0">
            <div className="flex items-center gap-3 min-w-0">
              <span className="text-xs font-semibold text-slate-900 dark:text-slate-100 whitespace-nowrap">
                Schema Validation
              </span>
              <div className="flex items-center gap-1 p-0.5 bg-slate-100 dark:bg-slate-900 rounded-md">
                {(
                  [
                    { id: 'all', label: `All (${totalIssues})` },
                    { id: 'error', label: `Errors (${validation.errorCount})` },
                    { id: 'warning', label: `Warnings (${validation.warningCount})` },
                  ] as { id: IssueFilter; label: string }[]
                ).map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    onClick={() => setFilter(tab.id)}
                    className={`px-2.5 py-1 text-[11px] font-medium rounded transition-colors cursor-pointer whitespace-nowrap tabular-nums ${
                      filter === tab.id
                        ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                        : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'
                    }`}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            </div>

            <button
              type="button"
              onClick={() => setPanelOpen(false)}
              title="Close validation panel"
              aria-label="Close validation panel"
              className="p-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>

          {/* Diagnostics Body */}
          <div className="flex-1 overflow-y-auto studio-scroll divide-y divide-slate-100 dark:divide-slate-800/80">
            {totalIssues === 0 ? (
              <div className="px-4 py-6 flex items-start gap-3">
                <CheckCircle2 className="w-4 h-4 text-emerald-600 dark:text-emerald-400 shrink-0 mt-0.5" />
                <div className="space-y-1">
                  <div className="text-xs font-semibold text-slate-900 dark:text-slate-100">
                    All schema checks passed
                  </div>
                  <p className="text-xs text-slate-500 dark:text-slate-400 leading-relaxed">
                    Actively checking for orphaned tables, circular dependencies without nullable foreign keys, duplicate index or constraint names, missing primary keys, foreign key type mismatches, and invalid SET NULL rules.
                  </p>
                </div>
              </div>
            ) : filteredIssues.length === 0 ? (
              <div className="px-4 py-6 text-xs text-slate-500 dark:text-slate-400">
                No {filter === 'error' ? 'errors' : 'warnings'} in the current schema.
              </div>
            ) : (
              filteredIssues.map((issue) => {
                const isError = issue.severity === 'error';
                const canQuickFixNullable =
                  issue.code === 'set-null-on-not-null' &&
                  issue.tableId &&
                  issue.columnId &&
                  onMakeColumnNullable;

                return (
                  <div
                    key={issue.id}
                    className="px-4 py-2.5 flex items-start justify-between gap-4 hover:bg-slate-50/90 dark:hover:bg-slate-800/40 transition-colors"
                  >
                    <button
                      type="button"
                      onClick={() => onFocusIssue(issue)}
                      className="flex items-start gap-2.5 min-w-0 flex-1 text-left cursor-pointer group"
                    >
                      {isError ? (
                        <AlertCircle className="w-4 h-4 text-rose-600 dark:text-rose-400 shrink-0 mt-0.5" />
                      ) : (
                        <AlertTriangle className="w-4 h-4 text-amber-600 dark:text-amber-400 shrink-0 mt-0.5" />
                      )}

                      <div className="min-w-0 flex-1 space-y-0.5">
                        <div className="flex items-center gap-2 text-xs">
                          <span className="font-semibold text-slate-900 dark:text-slate-100 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors truncate">
                            {issue.title}
                          </span>
                          <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
                            ·
                          </span>
                          <span
                            className={`text-[11px] font-medium shrink-0 ${
                              isError
                                ? 'text-rose-600 dark:text-rose-400'
                                : 'text-amber-600 dark:text-amber-400'
                            }`}
                          >
                            {isError ? 'Error' : 'Warning'}
                          </span>
                          <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
                            ·
                          </span>
                          <span className="text-[11px] text-slate-500 dark:text-slate-400 shrink-0">
                            {issue.category}
                          </span>
                        </div>
                        <p className="text-[11.5px] text-slate-600 dark:text-slate-300 leading-snug">
                          {issue.message}{' '}
                          <span className="text-slate-400 dark:text-slate-500">
                            {issue.suggestion}
                          </span>
                        </p>
                      </div>
                    </button>

                    <div className="flex items-center gap-2 shrink-0 pt-0.5">
                      {canQuickFixNullable && (
                        <button
                          type="button"
                          onClick={() =>
                            onMakeColumnNullable(issue.tableId!, issue.columnId!)
                          }
                          className="px-2 py-1 text-[11px] font-medium text-blue-600 dark:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-950/60 rounded transition-colors cursor-pointer whitespace-nowrap"
                        >
                          Make nullable
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => onFocusIssue(issue)}
                        title="Locate on canvas"
                        className="flex items-center gap-1 px-2 py-1 text-[11px] font-medium text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer whitespace-nowrap"
                      >
                        <span>Inspect</span>
                        <ArrowUpRight className="w-3 h-3" />
                      </button>
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>
      )}

      {/* Compact Workspace Status Bar */}
      <footer className="h-8 px-3.5 bg-white dark:bg-[#0F172A] border-t border-slate-200 dark:border-slate-800 flex items-center justify-between gap-4 text-[11.5px]">
        {/* Left: Validation Summary Toggle + Live Top Issue Preview */}
        <div className="flex items-center gap-3 min-w-0">
          <button
            type="button"
            onClick={() => setPanelOpen((v) => !v)}
            aria-expanded={panelOpen}
            className={`flex items-center gap-2 px-2 py-1 -ml-2 rounded transition-colors cursor-pointer whitespace-nowrap shrink-0 ${
              panelOpen
                ? 'bg-slate-100 dark:bg-slate-800 text-slate-900 dark:text-white'
                : 'hover:bg-slate-100 dark:hover:bg-slate-800/80 text-slate-700 dark:text-slate-200'
            }`}
          >
            {totalIssues === 0 ? (
              <>
                <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400 shrink-0" />
                <span className="font-medium text-emerald-700 dark:text-emerald-400">
                  No schema issues
                </span>
              </>
            ) : (
              <>
                {validation.errorCount > 0 && (
                  <span className="flex items-center gap-1 font-medium text-rose-600 dark:text-rose-400 tabular-nums">
                    <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                    <span>
                      {validation.errorCount}{' '}
                      {validation.errorCount === 1 ? 'error' : 'errors'}
                    </span>
                  </span>
                )}
                {validation.errorCount > 0 && validation.warningCount > 0 && (
                  <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
                    ·
                  </span>
                )}
                {validation.warningCount > 0 && (
                  <span className="flex items-center gap-1 font-medium text-amber-600 dark:text-amber-400 tabular-nums">
                    <AlertTriangle className="w-3.5 h-3.5 shrink-0" />
                    <span>
                      {validation.warningCount}{' '}
                      {validation.warningCount === 1 ? 'warning' : 'warnings'}
                    </span>
                  </span>
                )}
              </>
            )}
            {panelOpen ? (
              <ChevronDown className="w-3.5 h-3.5 text-slate-400 shrink-0" />
            ) : (
              <ChevronUp className="w-3.5 h-3.5 text-slate-400 shrink-0" />
            )}
          </button>

          {topIssue && !panelOpen && (
            <button
              type="button"
              onClick={() => onFocusIssue(topIssue)}
              title={`${topIssue.title} — ${topIssue.message}`}
              className="hidden md:flex items-center gap-1.5 min-w-0 text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200 transition-colors cursor-pointer truncate"
            >
              <span className="text-slate-300 dark:text-slate-700 shrink-0" aria-hidden="true">
                /
              </span>
              <span className="truncate">{topIssue.title}</span>
            </button>
          )}
        </div>

        {/* Right: Clean Unboxed Schema Metadata & Live SQL Toggle */}
        <div className="flex items-center gap-2 text-slate-500 dark:text-slate-400 whitespace-nowrap shrink-0 tabular-nums">
          <span className="font-medium text-slate-700 dark:text-slate-300">
            {engineLabel}
          </span>
          <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
            ·
          </span>
          <span>
            {tableCount} {tableCount === 1 ? 'table' : 'tables'}
          </span>
          <span className="hidden sm:inline text-slate-300 dark:text-slate-700" aria-hidden="true">
            ·
          </span>
          <span className="hidden sm:inline">
            {columnCount} {columnCount === 1 ? 'column' : 'columns'}
          </span>
          <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
            ·
          </span>
          <span>
            {relationCount} {relationCount === 1 ? 'relation' : 'relations'}
          </span>
          {indexCount > 0 && (
            <>
              <span className="hidden lg:inline text-slate-300 dark:text-slate-700" aria-hidden="true">
                ·
              </span>
              <span className="hidden lg:inline">
                {indexCount} {indexCount === 1 ? 'index' : 'indexes'}
              </span>
            </>
          )}
          <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
            ·
          </span>
          <button
            type="button"
            onClick={onToggleLiveSql}
            title="Toggle Live SQL DDL Preview Window"
            className={`flex items-center gap-1 px-2 py-0.5 rounded transition-colors cursor-pointer whitespace-nowrap ${
              isLiveSqlOpen
                ? 'bg-blue-50 dark:bg-blue-950/70 text-blue-600 dark:text-blue-400 font-medium'
                : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-white'
            }`}
          >
            <Code2 className="w-3.5 h-3.5 shrink-0" />
            <span>Live SQL</span>
          </button>
        </div>
      </footer>
    </div>
  );
};
