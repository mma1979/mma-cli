import React, { useMemo, useState } from 'react';
import { GitFork, Plus, Search, Table2, X } from 'lucide-react';
import { ACCENT_BAR_MAP } from '../data/templates';
import { Relation, SelectionState, Table } from '../types/schema';

type OutlineScope = 'all' | 'tables' | 'relations';

interface OutlineDrawerProps {
  tables: Table[];
  relations: Relation[];
  selection: SelectionState;
  searchInputRef?: React.RefObject<HTMLInputElement | null>;
  onFocusTable: (tableId: string) => void;
  onSelectRelation: (relationId: string) => void;
  onAddTable: () => void;
  onClose: () => void;
}

const HighlightMatch: React.FC<{ text: string; query: string }> = ({ text, query }) => {
  const q = query.trim();
  if (!q) return <>{text}</>;
  const lowerText = text.toLowerCase();
  const lowerQuery = q.toLowerCase();
  const idx = lowerText.indexOf(lowerQuery);
  if (idx === -1) return <>{text}</>;

  return (
    <>
      {text.slice(0, idx)}
      <span className="bg-blue-100 dark:bg-blue-900/70 text-blue-800 dark:text-blue-200 rounded-[2px] px-[1px]">
        {text.slice(idx, idx + q.length)}
      </span>
      {text.slice(idx + q.length)}
    </>
  );
};

export const OutlineDrawer: React.FC<OutlineDrawerProps> = ({
  tables,
  relations,
  selection,
  searchInputRef,
  onFocusTable,
  onSelectRelation,
  onAddTable,
  onClose,
}) => {
  const [query, setQuery] = useState('');
  const [scope, setScope] = useState<OutlineScope>('all');

  const trimmedQuery = query.trim().toLowerCase();
  const isSearching = trimmedQuery.length > 0;

  const tableMap = useMemo(() => {
    const map = new Map<string, Table>();
    tables.forEach((t) => map.set(t.id, t));
    return map;
  }, [tables]);

  const filteredTables = useMemo(() => {
    if (!trimmedQuery) {
      return tables.map((table) => ({ table, matchedColumns: [] as string[] }));
    }
    const matches: { table: Table; matchedColumns: string[] }[] = [];
    for (const table of tables) {
      const nameHit =
        table.name.toLowerCase().includes(trimmedQuery) ||
        (table.schema && table.schema.toLowerCase().includes(trimmedQuery));
      const matchedColumns = table.columns
        .filter((c) => c.name.toLowerCase().includes(trimmedQuery))
        .map((c) => c.name);

      if (nameHit || matchedColumns.length > 0) {
        matches.push({ table, matchedColumns: nameHit ? [] : matchedColumns });
      }
    }
    return matches;
  }, [tables, trimmedQuery]);

  const filteredRelations = useMemo(() => {
    if (!trimmedQuery) return relations;
    return relations.filter((r) => {
      const srcTable = tableMap.get(r.sourceTableId);
      const tgtTable = tableMap.get(r.targetTableId);
      const srcName = srcTable?.name.toLowerCase() ?? '';
      const tgtName = tgtTable?.name.toLowerCase() ?? '';
      const srcCol =
        srcTable?.columns.find((c) => c.id === r.sourceColumnId)?.name.toLowerCase() ?? '';
      const tgtCol =
        tgtTable?.columns.find((c) => c.id === r.targetColumnId)?.name.toLowerCase() ?? '';

      return (
        r.name.toLowerCase().includes(trimmedQuery) ||
        srcName.includes(trimmedQuery) ||
        tgtName.includes(trimmedQuery) ||
        srcCol.includes(trimmedQuery) ||
        tgtCol.includes(trimmedQuery)
      );
    });
  }, [relations, tableMap, trimmedQuery]);

  const showTablesSection = scope === 'all' || scope === 'tables';
  const showRelationsSection = scope === 'all' || scope === 'relations';

  const totalVisibleMatches =
    (showTablesSection ? filteredTables.length : 0) +
    (showRelationsSection ? filteredRelations.length : 0);

  return (
    <aside className="w-64 shrink-0 h-full bg-white dark:bg-[#0F172A] border-r border-slate-200 dark:border-slate-800 flex flex-col select-none z-20">
      {/* Header */}
      <div className="h-11 px-3.5 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between shrink-0">
        <span className="text-xs font-semibold text-slate-800 dark:text-slate-200">
          Schema Outline
        </span>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={onAddTable}
            title="Add table"
            aria-label="Add table"
            className="p-1 text-slate-500 hover:text-blue-600 dark:text-slate-400 dark:hover:text-blue-400 hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
          >
            <Plus className="w-3.5 h-3.5" />
          </button>
          <button
            type="button"
            onClick={onClose}
            title="Close outline"
            aria-label="Close outline"
            className="p-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Search Bar & Scope Filter */}
      <div className="p-2.5 border-b border-slate-200 dark:border-slate-800 space-y-2 shrink-0">
        <div className="relative">
          <Search className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-1/2 -translate-y-1/2 pointer-events-none" />
          <input
            ref={searchInputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Escape' && query) {
                e.stopPropagation();
                setQuery('');
              }
            }}
            placeholder="Search tables & relations..."
            aria-label="Search tables and relations"
            spellCheck={false}
            className="w-full pl-8 pr-7 py-1.5 text-xs bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-700/80 rounded-md text-slate-800 dark:text-slate-200 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:border-blue-500 transition-colors"
          />
          {query && (
            <button
              type="button"
              onClick={() => {
                setQuery('');
                searchInputRef?.current?.focus();
              }}
              title="Clear search (Esc)"
              aria-label="Clear search"
              className="absolute right-1.5 top-1/2 -translate-y-1/2 p-0.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 rounded transition-colors cursor-pointer"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>

        {/* Scope Segmented Filter */}
        <div className="grid grid-cols-3 gap-0.5 p-0.5 bg-slate-100 dark:bg-slate-900 rounded-md">
          {(
            [
              { id: 'all', label: 'All' },
              { id: 'tables', label: 'Tables' },
              { id: 'relations', label: 'Relations' },
            ] as { id: OutlineScope; label: string }[]
          ).map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => setScope(item.id)}
              className={`py-1 text-[11px] font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                scope === item.id
                  ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                  : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'
              }`}
            >
              {item.label}
            </button>
          ))}
        </div>
      </div>

      {/* Results List */}
      <div className="flex-1 overflow-y-auto studio-scroll divide-y divide-slate-200 dark:divide-slate-800">
        {isSearching && totalVisibleMatches === 0 ? (
          <div className="px-4 py-8 text-center space-y-2">
            <div className="text-xs font-medium text-slate-600 dark:text-slate-300">
              No matches for "{query.trim()}"
            </div>
            <button
              type="button"
              onClick={() => setQuery('')}
              className="text-xs font-medium text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
            >
              Clear search
            </button>
          </div>
        ) : (
          <>
            {/* Tables Section */}
            {showTablesSection && (
              <div className="py-2.5 px-2">
                <div className="px-2 mb-1.5 flex items-center justify-between text-[11px] font-medium text-slate-400 dark:text-slate-500">
                  <span>Tables</span>
                  <span className="font-mono tabular-nums">
                    {isSearching ? `${filteredTables.length}/${tables.length}` : tables.length}
                  </span>
                </div>

                {filteredTables.length === 0 ? (
                  <div className="px-2 py-2.5 text-xs text-slate-400 dark:text-slate-500">
                    {isSearching ? 'No matching tables' : 'No tables yet'}
                  </div>
                ) : (
                  <div className="space-y-0.5">
                    {filteredTables.map(({ table: t, matchedColumns }) => {
                      const isSelected =
                        (selection.type === 'table' || selection.type === 'column') &&
                        selection.tableId === t.id;
                      return (
                        <button
                          key={t.id}
                          type="button"
                          onClick={() => onFocusTable(t.id)}
                          className={`w-full px-2 py-1.5 rounded-md text-left transition-colors cursor-pointer ${
                            isSelected
                              ? 'bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300'
                              : 'text-slate-700 dark:text-slate-300 hover:bg-slate-100/80 dark:hover:bg-slate-800/60'
                          }`}
                        >
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex items-center gap-2 min-w-0">
                              <span
                                className={`w-1.5 h-3.5 rounded-[1px] shrink-0 ${
                                  ACCENT_BAR_MAP[t.color] || 'bg-blue-600'
                                }`}
                              />
                              <Table2 className="w-3.5 h-3.5 opacity-60 shrink-0" />
                              <span className="font-mono text-xs truncate">
                                <HighlightMatch text={t.name} query={query} />
                              </span>
                            </div>
                            <span className="font-mono text-[11px] tabular-nums text-slate-400 dark:text-slate-500 shrink-0">
                              {t.columns.length}
                            </span>
                          </div>
                          {matchedColumns.length > 0 && (
                            <div className="pl-7 mt-0.5 font-mono text-[10.5px] text-slate-500 dark:text-slate-400 truncate">
                              col:{' '}
                              <HighlightMatch
                                text={matchedColumns.slice(0, 3).join(', ')}
                                query={query}
                              />
                            </div>
                          )}
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>
            )}

            {/* Relations Section */}
            {showRelationsSection && (
              <div className="py-2.5 px-2">
                <div className="px-2 mb-1.5 flex items-center justify-between text-[11px] font-medium text-slate-400 dark:text-slate-500">
                  <span>Relations</span>
                  <span className="font-mono tabular-nums">
                    {isSearching
                      ? `${filteredRelations.length}/${relations.length}`
                      : relations.length}
                  </span>
                </div>

                {filteredRelations.length === 0 ? (
                  <div className="px-2 py-2.5 text-xs text-slate-400 dark:text-slate-500">
                    {isSearching ? 'No matching relations' : 'No relations defined'}
                  </div>
                ) : (
                  <div className="space-y-0.5">
                    {filteredRelations.map((r) => {
                      const src = tableMap.get(r.sourceTableId);
                      const tgt = tableMap.get(r.targetTableId);
                      const isSelected =
                        selection.type === 'relation' && selection.relationId === r.id;

                      return (
                        <button
                          key={r.id}
                          type="button"
                          onClick={() => onSelectRelation(r.id)}
                          className={`w-full px-2 py-1.5 rounded-md text-left transition-colors cursor-pointer ${
                            isSelected
                              ? 'bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300'
                              : 'text-slate-700 dark:text-slate-300 hover:bg-slate-100/80 dark:hover:bg-slate-800/60'
                          }`}
                        >
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex items-center gap-1.5 min-w-0">
                              <GitFork className="w-3.5 h-3.5 opacity-60 shrink-0" />
                              <span className="font-mono text-[11.5px] truncate">
                                <HighlightMatch text={src?.name ?? '?'} query={query} /> →{' '}
                                <HighlightMatch text={tgt?.name ?? '?'} query={query} />
                              </span>
                            </div>
                            <span className="font-mono text-[10.5px] text-slate-400 dark:text-slate-500 shrink-0">
                              {r.cardinality}
                            </span>
                          </div>
                          {r.name && (
                            <div className="pl-5 mt-0.5 font-mono text-[10.5px] text-slate-400 dark:text-slate-500 truncate">
                              <HighlightMatch text={r.name} query={query} />
                            </div>
                          )}
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </aside>
  );
};
