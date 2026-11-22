# SDD ledger — plan: outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md

- **Phase**: Phase 0 — Safety Net, Security Hotfixes & CI Foundation
- **Branch**: `agentic-v1`
- **INITIAL_BASE**: `2242b2e22de759860f14ed3ee4dcb38a9e8e4fc5`
- **Active Persona & Skills**: `software-architect` + `subagent-driven-development` + `legacy-modernizer` + `writing-plans`

## Preflight Conflict Scan
- **Task 0.1** edits `Directory.Packages.props`, `Directory.Build.props`, `Mma.Cli.*/Mma.Cli.*.csproj`, and removes `build_error.txt`.
- **Task 0.2** edits `Mma.Cli.AppV4/Program.cs`, `Mma.Cli.Shared/Builders/*.cs`, `Mma.Cli.Shared/Helpers/ConfigHelper.cs`, and adds `Mma.Cli.Tests/LegacyBuilderCharacterizationTests.cs`.
- **Task 0.3** adds `.github/workflows/ci.yml`.
- **Sequencing**: Task 0.2 and Task 0.3 rely on Central Package Management and zero `NU1903` from Task 0.1, and Task 0.3 CI validates Task 0.2's new tests, so tasks execute strictly sequentially: `0.1` -> `0.2` -> `0.3` -> `Final Review`.
- **Ruling**: Do NOT set singular `<TargetFramework>` in `Directory.Build.props` because `Mma.Cli.AppV4.csproj` uses plural `<TargetFrameworks>net10.0</TargetFrameworks>`. Cost if wrong: immediate MSBuild conflict; resolved upfront in Task 0.1 brief.
- **Ruling (Task 0.1)**: Accept adding transitive pin `System.Security.Cryptography.Pkcs` `9.0.23` in `Directory.Packages.props` — `Microsoft.Data.SqlClient 6.1.3` transitively resolved vulnerable `9.0.4` (`NU1903` `GHSA-846f-f764-76mx` / `NU1902` `GHSA-2fmv-rv2c-59x7`) under `NuGetAuditMode=all`, which violated Global Constraint #4 (`dotnet build --warnaserror:NU1903`). Cost if wrong: unnecessary minor patch bump inside `9.0.x`; reversible in one line.
- **Ruling (Final Review)**: Keep `Mma.Cli.AppV4.csproj` without `<Nullable>enable</Nullable>` during Phase 0 and enforce `<Nullable>enable</Nullable>` on the replacement `src/Mma.Cli.App` project in Phase 1 Task 1.3 (avoids churn on legacy CLI entry point scheduled for replacement). Cost if wrong: nullable annotations on `Mma.Cli.AppV4/Program.cs` stay deferred until Phase 1 Task 1.3; low risk and isolated to the soon-to-be-replaced host.

## Task Checklist
- [x] Task 0.1: complete (commits `2242b2e..3179bb3`, review clean) — Central Package Management & `NU1903` SQLite + PKCS advisory fix
- [x] Task 0.2: complete (commits `3179bb3..b6ec62b`, review clean) — Security/process/builder fixes + 17 characterization/regression test cases (`25/25` passing)
- [x] Task 0.3: complete (commits `b6ec62b..35c43e8`, review clean) — Cross-platform GitHub Actions CI (`windows-latest` + `ubuntu-latest`)
- [x] Final Whole-Branch Review & Verification (`2242b2e..35c43e8`, review clean; `PHASE0_SPEC: PASS`, `WHOLE_BRANCH_QUALITY: APPROVED`)

## Phase 0 Completion Status
- **Status**: `COMPLETE`
- **Commit Range**: `2242b2e..35c43e8` on branch `agentic-v1`
  - `3179bb3` `chore(build): centralize packages and resolve NU1903 SQLite advisory`
  - `ec0d342` `fix(core): resolve global process kill, TLS bypass, and builder path bugs`
  - `b6ec62b` `fix(core): harden Ctrl+C shutdown, RemoveConfig guard, and TLS gating tests`
  - `35c43e8` `ci: add GitHub Actions build and test workflow`
- **Final Release Verification**:
  - `dotnet restore Mma.Cli.sln --force`: exit `0`, zero `NU1903` advisories
  - `dotnet build Mma.Cli.sln --no-restore -c Release --warnaserror:NU1903`: exit `0`, `0` errors, `0` `NU1903`, `0` warnings in `Mma.Cli.Shared`, `Mma.Cli.AppV4`, and `Mma.Cli.Tests` (`64` pre-existing warnings remain in legacy `Mma.Cli.UI`)
  - `dotnet test Mma.Cli.sln --no-build -c Release --verbosity normal`: exit `0`, `25 Passed, 0 Failed, 0 Skipped` (8 pre-existing + 17 characterization/regression test executions)
- **Tracked Follow-Up Notes (for Phase 1+)**:
  1. Enforce `<Nullable>enable</Nullable>` on new `src/Mma.Cli.App` (Phase 1 Task 1.3).
  2. Restrict loopback TLS opt-in to `Uri.UriSchemeHttps` and disable cross-host redirects (`FollowRedirects = false`) when migrating `ImportFactory` to `IHttpClientFactory` (Phase 1).
  3. Optionally add `actions/upload-artifact@v4` for `test-results.trx` in `.github/workflows/ci.yml` and compile scaffolded test solutions in Phase 2 Roslyn tests.
  4. Verify `ubuntu-latest` GitHub Actions CI execution upon branch push.
- **Next Step**: **Phase 1 — Headless `Mma.Cli.Core` Engine & Unified Manifest** (`outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md`)

---

# Phase 1 — Headless `Mma.Cli.Core` Engine, Unified `.mma/project.mma` v2 Manifest & `System.CommandLine` v2 CLI

- **INITIAL_BASE_P1**: `35c43e809288e25d14587092b12cf3f78cb0b78f`
- **Branch**: `agentic-v1`

## Preflight Conflict Scan (Phase 1)
- **Task 1.1** updates `Directory.Packages.props` and `Mma.Cli.sln`, creates `src/Mma.Cli.Core/` and `src/tests/Mma.Cli.Core.Tests/`.
- **Task 1.2** updates `Mma.Cli.sln`, extends `src/Mma.Cli.Core/` (adds `Manifest/`, `ProjectValidator`, wires lock+manifest sync into `SolutionService`, `EntityService`, `PropertyService`, `RelationService`), creates `src/Mma.Cli.Database/`, and adds tests to `src/tests/Mma.Cli.Core.Tests/`.
- **Task 1.3** updates `Directory.Packages.props` and `Mma.Cli.sln`, creates `src/Mma.Cli.App/`, and adds CLI tests to `src/tests/Mma.Cli.Core.Tests/`.
- **Sequencing**: Strictly sequential (`1.1` -> `1.2` -> `1.3` -> `Final Whole-Branch Review`) because Tasks 1.2 and 1.3 build on Task 1.1's projects, interfaces, and `Directory.Packages.props`.
- **Rulings**:
  - **Ruling (Embedded Zips)**: Link `Mma.Cli.Shared\Resources\AutoMapper.zip` and `Mappster.zip` into `src/Mma.Cli.Core/Mma.Cli.Core.csproj` as `<EmbeddedResource>` links with explicit `LogicalName` instead of duplicating binary zip blobs. Cost if wrong: build path adjustment if `Mma.Cli.Shared` moves in Phase 2/3; easy 2-line move later.
  - **Ruling (Cross-Platform Solution Lock)**: Implement `SolutionLockManager` using in-process `ConcurrentDictionary<string, SemaphoreSlim>` + cross-process `.mma/.lock` (`FileShare.None`) with bounded retry/jitter rather than OS-specific named `Mutex`. Cost if wrong: negligible; works cleanly across async awaits on Windows/macOS/Linux.
  - **Ruling (Package Pins)**: Pin `Microsoft.Extensions.*` at `10.0.1` (matching existing EF Core `10.0.1`), `System.CommandLine` at `2.0.15`, and `Spectre.Console` at `0.57.2`. Cost if wrong: minor patch bump inside `10.0.x` if transitive mismatch arises.
- **Task 1.2 Consolidated Fix Queue (for Phase 1 Final Whole-Branch Wave)**:
  1. Move `File.Exists(...)` checks inside `_lockManager.AcquireAsync(...)` in `PropertyService` and `RelationService` (matching `EntityService`), and guard `.mma/project.mma` read sharing.
  2. Update `existingRecord.EntityName = manifestEntity.Name` on case-only entity renames in `SqliteWorkspaceRegistry.SyncFromManifestAsync`.

## Phase 1 Task Checklist
- [x] Task 1.1: complete (commits `35c43e8..246951c`, review clean) — Headless `Mma.Cli.Core` Engine, `.sln`/`.slnx` Locator, `DotnetRunner`, `SolutionExporter`, and Services (`94/94` tests passing)
- [x] Task 1.2: complete (commits `246951c..2c0376f`, review clean) — Unified `.mma/project.mma` v2 Manifest Store (with BOM handling & fail-fast validation), Per-Solution Lock & Relocated SQLite Workspace DB (`Mma.Cli.Database`, `122/122` tests passing)
- [x] Task 1.3: complete (commits `2c0376f..abf1c5b`, review clean) — Modern CLI (`src/Mma.Cli.App`) on `System.CommandLine` `2.0.15` + `Spectre.Console` `0.57.2` (`144/144` tests passing)
- [x] Final Whole-Branch Review & Verification (`INITIAL_BASE_P1 = 35c43e8`..`7984b05`, consolidated fix wave review clean; `SPEC: PASS`, `QUALITY: APPROVED`)

## Phase 1 Completion Status
- **Status**: `COMPLETE`
- **Commit Range**: `35c43e8..7984b05` on branch `agentic-v1`
  - `1602d44` `feat(core): extract headless Mma.Cli.Core engine with slnx and dry-run support`
  - `246951c` `fix(core): preserve EntityConfig fluent chain, regex anchors, and exporter timeouts`
  - `120db56` `feat(core): add canonical project.mma v2 manifest store and AppData workspace DB`
  - `2c0376f` `fix(core): strip UTF-8 BOM on manifest load and validate manifest before writes`
  - `b990091` `feat(cli): migrate command routing to System.CommandLine v2 and Spectre.Console`
  - `abf1c5b` `fix(cli): escape Spectre prompt choices and expand CLI command tests`
  - `7984b05` `fix(phase1): resolve lock TOCTOU, shared BOM reads, registry casing, and property removal`
- **Final Release Verification**:
  - `dotnet build <proj> -c Release --no-incremental --warnaserror` on `Mma.Cli.Core`, `Mma.Cli.Database`, `Mma.Cli.App`, and `Mma.Cli.Core.Tests`: exit `0`, `0` Warning(s), `0` Error(s)
  - `dotnet restore Mma.Cli.sln --force`: exit `0`
  - `dotnet build Mma.Cli.sln --no-restore -c Release --warnaserror:NU1903`: exit `0`, `0` errors, `0 NU1903` advisories
  - `dotnet test Mma.Cli.sln --no-build -c Release --verbosity normal`: exit `0`, `154 Passed, 0 Failed, 0 Skipped` (`25/25` `Mma.Cli.Tests` + `129/129` `Mma.Cli.Core.Tests`)
- **Next Step**: **Phase 2 — Roslyn + Scriban Codegen, Dry-Run Diff Engine & Multi-DB Introspection** (`outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md`)


---

# Phase 2 — Roslyn + Scriban Codegen, Dry-Run Diff Engine & Multi-DB Introspection

- **INITIAL_BASE_P2**: `7984b05f80911332b70d466cde5b5fad93f7ebee`
- **Branch**: `agentic-v1`
- **Active Persona & Skills**: `software-architect` + `subagent-driven-development` + `writing-plans` + `senior-architect`

## Preflight Conflict Scan (Phase 2)
- **Task 2.1** updates `Directory.Packages.props` (`Scriban` `7.6.1`), `src/Mma.Cli.Core/Mma.Cli.Core.csproj`, `Contracts/Requests.cs`, `Templates/EmbeddedTemplateProvider.cs`, `Services/EntityService.cs`, `ServiceCollectionExtensions.cs`; creates `Templates/**/*.scriban`, `CodeGen/*` (`ExecutionPlan`, `IDryRunDiffEngine`, `DryRunDiffEngine`, `ITemplateRenderer`, `ScribanTemplateRenderer`); adds `TemplateCompatibilityGoldenTests.cs` & `DryRunDiffEngineTests.cs`.
- **Task 2.2** updates `Directory.Packages.props` (`Microsoft.CodeAnalysis.CSharp` `5.0.0`), `src/Mma.Cli.Core/Mma.Cli.Core.csproj`, `Services/EntityService.cs`, `Services/PropertyService.cs`, `Services/RelationService.cs`; creates `Roslyn/*` (`RoslynSyntaxHelper`, `ModelSyntaxRewriter`, `EntitySyntaxRewriter`, `EntityConfigSyntaxRewriter`, `DbContextSyntaxRewriter`); adds `RoslynRewriterTests.cs`.
- **Task 2.3** updates `Directory.Packages.props` (`Microsoft.Data.Sqlite` `10.0.1`, `Npgsql` `10.0.4`, `MySqlConnector` `2.6.2`), `src/Mma.Cli.Database/Mma.Cli.Database.csproj`, `src/Mma.Cli.Database/ServiceCollectionExtensions.cs`; creates `src/Mma.Cli.Database/Introspection/*`; adds `SqliteIntrospectorTests.cs` & `SqlServerAuthMappingTests.cs`.
- **Sequencing**: Strictly sequential (`2.1` -> `2.2` -> `2.3` -> `Final Whole-Branch Review`) because Task 2.2 builds on Task 2.1's `DryRunDiffEngine`/`ExecutionPlan` and touches overlapping `Mma.Cli.Core` files, and Tasks 2.1–2.3 share `Directory.Packages.props`.
- **Rulings**:
  - **Ruling (NuGet Pins)**: `Scriban` `7.6.1`, `Microsoft.CodeAnalysis.CSharp` `5.0.0`, `Microsoft.Data.Sqlite` `10.0.1` (aligns with `Microsoft.EntityFrameworkCore.Sqlite` `10.0.1`), `Npgsql` `10.0.4`, `MySqlConnector` `2.6.2`. Must verify 0 `NU1903` advisories.
  - **Ruling (Windows Auth & TLS)**: `SqlServerIntrospector` fixes legacy `ImportDatabaseDialog.razor:85-87` by mapping `WindowsIntegrated` -> `IntegratedSecurity = true`, keeping `TrustServerCertificate = false` by default unless explicitly requested.

## Phase 2 Task Checklist
- [x] Task 2.1: complete (commits `7984b05..99926a8`, review clean + Fix Round 1 `99926a8` `APPROVED`) — `DryRunDiffEngine` & Scriban Template Renderer (`195/195` tests passing)
- [x] Task 2.2: complete (commits `99926a8..99920d8`, review clean; `SPEC: PASS`, `QUALITY: APPROVED`) — Roslyn Syntax Rewriters for Incremental Entity/Property/Relation Edits (`209/209` tests passing)
- [x] Task 2.3: complete (commits `99920d8..f316aac`, review clean + Fix Round 1 `f316aac` `APPROVED`) — Multi-Database Async Introspector (`315/315` tests passing)
- [x] Final Whole-Branch Review & Verification (`INITIAL_BASE_P2 = 7984b05`..`f316aac`, review clean; `PHASE2_SPEC: PASS`, `WHOLE_BRANCH_QUALITY: APPROVED`)

## Phase 2 Completion Status
- **Status**: `COMPLETE`
- **Commit Range**: `7984b05..f316aac` on branch `agentic-v1`
  - `69406c3` `feat(codegen): add Scriban template renderer and DryRunDiffEngine`
  - `99926a8` `fix(codegen): validate FileChangeKind before commit and clean created directories on rollback`
  - `99920d8` `feat(codegen): replace line-offset patching with Roslyn syntax rewriters`
  - `3a7a564` `feat(db): add async multi-database schema introspection`
  - `f316aac` `fix(db): harden Postgres FK joins, TLS defaults, and URI secret redaction`
- **Final Release Verification**:
  - `dotnet build <proj> -c Release --no-incremental --warnaserror` on `Mma.Cli.Core`, `Mma.Cli.Database`, `Mma.Cli.App`, and `Mma.Cli.Core.Tests`: exit `0`, `0` Warning(s), `0` Error(s)
  - `dotnet restore Mma.Cli.sln --force`: exit `0`
  - `dotnet build Mma.Cli.sln --no-restore -c Release --warnaserror:NU1903`: exit `0`, `0` Error(s), `0 NU1903` advisories
  - `dotnet test Mma.Cli.sln --no-build -c Release --verbosity normal`: exit `0`, `315 Passed, 0 Failed, 0 Skipped` (`25/25` `Mma.Cli.Tests` + `290/290` `Mma.Cli.Core.Tests`)
- **Non-Blocking Notes (Future Backlog / Phase 3+):**
  1. `RoslynRewriterTests`: add explicit unit coverage for file-scoped `namespace ...;` and `partial class` entities; `EntitySyntaxRewriter`: expand expression-bodied `Update(...) => ...` if user customizes to expression body.
  2. `DryRunDiffEngine.CommitAsync`: optionally re-check duplicate `FullPath` in hand-built `ExecutionPlan`s in Phase 1.
  3. `SqlServerIntrospector.ColumnsSqlQuery`: optionally convert `nvarchar`/`nchar` `col.max_length` byte length (`/ 2`) to character length.
  4. `RelationService`: support self-referencing 1:N (`ParentEntity == ChildEntity`) by merging parent/child edits if needed later.
  5. `CliApp`: optionally surface `GenerationSummary.Plan` unified diffs/hash in `mma g ... --dry-run` output.
- **Next Step**: **Phase 3 — ASP.NET Core 10 BFF & React 19 SPA (Strangler Fig Swap)** (`outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md`)


---

# Phase 3 — ASP.NET Core 10 BFF & React 19 SPA (Strangler Fig Swap)

- **INITIAL_BASE_P3**: `f316aac2837157c67d76db8f6c9aec1f53063479`
- **Branch**: `agentic-v1`
- **Active Persona & Skills**: `software-architect` + `subagent-driven-development` + `writing-plans` + `senior-architect`

## Preflight Conflict Scan (Phase 3)
- **Task 3.1** updates `Directory.Packages.props` (`Microsoft.AspNetCore.OpenApi` `10.0.1`, `Microsoft.AspNetCore.Mvc.Testing` `10.0.1`) and `Mma.Cli.sln`; creates `src/Mma.Cli.Bff/**` and `src/tests/Mma.Cli.Bff.Tests/**`.
- **Task 3.2** updates `.gitignore`; creates `src/Mma.Cli.Web/**` (React 19 + TypeScript 5 + Vite 6 + Tailwind + `@xyflow/react` + `@dagrejs/dagre` + TanStack Query + Zustand + Vitest + Playwright).
- **Task 3.3** updates `src/Mma.Cli.Bff/Mma.Cli.Bff.csproj`, `src/Mma.Cli.Bff/Program.cs`, `src/Mma.Cli.App/Mma.Cli.App.csproj`, `src/Mma.Cli.App/Commands/UiCommand.cs`, `src/tests/Mma.Cli.Core.Tests/CliCommandRoutingTests.cs`, `.github/workflows/ci.yml`, `Mma.Cli.sln`, and `Directory.Packages.props`; retires `Mma.Cli.UI/**` and `commands.json`.
- **Sequencing**: Strictly sequential (`3.1` -> `3.2` -> `3.3` -> `Final Whole-Branch Review`) because Task 3.2 builds against Task 3.1's OpenAPI schema & BFF routes, and Task 3.3 packages Task 3.2's `dist/` into Task 3.1's `Mma.Cli.Bff` and `Mma.Cli.App`.
- **Rulings**:
  - **Ruling (`.gitignore:35` `[Uu]i/` Trap)**: Never create any folder named `ui` or `Ui` inside `src/Mma.Cli.Web/` (use `src/components/primitives/` instead of `components/ui/`) or under `src/Mma.Cli.App/` (use `bff/`).
  - **Ruling (`Mma.Cli.Bff` Security)**: Bind strictly to `127.0.0.1` by default, validate `Host` & `Origin`, require single-use bootstrap token -> `HttpOnly; SameSite=Strict` cookie + `X-Mma-Csrf`, and enforce `WorkspacePathGuard` (`AllowedRoots` + NUL/symlink escape checks).

## Phase 3 Task Checklist
- [x] Task 3.1: complete (commits `f316aac..adcd404`, review clean) - `Mma.Cli.Bff` Endpoints, SSE Job Runner & Security Middleware (`331/331` tests passing)
- [ ] Task 3.2: implementation complete; verification incomplete - .NET suite passed 331/331 once with temporary host-safe settings, but reruns hit host/file access failures; required workspace validators are absent - report: `.sdd-phase3/task-3.2-report.md`
- [ ] Task 3.3: Pending - Cross-Platform Packaging Swap & Retire `Mma.Cli.UI`
- [ ] Final Whole-Branch Review & Verification (`INITIAL_BASE_P3 = f316aac`..`HEAD`)

### Task 3.1 Completion Status
- **Status**: `COMPLETE`
- **Commit**: `adcd404` `feat(bff): implement ASP.NET Core 10 BFF APIs, SSE streams, and localhost security`
- **Files**: 22 (2 modified, 20 added) - `src/Mma.Cli.Bff/**`, `src/tests/Mma.Cli.Bff.Tests/**`, `Directory.Packages.props`, `Mma.Cli.sln`
- **Verification**:
  - `dotnet build <proj> -c Release --no-incremental --warnaserror` on `Core`, `Database`, `Bff`, `App`, `Core.Tests`, `Bff.Tests`: `0 Warning(s), 0 Error(s)`
  - `dotnet restore Mma.Cli.sln --force` + `dotnet build Mma.Cli.sln -c Release --warnaserror:NU1903`: exit `0`, `0 NU1903`
  - `dotnet test Mma.Cli.sln --no-build -c Release`: `331 Passed, 0 Failed, 0 Skipped` (`25` + `290` + `16`)
- **Deviations**: `Microsoft.AspNetCore.OpenApi` pinned at `10.0.15` instead of planned `10.0.1` to clear `GHSA-v5pm-xwqc-g5wc` (transitive `Microsoft.OpenApi 2.0.0`). Documented in `.sdd-phase3/task-3.1-report.md`.
- **Next Step**: **Task 3.2 - React 19 + TypeScript Workspace (`Mma.Cli.Web`)** per `.sdd-phase3/task-3.2-brief.md`

### Task 3.2 Implementation Status (2026-11-21)
- **Status**: `IMPLEMENTED; VERIFICATION INCOMPLETE` — frontend checks pass; the .NET suite passed 331/331 once with host-safe overrides, but subsequent runs were unstable and required workspace validators are absent.
- **Implementation**: `src/Mma.Cli.Web/**` and `.gitignore` only. Task 3.3 dirty files were left untouched. No staging or commit performed per the standing task restriction.
- **Verified**: `npm run typecheck`, `npm run lint`, `npm test` (`11` files / `24` tests), Playwright with a fresh BFF and installed Edge (`3/3`), `npm run build` (entry `104.3 KB` gzip; `300 KB` limit; budget PASS), `npm run generate:api`, Windows `dotnet restore` + `dotnet build` (0 errors, 64 warnings in legacy `Mma.Cli.UI`), and no `ui/Ui` source directory or forbidden TypeScript escape hatches.
- **.NET test evidence**: `331/331` passed once after temporarily setting `MSTest.EnableParentProcessQuery=false` in ignored runtimeconfigs, redirecting `TEMP`/`TMP` into ignored workspace storage, and suppressing EventLog. A repeat failed one legacy directory-move test with `Access is denied`; restoring runtimeconfigs to `true` reproduced the testhost parent-handle `Win32Exception (5)`. The Linux-container and isolated Windows runs failed `Cli_SpecialCharacterPathWithBrackets_EscapesSpectreMarkupCleanly` at `CliCommandRoutingTests.cs:345`. Runtimeconfigs were restored; no .NET source was modified.
- **Pending**: stable 331-test run and the absent workspace/harness validators; user-selected normal-shell result has not been provided. Read-only tracing suggests the bracket-path assertion needs the full captured writer output to diagnose, and `csharp-ls` cannot load MSBuild because this host lacks SDK 10.
- **Report**: `.sdd-phase3/task-3.2-report.md`.
- **Next Step**: Obtain a stable normal-PowerShell 331-test run and resolve the absent workspace/harness validator requirement; only then complete Task 3.2 and continue to Task 3.3 with user authorization.
