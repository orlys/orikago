package main

import (
	"flag"
	"fmt"
	"go/ast"
	"go/parser"
	"go/scanner"
	"go/token"
	"io"
	"os"
	"reflect"
)

// position is the pos/end payload of a JSON AST node: a 1-based line, a
// 1-based column counted in UTF-16 code units (see columns.go), and the
// byte offset from the start of the file.
type position struct {
	Line   int `json:"line"`
	Column int `json:"col"`
	Offset int `json:"offset"`
}

// jsonNode mirrors one go/ast node in the wire format.
type jsonNode struct {
	Kind     string      `json:"kind"`
	Pos      position    `json:"pos"`
	End      position    `json:"end"`
	Text     string      `json:"text,omitempty"`
	Children []*jsonNode `json:"children"`
}

// parseError is one syntax error reported by the parse command.
type parseError struct {
	Line    int    `json:"line"`
	Column  int    `json:"col"`
	Message string `json:"msg"`
}

// parseResult is the wrapper object printed by the parse command.
type parseResult struct {
	AST    *jsonNode    `json:"ast"`
	Errors []parseError `json:"errors"`
}

// parseCommand implements "orikagoc parse". Input is one of: a file path, "-"
// for stdin, or --expr "<src>" for a single expression. Parse errors are
// data: they land in the "errors" array and the exit code stays 0.
func parseCommand(args []string) int {
	// Parse the flags; --expr is detected by presence, not by value.
	fs := newFlagSet("parse")
	expr := fs.String("expr", "",
		"parse `source` as a single expression instead of a file")
	pathFlag := fs.String("path", "",
		"file `name` reported in positions for stdin or --expr input")
	pretty := fs.Bool("pretty", false, "indent the JSON output")
	rest, code := parseArguments(fs, args, 1)
	if code >= 0 {
		return code
	}
	exprSet := false
	fs.Visit(func(f *flag.Flag) {
		if f.Name == "expr" {
			exprSet = true
		}
	})

	// Parse the selected input: an expression, stdin, or a file.
	fset := token.NewFileSet()
	mode := parser.ParseComments | parser.AllErrors | parser.SkipObjectResolution
	// Positions leave the sidecar with UTF-16 columns; the index holds the
	// bytes needed to convert them (registered explicitly for input that
	// never hits the disk, read on demand for a file argument).
	lines := newLineIndex()

	var (
		root ast.Node
		err  error
	)
	switch {
	case exprSet:
		if len(rest) > 0 {
			fmt.Fprintln(os.Stderr, "orikagoc: parse --expr does not take a file argument")
			return 2
		}
		name := *pathFlag
		if name == "" {
			name = "<expr>"
		}
		lines.addSource(name, []byte(*expr))
		var e ast.Expr
		e, err = parser.ParseExprFrom(fset, name, []byte(*expr), parser.AllErrors)
		if e != nil {
			root = e
		}
	case len(rest) == 1 && rest[0] == "-":
		src, readErr := io.ReadAll(os.Stdin)
		if readErr != nil {
			return infrastructureFailure(readErr)
		}
		name := *pathFlag
		if name == "" {
			name = "<source>"
		}
		lines.addSource(name, src)
		var f *ast.File
		f, err = parser.ParseFile(fset, name, src, mode)
		if f != nil {
			root = f
		}
	case len(rest) == 1:
		var f *ast.File
		f, err = parser.ParseFile(fset, rest[0], nil, mode)
		if f != nil {
			root = f
		}
	default:
		fmt.Fprintln(os.Stderr,
			`usage: orikagoc parse <file.go> | orikagoc parse - | orikagoc parse --expr "<src>"`)
		return 2
	}

	// Syntax errors become data; anything else is an infrastructure failure.
	result := parseResult{Errors: []parseError{}}
	if err != nil {
		list, ok := err.(scanner.ErrorList)
		if !ok {
			// Not a syntax problem: unreadable file and the like.
			return infrastructureFailure(err)
		}
		for _, e := range list {
			result.Errors = append(result.Errors, parseError{
				Line:    e.Pos.Line,
				Column:  lines.toUTF16Column(e.Pos.Filename, e.Pos.Line, e.Pos.Column),
				Message: e.Msg,
			})
		}
	}
	if root != nil {
		// A tree was produced (possibly partial): convert it for the output.
		result.AST = buildNode(fset, lines, root)
	}
	return emit(result, *pretty)
}

// buildNode converts an ast.Node and, recursively, its children into the
// JSON shape of the protocol.
func buildNode(fset *token.FileSet, lines *lineIndex, n ast.Node) *jsonNode {
	node := &jsonNode{
		Kind:     nodeKind(n),
		Pos:      toPosition(fset, lines, n.Pos()),
		End:      toPosition(fset, lines, n.End()),
		Children: []*jsonNode{},
	}
	switch v := n.(type) {
	case *ast.Ident:
		node.Text = v.Name
	case *ast.BasicLit:
		node.Text = v.Value
	}
	for _, c := range directChildren(n) {
		node.Children = append(node.Children, buildNode(fset, lines, c))
	}
	return node
}

func toPosition(fset *token.FileSet, lines *lineIndex, p token.Pos) position {
	pos := fset.Position(p)
	return position{
		Line:   pos.Line,
		Column: lines.toUTF16Column(pos.Filename, pos.Line, pos.Column),
		Offset: pos.Offset,
	}
}

// nodeKind names a node after its go/ast type: *ast.FuncDecl -> "FuncDecl".
func nodeKind(n ast.Node) string {
	t := reflect.TypeOf(n)
	for t != nil && t.Kind() == reflect.Pointer {
		t = t.Elem()
	}
	if t == nil {
		return "Unknown"
	}
	return t.Name()
}

// directChildren returns the immediate children of n in source order by
// letting ast.Inspect descend exactly one level.
func directChildren(n ast.Node) []ast.Node {
	var out []ast.Node
	self := true
	ast.Inspect(n, func(c ast.Node) bool {
		if c == nil {
			return false
		}
		if self {
			// Descend into n itself.
			self = false
			return true
		}
		// Direct child recorded; recursion happens in buildNode.
		out = append(out, c)
		return false
	})
	return out
}
