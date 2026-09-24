package main

import (
	"errors"
	"flag"
	"fmt"
	"go/token"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strconv"
	"strings"

	"golang.org/x/tools/go/packages"
)

// diagnostic is one entry of the "check" command output.
type diagnostic struct {
	File     string `json:"file"`
	Line     int    `json:"line"`
	Column   int    `json:"col"`
	Severity string `json:"severity"`
	Message  string `json:"msg"`
}

// loadMode is the go/packages mode used by every command that needs
// types. It is deliberately the full source-based mode: NeedDeps plus
// NeedSyntax makes the loader type-check dependencies from source, which
// is what allows checking for a GOOS/GOARCH that has no prebuilt export
// data on this machine.
const loadMode = packages.NeedTypes |
	packages.NeedSyntax |
	packages.NeedTypesInfo |
	packages.NeedName |
	packages.NeedFiles |
	packages.NeedCompiledGoFiles |
	packages.NeedDeps |
	packages.NeedImports

// buildOptions carries the build-context selectors that decide *which*
// files are part of a package: build tags and the target platform. They
// mirror the flags `go build` accepts so that what is type-checked is the
// same file set that will later be emitted.
type buildOptions struct {
	// Comma-separated, as passed to -tags.
	Tags   string
	GOOS   string
	GOARCH string
}

// registerBuildFlags wires -tags/-goos/-goarch onto fs.
func (o *buildOptions) registerBuildFlags(fs *flag.FlagSet) {
	fs.StringVar(&o.Tags, "tags", "",
		"comma-separated build `tags` (as in go build -tags)")
	fs.StringVar(&o.GOOS, "goos", "",
		"target `operating system` (GOOS); empty means the host default")
	fs.StringVar(&o.GOARCH, "goarch", "",
		"target `architecture` (GOARCH); empty means the host default")
}

// appendGoflags returns the value GOFLAGS should have once flag has been
// added to the inherited value.
//
// GOFLAGS is a space-separated list of flags the go command applies to
// every invocation, and it is the only channel through which -tags can
// reach the `go list` subprocess go/packages runs (go/packages exposes no
// argv for it). Replacing GOFLAGS outright would therefore silently drop
// whatever the caller's environment already asked for — -mod=vendor,
// -mod=mod, -trimpath, ... — and make the analyzed file set diverge from
// the one `go build` compiles, because the build inherits GOFLAGS intact
// and passes -tags on its own command line.
//
// Appending is also the correct way to *override* a flag that is already
// present: the go command parses GOFLAGS left to right into one flag set,
// so a repeated flag keeps its last occurrence. Verified with go1.21.5:
//
//	GOFLAGS="-tags=taga -tags=tagb" go list -f '{{.GoFiles}}'  ->  [b_tagb.go main.go]
//	GOFLAGS="-tags=tagb -tags=taga" go list -f '{{.GoFiles}}'  ->  [a_taga.go main.go]
//
// so an explicit -tags wins over an inherited one while every other
// inherited flag survives.
func appendGoflags(inherited, flag string) string {
	inherited = strings.TrimSpace(inherited)
	if inherited == "" {
		return flag
	}
	return inherited + " " + flag
}

// environment builds the environment for the `go list` subprocess
// go/packages runs: the current environment plus the requested overrides.
// Later entries win, so the overrides shadow any inherited value.
func (o buildOptions) environment() []string {
	env := os.Environ()
	if o.GOOS != "" {
		env = append(env, "GOOS="+o.GOOS)
	}
	if o.GOARCH != "" {
		env = append(env, "GOARCH="+o.GOARCH)
	}
	if o.Tags != "" {
		env = append(env, "GOFLAGS="+appendGoflags(os.Getenv("GOFLAGS"), "-tags="+o.Tags))
	}
	return env
}

// configuration returns the go/packages configuration for a load rooted
// at directory.
func (o buildOptions) configuration(
	directory string,
	fset *token.FileSet,
	tests bool,
) *packages.Config {
	return &packages.Config{
		Mode:  loadMode,
		Dir:   directory,
		Env:   o.environment(),
		Fset:  fset,
		Tests: tests,
	}
}

// loader type-checks Go packages through golang.org/x/tools/go/packages,
// which drives the real `go list`. Going through the go command is what
// makes module-aware resolution work: external dependencies declared in
// go.mod, the module cache, workspaces (go.work), vendor directories and
// build tags are all handled by the toolchain itself rather than being
// re-implemented here.
type loader struct {
	// Absolute directory the load is rooted at.
	directory string
	options   buildOptions
	fset      *token.FileSet
	lines     *lineIndex

	diagnostics     []diagnostic
	seenDiagnostics map[string]bool
}

// newLoader creates a loader rooted at directory.
func newLoader(directory string, options buildOptions) (*loader, error) {
	abs, err := filepath.Abs(directory)
	if err != nil {
		return nil, fmt.Errorf("resolving %s: %w", directory, err)
	}
	return &loader{
		directory:       abs,
		options:         options,
		fset:            token.NewFileSet(),
		lines:           newLineIndex(),
		seenDiagnostics: map[string]bool{},
	}, nil
}

// findModuleRoot walks up from dir looking for a go.mod; "" if none found.
func findModuleRoot(dir string) string {
	dir = filepath.Clean(dir)
	for {
		if fi, err := os.Stat(filepath.Join(dir, "go.mod")); err == nil && !fi.IsDir() {
			return dir
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			return ""
		}
		dir = parent
	}
}

// samePath compares two file paths, case-insensitively on Windows.
func samePath(a, b string) bool {
	a, b = filepath.Clean(a), filepath.Clean(b)
	if runtime.GOOS == "windows" {
		return strings.EqualFold(a, b)
	}
	return a == b
}

// resolvePath makes file absolute. Relative positions (go list reports
// ListError positions relative to cfg.Dir) are resolved against the
// loader's directory, NOT the process working directory: the sidecar is
// spawned by the C# host with whatever cwd Visual Studio happens to have,
// so filepath.Abs would fabricate paths to nonexistent files.
func (l *loader) resolvePath(file string) string {
	if file == "" || filepath.IsAbs(file) {
		return filepath.Clean(file)
	}
	return filepath.Join(l.directory, file)
}

// inModule reports whether file (already resolved) lives under the
// loader's directory.
func (l *loader) inModule(file string) bool {
	rel, err := filepath.Rel(l.directory, file)
	if err != nil {
		return false
	}
	return rel != ".." && !strings.HasPrefix(rel, ".."+string(filepath.Separator))
}

// addDiagnostic records a diagnostic. line/byteColumn arrive as go/token
// reports them (1-based, column counted in bytes) and are stored in the
// protocol's unit: 1-based UTF-16 code units.
func (l *loader) addDiagnostic(file string, line, byteColumn int, message string) {
	file = l.resolvePath(file)
	column := byteColumn
	if column > 0 {
		column = l.lines.toUTF16Column(file, line, byteColumn)
	}
	key := fmt.Sprintf("%s\x00%d\x00%d\x00%s", file, line, column, message)
	if l.seenDiagnostics[key] {
		// The same diagnostic was already recorded; keep only one.
		return
	}
	l.seenDiagnostics[key] = true
	l.diagnostics = append(l.diagnostics, diagnostic{
		File:     file,
		Line:     line,
		Column:   column,
		Severity: "error",
		Message:  message,
	})
}

// parsePackagesPosition splits the Pos of a packages.Error, which is a
// formatted token.Position ("file:line:col", "file:line", or empty); on
// Windows the file part itself contains a colon, so the position is
// peeled off from the right.
func parsePackagesPosition(pos string) (file string, line, column int) {
	pos = strings.TrimSpace(pos)
	if pos == "" || pos == "-" {
		return "", 0, 0
	}
	file = pos
	i := strings.LastIndex(file, ":")
	if i < 0 {
		return file, 0, 0
	}
	n, err := strconv.Atoi(file[i+1:])
	if err != nil {
		return file, 0, 0
	}
	rest := file[:i]
	j := strings.LastIndex(rest, ":")
	if j >= 0 {
		if columnNumber, columnErr := strconv.Atoi(rest[j+1:]); columnErr == nil {
			return rest[:j], columnNumber, n
		}
	}
	// Only one numeric component: it was the line, no column.
	return rest, n, 0
}

// checkModule type-checks every package of the module rooted at the
// loader's directory and returns the accumulated diagnostics, sorted by
// position. A returned error is an infrastructure failure (the go
// toolchain could not be run at all); source problems are diagnostics.
func (l *loader) checkModule() ([]diagnostic, error) {
	cfg := l.options.configuration(l.directory, l.fset, false)
	pkgs, err := packages.Load(cfg, "./...")
	if err != nil {
		// Infrastructure error only when the toolchain could not run at
		// all (missing go binary) or the arguments were bad (no such
		// directory). Everything else - typically a malformed go.mod that
		// makes `go list` exit before listing - is bad SOURCE, which the
		// contract says is data: report it as a diagnostic against go.mod
		// and exit 0, instead of blowing up the C# host mid-edit.
		var execErr *exec.Error
		if _, statErr := os.Stat(l.directory); statErr != nil || errors.As(err, &execErr) {
			return nil, fmt.Errorf("loading packages in %s: %w", l.directory, err)
		}
		line := 1
		if m := goModLinePattern.FindStringSubmatch(err.Error()); m != nil {
			if n, convErr := strconv.Atoi(m[1]); convErr == nil {
				line = n
			}
		}
		goMod := filepath.Join(l.directory, "go.mod")
		l.addDiagnostic(goMod, line, 0, strings.TrimSpace(err.Error()))
		return l.sortedDiagnostics(), nil
	}

	// Top-level packages report all their errors. Dependency packages are
	// visited too - go list attaches the actionable "no required module
	// provides package X; to add it: go get X" ListError (positioned at the
	// import site) to the dependency's STUB package, not to the importer -
	// but only their errors positioned inside this module are taken, so a
	// broken third-party dependency does not flood the Error List with
	// positions the user cannot act on.
	topLevel := map[*packages.Package]bool{}
	for _, pkg := range pkgs {
		topLevel[pkg] = true
	}
	packages.Visit(pkgs, nil, func(pkg *packages.Package) {
		for _, e := range pkg.Errors {
			file, line, column := parsePackagesPosition(e.Pos)
			if !topLevel[pkg] {
				if file == "" || !l.inModule(l.resolvePath(file)) {
					// A dependency error outside this module: not actionable here.
					continue
				}
			}
			l.addDiagnostic(file, line, column, e.Msg)
		}
	})

	return l.sortedDiagnostics(), nil
}

// goModLinePattern pulls the line number out of `go list`'s "go.mod:5:
// unknown directive" style messages so the diagnostic lands on the
// offending line.
var goModLinePattern = regexp.MustCompile(`go\.mod:(\d+)`)

// sortedDiagnostics returns the accumulated diagnostics in stable
// position order.
func (l *loader) sortedDiagnostics() []diagnostic {
	sort.SliceStable(l.diagnostics, func(i, j int) bool {
		a, b := l.diagnostics[i], l.diagnostics[j]
		if a.File != b.File {
			return a.File < b.File
		}
		if a.Line != b.Line {
			return a.Line < b.Line
		}
		if a.Column != b.Column {
			return a.Column < b.Column
		}
		return a.Message < b.Message
	})
	if l.diagnostics == nil {
		// Nothing was reported: marshal an empty array, not null.
		l.diagnostics = []diagnostic{}
	}
	return l.diagnostics
}
