import React from 'react';
import {
  AlertCircle,
  AlertTriangle,
  Copy,
  GripVertical,
  KeyRound,
  Link2,
  Plus,
  Trash2,
} from 'lucide-react';
import { ACCENT_BAR_MAP } from '../data/templates';
import { Column, PendingConnection, SelectionState, Table } from '../types/schema';
import { ValidationIssue } from '../utils/validator';
import {
  AnchorSide,
  COLUMN_ROW_HEIGHT,
  TABLE_FOOTER_HEIGHT,
  TABLE_HEADER_HEIGHT,
  TABLE_WIDTH,
} from '../utils/geometry';

export interface ActiveColumnPorts {
  left: boolean;
  right: boolean;
}

interface TableNodeProps {
  table: Table;
  selection: SelectionState;
  fkColumnIds: Set<string>;
  activeColumnPorts: Map<string, ActiveColumnPorts>;
  validationIssues?: ValidationIssue[];
  pendingConnection: PendingConnection | null;
  isDragging: boolean;
  onStartTableDrag: (e: React.PointerEvent, tableId: string) => void;
  onSelectTable: (tableId: string) => void;
  onSelectColumn: (tableId: string, columnId: string) => void;
  onAddColumn: (tableId: string) => void;
  onDuplicateTable: (tableId: string) => void;
  onDeleteTable: (tableId: string) => void;
  onStartConnection: (
    e: React.PointerEvent,
    tableId: string,
    columnId: string,
    side: AnchorSide
  ) => void;
  onHoverConnectionTarget: (target: { tableId: string; columnId: string } | undefined) => void;
  onCompleteConnection: (tableId: string, columnId: string) => void;
}

export const TableNode: React.FC<TableNodeProps> = ({
  table,
  selection,
  fkColumnIds,
  activeColumnPorts,
  validationIssues = [],
  pendingConnection,
  isDragging,
  onStartTableDrag,
  onSelectTable,
  onSelectColumn,
  onAddColumn,
  onDuplicateTable,
  onDeleteTable,
  onStartConnection,
  onHoverConnectionTarget,
  onCompleteConnection,
}) => {
  const isTableSelected =
    (selection.type === 'table' && selection.tableId === table.id) ||
    (selection.type === 'column' && selection.tableId === table.id);

  const selectedColumnId =
    selection.type === 'column' && selection.tableId === table.id ? selection.columnId : null;

  const isConnecting = pendingConnection !== null;

  const handleHeaderDrop = (e: React.PointerEvent) => {
    if (!pendingConnection) return;
    e.stopPropagation();
    const targetCol = table.columns.find((c) => c.isPrimaryKey) ?? table.columns[0];
    if (targetCol) {
      onCompleteConnection(table.id, targetCol.id);
    }
  };

  return (
    <div
      style={{
        transform: `translate3d(${table.x}px, ${table.y}px, 0)`,
        width: TABLE_WIDTH,
      }}
      onPointerDown={(e) => e.stopPropagation()}
      className={`absolute top-0 left-0 rounded-lg bg-white dark:bg-[#131C2E] border transition-shadow select-none ${
        isTableSelected
          ? 'border-blue-600 dark:border-blue-500 ring-2 ring-blue-600/20 dark:ring-blue-500/25 shadow-md z-20'
          : 'border-slate-200 dark:border-slate-700/90 shadow-sm hover:border-slate-300 dark:hover:border-slate-600 z-10'
      } ${isDragging ? 'shadow-xl cursor-grabbing z-30' : ''}`}
    >
      {/* Top 3px architectural accent strip */}
      <div className={`h-[3px] w-full rounded-t-[7px] ${ACCENT_BAR_MAP[table.color] || 'bg-blue-600'}`} />

      {/* Table Header (drag handle) */}
      <div
        style={{ height: TABLE_HEADER_HEIGHT - 3 }}
        onPointerDown={(e) => {
          onSelectTable(table.id);
          onStartTableDrag(e, table.id);
        }}
        onPointerUp={handleHeaderDrop}
        className="group/header px-3 flex items-center justify-between gap-2 bg-slate-50/80 dark:bg-slate-900/60 border-b border-slate-200/90 dark:border-slate-800 cursor-grab active:cursor-grabbing"
      >
        <div className="flex items-center gap-1.5 min-w-0">
          <GripVertical className="w-3.5 h-3.5 text-slate-400 dark:text-slate-500 shrink-0" />
          <span className="font-mono text-[13px] font-semibold text-slate-900 dark:text-slate-100 truncate">
            {table.name}
          </span>
        </div>

        <div className="flex items-center gap-1 shrink-0">
          {validationIssues.length > 0 && (
            <span
              title={validationIssues.map((i) => `${i.severity.toUpperCase()}: ${i.title}`).join('\n')}
              className={`flex items-center shrink-0 ${
                validationIssues.some((i) => i.severity === 'error')
                  ? 'text-rose-600 dark:text-rose-400'
                  : 'text-amber-600 dark:text-amber-400'
              }`}
            >
              {validationIssues.some((i) => i.severity === 'error') ? (
                <AlertCircle className="w-3.5 h-3.5" />
              ) : (
                <AlertTriangle className="w-3.5 h-3.5" />
              )}
            </span>
          )}
          <span className="text-[11px] font-mono tabular-nums text-slate-400 dark:text-slate-500 mr-1 group-hover/header:hidden">
            {table.columns.length}
          </span>
          <button
            type="button"
            onPointerDown={(e) => e.stopPropagation()}
            onClick={(e) => {
              e.stopPropagation();
              onDuplicateTable(table.id);
            }}
            title="Duplicate table"
            aria-label={`Duplicate ${table.name}`}
            className="hidden group-hover/header:flex p-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 rounded transition-colors cursor-pointer"
          >
            <Copy className="w-3.5 h-3.5" />
          </button>
          <button
            type="button"
            onPointerDown={(e) => e.stopPropagation()}
            onClick={(e) => {
              e.stopPropagation();
              onDeleteTable(table.id);
            }}
            title="Delete table"
            aria-label={`Delete ${table.name}`}
            className="hidden group-hover/header:flex p-1 text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 rounded transition-colors cursor-pointer"
          >
            <Trash2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Columns List */}
      <div className="divide-y divide-slate-100 dark:divide-slate-800/70">
        {table.columns.length === 0 ? (
          <div
            style={{ height: COLUMN_ROW_HEIGHT }}
            className="px-3 flex items-center text-xs text-slate-400 dark:text-slate-500 italic"
          >
            No columns defined
          </div>
        ) : (
          table.columns.map((col: Column) => {
            const isColSelected = selectedColumnId === col.id;
            const isFk = fkColumnIds.has(col.id);
            const activeSides = activeColumnPorts.get(col.id);
            const isTargetHovered =
              pendingConnection?.hoverTarget?.tableId === table.id &&
              pendingConnection?.hoverTarget?.columnId === col.id;
            const isSourceOfPending =
              pendingConnection?.sourceTableId === table.id &&
              pendingConnection?.sourceColumnId === col.id;

            return (
              <div
                key={col.id}
                style={{ height: COLUMN_ROW_HEIGHT }}
                onClick={(e) => {
                  e.stopPropagation();
                  onSelectColumn(table.id, col.id);
                }}
                onPointerEnter={() => {
                  if (isConnecting) {
                    onHoverConnectionTarget({ tableId: table.id, columnId: col.id });
                  }
                }}
                onPointerLeave={() => {
                  if (isConnecting) {
                    onHoverConnectionTarget(undefined);
                  }
                }}
                onPointerUp={(e) => {
                  if (isConnecting) {
                    e.stopPropagation();
                    onCompleteConnection(table.id, col.id);
                  }
                }}
                className={`group/col relative px-3 flex items-center justify-between gap-2 cursor-pointer transition-colors ${
                  isTargetHovered
                    ? 'bg-blue-100/80 dark:bg-blue-900/50 ring-1 ring-inset ring-blue-500'
                    : isColSelected
                    ? 'bg-blue-50/90 dark:bg-blue-950/50'
                    : 'hover:bg-slate-50 dark:hover:bg-slate-800/40'
                }`}
              >
                {/* Left Dynamic Connect Port */}
                <button
                  type="button"
                  title="Drag to connect relation"
                  aria-label={`Connect from ${table.name}.${col.name} left side`}
                  onPointerDown={(e) => {
                    e.stopPropagation();
                    onStartConnection(e, table.id, col.id, 'left');
                  }}
                  className={`absolute -left-[6px] top-1/2 -translate-y-1/2 w-3 h-3 rounded-full border-2 transition-all cursor-crosshair z-30 ${
                    isSourceOfPending && pendingConnection?.sourceSide === 'left'
                      ? 'bg-blue-600 border-white dark:border-slate-900 scale-125 opacity-100'
                      : isTargetHovered
                      ? 'bg-blue-600 border-white dark:border-slate-900 scale-125 opacity-100'
                      : activeSides?.left
                      ? 'bg-blue-600 dark:bg-blue-400 border-white dark:border-[#131C2E] opacity-100'
                      : isConnecting
                      ? 'bg-white dark:bg-slate-800 border-blue-500 opacity-85 hover:scale-125'
                      : 'bg-white dark:bg-slate-800 border-slate-400 dark:border-slate-500 opacity-0 group-hover/col:opacity-100 hover:border-blue-600 hover:scale-125'
                  }`}
                />

                {/* Key Indicator + Column Name */}
                <div className="flex items-center gap-1.5 min-w-0">
                  <span className="w-4 flex items-center justify-center shrink-0">
                    {col.isPrimaryKey ? (
                      <KeyRound className="w-3.5 h-3.5 text-amber-600 dark:text-amber-400" strokeWidth={2.2} />
                    ) : isFk ? (
                      <Link2 className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400" strokeWidth={2.2} />
                    ) : (
                      <span className="w-1.5 h-1.5 rounded-full bg-slate-300 dark:bg-slate-600" />
                    )}
                  </span>

                  <span
                    className={`font-mono text-[12px] truncate ${
                      col.isPrimaryKey
                        ? 'font-semibold text-slate-900 dark:text-white'
                        : 'font-normal text-slate-800 dark:text-slate-200'
                    }`}
                  >
                    {col.name}
                  </span>
                </div>

                {/* Type & Clean Unboxed Constraints */}
                <div className="flex items-center gap-1.5 shrink-0 font-mono text-[11px]">
                  <span className="text-slate-500 dark:text-slate-400 max-w-[92px] truncate">
                    {col.type}
                  </span>
                  {col.isNullable && !col.isPrimaryKey && (
                    <span
                      title="Nullable"
                      className="text-slate-400 dark:text-slate-500 text-[10px]"
                    >
                      ?
                    </span>
                  )}
                  {col.isUnique && !col.isPrimaryKey && (
                    <span
                      title="Unique"
                      className="text-indigo-600 dark:text-indigo-400 text-[10px] font-semibold"
                    >
                      UQ
                    </span>
                  )}
                </div>

                {/* Right Dynamic Connect Port */}
                <button
                  type="button"
                  title="Drag to connect relation"
                  aria-label={`Connect from ${table.name}.${col.name} right side`}
                  onPointerDown={(e) => {
                    e.stopPropagation();
                    onStartConnection(e, table.id, col.id, 'right');
                  }}
                  className={`absolute -right-[6px] top-1/2 -translate-y-1/2 w-3 h-3 rounded-full border-2 transition-all cursor-crosshair z-30 ${
                    isSourceOfPending && pendingConnection?.sourceSide === 'right'
                      ? 'bg-blue-600 border-white dark:border-slate-900 scale-125 opacity-100'
                      : isTargetHovered
                      ? 'bg-blue-600 border-white dark:border-slate-900 scale-125 opacity-100'
                      : activeSides?.right
                      ? 'bg-blue-600 dark:bg-blue-400 border-white dark:border-[#131C2E] opacity-100'
                      : isConnecting
                      ? 'bg-white dark:bg-slate-800 border-blue-500 opacity-85 hover:scale-125'
                      : 'bg-white dark:bg-slate-800 border-slate-400 dark:border-slate-500 opacity-0 group-hover/col:opacity-100 hover:border-blue-600 hover:scale-125'
                  }`}
                />
              </div>
            );
          })
        )}
      </div>

      {/* Footer Quick Add Column */}
      <button
        type="button"
        style={{ height: TABLE_FOOTER_HEIGHT }}
        onClick={(e) => {
          e.stopPropagation();
          onAddColumn(table.id);
        }}
        className="w-full px-3 flex items-center gap-1.5 text-[11.5px] font-medium text-slate-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-slate-50 dark:hover:bg-slate-800/50 border-t border-slate-100 dark:border-slate-800 rounded-b-lg transition-colors cursor-pointer"
      >
        <Plus className="w-3.5 h-3.5" />
        <span>Add column</span>
      </button>
    </div>
  );
};
