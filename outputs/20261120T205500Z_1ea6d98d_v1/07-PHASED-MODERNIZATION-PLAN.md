# 07 — Phased Modernization Implementation Plan

> **For agentic workers:** Use `subagent-driven-development` (recommended, because 7 cross-cutting phases share strict `Mma.Cli.Core` / OpenAPI / MCP / `IChatClient` contracts where a fresh per-task reviewer prevents silent contract drift) or `executing-plans` (native single-branch execution). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modernize MMA CLI from a Blazor Server / string-patching `.NET 10` tool into a clean, layered platform featuring a headless `Mma.Cli.Core` engine, Roslyn/Scriban codegen with dry-run diffs, an ASP.NET Core 10 BFF + React 19 UI, a dual-transport MCP Server/Client (`Mma.Cli.Mcp`), and a configurable multi-provider AI Agent (`Mma.Cli.Agent`).

**Architecture:** Strangler Fig migration starting with a characterization test safety net and critical security fixes (Phase 0), extracting a host-agnostic `Mma.Cli.Core` engine and canonical `.mma/project.mma` v2 manifest (Phase 1), upgrading codegen to Roslyn AST rewriters + Scriban + multi-DB introspection (Phase 2), swapping Blazor `Mma.Cli.UI` for `Mma.Cli.Bff` + `Mma.Cli.Web` (Phase 3), and layering `Mma.Cli.Mcp` and `Mma.Cli.Agent` over the exact same typed core operations (Phases 4–6).

**Tech Stack:** `.NET 10`, `System.CommandLine` 2.0, `Spectre.Console`, `Microsoft.CodeAnalysis.CSharp`, `Scriban`, `EF Core Sqlite`, `SqlClient`/`Npgsql`/`MySqlConnector`, `ASP.NET Core 10` Minimal APIs + OpenAPI, React 19 + TypeScript 5 + Vite 6 + Tailwind + shadcn/ui + `@xyflow/react` + TanStack Query + Zustand, `ModelContextProtocol` + `ModelContextProtocol.AspNetCore`, `Microsoft.Extensions.AI` + `Microsoft.Agents.AI` (`OpenAI`, `Azure.AI.OpenAI`, `Anthropic`, `Google.GenAI`, `OllamaSharp`), xUnit, Vitest, Playwright.

**Specs:**
- [01-EXECUTIVE-SUMMARY-AND-ASSESSMENT.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/01-EXECUTIVE-SUMMARY-AND-ASSESSMENT.md)
- [02-C4-TARGET-ARCHITECTURE.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/02-C4-TARGET-ARCHITECTURE.md)
- [03-ARCHITECTURE-DECISIONS-ADRS.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/03-ARCHITECTURE-DECISIONS-ADRS.md)
- [04-SPEC-REACT-BFF-UI.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/04-SPEC-REACT-BFF-UI.md)
- [05-SPEC-MCP-CAPABILITY.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/05-SPEC-MCP-CAPABILITY.md)
- [06-SPEC-AI-AGENT-MODE.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/06-SPEC-AI-AGENT-MODE.md)

## Global Constraints

- All .NET projects target `net10.0` with `Nullable=enable` and Central Package Management (`Directory.Packages.props`).
- End users installing `mma-cli` via `dotnet tool install -g mma-cli` must NOT require Node.js installed at runtime.
- Existing CLI commands (`mma new` / `mma n`, `mma generate entity|property|relation` / `mma g e|p|r`, `mma import`, `mma ui`) remain backward-compatible.
- Zero `Environment.Exit(...)` or direct `Console.*` calls inside shared/core/MCP/agent libraries.
- Never create a directory named `ui/` or `UI/` for the new React app because `.gitignore:35` ignores `[Uu]i/`.
- Cross-platform paths only (`Path.Combine`, `Path.DirectorySeparatorChar` — zero hardcoded `\\` or Windows `.bat` packaging scripts).
- Zero plaintext API keys or credentials committed or saved in `.mma/config.json` or stored inside `wwwroot/`.
- Every phase ends with green `dotnet test` (plus `npm test` / Playwright when frontend changes) and zero high-severity NuGet advisories.

## Review Focus (Top 5 Failure Modes to Pin with Tests)

1. **MCP `stdio` JSON-RPC stream corruption**: Any stray `Console.WriteLine`, EF Core console log, or `dotnet` stdout leak breaks `mma mcp`. *Pinned by:* `McpStdioFramingTests` asserting zero non-JSON-RPC bytes on stdout during DB/solution operations.
2. **Mapster `.g.cs` vs. AutoMapper `.cs` & legacy `.mma/templates/` overrides**: Roslyn/Scriban upgrades must preserve `.g.cs` vs. `.cs` naming and support legacy `$EntityName` override tokens. *Pinned by:* `TemplateCompatibilityGoldenTests` across both mappers and custom `.mma/templates/` overrides.
3. **Concurrent manifest/disk mutations across CLI, BFF, MCP, and Agent**: Simultaneous mutations could corrupt `.mma/project.mma` or overwrite files. *Pinned by:* `ManifestLockConcurrencyTests` verifying per-solution lock serialization and `planHash` optimistic concurrency errors.
4. **Cross-project entity collisions & partial batch import failures**: Querying by `EntityName` without `ProjectId` (`CliData.cs:27-28`) or throwing mid-batch leaves orphaned files. *Pinned by:* `MultiProjectIsolationTests` and `AtomicApplyRollbackTests`.
5. **Localhost security & cloud LLM credential leakage**: CSRF/DNS rebinding on BFF/MCP HTTP, `..` traversal in `/fs/browse`, or leaking DB connection strings to cloud LLMs. *Pinned by:* `BffSecurityMiddlewareTests` and `AgentContextRedactionTests`.

---

## Phase 0 — Safety Net, Security Hotfixes & CI Foundation

**Objective:** Eliminate active process-killing/TLS/NuGet vulnerabilities, fix confirmed `PropertiesBuilder`/`RelationsBuilder` crashes, establish characterization tests before structural refactoring, and add GitHub Actions CI.
**Rollback Strategy:** Each task is a self-contained commit on a feature branch; revert individual commits if any regression occurs.

### Task 0.1: Add Central Package Management & Fix `NU1903` SQLite Advisory

**Files:**
- Create: `Directory.Packages.props`, `Directory.Build.props`
- Modify: `Mma.Cli.Shared/Mma.Cli.Shared.csproj`, `Mma.Cli.AppV4/Mma.Cli.AppV4.csproj`, `Mma.Cli.UI/Mma.Cli.UI.csproj`, `Mma.Cli.Tests/Mma.Cli.Tests.csproj`
- Delete: `build_error.txt`

- [ ] **Step 1: Verify current `NU1903` warning reproduces**
  Run: `dotnet restore Mma.Cli.sln --force`
  Expected: `NU1903` warning citing `SQLitePCLRaw.lib.e_sqlite3 2.1.11`.
- [ ] **Step 2: Create `Directory.Packages.props` and upgrade `SQLitePCLRaw` / EF Core SQLite packages to patched non-vulnerable versions**
  Enable `ManagePackageVersionsCentrally=true` and `NuGetAudit=true`. Remove `build_error.txt`.
- [ ] **Step 3: Verify clean restore & zero `NU1903` warnings**
  Run: `dotnet build Mma.Cli.sln --warnaserror:NU1903`
  Expected: Exit 0, 0 `NU1903` warnings.
- [ ] **Step 4: Commit** `chore(build): centralize packages and resolve NU1903 SQLite advisory`

### Task 0.2: Fix Critical Process/TLS Security & Stability Bugs (`SEC-01`, `REL-02`, `GEN-01/02/04`)

**Files:**
- Modify: `Mma.Cli.AppV4/Program.cs:42-65`, `Mma.Cli.Shared/Builders/ImportFactory.cs:23`, `Mma.Cli.Shared/Builders/EntityBuilder.cs:54-55,73-74`, `Mma.Cli.Shared/Builders/SolutionBuilder.cs:39-40,218`, `Mma.Cli.Shared/Builders/PropertiesBuilder.cs:93,110,126,146`, `Mma.Cli.Shared/Builders/RelationsBuilder.cs:87,138,171,190`
- Create: `Mma.Cli.Tests/LegacyBuilderCharacterizationTests.cs`

**Interfaces:**
- Consumes: Existing `SolutionBuilder`, `EntityBuilder`, `PropertiesBuilder`, `RelationsBuilder` public methods.
- Produces: Characterization test suite locking AutoMapper + Mapster solution/entity/property/relation outputs before Phase 1 extraction.

- [ ] **Step 1: Write failing characterization tests (`LegacyBuilderCharacterizationTests.cs`)**
  Test: `EntityBuilder_WithoutMapperFlag_DefaultsToAutoMapper`, `PropertiesBuilder_MapsterSolution_UpdatesDotGCsModels`, `RelationsBuilder_RemoveRelation_UpdatesChildReadModel`.
- [ ] **Step 2: Run tests to confirm failure**
  Run: `dotnet test Mma.Cli.Tests --filter LegacyBuilderCharacterizationTests`
  Expected: FAIL (`IndexOutOfRangeException` / `FileNotFoundException`).
- [ ] **Step 3: Apply surgical fixes**
  - Replace `KillProcessesByName("dotnet", "mma-cli")` in `Program.cs:42-65` with tracked child `CancellationTokenSource` disposal.
  - Remove global `ServicePointManager.ServerCertificateValidationCallback` bypass in `ImportFactory.cs:23` (opt-in `AllowInsecureLocalhostTls` flag on `HttpClientHandler` only when explicitly configured).
  - Add bounds checks for `--mapper` flag (`mapperFlagIndex >= 0 && mapperFlagIndex + 1 < args.Length`) in `EntityBuilder` and `SolutionBuilder`, and fix `"axialaystem-covaluse"` typo in `SolutionBuilder.cs:218`.
  - Resolve `.g.cs` vs. `.cs` in `PropertiesBuilder` based on `Mapper == Mappers.Mapster`, and fix `ModifyModel.cs`/`ChiledEntityReadModel.cs` paths in `RelationsBuilder`.
- [ ] **Step 4: Run full test suite**
  Run: `dotnet test Mma.Cli.sln`
  Expected: PASS (all characterization + existing tests pass).
- [ ] **Step 5: Commit** `fix(core): resolve global process kill, TLS bypass, and builder path bugs`

### Task 0.3: Add GitHub Actions CI Pipeline

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create `.github/workflows/ci.yml` running `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build` on `windows-latest` and `ubuntu-latest`**
- [ ] **Step 2: Validate workflow YAML locally and via `dotnet test Mma.Cli.sln -c Release`**
- [ ] **Step 3: Commit** `ci: add GitHub Actions build and test workflow`

---

## Phase 1 — Headless `Mma.Cli.Core` Engine, Unified Manifest & CLI v2

**Objective:** Decouple generation from `string[] args`, `Environment.Exit`, and `Console.*`; establish `.mma/project.mma` v2 with locking as canonical source; modernize SQLite index location; migrate CLI to `System.CommandLine` v2 + `Spectre.Console`.
**Rollback Strategy:** Keep legacy `Mma.Cli.Shared` temporarily delegating to `Mma.Cli.Core` adapters until Phase 2 completes.

### Task 1.1: Create `Mma.Cli.Core` Project with Result Contracts & Progress Events

**Files:**
- Create: `src/Mma.Cli.Core/Mma.Cli.Core.csproj`, `src/Mma.Cli.Core/Results/OperationResult.cs`, `src/Mma.Cli.Core/Events/GenerationEvent.cs`, `src/Mma.Cli.Core/Contracts/*.cs`
- Modify: `Mma.Cli.sln`

**Interfaces:**
- Produces: `OperationResult<T>`, `GenerationError(Code, Message, Details)`, `GenerationEvent(Stage, Message, FilePath?)`, `ISolutionService`, `IEntityService`, `IPropertyService`, `IRelationService`, `IDotnetRunner`.

- [ ] **Step 1: Write unit tests verifying `BuildHelper` / `SolutionLocator` returns typed failures instead of calling `Environment.Exit` and detects both `*.sln` and `*.slnx`**
- [ ] **Step 2: Run test -> verify FAIL** (`dotnet test src/tests/Mma.Cli.Core.Tests`)
- [ ] **Step 3: Implement `Mma.Cli.Core` base contracts, `SolutionLocator` (`.sln` + `.slnx`), and cross-platform `DotnetRunner` (`dotnet build`, `dotnet ef`)**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(core): scaffold headless Mma.Cli.Core contracts and solution locator`

### Task 1.2: Implement Unified `.mma/project.mma` v2 Manifest Store, Per-Solution Lock & Relocated Workspace DB

**Files:**
- Create: `src/Mma.Cli.Core/Manifest/ProjectManifest.cs`, `src/Mma.Cli.Core/Manifest/ManifestStore.cs`, `src/Mma.Cli.Core/Manifest/SolutionLockManager.cs`, `src/Mma.Cli.Database/WorkspaceDbContext.cs`
- Test: `src/tests/Mma.Cli.Core.Tests/ManifestStoreTests.cs`, `src/tests/Mma.Cli.Core.Tests/ManifestLockConcurrencyTests.cs`

**Interfaces:**
- Consumes: `OperationResult<T>` from Task 1.1.
- Produces: `ManifestStore.LoadAsync(solutionPath, ct)`, `ManifestStore.SaveAtomicAsync(solutionPath, manifest, expectedHash, ct)`, `SolutionLockManager.AcquireAsync(solutionPath, ct)`.

- [ ] **Step 1: Write failing tests for v1 -> v2 manifest upgrade, `ProjectId`-scoped entity resolution, atomic file swap, and concurrent lock serialization (`ManifestLockConcurrencyTests`)**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `ManifestStore` (`System.Text.Json` source-gen/options), `SolutionLockManager`, and `WorkspaceDbContext` storing `workspace.db` in `Environment.GetFolderPath(LocalApplicationData)/mma-cli/`**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(core): add canonical project.mma v2 manifest store and AppData workspace DB`

### Task 1.3: Migrate CLI (`Mma.Cli.App`) to `System.CommandLine` v2 + `Spectre.Console`

**Files:**
- Create: `src/Mma.Cli.App/Mma.Cli.App.csproj`, `src/Mma.Cli.App/Program.cs`, `src/Mma.Cli.App/Commands/*.cs`, `src/Mma.Cli.App/Tui/InteractiveMenu.cs`
- Test: `src/tests/Mma.Cli.Core.Tests/CliCommandRoutingTests.cs`

**Interfaces:**
- Consumes: `ISolutionService`, `IEntityService`, `IPropertyService`, `IRelationService`, `ManifestStore`.
- Produces: `mma` binary supporting `new`/`n`, `generate`/`g` (`entity`/`e`, `property`/`p`, `relation`/`r`), `import`, `validate`, `doctor`, `--dry-run`, `--json`, and interactive `Spectre.Console` TUI.

- [ ] **Step 1: Write CLI parser & execution tests (`mma n`, `mma g e`, `mma g p --remove`, `mma validate --json`)**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `System.CommandLine` v2 command tree, `Spectre.Console` progress/tables, and `IHttpClientFactory`-based `SolutionExporter` (replacing `RestSharp` + `Newtonsoft.Json`)**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(cli): migrate command routing to System.CommandLine v2 and Spectre.Console`

---

## Phase 2 — Roslyn + Scriban Codegen, Dry-Run Diff Engine & Multi-DB Introspection

**Objective:** Replace fragile string-line-offset patching with Roslyn syntax rewriters, adopt Scriban templates with backward-compatible `.mma/templates/` support, add in-memory `DryRunDiffEngine`, and generalize `DatabaseEngine` across 4 RDBMS providers.

### Task 2.1: Implement `DryRunDiffEngine` & Scriban Template Renderer

**Files:**
- Create: `src/Mma.Cli.Core/CodeGen/DryRunDiffEngine.cs`, `src/Mma.Cli.Core/CodeGen/ScribanTemplateRenderer.cs`, `src/Mma.Cli.Core/Templates/**/*.scriban`
- Test: `src/tests/Mma.Cli.Core.Tests/TemplateCompatibilityGoldenTests.cs`, `src/tests/Mma.Cli.Core.Tests/DryRunDiffEngineTests.cs`

**Interfaces:**
- Produces: `DryRunDiffEngine.ComputePlanAsync(changes, ct) -> ExecutionPlan(PlanHash, FileChanges[])`, `DryRunDiffEngine.CommitAsync(plan, ct)`.

- [ ] **Step 1: Write failing golden tests verifying Scriban produces byte-compatible / AST-equivalent outputs for AutoMapper & Mapster and translates legacy `.mma/templates/` `$Token` overrides**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `ScribanTemplateRenderer` and `DryRunDiffEngine` (with backup/rollback on partial write failure)**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(codegen): add Scriban template renderer and DryRunDiffEngine`

### Task 2.2: Implement Roslyn Syntax Rewriters for Incremental Entity/Property/Relation Edits

**Files:**
- Create: `src/Mma.Cli.Core/Roslyn/EntitySyntaxRewriter.cs`, `src/Mma.Cli.Core/Roslyn/ModelSyntaxRewriter.cs`, `src/Mma.Cli.Core/Roslyn/DbContextSyntaxRewriter.cs`, `src/Mma.Cli.Core/Roslyn/EntityConfigSyntaxRewriter.cs`
- Test: `src/tests/Mma.Cli.Core.Tests/RoslynRewriterTests.cs`

**Interfaces:**
- Consumes: `DryRunDiffEngine`.
- Produces: Idempotent `AddProperty`, `RemoveProperty`, `AddRelation`, `RemoveRelation`, `RegisterEntityInDbContext` syntax operations resilient to user formatting/comments and Mapster `.g.cs` vs. AutoMapper `.cs`.

- [ ] **Step 1: Write failing Roslyn rewriter tests (formatted code, existing custom methods, add+remove idempotency, Mapster `.g.cs` & AutoMapper `.cs`)**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `CSharpSyntaxRewriter` classes using `Microsoft.CodeAnalysis.CSharp`**
- [ ] **Step 4: Run test -> verify PASS** (`dotnet test` verifies rewritten C# parses with 0 syntax diagnostics!)
- [ ] **Step 5: Commit** `feat(codegen): replace line-offset patching with Roslyn syntax rewriters`

### Task 2.3: Multi-Database Async Introspector (`Mma.Cli.Database`)

**Files:**
- Create: `src/Mma.Cli.Database/Introspection/IDatabaseIntrospector.cs`, `SqlServerIntrospector.cs`, `PostgresIntrospector.cs`, `SqliteIntrospector.cs`, `MySqlIntrospector.cs`, `SchemaNamingService.cs`
- Test: `src/tests/Mma.Cli.Core.Tests/SqliteIntrospectorTests.cs`, `src/tests/Mma.Cli.Core.Tests/SqlServerAuthMappingTests.cs`

**Interfaces:**
- Produces: `IDatabaseIntrospector.TestConnectionAsync(...)`, `IntrospectAsync(DatabaseIntrospectRequest, ct) -> DatabaseSchemaSnapshot`.

- [ ] **Step 1: Write failing tests for SQLite temp-DB introspection, configurable table/column exclusion lists, Windows Integrated Security connection string building (fixing `ImportDatabaseDialog.razor:85-87`), and plural/singular rules**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `IDatabaseIntrospector` implementations (`Microsoft.Data.SqlClient`, `Npgsql`, `Microsoft.Data.Sqlite`, `MySqlConnector`)**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(db): add async multi-database schema introspection`

---

## Phase 3 — ASP.NET Core 10 BFF & React 19 SPA (Strangler Fig Swap)

**Objective:** Build `Mma.Cli.Bff` + `Mma.Cli.Web`, verify end-to-end via xUnit + Playwright, configure cross-platform MSBuild packaging, and retire `Mma.Cli.UI` + `after-build.bat`.

### Task 3.1: Implement `Mma.Cli.Bff` Endpoints, SSE Job Runner & Security Middleware

**Files:**
- Create: `src/Mma.Cli.Bff/Mma.Cli.Bff.csproj`, `Program.cs`, `Security/LocalhostSecurityMiddleware.cs`, `Security/WorkspacePathGuard.cs`, `Endpoints/*.cs`, `Jobs/BackgroundJobCoordinator.cs`
- Test: `src/tests/Mma.Cli.Bff.Tests/BffSecurityMiddlewareTests.cs`, `src/tests/Mma.Cli.Bff.Tests/WorkspaceEndpointsTests.cs`

**Interfaces:**
- Consumes: `Mma.Cli.Core`, `Mma.Cli.Database`.
- Produces: OpenAPI v1 spec (`openapi.json`), REST `/api/v1/*`, SSE `/api/v1/jobs/{id}/events`, single-use bootstrap auth cookie + CSRF guard.

- [ ] **Step 1: Write failing `WebApplicationFactory` integration tests (`BffSecurityMiddlewareTests` covering disallowed `Origin`, missing CSRF header, `..` path traversal, valid bootstrap cookie flow, and `plan-diff` -> `apply`)**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `Mma.Cli.Bff` Minimal APIs, OpenAPI generation, `LocalhostSecurityMiddleware`, and `BackgroundJobCoordinator`**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(bff): implement ASP.NET Core 10 BFF APIs, SSE streams, and localhost security`

### Task 3.2: Build React 19 + TypeScript Workspace (`Mma.Cli.Web`)

**Files:**
- Create: `src/Mma.Cli.Web/package.json`, `vite.config.ts`, `src/api/*`, `src/features/{projects,erd,entities,relations,db-import,migrations,templates}/*`
- Test: `src/Mma.Cli.Web/src/**/*.test.tsx`, `src/Mma.Cli.Web/e2e/workspace.spec.ts`

**Interfaces:**
- Consumes: BFF `openapi.json` (via `openapi-typescript`) + SSE `/api/v1/jobs/{id}/events`.
- Produces: Prebuilt static bundle in `Mma.Cli.Bff/wwwroot/`.

- [ ] **Step 1: Scaffold Vite + React 19 + TS + Tailwind + shadcn/ui + `@xyflow/react` + TanStack Query + Zustand + Vitest + Playwright**
- [ ] **Step 2: Generate typed API client from `Mma.Cli.Bff` OpenAPI spec (`npm run generate:api`)**
- [ ] **Step 3: Implement Workspace Hub, New/Open Solution + Directory Picker, Interactive ERD Canvas & Entity Table Inspector, Relation Manager, Multi-DB Import Wizard, Dry-Run Unified Diff Drawer, and Streaming Migration Terminal**
- [ ] **Step 4: Run Vitest unit tests + Playwright E2E against live `Mma.Cli.Bff`**
  Run: `npm run test && npx playwright test`
  Expected: All component and E2E user flows pass (create solution -> add entity/property/relation -> preview diff -> apply -> inspect ERD).
- [ ] **Step 5: Commit** `feat(web): add React 19 + TypeScript workspace SPA with ERD canvas and diff viewer`

### Task 3.3: Cross-Platform Packaging Swap & Retire `Mma.Cli.UI`

**Files:**
- Modify: `src/Mma.Cli.Bff/Mma.Cli.Bff.csproj`, `src/Mma.Cli.App/Mma.Cli.App.csproj`, `src/Mma.Cli.App/Commands/UiCommand.cs`, `.github/workflows/ci.yml`
- Delete: `Mma.Cli.UI/**`, `commands.json`

- [ ] **Step 1: Add MSBuild targets building `Mma.Cli.Web` into `Mma.Cli.Bff/wwwroot/` and publishing `Mma.Cli.Bff` into `Mma.Cli.App/bff/`**
- [ ] **Step 2: Update `mma ui` command to launch `Mma.Cli.Bff` on `127.0.0.1` ephemeral port with bootstrap token and open default browser**
- [ ] **Step 3: Remove legacy `Mma.Cli.UI` and `commands.json` from solution & disk**
- [ ] **Step 4: Verify `dotnet pack` produces working global tool `.nupkg` containing `bff/` + `wwwroot/`**
  Run: `dotnet pack src/Mma.Cli.App -c Release`
  Expected: `.nupkg` created with exit 0; smoke test `mma ui --headless-check` exits 0.
- [ ] **Step 5: Commit** `refactor(ui): switch mma ui to BFF+React package and retire legacy Blazor UI`

---

## Phase 4 — MCP Server (`stdio` + Streamable HTTP)

**Objective:** Expose `Mma.Cli.Core` + `Mma.Cli.Database` over the Model Context Protocol (`stdio` and BFF `/mcp`).

### Task 4.1: Implement `Mma.Cli.Mcp` Tools, Resources & Prompts

**Files:**
- Create: `src/Mma.Cli.Mcp/Mma.Cli.Mcp.csproj`, `MmaMcpExtensions.cs`, `Tools/*.cs`, `Resources/*.cs`, `Prompts/*.cs`
- Test: `src/tests/Mma.Cli.Mcp.Tests/McpToolContractTests.cs`, `McpStdioFramingTests.cs`

**Interfaces:**
- Consumes: `Mma.Cli.Core`, `Mma.Cli.Database`, `ModelContextProtocol`.
- Produces: `AddMmaMcpServer(bool readOnly)` registering all `mma_*` tools, `mma://` resources, and 4 workflow prompts.

- [ ] **Step 1: Write failing MCP contract & stdio framing tests (including `dryRun: true` zero-mutation assertion and `--read-only` tool filtering)**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `[McpServerToolType]` tool classes with JSON Schema descriptions, MCP annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`), `mma://` resources, and prompts**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(mcp): implement Mma.Cli.Mcp server tools, resources, and prompts`

### Task 4.2: Wire `mma mcp` (`stdio`) and BFF `/mcp` (Streamable HTTP) + Run MCP Evals

**Files:**
- Modify: `src/Mma.Cli.App/Commands/McpCommand.cs`, `src/Mma.Cli.Bff/Program.cs`
- Create: `src/tests/Mma.Cli.Mcp.Tests/McpHttpTransportTests.cs`, `src/tests/Mma.Cli.Mcp.Tests/evals/mma_eval.xml`

- [ ] **Step 1: Write failing Streamable HTTP `/mcp` integration test + auth rejection test**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Wire `mma mcp` (`stderr`-only logging) and `app.MapMcp("/mcp")` behind `LocalhostSecurityMiddleware`**
- [ ] **Step 4: Run `dotnet test` + verify manually with `npx @modelcontextprotocol/inspector` + author 10-question `mma_eval.xml` benchmark**
- [ ] **Step 5: Commit** `feat(mcp): expose stdio and Streamable HTTP transports with evaluation suite`

---

## Phase 5 — Configurable Multi-Provider AI Agent Mode

**Objective:** Deliver `Mma.Cli.Agent` (`Microsoft.Extensions.AI` + `Microsoft.Agents.AI`), provider & secret configuration, `mma agent` CLI/TUI, and React Copilot Drawer with HITL plan/diff approval.

### Task 5.1: Implement `LlmProviderFactory`, `SecretResolver` & Config Guardrails

**Files:**
- Create: `src/Mma.Cli.Agent/Mma.Cli.Agent.csproj`, `Config/AgentConfigStore.cs`, `Secrets/SecretResolver.cs`, `Providers/LlmProviderFactory.cs`, `Middleware/ContextRedactor.cs`
- Test: `src/tests/Mma.Cli.Agent.Tests/ProviderFactoryTests.cs`, `SecretHygieneTests.cs`, `AgentContextRedactorTests.cs`

**Interfaces:**
- Produces: `ILlmProviderFactory.CreateChatClientAsync(string? profileId, CancellationToken ct) -> IChatClient`, `SecretResolver`, `ContextRedactor`.

- [ ] **Step 1: Write failing tests verifying OpenAI, Anthropic, Gemini, Azure OpenAI, Ollama, and OpenAI-compatible construction, rejecting plaintext keys in `.mma/config.json`, enforcing `local-only` privacy mode, and redacting connection strings**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `LlmProviderFactory`, `ChatClientBuilder` middleware pipeline, OS keyring/env `SecretResolver`, and `ContextRedactor`**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(agent): add multi-provider IChatClient factory and secret hygiene`

### Task 5.2: Implement `MmaAgentOrchestrator`, `ApprovalGate` & Session Persistence

**Files:**
- Create: `src/Mma.Cli.Agent/Orchestration/MmaAgentOrchestrator.cs`, `ApprovalGate.cs`, `Tools/AgentMmaToolkit.cs`
- Modify: `src/Mma.Cli.Database/WorkspaceDbContext.cs` (add `AgentSessions`, `AgentMessages`, `ToolAuditLogs`)
- Test: `src/tests/Mma.Cli.Agent.Tests/AgentOrchestratorLoopTests.cs`

**Interfaces:**
- Consumes: `ILlmProviderFactory`, `Mma.Cli.Core`, `DryRunDiffEngine`.
- Produces: Streaming `AgentStreamEvent` (`Token`, `ToolCall`, `PlanProposed`, `ApprovalRequired`, `Applied`, `BuildVerified`, `Error`).

- [ ] **Step 1: Write failing deterministic tests using scripted `FakeChatClient : IChatClient` (propose plan -> pause for HITL -> reject yields 0 writes -> approve executes & verifies build -> build-failure cap stops after 2 retries)**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `MmaAgentOrchestrator` over `Microsoft.Agents.AI` + `ApprovalGate` + SQLite session persistence**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(agent): implement HITL agent orchestrator and session persistence`

### Task 5.3: Wire `mma agent` / `mma config` CLI & React UI Copilot + Provider Settings

**Files:**
- Create: `src/Mma.Cli.App/Commands/AgentCommand.cs`, `src/Mma.Cli.App/Commands/ConfigCommand.cs`, `src/Mma.Cli.Bff/Endpoints/AgentEndpoints.cs`, `src/Mma.Cli.Bff/Endpoints/ProviderEndpoints.cs`, `src/Mma.Cli.Web/src/features/agent/*`, `src/Mma.Cli.Web/src/features/settings/*`
- Test: `src/tests/Mma.Cli.Bff.Tests/AgentEndpointsTests.cs`, `src/Mma.Cli.Web/e2e/agent-copilot.spec.ts`

- [ ] **Step 1: Write failing BFF SSE + Playwright E2E test for Copilot drawer (mock `IChatClient` -> plan card -> diff preview -> click Approve -> verify ERD updates)**
- [ ] **Step 2: Run tests -> verify FAIL**
- [ ] **Step 3: Implement CLI `mma agent` / `mma config provider`, BFF endpoints (`/api/v1/providers`, `/api/v1/agent/sessions/**`), and React Copilot Drawer + Provider Settings screen**
- [ ] **Step 4: Run `dotnet test` + `npm test` + `npx playwright test`**
- [ ] **Step 5: Commit** `feat(agent): expose AI Agent mode in CLI and React Copilot UI`

---

## Phase 6 — Phase 2 MCP Client Integration, Hardening & Documentation Sync

**Objective:** Connect MMA's AI Agent to external MCP servers (`McpClientManager`), add `mma mcp install` / `mma doctor` DX utilities, refresh `docs/architecture/` to match the new system, and cut `v5.0.0`.

### Task 6.1: External MCP Client (`McpClientManager`) & Trust Policy

**Files:**
- Create: `src/Mma.Cli.Mcp/Client/McpClientManager.cs`, `src/Mma.Cli.Mcp/Client/McpServerConfigStore.cs`
- Modify: `src/Mma.Cli.Agent/Tools/AgentMmaToolkit.cs`, `src/Mma.Cli.Bff/Endpoints/McpConfigEndpoints.cs`, `src/Mma.Cli.Web/src/features/settings/McpServersPage.tsx`
- Test: `src/tests/Mma.Cli.Mcp.Tests/McpClientBridgeTests.cs`

- [ ] **Step 1: Write failing test connecting `McpClientManager` to an in-process test MCP server, enforcing project `.mma/mcp.json` trust gate, and bridging tools into `AIFunction`**
- [ ] **Step 2: Run test -> verify FAIL**
- [ ] **Step 3: Implement `McpClientManager`, trust prompt/UI toggle, and React MCP Servers settings view**
- [ ] **Step 4: Run test -> verify PASS**
- [ ] **Step 5: Commit** `feat(mcp): integrate external MCP client tools into AI Agent runtime`

### Task 6.2: `mma doctor`, `mma mcp install` & Documentation/Release Readiness

**Files:**
- Modify: `docs/architecture/*.md`, `README.md`, `.github/workflows/release.yml`
- Create: `src/Mma.Cli.App/Commands/DoctorCommand.cs`, `src/Mma.Cli.App/Commands/McpInstallCommand.cs`

- [ ] **Step 1: Implement `mma doctor` (checks `.NET 10`, `dotnet-ef`, DB connectivity, configured providers) and `mma mcp install` (writes IDE configs cleanly)**
- [ ] **Step 2: Update `docs/architecture/` C4 diagrams and `README.md` to reflect the shipped React+BFF, MCP, and AI Agent architecture**
- [ ] **Step 3: Run full CI gate (`dotnet test`, `npm test`, `npx playwright test`, `dotnet pack -c Release`)**
- [ ] **Step 4: Commit** `docs(release): finalize v5.0.0 architecture docs, doctor diagnostics, and release workflow`
