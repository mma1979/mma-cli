# Task 3.2 Implementation Brief — React 19 + TypeScript Workspace (`Mma.Cli.Web`)

- **Branch**: `agentic-v1`
- **Base Commit**: `adcd404632a9111da255b42489ef6cc4d6df830c`
- **Plan Reference**:
  - `outputs/20261120T205500Z_1ea6d98d_v1/04-SPEC-REACT-BFF-UI.md`
  - `outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md` (Phase 3 — Task 3.2)
  - `outputs/20261120T205500Z_1ea6d98d_v1/08-REFACTORS-AND-FEATURE-BACKLOG.md` (C1.1, C1.2, C1.4)
- **Upstream contract**: `src/Mma.Cli.Bff` @ `adcd404` (OpenAPI `/openapi/v1.json`, 25 `/api/v1` routes + SSE job stream)

## Global Constraints (Mandatory)

1. **No Node.js at end-user runtime.** Everything under `src/Mma.Cli.Web/` is build-time only. No npm package, binary, or script may be required when the shipped `mma-cli` tool runs. Task 3.3 owns bundling into `Mma.Cli.Bff/wwwroot/`; Task 3.2 must produce a `dist/` that is copy-ready but must not wire packaging.
2. **Never create a directory named `ui` or `Ui`.** `.gitignore:35` matches `[Uu]i/`. shadcn/ui primitives MUST live in `src/components/primitives/` (not the CLI default `components/ui/`). Do not weaken `.gitignore` to work around this.
3. **TypeScript `strict: true` plus `noUncheckedIndexedAccess`, `noImplicitOverride`, `exactOptionalPropertyTypes`.** Zero `any`, zero `@ts-ignore` / `@ts-expect-error`, zero non-null assertions on API payloads.
4. **All HTTP goes through `src/api/client.ts`.** It sets `credentials: "include"`, adds `X-Mma-Csrf` on `POST`/`PUT`/`PATCH`/`DELETE`, and converts `application/problem+json` into a typed `ApiProblem { status, errorCode, detail, title }`.
5. **The browser never touches SQLite, target databases, the filesystem, or LLM endpoints.** Only `/api/v1/*`, `/openapi/*`, and `/api/v1/jobs/{id}/events` may be called. No `node:fs`, no direct connection strings in the client bundle.
6. **No secrets or absolute machine paths in source.** `.env*` files are local-only; commit `.env.example` only.
7. **Do not modify** `src/Mma.Cli.Bff/**`, `src/Mma.Cli.Core/**`, `src/Mma.Cli.Database/**`, `src/Mma.Cli.App/**`, `Mma.Cli.sln`, `Directory.Packages.props`, or `.github/workflows/ci.yml`. Task 3.3 owns BFF/App/CI/packaging changes. The only file outside `src/Mma.Cli.Web/` that may change is `.gitignore`.
8. **Bundle budget**: initial route ≤ 300 KB gzip. `@xyflow/react`, `shiki`, and `diff` must be route/lazy-chunked, not in the entry chunk. `npm run build` must fail the task if the entry chunk exceeds budget.
9. **Accessibility**: every control keyboard reachable, every input labelled, `focus-visible` rings preserved, and status never conveyed by colour alone (status pills carry text: `Draft`, `Applied`, `Modified`, `Drift`).
10. **Never stage or commit** `.sdd-phase3/`, `progress.md`, `node_modules/`, `dist/`, `playwright-report/`, `test-results/`, or `*.local`.
11. **No scope creep into Phase 5.** Provider profiles, AI Copilot, and MCP settings are stubs only (see §5.9). Do not add `/api/v1/providers` or `/api/v1/agent/*` calls; those routes do not exist yet and would 401/404.

---

## Verified Preconditions (as of `adcd404`)

### Toolchain (local)
- Node `v26.8.2`, npm `11.19.1`.
- `node_modules/` already ignored (`.gitignore:289`); `dist/`, `playwright-report/`, `test-results/` are **not** ignored yet.
- BFF default bind: `http://127.0.0.1:5180` when neither `--urls` nor `ASPNETCORE_URLS` is set (`Program.cs:13-18`).

### Auth & host for local dev / E2E
- `GET /api/v1/auth/session` is public and, when `MMA_BFF_DEV_AUTH=1` **and** no bootstrap token is configured, issues a dev session and `Set-Cookie: mma_bff_session` (`HealthAndAuthEndpoints.cs:71-99`, `BootstrapSessionManager.CreateDevSessionIfAllowed`).
- Allowed filesystem roots: `MMA_BFF_ALLOWED_ROOTS` (`;`-separated) or `BffSecurity:AllowedRoots`; default is the user profile.
- Workspace DB override: config `Bff:WorkspaceDbPath` or `WorkspaceDbPath`.
- Origin policy accepts any `http`/`https` origin whose **host** is `127.0.0.1`, `localhost`, `::1`, `[::1]` (port ignored). So the Vite dev origin `http://127.0.0.1:5173` is allowed as-is.
- Every `/api/v1/*` route except `GET /health`, `POST /auth/bootstrap`, `GET /auth/session` requires the session cookie; mutating verbs also require `X-Mma-Csrf`.

### Routes the SPA must consume (all verified in source)

| Method & route | Request | Response | Status |
| --- | --- | --- | --- |
| `GET /api/v1/health` | – | `HealthResponse` | 200 (public) |
| `POST /api/v1/auth/bootstrap` | `{ token }` | `{ authenticated, csrfToken }` + cookie | 200 (public) |
| `GET /api/v1/auth/session` | – | `{ authenticated, csrfToken?, userId, displayName }` | 200 (public) |
| `GET /api/v1/fs/browse?path=` | – | `{ currentPath, parentPath, allowedRoots, entries[] }` | 200 |
| `GET /api/v1/workspaces` | – | `WorkspaceSummaryDto[]` | 200 |
| `POST /api/v1/workspaces/register` | `{ solutionPath }` | `{ workspace, solutionInfo, manifest?, manifestHash? }` | 200 |
| `POST /api/v1/workspaces` | `{ parentDirectory, solutionName, mapper, dryRun }` | `{ workspace?, summary }` | 201, or 200 with `workspace: null` when `dryRun` |
| `POST /api/v1/workspaces/{id}/export` | – | `{ exported, solutionPath }` | 200 |
| `GET /api/v1/projects/{id}` | – | `{ workspace, solutionInfo, manifest, manifestHash, validation }` | 200 |
| `POST /api/v1/projects/{id}/entities` | `{ name, pkType, hasService, hasApi }` | `{ manifest, contentHash }` | 200 |
| `DELETE /api/v1/projects/{id}/entities/{entityName}` | – | `{ manifest, contentHash }` | 200 |
| `POST /api/v1/projects/{id}/entities/{entityName}/properties` | `{ name, dataType, nullable, isForeignKey, maxLength, required }` | `{ manifest, contentHash }` | 200 |
| `DELETE /api/v1/projects/{id}/entities/{entityName}/properties/{propertyName}` | – | `{ manifest, contentHash }` | 200 |
| `POST /api/v1/projects/{id}/relations` | `{ parentEntity, childEntity, foreignKey, foreignKeyType, relationType, deleteBehavior }` | `{ manifest, contentHash }` | 200 |
| `DELETE /api/v1/projects/{id}/relations/{relationName}` | – | `{ manifest, contentHash }` | 200 |
| `POST /api/v1/projects/{id}/plan-diff` | – | `{ planHash, manifestHash, hasChanges, fileChanges[] }` | 200 |
| `POST /api/v1/projects/{id}/apply` | `{ planHash?, runBuild, createMigration, migrationName? }` | `{ jobId, planHash, createdFiles, modifiedFiles, manifest }` | 200 |
| `POST /api/v1/projects/{id}/build` | `{ configuration }` | `{ jobId }` | 202 |
| `POST /api/v1/projects/{id}/migrations` | `{ name, startupProject?, efProject? }` | `{ jobId }` | 202 |
| `GET /api/v1/jobs/{jobId}/events` | – | SSE `progress` / `log` / `done` / `error` | 200 `text/event-stream` |
| `POST /api/v1/db-import/test` | `{ provider, connectionString?, parameters?, filters? }` | `{ connected, provider }` | 200 |
| `POST /api/v1/db-import/introspect` | `{ provider, connectionString?, parameters?, filters? }` | `DatabaseSchemaSnapshot` | 200 |
| `POST /api/v1/projects/{id}/db-import/apply` | `{ snapshot?, provider?, connectionString?, parameters?, filters?, selectedTables?, tableEntityOverrides?, applyImmediately }` | `{ manifest, contentHash, jobId?, plan? }` | 200 |
| `GET /api/v1/projects/{id}/templates` | – | `{ activeMapper, templates[] }` | 200 |
| `PUT /api/v1/projects/{id}/templates/{mapper}/{name}` | `{ content }` | `{ mapper, name, isCustomOverride, content, renderedPreview }` | 200 |
| `GET /openapi/v1.json` | – | OpenAPI document | 200 (public) |

### Error contract
RFC 9457 `application/problem+json` with `extensions.errorCode`. Status mapping: `VALIDATION_ERROR`/`TEMPLATE_ERROR`/`ENTITY_EXISTS`/`PROPERTY_EXISTS`/`RELATION_EXISTS` → 400; `SOLUTION_NOT_FOUND`/`ENTITY_NOT_FOUND`/`PROPERTY_NOT_FOUND`/`RELATION_NOT_FOUND` → 404; `PATH_OUTSIDE_ALLOWLIST`/`INVALID_HOST`/`ORIGIN_NOT_ALLOWED`/`CSRF_INVALID` → 403; `UNAUTHORIZED`/`AUTH_TOKEN_INVALID`/`AUTH_TOKEN_CONSUMED` → 401; `MANIFEST_CONFLICT` → 409; `PROCESS_FAILED`/`IO_ERROR`/`HTTP_ERROR` → 422/500.

### Wire-format gotchas (must be handled explicitly)
- JSON is **camelCase**, `null` properties are **omitted** (`DefaultIgnoreCondition = WhenWritingNull`), and enums are **strings** (`JsonStringEnumConverter`). `PlanFileDiffDto.kind` is `"Create" | "Modify" | "Delete"`, not a number.
- `ManifestEntity.Applied` / `ManifestProperty.Applied` / `ManifestRelation.Applied` are the only draft/applied discriminator — there is no separate draft list.
- `WorkspaceSummaryDto.pendingChangesCount` and `hasDrift` are computed server-side; do not recompute in the UI.
- Supported property CLR types (`DataTypes.SupportedTypes`): `Guid`, `int`, `long`, `string`, `decimal`, `float`, `bool`, `DateTime`, `byte[]`, `TimeSpan`. The property editor is a closed select; no free text.
- Mappers: `AutoMapper`, `Mapster` only (`MapperKind`).

---

## Detailed Design Specification

### 1. Project setup (`src/Mma.Cli.Web/`)

**Files**
- `package.json`, `package-lock.json`
- `vite.config.ts`, `tsconfig.json`, `tsconfig.node.json`, `index.html`
- `eslint.config.js`, `.prettierrc`, `.env.example`
- `src/main.tsx`, `src/App.tsx`, `src/styles/globals.css`

**Stack (pinned majors, caret ranges)**
- `react@^19`, `react-dom@^19`, `react-router-dom@^7`
- `typescript@^5`, `vite@^6`, `@vitejs/plugin-react`
- `tailwindcss@^4` + `@tailwindcss/vite`
- `@tanstack/react-query@^5`, `zustand@^5`
- `@xyflow/react@^12`, `@dagrejs/dagre@^1`
- `shiki@^3`, `diff@^8`
- `lucide-react`, `clsx`, `tailwind-merge`, `class-variance-authority`, `@radix-ui/*` primitives as needed
- `vitest@^3`, `@vitest/coverage-v8`, `jsdom`, `@testing-library/react`, `@testing-library/user-event`, `@testing-library/jest-dom`, `msw@^2`
- `@playwright/test@^1`, `openapi-typescript@^7`

**Scripts (exact contract)**
```json
{
  "dev": "vite",
  "build": "tsc -b && vite build",
  "preview": "vite preview --port 5173 --strictPort",
  "typecheck": "tsc -b --noEmit",
  "lint": "eslint .",
  "test": "vitest run",
  "test:watch": "vitest",
  "e2e": "playwright test",
  "generate:api": "openapi-typescript http://127.0.0.1:5180/openapi/v1.json -o src/api/schema.d.ts"
}
```

**`vite.config.ts`**
- `server: { host: "127.0.0.1", port: 5173, strictPort: true, proxy: { "/api": { target: "http://127.0.0.1:5180", changeOrigin: false }, "/openapi": { target: "http://127.0.0.1:5180", changeOrigin: false } } }`
- `build.rollupOptions.output.manualChunks` splitting `flow` (`@xyflow/react` + `dagre`), `shiki`, and `diff` into separate chunks.
- A tiny post-build assertion script (`scripts/check-bundle-budget.mjs`) that gzips the entry chunk and exits non-zero above 300 KB.

**`tsconfig.json`** — `target: ES2022`, `module: ESNext`, `moduleResolution: Bundler`, `jsx: react-jsx`, `strict: true`, `noUncheckedIndexedAccess: true`, `noImplicitOverride: true`, `exactOptionalPropertyTypes: true`, `noUnusedLocals`, `noUnusedParameters`, `verbatimModuleSyntax`.

**Directory layout (authoritative; no `ui/` anywhere)**
```text
src/Mma.Cli.Web/src/
├── api/            client.ts, schema.d.ts, problem.ts, sse.ts, queryKeys.ts
├── stores/         authStore.ts, workspaceStore.ts, diffStore.ts
├── components/
│   ├── primitives/     button, input, select, dialog, tabs, badge, tooltip, table, toast
│   ├── layout/         AppShell, TopBar, StatusBar
│   └── shared/         UnifiedDiffViewer, LogTerminal, EmptyState, ErrorBanner, StatusPill
├── features/
│   ├── projects/       WorkspaceHub, WorkspaceCard, NewSolutionModal, DirectoryPickerModal, RegisterSolutionModal
│   ├── studio/         SolutionStudio, EntityRail, InspectorPanel
│   ├── erd/            ErdCanvas, EntityNode, RelationEdge, layout.ts
│   ├── entities/       EntityTableView, PropertyEditor, EntityForm
│   ├── relations/      RelationDrawer
│   ├── db-import/      DbImportWizard, ConnectionStep, TableSelectionStep, EntityNamingStep, PreviewStep
│   ├── migrations/     PlanDiffDrawer, ApplyDialog, MigrationPanel
│   ├── templates/      TemplateExplorer
│   └── settings/       SettingsPlaceholder  (Phase 5 stub only)
└── test/           setup.ts, msw/handlers.ts, msw/server.ts
```

### 2. API layer

**`src/api/schema.d.ts`** — generated only; never hand-edited. Commit it so `tsc` works without a running BFF.

**`src/api/client.ts`**
- `class ApiProblem extends Error { status: number; errorCode: string; detail?: string; title?: string }`
- `request<T>(path, { method, body, signal })`:
  - `fetch(path, { credentials: "include", headers })`
  - adds `Content-Type: application/json` only when `body` is present
  - adds `X-Mma-Csrf: <token>` for non-GET when a token exists
  - on `!response.ok`: parse `application/problem+json` (fall back to text), throw `ApiProblem`
  - on 204/empty body: return `undefined as T`
- Typed helpers `api.get<T>`, `api.post<T>`, `api.put<T>`, `api.del<T>`.
- A single `onUnauthorized` hook (set by `authStore`) so a `401` clears auth state and re-triggers the session bootstrap once (no infinite loop).

**`src/api/sse.ts`**
- `openJobStream(jobId, { onEvent, onDone, onError })` using `EventSource("/api/v1/jobs/{id}/events")` (GET + cookie, so no CSRF needed).
- Parse `event:`/`data:` frames into `{ eventId, timestamp, type, jobOrSessionId, payload }`.
- `EventSource` cannot send headers, so resume is by re-subscribing: on error, retry up to 3 times with 500 ms/1 s/2 s backoff; the BFF replays its bounded history per job.
- Always close the stream on `done`/`error` and on component unmount.

**`src/api/queryKeys.ts`** — `workspaces`, `workspace(id)`, `project(id)`, `templates(id)`, `health`.

### 3. State

**`authStore` (Zustand)**
- `{ status: "unknown" | "authenticated" | "anonymous", csrfToken: string | null, displayName: string, bootstrap(): Promise<void> }`
- `bootstrap()` calls `GET /api/v1/auth/session` on app mount; if `?token=` is present in the URL, it first calls `POST /api/v1/auth/bootstrap` and then removes the query param via `history.replaceState`.
- `csrfToken` lives in memory only (never `localStorage`).

**`workspaceStore`**
- `{ activeView: "erd" | "table", selectedEntity: string | null, selectedRelation: string | null, inspectorOpen: boolean, planOpen: boolean, terminalJobId: string | null }`
- Selection is keyed by entity **name** within the active project; reset on `projectId` change.

**TanStack Query**
- `staleTime: 5_000`, `retry: 0` for mutations, `retry: 2` for queries.
- Every manifest mutation returns `{ manifest, contentHash }` and MUST write it into the `project(id)` cache (`setQueryData`) — do not refetch blindly — then invalidate `workspaces` so badges refresh.
- `plan-diff` result is cached under `["plan", projectId]` and invalidated by any manifest mutation and by `apply`.

### 4. Routes

| Path | Screen | Notes |
| --- | --- | --- |
| `/` | Workspace Hub | cards + empty state + register/new |
| `/projects/:projectId` | Solution Studio | ERD/Table tabs, inspector, plan drawer |
| `/projects/:projectId/db-import` | Import Wizard | or modal launched from studio |
| `/projects/:projectId/templates` | Template Explorer | read + save overrides |
| `/settings` | Phase 5 placeholder | no API calls; text "Provider & MCP settings arrive in Phase 5" |
| `*` | Not found | link back to `/` |

### 5. Feature acceptance criteria

**5.1 Workspace Hub (`/`)**
- `GET /api/v1/workspaces` on load; loading skeletons; empty state with two CTAs.
- Card shows name, solution path (middle-truncated, full path in `title`), mapper badge, `entityCount`, `relationCount`, `pendingChangesCount` badge, `Drift` pill when `hasDrift`, and a disabled/"Missing on disk" state when `!existsOnDisk`.
- Actions: Open (navigate), Export (POST export → toast), Remove from list (not implemented in BFF — omit, do not fake it).
- `RegisterSolutionModal` uses `DirectoryPickerModal` to pick a folder, then `POST /workspaces/register`; on 404 `SOLUTION_NOT_FOUND` show inline error naming `.sln`/`.slnx`.

**5.2 New Solution**
- Fields: parent directory (picker), solution name (validated non-empty, no path separators), mapper (`AutoMapper` | `Mapster`), dry-run toggle.
- `POST /workspaces`; when `dryRun` the response has `workspace: null` — show the `GenerationSummary` diff summary in a read-only panel instead of navigating.
- On success navigate to `/projects/{workspace.id}` and show created file count from `summary`.

**5.3 Directory Picker**
- `GET /fs/browse?path=`; list directories first, then files; `.sln`/`.slnx` entries flagged via `isSolution` and selectable.
- Breadcrumb from `currentPath`; "Up" disabled when `parentPath` is null; roots selector from `allowedRoots`.
- 403 `PATH_OUTSIDE_ALLOWLIST` renders "That folder is outside the allowed workspace roots" with the offending path, never a raw stack.

**5.4 Solution Studio**
- `GET /projects/{id}`; header shows solution name, mapper, manifest hash (short, copyable), validation banner when `validation.isValid === false` listing `issues[].severity/code/message/target`.
- Left rail: entities with `Draft`/`Applied` pill and pending property/relation counts; search filter.
- Tabs: `ERD` (default) and `Table`; tab state in `workspaceStore`.

**5.5 ERD canvas**
- Nodes = `manifest.entities`; edges = `manifest.relations` (`parentEntity` → `childEntity`, label = `foreignKey`, dashed when `applied === false`).
- Layout: `@dagrejs/dagre` `rankdir: "TB"`, `nodesep: 60`, `ranksep: 90`, node size 240×dynamic-height; memoized per `manifest.contentHash`-equivalent (use `JSON.stringify(entities.map(e => e.name))` + property count as the memo key).
- Node card: name, PK badge (`pkType`), property rows (name : type, FK icon when `isForeignKey`, `?` when `nullable`, `maxLength` chip), and a status pill.
- Interactions: pan/zoom, minimap, fit-view control; click node → select + open inspector; `Add relation` mode: first click sets parent, second sets child, then `RelationDrawer` opens prefilled with `foreignKey = "${parentName}Id"`, `foreignKeyType = parent.pkType`, `deleteBehavior = "Cascade"`, `relationType = "1:N"`.
- Empty state: "No entities yet" + `Add entity`.
- Pure layout function `computeLayout(entities, relations)` exported for unit testing.

**5.6 Entity table + property editor**
- Table view lists entities; expanding one shows its properties with inline add/remove.
- `Add entity`: `POST /entities` with `{ name, pkType, hasService, hasApi }`; client-side validation: unique name (case-insensitive) against the current manifest, non-empty, C# identifier regex.
- `Add property`: closed type select (10 types), `nullable`, `isForeignKey`, `maxLength` (positive int), `required` (tri-state: unset/true/false).
- `Remove property` / `Remove entity` / `Remove relation`: confirm dialog stating whether the item is `Draft` (manifest-only) or `Applied` (schedules code removal), then `DELETE`.
- Every mutation replaces cached manifest via `setQueryData` and shows a toast with the returned `contentHash` short form.

**5.7 Plan / diff / apply**
- `Preview changes` → `POST /plan-diff`; when `hasChanges === false` show "No pending changes" and disable Apply.
- `PlanDiffDrawer`: file list grouped by `kind` with counts, `planHash` displayed and copyable; selecting a file renders its `unifiedDiff` in `UnifiedDiffViewer`.
- `UnifiedDiffViewer` parses the unified diff with `diff` (`parsePatch`), renders add/remove/context lines with `shiki` highlighting via a **lazy-loaded** highlighter; falls back to plain monospace while loading.
- `Apply` → `ApplyDialog` (options: `runBuild`, `createMigration`, `migrationName` required when the latter is on) → `POST /apply` with the cached `planHash`; on 409 `MANIFEST_CONFLICT` close the dialog, invalidate the plan, and show "The manifest changed — preview again".
- On success: invalidate project + workspaces, open `LogTerminal` with the returned `jobId`.

**5.8 Streaming terminal & migrations**
- `LogTerminal` subscribes via `openJobStream`; renders `progress` (stage label + spinner), `log` (monospace, auto-scroll with a "stick to bottom" toggle), `done` (green summary from payload), `error` (red, keeps partial logs).
- `Build` and `Migrations` buttons issue the 202 call, then attach the returned `jobId` to the terminal.
- Terminal is dismissible; closing it must close the `EventSource`.

**5.9 Templates**
- `GET /templates` → list grouped by name with an `Override` pill when `isCustomOverride`.
- Editor (textarea or Monaco-lite; keep bundle small) with Save → `PUT /templates/{mapper}/{name}`; render `renderedPreview` in a read-only panel after save.
- 400 `TEMPLATE_ERROR` renders the server detail inline above the editor without losing typed content.

**5.10 DB import wizard**
- Step 1 Connect: provider select (`SqlServer` | `PostgreSql` | `Sqlite` | `MySql`), either connection string or structured `parameters` (`serverOrPath`, `database`, `username`, `password`, `port`, `sqlServerAuth`, `trustServerCertificate`); `Test connection` → `POST /db-import/test`.
- Step 2 Introspect: `POST /db-import/introspect`; show tables with column counts and detected relations; per-table entity-name override input; exclusion lists are server defaults (`SchemaFilterOptions`) — expose only `includeSystemTables` and table overrides, not the full exclusion editor.
- Step 3 Review: selected tables + overrides summary, `applyImmediately` toggle.
- Step 4 Apply: `POST /projects/{id}/db-import/apply`; if `applyImmediately` and `jobId` is returned, open the terminal; always update the manifest cache from the response.
- SQLite: `serverOrPath` must be inside an allowed root; a 403 `PATH_OUTSIDE_ALLOWLIST` shows the root hint from `GET /fs/browse`.
- Never persist or log the password; clear it on step change and on unmount.

### 6. Styling & layout
- Dark/light via `prefers-color-scheme` plus a manual toggle persisted in `localStorage` (theme only — never tokens).
- App shell: top bar (product name, project breadcrumb, health dot from `GET /health`), left rail, main region, status bar (manifest hash, pending count, SSE state).
- Tokens in `globals.css` as CSS variables; primitives use `class-variance-authority` variants. No inline hex colours in components.
- Responsive: ≥1280 full three-pane; 768–1279 collapsible rail; <768 table view only, ERD shows a notice.

### 7. Tests (write first, verify red, then implement)

**Vitest (`src/Mma.Cli.Web/src/**/*.test.ts(x)`)**
- `features/erd/layout.test.ts` — dagre layout: positions non-overlapping, edges reference existing nodes, self-relation handled without throwing.
- `components/shared/UnifiedDiffViewer.test.tsx` — parses create/modify/delete patches, counts add/remove lines, renders without highlighter.
- `api/client.test.ts` (MSW) — attaches `X-Mma-Csrf` on POST, omits it on GET, maps `problem+json` → `ApiProblem` with `errorCode`, handles empty 204 body, `onUnauthorized` fires once on 401.
- `api/sse.test.ts` — frame parsing, `done` closes the stream, error backoff stops after 3 attempts.
- `stores/workspaceStore.test.ts` — selection reset on project change, tab persistence.
- `features/entities/PropertyEditor.test.tsx` — type select is closed to the 10 supported types; `maxLength` rejects 0/negative; tri-state `required`.
- `features/projects/WorkspaceCard.test.tsx` — drift/pending/missing-on-disk pills render text, not colour only.
- `features/migrations/PlanDiffDrawer.test.tsx` — empty plan disables Apply; `planHash` shown; 409 path surfaces the conflict message.

**Playwright (`e2e/workspace.spec.ts`)**
- Config launches two servers: `dotnet run --project src/Mma.Cli.Bff -c Release` with `MMA_BFF_DEV_AUTH=1`, `MMA_BFF_ALLOWED_ROOTS=<temp>`, `Bff__WorkspaceDbPath=<temp>/workspace.db`, `ASPNETCORE_URLS=http://127.0.0.1:5180`; and `npm run dev` on `127.0.0.1:5173`. `reuseExistingServer: !process.env.CI`.
- Flow: load `/` → create solution in the temp root → add `Order` and `OrderItem` → add `Title:string` to `Order` → add relation `Order → OrderItem` via `OrderId` → Preview changes (assert non-empty file list incl. `Order.cs`) → Apply with `runBuild=false` → assert terminal reaches `done` → assert both entity nodes show `Applied` → assert the manifest hash changed.
- Second test: attempt `Add entity` with a duplicate name → inline validation, no network call (assert via `page.route` count or MSW-less request spy).
- Third test: open `/settings` → Phase 5 placeholder, zero `/api/v1/providers` requests.

### 8. `.gitignore` additions (only file outside the web project)
```
src/Mma.Cli.Web/dist/
src/Mma.Cli.Web/playwright-report/
src/Mma.Cli.Web/test-results/
src/Mma.Cli.Web/.vite/
src/Mma.Cli.Web/*.local
src/Mma.Cli.Web/.env
src/Mma.Cli.Web/.env.*
!src/Mma.Cli.Web/.env.example
```

---

## Workflow & Verification Steps

1. **Scaffold + red tests.** Create the project skeleton, `schema.d.ts` (from a running BFF), MSW handlers, and the Vitest/Playwright tests above. Run them and record the failures.
2. **Implement** §2–§6 until `npm run typecheck`, `npm run lint`, and `npm test` are green.
3. **Contract check.** With the BFF running (`MMA_BFF_DEV_AUTH=1`), run `npm run generate:api` and confirm `schema.d.ts` is unchanged or only additive; re-run `typecheck`.
4. **E2E.** `npx playwright test` against the two-server config; record pass counts.
5. **No-.NET-regression gate.** `dotnet test Mma.Cli.sln --no-build -c Release` must stay `331/331` (Task 3.2 touches no .NET code; a change here means scope leaked).
6. **Budget gate.** `npm run build && node scripts/check-bundle-budget.mjs` → exit 0; record entry chunk gzip size.
7. **Hygiene.** `git status --short` shows only `src/Mma.Cli.Web/**` and `.gitignore`; `Get-ChildItem -Recurse -Directory src | Where-Object { $_.Name -ieq 'ui' }` returns empty; no `.env`/secret files staged.
8. **Commit** (explicit paths only, never `git add -A`):
   `feat(web): add React 19 + TypeScript workspace SPA with ERD canvas and diff viewer`
9. **Report** to `.sdd-phase3/task-3.2-report.md`: commit SHA, file inventory, red→green evidence, `typecheck`/`lint`/`test`/`e2e`/`build`/budget results, the `331/331` .NET check, and any deviation from this brief.

## Expected File Inventory (minimum)

- Root: `package.json`, `package-lock.json`, `vite.config.ts`, `tsconfig.json`, `tsconfig.node.json`, `index.html`, `eslint.config.js`, `.env.example`, `playwright.config.ts`, `scripts/check-bundle-budget.mjs`
- `src/api/` (5), `src/stores/` (3), `src/components/{primitives,layout,shared}` (~14), `src/features/{projects,studio,erd,entities,relations,db-import,migrations,templates,settings}` (~20), `src/test/` (3), `src/**/*.test.*` (≥8), `e2e/workspace.spec.ts`
- Modified: `.gitignore`

## Out of Scope (do not implement in 3.2)

- Copying `dist/` into `Mma.Cli.Bff/wwwroot/`, MSBuild targets, `mma ui` rewire, CI workflow edits, Blazor/`commands.json` deletion — all Task 3.3.
- Provider profiles, Copilot drawer, MCP settings — Phase 5.
- Template pack registry, 3-way drift detector, command palette, undo/redo — backlog (`08`, C1.5/C2.6/C3.3).
