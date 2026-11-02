using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Editing;

namespace GameTranslationEngine.App.Controls;

/// <summary>
/// B4 · Text box of a message: game commands in their own color, glossary
/// terms marked (hover shows "original → tradução" and the description) and,
/// in the translation, misspelled words underlined with suggestions on right click.
/// </summary>
public sealed class MessageEditor : Decorator
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MessageEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<MessageEditor, bool>(nameof(IsReadOnly));

    /// <summary>True for the translation: Portuguese glossary side and spell checking.</summary>
    public static readonly StyledProperty<bool> IsTranslationProperty =
        AvaloniaProperty.Register<MessageEditor, bool>(nameof(IsTranslation));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<MessageEditor, string?>(nameof(Watermark));

    public static readonly StyledProperty<EditorServices?> ServicesProperty =
        AvaloniaProperty.Register<MessageEditor, EditorServices?>(nameof(Services));

    private static readonly IBrush CommandBrush = new SolidColorBrush(Color.Parse("#9A6CD4"));
    private static readonly IBrush EscapeBrush = new SolidColorBrush(Color.Parse("#B08AE0"));
    private static readonly IBrush InvalidBrush = new SolidColorBrush(Color.Parse("#D13438"));
    private static readonly IBrush GlossaryBrush = new SolidColorBrush(Color.Parse("#2B88D8"));
    private static readonly IBrush GlossaryBackground = new SolidColorBrush(Color.Parse("#2B88D8"), 0.16);
    private static readonly IBrush SearchBackground = new SolidColorBrush(Color.Parse("#F2C230"), 0.45);
    private static readonly IPen SquigglePen = new Pen(new SolidColorBrush(Color.Parse("#E81123")), 1.2);

    private readonly TextEditor _editor;
    private IReadOnlyList<TextSpan> _spans = [];
    private bool _updating;

    public MessageEditor()
    {
        _editor = new TextEditor
        {
            WordWrap = true,
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = 13,
            MinHeight = 56,
            Padding = new Thickness(8, 6),
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.HighlightCurrentLine = false;
        _editor.Options.AllowScrollBelowDocument = false;
        _editor.TextArea.TextView.LineTransformers.Add(new Colorizer(this));
        _editor.TextArea.TextView.BackgroundRenderers.Add(new SquiggleRenderer(this));
        _editor.TextChanged += (_, _) =>
        {
            if (_updating) return;
            _updating = true;
            SetCurrentValue(TextProperty, _editor.Text);
            _updating = false;
            Analyze();
        };
        _editor.PointerHover += OnPointerHover;
        _editor.PointerHoverStopped += (_, _) => ToolTip.SetIsOpen(_editor, false);
        _editor.ContextRequested += OnContextRequested;
        var frame = new Border { Child = _editor, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("TextControlBorderBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("TextControlBackground"));
        Child = frame;
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool IsTranslation
    {
        get => GetValue(IsTranslationProperty);
        set => SetValue(IsTranslationProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public EditorServices? Services
    {
        get => GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            if (_updating) return;
            var text = change.GetNewValue<string>() ?? "";
            if (_editor.Text == text) return;
            _updating = true;
            _editor.Text = text;
            _updating = false;
            Analyze();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == WatermarkProperty)
        {
            _editor.Watermark = change.GetNewValue<string?>();
        }
        else if (change.Property == ServicesProperty)
        {
            if (change.GetOldValue<EditorServices?>() is { } old) old.Changed -= Analyze;
            if (change.GetNewValue<EditorServices?>() is { } services && IsAttached) services.Changed += Analyze;
            Analyze();
        }
        else if (change.Property == IsTranslationProperty)
        {
            Analyze();
        }
    }

    private bool IsAttached { get; set; }

    // Editors are recycled by the list: only attached ones listen to the project.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        IsAttached = true;
        if (Services is { } services)
        {
            services.Changed -= Analyze;
            services.Changed += Analyze;
        }
        Analyze();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        IsAttached = false;
        if (Services is { } services) services.Changed -= Analyze;
    }

    private void Analyze()
    {
        var spans = Services?.Analyzer.Analyze(_editor.Text, IsTranslation) ?? [];
        // B5 · Occurrences of the global search, on the side(s) it looks at.
        if (Services is { SearchPattern: { } pattern } services
            && services.SearchScope != (IsTranslation ? GameTranslationEngine.Core.Projects.SearchScope.Original : GameTranslationEngine.Core.Projects.SearchScope.Translation))
        {
            var found = new List<TextSpan>(spans);
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(_editor.Text))
                found.Add(new TextSpan(match.Index, match.Length, SpanKind.Search));
            spans = found;
        }
        _spans = spans;
        _editor.TextArea.TextView.Redraw();
    }

    private TextSpan? SpanAt(Point pointInEditor, params SpanKind[] kinds)
    {
        var view = _editor.TextArea.TextView;
        var point = _editor.TranslatePoint(pointInEditor, view);
        if (point is null) return null;
        var position = view.GetPosition(point.Value + view.ScrollOffset);
        if (position is null) return null;
        var offset = _editor.Document.GetOffset(position.Value.Location);
        return _spans.FirstOrDefault(s => kinds.Contains(s.Kind) && (s.Contains(offset) || (offset == s.End && offset > s.Start)));
    }

    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        var span = SpanAt(e.GetPosition(_editor), SpanKind.Glossary, SpanKind.Misspelled, SpanKind.InvalidCommand);
        object? tip = span switch
        {
            { Kind: SpanKind.Glossary, Term: { } term } => GlossaryTip(term),
            { Kind: SpanKind.Misspelled } => "Palavra não encontrada no dicionário. Clique com o botão direito para ver sugestões.",
            { Kind: SpanKind.InvalidCommand } => "Comando desconhecido ou sem \"]\": o jogo não vai aceitar esta mensagem.",
            _ => null,
        };
        if (tip is null)
        {
            ToolTip.SetIsOpen(_editor, false);
            return;
        }
        ToolTip.SetTip(_editor, tip);
        ToolTip.SetIsOpen(_editor, true);
    }

    private static Control GlossaryTip(GlossaryTerm term)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Text = $"{term.Original}  →  {(term.Translation.Length > 0 ? term.Translation : "(sem tradução definida)")}",
        });
        if (term.Description.Trim().Length > 0)
            panel.Children.Add(new TextBlock { Text = term.Description, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var items = new List<Control>();
        var services = Services;
        if (e.TryGetPosition(_editor, out var point)
            && services?.Speller is { } speller && !IsReadOnly
            && SpanAt(point, SpanKind.Misspelled) is { } wrong)
        {
            var word = _editor.Document.GetText(wrong.Start, wrong.Length);
            var suggestions = speller.Suggest(word);
            foreach (var suggestion in suggestions)
                items.Add(MenuItem(suggestion, () => _editor.Document.Replace(wrong.Start, wrong.Length, suggestion), bold: true));
            if (suggestions.Count == 0) items.Add(new MenuItem { Header = "(sem sugestões)", IsEnabled = false });
            items.Add(MenuItem($"Adicionar \"{word}\" ao dicionário do projeto", () => services.AddWord(word)));
            items.Add(new Separator());
        }
        var selected = _editor.SelectedText.Trim();
        if (services is not null && selected.Length > 0 && !selected.Contains('['))
        {
            items.Add(MenuItem("Adicionar ao glossário…", () => services.RequestAddTerm(selected, IsTranslation)));
            items.Add(new Separator());
        }
        if (!IsReadOnly) items.Add(MenuItem("Recortar", _editor.Cut, enabled: _editor.SelectionLength > 0));
        items.Add(MenuItem("Copiar", _editor.Copy, enabled: _editor.SelectionLength > 0));
        if (!IsReadOnly) items.Add(MenuItem("Colar", _editor.Paste));
        items.Add(MenuItem("Selecionar tudo", _editor.SelectAll));

        var menu = new ContextMenu { ItemsSource = items };
        _editor.ContextMenu = menu;
        menu.Open(_editor);
        e.Handled = true;
    }

    private static MenuItem MenuItem(string header, Action action, bool enabled = true, bool bold = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (bold) item.FontWeight = FontWeight.SemiBold;
        item.Click += (_, _) => action();
        return item;
    }

    private sealed class Colorizer(MessageEditor owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            foreach (var span in owner._spans)
            {
                var start = Math.Max(span.Start, line.Offset);
                var end = Math.Min(span.End, line.EndOffset);
                if (start >= end || end > CurrentContext.Document.TextLength) continue;
                switch (span.Kind)
                {
                    case SpanKind.Command:
                        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(CommandBrush));
                        break;
                    case SpanKind.Escape:
                        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(EscapeBrush));
                        break;
                    case SpanKind.InvalidCommand:
                        ChangeLinePart(start, end, e =>
                        {
                            e.TextRunProperties.SetForegroundBrush(InvalidBrush);
                            e.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                        });
                        break;
                    case SpanKind.Search:
                        ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(SearchBackground));
                        break;
                    case SpanKind.Glossary:
                        ChangeLinePart(start, end, e =>
                        {
                            e.TextRunProperties.SetForegroundBrush(GlossaryBrush);
                            e.TextRunProperties.SetBackgroundBrush(GlossaryBackground);
                        });
                        break;
                }
            }
        }
    }

    /// <summary>Wavy red line under misspelled words.</summary>
    private sealed class SquiggleRenderer(MessageEditor owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid) return;
            var length = textView.Document?.TextLength ?? 0;
            foreach (var span in owner._spans)
            {
                if (span.Kind != SpanKind.Misspelled || span.End > length) continue;
                var segment = new TextSegment { StartOffset = span.Start, Length = span.Length };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        var y = rect.Bottom - 1.5;
                        context.BeginFigure(new Point(rect.Left, y), false);
                        var up = true;
                        for (var x = rect.Left + 2; x <= rect.Right; x += 2, up = !up)
                            context.LineTo(new Point(x, up ? y - 2 : y));
                        context.EndFigure(false);
                    }
                    drawingContext.DrawGeometry(null, SquigglePen, geometry);
                }
            }
        }
    }
}
