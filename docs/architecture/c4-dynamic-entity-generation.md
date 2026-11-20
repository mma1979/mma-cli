# Dynamic diagram: entity generation

This diagram covers the `mma generate entity` flow; interactive mode and the UI invoke the same underlying `EntityBuilder` sequence.

```mermaid
C4Dynamic
  title Dynamic diagram for MMA CLI entity generation

  Person(developer, "Application developer", "Requests a new entity")
  Container(cli, "MMA command-line tool", ".NET 10", "Validates and routes generate command")
  Component(entityBuilder, "EntityBuilder", "C#", "Generates entity-related source and updates mappings")
  Component(buildHelper, "BuildHelper", "C#", "Finds solution and mapper configuration")
  Component(templateHelper, "TemplateHelper", "C#", "Resolves default or overridden templates")
  Container_Ext(solution, "Generated API solution", "File system", "Target API source projects")

  Rel(developer, cli, "1. Runs `mma generate entity`", "Terminal")
  Rel(cli, entityBuilder, "2. Supplies entity name, key type, mapper, and API option")
  Rel(entityBuilder, buildHelper, "3. Finds solution layout and projects")
  Rel(entityBuilder, templateHelper, "4. Resolves model and source templates")
  Rel(templateHelper, solution, "5. Reads optional .mma template overrides", "File system")
  Rel(entityBuilder, solution, "6. Writes models and validator", "File system")
  Rel(entityBuilder, solution, "7. Writes entity and EF configuration", "File system")
  Rel(entityBuilder, solution, "8. Updates DbSet and configuration registration", "File system")
  Rel(entityBuilder, solution, "9. Writes service and optional controller", "File system")
  Rel(cli, developer, "10. Reports success", "Terminal")
```

For AutoMapper output, entity generation also updates the mapping profile. The `--no-api` option omits controller generation.
