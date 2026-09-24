# Orikago — Go for Visual Studio

> **Preview / experimental.** Orikago leans on Visual Studio internals that carry no compatibility promise; a VS update can break it. Not affiliated with Microsoft or the Go team.

![Visual Studio editing a Go project with gopls diagnostics, a breakpoint, Go-specific Dependencies commands and the Orikago output pane](https://raw.githubusercontent.com/orlys/orikago/main/img/1.png)

Open, edit, build and debug Go projects in Visual Studio the way you would a C# project.

## Why Orikago

Go tooling lives in VS Code and GoLand; neither can open a Visual Studio solution. Orikago is
for teams that already live in Visual Studio: a `.goproj` sits in the same `.slnx` as your
`.csproj` files, builds with the same `dotnet build` / Build Solution, and debugs with the same F5.
One solution, one IDE, one CI command for both languages.

## Features

- **`.goproj` projects** — Go modules load in Solution Explorer and build, run and test through MSBuild, driven by the [Orikago.Sdk](https://www.nuget.org/packages/Orikago.Sdk) project SDK.
- **IntelliSense via gopls** — completion, hover, signature help, go to definition, find references, rename, formatting and live diagnostics.
- **F5 debugging via delve** — breakpoints, stepping, locals, call stacks and goroutines; an unrecovered panic breaks at the panic site.
- **Go-aware Dependencies node** — *Add Go Module Reference…* and *Tidy Go Modules* instead of NuGet commands.
- **Go diagnostics in the Error List** — compiler, `go vet` and test failures appear with file and line; double-click to jump.
- **Project templates** — *Orikago Console App* and *Orikago Class Library* in the New Project dialog.

## Requirements

- Visual Studio 2022 17.14 or Visual Studio 2026
- [Go](https://go.dev/dl/) on `PATH`
- gopls: `go install golang.org/x/tools/gopls@latest`
- delve (for debugging): `go install github.com/go-delve/delve/cmd/dlv@latest`

## Getting started

```powershell
dotnet new install Orikago.Templates::0.1.0-preview
dotnet new go-console -n Hello
```

Open the folder's `.goproj` (or add it to a solution) and press **F5**.
A minimal project file:

```xml
<Project Sdk="Orikago.Sdk/0.1.0-preview">
  <PropertyGroup>
    <LangVersion>1.21</LangVersion>
  </PropertyGroup>
</Project>
```

## Links

- Source, documentation and issues: <https://github.com/orlys/orikago>
- License: MIT
