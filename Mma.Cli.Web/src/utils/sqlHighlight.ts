export type SqlTokenKind =
  | 'comment'
  | 'keyword'
  | 'type'
  | 'string'
  | 'number'
  | 'identifier'
  | 'plain';

export interface SqlToken {
  kind: SqlTokenKind;
  text: string;
}

const SQL_KEYWORDS = new Set([
  'CREATE',
  'TABLE',
  'DROP',
  'IF',
  'EXISTS',
  'ALTER',
  'ADD',
  'CONSTRAINT',
  'PRIMARY',
  'FOREIGN',
  'KEY',
  'REFERENCES',
  'UNIQUE',
  'NOT',
  'NULL',
  'DEFAULT',
  'CHECK',
  'ON',
  'DELETE',
  'UPDATE',
  'CASCADE',
  'RESTRICT',
  'SET',
  'NO',
  'ACTION',
  'INDEX',
  'COMMENT',
  'IS',
  'ENGINE',
  'CHARSET',
  'AUTO_INCREMENT',
  'IDENTITY',
  'GENERATED',
  'BY',
  'AS',
  'GO',
  'EXEC',
  'IN',
  'BETWEEN',
  'AND',
  'OR',
]);

const SQL_TYPES = new Set([
  'UUID',
  'SERIAL',
  'BIGSERIAL',
  'SMALLSERIAL',
  'INT',
  'INTEGER',
  'BIGINT',
  'SMALLINT',
  'TINYINT',
  'VARCHAR',
  'NVARCHAR',
  'CHAR',
  'NCHAR',
  'TEXT',
  'MEDIUMTEXT',
  'LONGTEXT',
  'BOOLEAN',
  'BOOL',
  'BIT',
  'NUMERIC',
  'DECIMAL',
  'DOUBLE',
  'PRECISION',
  'FLOAT',
  'REAL',
  'TIMESTAMP',
  'TIMESTAMPTZ',
  'DATETIME',
  'DATETIME2',
  'DATETIMEOFFSET',
  'DATE',
  'JSON',
  'JSONB',
  'BYTEA',
  'BLOB',
  'VARBINARY',
  'UNIQUEIDENTIFIER',
  'MONEY',
]);

const TOKEN_REGEX =
  /(--.*$|'(?:''|[^'])*'|"[^"]*"|`[^`]*`|\[[^\]]*\]|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][A-Za-z0-9_]*\b)/g;

/**
 * Safely tokenizes a single SQL DDL line into typed spans for React rendering.
 */
export function tokenizeSqlLine(line: string): SqlToken[] {
  if (!line) return [{ kind: 'plain', text: '' }];
  const tokens: SqlToken[] = [];
  let lastIndex = 0;

  TOKEN_REGEX.lastIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = TOKEN_REGEX.exec(line)) !== null) {
    if (match.index > lastIndex) {
      tokens.push({ kind: 'plain', text: line.slice(lastIndex, match.index) });
    }

    const raw = match[0];
    let kind: SqlTokenKind = 'plain';

    if (raw.startsWith('--')) {
      kind = 'comment';
    } else if (raw.startsWith("'")) {
      kind = 'string';
    } else if (raw.startsWith('"') || raw.startsWith('`') || raw.startsWith('[')) {
      kind = 'identifier';
    } else if (/^\d/.test(raw)) {
      kind = 'number';
    } else {
      const upper = raw.toUpperCase();
      if (SQL_KEYWORDS.has(upper)) {
        kind = 'keyword';
      } else if (SQL_TYPES.has(upper)) {
        kind = 'type';
      }
    }

    tokens.push({ kind, text: raw });
    lastIndex = match.index + raw.length;
  }

  if (lastIndex < line.length) {
    tokens.push({ kind: 'plain', text: line.slice(lastIndex) });
  }

  return tokens;
}

export interface SqlLineDiffSummary {
  changedLineIndices: Set<number>;
  addedCount: number;
  removedCount: number;
}

/**
 * Computes a lightweight line-level diff between a baseline SQL string and the current SQL string
 * using LCS (falling back cleanly to multiset diff on very large schemas) so newly added or
 * edited SQL DDL lines highlight immediately in real time.
 */
export function computeSqlLineDiff(
  baselineLines: string[],
  currentLines: string[]
): SqlLineDiffSummary {
  const n = baselineLines.length;
  const m = currentLines.length;

  if (n === 0 && m === 0) {
    return { changedLineIndices: new Set(), addedCount: 0, removedCount: 0 };
  }

  // Exact fast-path
  if (n === m && baselineLines.every((l, i) => l === currentLines[i])) {
    return { changedLineIndices: new Set(), addedCount: 0, removedCount: 0 };
  }

  // Use LCS for typical schemas (up to ~600 lines) for exact positional matching
  if (n * m <= 360000) {
    const dp: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
    for (let i = n - 1; i >= 0; i--) {
      for (let j = m - 1; j >= 0; j--) {
        if (baselineLines[i] === currentLines[j]) {
          dp[i][j] = dp[i + 1][j + 1] + 1;
        } else {
          dp[i][j] = dp[i + 1][j] >= dp[i][j + 1] ? dp[i + 1][j] : dp[i][j + 1];
        }
      }
    }

    const changedLineIndices = new Set<number>();
    let addedCount = 0;
    let removedCount = 0;
    let i = 0;
    let j = 0;
    while (i < n && j < m) {
      if (baselineLines[i] === currentLines[j]) {
        i++;
        j++;
      } else if (dp[i + 1][j] >= dp[i][j + 1]) {
        removedCount++;
        i++;
      } else {
        changedLineIndices.add(j);
        addedCount++;
        j++;
      }
    }
    while (i < n) {
      removedCount++;
      i++;
    }
    while (j < m) {
      changedLineIndices.add(j);
      addedCount++;
      j++;
    }

    return { changedLineIndices, addedCount, removedCount };
  }

  // Multiset fallback for huge schemas
  const baseCounts = new Map<string, number>();
  for (const line of baselineLines) {
    baseCounts.set(line, (baseCounts.get(line) || 0) + 1);
  }
  const changedLineIndices = new Set<number>();
  let addedCount = 0;
  currentLines.forEach((line, idx) => {
    const rem = baseCounts.get(line) || 0;
    if (rem > 0) {
      baseCounts.set(line, rem - 1);
    } else {
      changedLineIndices.add(idx);
      addedCount++;
    }
  });
  let removedCount = 0;
  baseCounts.forEach((count) => {
    removedCount += count;
  });

  return { changedLineIndices, addedCount, removedCount };
}

/**
 * Finds the 0-based line range of a table's CREATE TABLE statement inside generated SQL DDL.
 */
export function findTableSqlRange(
  lines: string[],
  tableName: string
): { start: number; end: number } | null {
  if (!tableName.trim()) return null;
  const escaped = tableName.trim().replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const createPattern = new RegExp(
    `^CREATE\\s+TABLE\\s+(?:["\`\\[]?[\\w$-]+["\`\\]]?\\.)?["\`\\[]?${escaped}["\`\\]]?\\s*\\(`,
    'i'
  );

  for (let i = 0; i < lines.length; i++) {
    if (createPattern.test(lines[i].trim())) {
      let end = i;
      for (let j = i + 1; j < lines.length; j++) {
        end = j;
        if (lines[j].trim().startsWith(')')) {
          break;
        }
      }
      return { start: i, end };
    }
  }
  return null;
}
