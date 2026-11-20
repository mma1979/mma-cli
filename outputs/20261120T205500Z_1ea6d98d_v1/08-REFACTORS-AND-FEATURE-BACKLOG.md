# 08 — Comprehensive Refactors & Feature Backlog

> **Epistemic Status**: Category A and Category B items are anchored directly in **Verified Facts** from the repository audit (`file:line` cited). Category C represents prioritized **Recommendations** designed to maximize product value across the React+BFF UI, MCP ecosystem, AI Agent mode, and core code generator.

---

## Category A — Blocking Prerequisites & Critical Bug/Security Fixes (12 Items)

Must be resolved before or alongside Phase 0–1 so the BFF, MCP Server, and AI Agent do not inherit fatal crashes or security holes.

| # | Item | Verified Evidence (`file:line`) | Severity | Target Phase |
| --- | --- | --- | --- | --- |
| **A1** | Replace `Environment.Exit(...)` in shared library with typed `OperationResult<T>` | [PropertiesBuilder.cs:41,67](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/PropertiesBuilder.cs#L41), [RelationsBuilder.cs:37,61](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/RelationsBuilder.cs#L37), [BuildHelper.cs:55,129](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Helpers/BuildHelper.cs#L55) | Critical | Phase 1 |
| **A2** | Remove global `KillProcessesByName("dotnet", "mma-cli")` on Ctrl+C; dispose owned child processes only | [Mma.Cli.AppV4/Program.cs:42-65](file:///d:/workspaces/my/mma-cli/Mma.Cli.AppV4/Program.cs#L42-L65) | Critical | Phase 0 |
| **A3** | Remove global `ServicePointManager.ServerCertificateValidationCallback += (...) => true` TLS bypass (`SYSLIB0014`) | [ImportFactory.cs:23](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/ImportFactory.cs#L23) | Critical | Phase 0 |
| **A4** | Replace `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` with strict localhost `Origin`/`Host` + cookie/CSRF auth | [Mma.Cli.UI/Program.cs:45-48](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Program.cs#L45-L48) | Critical | Phase 3 |
| **A5** | Move SQLite DB out of `wwwroot\data\mma_cli.db` into OS `LocalApplicationData` (`workspace.db`) | [appsettings.json:11](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/appsettings.json#L11), [Mma.Cli.UI/Program.cs:53](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Program.cs#L53) | High | Phase 1 |
| **A6** | Resolve `NU1903` high-severity advisory (`GHSA-2m69-gcr7-jv3q`) on `SQLitePCLRaw.lib.e_sqlite3 2.1.11` | `dotnet test` output | High | Phase 0 |
| **A7** | Fix `List.ForEach(async ...)` async-void concurrency bugs against EF Core `CliDbContext` and premature `dotnet ef` runs | [Workspace.razor:420-447,592](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/Workspace.razor#L420-L447) | Critical | Phase 1 / 3 |
| **A8** | Fix `PropertiesBuilder` `.g.cs` vs. `.cs` Mapster mismatch | [PropertiesBuilder.cs:93,110,126,146](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/PropertiesBuilder.cs#L93), [EntityBuilder.cs:95,115](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/EntityBuilder.cs#L95) | Critical | Phase 0 / 2 |
| **A9** | Fix `RelationsBuilder` `{Entity}Dto.cs` & wrong `{ParentEntity}ReadModel.cs` deletion path | [RelationsBuilder.cs:87,138,171,190](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/RelationsBuilder.cs#L87) | Critical | Phase 0 / 2 |
| **A10** | Add bounds checks on `--mapper` arg parsing (`args[mapperFlagIndex + 1]`) and fix `"axialaystem-covaluse"` typo | [EntityBuilder.cs:54-55](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/EntityBuilder.cs#L54-L55), [SolutionBuilder.cs:39-40,218](file:///d:/workspaces/my/mma-cli/Mma.Cli.Shared/Builders/SolutionBuilder.cs#L39-L40) | High | Phase 0 |
| **A11** | Fix `!e.Applied` filter skipping incremental properties/relations on applied entities, and scope entity lookups by `ProjectId` | [Workspace.razor:415,629-630](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/Workspace.razor#L415), [CliData.cs:27-28](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Data/CliData.cs#L27-L28) | High | Phase 1 / 3 |
| **A12** | Fix Windows Auth (`IntegratedSecurity = true` vs. `ActiveDirectoryIntegrated`) and avoid forcing `TrustServerCertificate = true` | [ImportDatabaseDialog.razor:85-87](file:///d:/workspaces/my/mma-cli/Mma.Cli.UI/Pages/ImportDatabaseDialog.razor#L85-L87) | Medium | Phase 2 / 3 |

---

## Category B — Foundational Architectural Refactors (12 Items)

| # | Refactor | Current State / Gap | Target Modernization | Target Phase |
| --- | --- | --- | --- | --- |
| **B1** | **Extract headless `Mma.Cli.Core`** | `Mma.Cli.Shared` mixes CLI args, `Console`, and ASP.NET MVC ref | Pure DI services with typed requests, `OperationResult<T>`, `CancellationToken`, `IProgress<GenerationEvent>` | Phase 1 |
| **B2** | **Roslyn AST Syntax Rewriters** | `lines[^2]` and `index + 10` text patching (`PropertiesBuilder.cs:96,208`) | `CSharpSyntaxRewriter` AST edits + Roslyn syntax validation | Phase 2 |
| **B3** | **Scriban Templates + Compat Layer** | Raw C# verbatim strings + `.Replace("$Token", ...)` | Scriban templates supporting loops/validations + legacy `.mma/templates/` token compatibility | Phase 2 |
| **B4** | **Canonical `.mma/project.mma` v2 + JSON Schema** | Split state between SQLite and one-way `ProjectDumber.cs` | Versioned `.mma/project.mma` v2 with schema validation, per-solution lock, and atomic swap | Phase 1 |
| **B5** | **`.slnx` Solution Support** | `BuildHelper.cs:49` scans only `*.sln` | Support both `*.slnx` (.NET 10 default) and `*.sln` cleanly | Phase 1 |
| **B6** | **`System.CommandLine` v2 + `Spectre.Console`** | Hand-switched `args[0]` + `Sharprompt` + `Output.cs` | Strongly typed commands, `--dry-run`, `--json`, shell completion, rich TUI | Phase 1 |
| **B7** | **Replace `Newtonsoft.Json` & `RestSharp`** | `Mma.Cli.Shared.csproj:12-13` pulls MVC + RestSharp | Standardize on `System.Text.Json` + `IHttpClientFactory` | Phase 1 |
| **B8** | **Cross-Platform Paths & MSBuild Packaging** | Windows `\\` strings, `after-build.bat`, `commands.json`, `Console.Clear()` | `Path.Combine` everywhere, cross-platform MSBuild React+BFF bundling, ephemeral loopback port | Phase 3 |
| **B9** | **EF Core Migrations for Workspace DB** | No schema evolution strategy for `CliDbContext` | Versioned EF Core migrations in `Mma.Cli.Database` for `workspace.db` | Phase 1 / 5 |
| **B10** | **Central Package Management + Analyzer Hygiene** | 15 compiler warnings (`CS8618`, `CS8602`, `SYSLIB0014`) + no `Directory.Packages.props` | `Directory.Packages.props`, `Directory.Build.props`, clean nullable annotations, `TreatWarningsAsErrors` | Phase 0–1 |
| **B11** | **Domain Naming & Typography Cleanup** | `ChiledEntity`, `ChiledName`, `ProjectDumber`, `Mappster`, `Defaut`, `Constractor`, `Modifiy` | Clean domain names in new Core/Web/MCP contracts while keeping backward-compatible `.mma` v1 alias deserializers | Phase 1 |
| **B12** | **GitHub Actions CI/CD + Automated Release** | `.github/` has 0 workflows; stale `build_error.txt` tracked | Multi-OS CI (`dotnet test`, Vitest, Playwright, NuGet audit) + automated NuGet release packing | Phase 0 / 3 / 6 |

---

## Category C — High-Value Features to Consider

### Theme 1: Visual Architecture & Modeling (React + BFF)

| Feature | Description | Priority | Effort | Value | Target |
| --- | --- | :---: | :---: | --- | --- |
| **C1.1 Interactive ERD Canvas** | Drag-and-drop entity nodes, auto-layout (`dagre`/`elk`), visual 1:1 / 1:N / M:N relation drawing, zoom/minimap, `Draft` vs. `Applied` badges | **P0** | L | Core differentiator replacing flat grids | Phase 3 |
| **C1.2 Dry-Run Unified Diff Viewer** | Inspect exact C# and `.mma/project.mma` diffs before writing files to disk | **P0** | M | Builds trust; eliminates blind code overwrites | Phase 2–3 |
| **C1.3 Rich Property & Validation Metadata** | Configure `MaxLength`, `Required`, `Regex`, `Range`, `DefaultValue`, `UniqueIndex`, `Enum` types, and value objects (reflected in FluentValidation & EF Config) | **P1** | M | Moves MMA beyond primitive type generation | Phase 2–3 |
| **C1.4 Streaming Build & Migration Terminal** | Live SSE console in workspace showing `dotnet build` / `dotnet ef migrations add` output + clickable file errors | **P0** | M | Replaces silent spinner in `Workspace.razor:78` | Phase 3 |
| **C1.5 Command Palette (`Cmd+K`) & Undo/Redo** | Keyboard-first navigation across entities, commands, templates, and ERD draft undo/redo | **P2** | M | Developer-grade UX polish | Backlog |
| **C1.6 Entity & Relation Rename / Refactor** | Safely rename an entity/property/FK across `.mma/project.mma` and C# files via Roslyn | **P2** | M | Common real-world maintenance task | Backlog |

### Theme 2: Code Generation & Template Ecosystem

| Feature | Description | Priority | Effort | Value | Target |
| --- | --- | :---: | :---: | --- | --- |
| **C2.1 Idempotent `partial` + `.g.cs` Separation** | Split generated code (`Order.g.cs`) from developer-owned partial files (`Order.cs`) across both AutoMapper/Mapster/Mapperly | **P1** | M | Allows safe re-generation without clobbering custom business logic | Phase 2 / Backlog |
| **C2.2 Mapperly Support (Zero-License Source-Gen Mapper)** | Add **Riok.Mapperly** alongside AutoMapper & Mapster (compile-time, AOT-safe, mitigates AutoMapper commercial licensing changes) | **P1** | M | Future-proofs generated .NET 10 solutions | Backlog |
| **C2.3 Modern Architecture Starter Profiles** | Let `mma new` choose **Clean Architecture (Controllers)**, **Vertical Slice / Minimal APIs**, or **.NET Aspire AppHost + OpenTelemetry + Scalar OpenAPI** | **P1** | L | Keeps generated APIs truly modern for .NET 10 | Backlog |
| **C2.4 Automated Test Scaffolding per Entity** | Generate unit validator tests + `WebApplicationFactory` CRUD integration tests for every scaffolded entity | **P1** | M | Immediate quality baseline in consumer solutions | Backlog |
| **C2.5 Frontend TypeScript SDK / React CRUD Export** | Generate typed TypeScript API hooks (`openapi-typescript` / React Query) directly from generated ASP.NET Core controllers | **P2** | M | Accelerates full-stack teams using React | Backlog |
| **C2.6 Template Pack Registry / Git Overrides** | Load custom template packs from Git repos or NuGet packages in addition to `.mma/templates/` | **P3** | M | Enterprise team standardization | Backlog |

### Theme 3: Database & Schema Engineering

| Feature | Description | Priority | Effort | Value | Target |
| --- | --- | :---: | :---: | --- | --- |
| **C3.1 Multi-DB Reverse Engineering** | Async schema import for **SQL Server, PostgreSQL (`Npgsql`), SQLite, MySQL/MariaDB (`MySqlConnector`)** | **P0** | M | Removes SQL Server lock-in in `DatabaseEngine.cs` | Phase 2 |
| **C3.2 Configurable Schema Exclusion Rules** | Replace hardcoded `AppUsers`/`ELMAH_Error`/`CreatedBy` exclusions (`DatabaseEngine.cs:46-47`) with `.mma/config.json` glob patterns & custom pluralization overrides | **P1** | S | Fixes surprises on non-ABP/custom databases | Phase 2 |
| **C3.3 3-Way Schema Drift Detector (`mma diff`)** | Compare `.mma/project.mma` vs. C# code on disk vs. live database schema; highlight missing migrations or untracked columns | **P1** | M | Essential for CI pipelines & team sync | Phase 2 / Backlog |
| **C3.4 Safe SQL Migration Script Preview** | Run `dotnet ef migrations script --idempotent` and display SQL preview before `database update` | **P1** | S | Prevents accidental destructive DB updates | Phase 3 / Backlog |
| **C3.5 SQL DDL / DBML / OpenAPI Import & Export** | Import `.sql` DDL, `.dbml`, or `openapi.json` into `.mma/project.mma`, and export Mermaid ERD / DBML docs | **P2** | M | Enables database-free modeling from design docs | Backlog |

### Theme 4: AI Agent & Provider Workflows

| Feature | Description | Priority | Effort | Value | Target |
| --- | --- | :---: | :---: | --- | --- |
| **C4.1 Natural-Language Domain Architect** | Describe business requirements ("Multi-tenant invoicing with Customers, Invoices, LineItems, Payments") -> Agent drafts normalized schema + diffs for approval | **P0** | L | Flagship AI Agent experience | Phase 5 |
| **C4.2 Plan + Unified Diff HITL Approval Gate** | Structured plan card & code diff before any disk write, with granular approve/reject/revise | **P0** | M | Prevents destructive AI hallucinations | Phase 5 |
| **C4.3 Multi-Provider Profiles + Local Ollama Privacy Mode** | Hot-switch OpenAI, Anthropic, Gemini, Azure OpenAI, Ollama, OpenRouter; `local-only` flag blocks cloud telemetry/egress | **P0** | M | Eliminates vendor lock-in & supports air-gapped teams | Phase 5 |
| **C4.4 Multimodal ERD Image/Diagram Import** | Upload whiteboard photo or ERD screenshot in React Copilot -> vision-capable `IChatClient` extracts proposed entities/relations | **P1** | M | High-wow productivity shortcut | Backlog |
| **C4.5 AI Schema & Migration Risk Reviewer** | Agent audits proposed entities/relations for missing indexes, cascade delete cycles, PII fields, and breaking EF migrations | **P2** | M | Proactive architectural quality gate | Backlog |
| **C4.6 Token/Cost Telemetry & Session Budget Caps** | Live token meter, per-turn cost estimate, and hard session cap per provider profile | **P1** | S | Prevents runaway LLM bills | Phase 5 |

### Theme 5: MCP Ecosystem & IDE Integration

| Feature | Description | Priority | Effort | Value | Target |
| --- | --- | :---: | :---: | --- | --- |
| **C5.1 Dual-Transport MCP Server (`stdio` + `/mcp`)** | Expose 18 domain tools, `mma://` resources, and 4 workflow prompts over `mma mcp` and BFF `/mcp` | **P0** | M | Lets Claude Code, Cursor, VS Code, Antigravity drive MMA natively | Phase 4 |
| **C5.2 `mma mcp install` IDE Auto-Configurator** | One-command setup writing MCP client entries for Claude Code, Cursor, VS Code, and Antigravity | **P1** | S | Frictionless developer onboarding | Phase 6 |
| **C5.3 External MCP Client in AI Agent** | Connect MMA Agent to external Postgres, GitHub, Jira, or Context7 MCP servers with trust approval | **P1** | M | Grounds AI modeling in external tickets/docs/live DBs | Phase 6 |
| **C5.4 MCP Progress & Task Support** | Stream MCP progress notifications during long-running `dotnet ef` and `dotnet build` tool calls | **P2** | S | Prevents client timeouts on large solutions | Backlog |

### Theme 6: Developer Experience, Safety & Team Collaboration

| Feature | Description | Priority | Effort | Value | Target |
| --- | --- | :---: | :---: | --- | --- |
| **C6.1 Automatic Git Checkpoint (`--git-checkpoint`)** | Create a safe Git stash/commit marker before Apply or Agent mutation for instant 1-step rollback | **P1** | S | Zero-fear AI & bulk schema changes | Backlog |
| **C6.2 `mma doctor` Diagnostics Command** | Verify `.NET 10` SDK, `dotnet-ef` tool, DB drivers, provider key resolution, and solution health | **P1** | S | Cuts support/onboarding friction dramatically | Phase 6 |
| **C6.3 `mma validate` & `--json` CI Mode** | Headless validation of `.mma/project.mma`, templates, and drift checks returning CI exit codes | **P1** | S | Enables PR checks on `.mma/project.mma` changes | Phase 1 |
| **C6.4 Shell Tab Completion** | Built-in bash/zsh/PowerShell completions via `System.CommandLine` `suggest` | **P2** | S | Fast daily CLI ergonomics | Phase 1 / Backlog |
