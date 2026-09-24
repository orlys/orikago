namespace Orikago.CodeAnalysis.Tests;

using System.Text;

/// <summary>
/// Columns crossing the sidecar boundary are UTF-16 code units, not bytes.
/// </summary>
/// <remarks>
/// go/token counts a Position.Column in bytes within the line, while .NET strings,
/// Visual Studio and LSP count UTF-16 code units. On a line containing non-ASCII text
/// the two disagree, so an editor asking about an identifier after 你好 used to miss it
/// entirely. These tests use a source where the difference is unmistakable and assert
/// the .NET-side numbers — the ones a caller can compute from its own string.
/// </remarks>
public sealed class GoColumnUnitTests
{
    //  line  1: package main
    //  line  2:
    //  line  3: import "fmt"
    //  line  4:
    //  line  5: type T struct{ 名前 string; count int }
    //  line  6:
    //  line  7: func main() {
    //  line  8: \tvar t T
    //  line  9: \tvalue := 42
    //  line 10: \tfmt.Println("你好世界", value, t.count)
    //  line 11: }
    //
    // Line 10 is where bytes and UTF-16 units part ways: 你好世界 is 12 bytes but 4
    // UTF-16 units, so `value` sits at byte column 30 / UTF-16 column 22 and `count`
    // at byte column 39 / UTF-16 column 31.
    // Line 5 does the same for a declaration site: 名前 is 6 bytes / 2 UTF-16 units,
    // so the `count` field is declared at byte column 31 / UTF-16 column 27.
    private const string NON_ASCII_SOURCE =
        "package main\n" +
        "\n" +
        "import \"fmt\"\n" +
        "\n" +
        "type T struct{ 名前 string; count int }\n" +
        "\n" +
        "func main() {\n" +
        "\tvar t T\n" +
        "\tvalue := 42\n" +
        "\tfmt.Println(\"你好世界\", value, t.count)\n" +
        "}\n";

    public GoColumnUnitTests()
    {
        Sidecar.EnsureConfigured();
    }

    /// <summary>
    /// The 1-based UTF-16 column of the first occurrence of <paramref name="token"/> on
    /// the given 1-based line — i.e. exactly what an editor working on the .NET string
    /// would report, computed independently of the sidecar.
    /// </summary>
    private static int Utf16ColumnOf(string source, int line, string token)
    {
        var text = source.Split("\n", StringSplitOptions.None)[line - 1];
        var index = text.IndexOf(token, StringComparison.Ordinal);
        Assert.True(
            condition: index >= 0,
            userMessage: FormattableString.Invariant(
                $"'{token}' not found on line {line}: {text}"));
        return index + 1;
    }

    [Fact]
    public void Fixture_NonAsciiLine_Utf16ColumnsDifferFromByteColumns()
    {
        // Guards the fixture itself: if these two agreed the tests below would prove
        // nothing. (Byte columns come from UTF-8 encoding the prefix of the line.)

        // Arrange
        var line10 = NON_ASCII_SOURCE.Split("\n", StringSplitOptions.None)[9];

        // Act
        var utf16Column = Utf16ColumnOf(NON_ASCII_SOURCE, 10, "value");
        var byteColumn = Encoding.UTF8.GetByteCount(line10[..(utf16Column - 1)]) + 1;

        // Assert
        Assert.Equal(22, utf16Column);
        Assert.Equal(30, byteColumn);
        Assert.NotEqual(utf16Column, byteColumn);
    }

    [Fact]
    public void GetSymbolAt_VariableAfterNonAsciiTextOnSameLine_ResolvesAtUtf16Column()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        var mainGo = module.WriteFile("main.go", NON_ASCII_SOURCE);
        var model = GoCompilation.Create(module.Directory).GetSemanticModel();

        // `value` on line 10: UTF-16 column 22
        var utf16Column = Utf16ColumnOf(NON_ASCII_SOURCE, 10, "value");

        // Act
        var symbol = model.GetSymbolAt(mainGo, 10, utf16Column);

        // Assert
        Assert.NotNull(symbol);
        Assert.Equal("value", symbol.Name);
        Assert.Equal("var", symbol.Kind);
        Assert.Equal("int", symbol.Type);
        Assert.NotNull(symbol.DeclaredAt);
        Assert.Equal(9, symbol.DeclaredAt.Line);
        Assert.Equal(2, symbol.DeclaredAt.Column);
    }

    [Fact]
    public void GetSymbolAt_ByteColumnOfSameIdentifier_DoesNotResolve()
    {
        // The old, byte-column behaviour: column 30 was `value`. It must not be now —
        // otherwise the protocol would be ambiguous rather than fixed.

        // Arrange
        using var module = new TemporaryGoModule();
        var mainGo = module.WriteFile("main.go", NON_ASCII_SOURCE);
        var model = GoCompilation.Create(module.Directory).GetSemanticModel();

        // Act
        var symbol = model.GetSymbolAt(mainGo, 10, 30);

        // Assert
        Assert.Null(symbol);
    }

    [Fact]
    public void GetSymbolAt_FieldDeclaredAfterNonAsciiField_ReportsUtf16DeclarationColumn()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        var mainGo = module.WriteFile("main.go", NON_ASCII_SOURCE);
        var model = GoCompilation.Create(module.Directory).GetSemanticModel();

        // The `count` part of `t.count` on line 10: UTF-16 column 31
        var useColumn = Utf16ColumnOf(NON_ASCII_SOURCE, 10, "t.count") + 2;

        // Act
        var symbol = model.GetSymbolAt(mainGo, 10, useColumn);

        // Assert
        Assert.NotNull(symbol);
        Assert.Equal("count", symbol.Name);
        Assert.Equal("field", symbol.Kind);
        Assert.Equal("int", symbol.Type);

        // Declaration site is on line 5, after the non-ASCII field name 名前: the
        // reported column must be the UTF-16 one (27), not the byte one (31).
        Assert.NotNull(symbol.DeclaredAt);
        Assert.Equal(5, symbol.DeclaredAt.Line);
        Assert.Equal(Utf16ColumnOf(NON_ASCII_SOURCE, 5, "count"), symbol.DeclaredAt.Column);
        Assert.Equal(27, symbol.DeclaredAt.Column);
    }

    [Fact]
    public void GetDiagnostics_ErrorAfterNonAsciiTextOnSameLine_ReportsUtf16Column()
    {
        // Arrange
        //  line 1: package main
        //  line 2:
        //  line 3: func main() {
        //  line 4: \tvar s, n = "你好世界", "x" + 1
        //  line 5: \t_, _ = s, n
        //  line 6: }
        //
        // The mismatched-types error is reported at the `"x"` operand: byte column 29,
        // UTF-16 column 21.
        const string NON_ASCII_TYPE_ERROR_SOURCE =
            "package main\n" +
            "\n" +
            "func main() {\n" +
            "\tvar s, n = \"你好世界\", \"x\" + 1\n" +
            "\t_, _ = s, n\n" +
            "}\n";

        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", NON_ASCII_TYPE_ERROR_SOURCE);

        // Act
        var diagnostics = GoCompilation.Create(module.Directory).GetDiagnostics();

        // Assert
        var error = Assert.Single(diagnostics);
        Assert.Equal("GOTYPE", error.Id);
        Assert.Equal(4, error.Location.Line);
        Assert.Equal(
            expected: Utf16ColumnOf(NON_ASCII_TYPE_ERROR_SOURCE, 4, "\"x\""),
            actual: error.Location.Column);
        Assert.Equal(21, error.Location.Column);
    }

    [Fact]
    public void ParseText_PositionsAfterNonAsciiText_AreUtf16Columns()
    {
        // The parse protocol carries columns too; they use the same unit.

        // Arrange
        //  line 1: package main
        //  line 2:
        //  line 3: var x = "你好世界" + y
        const string SOURCE = "package main\n\nvar x = \"你好世界\" + y\n";

        var lineStart = Encoding.UTF8.GetByteCount("package main\n\n");
        var byteColumn = Encoding.UTF8.GetByteCount("var x = \"你好世界\" + ") + 1;

        // Act
        var tree = GoSyntaxTree.ParseText(SOURCE, "utf16.go");

        // Assert
        Assert.NotNull(tree.Root);
        var y = Assert.Single(
            collection: tree.Root.DescendantNodes(),
            predicate: node => string.Equals(node.Kind, "Ident", StringComparison.Ordinal) &&
                string.Equals(node.Text, "y", StringComparison.Ordinal));

        // `y` is at UTF-16 column 18 but byte column 26.
        Assert.Equal(3, y.Start.Line);
        Assert.Equal(18, y.Start.Column);
        Assert.Equal(Utf16ColumnOf(SOURCE, 3, "+ y") + 2, y.Start.Column);

        // Offsets remain byte offsets, deliberately — they are documented as such.
        Assert.Equal(26, byteColumn);
        Assert.Equal(lineStart + byteColumn - 1, y.Start.Offset);
    }
}