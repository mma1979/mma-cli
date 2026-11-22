# Phase 3 Task 3.2 — React Workspace Report

- **Status**: Implementation complete; frontend checks pass. The .NET suite passed 331/331 once under explicit host-safe overrides, but subsequent runs exposed sandbox filesystem/process failures and one repeatable Core bracket-path test failure.
- **Branch/base**: `agentic-v1`, task base `adcd404632a9111da255b42489ef6cc4d6df830c`.
- **Commit**: None. No files were staged or committed, per the standing instruction to preserve the existing uncommitted work.
- **Scope**: `src/Mma.Cli.Web/**` and `.gitignore`. Existing Task 3.3 changes were not edited.

## Delivered

- React 19 + TypeScript SPA with workspace registration/creation, dry-run file summaries, project ERD/table editing, entity/property/relation forms, templates, plan diff/apply, migration/job terminal, and Phase 5 settings placeholder.
- Route-level lazy loading and separately chunked flow/diff/highlighting dependencies.
- OpenAPI schema generated from the running BFF at `src/Mma.Cli.Web/src/api/schema.d.ts`; API model aliases now derive from the generated `components` types.
- DB-import connect/introspect/review/apply flow. Connection secrets are removed from the form when entering Introspect, wiped from refs and input elements when backing out to Connect, cleared after introspection/provider change/on unmount, and never resent in the apply request.
- Auth 401 recovery, `.gitignore` rules for build/test/local files, and Vitest/Playwright coverage.

## Verification

| Check | Result |
| --- | --- |
| `npm run generate:api` | Pass; generated schema from `http://127.0.0.1:5180/openapi/v1.json`. |
| `npm run typecheck` | Pass. |
| `npm run lint` | Pass. |
| `npm test` | Pass: 11 test files, 24 tests. |
| `npx playwright test` | Pass: 3/3 using a fresh BFF and installed Microsoft Edge (`PLAYWRIGHT_CHANNEL=msedge`, `CI=1`). The live flow creates a workspace, adds `Order`/`OrderItem`, adds `Title`, creates the relation, checks `Order.cs`, applies, and verifies terminal completion, applied badges, and changed manifest hash. |
| `npm run build` | Pass; bundle budget passes. Entry gzip: 104.3 KB / 300 KB limit. Vite still warns that some lazy chunks exceed 500 KB minified. |
| TypeScript hygiene | Pass: no `any`, `@ts-ignore`, or `@ts-expect-error` matches; no `ui`/`Ui` source directory. |
| .NET restore/build | Pass with `DOTNET_CLI_USE_MSBUILD_SERVER=0` and `MSBUILDDISABLENODEREUSE=1`: `dotnet restore Mma.Cli.sln --force --disable-parallel`; `dotnet build Mma.Cli.sln --no-restore -c Release --verbosity minimal` has 0 errors and 64 warnings in `Mma.Cli.UI`. |
| .NET regression gate | **Passed once (331/331), not stable under this host.** With `MSTest.EnableParentProcessQuery=false` temporarily set in the three ignored test runtimeconfigs, `TEMP`/`TMP` redirected to ignored workspace storage, and EventLog logging suppressed, `dotnet test Mma.Cli.sln --no-build -c Release --verbosity minimal` passed `25/25` legacy, `16/16` BFF, and `290/290` Core tests. The testhost switch is present in [VSTest source](https://raw.githubusercontent.com/microsoft/vstest/main/src/testhost.x86/DefaultEngineInvoker.cs). A repeat with the same settings failed one legacy test at `Directory.Move` with `Access is denied`; a run after restoring the runtimeconfigs to their default `true` again hit the parent-process `Win32Exception (5)`. The Linux-container and isolated Windows runs also failed `Cli_SpecialCharacterPathWithBrackets_EscapesSpectreMarkupCleanly` (`CliCommandRoutingTests.cs:345`), expecting `missing-[dir]`. Runtimeconfigs were restored to `true`; no .NET source was changed. |
| Workspace/harness validators | Not run: `./init.sh`, `scripts/validate-workspace.ps1`, and `skills/harness-creator/scripts/validate-harness.mjs` are absent from the repository and the installed skill bundle. |

## Red → Green Notes

- Vitest initially exposed a stale DB-import heading assertion; the locator was corrected and the wizard regression test passes.
- Playwright initially encountered React Flow's background intercepting node clicks; the test now selects entities through the accessible entity rail. The complete isolated E2E suite passes.
- Final review found credentials were not wiped when backing from Introspect to Connect. The transition now clears both in-memory refs and input values; the DB-import regression test covers this path and secret-free apply.
- The bundled Playwright Chromium executable is not installed here; using the already installed Edge avoided a browser download.

## Remaining

- The user-selected normal PowerShell result has not been supplied. Although the full suite passed once with explicit host-safe overrides, subsequent runs were not stable; the workspace/harness validator scripts are also absent. Keep Task 3.2 unchecked in `progress.md` until a stable 331-test run and required validators are available.
- No-file-change probes with `DOTNET_MSTest_EnableParentProcessQuery=false` and `DOTNET_AppContextSwitchOverrides=MSTest.EnableParentProcessQuery=false` were ignored; the testhost still failed with the parent-process `Win32Exception (5)`.
- Read-only code-path tracing found the missing path is carried from `SolutionLocator` through `CliOutputFormatter` with markup escaping before the test asserts on `consoleWriter`. Exact cause remains unconfirmed; capturing the full writer output would require test instrumentation outside Task 3.2 scope. `csharp-ls --solution Mma.Cli.sln --diagnose` could not load MSBuild; this host has .NET SDK `11.0.100` and runtimes 10/11, not SDK 10.
- No commit was created. Continue with Task 3.3 only after the .NET regression gate is resolved and the user authorizes moving on.
