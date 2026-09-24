// Command orikagoc is the Go analysis sidecar for the Orika compiler
// platform. It exposes go/parser, go/ast and go/types over a small JSON
// protocol consumed by the Orikago.CodeAnalysis C# library.
//
// Commands:
//
//	orikagoc parse <file.go>            parse a file to a JSON AST
//	orikagoc parse -                    same, reading source from stdin
//	orikagoc parse --expr "<src>"       parse a single expression
//	orikagoc check <moduleDir>          type-check a whole module
//	orikagoc symbol <file.go> <L> <C>   symbol info at a 1-based position
//
// Every command prints a single JSON document on stdout and exits 0 even
// when the analyzed source contains errors: bad source is data (returned
// as errors/diagnostics), not a tool failure. A nonzero exit code means an
// infrastructure error (unreadable input, bad arguments, ...), reported on
// stderr.
//
// check and symbol are module-aware: they load packages through
// golang.org/x/tools/go/packages, i.e. through the real `go list`, so
// external dependencies, go.work workspaces and vendor directories
// resolve exactly as they do for `go build`. Both accept -tags, -goos and
// -goarch so that the file set being analyzed can be made identical to
// the one a given build would compile.
//
// All columns in the protocol — both those reported and the one accepted
// by symbol — are 1-based and counted in UTF-16 code units, matching what
// .NET and Visual Studio use. (go/token itself counts bytes; the
// conversion happens at this boundary. Byte offsets stay byte offsets.)
package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
)

// main runs one subcommand and exits.
//
// Rule 090 exception: one-shot CLI invocation, not a long-running service.
func main() {
	os.Exit(run(os.Args[1:]))
}

// run dispatches to a subcommand. It never lets a panic escape: analyzing
// arbitrary (possibly broken) Go source must at worst produce diagnostics,
// and a genuine internal error becomes a nonzero exit, not a crash dump.
func run(args []string) (code int) {
	defer func() {
		if r := recover(); r != nil {
			fmt.Fprintf(os.Stderr, "orikagoc: internal error: %v\n", r)
			code = 1
		}
	}()
	if len(args) == 0 {
		usage(os.Stderr)
		return 2
	}
	switch args[0] {
	case "parse":
		return parseCommand(args[1:])
	case "check":
		return checkCommand(args[1:])
	case "symbol":
		return symbolCommand(args[1:])
	case "help", "-h", "-help", "--help":
		usage(os.Stdout)
		return 0
	default:
		fmt.Fprintf(os.Stderr, "orikagoc: unknown command %q\n\n", args[0])
		usage(os.Stderr)
		return 2
	}
}

const usageText = `orikagoc - Go analysis sidecar for Orikago.CodeAnalysis

usage:
  orikagoc parse <file.go> [-pretty]         print the go/parser AST as JSON
  orikagoc parse - [--path <name>]           same, reading source from stdin
  orikagoc parse --expr "<src>"              parse a single expression
  orikagoc check <moduleDir> [build flags]   type-check a module (go/packages)
  orikagoc symbol <file.go> <line> <col> [-dir <moduleDir>] [build flags]
                                              symbol info at a 1-based position

build flags (check, symbol) select the build context, exactly as for go build:
  -tags <list>    comma-separated build tags
  -goos <os>      target GOOS (default: host)
  -goarch <arch>  target GOARCH (default: host)

All commands print JSON on stdout and exit 0 even when the analyzed source
contains errors; a nonzero exit reports an infrastructure failure on stderr.

Columns are 1-based and counted in UTF-16 code units (as .NET and Visual
Studio count them), not in bytes; offsets are byte offsets.
`

func usage(w io.Writer) {
	fmt.Fprint(w, usageText)
}

// infrastructureFailure reports an infrastructure (non-source) error and
// yields exit code 1.
func infrastructureFailure(err error) int {
	fmt.Fprintln(os.Stderr, "orikagoc: "+err.Error())
	return 1
}

// emit writes v to stdout as JSON: one line by default, indented with pretty.
func emit(v any, pretty bool) int {
	encoder := json.NewEncoder(os.Stdout)
	if pretty {
		encoder.SetIndent("", "  ")
	}
	if err := encoder.Encode(v); err != nil {
		return infrastructureFailure(err)
	}
	return 0
}

func newFlagSet(name string) *flag.FlagSet {
	fs := flag.NewFlagSet(name, flag.ContinueOnError)
	fs.SetOutput(os.Stderr)
	return fs
}

// parseArguments parses fs against args, allowing flags to appear both
// before and after up to positionalCount positional arguments (so
// "parse file.go -pretty" and "parse -pretty file.go" both work). It
// returns the positional arguments and -1, or a non-negative exit code
// when flag parsing failed or help was requested.
func parseArguments(fs *flag.FlagSet, args []string, positionalCount int) ([]string, int) {
	if err := fs.Parse(args); err != nil {
		if err == flag.ErrHelp {
			return nil, 0
		}
		return nil, 2
	}
	rest := fs.Args()
	if len(rest) <= positionalCount {
		return rest, -1
	}
	positional := append([]string(nil), rest[:positionalCount]...)
	if err := fs.Parse(rest[positionalCount:]); err != nil {
		if err == flag.ErrHelp {
			return nil, 0
		}
		return nil, 2
	}
	return append(positional, fs.Args()...), -1
}
