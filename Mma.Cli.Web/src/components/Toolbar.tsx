import React, { useEffect, useRef, useState } from 'react';
import {
  Check,
  Code2,
  Database,
  FileJson,
  Grid,
  HardDriveDownload,
  ImageDown,
  Layers,
  LayoutGrid,
  Moon,
  Plus,
  Redo2,
  Sun,
  Undo2,
  Upload,
} from 'lucide-react';
import { DEFAULT_DATABASE_ENGINE } from '../config/featureFlags';
import { ENGINE_PROFILES, SUPPORTED_ENGINE_LIST } from '../data/engines';
import { SCHEMA_TEMPLATES, SchemaTemplate } from '../data/templates';
import { DatabaseEngine } from '../types/schema';

interface ToolbarProps {
  engine: DatabaseEngine;
  onEngineChange: (engine: DatabaseEngine) => void;
  onAddTable: () => void;
  onAutoLayout: () => void;
  onUndo: () => void;
  onRedo: () => void;
  canUndo: boolean;
  canRedo: boolean;
  snapGrid: boolean;
  onToggleSnapGrid: () => void;
  onSelectTemplate: (template: SchemaTemplate) => void;
  onOpenImportDatabase: () => void;
  onOpenExportSql: () => void;
  onOpenExportJson: () => void;
  onOpenExportImage: () => void;
  onOpenImportJson: () => void;
  theme: 'light' | 'dark';
  onToggleTheme: () => void;
}

export const Toolbar: React.FC<ToolbarProps> = ({
  engine,
  onEngineChange,
  onAddTable,
  onAutoLayout,
  onUndo,
  onRedo,
  canUndo,
  canRedo,
  snapGrid,
  onToggleSnapGrid,
  onSelectTemplate,
  onOpenImportDatabase,
  onOpenExportSql,
  onOpenExportJson,
  onOpenExportImage,
  onOpenImportJson,
  theme,
  onToggleTheme,
}) => {
  const [templateMenuOpen, setTemplateMenuOpen] = useState(false);
  const [engineMenuOpen, setEngineMenuOpen] = useState(false);
  const templateMenuRef = useRef<HTMLDivElement>(null);
  const engineMenuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (templateMenuRef.current && !templateMenuRef.current.contains(e.target as Node)) {
        setTemplateMenuOpen(false);
      }
      if (engineMenuRef.current && !engineMenuRef.current.contains(e.target as Node)) {
        setEngineMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const activeEngine = ENGINE_PROFILES[engine] || ENGINE_PROFILES[DEFAULT_DATABASE_ENGINE];

  const iconBtn =
    'w-8 h-8 flex items-center justify-center rounded-lg text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors cursor-pointer shrink-0';

  const activeIconBtn =
    'w-8 h-8 flex items-center justify-center rounded-lg bg-blue-50 dark:bg-blue-950/70 text-blue-600 dark:text-blue-400 transition-colors cursor-pointer shrink-0';

  return (
    <header
      onPointerDown={(e) => e.stopPropagation()}
      aria-label="Workspace toolbar"
      className="absolute top-4 left-1/2 -translate-x-1/2 z-30 flex items-center gap-0.5 p-1.5 bg-white/95 dark:bg-[#111827]/95 backdrop-blur-md border border-slate-200 dark:border-slate-700/90 rounded-xl shadow-md select-none"
    >
      {/* Database Engine Switcher */}
      <div className="relative" ref={engineMenuRef}>
        <button
          type="button"
          onClick={() => {
            setEngineMenuOpen((v) => !v);
            setTemplateMenuOpen(false);
          }}
          title={`Database Engine: ${activeEngine.label}`}
          aria-label={`Database Engine: ${activeEngine.label}`}
          className={engineMenuOpen ? activeIconBtn : iconBtn}
        >
          <Database className="w-4 h-4 text-blue-600 dark:text-blue-400" strokeWidth={2} />
        </button>

        {engineMenuOpen && (
          <div className="absolute left-0 top-full mt-2.5 w-72 bg-white dark:bg-[#111827] border border-slate-200 dark:border-slate-700 rounded-lg shadow-lg py-1.5 z-50">
            <div className="px-3 py-1.5 text-[11px] font-medium text-slate-400 dark:text-slate-500">
              Database Engine
            </div>
            {SUPPORTED_ENGINE_LIST.map((profile) => {
              const isSelected = profile.id === engine;
              return (
                <button
                  key={profile.id}
                  type="button"
                  onClick={() => {
                    onEngineChange(profile.id);
                    setEngineMenuOpen(false);
                  }}
                  className={`w-full text-left px-3 py-2 flex items-start justify-between gap-2.5 transition-colors cursor-pointer ${
                    isSelected
                      ? 'bg-blue-50/80 dark:bg-blue-950/50'
                      : 'hover:bg-slate-50 dark:hover:bg-slate-800/80'
                  }`}
                >
                  <div className="min-w-0">
                    <div
                      className={`text-xs font-semibold ${
                        isSelected
                          ? 'text-blue-700 dark:text-blue-300'
                          : 'text-slate-900 dark:text-slate-100'
                      }`}
                    >
                      {profile.label}
                    </div>
                    <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 leading-snug">
                      {profile.description}
                    </div>
                  </div>
                  {isSelected && (
                    <Check className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400 shrink-0 mt-0.5" />
                  )}
                </button>
              );
            })}
          </div>
        )}
      </div>

      {/* Starter Templates */}
      <div className="relative" ref={templateMenuRef}>
        <button
          type="button"
          onClick={() => {
            setTemplateMenuOpen((v) => !v);
            setEngineMenuOpen(false);
          }}
          title="Load starter template"
          aria-label="Load starter template"
          className={templateMenuOpen ? activeIconBtn : iconBtn}
        >
          <Layers className="w-4 h-4" />
        </button>

        {templateMenuOpen && (
          <div className="absolute left-0 top-full mt-2.5 w-68 bg-white dark:bg-[#111827] border border-slate-200 dark:border-slate-700 rounded-lg shadow-lg py-1.5 z-50">
            <div className="px-3 py-1.5 text-[11px] font-medium text-slate-400 dark:text-slate-500">
              Starter Schemas
            </div>
            {SCHEMA_TEMPLATES.map((tpl) => (
              <button
                key={tpl.id}
                type="button"
                onClick={() => {
                  onSelectTemplate(tpl);
                  setTemplateMenuOpen(false);
                }}
                className="w-full text-left px-3 py-2 hover:bg-slate-50 dark:hover:bg-slate-800/80 transition-colors cursor-pointer"
              >
                <div className="text-xs font-semibold text-slate-900 dark:text-slate-100">
                  {tpl.label}
                </div>
                <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 leading-snug">
                  {tpl.summary}
                </div>
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="h-4 w-px bg-slate-200 dark:bg-slate-700/80 mx-1" aria-hidden="true" />

      {/* Modeling Controls */}
      <button
        type="button"
        onClick={onAddTable}
        title="Add Table"
        aria-label="Add Table"
        className={iconBtn}
      >
        <Plus className="w-4 h-4" strokeWidth={2.2} />
      </button>

      <button
        type="button"
        onClick={onAutoLayout}
        title="Auto-Layout Tables"
        aria-label="Auto-Layout Tables"
        className={iconBtn}
      >
        <LayoutGrid className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onUndo}
        disabled={!canUndo}
        title="Undo (Ctrl+Z)"
        aria-label="Undo"
        className={`${iconBtn} disabled:opacity-35 disabled:pointer-events-none`}
      >
        <Undo2 className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onRedo}
        disabled={!canRedo}
        title="Redo (Ctrl+Shift+Z)"
        aria-label="Redo"
        className={`${iconBtn} disabled:opacity-35 disabled:pointer-events-none`}
      >
        <Redo2 className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onToggleSnapGrid}
        title={snapGrid ? 'Grid snap enabled (20px)' : 'Grid snap disabled'}
        aria-label="Toggle grid snap"
        className={snapGrid ? activeIconBtn : iconBtn}
      >
        <Grid className="w-4 h-4" />
      </button>

      <div className="h-4 w-px bg-slate-200 dark:bg-slate-700/80 mx-1" aria-hidden="true" />

      {/* Import / Export / Theme */}
      <button
        type="button"
        onClick={onOpenImportDatabase}
        title="Import from Existing Database"
        aria-label="Import from Existing Database"
        className={iconBtn}
      >
        <HardDriveDownload className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onOpenImportJson}
        title="Import JSON Schema"
        aria-label="Import JSON Schema"
        className={iconBtn}
      >
        <Upload className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onOpenExportJson}
        title="Export JSON"
        aria-label="Export JSON"
        className={iconBtn}
      >
        <FileJson className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onOpenExportImage}
        title="Export Diagram Image (SVG / PNG)"
        aria-label="Export Diagram Image (SVG or PNG)"
        className={iconBtn}
      >
        <ImageDown className="w-4 h-4" />
      </button>

      <button
        type="button"
        onClick={onOpenExportSql}
        title={`Export SQL (${activeEngine.label})`}
        aria-label={`Export SQL (${activeEngine.label})`}
        className="w-8 h-8 flex items-center justify-center rounded-lg bg-blue-600 hover:bg-blue-700 text-white transition-colors cursor-pointer shrink-0"
      >
        <Code2 className="w-4 h-4" />
      </button>

      <div className="h-4 w-px bg-slate-200 dark:bg-slate-700/80 mx-1" aria-hidden="true" />

      <button
        type="button"
        onClick={onToggleTheme}
        title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
        aria-label="Toggle color theme"
        className={iconBtn}
      >
        {theme === 'dark' ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
      </button>
    </header>
  );
};
