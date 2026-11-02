using Avalonia.Controls;
using GameTranslationEngine.App.ViewModels;

namespace GameTranslationEngine.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as MainWindowViewModel)?.Shutdown();
    }
}
