using Avalonia.Controls;
using Avalonia.Threading;

namespace GameTranslationEngine.App.Views;

public partial class TbfEditorView : UserControl
{
    public TbfEditorView()
    {
        InitializeComponent();
        // A search result selects a message: bring it into view once the list has laid it out.
        Messages.SelectionChanged += (_, _) =>
        {
            if (Messages.SelectedItem is { } item)
                Dispatcher.UIThread.Post(() => Messages.ScrollIntoView(item), DispatcherPriority.Background);
        };
    }
}
