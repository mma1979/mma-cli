# 01 — Executive Summary & Current-State Technical Assessment

> **Epistemic Status Legend**
> - **Verified Fact**: Directly observed in repository source files, `.csproj` manifests, `.gitignore`, or `dotnet test` output during this session.
> - **Assumption**: Explicitly agreed target-state constraint (accepted via `"proceed"`).
> - **Recommendation**: Architectural proposal or engineering design decision derived from verified facts and agreed assumptions.

---

## 1. Executive Vision & Modernization Objectives

**MMA CLI** (`mma-cli`, current package version `4.5.8` on `.NET 10`) is a developer-operated .NET global tool for scaffolding ASP.NET Core solutions (with AutoMapper or Mapster), modeling entities, properties, and relationships, reverse-engineering SQL Server schemas, and launching a local Blazor Server UI (`Mma.Cli.UI`).

This blueprint modernizes MMA CLI along **three primary pillars** supported by **essential core engineering prerequisites**:

1. **Pillar 1 — React + Backend-for-Frontend (BFF) UI Redesign**: Replace the Blazor Server / Radzen UI (`Mma.Cli.UI`) with an **ASP.NET Core 10 local-first BFF** (`Mma.Cli.Bff`) serving an embedded **Vite + React 19 + TypeScript + Tailwind + shadcn/ui** single-page application (`Mma.Cli.Web`), adding an interactive ERD canvas, dry-run diff preview, streaming build/migration logs, and strict localhost security hardening—while preserving zero-Node.js end-user execution inside the `mma` .NET global tool.
2. **Pillar 2 — Model Context Protocol (MCP) Capability**: Expose MMA's scaffolding, inspection, manifest validation, database introspection, and migration tooling as a first-class **MCP Server** (`Mma.Cli.Mcp`) using the official C# MCP SDK (`ModelContextProtocol` + `ModelContextProtocol.AspNetCore`) over both **`stdio`** (`mma mcp`) and **Streamable HTTP** (`/mcp` on the BFF). In Phase 2, add an **MCP Client** (`McpClientManager`) so MMA's own AI agent can call external MCP servers.
3. **Pillar 3 — AI Agent Mode with Configurable Providers**: Introduce a unified **AI Agent runtime** (`Mma.Cli.Agent`) shared by `mma agent` (CLI/TUI) and the React Copilot drawer, built on **`Microsoft.Extensions.AI`** (`IChatClient`, `AIFunction`) and **`Microsoft.Agents.AI`** (Microsoft Agent Framework). Support **OpenAI, Anthropic, Google Gemini, Azure OpenAI, Ollama, and OpenAI-compatible** endpoints with OS keyring / environment-variable secret hygiene, plan/diff previews, and mandatory human-in-the-loop (HITL) approval gates.
4. **Foundational Prerequisites (Core Refactors)**: Extract a headless `Mma.Cli.Core` engine free of `Environment.Exit`, `Console`, and `string[] args`; fix critical security and data-corruption bugs; replace brittle line-offset C# file patching with Roslyn syntax rewriting + Scriban templates; and unify `.mma/project.mma` and SQLite workspace persistence.

---

## 2. Active Assumptions

1. **Plan-only first**: This deliverable produces the complete architecture, specs, ADRs, phased plan, and backlog in `outputs/20261120T205500Z_1ea6d98d_v1/` without mutating existing source or `docs/architecture/`.
2. **Packaging & Runtime**: `mma ui` remains a local-first tool packaged into the `mma-cli` NuGet tool (zero Node.js required on user machines), designed with clean session/origin/auth boundaries for future remote or containerized hosting.
3. **MCP Scope**: Dual-transport MCP Server first (Phase 4), followed by MCP Client integration inside the AI Agent runtime (Phase 6).
4. **Agent Safety Default**: Domain-tool-driven execution with dry-run diff preview and HITL approval by default; arbitrary file edits and shell execution are disabled unless explicitly opted in.
5. **Backward Compatibility**: Existing `mma new` / `mma n`, `mma generate` / `mma g e|p|r`, `mma import`, `mma ui`, and `.mma/templates/` overrides remain supported or cleanly migrated.

---

## 3. Verified Current-State Baseline

| Aspect | Verified Observation | Source / Evidence |
| --- | --- | --- |
| **Solution & Target Framework** | 4 projects in `Mma.Cli.sln`, all targeting `net10.0` (`Mma.Cli.AppV4`, `Mma.Cli.Shared`, `Mma.Cli.UI`, `Mma.Cli.Tests`) | [Mma.Cli.sln](file:///d:/workspaces/my/mma-cli/Mma.Cli.sln) |
| **CLI Packaging** | `PackAsTool=true`, `ToolCommandName=mma`, `AssemblyName=mma-cli`, `Version=4.5.8`, `GeneratePackageOnBuild=true` | [Mma.Cli.AppV4.csproj](file:///d:/workspaces/my/mma-cli/Mma.Cli.AppV4/Mma.Cli.AppV4.csproj#L3-L19) |
| **UI Technology** | Blazor Server + `Radzen.Blazor 8.4.2`, empty `ProjectsController.cs`, Windows-only `after-build.bat` | [Mma.Cli.UI.csproj](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Mma.Cli.UI.csproj#L11-L25), [ProjectsController.cs](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Controllers/ProjectsController.cs) |
| **Shared Layer Dependencies** | `EF Core Sqlite 10.0.1`, `Microsoft.AspNetCore.Mvc.NewtonsoftJson 10.0.1`, `RestSharp 113.0.0`, `SharpZipLib 1.4.2` | [Mma.Cli.Shared.csproj](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Mma.Cli.Shared.csproj#L9-L15) |
| **Test Baseline** | `dotnet test Mma.Cli.sln --no-restore --verbosity minimal` exited 0: **8 passed, 0 failed, 0 skipped (605 ms)** covering `BuildHelperTests` (5), `TemplateHelperTests` (2), `UnitTest1` (1) | [BuildHelperTests.cs](file:///d:/workspaces/my/mma-cli/Mma.Cli.Tests/BuildHelperTests.cs), [TemplateHelperTests.cs](file:///d:/workspaces/my/mma-cli/Mma.Cli.Tests/TemplateHelperTests.cs) |
| **NuGet Security Audit** | `NU1903`: `SQLitePCLRaw.lib.e_sqlite3` `2.1.11` has high-severity advisory `GHSA-2m69-gcr7-jv3q` | `dotnet test` output |
| **Compiler Warnings** | 15 warnings: 12x `CS8618` (`PropertiesBuilder.cs`), 2x `CS8602` (`ConfigHelper.cs`), 1x `SYSLIB0014` (`ImportFactory.cs`) | `dotnet test` output |
| **CI/CD Workflows** | `.github/` contains zero workflow files | Directory scan of `.github/` |
| **Repo Hygiene Hazards** | `.gitignore:35` ignores `[Uu]i/` (must avoid naming frontend folder `ui/`); stale `build_error.txt` tracked in root | [.gitignore](file:///d:/workspaces/my/mma-cli/.gitignore#L35), [build_error.txt](file:///d:/workspaces/my/mma-cli/build_error.txt) |

---

## 4. Risk & Technical Debt Audit Matrix (Verified Facts)

### 4.1 Security & Data Exposure Risks

| ID | Severity | Verified Finding | Evidence (`file:line`) | Impact on Modernization | Resolved In |
| --- | --- | --- | --- | --- | --- |
| **SEC-01** | **Critical** | Global TLS certificate validation disabled via `ServicePointManager.ServerCertificateValidationCallback += (...) => true` | [ImportFactory.cs:23](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/ImportFactory.cs#L23) | Disables HTTPS certificate validation across the entire process (CLI, BFF, MCP, LLM HTTP clients). | **Phase 0** |
| **SEC-02** | **Critical** | Wide-open CORS (`AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`) on local UI server without auth or origin validation | [Mma.Cli.UI/Program.cs:45-48](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Program.cs#L45-L48) | Any malicious browser tab can issue cross-origin mutations, read local filesystem paths, or trigger `dotnet ef`. | **Phase 3** |
| **SEC-03** | **High** | SQLite workspace DB stored at `wwwroot\data\mma_cli.db` while `app.UseStaticFiles()` is enabled | [appsettings.json:11](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/appsettings.json#L11), [Mma.Cli.UI/Program.cs:53](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Program.cs#L53) | Exposes workspace SQLite DB (and potentially connection details) via HTTP static file serving; wiped on tool upgrade. | **Phase 1 / 3** |
| **SEC-04** | **High** | High-severity NuGet vulnerability `NU1903` in `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (`GHSA-2m69-gcr7-jv3q`) | `dotnet test` output | Known vulnerable native SQLite binary shipped in tool package. | **Phase 0** |
| **SEC-05** | **Medium** | `ImportDatabaseDialog` forces `TrustServerCertificate = true` and maps `"Windows Authentication"` to `SqlAuthenticationMethod.ActiveDirectoryIntegrated` | [ImportDatabaseDialog.razor:85-87](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/ImportDatabaseDialog.razor#L85-L87) | Breaks standard Windows Integrated Auth (`IntegratedSecurity=true`) and silently trusts unverified SQL Server certs. | **Phase 2 / 3** |

### 4.2 Process Stability & Host-Coupling Risks

| ID | Severity | Verified Finding | Evidence (`file:line`) | Impact on Modernization | Resolved In |
| --- | --- | --- | --- | --- | --- |
| **REL-01** | **Critical** | Shared builders/helpers call `Environment.Exit(...)` directly on validation/path errors | [PropertiesBuilder.cs:41,67](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/PropertiesBuilder.cs#L41), [RelationsBuilder.cs:37,61](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/RelationsBuilder.cs#L37), [BuildHelper.cs:55,129](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Helpers/BuildHelper.cs#L55) | One bad argument crashes the BFF web server, MCP server, or AI Agent host process. | **Phase 1** |
| **REL-02** | **Critical** | `OnCancelKeyPress` kills **every** `dotnet` and `mma-cli` process on the workstation | [Mma.Cli.AppV4/Program.cs:42-65](file:///d:/workspaces/my/mma-cli/Mma.Cli.AppV4/Program.cs#L42-L65) | Pressing `Ctrl+C` in `mma` terminates IDEs, background APIs, tests, and unrelated `.NET` servers. | **Phase 0** |
| **REL-03** | **High** | `Output.cs` mutates global `Console.ForegroundColor` without resetting; `Program.cs:180` clears console on every UI stdout line | [Output.cs:42-58](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Helpers/Output.cs#L42-L58), [Program.cs:179-182](file:///d:/workspaces/my/mma-cli/Mma.Cli.AppV4/Program.cs#L179-L182) | Breaks `stdio` MCP JSON-RPC framing if anything writes to stdout; cannot stream logs to React UI or Agent. | **Phase 1 / 4** |
| **REL-04** | **High** | `Mma.Cli.Shared.csproj` pulls `Microsoft.AspNetCore.Mvc.NewtonsoftJson` and uses `Newtonsoft.Json` + `RestSharp` | [Mma.Cli.Shared.csproj:12-13](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Mma.Cli.Shared.csproj#L12-L13) | Bloats CLI dependencies and couples shared domain logic to ASP.NET MVC. | **Phase 1** |

### 4.3 Code Generation Correctness Bugs

| ID | Severity | Verified Finding | Evidence (`file:line`) | Impact on Modernization | Resolved In |
| --- | --- | --- | --- | --- | --- |
| **GEN-01** | **Critical** | `PropertiesBuilder` hardcodes `.cs` for `{EntityName}ModifyModel.cs`/`ReadModel.cs`, whereas Mapster `EntityBuilder` writes `.g.cs` | [PropertiesBuilder.cs:93,110,126,146](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/PropertiesBuilder.cs#L93), [EntityBuilder.cs:95,115](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/EntityBuilder.cs#L95) | Adding/removing properties on Mapster solutions throws `FileNotFoundException`. | **Phase 0 / 2** |
| **GEN-02** | **Critical** | `RelationsBuilder` targets `{Entity}Dto.cs` (non-existent; models are `ModifyModel`) and removes from `ParentEntityReadModel.cs` instead of `ChiledEntityReadModel.cs` | [RelationsBuilder.cs:87,138,171,190](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/RelationsBuilder.cs#L87) | Relation scaffolding fails or mutates the wrong file. | **Phase 0 / 2** |
| **GEN-03** | **High** | Brittle line-offset C# editing via `lines[^2]` and `index + 10` | [PropertiesBuilder.cs:96,208,224](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/PropertiesBuilder.cs#L96), [RelationsBuilder.cs:253,269,376,392](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/RelationsBuilder.cs#L253) | Corrupts user-formatted code or custom templates; prevents dry-run AST diffs. | **Phase 2** |
| **GEN-04** | **High** | Unchecked `args[mapperFlagIndex + 1]` array indexing throws `IndexOutOfRangeException` when `--mapper` is omitted or trailing | [EntityBuilder.cs:54-55,73-74](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/EntityBuilder.cs#L54-L55), [SolutionBuilder.cs:39-40](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/SolutionBuilder.cs#L39-L40) | `mma g e Customer long` crashes unless `--mapper` is explicitly provided despite README claiming default. | **Phase 0 / 1** |
| **GEN-05** | **Medium** | Solution scanning checks only `*.sln` (ignores `.NET 10` `.slnx`), `SolutionBuilder` contains `"axialaystem-covaluse"` typo, EF config hardcodes SQL Server types (`newid()`, `varbinary(max)`) | [BuildHelper.cs:49](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Helpers/BuildHelper.cs#L49), [SolutionBuilder.cs:218](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/SolutionBuilder.cs#L218), [PropertiesBuilder.cs:307-316](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/PropertiesBuilder.cs#L307-L316) | Blocks `.slnx` solutions and non-SQL-Server target databases. | **Phase 1 / 2** |

### 4.4 UI State, Concurrency & Packaging Risks

| ID | Severity | Verified Finding | Evidence (`file:line`) | Impact on Modernization | Resolved In |
| --- | --- | --- | --- | --- | --- |
| **UI-01** | **Critical** | `List.ForEach(async ...)` in `MigrateAndApply` and `ImportDatabase` launches concurrent async-void delegates sharing one scoped `CliDbContext` | [Workspace.razor:420-447,592](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/Workspace.razor#L420-L447) | `InvalidOperationException` on EF Core context, lost exceptions, and `dotnet ef migrations add` firing before codegen finishes. | **Phase 1 / 3** |
| **UI-02** | **High** | `MigrateAndApply` filters `!e.Applied`, skipping new properties/relations added to already-applied entities | [Workspace.razor:415](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/Workspace.razor#L415) | Incremental schema edits silently fail to generate code. | **Phase 1 / 3** |
| **UI-03** | **High** | Relation lookup queries `ctx.Entities.FirstOrDefault(e => e.EntityName == ...)` without `ProjectId` filter | [Workspace.razor:629-630](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/Workspace.razor#L629-L630), [CliData.cs:27-28](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Data/CliData.cs#L27-L28) | Corrupts multi-project workspaces by linking entities across different solutions. | **Phase 1** |
| **UI-04** | **Medium** | Windows-only path concatenation (`\\`), hardcoded `http://localhost:5000`, and manual `after-build.bat` packaging | [NewProject.razor:129-138,234-235](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/NewProject.razor#L129-L138), [after-build.bat](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/after-build.bat) | Breaks macOS/Linux developers and risks publishing stale UI assets in NuGet `.nupkg`. | **Phase 3** |
| **UI-05** | **Medium** | Two split sources of truth (`mma_cli.db` SQLite vs. `.mma/project.mma`) + mixed in-process calls vs. `Cli.Wrap("mma")` shell-outs | [ProjectDumber.cs:18-83](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Data/ProjectDumber.cs#L18-L83), [EntityGrid.razor:145](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Components/EntityGrid.razor#L145) | Changes made via CLI/MCP/Agent drift out of sync with the UI. | **Phase 1** |

---

## 5. Legacy Modernization Strategy

Following the **Strangler Fig** and **Branch-by-Abstraction** patterns:

1. **Characterization First (Phase 0)**: Lock current template outputs and builder behaviors behind xUnit golden-file tests before rewriting internals, while applying zero-risk security/crash fixes (`NU1903`, `ServicePointManager`, `OnCancelKeyPress`, `PropertiesBuilder`/`RelationsBuilder` bugs).
2. **Extract Headless Core (Phase 1)**: Move all domain logic behind typed command/service interfaces (`Mma.Cli.Core`) returning structured `OperationResult<T>` values and emitting `IProgress<GenerationEvent>`, making `.mma/project.mma` v2 the canonical source of truth.
3. **Upgrade Codegen Engine (Phase 2)**: Swap line-index patching for Roslyn syntax rewriters and Scriban templates while keeping full backward compatibility with `.mma/templates/<mapper>/<Template>.cs` overrides and introducing `DryRunDiffEngine`.
4. **Build BFF + React Alongside Legacy UI (Phase 3)**: Implement `Mma.Cli.Bff` + `Mma.Cli.Web` cleanly against `Mma.Cli.Core` (avoiding `ui/` folder due to `.gitignore:35`), verify feature parity + ERD/Diff upgrades via Playwright E2E, then swap `mma ui` to launch the BFF and delete `Mma.Cli.UI` + `after-build.bat`.
5. **Layer MCP & AI Agent on Identical Core Tools (Phases 4–6)**: Because `Mma.Cli.Core` already exposes typed, dry-runnable, cancellation-aware operations, both `Mma.Cli.Mcp` and `Mma.Cli.Agent` become thin, auditable protocol/orchestration layers rather than duplicated code generators.
