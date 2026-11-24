import { ENGINE_PROFILES } from '../data/engines';
import { TABLE_ACCENT_PALETTE } from '../data/templates';
import { Column, SchemaModel, Table, TableAccentColor } from '../types/schema';
import {
  AnchorSide,
  COLUMN_ROW_HEIGHT,
  computeRelationGeometry,
  GRID_SIZE,
  TABLE_HEADER_HEIGHT,
  TABLE_WIDTH,
} from './geometry';

export interface DiagramExportOptions {
  theme: 'light' | 'dark';
  transparent: boolean;
  showGrid: boolean;
  showHeader: boolean;
  showCardinality: boolean;
  padding?: number;
}

export interface GeneratedSvgDiagram {
  svg: string;
  width: number;
  height: number;
}

const ACCENT_HEX_MAP: Record<TableAccentColor, string> = TABLE_ACCENT_PALETTE.reduce(
  (acc, item) => {
    acc[item.id] = item.dotColor;
    return acc;
  },
  {} as Record<TableAccentColor, string>
);

function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

function truncateText(text: string, maxChars: number): string {
  if (text.length <= maxChars) return text;
  return `${text.slice(0, Math.max(1, maxChars - 1))}…`;
}

function getExportTableHeight(table: Table): number {
  return TABLE_HEADER_HEIGHT + Math.max(1, table.columns.length) * COLUMN_ROW_HEIGHT;
}

function renderCrowsFootSvg(
  x: number,
  y: number,
  side: AnchorSide,
  type: 'one' | 'many',
  stroke: string
): string {
  const dir = side === 'right' ? 1 : -1;
  if (type === 'one') {
    const bar1X = x + dir * 8;
    const bar2X = x + dir * 13;
    return [
      `<g stroke="${stroke}" stroke-width="1.7" stroke-linecap="round" fill="none">`,
      `<line x1="${bar1X}" y1="${y - 5.5}" x2="${bar1X}" y2="${y + 5.5}" />`,
      `<line x1="${bar2X}" y1="${y - 5.5}" x2="${bar2X}" y2="${y + 5.5}" />`,
      `</g>`,
    ].join('');
  }

  const forkX = x + dir * 14;
  const edgeX = x + dir * 2;
  return [
    `<g stroke="${stroke}" stroke-width="1.7" stroke-linecap="round" fill="none">`,
    `<line x1="${forkX}" y1="${y}" x2="${edgeX}" y2="${y - 6.5}" />`,
    `<line x1="${forkX}" y1="${y}" x2="${edgeX}" y2="${y + 6.5}" />`,
    `<line x1="${forkX}" y1="${y - 5}" x2="${forkX}" y2="${y + 5}" />`,
    `</g>`,
  ].join('');
}

/**
 * Generates a crisp, self-contained vector SVG diagram of the active schema model
 * matching the canvas geometry, flexible connect points, and Crow's Foot notation.
 */
export function generateSchemaSvg(
  schema: SchemaModel,
  options: DiagramExportOptions
): GeneratedSvgDiagram {
  const {
    theme,
    transparent,
    showGrid,
    showHeader,
    showCardinality,
    padding = 52,
  } = options;

  const isDark = theme === 'dark';

  const palette = isDark
    ? {
        bg: '#0B1120',
        gridDot: 'rgba(148, 163, 184, 0.22)',
        cardBg: '#131C2E',
        cardHeaderBg: '#0F172A',
        cardBorder: '#334155',
        rowDivider: '#1E293B',
        titleText: '#F8FAFC',
        subText: '#94A3B8',
        colNamePk: '#FFFFFF',
        colName: '#E2E8F0',
        colType: '#94A3B8',
        nullableText: '#64748B',
        uniqueText: '#818CF8',
        pkIcon: '#FBBF24',
        fkIcon: '#60A5FA',
        colDot: '#475569',
        relStroke: '#64748B',
        portFill: '#60A5FA',
        portBorder: '#131C2E',
        badgeBg: '#131C2E',
        badgeBorder: '#475569',
        badgeText: '#E2E8F0',
        shadowOpacity: '0.35',
      }
    : {
        bg: '#F8FAFC',
        gridDot: 'rgba(100, 116, 139, 0.24)',
        cardBg: '#FFFFFF',
        cardHeaderBg: '#F8FAFC',
        cardBorder: '#CBD5E1',
        rowDivider: '#F1F5F9',
        titleText: '#0F172A',
        subText: '#64748B',
        colNamePk: '#0F172A',
        colName: '#1E293B',
        colType: '#64748B',
        nullableText: '#94A3B8',
        uniqueText: '#4F46E5',
        pkIcon: '#D97706',
        fkIcon: '#2563EB',
        colDot: '#CBD5E1',
        relStroke: '#94A3B8',
        portFill: '#2563EB',
        portBorder: '#FFFFFF',
        badgeBg: '#FFFFFF',
        badgeBorder: '#CBD5E1',
        badgeText: '#334155',
        shadowOpacity: '0.09',
      };

  const tableMap = new Map<string, Table>();
  schema.tables.forEach((t) => tableMap.set(t.id, t));

  // Identify FK columns
  const fkColumnsByTable = new Map<string, Set<string>>();
  for (const rel of schema.relations) {
    if (rel.cardinality === 'N:M') {
      if (!fkColumnsByTable.has(rel.sourceTableId)) fkColumnsByTable.set(rel.sourceTableId, new Set());
      if (!fkColumnsByTable.has(rel.targetTableId)) fkColumnsByTable.set(rel.targetTableId, new Set());
      fkColumnsByTable.get(rel.sourceTableId)!.add(rel.sourceColumnId);
      fkColumnsByTable.get(rel.targetTableId)!.add(rel.targetColumnId);
    } else {
      const fkTableId = rel.cardinality === 'N:1' ? rel.sourceTableId : rel.targetTableId;
      const fkColId = rel.cardinality === 'N:1' ? rel.sourceColumnId : rel.targetColumnId;
      if (!fkColumnsByTable.has(fkTableId)) fkColumnsByTable.set(fkTableId, new Set());
      fkColumnsByTable.get(fkTableId)!.add(fkColId);
    }
  }

  // Compute relation geometry & active ports
  const pairCounter = new Map<string, number>();
  const computedRelations = [];
  const activePorts = new Map<string, { left: boolean; right: boolean }>();

  const markPort = (tableId: string, colId: string, side: AnchorSide) => {
    const key = `${tableId}::${colId}`;
    const prev = activePorts.get(key) || { left: false, right: false };
    activePorts.set(key, {
      left: prev.left || side === 'left',
      right: prev.right || side === 'right',
    });
  };

  for (const rel of schema.relations) {
    const src = tableMap.get(rel.sourceTableId);
    const tgt = tableMap.get(rel.targetTableId);
    if (!src || !tgt) continue;

    const pairKey = [src.id, tgt.id].sort().join('::');
    const idx = pairCounter.get(pairKey) || 0;
    pairCounter.set(pairKey, idx + 1);

    const geom = computeRelationGeometry(rel, src, tgt, idx);
    computedRelations.push({ rel, geom });
    markPort(src.id, rel.sourceColumnId, geom.sourceSide);
    markPort(tgt.id, rel.targetColumnId, geom.targetSide);
  }

  // Compute exact world bounding box across all tables + relation curves
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  if (schema.tables.length === 0) {
    minX = 0;
    minY = 0;
    maxX = 640;
    maxY = 360;
  } else {
    for (const table of schema.tables) {
      const h = getExportTableHeight(table);
      minX = Math.min(minX, table.x);
      minY = Math.min(minY, table.y);
      maxX = Math.max(maxX, table.x + TABLE_WIDTH);
      maxY = Math.max(maxY, table.y + h);
    }

    for (const { geom } of computedRelations) {
      const extraPad = geom.isSelfRef ? 28 : 24;
      minX = Math.min(minX, geom.sx - extraPad, geom.tx - extraPad, geom.labelX - extraPad);
      maxX = Math.max(maxX, geom.sx + extraPad, geom.tx + extraPad, geom.labelX + extraPad);
      minY = Math.min(minY, geom.sy - 16, geom.ty - 16, geom.labelY - 16);
      maxY = Math.max(maxY, geom.sy + 16, geom.ty + 16, geom.labelY + 16);
    }
  }

  const headerSpace = showHeader ? 60 : 0;
  const width = Math.max(360, Math.ceil(maxX - minX + padding * 2));
  const height = Math.max(240, Math.ceil(maxY - minY + padding * 2 + headerSpace));

  const offsetX = padding - minX;
  const offsetY = padding + headerSpace - minY;

  const monoFont = `ui-monospace, 'JetBrains Mono', 'SFMono-Regular', Menlo, Consolas, monospace`;
  const sansFont = `system-ui, -apple-system, 'Plus Jakarta Sans', 'Segoe UI', sans-serif`;

  const engineLabel = ENGINE_PROFILES[schema.engine || 'postgresql'].label;

  const parts: string[] = [];
  parts.push(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" fill="none">`
  );

  // Defs: soft card shadow, dot grid pattern, table clip paths
  parts.push(`<defs>`);
  parts.push(
    `<filter id="cardShadow" x="-8%" y="-6%" width="116%" height="118%">` +
      `<feDropShadow dx="0" dy="2" stdDeviation="4" flood-color="#0F172A" flood-opacity="${palette.shadowOpacity}" />` +
      `</filter>`
  );
  if (!transparent && showGrid) {
    parts.push(
      `<pattern id="dotGrid" width="${GRID_SIZE}" height="${GRID_SIZE}" patternUnits="userSpaceOnUse">` +
        `<circle cx="1.5" cy="1.5" r="1.15" fill="${palette.gridDot}" />` +
        `</pattern>`
    );
  }
  schema.tables.forEach((table, idx) => {
    const h = getExportTableHeight(table);
    parts.push(
      `<clipPath id="tblClip-${idx}">` +
        `<rect x="${table.x}" y="${table.y}" width="${TABLE_WIDTH}" height="${h}" rx="8" ry="8" />` +
        `</clipPath>`
    );
  });
  parts.push(`</defs>`);

  // Background fill + optional dot grid
  if (!transparent) {
    parts.push(`<rect x="0" y="0" width="${width}" height="${height}" fill="${palette.bg}" />`);
    if (showGrid) {
      parts.push(`<rect x="0" y="0" width="${width}" height="${height}" fill="url(#dotGrid)" />`);
    }
  }

  // Optional clean documentation title header
  if (showHeader) {
    const totalCols = schema.tables.reduce((acc, t) => acc + t.columns.length, 0);
    const metaLine = `${engineLabel}  ·  ${schema.tables.length} ${
      schema.tables.length === 1 ? 'table' : 'tables'
    }  ·  ${totalCols} ${totalCols === 1 ? 'column' : 'columns'}  ·  ${
      schema.relations.length
    } ${schema.relations.length === 1 ? 'relation' : 'relations'}`;

    parts.push(
      `<g transform="translate(${padding}, ${padding - 8})">` +
        `<text x="0" y="16" font-family="${sansFont}" font-size="16" font-weight="700" fill="${palette.titleText}">${escapeXml(
          schema.name || 'database_schema'
        )}</text>` +
        `<text x="0" y="34" font-family="${sansFont}" font-size="11.5" font-weight="500" fill="${palette.subText}">${escapeXml(
          metaLine
        )}</text>` +
        `</g>`
    );
  }

  // World coordinate layer
  parts.push(`<g transform="translate(${offsetX}, ${offsetY})">`);

  // 1. Relation curves + Crow's Foot markers
  for (const { rel, geom } of computedRelations) {
    const dashAttr = rel.cardinality === 'N:M' ? ' stroke-dasharray="5 4"' : '';
    parts.push(`<g>`);
    parts.push(
      `<path d="${geom.pathD}" fill="none" stroke="${palette.relStroke}" stroke-width="1.75"${dashAttr} stroke-linecap="round" />`
    );
    parts.push(renderCrowsFootSvg(geom.sx, geom.sy, geom.sourceSide, geom.sourceEndType, palette.relStroke));
    parts.push(renderCrowsFootSvg(geom.tx, geom.ty, geom.targetSide, geom.targetEndType, palette.relStroke));

    if (showCardinality) {
      parts.push(
        `<g transform="translate(${geom.labelX}, ${geom.labelY})">` +
          `<rect x="-18" y="-10" width="36" height="20" rx="4" fill="${palette.badgeBg}" stroke="${palette.badgeBorder}" stroke-width="1" />` +
          `<text x="0" y="3.5" text-anchor="middle" font-family="${monoFont}" font-size="10" font-weight="600" fill="${palette.badgeText}">${escapeXml(
            rel.cardinality
          )}</text>` +
          `</g>`
      );
    }
    parts.push(`</g>`);
  }

  // 2. Table cards
  schema.tables.forEach((table, tIdx) => {
    const h = getExportTableHeight(table);
    const accentHex = ACCENT_HEX_MAP[table.color] || '#2563EB';
    const fkCols = fkColumnsByTable.get(table.id) ?? new Set<string>();

    parts.push(`<g>`);
    // Card outer shadow + surface
    parts.push(
      `<rect x="${table.x}" y="${table.y}" width="${TABLE_WIDTH}" height="${h}" rx="8" ry="8" fill="${palette.cardBg}" filter="url(#cardShadow)" />`
    );

    // Clipped interior
    parts.push(`<g clip-path="url(#tblClip-${tIdx})">`);

    // Header fill
    parts.push(
      `<rect x="${table.x}" y="${table.y}" width="${TABLE_WIDTH}" height="${TABLE_HEADER_HEIGHT}" fill="${palette.cardHeaderBg}" />`
    );
    // Top 3px architectural accent bar
    parts.push(
      `<rect x="${table.x}" y="${table.y}" width="${TABLE_WIDTH}" height="3.5" fill="${accentHex}" />`
    );
    // Header bottom divider
    parts.push(
      `<line x1="${table.x}" y1="${table.y + TABLE_HEADER_HEIGHT}" x2="${
        table.x + TABLE_WIDTH
      }" y2="${table.y + TABLE_HEADER_HEIGHT}" stroke="${palette.cardBorder}" stroke-width="1" />`
    );

    // Table Name & Column Count
    const displayTableName = truncateText(table.name, 26);
    parts.push(
      `<text x="${table.x + 14}" y="${table.y + 27.5}" font-family="${monoFont}" font-size="13" font-weight="700" fill="${palette.titleText}">${escapeXml(
        displayTableName
      )}</text>`
    );
    parts.push(
      `<text x="${table.x + TABLE_WIDTH - 14}" y="${table.y + 27}" text-anchor="end" font-family="${monoFont}" font-size="11" font-weight="500" fill="${palette.subText}">${table.columns.length}</text>`
    );

    // Columns
    if (table.columns.length === 0) {
      parts.push(
        `<text x="${table.x + 14}" y="${
          table.y + TABLE_HEADER_HEIGHT + 21
        }" font-family="${sansFont}" font-size="11.5" font-style="italic" fill="${palette.subText}">No columns defined</text>`
      );
    } else {
      table.columns.forEach((col: Column, cIdx: number) => {
        const rowTop = table.y + TABLE_HEADER_HEIGHT + cIdx * COLUMN_ROW_HEIGHT;
        const rowMidY = rowTop + COLUMN_ROW_HEIGHT / 2;

        if (cIdx > 0) {
          parts.push(
            `<line x1="${table.x}" y1="${rowTop}" x2="${table.x + TABLE_WIDTH}" y2="${rowTop}" stroke="${palette.rowDivider}" stroke-width="1" />`
          );
        }

        const isFk = fkCols.has(col.id);

        // Key indicator glyph
        if (col.isPrimaryKey) {
          // Crisp key glyph
          parts.push(
            `<g transform="translate(${table.x + 13}, ${rowMidY - 5.5})" stroke="${palette.pkIcon}" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" fill="none">` +
              `<circle cx="3.8" cy="5.5" r="2.7" />` +
              `<path d="M 6.5 5.5 L 11.5 5.5 M 9.5 5.5 L 9.5 8 M 11.5 5.5 L 11.5 7.5" />` +
              `</g>`
          );
        } else if (isFk) {
          // Crisp foreign-key diamond/link glyph
          parts.push(
            `<g transform="translate(${table.x + 13}, ${rowMidY - 5})" stroke="${palette.fkIcon}" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" fill="none">` +
              `<path d="M 4.5 7.5 L 2.5 7.5 A 2.5 2.5 0 0 1 2.5 2.5 L 4.5 2.5" />` +
              `<path d="M 7.5 2.5 L 9.5 2.5 A 2.5 2.5 0 0 1 9.5 7.5 L 7.5 7.5" />` +
              `<line x1="4" y1="5" x2="8" y2="5" />` +
              `</g>`
          );
        } else {
          parts.push(
            `<circle cx="${table.x + 18.5}" cy="${rowMidY}" r="2.2" fill="${palette.colDot}" />`
          );
        }

        // Column Name
        const colNameText = truncateText(col.name, 19);
        parts.push(
          `<text x="${table.x + 30}" y="${rowMidY + 4}" font-family="${monoFont}" font-size="11.8" font-weight="${
            col.isPrimaryKey ? '600' : '400'
          }" fill="${col.isPrimaryKey ? palette.colNamePk : palette.colName}">${escapeXml(
            colNameText
          )}</text>`
        );

        // Right-aligned metadata: Type + optional ? / UQ
        let rightCursor = table.x + TABLE_WIDTH - 12;

        if (col.isUnique && !col.isPrimaryKey) {
          parts.push(
            `<text x="${rightCursor}" y="${rowMidY + 3.5}" text-anchor="end" font-family="${monoFont}" font-size="9.5" font-weight="600" fill="${palette.uniqueText}">UQ</text>`
          );
          rightCursor -= 18;
        }

        if (col.isNullable && !col.isPrimaryKey) {
          parts.push(
            `<text x="${rightCursor}" y="${rowMidY + 3.5}" text-anchor="end" font-family="${monoFont}" font-size="10" font-weight="500" fill="${palette.nullableText}">?</text>`
          );
          rightCursor -= 11;
        }

        const displayType = truncateText(col.type, 14);
        parts.push(
          `<text x="${rightCursor}" y="${rowMidY + 3.5}" text-anchor="end" font-family="${monoFont}" font-size="10.8" fill="${palette.colType}">${escapeXml(
            displayType
          )}</text>`
        );
      });
    }

    parts.push(`</g>`);

    // Outer crisp card border (drawn on top of clipped content)
    parts.push(
      `<rect x="${table.x}" y="${table.y}" width="${TABLE_WIDTH}" height="${h}" rx="8" ry="8" fill="none" stroke="${palette.cardBorder}" stroke-width="1" />`
    );

    // Active connection port dots on connected columns
    table.columns.forEach((col: Column, cIdx: number) => {
      const port = activePorts.get(`${table.id}::${col.id}`);
      if (!port) return;
      const rowMidY = table.y + TABLE_HEADER_HEIGHT + cIdx * COLUMN_ROW_HEIGHT + COLUMN_ROW_HEIGHT / 2;
      if (port.left) {
        parts.push(
          `<circle cx="${table.x}" cy="${rowMidY}" r="3.5" fill="${palette.portFill}" stroke="${palette.portBorder}" stroke-width="1.5" />`
        );
      }
      if (port.right) {
        parts.push(
          `<circle cx="${table.x + TABLE_WIDTH}" cy="${rowMidY}" r="3.5" fill="${palette.portFill}" stroke="${palette.portBorder}" stroke-width="1.5" />`
        );
      }
    });

    parts.push(`</g>`);
  });

  parts.push(`</g>`);
  parts.push(`</svg>`);

  return {
    svg: parts.join('\n'),
    width,
    height,
  };
}

/**
 * Rasterizes a generated SVG diagram onto an HTML5 Canvas at the requested pixel scale
 * and returns a PNG Blob for downloading or clipboard copying.
 */
export function renderSvgToPngBlob(
  svgMarkup: string,
  width: number,
  height: number,
  scale = 2
): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const svgBlob = new Blob([svgMarkup], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);
    const img = new Image();

    img.onload = () => {
      try {
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(width * scale);
        canvas.height = Math.round(height * scale);
        const ctx = canvas.getContext('2d');
        if (!ctx) {
          URL.revokeObjectURL(url);
          reject(new Error('Canvas 2D context is unavailable.'));
          return;
        }
        ctx.scale(scale, scale);
        ctx.drawImage(img, 0, 0, width, height);
        URL.revokeObjectURL(url);

        canvas.toBlob(
          (blob) => {
            if (!blob) {
              reject(new Error('Failed to encode PNG image.'));
              return;
            }
            resolve(blob);
          },
          'image/png',
          1.0
        );
      } catch (err) {
        URL.revokeObjectURL(url);
        reject(err instanceof Error ? err : new Error('Failed to render PNG.'));
      }
    };

    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Failed to load SVG into image renderer.'));
    };

    img.src = url;
  });
}

export function downloadBlobFile(filename: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}
