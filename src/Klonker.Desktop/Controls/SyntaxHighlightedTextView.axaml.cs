using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Klonker.Desktop.ViewModels;

namespace Klonker.Desktop.Controls;

public partial class SyntaxHighlightedTextView : UserControl
{
    private static readonly IBrush PlainBrush = Brush.Parse("#D8DEE4");
    private static readonly IBrush KeywordBrush = Brush.Parse("#FF7AB2");
    private static readonly IBrush TypeBrush = Brush.Parse("#70D7C7");
    private static readonly IBrush StringBrush = Brush.Parse("#E7B64C");
    private static readonly IBrush NumberBrush = Brush.Parse("#B59BFF");
    private static readonly IBrush CommentBrush = Brush.Parse("#6F8290");
    private static readonly IBrush PreprocessorBrush = Brush.Parse("#F09A6B");
    private static readonly IBrush FunctionBrush = Brush.Parse("#70AFFF");
    private static readonly IBrush PropertyBrush = Brush.Parse("#7DD3FC");
    private static readonly IBrush HeadingBrush = Brush.Parse("#52CF6D");
    private static readonly IBrush OperatorBrush = Brush.Parse("#A8B4BC");

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SyntaxHighlightedTextView, string?>(nameof(Text));

    public static readonly StyledProperty<string?> FileNameProperty =
        AvaloniaProperty.Register<SyntaxHighlightedTextView, string?>(nameof(FileName));

    public static readonly StyledProperty<string> AutomationNameProperty =
        AvaloniaProperty.Register<SyntaxHighlightedTextView, string>(
            nameof(AutomationName),
            defaultValue: "Syntax highlighted file preview");

    public SyntaxHighlightedTextView()
    {
        InitializeComponent();
        RenderText();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? FileName
    {
        get => GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public string AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FileNameProperty)
        {
            RenderText();
        }
    }

    private void RenderText()
    {
        if (CodeText is null || FileNameLabel is null || LanguageLabel is null)
        {
            return;
        }

        var fileName = FileName ?? "No file selected";
        FileNameLabel.Text = fileName;
        LanguageLabel.Text = SyntaxHighlighter.GetLanguageName(fileName);

        var inlines = new InlineCollection();
        foreach (var token in SyntaxHighlighter.Highlight(
                     Text ?? "Select a text file to preview it.",
                     fileName))
        {
            inlines.Add(new Run(token.Text)
            {
                Foreground = GetBrush(token.Kind),
            });
        }

        CodeText.Inlines = inlines;
    }

    private static IBrush GetBrush(SyntaxTokenKind kind) =>
        kind switch
        {
            SyntaxTokenKind.Keyword => KeywordBrush,
            SyntaxTokenKind.Type => TypeBrush,
            SyntaxTokenKind.StringLiteral => StringBrush,
            SyntaxTokenKind.Number => NumberBrush,
            SyntaxTokenKind.Comment => CommentBrush,
            SyntaxTokenKind.Preprocessor => PreprocessorBrush,
            SyntaxTokenKind.Function => FunctionBrush,
            SyntaxTokenKind.Property => PropertyBrush,
            SyntaxTokenKind.Heading => HeadingBrush,
            SyntaxTokenKind.Operator => OperatorBrush,
            _ => PlainBrush,
        };
}
