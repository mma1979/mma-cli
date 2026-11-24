# SchemaForge Client (React + TypeScript + Vite)

This directory contains the local-first SchemaForge frontend. The canonical API host is the ASP.NET Core .NET 10 project in `../server/Mma.Cli.Bff.csproj` (see [../README.md](../README.md) for the full workflow).

## Prerequisites

- Node.js 20.19+ or 22.12+ and npm
- .NET 10 runtime/SDK to run the local BFF host (`../server/Mma.Cli.Bff.csproj`)

## Development

Start the .NET host on `http://localhost:5080`, then run Vite:

```powershell
npm ci
npm run dev
```

Vite serves the UI on `http://localhost:5173` and proxies `/api` requests to `http://localhost:5080`.

## Production Build

```powershell
npm run lint
npm run build
```

`npm run build` outputs the compiled bundle into `../server/wwwroot`, which ASP.NET Core serves directly at `http://localhost:5080`.

## Local / Firewalled Databases

Live database introspection blocks local and private network destinations by design. To import a local or firewalled database, use the **SQL Dump / DDL Script** tab in the Import modal.
