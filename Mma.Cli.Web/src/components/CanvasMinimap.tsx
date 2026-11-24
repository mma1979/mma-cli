import React, { useMemo, useRef, useState } from 'react';
import { Maximize2, X } from 'lucide-react';
import { TABLE_ACCENT_PALETTE } from '../data/templates';
import { Relation, SelectionState, Table, TableAccentColor, ViewportState } from '../types/schema';
import {
  ComputedRelationGeometry,
  getTableHeight,
  TABLE_HEADER_HEIGHT,
  TABLE_WIDTH,
} from '../utils/geometry';

const MAP_WIDTH = 228;
const MAP_HEIGHT = 142;

const ACCENT_HEX_MAP: Record<TableAccentColor, string> = TABLE_ACCENT_PALETTE.reduce(
  (acc, item) => {
    acc[item.id] = item.dotColor;
    return acc;
  },
  {} as Record<TableAccentColor, string>
);

interface MinimapProjection {
  minX: number;
  minY: number;
  scale: number;
  offsetX: number;
  offsetY: number;
}

interface CanvasMinimapProps {
  tables: Table[];
  computedRelations: { rel: Relation; geom: ComputedRelationGeometry }[];
  selection: SelectionState;
  viewport: ViewportState;
  canvasSize: { width: number; height: number };
  theme: 'light' | 'dark';
  onViewportChange: (next: ViewportState) => void;
  onFitView: () => void;
  onClose: () => void;
}

export const CanvasMinimap: React.FC<CanvasMinimapProps> = ({
  tables,
  computedRelations,
  selection,
  viewport,
  canvasSize,
  theme,
  onViewportChange,
  onFitView,
  onClose,
}) => {
  const [isDragging, setIsDragging] = useState(false);
  const svgRef = useRef<SVGSVGElement | null>(null);

  const dragRef = useRef<{
    pointerId: number;
    projection: MinimapProjection;
    grabOffsetWorldX: number;
    grabOffsetWorldY: number;
  } | null>(null);

  // Current visible canvas viewport in world coordinates
  const vpWorldX = -viewport.x / viewport.zoom;
  const vpWorldY = -viewport.y / viewport.zoom;
  const vpWorldW = canvasSize.width / viewport.zoom;
  const vpWorldH = canvasSize.height / viewport.zoom;

  const liveProjection = useMemo<MinimapProjection>(() => {
    let minX = vpWorldX;
    let minY = vpWorldY;
    let maxX = vpWorldX + vpWorldW;
    let maxY = vpWorldY + vpWorldH;

    tables.forEach((t) => {
      minX = Math.min(minX, t.x);
      minY = Math.min(minY, t.y);
      maxX = Math.max(maxX, t.x + TABLE_WIDTH);
      maxY = Math.max(maxY, t.y + getTableHeight(t));
    });

    const margin = 120;
    minX -= margin;
    minY -= margin;
    maxX += margin;
    maxY += margin;

    const worldW = Math.max(480, maxX - minX);
    const worldH = Math.max(320, maxY - minY);
    const scale = Math.min(MAP_WIDTH / worldW, MAP_HEIGHT / worldH);
    const offsetX = (MAP_WIDTH - worldW * scale) / 2;
    const offsetY = (MAP_HEIGHT - worldH * scale) / 2;

    return { minX, minY, scale, offsetX, offsetY };
  }, [tables, vpWorldX, vpWorldY, vpWorldW, vpWorldH]);

  // Freeze projection while actively dragging so the viewport box never shifts under cursor
  const projection = dragRef.current ? dragRef.current.projection : liveProjection;

  const clientToWorld = (
    clientX: number,
    clientY: number,
    proj: MinimapProjection
  ): { worldX: number; worldY: number } => {
    const rect = svgRef.current?.getBoundingClientRect();
    if (!rect) return { worldX: 0, worldY: 0 };
    const mapX = clientX - rect.left;
    const mapY = clientY - rect.top;
    return {
      worldX: proj.minX + (mapX - proj.offsetX) / proj.scale,
      worldY: proj.minY + (mapY - proj.offsetY) / proj.scale,
    };
  };

  const applyViewportFromWorldTopLeft = (nextWorldX: number, nextWorldY: number) => {
    onViewportChange({
      ...viewport,
      x: -nextWorldX * viewport.zoom,
      y: -nextWorldY * viewport.zoom,
    });
  };

  const handlePointerDown = (e: React.PointerEvent<SVGSVGElement>) => {
    if (e.button !== 0) return;
    e.stopPropagation();

    const proj = liveProjection;
    const { worldX, worldY } = clientToWorld(e.clientX, e.clientY, proj);

    const insideViewport =
      worldX >= vpWorldX &&
      worldX <= vpWorldX + vpWorldW &&
      worldY >= vpWorldY &&
      worldY <= vpWorldY + vpWorldH;

    let grabOffsetWorldX: number;
    let grabOffsetWorldY: number;

    if (insideViewport) {
      // Grabbed the viewport lens directly — preserve grab point
      grabOffsetWorldX = worldX - vpWorldX;
      grabOffsetWorldY = worldY - vpWorldY;
    } else {
      // Clicked outside the lens — recenter viewport immediately and allow drag
      grabOffsetWorldX = vpWorldW / 2;
      grabOffsetWorldY = vpWorldH / 2;
      applyViewportFromWorldTopLeft(worldX - grabOffsetWorldX, worldY - grabOffsetWorldY);
    }

    dragRef.current = {
      pointerId: e.pointerId,
      projection: proj,
      grabOffsetWorldX,
      grabOffsetWorldY,
    };
    setIsDragging(true);
    e.currentTarget.setPointerCapture(e.pointerId);
  };

  const handlePointerMove = (e: React.PointerEvent<SVGSVGElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    e.stopPropagation();

    const { worldX, worldY } = clientToWorld(e.clientX, e.clientY, drag.projection);
    applyViewportFromWorldTopLeft(
      worldX - drag.grabOffsetWorldX,
      worldY - drag.grabOffsetWorldY
    );
  };

  const handlePointerEnd = (e: React.PointerEvent<SVGSVGElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    e.stopPropagation();
    try {
      e.currentTarget.releasePointerCapture(drag.pointerId);
    } catch {
      // Ignore if capture already released
    }
    dragRef.current = null;
    setIsDragging(false);
  };

  const worldTransform = `translate(${projection.offsetX - projection.minX * projection.scale}, ${
    projection.offsetY - projection.minY * projection.scale
  }) scale(${projection.scale})`;

  const relationStroke = theme === 'dark' ? '#475569' : '#CBD5E1';
  const activeRelationStroke = theme === 'dark' ? '#60A5FA' : '#2563EB';
  const tableFill = theme === 'dark' ? '#1E293B' : '#FFFFFF';
  const tableBorder = theme === 'dark' ? '#334155' : '#CBD5E1';

  return (
    <div
      onPointerDown={(e) => e.stopPropagation()}
      className="absolute bottom-4 right-4 bg-white/95 dark:bg-[#111827]/95 backdrop-blur-sm border border-slate-200 dark:border-slate-700/90 rounded-lg shadow-sm z-30 select-none overflow-hidden"
    >
      {/* Compact Minimap Header */}
      <div className="h-7 px-2.5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between gap-2">
        <span className="text-[11px] font-medium text-slate-600 dark:text-slate-300">
          Minimap
        </span>
        <div className="flex items-center gap-0.5">
          <button
            type="button"
            onClick={onFitView}
            title="Fit all tables in view"
            aria-label="Fit all tables in view"
            className="p-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
          >
            <Maximize2 className="w-3 h-3" />
          </button>
          <button
            type="button"
            onClick={onClose}
            title="Hide minimap"
            aria-label="Hide minimap"
            className="p-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
          >
            <X className="w-3 h-3" />
          </button>
        </div>
      </div>

      {/* Interactive SVG Map Surface */}
      <div className="p-1.5 bg-slate-50/90 dark:bg-[#0B1120]/90">
        <svg
          ref={svgRef}
          width={MAP_WIDTH}
          height={MAP_HEIGHT}
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerEnd}
          onPointerCancel={handlePointerEnd}
          role="application"
          aria-label="Schema canvas minimap. Click or drag to navigate the workspace."
          className={`block rounded touch-none ${
            isDragging ? 'cursor-grabbing' : 'cursor-grab'
          }`}
        >
          <g transform={worldTransform}>
            {/* Relation Curves */}
            {computedRelations.map(({ rel, geom }) => {
              const isSelected =
                selection.type === 'relation' && selection.relationId === rel.id;
              return (
                <path
                  key={rel.id}
                  d={geom.pathD}
                  fill="none"
                  stroke={isSelected ? activeRelationStroke : relationStroke}
                  strokeWidth={isSelected ? 1.6 : 1}
                  vectorEffect="non-scaling-stroke"
                />
              );
            })}

            {/* Table Silhouettes with Color-Coded Headers */}
            {tables.map((table) => {
              const height = getTableHeight(table);
              const isSelected =
                (selection.type === 'table' || selection.type === 'column') &&
                selection.tableId === table.id;
              const accentHex = ACCENT_HEX_MAP[table.color] || '#2563EB';

              return (
                <g key={table.id} transform={`translate(${table.x}, ${table.y})`}>
                  <rect
                    x={0}
                    y={0}
                    width={TABLE_WIDTH}
                    height={height}
                    rx={12}
                    fill={tableFill}
                    stroke={isSelected ? '#2563EB' : tableBorder}
                    strokeWidth={isSelected ? 1.75 : 1}
                    vectorEffect="non-scaling-stroke"
                  />
                  <rect
                    x={0}
                    y={0}
                    width={TABLE_WIDTH}
                    height={TABLE_HEADER_HEIGHT}
                    rx={8}
                    fill={accentHex}
                    opacity={isSelected ? 0.95 : 0.78}
                  />
                </g>
              );
            })}

            {/* Draggable Viewport Lens */}
            <rect
              x={vpWorldX}
              y={vpWorldY}
              width={vpWorldW}
              height={vpWorldH}
              rx={16}
              fill={theme === 'dark' ? 'rgba(59, 130, 246, 0.14)' : 'rgba(37, 99, 235, 0.10)'}
              stroke={theme === 'dark' ? '#60A5FA' : '#2563EB'}
              strokeWidth={1.5}
              vectorEffect="non-scaling-stroke"
            />
          </g>
        </svg>
      </div>
    </div>
  );
};
