# To do

Open items left after the 0.2.1-preview release (2026-11-27).

- [x] Install the 0.2.1 VSIX in Visual Studio and open a `.goproj`. Checked over DTE in
      VS 18.11.3 with a `go-console` project from nuget.org: the package loads, the four Go
      commands are in the command table, the build succeeds, gopls publishes diagnostics and
      F5 stops at a breakpoint through delve. Nothing was looked at on screen: icons,
      colouring and squiggles are unchecked.
- [ ] The file watchers in `GoLanguageClient` forward gopls's own temporary files when the
      temp directory is under the solution root. gopls then reloads, writes another
      `gopls-tempmod*/go.mod` and asks to run govulncheck again, about once a second. Seen
      only with TEMP inside the solution folder; skipping `Path.GetTempPath()` in
      `IsExcludedPath` would stop it.
