# Container diagram

MMA CLI is packaged as one .NET tool. Its Blazor Server UI is a separately built executable distributed inside that tool and launched on demand. The shared project is a code library, so it is intentionally represented as an internal implementation detail in component diagrams rather than as a C4 container.

```mermaid
C4Container
  title Container diagram for MMA CLI

  Person(developer, "Application developer", "Uses the generator from a terminal or browser")

  System_Boundary(mma, "MMA CLI") {
    Container(cli, "MMA command-line tool", ".NET 10 console global tool", "Parses commands, runs interactive mode, scaffolds and modifies solutions")
    Container(ui, "MMA UI", "ASP.NET Core Blazor Server", "Local browser workspace for modelling projects and applying generation changes")
    ContainerDb(uiDb, "MMA UI workspace database", "SQLite / EF Core", "Persists projects, entities, fields, and relations for the UI")
  }

  Container_Ext(generatedSolution, "Generated API solution", "ASP.NET Core source tree", "Created or updated solution, including .mma metadata and optional template overrides")
  System_Ext(generatorApi, "Generator API", "External HTTP API", "Receives exported project definitions")
  System_Ext(sqlServer, "SQL Server instance", "Microsoft SQL Server", "Database schema selected for import")

  Rel(developer, cli, "Executes commands", "Terminal")
  Rel(developer, ui, "Uses workspace", "Browser / HTTPS")
  Rel(cli, ui, "Launches packaged UI with `mma ui`", "dotnet process")
  Rel(cli, generatedSolution, "Reads and writes generated files", "File system")
  Rel(ui, uiDb, "Stores and reads workspace state", "EF Core / SQLite")
  Rel(ui, generatedSolution, "Applies generated changes and runs EF commands", "File system / dotnet")
  Rel(ui, sqlServer, "Inspects tables and columns", "SQL Server protocol")
  Rel(cli, generatorApi, "Posts project metadata", "JSON/HTTPS")
```

## Container responsibilities

| Container | Responsibility |
| --- | --- |
| MMA command-line tool | Command dispatch, interactive prompts, source generation, watches `.mma/project.mma`, and starts the UI. |
| MMA UI | Browser workflow for project modelling, database-first import, migration application, and generator invocation. |
| MMA UI workspace database | Local-only state for UI projects, entities, entity rows, and relations. |
| Generated API solution | The consumer-owned codebase modified by generator builders. |
