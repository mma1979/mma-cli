import { Relation, Table } from '../types/schema';

export const TABLE_WIDTH = 272;
export const TABLE_HEADER_HEIGHT = 44;
export const COLUMN_ROW_HEIGHT = 34;
export const TABLE_FOOTER_HEIGHT = 34;
export const GRID_SIZE = 20;

export type AnchorSide = 'left' | 'right';

export interface ComputedRelationGeometry {
  relationId: string;
  sourceSide: AnchorSide;
  targetSide: AnchorSide;
  sx: number;
  sy: number;
  tx: number;
  ty: number;
  pathD: string;
  labelX: number;
  labelY: number;
  sourceEndType: 'one' | 'many';
  targetEndType: 'one' | 'many';
  isSelfRef: boolean;
}

export function getTableHeight(table: Table): number {
  return (
    TABLE_HEADER_HEIGHT +
    Math.max(1, table.columns.length) * COLUMN_ROW_HEIGHT +
    TABLE_FOOTER_HEIGHT
  );
}

export function getColumnAnchorY(table: Table, columnId: string): number {
  const index = table.columns.findIndex((c) => c.id === columnId);
  const safeIndex = index >= 0 ? index : 0;
  return table.y + TABLE_HEADER_HEIGHT + safeIndex * COLUMN_ROW_HEIGHT + COLUMN_ROW_HEIGHT / 2;
}

export function snapToGrid(value: number, enabled: boolean): number {
  if (!enabled) return Math.round(value);
  return Math.round(value / GRID_SIZE) * GRID_SIZE;
}

/**
 * Dynamically calculates optimal anchor sides (left/right) and smooth SVG path
 * for a relation based on the live relative positions of its source and target tables.
 */
export function computeRelationGeometry(
  relation: Relation,
  sourceTable: Table,
  targetTable: Table,
  parallelIndex = 0
): ComputedRelationGeometry {
  const sy = getColumnAnchorY(sourceTable, relation.sourceColumnId);
  const ty = getColumnAnchorY(targetTable, relation.targetColumnId);

  const isSelfRef = sourceTable.id === targetTable.id;

  const sourceEndType: 'one' | 'many' =
    relation.cardinality === '1:1' || relation.cardinality === '1:N' ? 'one' : 'many';
  const targetEndType: 'one' | 'many' =
    relation.cardinality === '1:1' || relation.cardinality === 'N:1' ? 'one' : 'many';

  const stemLen = 18;
  const stagger = parallelIndex * 18;

  if (isSelfRef) {
    const sx = sourceTable.x + TABLE_WIDTH;
    const tx = targetTable.x + TABLE_WIDTH;
    const bulge = 64 + stagger;
    const sStemX = sx + stemLen;
    const tStemX = tx + stemLen;
    const loopX = sx + bulge;

    // If same column, add vertical spread so the loop stays visible
    const adjTy = Math.abs(ty - sy) < 4 ? sy + 14 : ty;

    const pathD = [
      `M ${sx} ${sy}`,
      `L ${sStemX} ${sy}`,
      `C ${loopX} ${sy}, ${loopX} ${adjTy}, ${tStemX} ${adjTy}`,
      `L ${tx} ${adjTy}`,
    ].join(' ');

    return {
      relationId: relation.id,
      sourceSide: 'right',
      targetSide: 'right',
      sx,
      sy,
      tx,
      ty: adjTy,
      pathD,
      labelX: loopX - 6,
      labelY: (sy + adjTy) / 2,
      sourceEndType,
      targetEndType,
      isSelfRef: true,
    };
  }

  const sLeft = sourceTable.x;
  const sRight = sourceTable.x + TABLE_WIDTH;
  const sCenter = sourceTable.x + TABLE_WIDTH / 2;

  const tLeft = targetTable.x;
  const tRight = targetTable.x + TABLE_WIDTH;
  const tCenter = targetTable.x + TABLE_WIDTH / 2;

  const gapThreshold = 36;

  let sourceSide: AnchorSide;
  let targetSide: AnchorSide;

  if (tLeft >= sRight + gapThreshold) {
    // Target is clearly to the right of Source
    sourceSide = 'right';
    targetSide = 'left';
  } else if (sLeft >= tRight + gapThreshold) {
    // Target is clearly to the left of Source
    sourceSide = 'left';
    targetSide = 'right';
  } else if (tCenter >= sCenter) {
    // Tables overlap horizontally; route cleanly around the right side
    sourceSide = 'right';
    targetSide = 'right';
  } else {
    // Tables overlap horizontally; route cleanly around the left side
    sourceSide = 'left';
    targetSide = 'left';
  }

  const sx = sourceSide === 'right' ? sRight : sLeft;
  const tx = targetSide === 'right' ? tRight : tLeft;

  const sDir = sourceSide === 'right' ? 1 : -1;
  const tDir = targetSide === 'right' ? 1 : -1;

  const sStemX = sx + sDir * stemLen;
  const tStemX = tx + tDir * stemLen;

  let pathD: string;
  let labelX: number;
  let labelY: number;

  if (sourceSide !== targetSide) {
    // Opposite sides: smooth S-curve
    const horizDist = Math.abs(tStemX - sStemX);
    const curveOffset = Math.max(42, Math.min(180, horizDist * 0.48)) + stagger * 0.5;

    const cp1x = sStemX + sDir * curveOffset;
    const cp2x = tStemX + tDir * curveOffset;

    pathD = [
      `M ${sx} ${sy}`,
      `L ${sStemX} ${sy}`,
      `C ${cp1x} ${sy}, ${cp2x} ${ty}, ${tStemX} ${ty}`,
      `L ${tx} ${ty}`,
    ].join(' ');

    // Cubic Bezier midpoint at t = 0.5
    labelX = 0.125 * sStemX + 0.375 * cp1x + 0.375 * cp2x + 0.125 * tStemX;
    labelY = (sy + ty) / 2;
  } else {
    // Same side (stacked or overlapping vertically): graceful C-curve outside both cards
    const outerClearance = 56 + stagger;
    const extremeX =
      sourceSide === 'right'
        ? Math.max(sStemX, tStemX) + outerClearance
        : Math.min(sStemX, tStemX) - outerClearance;

    pathD = [
      `M ${sx} ${sy}`,
      `L ${sStemX} ${sy}`,
      `C ${extremeX} ${sy}, ${extremeX} ${ty}, ${tStemX} ${ty}`,
      `L ${tx} ${ty}`,
    ].join(' ');

    labelX = 0.125 * sStemX + 0.75 * extremeX + 0.125 * tStemX;
    labelY = (sy + ty) / 2;
  }

  return {
    relationId: relation.id,
    sourceSide,
    targetSide,
    sx,
    sy,
    tx,
    ty,
    pathD,
    labelX,
    labelY,
    sourceEndType,
    targetEndType,
    isSelfRef: false,
  };
}

/**
 * Builds a live preview curve while the user drags a new relation from a port.
 */
export function computePendingConnectionPath(
  startX: number,
  startY: number,
  sourceSide: AnchorSide,
  currentX: number,
  currentY: number
): string {
  const sDir = sourceSide === 'right' ? 1 : -1;
  const dx = Math.abs(currentX - startX);
  const offset = Math.max(44, Math.min(160, dx * 0.45));
  const cp1x = startX + sDir * offset;
  const targetDir = currentX >= startX ? -1 : 1;
  const cp2x = currentX + targetDir * Math.min(offset, 90);
  return `M ${startX} ${startY} C ${cp1x} ${startY}, ${cp2x} ${currentY}, ${currentX} ${currentY}`;
}

/**
 * Clean topological layered auto-layout that organizes connected tables left-to-right
 * by dependency depth and packs each column neatly without overlap.
 */
export function computeAutoLayout(tables: Table[], relations: Relation[]): Table[] {
  if (tables.length === 0) return tables;

  const inDegree = new Map<string, number>();
  const adjacency = new Map<string, string[]>();

  for (const t of tables) {
    inDegree.set(t.id, 0);
    adjacency.set(t.id, []);
  }

  for (const r of relations) {
    if (r.sourceTableId === r.targetTableId) continue;
    if (!adjacency.has(r.sourceTableId) || !inDegree.has(r.targetTableId)) continue;
    adjacency.get(r.sourceTableId)!.push(r.targetTableId);
    inDegree.set(r.targetTableId, (inDegree.get(r.targetTableId) || 0) + 1);
  }

  // Assign layers via BFS from roots
  const layerMap = new Map<string, number>();
  const queue: string[] = [];

  for (const t of tables) {
    if ((inDegree.get(t.id) || 0) === 0) {
      layerMap.set(t.id, 0);
      queue.push(t.id);
    }
  }

  // Fallback for cycles: seed remaining unvisited nodes
  if (queue.length === 0) {
    layerMap.set(tables[0].id, 0);
    queue.push(tables[0].id);
  }

  let head = 0;
  while (head < queue.length) {
    const curr = queue[head++];
    const currLayer = layerMap.get(curr) || 0;
    for (const next of adjacency.get(curr) || []) {
      const existing = layerMap.get(next);
      const candidate = Math.min(currLayer + 1, 4);
      if (existing === undefined || candidate > existing) {
        layerMap.set(next, candidate);
        if (!queue.includes(next)) {
          queue.push(next);
        }
      }
    }
  }

  // Any disconnected tables get assigned cleanly
  for (const t of tables) {
    if (!layerMap.has(t.id)) {
      layerMap.set(t.id, 0);
    }
  }

  // Group tables by layer
  const layers = new Map<number, Table[]>();
  for (const t of tables) {
    const l = layerMap.get(t.id) || 0;
    if (!layers.has(l)) layers.set(l, []);
    layers.get(l)!.push(t);
  }

  const colGap = 110;
  const rowGap = 56;
  const startX = 60;
  const startY = 60;

  const sortedLayerKeys = Array.from(layers.keys()).sort((a, b) => a - b);
  const newCoords = new Map<string, { x: number; y: number }>();

  sortedLayerKeys.forEach((layerIdx, colPos) => {
    const group = layers.get(layerIdx)!;
    // Wrap long columns to max 3 tables vertically per column
    let cursorY = startY;
    group.forEach((table) => {
      const x = startX + colPos * (TABLE_WIDTH + colGap);
      newCoords.set(table.id, { x, y: cursorY });
      cursorY += getTableHeight(table) + rowGap;
    });
  });

  return tables.map((t) => {
    const pos = newCoords.get(t.id);
    return pos ? { ...t, x: pos.x, y: pos.y } : t;
  });
}
