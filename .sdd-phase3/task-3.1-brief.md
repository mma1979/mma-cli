# Task 3.1 Implementation Brief — `Mma.Cli.Bff` Endpoints, SSE Job Runner & Security Middleware

- **Branch**: `agentic-v1`
- **Base Commit**: `f316aac2837157c67d76db8f6c9aec1f53063479`
- **Plan Reference**:
  - `outputs/20261120T205500Z_1ea6d98d_v1/04-SPEC-REACT-BFF-UI.md`
  - `outputs/20261120T205500Z_1ea6d98d_v1/07-PHASED-MODERNIZATION-PLAN.md` (Phase 3 — Task 3.1)

## Global Constraints (Mandatory)
1. Target `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, Central Package Management (`Directory.Packages.props`, ZERO inline `Version="..."` in any `.csproj`).
2. Strict Headless & Process Invariants:
   - Keep `src/Mma.Cli.Core` and `src/Mma.Cli.Database` untouched or 100% free of HTTP/UI/Web/banned APIs (`HeadlessInvariantTests` must pass).
   - Zero `Environment.Exit` in `src/Mma.Cli.Bff` or `src/Mma.Cli.App`.
3. Never create any folder named `ui` or `Ui` (`.gitignore:35` matches `[Uu]i/`).
4. Verify ALL builds pass with **0 warnings / 0 errors** under `dotnet build <proj> -c Release --no-incremental --warnaserror` across all `src/*` projects (`Core`, `Database`, `App`, `Bff`, `Core.Tests`, `Bff.Tests`) and **0 `NU1903` advisories** under `dotnet build Mma.Cli.sln -c Release --warnaserror:NU1903`.
5. All existing `315` tests (`25` `Mma.Cli.Tests` + `290` `Mma.Cli.Core.Tests`) plus all new `Mma.Cli.Bff.Tests` must pass cleanly.
6. Never stage or commit `.sdd-phase3/` or `progress.md`; never run `git add -A` or `git commit -a`. Never invoke nested subagents.

---

## Detailed Design Specification

### 1. Packages, Solution & Project Setup
- **`Directory.Packages.props`**:
  - Add:
    - `<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.1" />`
    - `<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.1" />`
- **`src/Mma.Cli.Bff/Mma.Cli.Bff.csproj`**:
  - `Sdk="Microsoft.NET.Sdk.Web"`, `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<IsPackable>false</IsPackable>`.
  - `<PackageReference Include="Microsoft.AspNetCore.OpenApi" />`
  - `<ProjectReference>` to `..\Mma.Cli.Core\Mma.Cli.Core.csproj` and `..\Mma.Cli.Database\Mma.Cli.Database.csproj`.
- **`src/tests/Mma.Cli.Bff.Tests/Mma.Cli.Bff.Tests.csproj`**:
  - `Sdk="Microsoft.NET.Sdk"`, `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<IsPackable>false</IsPackable>`.
  - Package references (no inline version): `coverlet.collector`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`.
  - Project references: `..\..\Mma.Cli.Bff\Mma.Cli.Bff.csproj`, `..\..\Mma.Cli.Core\Mma.Cli.Core.csproj`, `..\..\Mma.Cli.Database\Mma.Cli.Database.csproj`.
- **`Mma.Cli.sln`**:
  - Add `Mma.Cli.Bff` (`src\Mma.Cli.Bff\Mma.Cli.Bff.csproj`) and `Mma.Cli.Bff.Tests` (`src\tests\Mma.Cli.Bff.Tests\Mma.Cli.Bff.Tests.csproj`) cleanly under the `src` solution folder (verify with `dotnet sln Mma.Cli.sln list`).

### 2. Localhost Security, Identity Seam & Path Jail (`src/Mma.Cli.Bff/Security/`)
1. **`BffSecurityOptions.cs`**:
   - `BootstrapToken` (`string?`, read from `MMA_BFF_BOOTSTRAP_TOKEN` or config `"BffSecurity:BootstrapToken"`).
   - `AllowDevAutoSession` (`bool`, default `false`, overridable via `MMA_BFF_DEV_AUTH=1` or config/test configuration so headless/local dev can opt in explicitly without compromising production default security).
   - `AllowedRoots` (`List<string>` defaulting to `[Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]` when empty and no override configured, configurable via `MMA_BFF_ALLOWED_ROOTS` `;`-separated or config array `"BffSecurity:AllowedRoots"`).
   - `CookieName` (`"mma_bff_session"`), `CsrfHeaderName` (`"X-Mma-Csrf"`).
2. **`IMmaIdentityContext.cs`**:
   - Interface (`string UserId`, `string DisplayName`, `bool IsAuthenticated`) with `LocalSingleUserIdentity` default implementation (future OIDC/JWT remote seam).
3. **`BootstrapSessionManager.cs`** (Singleton):
   - Holds optional single-use `_pendingBootstrapToken` (`Interlocked` / `lock` atomic exchange) and concurrent dictionary of active sessions `SessionEntry(string SessionId, string CsrfToken, DateTimeOffset CreatedAtUtc)`.
   - `TryExchangeBootstrapToken(string? presentedToken, out SessionEntry? session, out string errorCode)`:
     - Uses constant-time comparison (`CryptographicOperations.FixedTimeEquals`) after verifying configured token exists; atomically marks `_bootstrapTokenConsumed = true` BEFORE returning success!
     - If already consumed: `errorCode = "AUTH_TOKEN_CONSUMED"`; if invalid/unconfigured: `"AUTH_TOKEN_INVALID"`.
   - `CreateDevSessionIfAllowed()`: creates a session ONLY when `options.AllowDevAutoSession && string.IsNullOrWhiteSpace(options.BootstrapToken)`.
   - `TryGetSession(string? sessionId, out SessionEntry? session)` & `ValidateCsrf(SessionEntry session, string? headerToken)` (via `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes!).
4. **`LocalhostSecurityMiddleware.cs`**:
   - Applied BEFORE static files & endpoints:
   - **Host Check**: Reads `context.Request.Host.Host`. Only permits `"127.0.0.1"`, `"localhost"`, `"::1"` (case-insensitive). Otherwise returns `403 Forbidden` RFC 9457 ProblemDetails (`errorCode: "INVALID_HOST"`).
   - **Origin Check**: If `Origin` header is present (not `"null"`? Reject `"null"` `Origin` on non-GET / API calls or reject any non-loopback `Origin`!): parse URI; only permit `http`/`https` whose `uri.Host` is `"127.0.0.1"`, `"localhost"`, or `"::1"`. Reject `"null"` or external `Origin`s with `403 Forbidden` ProblemDetails (`errorCode: "ORIGIN_NOT_ALLOWED"`).
   - **API Authentication & CSRF (`/api/v1/*`)**:
     - Public/unauthenticated routes: `GET /api/v1/health`, `POST /api/v1/auth/bootstrap`, `GET /api/v1/auth/session` (and `/openapi/*`).
     - All other `/api/v1/*` routes:
       1. Require valid session cookie `mma_bff_session` (`BootstrapSessionManager.TryGetSession`) -> else `401 Unauthorized` ProblemDetails (`errorCode: "UNAUTHORIZED"`).
       2. For `POST`, `PUT`, `PATCH`, `DELETE`: require valid `X-Mma-Csrf` header matching session's CSRF token -> else `403 Forbidden` ProblemDetails (`errorCode: "CSRF_INVALID"`).
5. **`WorkspacePathGuard.cs`** (Singleton):
   - `OperationResult<string> ValidateDirectoryPath(string? candidatePath)` & `ValidatePathWithinRoots(string? candidatePath, out string? errorCode)`:
     - Rejects null/whitespace or `'\0'` characters (`VALIDATION_ERROR`).
     - Resolves canonical path via `Path.GetFullPath(...)` (`TrimEndingDirectorySeparator`).
     - Checks prefix boundary against at least one canonical root in `AllowedRoots` (`OperatingSystem.IsWindows()` -> `OrdinalIgnoreCase`, else `Ordinal`, verifying exact match or `root + DirectorySeparatorChar`).
     - Walks every existing ancestor/target component (`DirectoryInfo` / `FileInfo`) checking `LinkTarget`: if any component is a symlink/junction (`LinkTarget is not null`), resolves `ResolveLinkTarget(returnFinalTarget: true)?.FullName` and verifies THAT resolved target is ALSO inside `AllowedRoots`!
     - Any out-of-bounds or symlink escape returns `Failure("PATH_OUTSIDE_ALLOWLIST", ...)` mapped to `403 Forbidden` ProblemDetails!
   - Also helper `ValidateSqlitePathIfLocal(string? serverOrPath, string? connectionString)` ensuring local `.sqlite`/`.db` files passed to DB introspection endpoints sit inside `AllowedRoots` (except `:memory:`).

### 3. Background Jobs & SSE Coordinator (`src/Mma.Cli.Bff/Jobs/`)
- `BffSseEvent(string EventId, DateTimeOffset Timestamp, string Type, string JobOrSessionId, object? Payload)`
- `IBackgroundJobCoordinator`:
  - `string CreateJob(string kind, int? projectId = null)`
  - `void Publish(string jobId, string type, object? payload = null)` (stores bounded replay history per job so SSE clients connecting right after job start/completion don't miss earlier events, AND wakes active live subscribers, completing the stream cleanly on `"done"` or `"error"`!).
  - `bool TryGetJobStream(string jobId, out IAsyncEnumerable<BffSseEvent> stream)` (accepts `CancellationToken` cleanly or `StreamEventsAsync(string jobId, CancellationToken ct)`).

### 4. BFF Application Services & Endpoints (`src/Mma.Cli.Bff/Services/`, `Endpoints/`, `Models/`)
1. **CamelCase JSON & RFC 9457 ProblemDetails (`BffProblemDetails.cs`)**:
   - Standard `ProblemDetails` responses (`status`, `title`, `detail`, `extensions["errorCode"]`) mapping:
     - `VALIDATION_ERROR`, `TEMPLATE_ERROR`, `ENTITY_EXISTS`, `PROPERTY_EXISTS`, `RELATION_EXISTS` -> `400 Bad Request`
     - `SOLUTION_NOT_FOUND`, `ENTITY_NOT_FOUND`, `PROPERTY_NOT_FOUND`, `RELATION_NOT_FOUND` -> `404 Not Found`
     - `PATH_OUTSIDE_ALLOWLIST` -> `403 Forbidden`
     - `MANIFEST_CONFLICT` -> `409 Conflict`
     - `PROCESS_FAILED`, `IO_ERROR`, `HTTP_ERROR` -> `500 Internal Server Error` or `422 Unprocessable Entity` where appropriate (`PROCESS_FAILED` -> `422` or `500`).
2. **Endpoints**:
   - `GET /api/v1/health`: returns `{ status: "healthy", version, environment, dotnetVersion, dotnetEfAvailable, timestampUtc }`.
   - `POST /api/v1/auth/bootstrap`: body `{ token }`. Sets cookie `mma_bff_session` (`HttpOnly = true`, `SameSite = SameSiteMode.Strict`, `Path = "/"`), returns `{ authenticated: true, csrfToken }`.
   - `GET /api/v1/auth/session`: validates existing cookie or issues dev session if `AllowDevAutoSession && BootstrapToken is empty`, returning `{ authenticated: bool, csrfToken?: string }`.
   - `GET /api/v1/fs/browse?path=...`: defaults to first `AllowedRoot` if `path` omitted; validates via `WorkspacePathGuard`; returns `{ currentPath, parentPath, allowedRoots, entries: [{ name, fullPath, isDirectory, isSolution }] }`.
   - `GET /api/v1/workspaces`: ensures SQLite registry initialized, lists registered workspaces enriched with manifest entity/relation counts & drift check if directory still exists.
   - `POST /api/v1/workspaces/register`: `{ solutionPath }`, validates via `WorkspacePathGuard`, locates `.sln`/`.slnx` via `ISolutionLocator`, loads manifest, registers & syncs in `IWorkspaceRegistry`.
   - `POST /api/v1/workspaces`: `{ parentDirectory, solutionName, mapper, dryRun }`, validates `parentDirectory` via `WorkspacePathGuard`, runs `ISolutionService.CreateAsync(...)`, registers & syncs in `IWorkspaceRegistry`, returns `{ workspace, summary }` (`201 Created`).
   - `POST /api/v1/workspaces/{id:int}/export`: validates workspace path, runs `ISolutionExporter.ExportAsync(...)`.
   - `GET /api/v1/projects/{id:int}`: loads workspace, validates path, loads `.mma/project.mma` (`ManifestStore`), runs `IProjectValidator.ValidateAsync(...)`, returns `{ workspace, solutionInfo, manifest, manifestHash, validation }`.
   - **Draft Modeling Endpoints** (`POST/DELETE` entities, properties, relations):
     - Mutate `.mma/project.mma` safely under `ISolutionLockManager` (`ManifestStore.UpdateAtomicAsync`), sync `IWorkspaceRegistry`, returning updated `{ manifest, contentHash }`:
       - `POST /api/v1/projects/{id:int}/entities` (`{ name, pkType = "Guid", hasService = true, hasApi = true }`)
       - `DELETE /api/v1/projects/{id:int}/entities/{entityName}`
       - `POST /api/v1/projects/{id:int}/entities/{entityName}/properties` (`{ name, dataType, nullable = false, isForeignKey = false, maxLength = null, required = null }`)
       - `DELETE /api/v1/projects/{id:int}/entities/{entityName}/properties/{propertyName}`
       - `POST /api/v1/projects/{id:int}/relations` (`{ parentEntity, childEntity, foreignKey, foreignKeyType = "Guid", relationType = "1:N", deleteBehavior = "Cascade" }`)
       - `DELETE /api/v1/projects/{id:int}/relations/{relationName}`
   - **`BffProjectMutationService.cs`** (orchestrating `plan-diff`, `apply`, `db-import/apply`):
     - `POST /api/v1/projects/{id:int}/plan-diff`:
       - Computes dry-run changes for ALL pending (`Applied == false`) entities, properties, and relations in `.mma/project.mma` WITHOUT touching C# files on disk!
       - Importantly: for unapplied entities that also have pending draft properties or relations on them, apply Roslyn rewriters in-memory on the staged `NewContent` and stage unified diffs via `IDryRunDiffEngine.ComputePlanAsync` so the preview accurately shows the full planned C# diff!
       - Computes a combined `planHash` bound to both `manifestHash` and staged file diffs.
     - `POST /api/v1/projects/{id:int}/apply` (`{ planHash?, runBuild = false, createMigration = false, migrationName? }`):
       - Verifies optional `planHash` matches current computed plan hash (`MANIFEST_CONFLICT` if drifted).
       - Creates a job in `IBackgroundJobCoordinator`, publishes `progress` events while executing:
         1. `IEntityService.GenerateAsync` for each pending entity (`e.Applied == false`), updating manifest entity state.
         2. `IPropertyService.ApplyAsync` for each pending non-FK property (`p.Applied == false`, skipping any FK column about to be added by a pending relation on the same entity/FK name so `RelationService` won't fail with `PROPERTY_EXISTS`!).
         3. `IRelationService.ApplyAsync` for each pending relation (`r.Applied == false`).
         4. Optional `IDotnetRunner.BuildAsync` if `runBuild == true` (streaming `log` events to SSE job).
       - Publishes `"done"` event with summary & returns `{ jobId, planHash, createdFiles, modifiedFiles, manifest }`.
   - `POST /api/v1/projects/{id:int}/build` & `POST /api/v1/projects/{id:int}/migrations`: start background build or `dotnet ef migrations add <name>`, streaming output to `IBackgroundJobCoordinator` and returning `{ jobId }` (`202 Accepted`).
   - `GET /api/v1/jobs/{jobId}/events`: writes SSE frames (`id:`, `event:`, `data: <json>\n\n`) with `Content-Type: text/event-stream` and `Cache-Control: no-cache` until `"done"` or `"error"` completes the job stream.
   - `POST /api/v1/db-import/test` & `POST /api/v1/db-import/introspect`: uses `IDatabaseIntrospectorFactory`; validates local SQLite file paths via `WorkspacePathGuard`; NEVER leaks DB passwords in ProblemDetails!
   - `POST /api/v1/projects/{id:int}/db-import/apply`: merges selected introspected entities/properties/relations into `.mma/project.mma` and optionally runs `ApplyAsync` (`applyImmediately: true/false`), returning `{ manifest, jobId?, plan? }`.
   - `GET /api/v1/projects/{id:int}/templates` & `PUT /api/v1/projects/{id:int}/templates/{mapper}/{name}`:
     - Lists allowed template names (`ModifyModel`, `ReadModel`, `Entity`, `Service`, `Controller`, `Validator`, `EntityConfig`, `Config`, `DbSetEntry`, `ConfigEntry`) for `AutoMapper` / `Mapster`, indicating `isCustomOverride` + active source content.
     - `PUT` validates `mapper` & `name` against whitelist, renders sample `TemplateModel` with `ITemplateRenderer.Render(...)` to reject malformed Scriban with `400 TEMPLATE_ERROR` before writing `.mma/templates/<mapper>/<name>.scriban` cleanly under solution lock!

### 5. `Program.cs` & Host Configuration
- Default bind URL when neither `--urls` nor `ASPNETCORE_URLS` is set and not inside `TestServer`: `builder.WebHost.UseUrls("http://127.0.0.1:5180")`.
- On `WebApplication`, add `app.Services.GetRequiredService<IServiceScopeFactory>()` init or startup check? Ensure SQLite `EnsureInitializedAsync` succeeds cleanly when `IWorkspaceRegistry` is first resolved (or on startup helper).
- Register `app.MapOpenApi("/openapi/{documentName}.json")` (available in all environments so `/openapi/v1.json` works in `WebApplicationFactory` and `npm run generate:api`).
- Call `app.UseMiddleware<LocalhostSecurityMiddleware>();` before endpoints and static files (`app.UseDefaultFiles(); app.UseStaticFiles();`).
- End `Program.cs` with `public partial class Program;` so `WebApplicationFactory<Program>` works cleanly.

### 6. TDD Tests (`src/tests/Mma.Cli.Bff.Tests/`)
1. **`BffSecurityMiddlewareTests.cs`**:
   - Disallowed `Host: evil.com` -> `403`, JSON ProblemDetails `errorCode == "INVALID_HOST"`
   - Disallowed `Origin: https://evil.example` -> `403`, `errorCode == "ORIGIN_NOT_ALLOWED"`
   - `"null"` `Origin` on POST -> `403`, `errorCode == "ORIGIN_NOT_ALLOWED"`
   - Unauthenticated `GET /api/v1/workspaces` -> `401`, `errorCode == "UNAUTHORIZED"`
   - Valid `POST /api/v1/auth/bootstrap` -> `200 OK`, `Set-Cookie` contains `HttpOnly` & `SameSite=Strict`, returns `csrfToken`
   - Replay of same bootstrap token -> `401`, `errorCode == "AUTH_TOKEN_CONSUMED"`
   - State-changing `POST /api/v1/workspaces` without `X-Mma-Csrf` or with mismatched token -> `403`, `errorCode == "CSRF_INVALID"`
   - `GET /api/v1/fs/browse?path=../../Outside` and `POST /api/v1/workspaces/register` outside `AllowedRoots` (including a symlink test when permitted or explicit parent `..` escape) -> `403`, `errorCode == "PATH_OUTSIDE_ALLOWLIST"`
2. **`WorkspaceEndpointsTests.cs`**:
   - End-to-end authenticated `WebApplicationFactory<Program>` workflow using isolated temp `AllowedRoots` and temp `workspace.db`:
     - `GET /api/v1/health` -> `200 OK`
     - `GET /api/v1/fs/browse` -> lists directories inside temp root
     - `POST /api/v1/workspaces` -> creates solution (`SampleBffApi`), registers in SQLite, returns `201 Created`
     - Draft modeling (`POST entities` `Order` + `OrderItem`, `POST properties` `Title` `string`, `POST relations` `Order -> OrderItem` via `OrderId`) updates `.mma/project.mma` with `Applied: false` while zero C# entity files exist on disk
     - `POST /api/v1/projects/{id}/plan-diff` -> returns non-empty `planHash` and unified diffs (`Order.cs`, `OrderItem.cs`, `OrderConfig.cs`, `ApplicationDbContext.cs`, etc.) with zero disk writes
     - Stale `planHash` passed to `POST /api/v1/projects/{id}/apply` -> `409 Conflict` (`MANIFEST_CONFLICT`)
     - Matching `planHash` passed to `POST /api/v1/projects/{id}/apply` -> writes C# files on disk, updates `.mma/project.mma` (`Applied == true`), syncs `workspace.db`, returns `jobId`
     - `GET /api/v1/jobs/{jobId}/events` -> returns `text/event-stream` containing `"progress"` and `"done"` SSE events!
     - `POST /api/v1/db-import/test`, `POST /api/v1/db-import/introspect`, and `POST /api/v1/projects/{id}/db-import/apply` against a temporary SQLite database
     - `GET /api/v1/projects/{id}/templates` & `PUT /api/v1/projects/{id}/templates/AutoMapper/Validator` (verifying valid Scriban save AND invalid Scriban `400 TEMPLATE_ERROR` rejection)
     - `GET /openapi/v1.json` -> `200 OK` valid OpenAPI JSON containing `/api/v1/workspaces` and `/api/v1/projects/{id}/plan-diff`

---

## Workflow & Verification Steps
1. Create test project + failing `BffSecurityMiddlewareTests.cs` and `WorkspaceEndpointsTests.cs` (or scaffold minimal compilation first, verify TDD test failures, then implement endpoints/security -> verify green).
2. Run `--no-incremental --warnaserror` on `Mma.Cli.Core`, `Mma.Cli.Database`, `Mma.Cli.Bff`, `Mma.Cli.App`, `Mma.Cli.Core.Tests`, and `Mma.Cli.Bff.Tests` -> `0 Warning(s), 0 Error(s)`.
3. Run `dotnet restore Mma.Cli.sln --force` -> `dotnet build Mma.Cli.sln --no-restore -c Release --warnaserror:NU1903` -> `dotnet test Mma.Cli.sln --no-build -c Release --verbosity normal`.
4. Verify with `git status --short` ONLY intended Task 3.1 files are modified/added (never `.sdd-phase3/`, `progress.md`, or bin/obj), stage explicitly, and commit with:
   `feat(bff): implement ASP.NET Core 10 BFF APIs, SSE streams, and localhost security`
5. Write concise report to `d:\workspaces\my\mma-cli\.sdd-phase3\task-3.1-report.md` including commit SHA, file inventory, build/test counts, and verification output.
