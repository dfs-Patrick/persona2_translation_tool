using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.App.ViewModels;

/// <summary>Switches between the start page and an open project.</summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings = AppSettings.Load();

    [ObservableProperty] private ObservableObject _currentPage;
    [ObservableProperty] private string _title = "Game Translation Engine";
    /// <summary>Faded picture behind the window: the engine of the open project, or the one chosen on the start page.</summary>
    [ObservableProperty] private Bitmap? _background;

    public MainWindowViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _currentPage = CreateStart();
    }

    /// <summary>Saves pending edits; called when the window closes.</summary>
    public void Shutdown()
    {
        if (CurrentPage is ProjectViewModel project) project.SavePending();
    }

    public void OpenFromCommandLine(string path)
    {
        try
        {
            Open(TranslationProject.Open(path));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            if (CurrentPage is StartViewModel start) start.Error = $"Não foi possível abrir {path}: {error.Message}";
        }
    }

    private StartViewModel CreateStart()
    {
        var start = new StartViewModel(_dialogs, _settings, Open);
        Background = EngineArt.Background(start.Engine);
        start.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StartViewModel.Engine)) Background = EngineArt.Background(start.Engine);
        };
        return start;
    }

    private void Open(TranslationProject project)
    {
        _settings.AddRecent(project.ProjectFilePath);
        CurrentPage = new ProjectViewModel(project, _dialogs, Close);
        Background = EngineArt.Background(project.Engine);
        Title = $"{project.Name} — Game Translation Engine";
    }

    private void Close()
    {
        Shutdown();
        CurrentPage = CreateStart();
        Title = "Game Translation Engine";
    }
}
