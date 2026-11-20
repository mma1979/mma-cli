# Summary — MMA CLI Modernization Blueprint (`v1`)

## 1. Objective

Produce a complete, actionable **Tier 2** architecture and implementation blueprint to modernize **MMA CLI** ([Mma.Cli.sln](file:///d:/workspaces/my/mma-cli/Mma.Cli.sln)) across three core initiatives—**React + BFF UI redesign**, **MCP capability (Server + Client)**, and **AI Agent mode with configurable providers**—plus foundational codegen, security, and DX refactors and a prioritized feature backlog.

---

## 2. Work Done

All 11 versioned planning deliverables were created inside `d:\workspaces\my\mma-cli\outputs\20261120T205500Z_1ea6d98d_v1\` without modifying any existing source files or `docs/architecture/`:

| Deliverable | Purpose |
| --- | --- |
| [01-EXECUTIVE-SUMMARY-AND-ASSESSMENT.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/01-EXECUTIVE-SUMMARY-AND-ASSESSMENT.md) | Vision, verified baseline, 5-category Risk & Technical Debt Matrix (with exact `file:line` evidence), Strangler Fig strategy |
| [02-C4-TARGET-ARCHITECTURE.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/02-C4-TARGET-ARCHITECTURE.md) | Mermaid C4 Level 1–4 diagrams (`C4Context`, `C4Container`, `C4Component`, `C4Deployment`), 3 dynamic sequence flows, target project layout |
| [03-ARCHITECTURE-DECISIONS-ADRS.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/03-ARCHITECTURE-DECISIONS-ADRS.md) | 7 ADRs (`Mma.Cli.Core`, Roslyn+Scriban+Diff, `.mma/project.mma` v2 + AppData SQLite, ASP.NET 10 BFF+React, C# MCP SDK, `Microsoft.Extensions.AI`+`Microsoft.Agents.AI`, `System.CommandLine` v2+`Spectre.Console`) |
| [04-SPEC-REACT-BFF-UI.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/04-SPEC-REACT-BFF-UI.md) | Blazor-to-React parity & upgrade matrix, BFF REST/SSE endpoints, OpenAPI-first TS client, `@xyflow/react` ERD architecture, localhost security hardening, cross-platform MSBuild packaging |
| [05-SPEC-MCP-CAPABILITY.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/05-SPEC-MCP-CAPABILITY.md) | Dual-transport (`stdio` + `/mcp` Streamable HTTP) MCP Server tool/resource/prompt catalog, IDE snippets, Phase 2 external MCP Client (`McpClientManager`), 10-question eval plan |
| [06-SPEC-AI-AGENT-MODE.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/06-SPEC-AI-AGENT-MODE.md) | Unified CLI + UI Agent on `Microsoft.Extensions.AI` + `Microsoft.Agents.AI`, 6 provider types, OS Keyring/`env:` secret hygiene, HITL plan/diff approval loop, redaction & circuit breakers |
| [07-PHASED-MODERNIZATION-PLAN.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md) | 7-phase (`Phase 0`–`Phase 6`) TDD implementation plan with exact file lists, interface contracts, verification commands, top 5 failure modes, and rollback strategy |
| [08-REFACTORS-AND-FEATURE-BACKLOG.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/08-REFACTORS-AND-FEATURE-BACKLOG.md) | 12 blocking security/bug prerequisites, 12 foundational refactors, and 26 prioritized feature ideas across 6 product themes |
| [CONTEXT.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/CONTEXT.md) | Clarifications, active persona/skills, explicit assumptions, verified baseline snapshot |
| [SUMMARY.md](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/SUMMARY.md) | This deliverable summary, verification record, and next steps |
| [COMMIT_MESSAGE.txt](file:///d:/workspaces/my/mma-cli/outputs/20261120T205500Z_1ea6d98d_v1/COMMIT_MESSAGE.txt) | Conventional Commit message (`docs(architecture): ...`) |

### Key Architectural Decisions

1. **Headless `Mma.Cli.Core` First**: Extract all scaffolding/patching logic out of `Mma.Cli.Shared` away from `Environment.Exit`, `Console`, and `string[] args`, so CLI, BFF, MCP, and AI Agent share one deterministic engine.
2. **Roslyn + Scriban + Dry-Run Diffs**: Replace `lines[^2]` / `index + 10` patching with `Microsoft.CodeAnalysis.CSharp` syntax rewriters, `Scriban` templates (backward-compatible with `.mma/templates/` overrides), and an in-memory `DryRunDiffEngine`.
3. **ASP.NET Core 10 Local BFF + Prebuilt React 19 SPA**: Keep Node.js out of end-user workstations; serve prebuilt React (`Mma.Cli.Web`) from `Mma.Cli.Bff`, secured via single-use launch token, `HttpOnly; SameSite=Strict` cookie + CSRF header, strict `Origin`/`Host` validation, and moved SQLite DB outside `wwwroot/`.
4. **Native `.NET 10` MCP & Multi-Provider AI**: Use official `ModelContextProtocol` (`stdio` + `/mcp`) and `Microsoft.Extensions.AI` + `Microsoft.Agents.AI` (`OpenAI`, `Anthropic`, `Google.GenAI`, `Azure.AI.OpenAI`, `OllamaSharp`, OpenAI-compatible) with OS keyring/env secret hygiene and HITL plan/diff approval.

---

## 3. Active Persona & Skills

- **Persona**: `software-architect`
- **Skills Loaded**: `senior-architect`, `c4-architecture`, `mermaid-diagrams`, `writing-plans`, `legacy-modernizer`, `mcp-builder`, `ai-engineer`

---

## 4. Assumptions & Open Questions

- **Active Assumptions**: Plan-only Tier 2 first; local-first BFF+React packaged inside `mma-cli` with remote-ready auth seam; dual-transport MCP Server first + Phase 2 MCP Client; domain-tool-driven AI Agent with HITL plan/diff approval by default; full refactor & feature backlog included.
- **Open Decisions Before Implementation Begins**:
  1. Confirm starting execution with **Phase 0** (security hotfixes, `NU1903`, process-kill fix, builder path fixes, characterization tests, GitHub Actions CI).
  2. Choose preferred execution approach per `writing-plans`: **Subagent-driven** (recommended for cross-cutting contract gates) or **Native**.
  3. Confirm priority of **Mapperly** / **Minimal API starter profile** (listed as `P1` backlog items in `08-REFACTORS-AND-FEATURE-BACKLOG.md` vs. Phase 2 scope).

---

## 5. Verification

| Check | Command / Scope | Observed Result |
| --- | --- | --- |
| **Baseline Test Run** | `dotnet test d:\workspaces\my\mma-cli\Mma.Cli.sln --no-restore --verbosity minimal` | Exited `0`: **8 passed, 0 failed, 0 skipped** (`605 ms`); emitted `NU1903` (`SQLitePCLRaw.lib.e_sqlite3 2.1.11`) and 15 compiler warnings. |
| **Git Cleanliness Check** | `git status --short` + file timestamp check | Only `outputs/20261120T205500Z_1ea6d98d_v1/` was created in this session; pre-existing uncommitted repository changes (`Mma.Cli.*`, `docs/`, `build_error.txt`) were left untouched. |
| **Deliverable Validation Script** | PowerShell validation across all 11 files in `outputs/20261120T205500Z_1ea6d98d_v1/` | Verified all 11 files exist and are non-empty, balanced Markdown fences, valid Mermaid headers without raw HTML tags, zero unfinished placeholder markers, and valid `COMMIT_MESSAGE.txt` length/trailers. |

---

## 6. Next Steps

1. Review the blueprint starting with `01-EXECUTIVE-SUMMARY-AND-ASSESSMENT.md`, `02-C4-TARGET-ARCHITECTURE.md`, and `07-PHASED-MODERNIZATION-PLAN.md`.
2. Approve starting **Phase 0** (security/crash hotfixes + characterization tests + CI) and select execution mode:
   - **Subagent-driven** *(Recommended)* — fresh subagent per task + independent reviewer gate to enforce `Mma.Cli.Core` / OpenAPI / MCP / `IChatClient` contracts across the 7 phases.
   - **Native** — sequential execution in the main session followed by a final branch review.

---

## 7. References

- Repository files inspected: [Mma.Cli.sln](file:///d:/workspaces/my/mma-cli/Mma.Cli.sln), [README.md](file:///d:/workspaces/my/mma-cli/README.md), [.gitignore](file:///d:/workspaces/my/mma-cli/.gitignore), [commands.json](file:///d:/workspaces/my/mma-cli/commands.json), [Mma.Cli.AppV4/Program.cs](file:///d:/workspaces/my/mma-cli/Mma.Cli.AppV4/Program.cs), `Mma.Cli.Shared/Builders/*.cs`, `Mma.Cli.Shared/Helpers/*.cs`, `Mma.Cli.Shared/Data/*.cs`, `Mma.Cli.UI/Program.cs`, `Mma.Cli.UI/Data/*.cs`, `Mma.Cli.UI/Pages/*.razor`, `Mma.Cli.UI/Components/*.razor`, `Mma.Cli.Tests/*.cs`, `docs/architecture/*.md`.
- Verified ecosystem packages & specifications:
  - Model Context Protocol C# SDK: `ModelContextProtocol`, `ModelContextProtocol.Core`, `ModelContextProtocol.AspNetCore` (`https://modelcontextprotocol.io/specification/draft.md`).
  - Microsoft AI & Agent stacks: `Microsoft.Extensions.AI`, `Microsoft.Agents.AI`, `OpenAI`, `Microsoft.Extensions.AI.OpenAI`, `Azure.AI.OpenAI`, `Azure.Identity`, `Anthropic` (v10+), `Google.GenAI`, `OllamaSharp`.
  - .NET 10 CLI: `System.CommandLine` v2.0.0.
