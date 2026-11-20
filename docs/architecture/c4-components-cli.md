# CLI component diagram

This diagram shows the internal components of the `Mma.Cli.AppV4` executable. It concentrates on command dispatch and user-facing execution modes.

```mermaid
C4Component
  title Component diagram for the MMA CLI executable

  Person(developer, "Application developer", "Runs MMA commands")
  Container_Ext(ui, "MMA UI", "ASP.NET Core Blazor Server", "Packaged local web UI")
  Container_Ext(solution, "Generated API solution", "Source tree", "Target solution being scaffolded or modified")

  Container_Boundary(cli, "MMA command-line tool") {
    Component(program, "Program command router", "C# / .NET", "Routes new, generate, ui, import, help, and version commands")
    Component(interactive, "Interactive workflow", "Sharprompt", "Collects generation choices when no arguments are supplied")
    Component(watcher, "Project watcher", "FileSystemWatcher", "Watches .mma/project.mma and triggers import")
    Component(output, "Console output", "C#", "Formats success, warning, and error messages")
    Component(builders, "Shared generation builders", "Mma.Cli.Shared", "Creates and changes solution source files")
    Component(importer, "Import factory", "Mma.Cli.Shared / RestSharp", "Exports project metadata to the configured endpoint")
  }

  Rel(developer, program, "Invokes", "CLI arguments")
  Rel(program, interactive, "Starts when no command is supplied")
  Rel(program, builders, "Invokes for new and generate commands")
  Rel(interactive, builders, "Invokes after collecting options")
  Rel(program, ui, "Starts local UI process", "dotnet cli-ui.dll")
  Rel(program, importer, "Invokes for import command")
  Rel(program, watcher, "Starts from interactive watch mode")
  Rel(watcher, importer, "Re-imports on metadata change")
  Rel(builders, solution, "Writes generated source", "File system")
  Rel(program, output, "Reports command outcome")
  Rel(interactive, output, "Reports equivalent command and outcome")
```

## Command routing

`Program` is the composition root. It calls `SolutionBuilder` for new solutions; `EntityBuilder`, `PropertiesBuilder`, and `RelationsBuilder` for incremental updates; `ImportFactory` for export; and `CliWrap` for launching the packaged UI.
