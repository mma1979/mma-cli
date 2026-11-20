# MMA CLI architecture

This documentation describes the architecture currently implemented in this repository. It uses C4 diagrams in Mermaid syntax and distinguishes deployable containers from shared code libraries.

## Document set

| Document | C4 level | Focus |
| --- | --- | --- |
| [System context](c4-context.md) | Level 1 | People, MMA CLI, generated solutions, and optional generator API |
| [Containers](c4-containers.md) | Level 2 | CLI tool, embedded Blazor UI, local data, templates, and generated output |
| [CLI components](c4-components-cli.md) | Level 3 | Command routing and generation workflow |
| [Shared generation components](c4-components-generation.md) | Level 3 | Builders, templates, configuration, and import integration |
| [UI components](c4-components-ui.md) | Level 3 | Blazor workspace, application service, persistence, and database import |
| [Solution-generation flow](c4-dynamic-solution-generation.md) | Dynamic | `mma new` workflow |
| [Entity-generation flow](c4-dynamic-entity-generation.md) | Dynamic | `mma generate entity` workflow |
| [Deployment](c4-deployment.md) | Level 4 | Observed local execution and packaged UI deployment |

## Scope and conventions

MMA CLI is a developer-operated .NET global tool. It creates and changes source code in projects on the user's machine; generated solutions are outputs, not runtime containers of MMA CLI. The repository does not define a production hosting environment for the UI or a hosted generator endpoint, so the deployment diagram models only the local topology that the code demonstrates.

The optional remote **Generator API** is based on the configurable `GeneratorEndpoint` used by the `import` command. Its internals are outside this repository and are intentionally not modeled.
