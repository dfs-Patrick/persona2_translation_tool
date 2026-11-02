using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.App.ViewModels;

public sealed record RecentProject(string Path, string Name, string Folder);

/// <summary>Start page: create a project from an ISO or open an existing one.</summary>
public partial class StartViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly Action<TranslationProject> _opened;

    [ObservableProperty] private string _projectName = "Tradução PT-BR";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Logo))]
    private IGameEngine _engine = GameEngines.Default;

    public Avalonia.Media.Imaging.Bitmap? Logo => EngineArt.Logo(Engine);
    [ObservableProperty] private string _isoPath = "";
    [ObservableProperty] private string _projectFolder = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private double _progress;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isBusy;

    public StartViewModel(IDialogService dialogs, AppSettings settings, Action<TranslationProject> opened)
    {
        _dialogs = dialogs;
        _settings = settings;
        _opened = opened;
        foreach (var path in settings.RecentProjects.Where(File.Exists))
        {
            var folder = System.IO.Path.GetDirectoryName(path)!;
            Recent.Add(new RecentProject(path, System.IO.Path.GetFileName(folder), folder));
        }
    }

    public ObservableCollection<RecentProject> Recent { get; } = [];

    /// <summary>Game engines a new project can use (one per supported game).</summary>
    public IReadOnlyList<IGameEngine> Engines => GameEngines.All;
    public bool HasRecent => Recent.Count > 0;

    [RelayCommand]
    private async Task BrowseIso()
    {
        var path = await _dialogs.PickFileAsync("Escolha a ISO original", "Imagem de disco", "*.iso", "*.cue");
        if (path is null) return;
        IsoPath = path;
        if (ProjectFolder.Length == 0)
            ProjectFolder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "projeto");
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var path = await _dialogs.PickFolderAsync("Escolha a pasta do projeto");
        if (path is not null) ProjectFolder = path;
    }

    private bool CanCreate() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        Error = null;
        if (!File.Exists(IsoPath)) { Error = "Escolha um arquivo de ISO existente."; return; }
        if (ProjectFolder.Trim().Length == 0) { Error = "Escolha a pasta onde o projeto será criado."; return; }
        if (ProjectName.Trim().Length == 0) { Error = "Dê um nome ao projeto."; return; }

        IsBusy = true;
        try
        {
            var progress = new Progress<(string Step, double Fraction)>(p =>
            {
                ProgressText = $"{p.Step}… {p.Fraction:P0}";
                Progress = p.Fraction * 100;
            });
            var project = await Task.Run(() =>
                TranslationProject.CreateAsync(ProjectFolder.Trim(), ProjectName.Trim(), IsoPath, Engine, progress));
            _opened(project);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
                                          or UnauthorizedAccessException)
        {
            Error = error.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
        }
    }

    [RelayCommand]
    private async Task Open()
    {
        var path = await _dialogs.PickFileAsync("Abrir projeto", "Projeto de tradução",
            TranslationProject.FileName, TranslationProject.LegacyFileName);
        if (path is not null) OpenPath(path);
    }

    [RelayCommand]
    private void OpenRecent(RecentProject recent) => OpenPath(recent.Path);

    private void OpenPath(string path)
    {
        Error = null;
        try
        {
            _opened(TranslationProject.Open(path));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Error = $"Não foi possível abrir o projeto: {error.Message}";
        }
    }
}
