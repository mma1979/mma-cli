# Context — MMA CLI Modernization Blueprint (`v1`)

- **Conversation ID**: `1ea6d98d` (full ID: `1ea6d98d-f965-4676-8671-806f57991d0a`)
- **Output Directory**: `d:\workspaces\my\mma-cli\outputs\20261120T205500Z_1ea6d98d_v1\`
- **Created At**: `2026-11-20T20:55:00Z`
- **Active Persona**: `software-architect`
- **Loaded Skills**: `senior-architect`, `c4-architecture`, `mermaid-diagrams`, `writing-plans`, `legacy-modernizer`, `mcp-builder`, `ai-engineer`

---

## 1. Clarifications Asked & Confirmed

The user requested a modernization plan and refactor/feature suggestions for MMA CLI covering:
1. Redesigning the UI to use React with the BFF pattern.
2. Adding Model Context Protocol (MCP) capability.
3. Adding an AI Agent mode with configurable providers.

All 6 clarifying questions and suggested defaults were presented and confirmed via user response: **`proceed`**.

| Question | Confirmed Decision |
| --- | --- |
| **1. Persona & Skills** | `software-architect` with `senior-architect`, `c4-architecture`, `mermaid-diagrams`, `writing-plans`, `legacy-modernizer`, `mcp-builder`, `ai-engineer`. |
| **2. Deliverable Scope & Tier** | **Plan-only first, Tier 2** in `outputs/20261120T205500Z_1ea6d98d_v1/`, leaving source files and `docs/architecture/` untouched until approval. |
| **3. React + BFF Runtime Topology** | **Local-first ASP.NET Core 10 BFF serving a prebuilt Vite/React/TypeScript SPA** packaged inside the `mma-cli` .NET global tool (zero Node.js required for end users), with clean auth/session/origin boundaries for future remote hosting. |
| **4. MCP Direction & Transports** | **Both** — MCP Server first via official C# `ModelContextProtocol` SDK supporting `stdio` (`mma mcp`) and Streamable HTTP (`/mcp` on BFF), followed by MCP Client integration in the AI Agent runtime. |
| **5. AI Agent Mode & Providers** | **Domain-tool-driven agent with plan/diff preview & HITL approval** built on `Microsoft.Extensions.AI` + `Microsoft.Agents.AI` supporting OpenAI, Anthropic, Google Gemini, Azure OpenAI, Ollama, and OpenAI-compatible endpoints, with secrets via OS keyring / env vars (never plaintext in `.mma/config.json`). Arbitrary file edits and shell execution disabled unless explicitly opted in. |
| **6. Additional Refactors & Backlog** | **Included** — categorized into blocking prerequisites, foundational refactors, and prioritized feature themes. |

---

## 2. Explicit Assumptions

1. **Plan-Only Deliverable**: No production code, `.csproj`, or existing documentation under `docs/architecture/` is altered in this turn.
2. **Contributor Build Prerequisite**: End users do not need Node.js installed when running `dotnet tool install -g mma-cli`, whereas contributors and CI pipelines will run Node.js/npm during build/pack to compile `Mma.Cli.Web` into `Mma.Cli.Bff/wwwroot/`.
3. **Backward Compatibility**: Existing `mma new` (`n`), `mma generate entity|property|relation` (`g e|p|r`), `mma import`, `mma ui`, and `.mma/templates/` overrides remain supported across the migration.
4. **Target Framework**: All .NET projects remain on `.NET 10` (`net10.0`).
5. **Repo Naming Constraint**: Because `.gitignore:35` ignores `[Uu]i/`, the new React frontend and BFF projects will use `src/Mma.Cli.Web` and `src/Mma.Cli.Bff` (never `ui/`).

---

## 3. Verified Codebase Baseline (Snapshot)

- **Solution**: `Mma.Cli.sln` — 4 projects (`Mma.Cli.AppV4` v4.5.8, `Mma.Cli.Shared`, `Mma.Cli.UI`, `Mma.Cli.Tests`), all targeting `net10.0`.
- **Test Baseline**: `dotnet test d:\workspaces\my\mma-cli\Mma.Cli.sln --no-restore --verbosity minimal` exited `0` (**8 passed, 0 failed, 0 skipped** in `605 ms`).
- **Package & Compiler Warnings Observed During Test Run**:
  - `NU1903` (High severity): `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (`https://github.com/advisories/GHSA-2m69-gcr7-jv3q`).
  - `12x CS8618` in `Mma.Cli.Shared/Builders/PropertiesBuilder.cs:25,30`.
  - `2x CS8602` in `Mma.Cli.Shared/Helpers/ConfigHelper.cs:22,35`.
  - `1x SYSLIB0014` (`ServicePointManager`) in `Mma.Cli.Shared/Builders/ImportFactory.cs:23`.
- **CI/CD**: `.github/` contains zero workflow files.
- **Repository Hygiene**: `build_error.txt` is tracked at repo root; `Mma.Cli.UI/after-build.bat` and `commands.json` implement manual Windows-only UI publishing (`Mma.Cli.UI.csproj:23-25` has MSBuild target commented out).

---

## 4. Epistemic Status Legend

- **Verified Fact**: Directly observed in files read (`Program.cs`, `CliData.cs`, `DatabaseEngine.cs`, `ConfigHelper.cs`, `Output.cs`, builders, `.razor` pages, `.csproj` files, `.gitignore`, `README.md`, `docs/architecture/*.md`) or tool outputs (`dotnet test`).
- **Assumption**: Confirmed user preferences and scope choices listed in Section 2.
- **Recommendation**: Target architecture, ADRs, technology selections, phase decomposition, and backlog proposals.
