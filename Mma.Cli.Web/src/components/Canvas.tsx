import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Map as MapIcon,
  Maximize2,
  Minus,
  PanelLeft,
  PanelRight,
  Plus,
  Table2,
} from 'lucide-react';
import {
  PendingConnection,
  Relation,
  SelectionState,
  Table,
  ViewportState,
} from '../types/schema';
import {
  AnchorSide,
  computePendingConnectionPath,
  computeRelationGeometry,
  getColumnAnchorY,
  getTableHeight,
  GRID_SIZE,
  snapToGrid,
  TABLE_WIDTH,
} from '../utils/geometry';
import { ActiveColumnPorts, TableNode } from './TableNode';
import { CanvasMinimap } from './CanvasMinimap';
import { SchemaValidationResult } from '../utils/validator';

interface CanvasProps {
  tables: Table[];
  relations: Relation[];
  selection: SelectionState;
  viewport: ViewportState;
  snapGrid: boolean;
  showOutline: boolean;
  showInspector: boolean;
  theme: 'light' | 'dark';
  validation: SchemaValidationResult;
  onViewportChange: (next: ViewportState) => void;
  onSelect: (selection: SelectionState) => void;
  onMoveTableLive: (tableId: string, x: number, y: number) => void;
  onCommitTableMove: (tableId: string, startX: number, startY: number, endX: number, endY: number) => void;
  onCreateRelation: (
    sourceTableId: string,
    sourceColumnId: string,
    targetTableId: string,
    targetColumnId: string
  ) => void;
  onAddColumn: (tableId: string) => void;
  onDuplicateTable: (tableId: string) => void;
  onDeleteTable: (tableId: string) => void;
  onAddFirstTable: () => void;
  onLoadStarterTemplate: () => void;
  onToggleOutline: () => void;
  onToggleInspector: () => void;
  canvasContainerRef: React.RefObject<HTMLDivElement | null>;
}

const MIN_ZOOM = 0.35;
const MAX_ZOOM = 2.0;

/**
 * Renders crisp Crow's Foot notation glyphs directly at the table edge anchor
 * oriented along the dynamic connect side (left or right).
 */
const CrowsFootEnd: React.FC<{
  x: number;
  y: number;
  side: AnchorSide;
  type: 'one' | 'many';
  stroke: string;
  strokeWidth: number;
}> = ({ x, y, side, type, stroke, strokeWidth }) => {
  const dir = side === 'right' ? 1 : -1;

  if (type === 'one') {
    const bar1X = x + dir * 8;
    const bar2X = x + dir * 13;
    return (
      <g stroke={stroke} strokeWidth={strokeWidth} strokeLinecap="round" fill="none">
        <line x1={bar1X} y1={y - 5.5} x2={bar1X} y2={y + 5.5} />
        <line x1={bar2X} y1={y - 5.5} x2={bar2X} y2={y + 5.5} />
      </g>
    );
  }

  // Crow's foot "many" prongs fanning into the table edge
  const forkX = x + dir * 14;
  const edgeX = x + dir * 2;
  return (
    <g stroke={stroke} strokeWidth={strokeWidth} strokeLinecap="round" fill="none">
      <line x1={forkX} y1={y} x2={edgeX} y2={y - 6.5} />
      <line x1={forkX} y1={y} x2={edgeX} y2={y + 6.5} />
      <line x1={forkX} y1={y - 5} x2={forkX} y2={y + 5} />
    </g>
  );
};

export const Canvas: React.FC<CanvasProps> = ({
  tables,
  relations,
  selection,
  viewport,
  snapGrid,
  showOutline,
  showInspector,
  theme,
  validation,
  onViewportChange,
  onSelect,
  onMoveTableLive,
  onCommitTableMove,
  onCreateRelation,
  onAddColumn,
  onDuplicateTable,
  onDeleteTable,
  onAddFirstTable,
  onLoadStarterTemplate,
  onToggleOutline,
  onToggleInspector,
  canvasContainerRef,
}) => {
  const [hoveredRelationId, setHoveredRelationId] = useState<string | null>(null);
  const [showMinimap, setShowMinimap] = useState(false);
  const [pendingConnection, setPendingConnection] = useState<PendingConnection | null>(null);
  const [draggingTableId, setDraggingTableId] = useState<string | null>(null);
  const [isPanning, setIsPanning] = useState(false);
  const [canvasSize, setCanvasSize] = useState<{ width: number; height: number }>({
    width: 1000,
    height: 700,
  });

  useEffect(() => {
    const el = canvasContainerRef.current;
    if (!el) return;
    const updateSize = () => {
      setCanvasSize({ width: el.clientWidth, height: el.clientHeight });
    };
    updateSize();
    const ro = new ResizeObserver(updateSize);
    ro.observe(el);
    return () => ro.disconnect();
  }, [canvasContainerRef]);

  useEffect(() => {
    if (!pendingConnection) return;
    const handleWindowPointerUp = () => {
      setPendingConnection(null);
    };
    window.addEventListener('pointerup', handleWindowPointerUp);
    return () => window.removeEventListener('pointerup', handleWindowPointerUp);
  }, [pendingConnection]);

  const dragStateRef = useRef<{
    type: 'pan' | 'table';
    pointerId: number;
    startClientX: number;
    startClientY: number;
    originX: number;
    originY: number;
    tableId?: string;
    latestX?: number;
    latestY?: number;
  } | null>(null);

  const tableMap = useMemo(() => {
    const map = new Map<string, Table>();
    tables.forEach((t) => map.set(t.id, t));
    return map;
  }, [tables]);

  // Identify foreign key columns so TableNode renders FK icon cleanly
  const fkColumnsByTable = useMemo(() => {
    const map = new Map<string, Set<string>>();
    for (const rel of relations) {
      if (rel.cardinality === 'N:M') {
        if (!map.has(rel.sourceTableId)) map.set(rel.sourceTableId, new Set());
        if (!map.has(rel.targetTableId)) map.set(rel.targetTableId, new Set());
        map.get(rel.sourceTableId)!.add(rel.sourceColumnId);
        map.get(rel.targetTableId)!.add(rel.targetColumnId);
      } else {
        const fkTableId = rel.cardinality === 'N:1' ? rel.sourceTableId : rel.targetTableId;
        const fkColId = rel.cardinality === 'N:1' ? rel.sourceColumnId : rel.targetColumnId;
        if (!map.has(fkTableId)) map.set(fkTableId, new Set());
        map.get(fkTableId)!.add(fkColId);
      }
    }
    return map;
  }, [relations]);

  // Compute flexible connect points & SVG paths for every relation
  const { computedRelations, activePortsByTable } = useMemo(() => {
    const pairCounter = new Map<string, number>();
    const results = [];
    const portsByTable = new Map<string, Map<string, ActiveColumnPorts>>();

    const markPort = (tableId: string, colId: string, side: AnchorSide) => {
      if (!portsByTable.has(tableId)) portsByTable.set(tableId, new Map());
      const colMap = portsByTable.get(tableId)!;
      const prev = colMap.get(colId) || { left: false, right: false };
      colMap.set(colId, {
        left: prev.left || side === 'left',
        right: prev.right || side === 'right',
      });
    };

    for (const rel of relations) {
      const src = tableMap.get(rel.sourceTableId);
      const tgt = tableMap.get(rel.targetTableId);
      if (!src || !tgt) continue;

      const pairKey = [src.id, tgt.id].sort().join('::');
      const idx = pairCounter.get(pairKey) || 0;
      pairCounter.set(pairKey, idx + 1);

      const geom = computeRelationGeometry(rel, src, tgt, idx);
      results.push({ rel, geom });

      markPort(src.id, rel.sourceColumnId, geom.sourceSide);
      markPort(tgt.id, rel.targetColumnId, geom.targetSide);
    }

    return { computedRelations: results, activePortsByTable: portsByTable };
  }, [relations, tableMap]);

  const screenToCanvas = useCallback(
    (clientX: number, clientY: number) => {
      const rect = canvasContainerRef.current?.getBoundingClientRect();
      if (!rect) return { x: 0, y: 0 };
      return {
        x: (clientX - rect.left - viewport.x) / viewport.zoom,
        y: (clientY - rect.top - viewport.y) / viewport.zoom,
      };
    },
    [canvasContainerRef, viewport.x, viewport.y, viewport.zoom]
  );

  // Wheel zoom anchored at pointer
  useEffect(() => {
    const el = canvasContainerRef.current;
    if (!el) return;

    const handleWheel = (e: WheelEvent) => {
      e.preventDefault();
      const rect = el.getBoundingClientRect();
      const pointerX = e.clientX - rect.left;
      const pointerY = e.clientY - rect.top;

      const zoomFactor = e.deltaY < 0 ? 1.09 : 1 / 1.09;
      const nextZoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, viewport.zoom * zoomFactor));
      const scaleRatio = nextZoom / viewport.zoom;

      const nextX = pointerX - (pointerX - viewport.x) * scaleRatio;
      const nextY = pointerY - (pointerY - viewport.y) * scaleRatio;

      onViewportChange({
        x: nextX,
        y: nextY,
        zoom: Number(nextZoom.toFixed(3)),
      });
    };

    el.addEventListener('wheel', handleWheel, { passive: false });
    return () => el.removeEventListener('wheel', handleWheel);
  }, [canvasContainerRef, viewport, onViewportChange]);

  const handleCanvasPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0 && e.button !== 1) return;
    onSelect({ type: 'none' });
    setIsPanning(true);
    dragStateRef.current = {
      type: 'pan',
      pointerId: e.pointerId,
      startClientX: e.clientX,
      startClientY: e.clientY,
      originX: viewport.x,
      originY: viewport.y,
    };
    e.currentTarget.setPointerCapture(e.pointerId);
  };

  const handleStartTableDrag = useCallback(
    (e: React.PointerEvent, tableId: string) => {
      if (e.button !== 0) return;
      const table = tableMap.get(tableId);
      if (!table) return;
      setDraggingTableId(tableId);
      dragStateRef.current = {
        type: 'table',
        pointerId: e.pointerId,
        startClientX: e.clientX,
        startClientY: e.clientY,
        originX: table.x,
        originY: table.y,
        tableId,
        latestX: table.x,
        latestY: table.y,
      };
      canvasContainerRef.current?.setPointerCapture(e.pointerId);
    },
    [tableMap, canvasContainerRef]
  );

  const handleStartConnection = useCallback(
    (e: React.PointerEvent, tableId: string, columnId: string, side: AnchorSide) => {
      const table = tableMap.get(tableId);
      if (!table) return;
      const startX = side === 'right' ? table.x + TABLE_WIDTH : table.x;
      const startY = getColumnAnchorY(table, columnId);
      const cursor = screenToCanvas(e.clientX, e.clientY);

      setPendingConnection({
        sourceTableId: tableId,
        sourceColumnId: columnId,
        sourceSide: side,
        startX,
        startY,
        currentX: cursor.x,
        currentY: cursor.y,
      });
    },
    [tableMap, screenToCanvas]
  );

  const handleCanvasPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (pendingConnection) {
      const pt = screenToCanvas(e.clientX, e.clientY);
      setPendingConnection((prev) =>
        prev ? { ...prev, currentX: pt.x, currentY: pt.y } : null
      );
      return;
    }

    const drag = dragStateRef.current;
    if (!drag) return;

    if (drag.type === 'pan') {
      const dx = e.clientX - drag.startClientX;
      const dy = e.clientY - drag.startClientY;
      onViewportChange({
        ...viewport,
        x: drag.originX + dx,
        y: drag.originY + dy,
      });
    } else if (drag.type === 'table' && drag.tableId) {
      const dx = (e.clientX - drag.startClientX) / viewport.zoom;
      const dy = (e.clientY - drag.startClientY) / viewport.zoom;
      const nextX = snapToGrid(drag.originX + dx, snapGrid);
      const nextY = snapToGrid(drag.originY + dy, snapGrid);
      drag.latestX = nextX;
      drag.latestY = nextY;
      onMoveTableLive(drag.tableId, nextX, nextY);
    }
  };

  const handleCanvasPointerUp = (e: React.PointerEvent<HTMLDivElement>) => {
    if (pendingConnection) {
      if (pendingConnection.hoverTarget) {
        onCreateRelation(
          pendingConnection.sourceTableId,
          pendingConnection.sourceColumnId,
          pendingConnection.hoverTarget.tableId,
          pendingConnection.hoverTarget.columnId
        );
      }
      setPendingConnection(null);
      return;
    }

    const drag = dragStateRef.current;
    if (!drag) return;

    if (drag.type === 'table' && drag.tableId) {
      const endX = drag.latestX ?? drag.originX;
      const endY = drag.latestY ?? drag.originY;
      if (endX !== drag.originX || endY !== drag.originY) {
        onCommitTableMove(drag.tableId, drag.originX, drag.originY, endX, endY);
      }
      setDraggingTableId(null);
    } else if (drag.type === 'pan') {
      setIsPanning(false);
    }

    try {
      e.currentTarget.releasePointerCapture(drag.pointerId);
    } catch {
      // Ignore if capture was already released
    }
    dragStateRef.current = null;
  };

  const handleCompleteConnection = useCallback(
    (targetTableId: string, targetColumnId: string) => {
      if (!pendingConnection) return;
      onCreateRelation(
        pendingConnection.sourceTableId,
        pendingConnection.sourceColumnId,
        targetTableId,
        targetColumnId
      );
      setPendingConnection(null);
    },
    [pendingConnection, onCreateRelation]
  );

  const handleZoomStep = (delta: number) => {
    const el = canvasContainerRef.current;
    const centerX = el ? el.clientWidth / 2 : 500;
    const centerY = el ? el.clientHeight / 2 : 350;
    const nextZoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, Number((viewport.zoom + delta).toFixed(2))));
    const ratio = nextZoom / viewport.zoom;
    onViewportChange({
      x: centerX - (centerX - viewport.x) * ratio,
      y: centerY - (centerY - viewport.y) * ratio,
      zoom: nextZoom,
    });
  };

  const handleFitView = useCallback(() => {
    const el = canvasContainerRef.current;
    if (!el || tables.length === 0) {
      onViewportChange({ x: 40, y: 40, zoom: 1 });
      return;
    }

    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;

    tables.forEach((t) => {
      minX = Math.min(minX, t.x);
      minY = Math.min(minY, t.y);
      maxX = Math.max(maxX, t.x + TABLE_WIDTH);
      maxY = Math.max(maxY, t.y + getTableHeight(t));
    });

    const contentW = Math.max(200, maxX - minX);
    const contentH = Math.max(200, maxY - minY);
    const pad = 72;
    const scaleX = (el.clientWidth - pad * 2) / contentW;
    const scaleY = (el.clientHeight - pad * 2) / contentH;
    const nextZoom = Math.min(1.15, Math.max(MIN_ZOOM, Math.min(scaleX, scaleY)));

    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;

    onViewportChange({
      x: el.clientWidth / 2 - centerX * nextZoom,
      y: el.clientHeight / 2 - centerY * nextZoom,
      zoom: Number(nextZoom.toFixed(3)),
    });
  }, [canvasContainerRef, tables, onViewportChange]);

  const gridSpacing = GRID_SIZE * viewport.zoom;
  const dotColor = theme === 'dark' ? 'rgba(148, 163, 184, 0.16)' : 'rgba(100, 116, 139, 0.22)';

  const defaultStroke = theme === 'dark' ? '#64748B' : '#94A3B8';
  const activeStroke = theme === 'dark' ? '#60A5FA' : '#2563EB';
  const hoverStroke = theme === 'dark' ? '#93C5FD' : '#3B82F6';

  return (
    <div
      ref={canvasContainerRef}
      onPointerDown={handleCanvasPointerDown}
      onPointerMove={handleCanvasPointerMove}
      onPointerUp={handleCanvasPointerUp}
      className={`relative flex-1 h-full overflow-hidden touch-none bg-slate-50 dark:bg-[#0B1120] ${
        isPanning ? 'cursor-grabbing' : pendingConnection ? 'cursor-crosshair' : 'cursor-grab'
      }`}
      style={{
        backgroundImage: `radial-gradient(${dotColor} 1.25px, transparent 1.25px)`,
        backgroundSize: `${gridSpacing}px ${gridSpacing}px`,
        backgroundPosition: `${viewport.x}px ${viewport.y}px`,
      }}
    >
      {/* Transformed World Space */}
      <div
        className="absolute top-0 left-0 origin-top-left will-change-transform"
        style={{
          transform: `translate3d(${viewport.x}px, ${viewport.y}px, 0) scale(${viewport.zoom})`,
        }}
      >
        {/* SVG Relations Layer */}
        <svg
          className="absolute top-0 left-0 overflow-visible pointer-events-none"
          width="1"
          height="1"
        >
          {computedRelations.map(({ rel, geom }) => {
            const isSelected =
              selection.type === 'relation' && selection.relationId === rel.id;
            const isHovered = hoveredRelationId === rel.id;
            const isConnectedToSelectedTable =
              (selection.type === 'table' || selection.type === 'column') &&
              (rel.sourceTableId === selection.tableId ||
                rel.targetTableId === selection.tableId);

            const relIssues = validation.byRelationId.get(rel.id) ?? [];
            const hasRelError = relIssues.some((i) => i.severity === 'error');
            const hasRelWarning = !hasRelError && relIssues.length > 0;

            const strokeColor = isSelected
              ? activeStroke
              : hasRelError
              ? theme === 'dark'
                ? '#FB7185'
                : '#E11D48'
              : hasRelWarning
              ? theme === 'dark'
                ? '#FBBF24'
                : '#D97706'
              : isHovered || isConnectedToSelectedTable
              ? hoverStroke
              : defaultStroke;

            const strokeWidth = isSelected
              ? 2.4
              : hasRelError || hasRelWarning || isHovered || isConnectedToSelectedTable
              ? 2
              : 1.65;

            return (
              <g key={rel.id} className="pointer-events-auto">
                {/* Wide transparent hit area for effortless clicking */}
                <path
                  d={geom.pathD}
                  fill="none"
                  stroke="transparent"
                  strokeWidth={16}
                  className="cursor-pointer"
                  onPointerDown={(e) => {
                    e.stopPropagation();
                    onSelect({ type: 'relation', relationId: rel.id });
                  }}
                  onPointerEnter={() => setHoveredRelationId(rel.id)}
                  onPointerLeave={() => setHoveredRelationId(null)}
                />

                {/* Visible relation path */}
                <path
                  d={geom.pathD}
                  fill="none"
                  stroke={strokeColor}
                  strokeWidth={strokeWidth}
                  strokeDasharray={rel.cardinality === 'N:M' ? '5 4' : undefined}
                  className="pointer-events-none transition-colors duration-150"
                />

                {/* Source Crow's Foot / One Endpoint */}
                <CrowsFootEnd
                  x={geom.sx}
                  y={geom.sy}
                  side={geom.sourceSide}
                  type={geom.sourceEndType}
                  stroke={strokeColor}
                  strokeWidth={strokeWidth}
                />

                {/* Target Crow's Foot / One Endpoint */}
                <CrowsFootEnd
                  x={geom.tx}
                  y={geom.ty}
                  side={geom.targetSide}
                  type={geom.targetEndType}
                  stroke={strokeColor}
                  strokeWidth={strokeWidth}
                />

                {/* Cardinality Midpoint Label */}
                {(isSelected || isHovered || isConnectedToSelectedTable) && (
                  <g
                    transform={`translate(${geom.labelX}, ${geom.labelY})`}
                    onPointerDown={(e) => {
                      e.stopPropagation();
                      onSelect({ type: 'relation', relationId: rel.id });
                    }}
                    className="cursor-pointer"
                  >
                    <rect
                      x={-18}
                      y={-10}
                      width={36}
                      height={20}
                      rx={4}
                      className={
                        isSelected
                          ? 'fill-blue-600'
                          : 'fill-white dark:fill-[#131C2E] stroke-slate-300 dark:stroke-slate-600'
                      }
                      strokeWidth={isSelected ? 0 : 1}
                    />
                    <text
                      x={0}
                      y={3.5}
                      textAnchor="middle"
                      className={`font-mono text-[10px] font-semibold select-none ${
                        isSelected
                          ? 'fill-white'
                          : 'fill-slate-700 dark:fill-slate-200'
                      }`}
                    >
                      {rel.cardinality}
                    </text>
                  </g>
                )}
              </g>
            );
          })}

          {/* Live rubber-band preview curve when dragging a new relation */}
          {pendingConnection && (
            <g className="pointer-events-none">
              <path
                d={computePendingConnectionPath(
                  pendingConnection.startX,
                  pendingConnection.startY,
                  pendingConnection.sourceSide,
                  pendingConnection.currentX,
                  pendingConnection.currentY
                )}
                fill="none"
                stroke={activeStroke}
                strokeWidth={2.2}
                strokeDasharray="6 4"
              />
              <circle
                cx={pendingConnection.currentX}
                cy={pendingConnection.currentY}
                r={4.5}
                fill={activeStroke}
              />
            </g>
          )}
        </svg>

        {/* Table Cards */}
        {tables.map((table) => (
          <TableNode
            key={table.id}
            table={table}
            selection={selection}
            fkColumnIds={fkColumnsByTable.get(table.id) ?? new Set()}
            activeColumnPorts={activePortsByTable.get(table.id) ?? new Map()}
            validationIssues={validation.byTableId.get(table.id)}
            pendingConnection={pendingConnection}
            isDragging={draggingTableId === table.id}
            onStartTableDrag={handleStartTableDrag}
            onSelectTable={(id) => onSelect({ type: 'table', tableId: id })}
            onSelectColumn={(tableId, columnId) =>
              onSelect({ type: 'column', tableId, columnId })
            }
            onAddColumn={onAddColumn}
            onDuplicateTable={onDuplicateTable}
            onDeleteTable={onDeleteTable}
            onStartConnection={handleStartConnection}
            onHoverConnectionTarget={(hoverTarget) =>
              setPendingConnection((prev) => (prev ? { ...prev, hoverTarget } : null))
            }
            onCompleteConnection={handleCompleteConnection}
          />
        ))}
      </div>

      {/* Empty Canvas State */}
      {tables.length === 0 && (
        <div
          onPointerDown={(e) => e.stopPropagation()}
          className="absolute inset-0 flex items-center justify-center pointer-events-none"
        >
          <div className="pointer-events-auto max-w-sm w-full mx-4 p-6 bg-white dark:bg-[#131C2E] border border-slate-200 dark:border-slate-800 rounded-xl shadow-sm text-center">
            <div className="w-10 h-10 rounded-lg bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 flex items-center justify-center mx-auto mb-3">
              <Table2 className="w-5 h-5" />
            </div>
            <h2 className="text-base font-semibold text-slate-900 dark:text-slate-100">
              Empty schema workspace
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-1.5 leading-relaxed">
              Create your first database table or load a starter schema to model tables, drag relations, and export SQL or JSON.
            </p>
            <div className="mt-5 flex items-center justify-center gap-2.5">
              <button
                type="button"
                onClick={onAddFirstTable}
                className="px-3.5 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-md transition-colors cursor-pointer"
              >
                Add First Table
              </button>
              <button
                type="button"
                onClick={onLoadStarterTemplate}
                className="px-3.5 py-2 text-xs font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-md transition-colors cursor-pointer"
              >
                Load Sample
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Subtle connection helper toast while actively dragging a relation */}
      {pendingConnection && (
        <div className="pointer-events-none absolute top-[68px] left-1/2 -translate-x-1/2 px-3 py-1.5 rounded-md bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 text-xs font-medium shadow-md z-30">
          Drop onto any target column or table header to create a relation
        </div>
      )}

      {/* Floating Bottom-Left Workspace Controls */}
      <div
        onPointerDown={(e) => e.stopPropagation()}
        className="absolute bottom-4 left-4 flex items-center gap-1 p-1 bg-white/95 dark:bg-[#111827]/95 backdrop-blur-sm border border-slate-200 dark:border-slate-700/90 rounded-lg shadow-sm z-30 select-none"
      >
        <button
          type="button"
          onClick={onToggleOutline}
          title={showOutline ? 'Hide schema outline' : 'Show schema outline'}
          aria-label="Toggle schema outline"
          className={`p-1.5 rounded-md transition-colors cursor-pointer ${
            showOutline
              ? 'bg-blue-50 dark:bg-blue-950/70 text-blue-600 dark:text-blue-400'
              : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800'
          }`}
        >
          <PanelLeft className="w-4 h-4" />
        </button>

        <div className="h-4 w-px bg-slate-200 dark:bg-slate-700 mx-0.5" aria-hidden="true" />

        <button
          type="button"
          onClick={() => handleZoomStep(-0.12)}
          title="Zoom out"
          aria-label="Zoom out"
          className="p-1.5 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
        >
          <Minus className="w-3.5 h-3.5" />
        </button>

        <button
          type="button"
          onClick={() => onViewportChange({ ...viewport, zoom: 1 })}
          title="Reset zoom to 100%"
          className="px-1.5 py-1 font-mono text-[11px] font-medium tabular-nums text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md min-w-[46px] text-center cursor-pointer"
        >
          {Math.round(viewport.zoom * 100)}%
        </button>

        <button
          type="button"
          onClick={() => handleZoomStep(0.12)}
          title="Zoom in"
          aria-label="Zoom in"
          className="p-1.5 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
        >
          <Plus className="w-3.5 h-3.5" />
        </button>

        <button
          type="button"
          onClick={handleFitView}
          title="Fit schema to viewport"
          aria-label="Fit view"
          className="p-1.5 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
        >
          <Maximize2 className="w-3.5 h-3.5" />
        </button>

        <div className="h-4 w-px bg-slate-200 dark:bg-slate-700 mx-0.5" aria-hidden="true" />

        <button
          type="button"
          onClick={() => setShowMinimap((v) => !v)}
          title={showMinimap ? 'Hide minimap' : 'Show minimap'}
          aria-label="Toggle minimap"
          className={`p-1.5 rounded-md transition-colors cursor-pointer ${
            showMinimap
              ? 'bg-blue-50 dark:bg-blue-950/70 text-blue-600 dark:text-blue-400'
              : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800'
          }`}
        >
          <MapIcon className="w-3.5 h-3.5" />
        </button>

        <button
          type="button"
          onClick={onToggleInspector}
          title={showInspector ? 'Hide property inspector' : 'Show property inspector'}
          aria-label="Toggle property inspector"
          className={`p-1.5 rounded-md transition-colors cursor-pointer ${
            showInspector
              ? 'bg-blue-50 dark:bg-blue-950/70 text-blue-600 dark:text-blue-400'
              : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800'
          }`}
        >
          <PanelRight className="w-4 h-4" />
        </button>
      </div>

      {/* Interactive Click-and-Drag Canvas Minimap */}
      {showMinimap && tables.length > 0 && (
        <CanvasMinimap
          tables={tables}
          computedRelations={computedRelations}
          selection={selection}
          viewport={viewport}
          canvasSize={canvasSize}
          theme={theme}
          onViewportChange={onViewportChange}
          onFitView={handleFitView}
          onClose={() => setShowMinimap(false)}
        />
      )}
    </div>
  );
};
