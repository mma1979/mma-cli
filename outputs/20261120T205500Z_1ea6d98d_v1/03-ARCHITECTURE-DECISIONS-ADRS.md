# 03 — Architecture Decision Records (ADRs)

Each ADR below captures the verified context from the codebase audit, the chosen architectural decision, evaluated alternatives, trade-offs, and concrete mitigations.

---

## ADR-001: Extract Headless `Mma.Cli.Core` Application Engine

- **Status**: Proposed
- **Context (Verified Facts)**:
  - `Mma.Cli.Shared` builders currently couple domain logic to raw CLI argument arrays (`EntityBuilder.New(string[] args)`, `PropertiesBuilder.New(string[] args, ...)`), `Directory.GetCurrentDirectory()`, static `Console` color mutations (`Output.cs`), and fatal `Environment.Exit(...)` calls (`PropertiesBuilder.cs:41,67`, `RelationsBuilder.cs:37,61`, `BuildHelper.cs:55,129`).
  - `Mma.Cli.Shared.csproj:12` also references `Microsoft.AspNetCore.Mvc.NewtonsoftJson`, pulling ASP.NET MVC into a shared library.
- **Decision**:
  - Extract a clean, headless **`Mma.Cli.Core`** library using typed command/query request records, structured `OperationResult<T>` returns (with machine-readable error codes), `CancellationToken`, and `IProgress<GenerationEvent>` streams.
  - Enforce zero `Environment.Exit`, zero `Console.*`, zero `HttpContext`, and zero UI types in `Mma.Cli.Core`.
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **Keep `Mma.Cli.Shared` and patch around `Environment.Exit`** | Minimal upfront diff | Leaves `string[] args` parsing, static `Output`, and synchronous blocking intact; unsafe for BFF, MCP, and Agent concurrency. |
| **Invoke `mma` CLI subprocesses from BFF/MCP/Agent** | Process isolation | Breaks dry-run diff previews, disables in-process streaming progress, slow startup per tool call, duplicates process-management bugs seen in `Workspace.razor:290`. |
| **Headless `Mma.Cli.Core` + DI Services (Chosen)** | Single deterministic engine shared by CLI, BFF, MCP, and Agent; trivial to unit test | Requires initial extraction refactor (Phase 1). |

- **Trade-offs & Mitigations**:
  - *Trade-off*: Upfront refactoring effort in Phase 1 before new UI/MCP/Agent features land.
  - *Mitigation*: Build characterization tests around existing `Mma.Cli.Shared` builders in Phase 0 first, then preserve `mma n` / `mma g` CLI flags via thin `System.CommandLine` adapters calling `Mma.Cli.Core`.

---

## ADR-002: Replace Line-Offset String Patching with Roslyn Syntax Rewriters, Scriban Templates & Dry-Run Diff Engine

- **Status**: Proposed
- **Context (Verified Facts)**:
  - `PropertiesBuilder.cs` and `RelationsBuilder.cs` insert C# code by searching string fragments and inserting at `lines[^2]` (`PropertiesBuilder.cs:96,113`) or hardcoded `index + 10` offsets (`PropertiesBuilder.cs:208,224`, `RelationsBuilder.cs:253,269,376,392`).
  - Existing bugs include Mapster `.g.cs` vs. `.cs` filename mismatch (`PropertiesBuilder.cs:93` vs. `EntityBuilder.cs:95`), `Dto.cs` vs. `ModifyModel.cs` mismatch (`RelationsBuilder.cs:87,138`), and removing from the wrong ReadModel (`RelationsBuilder.cs:190`).
- **Decision**:
  - Use **`Microsoft.CodeAnalysis.CSharp` (Roslyn)** syntax tree rewriters (`CSharpSyntaxRewriter`) for incremental edits to existing C# files (`Entity`, `ModifyModel`, `ReadModel`, `EntityConfig`, `ApplicationDbContext`, `MappingProfile`).
  - Use **`Scriban`** for template rendering (supporting loops, conditionals, rich property validations, and custom `.mma/templates/<mapper>/<Template>.cs` overrides with compatibility mapping for legacy `$EntityName` / `$SolutionName` placeholders).
  - Wrap every file write in a **`DryRunDiffEngine`** (virtual filesystem staging) so CLI (`--dry-run`), React UI, MCP (`dryRun: true`), and AI Agent approvals can preview exact unified diffs before touching disk.
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **Fix line offsets in string builders** | Fast short-term | Still breaks whenever a developer formats code, adds comments, or customizes a constructor. |
| **T4 / Razor templates only** | Familiar to .NET devs | T4 tooling is heavy cross-platform; Razor requires Roslyn runtime compilation per template; neither solves safe incremental AST editing of existing files. |
| **Roslyn Rewriters + Scriban + Staging Diff (Chosen)** | Syntax-aware, format-preserving, idempotent edits + fast safe templates + native dry-run diffs | Slightly higher learning curve for Roslyn syntax visitors; `Microsoft.CodeAnalysis.CSharp` adds ~5 MB to tool package. |

- **Trade-offs & Mitigations**:
  - *Mitigation*: Isolate Roslyn helpers behind focused interfaces (`IEntityCodeRewriter`, `IDbContextRewriter`) with golden snapshot tests; auto-detect legacy `$Token` syntax in existing `.mma/templates/` overrides and translate or evaluate cleanly so existing user templates do not break.

---

## ADR-003: Unify Project State — `.mma/project.mma` v2 as Canonical Source + SQLite as Local Workspace Index

- **Status**: Proposed
- **Context (Verified Facts)**:
  - Two uncoordinated persistence models currently exist: SQLite `CliDbContext` stored inside `Mma.Cli.UI/wwwroot/data/mma_cli.db` (`appsettings.json:11`) and `.mma/project.mma` dumped one-way via `ProjectDumber.cs`.
  - `CliData.cs:27-28` and `Workspace.razor:629-630` look up entities by `EntityName` without `ProjectId`, risking cross-project data corruption.
  - CLI commands (`mma g e/p/r`) never update SQLite, and UI mutations aren't guaranteed to match CLI/MCP/Agent changes.
- **Decision**:
  - Make **`.mma/project.mma` (Schema v2, formatted indented JSON with schema version + JSON Schema validation)** inside each solution root the **single canonical source of truth** for entities, properties, validations, relations, mapper, and applied status.
  - Repurpose **SQLite (`workspace.db`)**, relocated to the OS user local app-data directory (`%LocalAppData%\mma-cli\workspace.db` / `~/.local/share/mma-cli/workspace.db`), strictly as a **workspace registry & history store** (registered solution paths, recent projects, AI Agent chat threads, approvals, audit logs).
  - Protect `.mma/project.mma` writes with a **per-solution named lock + atomic temp-file swap** so CLI, BFF, MCP, and Agent never corrupt state concurrently.
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **SQLite as sole source of truth** | Relational queries across projects | Not Git-trackable per solution; teammates pulling a repo wouldn't get the model; CLI without UI DB drifts. |
| **Manifest only (drop SQLite completely)** | Zero DB migrations | No fast place to store multi-turn AI Agent conversations, approval audit trails, or workspace list across directories. |
| **Canonical `.mma/project.mma` v2 + User AppData SQLite Index (Chosen)** | Git-friendly, team-shareable model + persistent agent history across tool upgrades | Requires startup/on-open manifest reconciliation (`SyncFromSolution`). |

- **Trade-offs & Mitigations**:
  - *Mitigation*: Include a `schemaVersion` field in `.mma/project.mma` with automatic v1 -> v2 migration on open, plus a non-mutating `mma validate` command to detect drift between `.mma/project.mma` and actual C# files on disk.

---

## ADR-004: Local-First ASP.NET Core 10 BFF + Vite/React 19/TypeScript SPA

- **Status**: Proposed
- **Context (Verified Facts)**:
  - Current UI (`Mma.Cli.UI`) uses Blazor Server + Radzen with SignalR circuits, `AllowAnyOrigin()` CORS (`Program.cs:45-48`), Windows-only `after-build.bat` packaging, and synchronous UI-bound EF Core calls (`Workspace.razor`).
  - User requires a React UI using the **BFF pattern**, packaged inside `mma-cli` with no end-user Node.js dependency, while preserving a clean seam for future remote hosting.
- **Decision**:
  - Replace `Mma.Cli.UI` with **`Mma.Cli.Bff`** (ASP.NET Core 10 Web API + SSE + Static File Host) paired with **`Mma.Cli.Web`** (Vite 6 + React 19 + TypeScript 5 + Tailwind CSS + shadcn/ui + TanStack Query + Zustand + React Flow `@xyflow/react`).
  - Keep **all filesystem access, DB introspection, `dotnet ef` execution, MCP transport, and LLM API keys strictly inside `Mma.Cli.Bff`** (the browser never touches DB drivers, disk directly, or LLM secrets).
  - Enforce **OpenAPI-first contracts** (`Microsoft.AspNetCore.OpenApi` -> `openapi-typescript`) and harden localhost security (bind `127.0.0.1`, one-time launch token exchanged for `HttpOnly; SameSite=Strict` session cookie + CSRF header, strict `Origin`/`Host` validation, workspace path allow-listing).
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **Keep Blazor Server / Blazor WASM** | All C# | Does not meet requirement for React UI; Blazor Server suffers circuit timeouts and weak visual graph/ERD ecosystem. |
| **Next.js (Node.js BFF) + .NET CLI** | Full-stack TS | Forces end users to install Node.js runtime alongside .NET; duplicates C# Core/Roslyn logic or incurs slow CLI process spawning on every API call. |
| **Electron / Tauri Desktop App** | Native window | Heavy binary distribution outside `dotnet tool install -g mma-cli`; harder to share BFF `/mcp` and browser ergonomics. |
| **ASP.NET Core 10 BFF + Prebuilt Vite React SPA (Chosen)** | Zero Node.js for end users, single `dotnet tool`, direct in-process access to `Mma.Cli.Core`/MCP/Agent, remote-ready BFF seam | Requires Node.js at **CI/contributor build time** only to compile static SPA assets into `wwwroot/`. |

- **Trade-offs & Mitigations**:
  - *Mitigation*: Wire MSBuild `BeforeBuild`/`Publish` targets in `Mma.Cli.Bff.csproj` with incremental inputs/outputs so contributors get automatic cross-platform builds (Windows/macOS/Linux), and use ASP.NET Core `SpaProxy` during local `vite dev` HMR development.

---

## ADR-005: Official C# MCP SDK for Dual-Transport MCP Server & Phase 2 MCP Client

- **Status**: Proposed
- **Context (Verified Facts)**:
  - MMA CLI already targets `.NET 10`. The official C# MCP SDK (`ModelContextProtocol`, `ModelContextProtocol.Core`, `ModelContextProtocol.AspNetCore`) natively supports `.NET 10`, DI-based `[McpServerToolType]`/`[McpServerTool]` discovery, `stdio` transport, Streamable HTTP (`app.MapMcp("/mcp")`), and MCP Client connections.
- **Decision**:
  - Build **`Mma.Cli.Mcp`** on the official **`ModelContextProtocol`** C# SDK.
  - Expose **identical domain tools, resources, and prompts** over two transports:
    1. **`stdio`** (`mma mcp`) for IDE/agent launchers (Claude Code, Cursor, VS Code, Antigravity), enforcing strict stdout isolation (all diagnostics logged to `stderr` or file so JSON-RPC framing is never corrupted).
    2. **Streamable HTTP** (`/mcp` on `Mma.Cli.Bff`) for local/remote HTTP MCP clients, protected by localhost `Origin` checks and bearer/session auth.
  - Phase 2 (Milestone 6): Add **`McpClientManager`** inside `Mma.Cli.Mcp`/`Mma.Cli.Agent` so MMA's AI Agent can consume external MCP servers configured in `~/.mma/mcp.json` / `.mma/mcp.json`.
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **Node.js / TypeScript `@modelcontextprotocol/server` wrapping `mma` CLI** | Aligns with TS MCP examples | Requires Node.js on user machines; forces fragile CLI subprocess parsing; loses in-process Roslyn dry-run diffs and cancellation. |
| **Custom JSON-RPC 2.0 implementation in C#** | Zero external dependency | High maintenance burden tracking MCP spec revisions (Streamable HTTP, capabilities negotiation, annotations, tasks). |
| **Official C# `ModelContextProtocol` SDK (Chosen)** | Zero Node.js runtime dependency, native `.NET 10` DI, shares `Mma.Cli.Core` directly, official spec alignment | Relies on evolving MCP C# SDK releases (mitigated via Central Package Management pinning and contract tests). |

---

## ADR-006: Configurable Multi-Provider AI Agent on `Microsoft.Extensions.AI` + `Microsoft.Agents.AI`

- **Status**: Proposed
- **Context**:
  - User requires an AI Agent mode with configurable LLM providers, usable across workflows without lock-in to a single vendor.
  - Verified .NET 10 ecosystem packages provide official `IChatClient` implementations across **OpenAI** (`OpenAI`), **Azure OpenAI** (`Azure.AI.OpenAI`), **Anthropic** (`Anthropic`), **Google Gemini** (`Google.GenAI`), and **Ollama** (`OllamaSharp`), composable with `Microsoft.Extensions.AI` middleware (`UseFunctionInvocation`, `UseOpenTelemetry`, `UseLogging`) and **`Microsoft.Agents.AI`** (Microsoft Agent Framework) stateful agent loops.
- **Decision**:
  - Implement **`Mma.Cli.Agent`** directly over **`Microsoft.Extensions.AI`** (`IChatClient`, `AIFunction`) and **`Microsoft.Agents.AI`**.
  - Support a **Provider Profile Registry** (`~/.mma/config.json` + `.mma/config.json` non-secret overrides) supporting `openai`, `anthropic`, `gemini`, `azure-openai`, `ollama`, and `openai-compatible` (OpenRouter, LM Studio, vLLM, Groq, DeepSeek).
  - Resolve secrets strictly via **OS Keyring -> `env:<VAR_NAME>` -> interactive prompt**, rejecting plaintext keys in project-scoped `.mma/config.json`.
  - Enforce a **Plan -> Dry-Run Diff -> Human-in-the-Loop Approval Gate -> Deterministic Apply -> Build Verify** execution loop. Arbitrary shell/file-write tools remain disabled unless explicitly opted in.
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **Semantic Kernel (`Microsoft.SemanticKernel`)** | Mature plugin ecosystem | Effectively succeeded for new .NET 10 greenfield agents by `Microsoft.Extensions.AI` + `Microsoft.Agents.AI`; heavier abstraction footprint. |
| **Custom per-provider HTTP/REST wrappers** | Full control | Re-invents tool-calling normalization, streaming delta parsing, token usage accounting, and retry middleware for 6+ vendors. |
| **Python / LangGraph sidecar** | Rich Python ecosystem | Breaks single-binary `.NET` global tool distribution; requires Python runtime on user workstation. |
| **`Microsoft.Extensions.AI` + `Microsoft.Agents.AI` (Chosen)** | Native .NET 10, official provider `IChatClient` support, clean middleware pipeline, easy deterministic mocking in xUnit | Official `Anthropic`/`Google.GenAI`/`Microsoft.Agents.AI` packages evolve quickly (mitigated by wrapping behind `ILlmProviderFactory` and pinning versions). |

---

## ADR-007: `System.CommandLine` v2 + `Spectre.Console` CLI & Cross-Platform MSBuild Packaging

- **Status**: Proposed
- **Context (Verified Facts)**:
  - `Mma.Cli.AppV4/Program.cs` manually switches on `args[0]` (`HandleCommandLineAsync`), lacks structured subcommands/tab-completion/exit codes, uses `Sharprompt` for basic prompts, clears console on `mma ui` stdout, kills all `dotnet` processes on `Ctrl+C` (`Program.cs:42-65`), and relies on Windows-only `Mma.Cli.UI/after-build.bat` + `commands.json` to copy UI artifacts into `Mma.Cli.AppV4/UI/`.
- **Decision**:
  - Migrate CLI parsing to **`System.CommandLine` `2.0.0`** (GA for `.NET 10`) with backward-compatible command aliases (`new`/`n`, `generate`/`g` `entity`/`e`, `property`/`p`, `relation`/`r`, `import`, `ui`, `mcp`, `agent`, `config`, `doctor`, `validate`, `diff`), `--json` machine output, and shell completion.
  - Upgrade interactive TUI (`mma` with no args, `mma agent`, `mma doctor`) to **`Spectre.Console`** (rich tables, diff panels, confirmation prompts, live spinners).
  - Replace `after-build.bat` and `commands.json` with **cross-platform MSBuild targets** building `Mma.Cli.Web`, embedding static assets into `Mma.Cli.Bff/wwwroot/`, and publishing `Mma.Cli.Bff` into `Mma.Cli.App/bff/` during `dotnet pack`.
- **Alternatives Considered**:

| Option | Pros | Cons / Why Rejected |
| --- | --- | --- |
| **Keep hand-rolled `args[0]` switch + `Sharprompt`** | No migration | Fragile index parsing (`args[mapperFlagIndex + 1]`), no tab completion, poor help output, hard to extend with `mcp`/`agent`/`config`. |
| **Spectre.Console.Cli** | Integrated with Spectre widgets | `System.CommandLine` v2 is .NET's first-party standard with native trimming/AOT and shell `suggest` support; pairing `System.CommandLine` (parsing) + `Spectre.Console` (rendering) gives best of both worlds. |
