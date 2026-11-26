# Orikago.Templates

`dotnet new` templates for Go projects built with
[Orikago.Sdk](https://www.nuget.org/packages/Orikago.Sdk).

```shell
dotnet new install Orikago.Templates@0.2.1-preview
dotnet new go-console -n MyTool   # console app
dotnet new go-lib -n MyLib        # class library
```

`--langVersion` sets the `go` directive of `go.mod` (default 1.21). Visual Studio shows both
templates in the New Project dialog.

More in the [repository](https://github.com/orlys/orikago).
