# Deployment diagram

This diagram shows the deployment topology supported directly by the repository: a .NET global tool executing on a developer workstation and launching a packaged local UI. No production deployment definition is present in this codebase.

```mermaid
C4Deployment
  title Deployment diagram for MMA CLI local execution

  Deployment_Node(workstation, "Developer workstation", "Windows, macOS, or Linux", "Developer-operated local environment") {
    Deployment_Node(dotnet, ".NET runtime", ".NET 10", "Executes MMA CLI and the UI") {
      Container(cli, "mma-cli global tool", ".NET console executable", "Installed as the `mma` command")
      Container(ui, "cli-ui", "ASP.NET Core Blazor Server", "Published UI launched by `mma ui` on localhost:5000")
    }
    Deployment_Node(filesystem, "Local file system", "OS file system", "Holds generated solutions, templates, and metadata") {
      Container(solution, "Generated API solution", "ASP.NET Core source tree", "Generated or updated customer solution")
      ContainerDb(uiDb, "MMA UI workspace database", "SQLite file", "wwwroot/data/mma_cli.db")
    }
  }

  Deployment_Node(external, "External services", "Optional", "Configured or selected by the developer") {
    Container_Ext(generatorApi, "Generator API", "HTTPS endpoint", "Receives project metadata during import")
    Container_Ext(sqlServer, "SQL Server", "Microsoft SQL Server", "Source database for UI schema import")
  }

  Rel(cli, ui, "Launches", "dotnet cli-ui.dll")
  Rel(cli, solution, "Creates and modifies", "File system")
  Rel(ui, uiDb, "Persists workspace", "EF Core / SQLite")
  Rel(ui, solution, "Generates code and runs migrations", "File system / dotnet")
  Rel(cli, generatorApi, "Exports metadata", "JSON/HTTPS")
  Rel(ui, sqlServer, "Reads schema", "SQL Server protocol")
```

## Packaging and operational notes

- `Mma.Cli.AppV4` is packaged as a .NET tool with command name `mma`.
- The UI project is published into the CLI distribution's `UI` directory and executed as `dotnet cli-ui.dll`.
- The UI persists its own workspace database locally; generated projects retain independent `.mma` metadata in their own root directories.
- Hosting the UI remotely, deploying the Generator API, authentication, TLS termination, observability, backups, and network policy are not specified by this repository.
