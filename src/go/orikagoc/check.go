package main

import (
	"fmt"
	"os"
)

// checkResult is the wrapper object printed by the check command.
type checkResult struct {
	Diagnostics []diagnostic `json:"diagnostics"`
}

// checkCommand implements "orikagoc check <moduleDir> [-tags ...]
// [-goos ...] [-goarch ...]": a module-aware type-check of every package
// in the module, driven by golang.org/x/tools/go/packages (and therefore
// by the real `go list`). Parse and type errors are data — they become
// diagnostics and the exit code stays 0; only a failure of the toolchain
// itself is an infrastructure error.
func checkCommand(args []string) int {
	// Parse the flags and the single module directory argument.
	fs := newFlagSet("check")
	pretty := fs.Bool("pretty", false, "indent the JSON output")
	var options buildOptions
	options.registerBuildFlags(fs)
	rest, code := parseArguments(fs, args, 1)
	if code >= 0 {
		return code
	}
	if len(rest) != 1 {
		fmt.Fprintln(os.Stderr,
			"usage: orikagoc check <moduleDir> [-tags <list>] [-goos <os>] [-goarch <arch>]")
		return 2
	}

	// The module directory must exist and be a directory.
	info, err := os.Stat(rest[0])
	if err != nil {
		return infrastructureFailure(err)
	}
	if !info.IsDir() {
		return infrastructureFailure(fmt.Errorf("%s is not a directory", rest[0]))
	}

	// Type-check the module and print its diagnostics.
	packageLoader, err := newLoader(rest[0], options)
	if err != nil {
		return infrastructureFailure(err)
	}
	diagnostics, err := packageLoader.checkModule()
	if err != nil {
		return infrastructureFailure(err)
	}
	return emit(checkResult{Diagnostics: diagnostics}, *pretty)
}
