namespace Orikago.CodeAnalysis.Tests;

public sealed class GoSyntaxTreeTests
{
    // Built via escapes so the byte layout (LF line endings, tab indentation) is exact —
    // the position assertions below were verified against go/parser (go1.21) for these bytes.
    //
    //  line 1: package main
    //  line 2:
    //  line 3: import "fmt"
    //  line 4:
    //  line 5: func main() {
    //  line 6: \tfmt.Println("hello")
    //  line 7: }
    private const string HELLO_SOURCE =
        "package main\n" +
        "\n" +
        "import \"fmt\"\n" +
        "\n" +
        "func main() {\n" +
        "\tfmt.Println(\"hello\")\n" +
        "}\n";

    public GoSyntaxTreeTests()
    {
        Sidecar.EnsureConfigured();
    }

    [Fact]
    public void ParseText_ValidProgram_RootIsFile()
    {
        // Act
        var tree = GoSyntaxTree.ParseText(HELLO_SOURCE, "hello.go");

        // Assert
        Assert.NotNull(tree.Root);
        Assert.Equal("File", tree.Root.Kind);
        Assert.NotEmpty(tree.Root.Children);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseText_ValidProgram_ContainsFuncDeclNamedMain()
    {
        // Act
        var tree = GoSyntaxTree.ParseText(HELLO_SOURCE, "hello.go");

        // Assert
        Assert.NotNull(tree.Root);
        var funcDecl = Assert.Single(
            collection: tree.Root.DescendantNodes(),
            predicate: node => HasKind(node, "FuncDecl"));

        var nameIdent = funcDecl.FirstChild("Ident");
        Assert.NotNull(nameIdent);
        Assert.Equal("main", nameIdent.Text);
    }

    [Fact]
    public void ParseText_ValidProgram_FuncKeywordPositionMatchesSource()
    {
        // Act
        var tree = GoSyntaxTree.ParseText(HELLO_SOURCE, "hello.go");

        // Assert
        Assert.NotNull(tree.Root);
        var funcDecl = Assert.Single(
            collection: tree.Root.DescendantNodes(),
            predicate: node => HasKind(node, "FuncDecl"));

        // ast.FuncDecl.Pos() is the `func` keyword: line 5, column 1 (1-based).
        Assert.Equal(5, funcDecl.Start.Line);
        Assert.Equal(1, funcDecl.Start.Column);

        // FuncDecl.End() is just past the closing brace `}` on line 7.
        Assert.Equal(7, funcDecl.End.Line);
        Assert.Equal(2, funcDecl.End.Column);
    }

    [Fact]
    public void ParseText_ValidProgram_AstIsDeepNotShallow()
    {
        // A stub that returns only a top-level File node (or File + decls) passes the
        // shape tests above; requiring interior expression nodes and token text rules
        // that out.

        // Act
        var tree = GoSyntaxTree.ParseText(HELLO_SOURCE, "hello.go");

        // Assert
        Assert.NotNull(tree.Root);
        var descendants = tree.Root.DescendantNodes().ToList();

        Assert.Contains(descendants, node => HasKind(node, "CallExpr"));
        Assert.Contains(descendants, node => HasKind(node, "BlockStmt"));
        Assert.Contains(descendants, node => HasKindAndText(node, "Ident", "fmt"));
        Assert.Contains(descendants, node => HasKindAndText(node, "Ident", "Println"));
        Assert.Contains(descendants, node => HasKindAndText(node, "BasicLit", "\"hello\""));
    }

    [Fact]
    public void ParseText_SyntaxError_DiagnosticAtLine2WithExactColumn()
    {
        // line 1: package main
        // line 2: func {          <- go/parser (AllErrors) reports the first error at
        //                            line 2, col 6: expected 'IDENT', found '{'

        // Act
        var tree = GoSyntaxTree.ParseText("package main\nfunc {", "broken.go");
        var diagnostics = tree.GetDiagnostics();

        // Assert
        Assert.NotEmpty(diagnostics);

        Assert.All(diagnostics, diagnostic => Assert.Equal("GOPARSE", diagnostic.Id));
        Assert.All(
            collection: diagnostics,
            action: diagnostic => Assert.Equal(GoDiagnosticSeverity.Error, diagnostic.Severity));

        Assert.Contains(
            collection: diagnostics,
            filter: diagnostic => (diagnostic.Location.Line == 2) &&
                (diagnostic.Location.Column == 6) &&
                diagnostic.Message.Contains("expected", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.Message.Contains("IDENT", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_FilePath_IsPreserved()
    {
        // Act
        var tree = GoSyntaxTree.ParseText(HELLO_SOURCE, "hello.go");

        // Assert
        Assert.Equal("hello.go", tree.FilePath);
    }

    private static bool HasKind(GoSyntaxNode node, string kind)
    {
        return string.Equals(node.Kind, kind, StringComparison.Ordinal);
    }

    private static bool HasKindAndText(GoSyntaxNode node, string kind, string text)
    {
        return HasKind(node, kind) && string.Equals(node.Text, text, StringComparison.Ordinal);
    }
}