package main

import (
	"fmt"
	"go/ast"
	"go/importer"
	"go/parser"
	"go/token"
	"go/types"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"golang.org/x/tools/go/packages"
)

// declaredAt is the declaration position reported by the symbol command.
type declaredAt struct {
	File   string `json:"file"`
	Line   int    `json:"line"`
	Column int    `json:"col"`
}

// symbolResult is the object printed by the symbol command. A nil Name
// marshals to {"name":null}: no symbol at the requested position.
type symbolResult struct {
	Name       *string     `json:"name"`
	Kind       string      `json:"kind,omitempty"`
	Type       string      `json:"type,omitempty"`
	DeclaredAt *declaredAt `json:"declaredAt,omitempty"`
}

// symbolCommand implements "orikagoc symbol <file.go> <line> <col>". The
// column is 1-based and counted in UTF-16 code units (see columns.go).
// The module directory is taken from -dir or inferred from the nearest
// go.mod above the file.
func symbolCommand(args []string) int {
	// Parse the flags and the three positional arguments.
	fs := newFlagSet("symbol")
	directoryFlag := fs.String("dir", "",
		"module `directory` the file belongs to (default: inferred from go.mod)")
	pretty := fs.Bool("pretty", false, "indent the JSON output")
	var options buildOptions
	options.registerBuildFlags(fs)
	rest, code := parseArguments(fs, args, 3)
	if code >= 0 {
		return code
	}
	if len(rest) != 3 {
		fmt.Fprintln(os.Stderr, "usage: orikagoc symbol <file.go> <line> <col> "+
			"[-dir <moduleDir>] [-tags <list>] [-goos <os>] [-goarch <arch>]")
		return 2
	}

	// Validate the file and the 1-based position.
	file, err := filepath.Abs(rest[0])
	if err != nil {
		return infrastructureFailure(err)
	}
	line, lineErr := strconv.Atoi(rest[1])
	column, columnErr := strconv.Atoi(rest[2])
	if lineErr != nil || columnErr != nil || line < 1 || column < 1 {
		fmt.Fprintln(os.Stderr, "orikagoc: line and col must be positive integers (1-based)")
		return 2
	}
	if _, err := os.Stat(file); err != nil {
		return infrastructureFailure(err)
	}

	// Resolve the module directory: -dir, else the nearest go.mod, else the
	// file's own directory.
	moduleDirectory := *directoryFlag
	if moduleDirectory == "" {
		moduleDirectory = findModuleRoot(filepath.Dir(file))
	}
	if moduleDirectory == "" {
		moduleDirectory = filepath.Dir(file)
	}

	// Look the symbol up and print it.
	packageLoader, err := newLoader(moduleDirectory, options)
	if err != nil {
		return infrastructureFailure(err)
	}
	result, err := packageLoader.symbolAt(file, line, column)
	if err != nil {
		return infrastructureFailure(err)
	}
	return emit(result, *pretty)
}

// symbolAt loads the package containing file and reports the symbol used
// or declared at the given 1-based position, whose column is in UTF-16
// code units. Errors in the source are tolerated: the lookup works off
// whatever go/types could compute.
func (l *loader) symbolAt(file string, line, utf16Column int) (symbolResult, error) {
	// go/token columns are byte offsets within the line; the protocol
	// speaks UTF-16 code units, so translate before matching positions.
	byteColumn := l.lines.toByteColumn(file, line, utf16Column)

	target, info := l.loadFileForSymbol(file)
	if target == nil {
		// The file is excluded by build constraints for the requested
		// GOOS/GOARCH/tags, or is not part of any loadable package.
		var err error
		target, info, err = l.standaloneFile(file)
		if err != nil {
			return symbolResult{}, err
		}
	}

	// Find the identifier and the object it defines or uses.
	id := identifierAt(l.fset, target, line, byteColumn)
	if id == nil {
		return symbolResult{}, nil
	}
	obj := info.Defs[id]
	if obj == nil {
		obj = info.Uses[id]
	}
	if obj == nil {
		return symbolResult{}, nil
	}

	// Describe the object and where it is declared.
	name := obj.Name()
	result := symbolResult{Name: &name, Kind: objectKind(obj), Type: objectType(obj)}
	if p := l.fset.Position(obj.Pos()); p.IsValid() {
		declarationFile := p.Filename
		if abs, err := filepath.Abs(declarationFile); err == nil {
			declarationFile = abs
		}
		result.DeclaredAt = &declaredAt{
			File:   declarationFile,
			Line:   p.Line,
			Column: l.lines.toUTF16Column(declarationFile, p.Line, p.Column),
		}
	}
	return result, nil
}

// loadFileForSymbol loads the package in the file's own directory (test
// variants included when the file is a _test.go) and returns the syntax
// tree of that file together with the package's type information. It
// returns nil when the file is not part of the loaded package — a load
// failure here is not fatal, the caller falls back to a standalone parse.
func (l *loader) loadFileForSymbol(file string) (*ast.File, *types.Info) {
	dir := filepath.Dir(file)
	isTest := strings.HasSuffix(strings.ToLower(filepath.Base(file)), "_test.go")
	cfg := l.options.configuration(dir, l.fset, isTest)
	pkgs, err := packages.Load(cfg, ".")
	if err != nil {
		return nil, nil
	}
	for _, pkg := range pkgs {
		if pkg.TypesInfo == nil {
			continue
		}
		for _, f := range pkg.Syntax {
			if samePath(l.fset.Position(f.Pos()).Filename, file) {
				return f, pkg.TypesInfo
			}
		}
	}
	return nil, nil
}

// standaloneFile is the fallback for a file the go command does not
// consider part of any package here (excluded by build constraints, or
// outside a module). It parses and type-checks that single file on its
// own; imports resolve through the stdlib source importer, and anything
// unresolvable simply yields less type information rather than an error.
func (l *loader) standaloneFile(file string) (*ast.File, *types.Info, error) {
	f, err := parser.ParseFile(l.fset, file, nil,
		parser.ParseComments|parser.AllErrors|parser.SkipObjectResolution)
	if f == nil {
		return nil, nil, fmt.Errorf("cannot parse %s: %w", file, err)
	}
	info := &types.Info{
		Defs: map[*ast.Ident]types.Object{},
		Uses: map[*ast.Ident]types.Object{},
	}
	conf := types.Config{
		Importer: importer.ForCompiler(l.fset, "source", nil),
		// Best effort: errors are not this command's job.
		Error:       func(error) {},
		FakeImportC: true,
	}
	pkgName := "main"
	if f.Name != nil {
		pkgName = f.Name.Name
	}
	_, _ = conf.Check(pkgName, l.fset, []*ast.File{f}, info)
	return f, info, nil
}

// identifierAt finds the identifier spanning the given 1-based line and
// 1-based *byte* column, if any.
func identifierAt(fset *token.FileSet, f *ast.File, line, byteColumn int) *ast.Ident {
	var found *ast.Ident
	ast.Inspect(f, func(n ast.Node) bool {
		if found != nil || n == nil {
			return false
		}
		id, ok := n.(*ast.Ident)
		if !ok {
			return true
		}
		p := fset.Position(id.Pos())
		e := fset.Position(id.End())
		if p.Line == line && byteColumn >= p.Column && byteColumn < e.Column {
			found = id
		}
		return false
	})
	return found
}

// objectKind names the kind of a go/types object as the protocol spells it.
func objectKind(obj types.Object) string {
	switch o := obj.(type) {
	case *types.PkgName:
		return "package"
	case *types.Const:
		return "const"
	case *types.TypeName:
		return "type"
	case *types.Var:
		if o.IsField() {
			return "field"
		}
		return "var"
	case *types.Func:
		return "func"
	case *types.Label:
		return "label"
	case *types.Builtin:
		return "builtin"
	case *types.Nil:
		return "nil"
	default:
		return "object"
	}
}

// objectType renders the type of a go/types object, or "" when it has
// no meaningful type.
func objectType(obj types.Object) string {
	t := obj.Type()
	if t == nil {
		return ""
	}
	if b, ok := t.(*types.Basic); ok && b.Kind() == types.Invalid {
		// Package names and builtins have no meaningful type.
		return ""
	}
	return t.String()
}
