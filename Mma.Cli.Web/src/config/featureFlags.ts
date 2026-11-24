import { DatabaseEngine } from '../types/schema';

export type LiveDatabaseEngine = 'postgresql' | 'mysql' | 'mssql';

const ALL_DATABASE_ENGINES: readonly DatabaseEngine[] = [
  'postgresql',
  'mssql',
  'mysql',
  'general',
];

const LIVE_DATABASE_ENGINES: readonly LiveDatabaseEngine[] = [
  'postgresql',
  'mysql',
  'mssql',
];

const DEFAULT_SUPPORTED_DATABASE_ENGINES: readonly DatabaseEngine[] = ['mssql'];

function parseSupportedDatabaseEngines(raw: string | undefined): readonly DatabaseEngine[] {
  if (!raw || !raw.trim()) {
    return DEFAULT_SUPPORTED_DATABASE_ENGINES;
  }

  const parsed: DatabaseEngine[] = [];
  for (const token of raw.split(',')) {
    const normalized = token.trim().toLowerCase() as DatabaseEngine;
    if (
      ALL_DATABASE_ENGINES.includes(normalized) &&
      !parsed.includes(normalized)
    ) {
      parsed.push(normalized);
    }
  }

  return parsed.length > 0 ? parsed : DEFAULT_SUPPORTED_DATABASE_ENGINES;
}

export const FEATURE_FLAGS = {
  /**
   * Comma-separated list via VITE_SUPPORTED_DB_ENGINES (e.g. "mssql,postgresql").
   * Defaults to MSSQL only.
   */
  supportedDatabaseEngines: parseSupportedDatabaseEngines(
    import.meta.env.VITE_SUPPORTED_DB_ENGINES
  ),
} as const;

export const SUPPORTED_DATABASE_ENGINES: readonly DatabaseEngine[] =
  FEATURE_FLAGS.supportedDatabaseEngines;

export const DEFAULT_DATABASE_ENGINE: DatabaseEngine =
  SUPPORTED_DATABASE_ENGINES[0] ?? 'mssql';

export const SUPPORTED_LIVE_DATABASE_ENGINES: readonly LiveDatabaseEngine[] = (() => {
  const enabled = LIVE_DATABASE_ENGINES.filter((engine) =>
    SUPPORTED_DATABASE_ENGINES.includes(engine)
  );
  return enabled.length > 0 ? enabled : ['mssql'];
})();

export const DEFAULT_LIVE_DATABASE_ENGINE: LiveDatabaseEngine =
  SUPPORTED_LIVE_DATABASE_ENGINES[0] ?? 'mssql';

export function isDatabaseEngineSupported(engine: DatabaseEngine): boolean {
  return SUPPORTED_DATABASE_ENGINES.includes(engine);
}

export function isLiveDatabaseEngineSupported(engine: LiveDatabaseEngine): boolean {
  return SUPPORTED_LIVE_DATABASE_ENGINES.includes(engine);
}

export function resolveSupportedDatabaseEngine(
  engine?: DatabaseEngine | null
): DatabaseEngine {
  if (engine && isDatabaseEngineSupported(engine)) {
    return engine;
  }
  return DEFAULT_DATABASE_ENGINE;
}
