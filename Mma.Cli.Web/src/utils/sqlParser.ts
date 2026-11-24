import {
  Column,
  DatabaseEngine,
  normalizeTableCodeGen,
  ReferentialAction,
  Relation,
  Table,
  TableAccentColor,
  TableIndex,
} from '../types/schema';

const ACCENT_COLORS: TableAccentColor[] = [
  'blue',
  'indigo',
  'cyan',
  'emerald',
  'amber',
  'rose',
  'slate',
];

export interface ParsedDatabaseResult {
  databaseName: string;
  engine: DatabaseEngine;
  tables: Table[];
  relations: Relation[];
}

function unquoteIdentifier(raw: string): string {
  return raw.trim().replace(/^[`"\[]|[`"\]]$/g, '').trim();
}

function parseQualifiedName(raw: string): { schema: string; name: string } {
  const parts = raw
    .trim()
    .split('.')
    .map((p) => unquoteIdentifier(p))
    .filter(Boolean);
  if (parts.length >= 2) {
    return { schema: parts[parts.length - 2], name: parts[parts.length - 1] };
  }
  return { schema: '', name: parts[0] || 'unnamed_table' };
}

function stripSqlComments(sql: string): string {
  // Remove /* ... */ block comments and -- line comments while respecting single quotes
  let out = '';
  let i = 0;
  let inSingle = false;
  while (i < sql.length) {
    const ch = sql[i];
    const next = sql[i + 1];

    if (inSingle) {
      out += ch;
      if (ch === "'" && next === "'") {
        out += next;
        i += 2;
        continue;
      }
      if (ch === "'") inSingle = false;
      i++;
      continue;
    }

    if (ch === "'") {
      inSingle = true;
      out += ch;
      i++;
      continue;
    }

    if (ch === '-' && next === '-') {
      while (i < sql.length && sql[i] !== '\n') i++;
      continue;
    }

    if (ch === '/' && next === '*') {
      i += 2;
      while (i < sql.length && !(sql[i] === '*' && sql[i + 1] === '/')) i++;
      i += 2;
      continue;
    }

    out += ch;
    i++;
  }
  return out;
}

function splitTopLevelCommas(body: string): string[] {
  const items: string[] = [];
  let current = '';
  let depth = 0;
  let inSingle = false;

  for (let i = 0; i < body.length; i++) {
    const ch = body[i];
    const next = body[i + 1];

    if (inSingle) {
      current += ch;
      if (ch === "'" && next === "'") {
        current += next;
        i++;
        continue;
      }
      if (ch === "'") inSingle = false;
      continue;
    }

    if (ch === "'") {
      inSingle = true;
      current += ch;
      continue;
    }

    if (ch === '(') depth++;
    else if (ch === ')') depth = Math.max(0, depth - 1);

    if (ch === ',' && depth === 0) {
      if (current.trim()) items.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  if (current.trim()) items.push(current.trim());
  return items;
}

function normalizeAction(raw?: string): ReferentialAction {
  const upper = (raw || 'NO ACTION').trim().toUpperCase();
  if (
    upper === 'CASCADE' ||
    upper === 'SET NULL' ||
    upper === 'SET DEFAULT' ||
    upper === 'RESTRICT' ||
    upper === 'NO ACTION'
  ) {
    return upper;
  }
  return 'NO ACTION';
}

function detectEngineFromSql(sql: string): DatabaseEngine {
  if (/\[dbo\]|\bNVARCHAR\b|\bUNIQUEIDENTIFIER\b|\bIDENTITY\s*\(/i.test(sql)) {
    return 'mssql';
  }
  if (/`[^`]+`|\bAUTO_INCREMENT\b|\bENGINE\s*=\s*InnoDB/i.test(sql)) {
    return 'mysql';
  }
  if (/\bSERIAL\b|\bBIGSERIAL\b|\bJSONB\b|\bTIMESTAMPTZ\b|COMMENT\s+ON\s+TABLE/i.test(sql)) {
    return 'postgresql';
  }
  return 'general';
}

function extractMatchingParens(sql: string, openParenIdx: number): { content: string; endIdx: number } | null {
  let depth = 0;
  let inSingle = false;
  for (let i = openParenIdx; i < sql.length; i++) {
    const ch = sql[i];
    const next = sql[i + 1];
    if (inSingle) {
      if (ch === "'" && next === "'") {
        i++;
        continue;
      }
      if (ch === "'") inSingle = false;
      continue;
    }
    if (ch === "'") {
      inSingle = true;
      continue;
    }
    if (ch === '(') depth++;
    else if (ch === ')') {
      depth--;
      if (depth === 0) {
        return {
          content: sql.slice(openParenIdx + 1, i),
          endIdx: i,
        };
      }
    }
  }
  return null;
}

interface PendingFk {
  name: string;
  childTableName: string;
  childColumnName: string;
  parentTableName: string;
  parentColumnName: string;
  onDelete: ReferentialAction;
  onUpdate: ReferentialAction;
}

export function parseSqlDumpToSchema(
  rawSql: string,
  fallbackName = 'imported_database'
): { result?: ParsedDatabaseResult; error?: string } {
  if (!rawSql.trim()) {
    return { error: 'Please paste SQL DDL or choose a .sql file first.' };
  }

  const engine = detectEngineFromSql(rawSql);
  const cleaned = stripSqlComments(rawSql);
  const stamp = Date.now();

  const tables: Table[] = [];
  const pendingFks: PendingFk[] = [];

  const createRegex = /CREATE\s+(?:TEMP(?:ORARY)?\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s*\(/gi;

  let match: RegExpExecArray | null;
  let tableCounter = 0;

  while ((match = createRegex.exec(cleaned)) !== null) {
    const qualified = parseQualifiedName(match[1]);
    const openParenIdx = match.index + match[0].length - 1;
    const parenBlock = extractMatchingParens(cleaned, openParenIdx);
    if (!parenBlock) continue;

    // Inspect tail options until next semicolon (e.g. ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='...')
    const afterParen = cleaned.slice(parenBlock.endIdx + 1);
    const semiIdx = afterParen.indexOf(';');
    const tail = semiIdx >= 0 ? afterParen.slice(0, semiIdx) : afterParen.slice(0, 200);

    const engineMatch = tail.match(/ENGINE\s*=\s*(\w+)/i);
    const charsetMatch = tail.match(/(?:DEFAULT\s+)?CHAR(?:ACTER\s+)?SET\s*=\s*(\w+)/i);
    const tblCommentMatch = tail.match(/COMMENT\s*=\s*'((?:''|[^'])*)'/i);

    const entries = splitTopLevelCommas(parenBlock.content);
    const columns: Column[] = [];
    const inlineIndexes: { name: string; colNames: string[]; isUnique: boolean }[] = [];
    const tablePkCols = new Set<string>();
    const tableUqCols = new Set<string>();

    entries.forEach((entry, colIdx) => {
      const trimmed = entry.trim();
      if (!trimmed) return;

      // Table-level PRIMARY KEY
      const pkMatch = trimmed.match(/^(?:CONSTRAINT\s+[`"\[]?[\w$-]+[`"\]]?\s+)?PRIMARY\s+KEY\s*\(([^)]+)\)/i);
      if (pkMatch) {
        pkMatch[1]
          .split(',')
          .map((c) => unquoteIdentifier(c.replace(/\s+(ASC|DESC)\b/i, '')))
          .forEach((c) => tablePkCols.add(c.toLowerCase()));
        return;
      }

      // Table-level UNIQUE constraint
      const uqMatch = trimmed.match(/^(?:CONSTRAINT\s+[`"\[]?[\w$-]+[`"\]]?\s+)?UNIQUE(?:\s+KEY|\s+INDEX)?(?:\s+[`"\[]?[\w$-]+[`"\]]?)?\s*\(([^)]+)\)/i);
      if (uqMatch) {
        const cols = uqMatch[1].split(',').map((c) => unquoteIdentifier(c));
        if (cols.length === 1) tableUqCols.add(cols[0].toLowerCase());
        return;
      }

      // Table-level FOREIGN KEY
      const fkMatch = trimmed.match(
        /^(?:CONSTRAINT\s+([`"\[]?[\w$-]+[`"\]]?)\s+)?FOREIGN\s+KEY\s*\(([^)]+)\)\s*REFERENCES\s+((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s*\(([^)]+)\)(.*)$/i
      );
      if (fkMatch) {
        const fkName = fkMatch[1] ? unquoteIdentifier(fkMatch[1]) : '';
        const childCol = unquoteIdentifier(fkMatch[2].split(',')[0]);
        const parentRef = parseQualifiedName(fkMatch[3]);
        const parentCol = unquoteIdentifier(fkMatch[4].split(',')[0]);
        const fkTail = fkMatch[5] || '';
        const delMatch = fkTail.match(/ON\s+DELETE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION)/i);
        const updMatch = fkTail.match(/ON\s+UPDATE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION)/i);

        pendingFks.push({
          name: fkName || `fk_${qualified.name}_${childCol}`,
          childTableName: qualified.name,
          childColumnName: childCol,
          parentTableName: parentRef.name,
          parentColumnName: parentCol,
          onDelete: normalizeAction(delMatch?.[1]),
          onUpdate: normalizeAction(updMatch?.[1]),
        });
        return;
      }

      // Table-level INDEX / KEY definition (e.g. MySQL KEY idx_foo (col1, col2))
      const inlineIdxMatch = trimmed.match(
        /^(?:INDEX|KEY)\s+([`"\[]?[\w$-]+[`"\]]?)\s*\(([^)]+)\)/i
      );
      if (inlineIdxMatch) {
        const idxName = unquoteIdentifier(inlineIdxMatch[1]);
        const colNames = inlineIdxMatch[2]
          .split(',')
          .map((c) => unquoteIdentifier(c.replace(/\s+(ASC|DESC)\b/i, '').replace(/\(\d+\)/g, '')));
        inlineIndexes.push({ name: idxName, colNames, isUnique: false });
        return;
      }

      // Skip other table-level CONSTRAINT / CHECK / FULLTEXT / SPATIAL
      if (/^(?:CONSTRAINT|CHECK|KEY|INDEX|FULLTEXT|SPATIAL)\b/i.test(trimmed)) {
        return;
      }

      // Parse Column Definition: <col_name> <type[(params)]> [constraints...]
      const colMatch = trimmed.match(/^([`"\[]?[\w$-]+[`"\]]?)\s+([a-zA-Z_][\w\s]*?(?:\([^)]*\))?(?:\[\])?)(?=\s+(?:PRIMARY|NOT|NULL|UNIQUE|DEFAULT|CHECK|REFERENCES|AUTO_INCREMENT|AUTOINCREMENT|IDENTITY|GENERATED|COMMENT|COLLATE|CONSTRAINT)\b|$)(.*)$/i);
      if (!colMatch) return;

      const colName = unquoteIdentifier(colMatch[1]);
      let colType = colMatch[2].trim().replace(/\s+/g, ' ');
      const rest = colMatch[3] || '';

      // Avoid swallowing UNSIGNED / VARYING incorrectly
      if (/\bVARYING\b/i.test(colType)) {
        colType = colType.replace(/CHARACTER\s+VARYING/i, 'varchar');
      }

      const isPrimaryKey = /\bPRIMARY\s+KEY\b/i.test(rest);
      const isNotNull = /\bNOT\s+NULL\b/i.test(rest) || isPrimaryKey;
      const isUnique = /\bUNIQUE\b/i.test(rest);
      const isAutoIncrement =
        /\b(AUTO_INCREMENT|AUTOINCREMENT|IDENTITY|GENERATED\b.*\bAS\s+IDENTITY)\b/i.test(rest) ||
        /^(big)?serial$/i.test(colType);

      const defaultMatch = rest.match(
        /\bDEFAULT\s+((?:'(?:''|[^'])*'(?:::[a-zA-Z_][\w]*)?)|(?:\([^)]*\))|(?:[a-zA-Z_][\w]*\([^)]*\))|(?:[^\s,]+))/i
      );
      const commentMatch = rest.match(/\bCOMMENT\s+'((?:''|[^'])*)'/i);
      const checkMatch = rest.match(/\bCHECK\s*\(([^)]+)\)/i);

      // Inline REFERENCES
      const inlineRef = rest.match(
        /\bREFERENCES\s+((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s*\(([^)]+)\)(.*)$/i
      );
      if (inlineRef) {
        const parentRef = parseQualifiedName(inlineRef[1]);
        const parentCol = unquoteIdentifier(inlineRef[2].split(',')[0]);
        const refTail = inlineRef[3] || '';
        const delMatch = refTail.match(/ON\s+DELETE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION)/i);
        const updMatch = refTail.match(/ON\s+UPDATE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION)/i);
        pendingFks.push({
          name: `fk_${qualified.name}_${colName}`,
          childTableName: qualified.name,
          childColumnName: colName,
          parentTableName: parentRef.name,
          parentColumnName: parentCol,
          onDelete: normalizeAction(delMatch?.[1]),
          onUpdate: normalizeAction(updMatch?.[1]),
        });
      }

      columns.push({
        id: `col-sql-${stamp}-${tableCounter}-${colIdx}`,
        name: colName,
        type: colType,
        isPrimaryKey,
        isNullable: !isNotNull,
        isUnique,
        isAutoIncrement,
        defaultValue: defaultMatch ? defaultMatch[1].trim() : '',
        checkConstraint: checkMatch ? checkMatch[1].trim() : '',
        comment: commentMatch ? commentMatch[1].replace(/''/g, "'") : '',
      });
    });

    // Apply table-level PK / UNIQUE flags onto columns
    columns.forEach((col) => {
      const lower = col.name.toLowerCase();
      if (tablePkCols.has(lower)) {
        col.isPrimaryKey = true;
        col.isNullable = false;
      }
      if (tableUqCols.has(lower)) {
        col.isUnique = true;
      }
    });

    const rawStorage = engineMatch?.[1];
    const storageEngine =
      engine === 'mysql'
        ? rawStorage === 'MyISAM' || rawStorage === 'MEMORY'
          ? rawStorage
          : 'InnoDB'
        : undefined;

    const resolvedInlineIndexes: TableIndex[] = inlineIndexes
      .map((ix, ixIdx) => ({
        id: `idx-sql-${stamp}-${tableCounter}-${ixIdx}`,
        name: ix.name,
        isUnique: ix.isUnique,
        columns: ix.colNames
          .map((cn) => columns.find((c) => c.name.toLowerCase() === cn.toLowerCase())?.id)
          .filter((cid): cid is string => Boolean(cid)),
      }))
      .filter((ix) => ix.columns.length > 0);

    tables.push({
      id: `tbl-sql-${stamp}-${tableCounter}`,
      name: qualified.name,
      schema:
        qualified.schema ||
        (engine === 'postgresql' ? 'public' : engine === 'mssql' ? 'dbo' : ''),
      storageEngine,
      charset: engine === 'mysql' ? charsetMatch?.[1] || 'utf8mb4' : undefined,
      codeGen: normalizeTableCodeGen(),
      color: ACCENT_COLORS[tableCounter % ACCENT_COLORS.length],
      comment: tblCommentMatch ? tblCommentMatch[1].replace(/''/g, "'") : '',
      x: 60 + (tableCounter % 4) * 340,
      y: 60 + Math.floor(tableCounter / 4) * 300,
      columns,
      indexes: resolvedInlineIndexes,
    });

    tableCounter++;
    createRegex.lastIndex = parenBlock.endIdx + 1;
  }

  // Parse standalone ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY statements
  const alterFkRegex =
    /ALTER\s+TABLE\s+(?:ONLY\s+)?((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s+ADD\s+(?:CONSTRAINT\s+([`"\[]?[\w$-]+[`"\]]?)\s+)?FOREIGN\s+KEY\s*\(([^)]+)\)\s*REFERENCES\s+((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s*\(([^)]+)\)([^;]*);/gi;

  let alterMatch: RegExpExecArray | null;
  while ((alterMatch = alterFkRegex.exec(cleaned)) !== null) {
    const childRef = parseQualifiedName(alterMatch[1]);
    const fkName = alterMatch[2] ? unquoteIdentifier(alterMatch[2]) : '';
    const childCol = unquoteIdentifier(alterMatch[3].split(',')[0]);
    const parentRef = parseQualifiedName(alterMatch[4]);
    const parentCol = unquoteIdentifier(alterMatch[5].split(',')[0]);
    const fkTail = alterMatch[6] || '';

    const delMatch = fkTail.match(/ON\s+DELETE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION)/i);
    const updMatch = fkTail.match(/ON\s+UPDATE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION)/i);

    pendingFks.push({
      name: fkName || `fk_${childRef.name}_${childCol}`,
      childTableName: childRef.name,
      childColumnName: childCol,
      parentTableName: parentRef.name,
      parentColumnName: parentCol,
      onDelete: normalizeAction(delMatch?.[1]),
      onUpdate: normalizeAction(updMatch?.[1]),
    });
  }

  // Parse standalone CREATE [UNIQUE] INDEX statements
  const createIdxRegex =
    /CREATE\s+(UNIQUE\s+)?(?:NONCLUSTERED\s+|CLUSTERED\s+)?INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?([`"\[]?[\w$-]+[`"\]]?)\s+ON\s+((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s*(?:USING\s+\w+\s*)?\(([^)]+)\)/gi;
  let idxMatch: RegExpExecArray | null;
  let standaloneIdxCounter = 0;
  while ((idxMatch = createIdxRegex.exec(cleaned)) !== null) {
    const isUnique = Boolean(idxMatch[1]);
    const idxName = unquoteIdentifier(idxMatch[2]);
    const tblRef = parseQualifiedName(idxMatch[3]);
    const colNames = idxMatch[4]
      .split(',')
      .map((c) => unquoteIdentifier(c.replace(/\s+(ASC|DESC|NULLS\s+FIRST|NULLS\s+LAST)\b/gi, '')));
    const target = tables.find((t) => t.name.toLowerCase() === tblRef.name.toLowerCase());
    if (!target) continue;
    const colIds = colNames
      .map((cn) => target.columns.find((c) => c.name.toLowerCase() === cn.toLowerCase())?.id)
      .filter((cid): cid is string => Boolean(cid));
    if (!target.indexes) target.indexes = [];
    target.indexes.push({
      id: `idx-sql-sa-${stamp}-${standaloneIdxCounter++}`,
      name: idxName,
      columns: colIds,
      isUnique,
    });
  }

  // Parse PostgreSQL COMMENT ON TABLE statements
  const commentTblRegex =
    /COMMENT\s+ON\s+TABLE\s+((?:[`"\[]?[\w$-]+[`"\]]?\.)?[`"\[]?[\w$-]+[`"\]]?)\s+IS\s+'((?:''|[^'])*)'/gi;
  let cTblMatch: RegExpExecArray | null;
  while ((cTblMatch = commentTblRegex.exec(cleaned)) !== null) {
    const ref = parseQualifiedName(cTblMatch[1]);
    const target = tables.find((t) => t.name.toLowerCase() === ref.name.toLowerCase());
    if (target) target.comment = cTblMatch[2].replace(/''/g, "'");
  }

  if (tables.length === 0) {
    return {
      error: 'No CREATE TABLE statements were found in the SQL script. Check the SQL syntax and try again.',
    };
  }

  // Resolve Foreign Keys into Relation objects
  const tableByLower = new Map<string, Table>();
  tables.forEach((t) => tableByLower.set(t.name.toLowerCase(), t));

  const relations: Relation[] = [];
  const seen = new Set<string>();

  pendingFks.forEach((fk, idx) => {
    const parentTable = tableByLower.get(fk.parentTableName.toLowerCase());
    const childTable = tableByLower.get(fk.childTableName.toLowerCase());
    if (!parentTable || !childTable) return;

    const parentCol = parentTable.columns.find(
      (c) => c.name.toLowerCase() === fk.parentColumnName.toLowerCase()
    );
    const childCol = childTable.columns.find(
      (c) => c.name.toLowerCase() === fk.childColumnName.toLowerCase()
    );
    if (!parentCol || !childCol) return;

    const dedup = `${parentTable.id}:${parentCol.id}->${childTable.id}:${childCol.id}`;
    if (seen.has(dedup)) return;
    seen.add(dedup);

    relations.push({
      id: `rel-sql-${stamp}-${idx}`,
      name: fk.name,
      sourceTableId: parentTable.id,
      sourceColumnId: parentCol.id,
      targetTableId: childTable.id,
      targetColumnId: childCol.id,
      cardinality: childCol.isUnique ? '1:1' : '1:N',
      onDelete: fk.onDelete,
      onUpdate: fk.onUpdate,
    });
  });

  return {
    result: {
      databaseName: fallbackName,
      engine,
      tables,
      relations,
    },
  };
}
