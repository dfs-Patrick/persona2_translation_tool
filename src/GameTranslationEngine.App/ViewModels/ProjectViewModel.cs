using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.App.ViewModels;

/// <summary>A translation file in the browser, with its progress.</summary>
public partial class TbfFileItemViewModel(TbfFileInfo info) : ObservableObject
{
    public TbfFileInfo Info { get; } = info;
    public string Label => Info.Label;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProgressText), nameof(IsDone))]
    private int _translated;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProgressText), nameof(IsDone))]
    private int _total = -1;

    public string ProgressText => Total < 0 ? "" : $"{Translated}/{Total}";
    public bool IsDone => Total > 0 && Translated == Total;
}

public partial class TbfGroupViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<TbfFileItemViewModel> Files { get; } = [];
    public string Summary => $"{Name} ({Files.Count})";
}

/// <summary>A node of the ISO browser (folders, files and the entries of the CPK).</summary>
public sealed class IsoNodeViewModel(string name, string detail)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public ObservableCollection<IsoNodeViewModel> Children { get; } = [];
}

/// <summary>An open project: translation files, ISO browser and the editor.</summary>
public partial class ProjectViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Action _close;
    private List<TbfFileItemViewModel> _files = [];

    [ObservableProperty] private TbfEditorViewModel? _editor;
    /// <summary>What the right side shows: the open TBF or the glossary.</summary>
    [ObservableProperty] private ObservableObject? _panel;
    [ObservableProperty] private string _spellerStatus = "";
    [ObservableProperty] private object? _selectedNode;
    [ObservableProperty] private string _fileFilter = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _isoWarning;
    [ObservableProperty] private string? _editorError;
    [ObservableProperty] private double _busyProgress;
    [ObservableProperty] private string _busyStep = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(ValidateCommand), nameof(BuildIsoCommand), nameof(ExportFontsCommand))]
    private bool _isBusy;

    public ProjectViewModel(TranslationProject project, IDialogService dialogs, Action close)
    {
        Project = project;
        _dialogs = dialogs;
        _close = close;
        IsoWarning = project.CheckIso();
        Services = new EditorServices(project);
        SpellerStatus = Services.SpellerStatus;
        Services.Changed += () => SpellerStatus = Services.SpellerStatus;
        Services.AddTermRequested += (text, fromTranslation) =>
        {
            ShowGlossary();
            Glossary!.AddTerm(fromTranslation ? "" : text, fromTranslation ? text : "");
        };
        Search = new SearchViewModel(
            () =>
            {
                // The search reads the files: edits still on screen are saved first.
                Editor?.Save();
                return _files.Select(f => f.Info).ToList();
            },
            OpenHit,
            Services.SetSearch);
        ReloadFiles();
        _ = LoadIsoTreeAsync();
    }

    public TranslationProject Project { get; }
    public EditorServices Services { get; }
    public SearchViewModel Search { get; }
    public Avalonia.Media.Imaging.Bitmap? Logo => EngineArt.Logo(Project.Engine);
    public GlossaryViewModel? Glossary { get; private set; }
    public string Name => Project.Name;
    public string IsoSummary =>
        $"Motor: {Project.Engine.Name} · ISO {Project.File.Iso.GameId} · {Project.File.Iso.Size / 1024.0 / 1024.0:N0} MB"
        + (Project.Engine.Fonts is null ? "" : $" · fonte: {Project.File.Font}");

    public ObservableCollection<TbfGroupViewModel> Groups { get; } = [];
    public ObservableCollection<IsoNodeViewModel> IsoTree { get; } = [];
    public bool HasNoFiles => _files.Count == 0;

    partial void OnSelectedNodeChanged(object? value)
    {
        if (value is TbfFileItemViewModel file) OpenFile(file);
    }

    partial void OnFileFilterChanged(string value) => RebuildGroups();

    /// <summary>Saves the open file and the glossary if they have changes.</summary>
    public void SavePending()
    {
        Editor?.Save();
        Glossary?.Save();
    }

    /// <summary>Ctrl+S: saves what is on screen.</summary>
    [RelayCommand]
    private void SaveCurrent()
    {
        if (Panel is GlossaryViewModel glossary) glossary.Save();
        else Editor?.Save();
    }

    /// <summary>B2 · Shows the glossary in place of the editor (the open file is kept).</summary>
    [RelayCommand]
    private void ShowGlossary()
    {
        Editor?.Save();
        Glossary ??= new GlossaryViewModel(Services, Project.GlossaryPath);
        Panel = Glossary;
        // Clicking the open file again brings the editor back.
        SelectedNode = null;
    }

    /// <summary>Back from the glossary to the file that was open.</summary>
    [RelayCommand]
    private void ShowEditor()
    {
        Glossary?.Save();
        Panel = Editor;
    }

    [RelayCommand]
    private void CloseProject() => _close();

    [RelayCommand]
    private async Task ImportTbf()
    {
        var folder = await _dialogs.PickFolderAsync("Escolha a pasta com os arquivos .tbf (ex.: lab/translation/en/new/messages da v2)");
        if (folder is null) return;
        SavePending();
        var (copied, skipped, invalid) = await Task.Run(() => Project.ImportTbf(folder));
        ReloadFiles();
        var message = $"{copied} arquivo(s) importado(s).";
        if (skipped > 0) message += $"\n{skipped} já existia(m) no projeto e não foram alterados.";
        if (invalid.Count > 0)
            message += $"\n{invalid.Count} inválido(s) ignorado(s):\n" + string.Join("\n", invalid.Take(10));
        await _dialogs.ShowMessageAsync("Importar TBF", message);
    }

    private bool CanUseIso() => !IsBusy && IsoWarning is null;
    private bool CanExportFonts() => CanUseIso() && Project.Engine.Fonts is not null;

    /// <summary>A2–A8: reads the game's text from the ISO and creates the missing TBF files.</summary>
    [RelayCommand(CanExecute = nameof(CanUseIso))]
    private Task Generate() => RunBusy("Gerar arquivos de tradução", async progress =>
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = await Task.Run(() => Project.GenerateTranslationArea(
            new Progress<double>(f => progress.Report(("Lendo os textos da ISO", f)))));
        ReloadFiles();
        var message = $"{report.Created} arquivo(s) criado(s), {report.Kept} já existia(m) e não foram alterados.\n" +
                      $"{report.MessagesTotal} mensagens lidas da ISO em {watch.Elapsed.TotalSeconds:N1} s.";
        if (report.Differences.Count > 0)
            message += $"\n\nAtenção ({report.Differences.Count}):\n" + string.Join("\n", report.Differences.Take(10));
        return message;
    });

    /// <summary>A7: font pages of the ISO as PNG, plus an editable copy of the PT-BR profile.</summary>
    [RelayCommand(CanExecute = nameof(CanExportFonts))]
    private Task ExportFonts() => RunBusy("Exportar fontes", _ => Task.Run(() => Project.Engine.Fonts!.Export(Project)));

    /// <summary>C1/C2: checks every translation without writing anything.</summary>
    [RelayCommand(CanExecute = nameof(CanUseIso))]
    private Task Validate() => RunBusy("Validar a tradução", async progress =>
    {
        var report = await Task.Run(() => Project.CreateBuilder().Validate(progress));
        return Describe(report, built: false);
    });

    /// <summary>C1–C9: compiles the translation into a new ISO.</summary>
    [RelayCommand(CanExecute = nameof(CanUseIso))]
    private async Task BuildIso()
    {
        SavePending();
        var output = await _dialogs.SaveFileAsync("Salvar a ISO traduzida", Project.DefaultOutputIso, "Imagem ISO", "*.iso");
        if (output is null) return;
        await RunBusy("Compilar a ISO", async progress =>
        {
            var report = await Task.Run(() => Project.CreateBuilder().Build(output, progress));
            return Describe(report, built: true);
        });
    }

    private static string Describe(GameTranslationEngine.Core.Build.BuildReport report, bool built)
    {
        var lines = new List<string>();
        if (!report.Succeeded)
            lines.Add($"❌ {report.Errors} erro(s): corrija e tente de novo." + (built ? " Nenhuma ISO foi gravada." : ""));
        else if (built && report.OutputPath is not null)
            lines.Add($"✅ ISO gravada e conferida em {report.Elapsed.TotalSeconds:N1} s:\n{report.OutputPath}");
        else
            lines.Add($"✅ Nenhum erro ({report.Elapsed.TotalSeconds:N1} s).");
        lines.Add($"{report.TbfFiles} TBF lidos · {report.TranslatedMessages} mensagens traduzidas · {report.ChangedScripts} scripts alterados.");
        if (report.RebuiltEntries.Count > 0) lines.Add("Remontados: " + string.Join(", ", report.RebuiltEntries) + ".");
        var shown = report.Issues.Where(i => i.Level != GameTranslationEngine.Core.Build.IssueLevel.Info).Take(40).ToList();
        if (shown.Count > 0)
        {
            lines.Add("");
            lines.AddRange(shown.Select(i => (i.Level == GameTranslationEngine.Core.Build.IssueLevel.Error ? "Erro · " : "Aviso · ") + i));
            var rest = report.Issues.Count(i => i.Level != GameTranslationEngine.Core.Build.IssueLevel.Info) - shown.Count;
            if (rest > 0) lines.Add($"… e mais {rest}.");
        }
        var infos = report.Issues.Count(i => i.Level == GameTranslationEngine.Core.Build.IssueLevel.Info);
        if (infos > 0) lines.Add($"\n{infos} correção(ões) automática(s) de [item_with_type] vindas da v2.");
        return string.Join("\n", lines);
    }

    /// <summary>Runs a long task with the progress bar; its result (or failure) is shown in a dialog.</summary>
    private async Task RunBusy(string title, Func<IProgress<(string Step, double Fraction)>, Task<string>> work)
    {
        SavePending();
        IsBusy = true;
        BusyProgress = 0;
        BusyStep = title;
        try
        {
            var progress = new Progress<(string Step, double Fraction)>(p =>
            {
                BusyStep = p.Step;
                BusyProgress = p.Fraction * 100;
            });
            var message = await work(progress);
            await _dialogs.ShowMessageAsync(title, message);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
                                          or NotSupportedException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(title, $"Falhou: {error.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyStep = "";
        }
    }

    [RelayCommand]
    private void ReloadFiles()
    {
        _files = Project.ListTranslationFiles().Select(f => new TbfFileItemViewModel(f)).ToList();
        RebuildGroups();
        OnPropertyChanged(nameof(HasNoFiles));
        Status = $"{_files.Count} arquivo(s) de tradução";
        _ = CountProgressAsync(_files);
    }

    /// <summary>B5 · Opens the file of a search result at its message.</summary>
    private void OpenHit(GameTranslationEngine.Core.Projects.SearchHit hit)
    {
        var file = _files.FirstOrDefault(f => f.Info.FullPath == hit.File.FullPath);
        if (file is null) return;
        OpenFile(file);
        Editor?.Reveal(hit.Index);
    }

    private void OpenFile(TbfFileItemViewModel file)
    {
        if (Editor?.File.FullPath == file.Info.FullPath)
        {
            Panel = Editor;
            return;
        }
        // Switching files saves the current one: edits are never lost silently.
        SavePending();
        try
        {
            var editor = new TbfEditorViewModel(file.Info, TbfDocument.Load(file.Info.FullPath), Services);
            editor.Saved += e =>
            {
                file.Translated = e.TranslatedCount;
                file.Total = e.Total;
                Status = $"Salvo: {file.Info.RelativePath}";
            };
            Editor?.Detach();
            Editor = editor;
            Panel = editor;
            EditorError = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            Editor?.Detach();
            Editor = null;
            Panel = null;
            EditorError = $"Não foi possível abrir {file.Info.RelativePath}: {error.Message}";
        }
    }

    private void RebuildGroups()
    {
        var filter = FileFilter.Trim();
        Groups.Clear();
        foreach (var group in _files
                     .Where(f => filter.Length == 0
                                 || f.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                 || f.Info.Group.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(f => f.Info.Group)
                     .OrderBy(g => g.Key))
        {
            var vm = new TbfGroupViewModel(group.Key);
            foreach (var file in group) vm.Files.Add(file);
            Groups.Add(vm);
        }
    }

    private static async Task CountProgressAsync(IReadOnlyList<TbfFileItemViewModel> files)
    {
        var counts = await Task.Run(() => files.Select(file =>
        {
            try
            {
                var doc = TbfDocument.Load(file.Info.FullPath);
                return (doc.TranslatedCount, doc.Entries.Count);
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                return (0, -1);
            }
        }).ToList());
        for (var i = 0; i < files.Count; i++) (files[i].Translated, files[i].Total) = counts[i];
    }

    private async Task LoadIsoTreeAsync()
    {
        if (IsoWarning is not null) return;
        try
        {
            var root = await Task.Run(BuildIsoTree);
            IsoTree.Add(root);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            IsoWarning = $"Não foi possível ler a ISO: {error.Message}";
        }
    }

    private IsoNodeViewModel BuildIsoTree()
    {
        using var iso = Project.OpenIso();
        var root = new IsoNodeViewModel(Path.GetFileName(Project.IsoPath), iso.VolumeId);
        AddChildren(iso, iso.Root, root);
        return root;
    }

    private static void AddChildren(IsoIndex iso, IsoEntry directory, IsoNodeViewModel node)
    {
        foreach (var entry in directory.Children.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name))
        {
            var child = new IsoNodeViewModel(entry.Name,
                entry.IsDirectory ? $"{entry.Children.Count} itens" : $"{FormatSize(entry.Size)} · setor {entry.Lba}");
            node.Children.Add(child);
            if (entry.IsDirectory)
            {
                AddChildren(iso, entry, child);
            }
            else if (entry.Name.EndsWith(".cpk", StringComparison.OrdinalIgnoreCase))
            {
                var cpk = CpkIndex.Read(iso.Open(entry));
                foreach (var item in cpk.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    var detail = $"{FormatSize(item.ExtractedSize)} · id {item.Id}";
                    if (item.IsCompressed) detail += $" · comprimido ({FormatSize(item.StoredSize)})";
                    child.Children.Add(new IsoNodeViewModel(item.FullName, detail));
                }
            }
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:N1} MB",
        >= 1024 => $"{bytes / 1024.0:N0} KB",
        _ => $"{bytes} B",
    };
}
