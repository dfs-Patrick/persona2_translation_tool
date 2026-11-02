using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GameTranslationEngine.App.ViewModels;
using GameTranslationEngine.App.Views;

namespace GameTranslationEngine.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(new Services.DialogService(window));
            // "GameTranslationEngine <project folder or project file>" opens the project directly.
            if (desktop.Args is [var project, ..]) viewModel.OpenFromCommandLine(project);
            window.DataContext = viewModel;
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
