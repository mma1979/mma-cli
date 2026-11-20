# UI component diagram

The UI is a local Blazor Server application. It lets users model projects visually, persist the working model in SQLite, inspect an existing SQL Server database, and apply the shared code-generation builders to a generated solution.

```mermaid
C4Component
  title Component diagram for MMA UI

  Person(developer, "Application developer", "Models and applies changes in the browser")
  ContainerDb(uiDb, "MMA UI workspace database", "SQLite", "Local workspace state")
  Container_Ext(solution, "Generated API solution", "Source tree", "Project that receives generated code")
  System_Ext(sqlServer, "SQL Server instance", "Microsoft SQL Server", "Database selected for schema import")

  Container_Boundary(ui, "MMA UI") {
    Component(pages, "Pages and workspace components", "Razor / Radzen", "New project, workspace, grids, and import dialog")
    Component(cliData, "CliData", "C# application service", "Loads workspace data and invokes builders to migrate changes")
    Component(databaseEngine, "DatabaseEngine", "Microsoft.Data.SqlClient", "Reads database schema for import")
    Component(dbContext, "CliDbContext", "EF Core", "Maps projects, entities, rows, and relations")
    Component(builders, "Shared generation builders", "Mma.Cli.Shared", "Creates and updates source code")
    Component(dotnetRunner, "Migration runner", "CliWrap / dotnet ef", "Creates EF migrations for the generated solution")
  }

  Rel(developer, pages, "Uses", "Browser / HTTPS")
  Rel(pages, cliData, "Loads, saves, and applies workspace changes")
  Rel(pages, databaseEngine, "Requests database schema import")
  Rel(cliData, dbContext, "Reads and writes workspace model")
  Rel(dbContext, uiDb, "Persists state", "EF Core / SQLite")
  Rel(cliData, builders, "Migrates model changes into source")
  Rel(builders, solution, "Writes generated code", "File system")
  Rel(pages, dotnetRunner, "Requests migration generation")
  Rel(dotnetRunner, solution, "Runs EF tooling in project", "dotnet ef")
  Rel(databaseEngine, sqlServer, "Reads tables and columns", "SQL Server protocol")
```

## Local state model

`CliDbContext` stores `ProjectModel`, `EntityModel`, `EntityRowModel`, and `RelationModel`. Its relationship configuration cascades deletes from projects to their entities and from entities to fields and relations.
