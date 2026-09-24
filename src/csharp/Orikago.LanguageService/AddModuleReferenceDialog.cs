namespace Orikago.LanguageService;

using Microsoft.VisualStudio.PlatformUI;

using System.Linq;
using System.Windows;
using System.Windows.Controls;

/// <summary>
/// Input dialog for "加入 Go 模組參考...": module path (required) and version (optional; empty =
/// latest).
/// </summary>
/// <remarks>
/// Built in code rather than XAML so the old-style csproj needs no XAML compilation wiring.
/// </remarks>
internal sealed class AddModuleReferenceDialog : DialogWindow
{
    private readonly TextBox _modulePathBox;
    private readonly TextBox _versionBox;

    public AddModuleReferenceDialog()
    {
        // A fixed-width window whose height follows its content
        Title = GoStrings.AddReferenceDialogTitle;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // The two input boxes and the OK/Cancel row
        _modulePathBox = new TextBox { Margin = new Thickness(0, 2, 0, 8) };
        _versionBox = new TextBox { Margin = new Thickness(0, 2, 0, 8) };

        var okButton = new Button
        {
            Content = GoStrings.OkButton,
            Width = 80,
            Margin = new Thickness(0, 8, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = GoStrings.CancelButton,
            Width = 80,
            Margin = new Thickness(0, 8, 0, 0),
            IsCancel = true,
        };
        okButton.Click += OnOk;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        // Labels, boxes and buttons stacked top to bottom
        var layout = new StackPanel { Margin = new Thickness(12) };
        layout.Children.Add(new TextBlock { Text = GoStrings.ModulePathLabel });
        layout.Children.Add(_modulePathBox);
        layout.Children.Add(new TextBlock { Text = GoStrings.VersionLabel });
        layout.Children.Add(_versionBox);
        layout.Children.Add(buttons);
        Content = layout;

        Loaded += delegate
        {
            _modulePathBox.Focus();
        };
    }

    public string ModulePath { get; private set; } = string.Empty;

    public string ModuleVersion { get; private set; } = string.Empty;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var module = _modulePathBox.Text.Trim();
        var version = _versionBox.Text.Trim();
        char[] quotes = ['"', '\'', '`'];

        if ((module is not { Length: > 0 }) ||
            module.Any(char.IsWhiteSpace) ||
            (module.IndexOfAny(quotes) >= 0))
        {
            // The values land on a "go get" command line via MSBuild; reject
            // anything a module path can never legally contain instead of
            // letting a typo produce a confusing build failure.
            ExtensionLog.Warning(
                "Add Go Module Reference: rejected module path '" + module + "'.");
            ShowInvalidInput(GoStrings.InvalidModulePath);
            return;
        }

        if (version.Any(char.IsWhiteSpace) || (version.IndexOfAny(quotes) >= 0))
        {
            // Same reasoning for the version: whitespace or quotes can never be part of one
            ExtensionLog.Warning(
                "Add Go Module Reference: rejected version '" + version + "'.");
            ShowInvalidInput(GoStrings.InvalidVersion);
            return;
        }

        ModulePath = module;
        ModuleVersion = version;
        DialogResult = true;
    }

    private void ShowInvalidInput(string message)
    {
        MessageBox.Show(
            owner: this,
            messageBoxText: message,
            caption: GoStrings.MessageBoxTitle,
            button: MessageBoxButton.OK,
            icon: MessageBoxImage.Warning);
    }
}
