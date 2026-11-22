# Task 3.1 Completion Report — `Mma.Cli.Bff` Endpoints, SSE Job Runner & Security Middleware

- **Status**: Completed
- **Branch**: `agentic-v1`
- **Commit SHA**: `adcd404632a9111da255b42489ef6cc4d6df830c`
- **Commit Message**: `feat(bff): implement ASP.NET Core 10 BFF APIs, SSE streams, and localhost security`

---

## Summary of Implementation

Implemented `src/Mma.Cli.Bff` (ASP.NET Core 10 Minimal APIs, OpenAPI `/openapi/v1.json`, SSE job coordinator, RFC 9457 `ProblemDetails` mapping, localhost/origin/CSRF/bootstrap-token security middleware, symlink-aware `WorkspacePathGuard`, and in-memory Roslyn/Scriban `plan-diff` + atomic `apply` orchestration) and `src/tests/Mma.Cli.Bff.Tests` (`WebApplicationFactory<Program>` security & end-to-end integration tests) using strict TDD.

Note on Central Package Management (`Directory.Packages.props`):
- `Microsoft.AspNetCore.Mvc.Testing` added at `10.0.1`.
- `Microsoft.AspNetCore.OpenApi` added at `10.0.15` (instead of `10.0.1`, which transitively pulled vulnerable `Microsoft.OpenApi 2.0.0` / `GHSA-v5pm-xwqc-g5wc`), achieving **0 `NU1903` advisories** across the full solution.

---

## File Inventory (22 files committed)

### Modified
- `Directory.Packages.props` — added `Microsoft.AspNetCore.Mvc.Testing` (`10.0.1`) and `Microsoft.AspNetCore.OpenApi` (`10.0.15`)
- `Mma.Cli.sln` — added `Mma.Cli.Bff` and `Mma.Cli.Bff.Tests` under the `src` solution folder

### Added — `src/Mma.Cli.Bff/`
- `Mma.Cli.Bff.csproj` — `Microsoft.NET.Sdk.Web`, `net10.0`, nullable/implicit usings enabled, CPM compliant
- `Program.cs` — default bind `http://127.0.0.1:5180`, camelCase JSON + string enum converter, lazy DI config resolution for `BffSecurityOptions` and `WorkspaceDbContext`, startup `EnsureInitializedAsync`, `LocalhostSecurityMiddleware`, static files, OpenAPI `/openapi/{documentName}.json`, and Minimal API route groups (`public partial class Program;`)
- `Security/BffSecurityOptions.cs` — config + `MMA_BFF_BOOTSTRAP_TOKEN`, `MMA_BFF_DEV_AUTH`, `MMA_BFF_ALLOWED_ROOTS` support
- `Security/IMmaIdentityContext.cs` — `IMmaIdentityContext` seam + `LocalSingleUserIdentity`
- `Security/BootstrapSessionManager.cs` — single-use constant-time (`CryptographicOperations.FixedTimeEquals`) bootstrap token exchange, dev auto-session gating, session registry, and fixed-time `X-Mma-Csrf` check
- `Security/LocalhostSecurityMiddleware.cs` — loopback `Host` (`127.0.0.1`, `localhost`, `::1`, `[::1]`) check (`403 INVALID_HOST`), strict `Origin` check including `"null"` rejection (`403 ORIGIN_NOT_ALLOWED`), cookie session enforcement (`401 UNAUTHORIZED`), and CSRF header validation (`403 CSRF_INVALID`)
- `Security/WorkspacePathGuard.cs` — canonical path boundary verification, ancestor/component symlink & junction target resolution (`PATH_OUTSIDE_ALLOWLIST`), null-byte rejection, and `ValidateSqlitePathIfLocal`
- `Jobs/BffSseEvent.cs` — SSE envelope record (`EventId`, `Timestamp`, `Type`, `JobOrSessionId`, `Payload`)
- `Jobs/IBackgroundJobCoordinator.cs` — job stream & publishing contract
- `Jobs/BackgroundJobCoordinator.cs` — thread-safe bounded replay buffer + live subscriber channels completing on `"done"` / `"error"`
- `Models/BffDtos.cs` — request/response DTO records for OpenAPI contract generation
- `Services/BffProblemDetails.cs` — RFC 9457 `ProblemDetails` factory/writer + `BffErrorCodes` status mapping
- `Services/BffProjectMutationService.cs` — in-memory virtual-file + Roslyn/Scriban `plan-diff` (`ComputePlanAsync`), manifest+diff `planHash` concurrency check, per-project `ApplyAsync` pipeline (entities -> non-FK properties -> relations -> registry sync -> optional build/migration + SSE progress), `ApplyDbImportAsync`, and `TryResolveProjectSubpath`
- `Endpoints/HealthAndAuthEndpoints.cs` — `GET /api/v1/health`, `POST /api/v1/auth/bootstrap`, `GET /api/v1/auth/session`
- `Endpoints/WorkspaceEndpoints.cs` — `GET /api/v1/fs/browse`, `GET /api/v1/workspaces`, `POST /api/v1/workspaces/register`, `POST /api/v1/workspaces`, `POST /api/v1/workspaces/{id:int}/export`
- `Endpoints/ProjectEndpoints.cs` — `GET /api/v1/projects/{id:int}`, draft entity/property/relation `POST`/`DELETE`, `plan-diff`, `apply`, `build` (`202 Accepted`), `migrations` (`202 Accepted`), `GET /api/v1/jobs/{jobId}/events` (`text/event-stream`)
- `Endpoints/DbImportAndTemplateEndpoints.cs` — `POST /api/v1/db-import/test`, `POST /api/v1/db-import/introspect` (with secret redaction), `POST /api/v1/projects/{id:int}/db-import/apply`, `GET /api/v1/projects/{id:int}/templates`, `PUT /api/v1/projects/{id:int}/templates/{mapper}/{name}`

### Added — `src/tests/Mma.Cli.Bff.Tests/`
- `Mma.Cli.Bff.Tests.csproj`
- `BffSecurityMiddlewareTests.cs`
- `WorkspaceEndpointsTests.cs`

---

## Verification Results

1. **TDD Baseline (Red)**:
   - Command: `dotnet test src/tests/Mma.Cli.Bff.Tests/Mma.Cli.Bff.Tests.csproj -c Release --no-restore --verbosity minimal` (against minimal stub `Program.cs`)
   - Result: `Failed: 13, Passed: 0, Total: 13` (`NotFound` on unimplemented endpoints/middleware)
2. **Strict `--no-incremental --warnaserror` Builds**:
   - `Mma.Cli.Core`: `0 Warning(s), 0 Error(s)`
   - `Mma.Cli.Database`: `0 Warning(s), 0 Error(s)`
   - `Mma.Cli.Bff`: `0 Warning(s), 0 Error(s)`
   - `Mma.Cli.App`: `0 Warning(s), 0 Error(s)`
   - `Mma.Cli.Core.Tests`: `0 Warning(s), 0 Error(s)`
   - `Mma.Cli.Bff.Tests`: `0 Warning(s), 0 Error(s)`
3. **`[Uu]i/` Directory Check**:
   - `Get-ChildItem -Recurse -Directory src | Where-Object { $_.Name -ieq 'ui' }` -> empty (0 matches).
4. **Full Solution Restore, `NU1903` Audit Build & Test Suite**:
   - `dotnet restore Mma.Cli.sln --force`: `0 NU1903` advisories
   - `dotnet build Mma.Cli.sln --no-restore -c Release --warnaserror:NU1903`: `0 Error(s)`
   - `dotnet test Mma.Cli.sln --no-build -c Release --verbosity normal`:
     - `Mma.Cli.Tests.dll`: `25/25` passed (`0` failed, `0` skipped)
     - `Mma.Cli.Core.Tests.dll`: `290/290` passed (`0` failed, `0` skipped)
     - `Mma.Cli.Bff.Tests.dll`: `16/16` passed (`0` failed, `0` skipped)
     - **Total**: **331/331 passed** (`0` failed, `0` skipped)
