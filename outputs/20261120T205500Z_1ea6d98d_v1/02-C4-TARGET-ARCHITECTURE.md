# 02 — Target C4 & Dynamic Architecture

> **Epistemic Status**: Target-state diagrams and project layout represent **Recommendations** aligned with the **Verified Facts** of the existing `.NET 10` repository (`Mma.Cli.sln`) and agreed **Assumptions** (`CONTEXT.md`). Every diagram follows C4 modeling rules: shared class libraries (`Mma.Cli.Core`, `Mma.Cli.Database`, `Mma.Cli.Mcp`, `Mma.Cli.Agent`) are modeled as **Components inside runtime Containers**, never as standalone C4 Containers.

---

## 1. Level 1 — System Context Diagram

Shows MMA CLI as a cohesive platform interacting with developers, external AI clients, target solutions, databases, configurable LLM providers, external MCP servers, and the optional legacy Generator API.

```mermaid
C4Context
  title System Context - Modernized MMA CLI Platform

  Person(dev, "Application Developer", "Uses CLI, TUI, React UI, or AI Agent to design and scaffold APIs")
  System_Ext(mcpClients, "External MCP Hosts", "Claude Code, Cursor, VS Code, Antigravity invoking MMA tools")

  System(mma, "MMA CLI Platform", ".NET 10 code generator with local React+BFF UI, MCP server/client, and AI Agent")

  System_Ext(targetSolution, "Generated API Solution", "Consumer ASP.NET Core solution, .mma manifest, and template overrides")
  System_Ext(targetDbs, "Target Databases", "SQL Server, PostgreSQL, SQLite, MySQL schemas for reverse engineering")
  System_Ext(llmProviders, "Configured LLM Providers", "OpenAI, Anthropic, Google Gemini, Azure OpenAI, Ollama, OpenAI-compatible")
  System_Ext(extMcp, "External MCP Servers", "Optional database, Git, or docs MCP tools consumed by MMA Agent")
  System_Ext(genApi, "Remote Generator API", "Optional HTTP endpoint configured via GeneratorEndpoint")

  Rel(dev, mma, "Runs commands, models schemas, approves agent plans", "Terminal / Browser")
  Rel(mcpClients, mma, "Calls scaffolding and inspection tools", "MCP stdio / Streamable HTTP")
  Rel(mma, targetSolution, "Reads manifests, generates code, runs dotnet ef", "File system / dotnet CLI")
  Rel(mma, targetDbs, "Introspects tables, columns, and foreign keys", "DB Drivers")
  Rel(mma, llmProviders, "Sends prompts and receives tool calls", "HTTPS / Local HTTP")
  Rel(mma, extMcp, "Invokes external context tools in Phase 2", "MCP Client")
  Rel(mma, genApi, "Exports .mma/project.mma metadata", "JSON / HTTPS")
```

---

## 2. Level 2 — Container Diagram

Three deployable/executable runtime units plus local persistence comprise the **MMA CLI Platform**, packaged into one `.NET 10` global tool (`mma-cli`):

```mermaid
C4Container
  title Container Diagram - Modernized MMA CLI Platform

  Person(dev, "Application Developer", "Operates CLI, browser workspace, or AI workflows")
  System_Ext(ide, "External MCP Client", "IDE or AI assistant connecting over stdio or HTTP")

  System_Boundary(mma, "MMA CLI Platform") {
    Container(cli, "MMA CLI Executable", ".NET 10 Console Tool (Mma.Cli)", "Command router, Spectre TUI, mma ui launcher, mma mcp stdio host, mma agent runner")
    Container(bff, "MMA BFF Host", "ASP.NET Core 10 (Mma.Cli.Bff)", "Localhost REST + SSE API, /mcp Streamable HTTP endpoint, Agent runtime host, serves SPA")
    Container(spa, "MMA Web Workspace", "React 19, TypeScript, Vite (Mma.Cli.Web)", "ERD canvas, entity/relation editor, diff preview, DB import wizard, AI Copilot, settings")
    ContainerDb(localDb, "Workspace Index & Session DB", "SQLite (User AppData Dir)", "Tracks registered solutions, agent sessions, approvals, and audit logs")
  }

  System_Ext(solution, "Generated API Solution", "Target source tree + .mma/project.mma manifest")
  System_Ext(llm, "LLM Provider API", "OpenAI, Anthropic, Gemini, Azure, or Ollama")

  Rel(dev, cli, "Executes CLI / TUI / Agent commands", "Terminal")
  Rel(dev, spa, "Models projects and reviews diffs", "Browser / HTTPS or Localhost HTTP")
  Rel(ide, cli, "Connects via stdio transport", "MCP JSON-RPC / stdio")
  Rel(ide, bff, "Connects via Streamable HTTP", "MCP /mcp")
  Rel(cli, bff, "Starts on demand with bootstrap token", "Process / 127.0.0.1")
  Rel(spa, bff, "Calls typed APIs and subscribes to events", "JSON / HTTP + SSE")
  Rel(bff, localDb, "Reads/writes workspace index and agent history", "EF Core / SQLite")
  Rel(cli, localDb, "Shares workspace index and agent sessions", "EF Core / SQLite")
  Rel(cli, solution, "Reads manifest and applies code changes", "File system")
  Rel(bff, solution, "Previews diffs, applies code, runs dotnet ef", "File system / Process")
  Rel(cli, llm, "Runs headless mma agent completions", "IChatClient / HTTPS")
  Rel(bff, llm, "Streams Copilot agent responses", "IChatClient / HTTPS")
```

### Container Responsibilities

| Container | Runtime | Responsibility |
| --- | --- | --- |
| **MMA CLI Executable (`Mma.Cli`)** | `.NET 10` Global Tool (`mma`) | `System.CommandLine` v2 command routing, `Spectre.Console` interactive TUI, `mma mcp` stdio server, `mma agent` CLI runner, and `mma ui` BFF launcher. |
| **MMA BFF Host (`Mma.Cli.Bff`)** | `ASP.NET Core 10` (`127.0.0.1`) | Owns localhost security, REST API (`/api/v1/*`), SSE streaming, Streamable HTTP MCP (`/mcp`), Agent session execution, and static hosting of the prebuilt React bundle. |
| **MMA Web Workspace (`Mma.Cli.Web`)** | Prebuilt `React 19 + TS` SPA | Browser workspace: solution explorer, interactive React Flow ERD, property/relation grid, dry-run unified diff viewer, DB import wizard, AI Copilot drawer, and provider settings. |
| **Workspace Index & Session DB** | `SQLite` in OS AppData | Stores known solution paths, recent workspaces, AI Agent conversations, approval history, and execution telemetry (never stored in `wwwroot/`). |

---

## 3. Level 3 — Component Diagrams

### 3.1 `Mma.Cli.Core` & `Mma.Cli.Database` (Headless Generation Engine)

Embedded as class libraries inside both `Mma.Cli` and `Mma.Cli.Bff`. Contains **zero** `Console.*`, `Environment.Exit`, `HttpContext`, or UI framework dependencies.

```mermaid
C4Component
  title Component Diagram - Mma.Cli.Core and Mma.Cli.Database

  Container(hosts, "CLI / BFF / MCP / Agent Hosts", ".NET 10 Processes", "Invoke application services with cancellation and progress")

  Container_Boundary(core, "Mma.Cli.Core & Mma.Cli.Database Libraries") {
    Component(appServices, "Generation Application Services", "C# / DI", "SolutionService, EntityService, PropertyService, RelationService, MigrationService")
    Component(manifestStore, "Manifest Store & File Lock", "System.Text.Json + Mutex", "Reads/writes .mma/project.mma v2 atomically with schema validation")
    Component(roslyn, "Roslyn Code Rewriter", "Microsoft.CodeAnalysis.CSharp", "AST-based updates to entities, DTOs, DbContext, MappingProfile, and EF configs")
    Component(scriban, "Scriban Template Engine", "Scriban", "Renders built-in templates or .mma/templates overrides")
    Component(diffEngine, "Dry-Run & Diff Engine", "Virtual File Staging", "Computes in-memory file plans and unified diffs without disk mutation")
    Component(dotnetRunner, "Dotnet Process Runner", "CliWrap + Cancellation", "Executes dotnet build and dotnet ef with streaming logs and safe cancellation")
    Component(dbIntrospect, "Multi-DB Introspector", "SqlClient / Npgsql / Sqlite / MySQL", "Async schema inspection behind IDatabaseIntrospector")
  }

  System_Ext(solution, "Target API Solution", "C# files + .mma/project.mma")
  System_Ext(databases, "Target Databases", "SQL Server, Postgres, SQLite, MySQL")

  Rel(hosts, appServices, "Calls typed commands and dry-run plans", "In-process API")
  Rel(appServices, manifestStore, "Loads, validates, and persists manifest", "In-process")
  Rel(appServices, roslyn, "Patches existing C# ASTs safely", "In-process")
  Rel(appServices, scriban, "Renders new entity/service/controller files", "In-process")
  Rel(appServices, diffEngine, "Stages operations for preview or atomic apply", "In-process")
  Rel(appServices, dotnetRunner, "Runs build and EF Core migrations", "In-process")
  Rel(appServices, dbIntrospect, "Imports external DB schemas", "In-process")
  Rel(manifestStore, solution, "Reads/writes .mma/project.mma", "File system")
  Rel(diffEngine, solution, "Commits staged file edits atomically", "File system")
  Rel(dotnetRunner, solution, "Runs dotnet build / dotnet ef", "Child process")
  Rel(dbIntrospect, databases, "Queries information schema", "Async ADO.NET")
```

### 3.2 `Mma.Cli.Bff`, `Mma.Cli.Mcp`, and `Mma.Cli.Agent` (Web, MCP & AI Host)

```mermaid
C4Component
  title Component Diagram - BFF, MCP, and AI Agent Layer

  Container(spa, "React SPA", "Browser", "Calls REST/SSE endpoints")
  System_Ext(extHost, "External MCP Host", "IDE", "Calls /mcp or stdio")

  Container_Boundary(bffHost, "Mma.Cli.Bff Host + Mma.Cli.Mcp + Mma.Cli.Agent") {
    Component(secFilter, "Localhost Security Middleware", "ASP.NET Core", "Host/Origin check, bootstrap cookie + CSRF token, workspace path jail")
    Component(restApi, "Workspace & Gen Endpoints", "Minimal APIs + OpenAPI", "CRUD projects/entities/relations, plan-diff, apply, DB import, SSE logs")
    Component(mcpServer, "MCP Server Registry", "ModelContextProtocol.AspNetCore", "Exposes mma_* tools, mma:// resources, and prompts on /mcp and stdio")
    Component(agentOrch, "Agent Orchestrator", "Microsoft.Agents.AI", "Multi-turn loop, plan generation, step/token limits, session persistence")
    Component(approvalGate, "HITL Approval Gate", "C# Async Wait", "Pauses mutating tool calls until user approves plan/diff in UI or CLI")
    Component(providerFactory, "LLM Provider Factory", "Microsoft.Extensions.AI", "Builds IChatClient pipeline for OpenAI, Anthropic, Gemini, Azure, Ollama")
    Component(secretResolver, "Secret & Config Store", "OS Keyring + Config", "Resolves env: keys or OS keyring; blocks plaintext keys in project config")
    Component(mcpClient, "External MCP Client Manager", "ModelContextProtocol.Core", "Phase 2: bridges external MCP server tools into AIFunction list")
  }

  Component_Ext(core, "Mma.Cli.Core Services", "Class Library", "Domain validation, dry-run diffs, and apply")
  System_Ext(llm, "Configured LLM API", "HTTPS / Ollama")

  Rel(spa, secFilter, "Sends authenticated local requests", "HTTP + Cookie/CSRF")
  Rel(secFilter, restApi, "Forwards validated requests", "ASP.NET Core pipeline")
  Rel(extHost, mcpServer, "Invokes MCP tools and resources", "Streamable HTTP / stdio")
  Rel(restApi, core, "Executes dry-run, apply, and introspection", "In-process")
  Rel(restApi, agentOrch, "Starts and streams agent turns", "SSE")
  Rel(mcpServer, core, "Executes mma_* tools", "In-process")
  Rel(agentOrch, approvalGate, "Requests user sign-off on write plans", "Event / Callback")
  Rel(agentOrch, providerFactory, "Obtains configured IChatClient", "In-process")
  Rel(agentOrch, core, "Runs read-only and approved write tools", "AIFunction")
  Rel(agentOrch, mcpClient, "Invokes external MCP tools in Phase 2", "AIFunction")
  Rel(providerFactory, secretResolver, "Fetches API keys securely", "In-process")
  Rel(providerFactory, llm, "Streams chat completions and tool calls", "HTTPS / Local HTTP")
```

---

## 4. Dynamic Request Flows (Sequence Diagrams)

### 4.1 Flow A — React BFF Modeling -> Dry-Run Diff -> Atomic Apply & Streaming Migration

```mermaid
sequenceDiagram
  autonumber
  actor Dev as Developer (React SPA)
  participant BFF as Mma.Cli.Bff
  participant Core as Mma.Cli.Core
  participant Disk as Solution & .mma/project.mma

  Dev->>BFF: POST /api/v1/projects/{id}/plan-diff (draft entity/property/relation changes)
  BFF->>Core: PlanChangesAsync(projectId, draftChanges)
  Core->>Disk: Acquire per-solution lock & read .mma/project.mma + target C# files
  Core->>Core: Run Roslyn rewriter + Scriban in virtual staging (dry-run)
  Core-->>BFF: Return PlannedOperation + UnifiedDiff[] + Warnings
  BFF-->>Dev: 200 OK (diff preview rendered in React Diff Drawer)

  Dev->>BFF: POST /api/v1/projects/{id}/apply (planHash, createMigration: true)
  BFF-->>Dev: SSE stream opened (/api/v1/jobs/{jobId}/events)
  BFF->>Core: ApplyPlanAsync(projectId, planHash, progressReporter)
  Core->>Disk: Verify planHash matches current manifest, write files atomically, update .mma/project.mma
  Core-->>BFF: SSE event: progress ("Files generated")
  Core->>Disk: Run dotnet ef migrations add (with CancellationToken)
  Core-->>BFF: SSE event: log (streaming EF stdout/stderr)
  Core-->>BFF: SSE event: done (summary + updated manifest)
  BFF-->>Dev: Update TanStack Query cache & ERD applied badges
```

### 4.2 Flow B — External AI IDE via MCP Server (`stdio` or `/mcp`)

```mermaid
sequenceDiagram
  autonumber
  participant IDE as External AI IDE (Claude Code / Cursor)
  participant MCP as Mma.Cli.Mcp (stdio or /mcp)
  participant Core as Mma.Cli.Core

  IDE->>MCP: tools/list
  MCP-->>IDE: mma_* tools with JSON Schemas and annotations (readOnlyHint, destructiveHint)

  IDE->>MCP: tools/call mma_inspect_solution (solutionPath)
  MCP->>Core: InspectSolutionAsync(solutionPath)
  Core-->>MCP: Solution summary (mapper, entities, properties, relations, .slnx/.sln)
  MCP-->>IDE: Structured JSON + concise summary

  IDE->>MCP: tools/call mma_add_entity (name: "Invoice", pkType: "Guid", dryRun: true)
  MCP->>Core: PlanAddEntityAsync(request)
  Core-->>MCP: Unified diff preview (0 files modified on disk)
  MCP-->>IDE: Structured diff content for user/agent verification

  IDE->>MCP: tools/call mma_add_entity (name: "Invoice", pkType: "Guid", dryRun: false)
  MCP->>Core: AddEntityAsync(request)
  Core-->>MCP: Written file paths + updated .mma/project.mma
  MCP-->>IDE: Result + resource list_changed notification
```

### 4.3 Flow C — AI Agent Mode with Configurable Providers & HITL Approval

```mermaid
sequenceDiagram
  autonumber
  actor Dev as Developer (CLI or React Copilot)
  participant Agent as Mma.Cli.Agent (Microsoft.Agents.AI)
  participant LLM as Configured IChatClient (OpenAI/Anthropic/Gemini/Ollama)
  participant Core as Mma.Cli.Core

  Dev->>Agent: Prompt: "Add Order and OrderItem with 1:N relation and Total decimal"
  Agent->>Core: Inspect current solution manifest (ReadOnly tool)
  Core-->>Agent: Existing entities, mapper, target framework
  Agent->>LLM: Chat messages + read-only context + MMA tool schemas
  LLM-->>Agent: Tool calls: plan_add_entity(Order), plan_add_entity(OrderItem), plan_add_relation
  Agent->>Core: Generate dry-run diffs for all proposed operations
  Core-->>Agent: Planned operations + unified C# diffs

  Agent-->>Dev: Present structured Plan + Unified Diffs (Approval Required)
  alt Developer Approves Plan
    Dev->>Agent: Approve (all or selected steps)
    Agent->>Core: Execute approved operations atomically
    Agent->>Core: Run dotnet build verification
    Core-->>Agent: Build succeeded (0 errors)
    Agent-->>Dev: Summary of applied files, manifest update, and token usage
  else Developer Rejects or Requests Revision
    Dev->>Agent: Reject / Feedback ("Use long PK instead of Guid")
    Agent->>LLM: Continue loop with user feedback (0 disk writes occurred)
  end
```

---

## 5. Level 4 — Target Deployment Diagram

```mermaid
C4Deployment
  title Deployment Diagram - Local Workstation Tool (Future Remote Auth Seam Preserved)

  Deployment_Node(ws, "Developer Workstation", "Windows / macOS / Linux", "No Node.js required at end-user runtime") {
    Deployment_Node(dotnet, ".NET 10 Global Tool Package", "mma-cli v5.x NuGet Tool", "Installed via dotnet tool install -g mma-cli") {
      Container(cli, "Mma.Cli", ".NET 10 Executable", "CLI, TUI, mma mcp stdio, mma agent, launches BFF")
      Container(bff, "Mma.Cli.Bff + Embedded SPA", "ASP.NET Core 10 + Static React dist/", "Bound to 127.0.0.1 ephemeral port")
    }
    Deployment_Node(appdata, "User Profile & AppData", "~/.mma/ and OS LocalAppData", "Persists across tool upgrades") {
      Container(config, "Provider & MCP Config", "JSON + OS Keyring", "~/.mma/config.json and ~/.mma/mcp.json (no plaintext keys)")
      ContainerDb(db, "Workspace & Session DB", "SQLite", "workspace.db (projects, agent threads, audit logs)")
    }
    Deployment_Node(repo, "Developer Solution Directory", "Git Repository", "Consumer-owned codebase") {
      Container(sol, "Target ASP.NET Core Solution", ".sln / .slnx + .mma/project.mma", "Generated code, manifest v2, optional .mma/templates/")
    }
  }

  Deployment_Node(cloud, "External Services", "HTTPS / Localhost", "Optional user-configured backends") {
    Container_Ext(llms, "LLM APIs or Local Ollama", "OpenAI / Anthropic / Gemini / Azure / Ollama", "Model inference")
    Container_Ext(dbs, "Target Databases", "SQL Server / Postgres / MySQL / SQLite", "Schema reverse engineering")
  }

  Rel(cli, bff, "Launches and shuts down cleanly", "Process + Bootstrap Token")
  Rel(bff, db, "Reads/writes workspace index and sessions", "EF Core / SQLite")
  Rel(cli, config, "Reads active provider profile", "File system + Keyring")
  Rel(bff, config, "Reads/updates provider profiles", "File system + Keyring")
  Rel(cli, sol, "Scaffolds and patches code", "File system")
  Rel(bff, sol, "Previews diffs and runs dotnet ef", "File system / Child process")
  Rel(bff, llms, "Executes agent completions", "IChatClient / HTTPS")
  Rel(bff, dbs, "Introspects schemas", "Async DB driver")
```

---

## 6. Target Solution & Repository Structure

> **Verified Constraint**: `.gitignore:35` ignores `[Uu]i/`. Do **not** name the new React project `ui/` or `UI/`. Use `src/Mma.Cli.Web` and `src/Mma.Cli.Bff`.

```text
Mma.Cli.sln (or Mma.Cli.slnx)
├── Directory.Packages.props                 # Central Package Management (pins SQLite, Roslyn, MEAI, MCP versions)
├── Directory.Build.props                    # Shared net10.0, Nullable, Analyzers, TreatWarningsAsErrors (incremental)
├── .github/workflows/
│   ├── ci.yml                               # dotnet build + test + vitest + Playwright + vulnerability scan
│   └── release.yml                          # Cross-platform build, React bundle, NuGet pack/publish
└── src/
    ├── Mma.Cli.Core/                        # Headless engine: Services, Roslyn rewriters, Scriban, Manifest v2, DryRunDiff
    ├── Mma.Cli.Database/                    # Multi-DB introspection (SqlClient, Npgsql, Sqlite, MySqlConnector) + Workspace SQLite store
    ├── Mma.Cli.Mcp/                         # Shared MCP Server tools, resources, prompts + Phase 2 McpClientManager
    ├── Mma.Cli.Agent/                       # Microsoft.Extensions.AI + Microsoft.Agents.AI orchestrator, providers, secrets, HITL
    ├── Mma.Cli.Bff/                         # ASP.NET Core 10 BFF: OpenAPI REST + SSE + /mcp + serves built React SPA from wwwroot/
    ├── Mma.Cli.Web/                         # React 19 + TypeScript + Vite 6 + Tailwind + shadcn/ui + React Flow ERD
    ├── Mma.Cli.App/                         # CLI global tool ('mma'): System.CommandLine v2 + Spectre.Console + launchers
    └── tests/
        ├── Mma.Cli.Core.Tests/              # Characterization, Roslyn/Scriban golden tests, manifest lock, dry-run tests
        ├── Mma.Cli.Mcp.Tests/               # MCP protocol contract tests + 10-question read-only eval suite
        ├── Mma.Cli.Agent.Tests/             # Deterministic mock-IChatClient agent loop, approval gate, redaction tests
        └── Mma.Cli.Bff.Tests/               # WebApplicationFactory integration tests + security/origin/CSRF tests
```

### Dependency Direction Rules (Clean Architecture Invariant)

- `Mma.Cli.Core` **depends on nothing UI/HTTP/CLI/AI-specific** (only Roslyn, Scriban, `System.Text.Json`, `CliWrap`, `Microsoft.Extensions.Logging.Abstractions`).
- `Mma.Cli.Database` depends on `Mma.Cli.Core` (contracts) and database drivers.
- `Mma.Cli.Mcp` depends on `Mma.Cli.Core`, `Mma.Cli.Database`, and `ModelContextProtocol`.
- `Mma.Cli.Agent` depends on `Mma.Cli.Core`, `Mma.Cli.Database`, `Mma.Cli.Mcp` (for MCP client bridge), `Microsoft.Extensions.AI`, `Microsoft.Agents.AI`, and provider SDKs.
- `Mma.Cli.Bff` and `Mma.Cli.App` are **composition roots** wiring DI, configuration, and transport hosting.
