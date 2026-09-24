package main

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"unicode/utf8"
)

// Column units
//
// go/token reports a Position.Column as a 1-based *byte* offset within its
// line. .NET, Visual Studio and the Language Server Protocol all count
// UTF-16 code units instead, so a line such as
//
//	x := 你好 + y
//
// puts `y` at byte column 15 but UTF-16 column 9. The orikagoc JSON
// protocol speaks UTF-16 code units exclusively: every column leaving the
// sidecar is converted from bytes to UTF-16 units, and every column
// entering it (the `symbol` position) is converted the other way before it
// is compared against go/token positions.
//
// Byte *offsets* (the "offset" field of parse positions) are untouched:
// they are documented as byte offsets and are used as opaque values.

// lineIndex caches the raw bytes of the lines of the files it is asked
// about, so that repeated column conversions in one process do not re-read
// the same file over and over.
type lineIndex struct {
	files map[string][][]byte
}

func newLineIndex() *lineIndex {
	return &lineIndex{files: map[string][][]byte{}}
}

func lineKey(path string) string {
	p := filepath.Clean(path)
	if runtime.GOOS == "windows" {
		p = strings.ToLower(p)
	}
	return p
}

// addSource registers already-known source bytes for path, so that input
// that never touched the disk (stdin, --expr) can still be converted.
func (x *lineIndex) addSource(path string, src []byte) {
	x.files[lineKey(path)] = splitLines(src)
}

// line returns the raw bytes of the given 1-based line of path, or nil
// when the file cannot be read or has no such line.
func (x *lineIndex) line(path string, line int) []byte {
	if path == "" || line < 1 {
		return nil
	}
	key := lineKey(path)
	lines, ok := x.files[key]
	if !ok {
		data, err := os.ReadFile(path)
		if err != nil {
			// Remembered as "unreadable"; do not retry.
			lines = nil
		} else {
			lines = splitLines(data)
		}
		x.files[key] = lines
	}
	if line > len(lines) {
		return nil
	}
	return lines[line-1]
}

// splitLines splits src into lines without their terminators, handling
// both LF and CRLF. A trailing newline does not produce a final empty
// line entry beyond what go/token would number.
func splitLines(src []byte) [][]byte {
	lines := make([][]byte, 0, 16)
	start := 0
	for i := 0; i < len(src); i++ {
		if src[i] != '\n' {
			continue
		}
		end := i
		if end > start && src[end-1] == '\r' {
			end--
		}
		lines = append(lines, src[start:end])
		start = i + 1
	}
	lines = append(lines, src[start:])
	return lines
}

// utf16Length counts the UTF-16 code units needed for b. Bytes that are not
// valid UTF-8 count as one unit each, mirroring how a decoder that
// substitutes U+FFFD would see them.
//
// A leading U+FEFF counts as ZERO units: it is the UTF-8 BOM (only line 1
// of a BOM file can start with it - go/scanner rejects a BOM anywhere
// else), its 3 bytes DO count in go/token's byte columns, but .NET and
// Visual Studio strip it from the buffer, so it must not shift the
// UTF-16 columns.
func utf16Length(b []byte) int {
	n := 0
	for i := 0; i < len(b); {
		r, size := utf8.DecodeRune(b[i:])
		if r == 0xFEFF && i == 0 {
			i += size
			continue
		}
		if r > 0xFFFF {
			// A surrogate pair.
			n += 2
		} else {
			n++
		}
		i += size
	}
	return n
}

// toUTF16Column converts a 1-based byte column on the given line of path into
// a 1-based UTF-16 code-unit column. Columns that cannot be resolved (no
// such file or line) are returned unchanged, which is exactly right for
// the pure-ASCII case and the best available answer otherwise.
func (x *lineIndex) toUTF16Column(path string, line, byteColumn int) int {
	if byteColumn <= 1 {
		return byteColumn
	}
	src := x.line(path, line)
	if src == nil {
		return byteColumn
	}
	n := byteColumn - 1
	if n > len(src) {
		// Past end of line (e.g. a position just after the last token):
		// count the whole line and keep the overshoot.
		return utf16Length(src) + 1 + (n - len(src))
	}
	return utf16Length(src[:n]) + 1
}

// toByteColumn converts a 1-based UTF-16 code-unit column on the given line
// of path into the 1-based byte column go/token uses. Unresolvable
// columns are returned unchanged.
func (x *lineIndex) toByteColumn(path string, line, utf16Column int) int {
	if utf16Column < 1 {
		// Only non-positive columns pass through unchanged. Column 1 is NOT
		// shortcut: on a BOM line the editor's column 1 is go/token's byte
		// column 4, so even that needs the conversion below.
		return utf16Column
	}
	src := x.line(path, line)
	if src == nil {
		return utf16Column
	}
	// UTF-16 units to skip.
	want := utf16Column - 1
	units, i := 0, 0
	if len(src) >= 3 && src[0] == 0xEF && src[1] == 0xBB && src[2] == 0xBF {
		// The line starts with a BOM. Mirror of utf16Length's BOM rule: the
		// BOM occupies 3 bytes of go/token's byte columns but zero UTF-16
		// units of the editor's, so skip it before counting.
		i = 3
	}
	for i < len(src) && units < want {
		r, size := utf8.DecodeRune(src[i:])
		if r > 0xFFFF {
			// A surrogate pair.
			units += 2
		} else {
			units++
		}
		i += size
	}
	if units < want {
		// Past end of line: keep the overshoot in bytes.
		return len(src) + 1 + (want - units)
	}
	return i + 1
}
