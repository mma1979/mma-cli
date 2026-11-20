# Dynamic diagram: solution generation

This is the observed `mma new <SolutionName> --mapper <automapper|mapster>` flow. It describes file operations only; no network service is required.

```mermaid
C4Dynamic
  title Dynamic diagram for MMA CLI solution generation

  Person(developer, "Application developer", "Runs mma new")
  Container(cli, "MMA command-line tool", ".NET 10", "Routes the new command")
  Component(solutionBuilder, "SolutionBuilder", "C#", "Scaffolds and renames a starter solution")
  Component(resources, "Embedded solution archive", ".NET resources", "AutoMapper or Mapster starter solution")
  Container_Ext(solution, "Generated API solution", "File system", "New solution root and projects")

  Rel(developer, cli, "1. Runs `mma new`", "Terminal")
  Rel(cli, solutionBuilder, "2. Creates builder with solution name and mapper")
  Rel(solutionBuilder, solution, "3. Creates solution directory", "File system")
  Rel(solutionBuilder, resources, "4. Selects mapper-specific archive")
  Rel(solutionBuilder, solution, "5. Extracts archive", "ZIP / File system")
  Rel(solutionBuilder, solution, "6. Renames solution, projects, files, and namespaces", "File system")
  Rel(solutionBuilder, solution, "7. Creates .mma/project.mma", "JSON / File system")
  Rel(cli, developer, "8. Reports success", "Terminal")
```

The builder writes a `.mma` directory containing project metadata. Subsequent UI and import workflows use that state.
