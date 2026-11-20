# 05 — Technical Specification: Model Context Protocol (MCP) Capability

> **Epistemic Status**: Grounded in verified `.NET 10` project capabilities and the official C# MCP SDK (`ModelContextProtocol`, `ModelContextProtocol.Core`, `ModelContextProtocol.AspNetCore`) alongside `mcp-builder` best practices.

---

## 1. Dual-Direction MCP Strategy

MMA CLI participates in the MCP ecosystem in **two complementary capacities**:

1. **MCP Server (`Mma.Cli.Mcp`, Phase 4)** — Allows external AI coding assistants and IDEs (Claude Code, Cursor, VS Code Copilot, Windsurf, Antigravity) to inspect MMA solutions, model schemas, preview C# diffs, scaffold entities/relations, reverse-engineer databases, and trigger builds safely.
2. **MCP Client (`McpClientManager`, Phase 6)** — Allows MMA's own built-in AI Agent (`Mma.Cli.Agent`) to connect to user-configured external MCP servers (e.g., PostgreSQL MCP, SQL Server MCP, GitHub MCP, Context7 docs MCP) and incorporate their tools into planning workflows.

---

## 2. MCP Server Transports & Protocol Hygiene

### 2.1 Transport Matrix

| Transport | Entry Point | SDK Package | Security & Framing Rules |
| --- | --- | --- | --- |
| **`stdio`** | `mma mcp [--workspace <path>] [--read-only]` | `ModelContextProtocol` (`.WithStdioServerTransport()`) | **Zero non-JSON-RPC bytes on `stdout`**. All `ILogger` logs, EF Core diagnostics, and `dotnet` child-process outputs MUST go to `stderr` or `~/.mma/logs/mcp.log`. |
| **Streamable HTTP** | `Mma.Cli.Bff` mapped at `/mcp` (`app.MapMcp("/mcp")`) | `ModelContextProtocol.AspNetCore` (`.WithHttpTransport()`) | Bound to `127.0.0.1`; protected by localhost `Origin` validation and bearer token / session auth. Supports stateless JSON or stateful SSE notifications. |

Both transports bind the **exact same DI tool, resource, and prompt registry** (`AddMmaMcpServer()`) backed by `Mma.Cli.Core`.

---

## 3. MCP Server Tool Catalog (`mma_*`)

Following `mcp-builder` guidelines:
- Consistent `mma_` prefix with clear `verb_noun` names.
- Strict JSON Schema / DataAnnotation + `[Description]` metadata on every parameter.
- Every mutating scaffolding tool supports **`dryRun` (default `false` or optional preview)** returning a structured `DryRunDiffResult` (`planHash`, `filesAffected`, `unifiedDiffs`) without modifying disk.
- Actionable error messages returning machine-readable error codes and next-step hints (e.g., `"Entity 'Order' not found in solution manifest. Call mma_list_entities to inspect available entities."`).

### 3.1 Tool Inventory & MCP Annotations

| Tool Name | Purpose | Key Inputs | `readOnlyHint` | `destructiveHint` | `idempotentHint` | `openWorldHint` |
| --- | --- | --- | :---: | :---: | :---: | :---: |
| `mma_list_projects` | Lists registered MMA projects from local workspace index | `limit`, `offset` | `true` | `false` | `true` | `false` |
| `mma_inspect_solution` | Inspects solution layout (`.sln`/`.slnx`), detected mapper, `.mma/project.mma`, and drift status | `solutionPath` | `true` | `false` | `true` | `false` |
| `mma_list_entities` | Paginated list of entities in a solution with PK type, API flag, property/relation counts | `solutionPath`, `limit`, `offset`, `filter` | `true` | `false` | `true` | `false` |
| `mma_get_entity_details` | Full details (columns, types, nullability, validations, outgoing/incoming relations) for one entity | `solutionPath`, `entityName` | `true` | `false` | `true` | `false` |
| `mma_validate_project` | Validates `.mma/project.mma` against schema v2 and detects drift vs. C# files on disk | `solutionPath` | `true` | `false` | `true` | `false` |
| `mma_list_templates` | Lists active built-in and `.mma/templates/` override templates | `solutionPath`, `mapper` | `true` | `false` | `true` | `false` |
| `mma_create_solution` | Scaffolds a new ASP.NET Core solution and `.mma/project.mma` | `parentDirectory`, `solutionName`, `mapper`, `dryRun` | `false` | `false` | `false` | `false` |
| `mma_add_entity` | Generates entity, DTOs, FluentValidator, EF config, DbContext mapping, service, optional controller | `solutionPath`, `entityName`, `pkType`, `hasApi`, `properties[]`, `dryRun` | `false` | `false` | `false` | `false` |
| `mma_remove_entity` | Removes entity and generated artifacts (or stages diff when `dryRun: true`) | `solutionPath`, `entityName`, `dryRun` | `false` | `true` | `true` | `false` |
| `mma_add_property` | Adds/updates a property across entity, Modify/Read models, validator, and EF config via Roslyn | `solutionPath`, `entityName`, `propertyName`, `dataType`, `nullable`, `maxLength`, `dryRun` | `false` | `false` | `false` | `false` |
| `mma_remove_property` | Removes a property from entity, models, and EF config via Roslyn | `solutionPath`, `entityName`, `propertyName`, `dryRun` | `false` | `true` | `true` | `false` |
| `mma_add_relation` | Adds 1:N (or 1:1) relationship, FK column, navigation properties, and EF Fluent configuration | `solutionPath`, `parentEntity`, `childEntity`, `foreignKeyName`, `fkType`, `deleteBehavior`, `dryRun` | `false` | `false` | `false` | `false` |
| `mma_remove_relation` | Removes relationship across parent/child entities, DTOs, and EF config | `solutionPath`, `parentEntity`, `childEntity`, `foreignKeyName`, `dryRun` | `false` | `true` | `true` | `false` |
| `mma_apply_manifest` | Applies all pending draft entities/properties/relations in `.mma/project.mma` atomically | `solutionPath`, `planHash`, `dryRun` | `false` | `false` | `true` | `false` |
| `mma_introspect_database` | Inspects tables, columns, and foreign keys from SQL Server/Postgres/SQLite/MySQL (uses named connection profile or connection string) | `provider`, `connectionProfileOrString`, `includeTables[]`, `excludeTables[]` | `true` | `false` | `true` | `true` |
| `mma_import_database_schema` | Converts introspected tables/relations into entities and applies or stages manifest updates | `solutionPath`, `provider`, `connectionProfileOrString`, `tables[]`, `dryRun` | `false` | `false` | `false` | `true` |
| `mma_create_ef_migration` | Runs `dotnet ef migrations add` (and optionally `script --idempotent`) for target solution | `solutionPath`, `migrationName`, `scriptOnly` | `false` | `false` | `false` | `false` |
| `mma_build_solution` | Runs `dotnet build` on target solution and returns structured compiler diagnostics | `solutionPath`, `configuration` | `true` | `false` | `true` | `false` |

> **Security Safeguard for `--read-only` Mode**: Launching `mma mcp --read-only` disables all tools with `readOnlyHint == false` at tool registration time, allowing safe audit/inspection connections in untrusted sessions.

---

## 4. MCP Resources & Prompts

### 4.1 Exposed MCP Resources

| URI Template | MIME Type | Description |
| --- | --- | --- |
| `mma://projects` | `application/json` | Registered solutions in the local workspace index |
| `mma://solution/{encodedPath}/manifest` | `application/json` | Live `.mma/project.mma` v2 canonical schema |
| `mma://solution/{encodedPath}/erd.mmd` | `text/markdown` | Dynamically generated Mermaid `erDiagram` of current entities & relations |
| `mma://solution/{encodedPath}/entities/{entityName}` | `application/json` | Full entity metadata + generated file paths |
| `mma://solution/{encodedPath}/templates/{mapper}/{name}` | `text/plain` | Effective Scriban template text (override or embedded default) |

### 4.2 Exposed MCP Prompts

| Prompt Name | Arguments | Workflow Guided |
| --- | --- | --- |
| `design-domain-model` | `domainDescription`, `solutionPath` | Guides client LLM to inspect existing entities first, propose normalized entities/relations, run `dryRun: true`, and ask user confirmation before applying. |
| `import-legacy-database` | `solutionPath`, `dbProvider` | Guides safe DB introspection -> table filtering -> pluralization review -> dry-run import -> `mma_build_solution`. |
| `review-entity-schema` | `solutionPath`, `entityName` | Audits PK choices, nullability, cascade delete behaviors, and missing validations against `.mma/project.mma`. |
| `upgrade-solution-templates` | `solutionPath` | Inspects `.mma/templates/` overrides and compares against latest built-in Scriban templates. |

---

## 5. External MCP Client Integration Snippets

### Claude Code / Antigravity / Cursor (`mcpServers` JSON)

```json
{
  "mcpServers": {
    "mma-cli": {
      "command": "mma",
      "args": ["mcp", "--workspace", "D:/workspaces/my/SampleApi"],
      "env": {
        "MMA_LOG_LEVEL": "Warning"
      }
    }
  }
}
```

### Streamable HTTP Client (when BFF is running via `mma ui` or daemon mode)

```json
{
  "mcpServers": {
    "mma-cli-http": {
      "type": "streamable-http",
      "url": "http://127.0.0.1:5180/mcp",
      "headers": {
        "Authorization": "Bearer ${MMA_MCP_TOKEN}"
      }
    }
  }
}
```

---

## 6. Phase 2: External MCP Client inside MMA AI Agent (`McpClientManager`)

In Phase 6, MMA's own AI Agent (`Mma.Cli.Agent`) acts as an **MCP Client** using `ModelContextProtocol.Core`:

1. **Configuration (`~/.mma/mcp.json` or `.mma/mcp.json`)**:
   - Declares external servers (`stdio` command + args + `env:` secret refs, or Streamable HTTP URL + bearer `env:` ref).
   - Project-local `.mma/mcp.json` servers require **explicit user trust approval** before spawning subprocesses (preventing malicious repo `.mma/mcp.json` code execution!).
2. **Tool Namespacing & Policy**:
   - External tools are wrapped as `Microsoft.Extensions.AI` `AIFunction` objects named `ext_<serverName>_<toolName>`.
   - Default risk tier for external MCP tools is `RequiresApproval` unless marked read-only in a user's `autoApproveTools` allow-list.
3. **Resilience & Lifecycle**:
   - Lazy connection on session start, bounded connect/call timeouts, and automatic disposal when the agent session closes.

---

## 7. MCP Verification & Evaluation Plan (Per `mcp-builder` Phase 3 & 4)

1. **Unit & Protocol Contract Tests (`Mma.Cli.Mcp.Tests`)**:
   - Spin up `McpServer` over an in-memory `Stream` pipe (`stdio` test harness) and `WebApplicationFactory` (`/mcp` Streamable HTTP test harness).
   - Verify `tools/list`, JSON Schema validity, `dryRun: true` produces zero disk changes, `dryRun: false` updates solution + `.mma/project.mma`, `--read-only` excludes write tools, and zero non-protocol bytes are emitted to `stdout`.
2. **Interactive Inspection**:
   - Validate manually with `npx @modelcontextprotocol/inspector mma mcp --workspace <temp-solution>`.
3. **10-Question Read-Only MCP Evaluation Suite**:
   - Create `tests/Mma.Cli.Mcp.Tests/evals/mma_eval.xml` containing 10 independent, verifiable read-only multi-tool tasks over a fixture solution (e.g., resolving entity PK types, counting foreign keys on a child entity, detecting schema drift between `.mma/project.mma` and C# files, verifying active mapper, listing custom `.mma/templates/` overrides, checking build error counts).
