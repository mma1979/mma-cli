import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  Check,
  Code2,
  Copy,
  Crosshair,
  Download,
  FileJson,
  GripHorizontal,
  ImageDown,
  Loader2,
  Maximize2,
  Minimize2,
  PictureInPicture2,
  RotateCcw,
  Upload,
  X,
} from 'lucide-react';
import { resolveSupportedDatabaseEngine } from '../config/featureFlags';
import { SUPPORTED_ENGINE_LIST } from '../data/engines';
import { SchemaModel, SelectionState, SqlDialect } from '../types/schema';
import {
  downloadBlobFile,
  generateSchemaSvg,
  renderSvgToPngBlob,
} from '../utils/diagramExporter';
import {
  downloadTextFile,
  generateJsonSchema,
  generateSqlDDL,
  parseImportedJsonSchema,
} from '../utils/exporters';
import {
  computeSqlLineDiff,
  findTableSqlRange,
  SqlTokenKind,
  tokenizeSqlLine,
} from '../utils/sqlHighlight';

export type ExportModalMode = 'sql' | 'json' | 'image' | 'import' | null;

interface ExportModalProps {
  mode: ExportModalMode;
  schema: SchemaModel;
  selection: SelectionState;
  workspaceTheme: 'light' | 'dark';
  showInspector: boolean;
  isLiveWindow: boolean;
  onToggleLiveWindow: (next: boolean) => void;
  onModeChange: (mode: ExportModalMode) => void;
  onImportSchema: (model: SchemaModel) => void;
  onClose: () => void;
}

const DIALECTS: { id: SqlDialect; label: string }[] = SUPPORTED_ENGINE_LIST.map((e) => ({
  id: e.id,
  label: e.label,
}));

const TOKEN_CLASS_MAP: Record<SqlTokenKind, string> = {
  comment: 'text-slate-500 italic',
  keyword: 'text-sky-400 font-medium',
  type: 'text-amber-300',
  string: 'text-emerald-300',
  number: 'text-violet-300',
  identifier: 'text-slate-100',
  plain: 'text-slate-300',
};

const SqlHighlightedLine: React.FC<{ line: string }> = React.memo(({ line }) => {
  const tokens = useMemo(() => tokenizeSqlLine(line), [line]);
  return (
    <span className="whitespace-pre">
      {tokens.map((tok, idx) => (
        <span key={idx} className={TOKEN_CLASS_MAP[tok.kind]}>
          {tok.text}
        </span>
      ))}
    </span>
  );
});

const DEFAULT_FLOAT_W = 540;
const DEFAULT_FLOAT_H = 380;
const MIN_FLOAT_W = 380;
const MIN_FLOAT_H = 240;

export const ExportModal: React.FC<ExportModalProps> = ({
  mode,
  schema,
  selection,
  workspaceTheme,
  showInspector,
  isLiveWindow,
  onToggleLiveWindow,
  onModeChange,
  onImportSchema,
  onClose,
}) => {
  const [dialect, setDialect] = useState<SqlDialect>(
    resolveSupportedDatabaseEngine(schema.engine)
  );
  const [includeDropTable, setIncludeDropTable] = useState(false);
  const [includeComments, setIncludeComments] = useState(true);
  const [quoteIdentifiers, setQuoteIdentifiers] = useState(true);
  const [copied, setCopied] = useState(false);

  // Live SQL Preview Window states
  const [followSelection, setFollowSelection] = useState(true);
  const [isMinimized, setIsMinimized] = useState(false);
  const [floatPos, setFloatPos] = useState<{ x: number; y: number }>(() => {
    if (typeof window === 'undefined') return { x: 40, y: 120 };
    const rightReserve = showInspector ? 356 : 24;
    return {
      x: Math.max(24, window.innerWidth - rightReserve - DEFAULT_FLOAT_W),
      y: Math.max(76, window.innerHeight - DEFAULT_FLOAT_H - 52),
    };
  });
  const [floatSize, setFloatSize] = useState<{ width: number; height: number }>({
    width: DEFAULT_FLOAT_W,
    height: DEFAULT_FLOAT_H,
  });

  // Diff baseline for real-time SQL change tracking
  const [baselineSql, setBaselineSql] = useState<string | null>(null);
  const [lastUpdatedLabel, setLastUpdatedLabel] = useState<string>('');
  const prevSqlRef = useRef<string>('');
  const codeContainerRef = useRef<HTMLDivElement | null>(null);
  const lineRefs = useRef<Map<number, HTMLDivElement>>(new Map());

  // Image export options
  const [imageFormat, setImageFormat] = useState<'png' | 'svg'>('png');
  const [imageTheme, setImageTheme] = useState<'light' | 'dark'>(workspaceTheme);
  const [imageScale, setImageScale] = useState<1 | 2 | 3>(2);
  const [imageTransparent, setImageTransparent] = useState(false);
  const [imageShowGrid, setImageShowGrid] = useState(true);
  const [imageShowHeader, setImageShowHeader] = useState(true);
  const [imageShowCardinality, setImageShowCardinality] = useState(true);
  const [imageBusy, setImageBusy] = useState(false);
  const [imageError, setImageError] = useState<string | null>(null);

  const [importText, setImportText] = useState('');
  const [importError, setImportError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Keep dialect synced when workspace engine changes or SQL modal opens
  useEffect(() => {
    if (mode === 'sql') {
      setDialect(resolveSupportedDatabaseEngine(schema.engine));
    } else if (mode === 'image') {
      setImageTheme(workspaceTheme);
      setImageError(null);
    }
  }, [schema.engine, workspaceTheme, mode]);

  const sqlOutput = useMemo(
    () =>
      generateSqlDDL(schema, {
        dialect,
        includeDropTable,
        includeComments,
        quoteIdentifiers,
      }),
    [schema, dialect, includeDropTable, includeComments, quoteIdentifiers]
  );

  const jsonOutput = useMemo(() => generateJsonSchema(schema), [schema]);

  // Initialize / clear diff baseline when SQL preview opens or closes
  useEffect(() => {
    if (mode === 'sql') {
      setBaselineSql((prev) => (prev === null ? sqlOutput : prev));
    } else {
      setBaselineSql(null);
      setIsMinimized(false);
    }
  }, [mode]); // eslint-disable-line react-hooks/exhaustive-deps

  // Track real-time update timestamp when SQL changes while open
  useEffect(() => {
    if (mode !== 'sql') {
      prevSqlRef.current = sqlOutput;
      return;
    }
    if (prevSqlRef.current && prevSqlRef.current !== sqlOutput) {
      const now = new Date();
      setLastUpdatedLabel(
        now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      );
    }
    prevSqlRef.current = sqlOutput;
  }, [sqlOutput, mode]);

  const sqlLines = useMemo(() => sqlOutput.split('\n'), [sqlOutput]);
  const baselineLines = useMemo(
    () => (baselineSql !== null ? baselineSql.split('\n') : sqlLines),
    [baselineSql, sqlLines]
  );

  const sqlDiff = useMemo(
    () => computeSqlLineDiff(baselineLines, sqlLines),
    [baselineLines, sqlLines]
  );

  // Locate currently selected table's CREATE TABLE block in SQL DDL
  const selectedTableName = useMemo(() => {
    if (selection.type === 'table' || selection.type === 'column') {
      return schema.tables.find((t) => t.id === selection.tableId)?.name ?? null;
    }
    return null;
  }, [selection, schema.tables]);

  const selectedTableRange = useMemo(
    () => (selectedTableName ? findTableSqlRange(sqlLines, selectedTableName) : null),
    [sqlLines, selectedTableName]
  );

  // Auto-scroll to selected table's DDL block when Follow Selection is enabled in Live Window
  useEffect(() => {
    if (mode !== 'sql' || !isLiveWindow || isMinimized || !followSelection) return;
    if (!selectedTableRange) return;
    const rowEl = lineRefs.current.get(selectedTableRange.start);
    if (rowEl && codeContainerRef.current) {
      rowEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  }, [mode, isLiveWindow, isMinimized, followSelection, selectedTableRange]);

  // Reposition floating window cleanly into viewport when detaching
  useEffect(() => {
    if (!isLiveWindow || typeof window === 'undefined') return;
    setFloatPos((prev) => ({
      x: Math.min(Math.max(12, prev.x), Math.max(12, window.innerWidth - floatSize.width - 12)),
      y: Math.min(Math.max(56, prev.y), Math.max(56, window.innerHeight - 120)),
    }));
  }, [isLiveWindow, floatSize.width]);

  const diagramResult = useMemo(
    () =>
      generateSchemaSvg(schema, {
        theme: imageTheme,
        transparent: imageTransparent,
        showGrid: imageShowGrid,
        showHeader: imageShowHeader,
        showCardinality: imageShowCardinality,
      }),
    [
      schema,
      imageTheme,
      imageTransparent,
      imageShowGrid,
      imageShowHeader,
      imageShowCardinality,
    ]
  );

  const [previewUrl, setPreviewUrl] = useState<string | null>(null);

  useEffect(() => {
    if (mode !== 'image') {
      setPreviewUrl(null);
      return;
    }
    const blob = new Blob([diagramResult.svg], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    setPreviewUrl(url);
    return () => {
      URL.revokeObjectURL(url);
    };
  }, [mode, diagramResult.svg]);

  // Drag handler for floating Live SQL Preview Window
  const handleWindowDragStart = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return;
    const target = e.target as HTMLElement;
    if (target.closest('button') || target.closest('select') || target.closest('input')) {
      return;
    }
    e.preventDefault();
    const startX = e.clientX;
    const startY = e.clientY;
    const originX = floatPos.x;
    const originY = floatPos.y;

    const handleMove = (ev: PointerEvent) => {
      const maxX = Math.max(12, window.innerWidth - floatSize.width - 12);
      const maxY = Math.max(56, window.innerHeight - (isMinimized ? 48 : 120));
      setFloatPos({
        x: Math.min(maxX, Math.max(12, originX + (ev.clientX - startX))),
        y: Math.min(maxY, Math.max(12, originY + (ev.clientY - startY))),
      });
    };

    const handleUp = () => {
      window.removeEventListener('pointermove', handleMove);
      window.removeEventListener('pointerup', handleUp);
    };

    window.addEventListener('pointermove', handleMove);
    window.addEventListener('pointerup', handleUp);
  };

  // Resize handler (bottom-right corner) for floating Live SQL Preview Window
  const handleWindowResizeStart = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    const startX = e.clientX;
    const startY = e.clientY;
    const originW = floatSize.width;
    const originH = floatSize.height;

    const handleMove = (ev: PointerEvent) => {
      const maxW = Math.max(MIN_FLOAT_W, window.innerWidth - floatPos.x - 12);
      const maxH = Math.max(MIN_FLOAT_H, window.innerHeight - floatPos.y - 44);
      setFloatSize({
        width: Math.min(maxW, Math.max(MIN_FLOAT_W, originW + (ev.clientX - startX))),
        height: Math.min(maxH, Math.max(MIN_FLOAT_H, originH + (ev.clientY - startY))),
      });
    };

    const handleUp = () => {
      window.removeEventListener('pointermove', handleMove);
      window.removeEventListener('pointerup', handleUp);
    };

    window.addEventListener('pointermove', handleMove);
    window.addEventListener('pointerup', handleUp);
  };

  if (!mode) return null;

  const activeText =
    mode === 'sql' ? sqlOutput : mode === 'json' ? jsonOutput : diagramResult.svg;
  const safeFileBase = (schema.name || 'schema').replace(/[^a-zA-Z0-9_-]/g, '_');

  const markCopied = () => {
    setCopied(true);
    setTimeout(() => setCopied(false), 1800);
  };

  const handleCopyText = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      markCopied();
    } catch {
      const textarea = document.createElement('textarea');
      textarea.value = text;
      document.body.appendChild(textarea);
      textarea.select();
      document.execCommand('copy');
      document.body.removeChild(textarea);
      markCopied();
    }
  };

  const handleCopy = async () => {
    if (mode === 'image' && imageFormat === 'png') {
      setImageBusy(true);
      setImageError(null);
      try {
        const pngBlob = await renderSvgToPngBlob(
          diagramResult.svg,
          diagramResult.width,
          diagramResult.height,
          imageScale
        );
        if (typeof ClipboardItem !== 'undefined' && navigator.clipboard?.write) {
          await navigator.clipboard.write([new ClipboardItem({ 'image/png': pngBlob })]);
          markCopied();
        } else {
          setImageError(
            'PNG clipboard copy is not supported in this browser. Use Download .png instead.'
          );
        }
      } catch (err) {
        setImageError(
          err instanceof Error
            ? err.message
            : 'Unable to copy PNG to clipboard. Use Download .png instead.'
        );
      } finally {
        setImageBusy(false);
      }
      return;
    }

    await handleCopyText(activeText);
  };

  const handleDownload = async () => {
    if (mode === 'sql') {
      downloadTextFile(`${safeFileBase}_${dialect}.sql`, sqlOutput, 'text/sql;charset=utf-8');
      return;
    }
    if (mode === 'json') {
      downloadTextFile(`${safeFileBase}.json`, jsonOutput, 'application/json;charset=utf-8');
      return;
    }
    if (mode === 'image') {
      setImageError(null);
      if (imageFormat === 'svg') {
        downloadTextFile(
          `${safeFileBase}_diagram.svg`,
          diagramResult.svg,
          'image/svg+xml;charset=utf-8'
        );
        return;
      }

      setImageBusy(true);
      try {
        const pngBlob = await renderSvgToPngBlob(
          diagramResult.svg,
          diagramResult.width,
          diagramResult.height,
          imageScale
        );
        downloadBlobFile(`${safeFileBase}_diagram_${imageScale}x.png`, pngBlob);
      } catch (err) {
        setImageError(err instanceof Error ? err.message : 'Failed to export PNG image.');
      } finally {
        setImageBusy(false);
      }
    }
  };

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const content = String(reader.result ?? '');
      setImportText(content);
      setImportError(null);
    };
    reader.readAsText(file);
  };

  const handleConfirmImport = () => {
    const { model, error } = parseImportedJsonSchema(importText);
    if (error || !model) {
      setImportError(error || 'Failed to parse JSON.');
      return;
    }
    onImportSchema(model);
    setImportText('');
    setImportError(null);
    onClose();
  };

  const codeLines = mode === 'sql' ? sqlLines : activeText.split('\n');
  const exportedPixelW =
    imageFormat === 'png' ? diagramResult.width * imageScale : diagramResult.width;
  const exportedPixelH =
    imageFormat === 'png' ? diagramResult.height * imageScale : diagramResult.height;

  const hasDiffChanges = sqlDiff.addedCount > 0 || sqlDiff.removedCount > 0;

  // ================= FLOATING NON-BLOCKING LIVE SQL PREVIEW WINDOW =================
  if (mode === 'sql' && isLiveWindow) {
    return (
      <section
        aria-label="Live SQL DDL Preview Window"
        style={{
          left: floatPos.x,
          top: floatPos.y,
          width: floatSize.width,
          height: isMinimized ? 'auto' : floatSize.height,
        }}
        className="fixed z-40 bg-white dark:bg-[#0F172A] border border-slate-300 dark:border-slate-700 rounded-xl shadow-2xl flex flex-col overflow-hidden"
      >
        {/* Draggable Window Title Bar */}
        <div
          onPointerDown={handleWindowDragStart}
          className="h-10 px-3 border-b border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/90 flex items-center justify-between gap-2 cursor-grab active:cursor-grabbing select-none shrink-0"
        >
          <div className="flex items-center gap-2 min-w-0">
            <GripHorizontal className="w-3.5 h-3.5 text-slate-400 shrink-0" />
            <Code2 className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400 shrink-0" />
            <span className="text-xs font-semibold text-slate-900 dark:text-slate-100 whitespace-nowrap">
              Live SQL Preview
            </span>
            <span className="text-slate-300 dark:text-slate-700" aria-hidden="true">
              ·
            </span>
            <span className="text-[11px] font-mono text-slate-500 dark:text-slate-400 truncate">
              {DIALECTS.find((d) => d.id === dialect)?.label}
            </span>
          </div>

          <div className="flex items-center gap-0.5 shrink-0">
            <button
              type="button"
              onClick={() => setFollowSelection((v) => !v)}
              title={
                followSelection
                  ? 'Following selected table in SQL preview'
                  : 'Click to follow selected table'
              }
              aria-label="Follow selected table"
              className={`p-1.5 rounded transition-colors cursor-pointer ${
                followSelection
                  ? 'bg-blue-50 dark:bg-blue-950/70 text-blue-600 dark:text-blue-400'
                  : 'text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-200/70 dark:hover:bg-slate-800'
              }`}
            >
              <Crosshair className="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              onClick={() => setIsMinimized((v) => !v)}
              title={isMinimized ? 'Expand preview window' : 'Collapse preview window'}
              aria-label={isMinimized ? 'Expand preview window' : 'Collapse preview window'}
              className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-200/70 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
            >
              {isMinimized ? (
                <Maximize2 className="w-3.5 h-3.5" />
              ) : (
                <Minimize2 className="w-3.5 h-3.5" />
              )}
            </button>
            <button
              type="button"
              onClick={() => {
                setIsMinimized(false);
                onToggleLiveWindow(false);
              }}
              title="Open full Export modal"
              aria-label="Open full Export modal"
              className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-200/70 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
            >
              <PictureInPicture2 className="w-3.5 h-3.5" />
            </button>
            <button
              type="button"
              onClick={onClose}
              title="Close live SQL preview"
              aria-label="Close live SQL preview"
              className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-200/70 dark:hover:bg-slate-800 rounded transition-colors cursor-pointer"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        {!isMinimized && (
          <>
            {/* Compact Options & Real-Time Diff Bar */}
            <div className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 bg-white dark:bg-[#0F172A] flex flex-wrap items-center justify-between gap-2 text-[11px] shrink-0">
              <div className="flex items-center gap-2.5 flex-wrap">
                <select
                  value={dialect}
                  onChange={(e) => setDialect(e.target.value as SqlDialect)}
                  aria-label="SQL dialect"
                  className="px-2 py-1 text-[11px] font-medium bg-slate-100 dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded text-slate-800 dark:text-slate-200 focus:outline-none focus:border-blue-500 cursor-pointer"
                >
                  {DIALECTS.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.label}
                    </option>
                  ))}
                </select>

                <label className="flex items-center gap-1 text-slate-600 dark:text-slate-300 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={includeDropTable}
                    onChange={(e) => setIncludeDropTable(e.target.checked)}
                    className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                  />
                  <span>DROP</span>
                </label>
                <label className="flex items-center gap-1 text-slate-600 dark:text-slate-300 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={includeComments}
                    onChange={(e) => setIncludeComments(e.target.checked)}
                    className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                  />
                  <span>Comments</span>
                </label>
                <label className="flex items-center gap-1 text-slate-600 dark:text-slate-300 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={quoteIdentifiers}
                    onChange={(e) => setQuoteIdentifiers(e.target.checked)}
                    className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                  />
                  <span>Quotes</span>
                </label>
              </div>

              {hasDiffChanges && (
                <div className="flex items-center gap-2 font-mono tabular-nums">
                  {sqlDiff.addedCount > 0 && (
                    <span className="text-emerald-600 dark:text-emerald-400 font-medium">
                      +{sqlDiff.addedCount}
                    </span>
                  )}
                  {sqlDiff.removedCount > 0 && (
                    <span className="text-rose-600 dark:text-rose-400 font-medium">
                      -{sqlDiff.removedCount}
                    </span>
                  )}
                  <button
                    type="button"
                    onClick={() => setBaselineSql(sqlOutput)}
                    title="Reset diff baseline to current SQL"
                    className="flex items-center gap-1 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 cursor-pointer"
                  >
                    <RotateCcw className="w-3 h-3" />
                  </button>
                </div>
              )}
            </div>

            {/* Live Syntax-Highlighted SQL Viewport */}
            <div
              ref={codeContainerRef}
              className="flex-1 overflow-auto bg-[#0B1120] text-slate-200 studio-scroll"
            >
              <div className="py-3 px-2 font-mono text-[11.5px] leading-[1.6] min-w-max">
                {sqlLines.map((line, i) => {
                  const isChanged = sqlDiff.changedLineIndices.has(i);
                  const isInSelectedTable =
                    followSelection &&
                    selectedTableRange !== null &&
                    i >= selectedTableRange.start &&
                    i <= selectedTableRange.end;

                  return (
                    <div
                      key={i}
                      ref={(el) => {
                        if (el) lineRefs.current.set(i, el);
                        else lineRefs.current.delete(i);
                      }}
                      className={`flex gap-3 px-2 rounded-xs transition-colors ${
                        isChanged
                          ? 'bg-emerald-500/15'
                          : isInSelectedTable
                          ? 'bg-blue-500/10'
                          : 'hover:bg-white/[0.03]'
                      }`}
                    >
                      <span className="w-7 text-right select-none text-slate-600 tabular-nums shrink-0">
                        {i + 1}
                      </span>
                      <span
                        aria-hidden="true"
                        className={`w-2.5 select-none text-center shrink-0 ${
                          isChanged ? 'text-emerald-400 font-semibold' : 'text-transparent'
                        }`}
                      >
                        +
                      </span>
                      <SqlHighlightedLine line={line} />
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Compact Footer + Resize Handle */}
            <div className="relative h-10 px-3 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-[#0F172A] flex items-center justify-between gap-2 shrink-0">
              <div className="text-[11px] text-slate-500 dark:text-slate-400 font-mono tabular-nums truncate">
                {sqlLines.length} lines
                {lastUpdatedLabel ? ` · synced ${lastUpdatedLabel}` : ' · live sync'}
              </div>

              <div className="flex items-center gap-1.5 pr-3 shrink-0">
                <button
                  type="button"
                  onClick={handleCopy}
                  className="flex items-center gap-1 px-2.5 py-1 text-[11px] font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200/80 dark:hover:bg-slate-700 rounded transition-colors cursor-pointer whitespace-nowrap"
                >
                  {copied ? (
                    <>
                      <Check className="w-3 h-3 text-emerald-600 dark:text-emerald-400" />
                      <span>Copied</span>
                    </>
                  ) : (
                    <>
                      <Copy className="w-3 h-3" />
                      <span>Copy</span>
                    </>
                  )}
                </button>
                <button
                  type="button"
                  onClick={handleDownload}
                  className="flex items-center gap-1 px-2.5 py-1 text-[11px] font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded transition-colors cursor-pointer whitespace-nowrap"
                >
                  <Download className="w-3 h-3" />
                  <span>.sql</span>
                </button>
              </div>

              {/* Bottom-Right Resize Grip */}
              <div
                onPointerDown={handleWindowResizeStart}
                title="Drag to resize preview window"
                className="absolute right-0 bottom-0 w-4 h-4 cursor-nwse-resize flex items-end justify-end p-1 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300"
              >
                <svg width="8" height="8" viewBox="0 0 8 8" fill="none" aria-hidden="true">
                  <path
                    d="M7 1L1 7M7 4.5L4.5 7"
                    stroke="currentColor"
                    strokeWidth="1.2"
                    strokeLinecap="round"
                  />
                </svg>
              </div>
            </div>
          </>
        )}
      </section>
    );
  }

  // ================= STANDARD FULL EXPORT MODAL =================
  return (
    <div
      onClick={onClose}
      className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-[2px] flex items-center justify-center p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-4xl max-h-[88vh] bg-white dark:bg-[#0F172A] border border-slate-200 dark:border-slate-700 rounded-xl shadow-2xl flex flex-col overflow-hidden"
      >
        {/* Modal Header */}
        <div className="px-5 py-3.5 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between gap-4">
          <div className="flex items-center gap-1 p-1 bg-slate-100 dark:bg-slate-900 rounded-lg overflow-x-auto">
            <button
              type="button"
              onClick={() => onModeChange('sql')}
              className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors cursor-pointer whitespace-nowrap shrink-0 ${
                mode === 'sql'
                  ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                  : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
              }`}
            >
              <Code2 className="w-3.5 h-3.5" />
              <span>Export SQL</span>
            </button>
            <button
              type="button"
              onClick={() => onModeChange('json')}
              className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors cursor-pointer whitespace-nowrap shrink-0 ${
                mode === 'json'
                  ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                  : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
              }`}
            >
              <FileJson className="w-3.5 h-3.5" />
              <span>Export JSON</span>
            </button>
            <button
              type="button"
              onClick={() => onModeChange('image')}
              className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors cursor-pointer whitespace-nowrap shrink-0 ${
                mode === 'image'
                  ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                  : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
              }`}
            >
              <ImageDown className="w-3.5 h-3.5" />
              <span>Export Image</span>
            </button>
            <button
              type="button"
              onClick={() => onModeChange('import')}
              className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors cursor-pointer whitespace-nowrap shrink-0 ${
                mode === 'import'
                  ? 'bg-white dark:bg-slate-800 text-slate-900 dark:text-white shadow-xs'
                  : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
              }`}
            >
              <Upload className="w-3.5 h-3.5" />
              <span>Import JSON</span>
            </button>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            {mode === 'sql' && (
              <button
                type="button"
                onClick={() => {
                  setBaselineSql(sqlOutput);
                  setIsMinimized(false);
                  onToggleLiveWindow(true);
                }}
                title="Detach into a floating live SQL preview window while editing the canvas"
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-950/60 hover:bg-blue-100/80 dark:hover:bg-blue-950 rounded-md transition-colors cursor-pointer whitespace-nowrap"
              >
                <PictureInPicture2 className="w-3.5 h-3.5" />
                <span>Live Preview Window</span>
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              aria-label="Close modal"
              className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer shrink-0"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* SQL Options Strip */}
        {mode === 'sql' && (
          <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/40 flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-1 p-0.5 bg-slate-200/70 dark:bg-slate-800 rounded-md">
              {DIALECTS.map((d) => (
                <button
                  key={d.id}
                  type="button"
                  onClick={() => setDialect(d.id)}
                  className={`px-2.5 py-1 text-xs font-medium rounded transition-colors cursor-pointer whitespace-nowrap ${
                    dialect === d.id
                      ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                      : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                  }`}
                >
                  {d.label}
                </button>
              ))}
            </div>

            <div className="flex flex-wrap items-center gap-4 text-xs text-slate-600 dark:text-slate-300">
              <label className="flex items-center gap-1.5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={includeDropTable}
                  onChange={(e) => setIncludeDropTable(e.target.checked)}
                  className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Include DROP TABLE</span>
              </label>
              <label className="flex items-center gap-1.5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={includeComments}
                  onChange={(e) => setIncludeComments(e.target.checked)}
                  className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Comments</span>
              </label>
              <label className="flex items-center gap-1.5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={quoteIdentifiers}
                  onChange={(e) => setQuoteIdentifiers(e.target.checked)}
                  className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Quote identifiers</span>
              </label>
            </div>
          </div>
        )}

        {/* Image Export Options Strip */}
        {mode === 'image' && (
          <div className="px-5 py-3 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/40 flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-2.5">
              {/* Format Toggle */}
              <div className="flex items-center gap-0.5 p-0.5 bg-slate-200/70 dark:bg-slate-800 rounded-md">
                {(['png', 'svg'] as const).map((fmt) => (
                  <button
                    key={fmt}
                    type="button"
                    onClick={() => {
                      setImageFormat(fmt);
                      setImageError(null);
                    }}
                    className={`px-2.5 py-1 text-xs font-mono font-semibold uppercase rounded transition-colors cursor-pointer whitespace-nowrap ${
                      imageFormat === fmt
                        ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                        : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                    }`}
                  >
                    {fmt}
                  </button>
                ))}
              </div>

              {/* Theme Toggle */}
              <div className="flex items-center gap-0.5 p-0.5 bg-slate-200/70 dark:bg-slate-800 rounded-md">
                {(['light', 'dark'] as const).map((t) => (
                  <button
                    key={t}
                    type="button"
                    onClick={() => setImageTheme(t)}
                    className={`px-2.5 py-1 text-xs font-medium capitalize rounded transition-colors cursor-pointer whitespace-nowrap ${
                      imageTheme === t
                        ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                        : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                    }`}
                  >
                    {t}
                  </button>
                ))}
              </div>

              {/* Resolution Scale (PNG only) */}
              {imageFormat === 'png' && (
                <div className="flex items-center gap-0.5 p-0.5 bg-slate-200/70 dark:bg-slate-800 rounded-md">
                  {([1, 2, 3] as const).map((sc) => (
                    <button
                      key={sc}
                      type="button"
                      onClick={() => setImageScale(sc)}
                      className={`px-2 py-1 text-xs font-mono font-medium rounded transition-colors cursor-pointer whitespace-nowrap tabular-nums ${
                        imageScale === sc
                          ? 'bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs'
                          : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'
                      }`}
                    >
                      {sc}x
                    </button>
                  ))}
                </div>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-4 text-xs text-slate-600 dark:text-slate-300">
              <label className="flex items-center gap-1.5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={imageShowHeader}
                  onChange={(e) => setImageShowHeader(e.target.checked)}
                  className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Title</span>
              </label>
              <label className="flex items-center gap-1.5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={imageShowCardinality}
                  onChange={(e) => setImageShowCardinality(e.target.checked)}
                  className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Cardinality</span>
              </label>
              <label className="flex items-center gap-1.5 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={imageTransparent}
                  onChange={(e) => setImageTransparent(e.target.checked)}
                  className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Transparent</span>
              </label>
              {!imageTransparent && (
                <label className="flex items-center gap-1.5 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={imageShowGrid}
                    onChange={(e) => setImageShowGrid(e.target.checked)}
                    className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                  />
                  <span>Dot grid</span>
                </label>
              )}
            </div>
          </div>
        )}

        {/* Body Content */}
        {mode === 'import' ? (
          <div className="p-5 flex-1 overflow-y-auto studio-scroll space-y-3.5">
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs text-slate-600 dark:text-slate-400">
                Upload a previously exported SchemaForge <code className="font-mono">.json</code> file or paste JSON below.
              </p>
              <input
                ref={fileInputRef}
                type="file"
                accept=".json,application/json"
                onChange={handleFileUpload}
                className="hidden"
              />
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                className="px-3 py-1.5 text-xs font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-md transition-colors whitespace-nowrap shrink-0 cursor-pointer"
              >
                Choose JSON File
              </button>
            </div>

            <textarea
              value={importText}
              onChange={(e) => {
                setImportText(e.target.value);
                setImportError(null);
              }}
              rows={14}
              spellCheck={false}
              placeholder='{\n  "schema": {\n    "name": "my_database",\n    "tables": [...],\n    "relations": [...]\n  }\n}'
              className="w-full p-3.5 font-mono text-xs bg-slate-50 dark:bg-[#0B1120] border border-slate-200 dark:border-slate-700 rounded-lg text-slate-800 dark:text-slate-200 focus:outline-none focus:border-blue-500 studio-scroll"
            />

            {importError && (
              <div className="px-3 py-2 text-xs text-rose-700 dark:text-rose-300 bg-rose-50 dark:bg-rose-950/50 border border-rose-200 dark:border-rose-900 rounded-md">
                {importError}
              </div>
            )}
          </div>
        ) : mode === 'image' ? (
          <div className="flex-1 min-h-[340px] overflow-auto studio-scroll bg-slate-100 dark:bg-[#070B14] p-6 flex flex-col items-center justify-center gap-3">
            {schema.tables.length === 0 ? (
              <div className="text-center space-y-1.5 max-w-xs">
                <div className="text-sm font-semibold text-slate-800 dark:text-slate-200">
                  Nothing to export yet
                </div>
                <p className="text-xs text-slate-500 dark:text-slate-400 leading-relaxed">
                  Add at least one table to the workspace canvas to generate an SVG or PNG schema diagram.
                </p>
              </div>
            ) : (
              <>
                <div
                  className="max-w-full rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm"
                  style={
                    imageTransparent
                      ? {
                          backgroundImage:
                            'linear-gradient(45deg, rgba(148,163,184,0.18) 25%, transparent 25%), linear-gradient(-45deg, rgba(148,163,184,0.18) 25%, transparent 25%), linear-gradient(45deg, transparent 75%, rgba(148,163,184,0.18) 75%), linear-gradient(-45deg, transparent 75%, rgba(148,163,184,0.18) 75%)',
                          backgroundSize: '16px 16px',
                          backgroundPosition: '0 0, 0 8px, 8px -8px, -8px 0px',
                        }
                      : undefined
                  }
                >
                  {previewUrl && (
                    <img
                      src={previewUrl}
                      alt={`${schema.name || 'Database'} schema diagram preview`}
                      className="block max-w-full max-h-[52vh] w-auto h-auto object-contain"
                    />
                  )}
                </div>

                {imageError && (
                  <div className="px-3 py-2 text-xs text-rose-700 dark:text-rose-300 bg-rose-50 dark:bg-rose-950/50 border border-rose-200 dark:border-rose-900 rounded-md">
                    {imageError}
                  </div>
                )}
              </>
            )}
          </div>
        ) : (
          <div className="flex-1 overflow-auto bg-[#0B1120] text-slate-200 studio-scroll">
            <div className="p-4 font-mono text-[12px] leading-relaxed min-w-max">
              {codeLines.map((line, i) => (
                <div key={i} className="flex gap-4 hover:bg-white/[0.03] px-1 rounded">
                  <span className="w-8 text-right select-none text-slate-600 tabular-nums shrink-0">
                    {i + 1}
                  </span>
                  {mode === 'sql' ? (
                    <SqlHighlightedLine line={line} />
                  ) : (
                    <span className="whitespace-pre text-slate-200">{line}</span>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Modal Footer */}
        <div className="px-5 py-3.5 border-t border-slate-200 dark:border-slate-800 bg-white dark:bg-[#0F172A] flex items-center justify-between gap-3">
          {mode === 'import' ? (
            <>
              <span className="text-xs text-slate-500 dark:text-slate-400">
                Importing replaces the active workspace (reversible via Undo).
              </span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={onClose}
                  className="px-3.5 py-2 text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-md transition-colors cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleConfirmImport}
                  disabled={!importText.trim()}
                  className="px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-40 rounded-md transition-colors cursor-pointer"
                >
                  Import Schema
                </button>
              </div>
            </>
          ) : (
            <>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-mono tabular-nums">
                {mode === 'image' ? (
                  <>
                    {exportedPixelW.toLocaleString()} × {exportedPixelH.toLocaleString()} px
                    {imageFormat === 'png' ? ` (${imageScale}x)` : ' (vector)'} ·{' '}
                    {schema.tables.length} tables · {schema.relations.length} relations
                  </>
                ) : (
                  <>
                    {codeLines.length} lines · {schema.tables.length} tables ·{' '}
                    {schema.relations.length} relations
                  </>
                )}
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={handleCopy}
                  disabled={imageBusy || (mode === 'image' && schema.tables.length === 0)}
                  className="flex items-center gap-1.5 px-3.5 py-2 text-xs font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200/80 dark:hover:bg-slate-700 disabled:opacity-40 rounded-md transition-colors cursor-pointer whitespace-nowrap"
                >
                  {copied ? (
                    <>
                      <Check className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" />
                      <span>Copied</span>
                    </>
                  ) : (
                    <>
                      <Copy className="w-3.5 h-3.5" />
                      <span>
                        {mode === 'image'
                          ? imageFormat === 'svg'
                            ? 'Copy SVG'
                            : 'Copy PNG'
                          : 'Copy'}
                      </span>
                    </>
                  )}
                </button>
                <button
                  type="button"
                  onClick={handleDownload}
                  disabled={imageBusy || (mode === 'image' && schema.tables.length === 0)}
                  className="flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-40 rounded-md transition-colors cursor-pointer whitespace-nowrap"
                >
                  {imageBusy ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <Download className="w-3.5 h-3.5" />
                  )}
                  <span>
                    Download .
                    {mode === 'sql'
                      ? 'sql'
                      : mode === 'json'
                      ? 'json'
                      : imageFormat}
                  </span>
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
};


