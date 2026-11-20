# System context

MMA CLI lets an application developer scaffold an ASP.NET Core solution and evolve its domain model through terminal, interactive, or browser-based workflows.

```mermaid
C4Context
  title System Context diagram for MMA CLI

  Person(developer, "Application developer", "Creates and evolves ASP.NET Core API solutions")
  System(mmaCli, "MMA CLI", ".NET developer tool that scaffolds and updates API source code")
  System_Ext(generatedSolution, "Generated API solution", "ASP.NET Core solution source code created or modified by MMA CLI")
  System_Ext(generatorApi, "Generator API", "Optional remote endpoint that receives exported project metadata")
  System_Ext(sqlServer, "SQL Server instance", "Optional database inspected through the UI import workflow")

  Rel(developer, mmaCli, "Runs commands and uses the local web UI")
  Rel(mmaCli, generatedSolution, "Creates and modifies source files", "File system")
  Rel(mmaCli, generatorApi, "Exports .mma project metadata", "JSON/HTTPS")
  Rel(mmaCli, sqlServer, "Reads schema during database import", "SQL Server protocol")
```

## Relationships

- The developer uses `mma new`, `mma generate`, `mma import`, interactive prompts, or `mma ui`.
- Generated solutions contain the API projects and source files that the tool scaffolds; they are not part of MMA CLI's runtime.
- Project metadata is retained in the generated solution's `.mma` directory and can be exported to the externally hosted Generator API.
- The UI can inspect a SQL Server schema to create its local workspace model before applying generation changes.
