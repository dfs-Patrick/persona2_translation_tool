using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.App.ViewModels;

/// <summary>A translation file in the browser, with its progress.</summary>
public partial class TbfFileItemViewModel(TbfFileInfo info) : ObservableObject
{
    public TbfFileInfo Info { get; } = info;
    public string Label => Info.Label;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ToolTip))]
    private string _directoryDescription = "";
    public string ToolTip => DirectoryDescription;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProgressText), nameof(IsDone))]
    private int _translated;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProgressText), nameof(IsDone))]
    private int _total = -1;

    public string ProgressText => Total < 0 ? "" : $"{Translated}/{Total}";
    public bool IsDone => Total > 0 && Translated == Total;
}

public sealed class DirectoryGroupViewModel : ObservableObject
{
    private readonly Action<DirectoryGroupViewModel> _changed;
    private string _label;
    private string _description;

    public DirectoryGroupViewModel(
        string directoryKey,
        string defaultLabel,
        string label,
        string description,
        Action<DirectoryGroupViewModel> changed)
    {
        DirectoryKey = directoryKey;
        DefaultLabel = defaultLabel;
        _label = label;
        _description = description;
        _changed = changed;
    }

    public string DirectoryKey { get; }
    public string DefaultLabel { get; }
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? DefaultLabel : Label;
    public string Summary => $"{DisplayLabel} ({Items.Count})";
    public ObservableCollection<object> Items { get; } = [];

    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(Summary));
                _changed(this);
            }
        }

    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value)) _changed(this);
        }
    }
}

/// <summary>A node of the ISO browser (folders, files and the entries of the CPK).</summary>
public sealed class IsoNodeViewModel(string name, string detail)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public ObservableCollection<IsoNodeViewModel> Children { get; } = [];
}

public partial class GameImageItemViewModel(GameImageSource source) : ObservableObject
{
    public GameImageSource Source { get; } = source;
    public string Name => Source.Name;
    public string Path => Source.Path;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ToolTip))]
    private string _directoryDescription = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ToolTip))]
    private string _description = "";

    public string ToolTip => string.Join(Environment.NewLine,
        new[] { Path, DirectoryDescription, Description }.Where(text => !string.IsNullOrWhiteSpace(text)));
}

public sealed class ImageSideViewModel
{
    private ImageSideViewModel(string name, Bitmap? preview, string resolution, string pixelFormat,
        string colorProfile, string palette, string compression, string grid)
    {
        Name = name;
        Preview = preview;
        Resolution = resolution;
        PixelFormat = pixelFormat;
        ColorProfile = colorProfile;
        Palette = palette;
        Compression = compression;
        Grid = grid;
    }

    public string Name { get; }
    public Bitmap? Preview { get; }
    public string Resolution { get; }
    public string PixelFormat { get; }
    public string ColorProfile { get; }
    public string Palette { get; }
    public string Compression { get; }
    public string Grid { get; }

    public static ImageSideViewModel Original(GameImageSource source) =>
        new(source.Name, BitmapFrom(source.Pixels),
            $"{source.Image.Width} × {source.Image.Height} px",
            $"{source.Image.PixelFormat} · {source.Image.BitsPerPixel} bpp",
            source.Image.ColorProfile,
            source.Image.ColorCount > 0
                ? $"{source.Image.ColorCount} cores · {source.Image.PaletteFormat}"
                : source.Image.PaletteFormat,
            source.Compression,
            source.Image.GridDescription);

    public static ImageSideViewModel Replacement(string path, ReadOnlySpan<byte> data)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".gim", StringComparison.OrdinalIgnoreCase))
        {
            var gim = GimImage.Read(data);
            var pixels = gim.ToRgba();
            return new ImageSideViewModel(Path.GetFileName(path), BitmapFrom(pixels),
                $"{gim.Width} × {gim.Height} px",
                $"{gim.PixelFormat} · {gim.BitsPerPixel} bpp",
                "Não incorporado (GIM/PSP)",
                gim.ColorCount > 0 ? $"{gim.ColorCount} cores · {gim.PaletteFormat}" : gim.PaletteFormat,
                "GIM independente",
                gim.IsSwizzled ? "Swizzled (16 bytes × 8 linhas)" : "Linear");
        }
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            var image = Png.Decode(data);
            var uniqueColors = new HashSet<uint>();
            for (var i = 0; i < image.Pixels.Length; i += 4)
                uniqueColors.Add(BitConverter.ToUInt32(image.Pixels, i));
            return new ImageSideViewModel(Path.GetFileName(path), BitmapFrom(image),
                $"{image.Width} × {image.Height} px", "RGBA · 8 bits por canal",
                PngColorProfile(data), $"{uniqueColors.Count} cores usadas",
                "PNG (DEFLATE)", "Linear (linhas)");
        }
        throw new InvalidDataException("A substituição deve ser um arquivo PNG ou GIM.");
    }

    private static Bitmap BitmapFrom(RgbaImage image)
    {
        using var stream = new MemoryStream(Png.Encode(image));
        return new Bitmap(stream);
    }

    private static string PngColorProfile(ReadOnlySpan<byte> data)
    {
        var position = 8;
        while (position + 12 <= data.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[position..]);
            if (length < 0 || position + 12L + length > data.Length) break;
            var type = Encoding.ASCII.GetString(data.Slice(position + 4, 4));
            if (type is "iCCP" or "sRGB" or "cICP") return $"Perfil PNG: {type}";
            if (type == "IEND") break;
            position += length + 12;
        }
        return "Não incorporado";
    }
}

public partial class ImageViewerViewModel : ObservableObject
{
    private readonly Action<string> _descriptionChanged;
    private string _description;

    [ObservableProperty]
    private string _editorStatus = "";

    public ImageViewerViewModel(GameImageSource source, ImageSideViewModel? replacement,
        string directoryDescription, string description, string? replacementError,
        Action<string> descriptionChanged)
    {
        Source = source;
        Original = ImageSideViewModel.Original(source);
        Replacement = replacement;
        DirectoryDescription = directoryDescription;
        ReplacementError = replacementError;
        _description = description;
        _descriptionChanged = descriptionChanged;
    }

    public GameImageSource Source { get; }
    public string Name => Source.Name;
    public string Path => Source.Path;
    public ImageSideViewModel Original { get; }
    public ImageSideViewModel? Replacement { get; private set; }
    public bool HasReplacement => Replacement is not null;
    public string DirectoryDescription { get; }
    public string? ReplacementError { get; }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value)) _descriptionChanged(value);
        }
    }

    public void SetReplacement(string path, ReadOnlySpan<byte> data)
    {
        Replacement = ImageSideViewModel.Replacement(path, data);
        OnPropertyChanged(nameof(Replacement));
        OnPropertyChanged(nameof(HasReplacement));
    }
}

/// <summary>An open project: translation files, ISO browser and the editor.</summary>
public partial class ProjectViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Action _close;
    private readonly AppSettings _settings;
    private List<TbfFileItemViewModel> _files = [];
    private readonly DirectoryActiveIndex _directoryIndex;
    private readonly ImageTranslationControl _imageControl;

    [ObservableProperty] private TbfEditorViewModel? _editor;
    /// <summary>What the right side shows: the open TBF or the glossary.</summary>
    [ObservableProperty] private ObservableObject? _panel;
    [ObservableProperty] private string _spellerStatus = "";
    [ObservableProperty] private object? _selectedNode;
    [ObservableProperty] private string _fileFilter = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _isoWarning;
    [ObservableProperty] private string? _editorError;
    [ObservableProperty] private string? _indexError;
    [ObservableProperty] private object? _selectedImageNode;
    [ObservableProperty] private string _imageStatus = "Lendo as imagens da ISO…";
    [ObservableProperty] private double _busyProgress;
    [ObservableProperty] private string _busyStep = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(ValidateCommand), nameof(BuildIsoCommand), nameof(ExportFontsCommand))]
    private bool _isBusy;

    public ProjectViewModel(TranslationProject project, IDialogService dialogs, Action close, AppSettings? settings = null)
    {
        Project = project;
        _dialogs = dialogs;
        _close = close;
        _settings = settings ?? AppSettings.Load();
        _directoryIndex = Project.LoadDirectoryActiveIndex();
        _imageControl = Project.LoadImageTranslationControl();
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
        _ = LoadImagesAsync();
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

    public ObservableCollection<DirectoryGroupViewModel> Groups { get; } = [];
    public ObservableCollection<IsoNodeViewModel> IsoTree { get; } = [];
    public ObservableCollection<DirectoryGroupViewModel> ImageGroups { get; } = [];
    public bool HasNoFiles => _files.Count == 0;

    partial void OnSelectedNodeChanged(object? value)
    {
        if (value is TbfFileItemViewModel file) OpenFile(file);
        else if (value is DirectoryGroupViewModel group) Panel = group;
    }

    partial void OnSelectedImageNodeChanged(object? value)
    {
        if (value is GameImageItemViewModel image) OpenImage(image);
        else if (value is DirectoryGroupViewModel group) Panel = group;
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

    [RelayCommand]
    private async Task ChooseImageReplacement(ImageViewerViewModel? viewer)
    {
        if (viewer is null) return;
        var path = await _dialogs.PickFileAsync("Escolha a imagem de substituição", "Imagem", "*.png", "*.gim");
        if (path is null) return;

        try
        {
            var data = await File.ReadAllBytesAsync(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension.Equals(".gim", StringComparison.OrdinalIgnoreCase))
                _ = GimImage.Read(data).ToRgba();
            else if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                _ = Png.Decode(data);
            else
                throw new InvalidDataException("A substituição deve ser PNG ou GIM.");
            var nameHash = ImageFileKey(viewer.Source.Path);
            var target = Path.Combine(Project.TranslationDirectory, "images", $"{nameHash}{extension}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
                await File.WriteAllBytesAsync(target, data);

            var entry = GetImageEntry(viewer.Source.Path);
            entry.Replacement = Path.GetRelativePath(Project.Root, target).Replace('\\', '/');
            ProjectIndexes.Save(Project.ImageTranslationControlPath, _imageControl);
            var selected = FindImage(viewer.Source.Path);
            if (selected is not null) OpenImage(selected);
        }
        catch (Exception error) when (error is IOException or InvalidDataException
                                          or NotSupportedException or UnauthorizedAccessException
                                          or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Substituir imagem", $"Não foi possível aplicar a imagem: {error.Message}");
        }
    }

    [RelayCommand]
    private void RemoveImageReplacement(ImageViewerViewModel? viewer)
    {
        if (viewer is null || !_imageControl.Images.TryGetValue(viewer.Source.Path, out var entry)) return;
        entry.Replacement = null;
        try
        {
            ProjectIndexes.Save(Project.ImageTranslationControlPath, _imageControl);
            IndexError = null;
            var selected = FindImage(viewer.Source.Path);
            if (selected is not null) OpenImage(selected);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            IndexError = $"Não foi possível salvar image-translation-control.json: {error.Message}";
        }
    }

    [RelayCommand]
    private Task OpenInLibreSprite(ImageViewerViewModel? viewer) =>
        OpenInEditor(viewer, "LibreSprite", ResolveLibreSpritePath);

    [RelayCommand]
    private Task OpenInGimp(ImageViewerViewModel? viewer) =>
        OpenInEditor(viewer, "GIMP", ResolveGimpPath);

    private async Task OpenInEditor(ImageViewerViewModel? viewer, string editorName, Func<Task<string?>> resolvePath)
    {
        if (viewer is null) return;
        try
        {
            var editorPath = await resolvePath();
            if (editorPath is null) return;
            var imagePath = EnsureEditablePng(viewer);

            var startInfo = new ProcessStartInfo
            {
                FileName = editorPath,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(imagePath);
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException($"O Windows não iniciou o {editorName}.");
            viewer.EditorStatus = editorName == "GIMP"
                ? "Aberto no GIMP. Edite, use Arquivo ▸ Sobrescrever e clique em “Atualizar prévia”."
                : "Aberto no LibreSprite. Salve a imagem e clique em “Atualizar prévia”.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException
                                          or InvalidOperationException or UnauthorizedAccessException
                                          or Win32Exception or ArgumentException)
        {
            await _dialogs.ShowMessageAsync(editorName, $"Não foi possível abrir a imagem: {error.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshImagePreview(ImageViewerViewModel? viewer)
    {
        if (viewer is null) return;
        try
        {
            if (!_imageControl.Images.TryGetValue(viewer.Source.Path, out var entry)
                || string.IsNullOrWhiteSpace(entry.Replacement))
                throw new InvalidDataException("Esta imagem ainda não tem um arquivo de trabalho.");

            var path = ResolveReplacement(entry.Replacement);
            var data = await File.ReadAllBytesAsync(path);
            viewer.SetReplacement(path, data);
            viewer.EditorStatus = $"Prévia atualizada de {Path.GetFileName(path)}.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException
                                          or NotSupportedException or UnauthorizedAccessException
                                          or ArgumentException)
        {
            viewer.EditorStatus = $"Não foi possível atualizar a prévia: {error.Message}";
        }
    }

    private string EnsureEditablePng(ImageViewerViewModel viewer)
    {
        var entry = GetImageEntry(viewer.Source.Path);
        var target = Path.Combine(Project.TranslationDirectory, "images",
            $"{ImageFileKey(viewer.Source.Path)}.png");
        byte[] png;
        if (!string.IsNullOrWhiteSpace(entry.Replacement))
        {
            var replacementPath = ResolveReplacement(entry.Replacement);
            var replacementData = File.ReadAllBytes(replacementPath);
            if (Path.GetExtension(replacementPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                _ = Png.Decode(replacementData);
                png = replacementData;
            }
            else
            {
                png = Png.Encode(GimImage.Read(replacementData).ToRgba());
            }
        }
        else
        {
            png = Png.Encode(viewer.Source.Pixels);
        }

        _ = Png.Decode(png);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, png);
        entry.Replacement = Path.GetRelativePath(Project.Root, target).Replace('\\', '/');
        ProjectIndexes.Save(Project.ImageTranslationControlPath, _imageControl);
        viewer.SetReplacement(target, png);
        return target;
    }

    private async Task<string?> ResolveLibreSpritePath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LibreSpritePath)
            && File.Exists(_settings.LibreSpritePath))
            return _settings.LibreSpritePath;

        var discovered = FindLibreSprite();
        if (discovered is not null)
        {
            _settings.LibreSpritePath = discovered;
            _settings.Save();
            return discovered;
        }

        var selected = await _dialogs.PickFileAsync(
            "Localize o executável do LibreSprite", "Executável", "*.exe");
        if (selected is null) return null;
        if (!File.Exists(selected))
            throw new FileNotFoundException("Executável do LibreSprite não encontrado.", selected);

        _settings.LibreSpritePath = selected;
        _settings.Save();
        return selected;
    }

    private async Task<string?> ResolveGimpPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.GimpPath) && File.Exists(_settings.GimpPath))
            return _settings.GimpPath;

        var discovered = FindGimp();
        if (discovered is not null)
        {
            _settings.GimpPath = discovered;
            _settings.Save();
            return discovered;
        }

        var selected = await _dialogs.PickFileAsync("Localize o executável do GIMP", "Executável", "*.exe");
        if (selected is null) return null;
        if (!File.Exists(selected))
            throw new FileNotFoundException("Executável do GIMP não encontrado.", selected);

        _settings.GimpPath = selected;
        _settings.Save();
        return selected;
    }

    /// <summary>Looks for the GUI executable (gimp-2.10.exe, gimp-3.exe…), skipping the console variant.</summary>
    private static string? FindGimp()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "GIMP*").OrderByDescending(d => d))
            {
                var bin = Path.Combine(dir, "bin");
                if (!Directory.Exists(bin)) continue;
                var exe = Directory.EnumerateFiles(bin, "gimp-*.exe")
                    .Where(f => !Path.GetFileName(f).Contains("console", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f)
                    .FirstOrDefault();
                if (exe is not null) return exe;
            }
        }
        return null;
    }

    private static string? FindLibreSprite()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreSprite", "LibreSprite.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreSprite", "LibreSprite.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LibreSprite", "LibreSprite.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreSprite", "LibreSprite.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ImageFileKey(string imagePath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imagePath))).ToLowerInvariant();

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

    private void OpenImage(GameImageItemViewModel image)
    {
        Editor?.Save();
        Glossary?.Save();
        var imageControlEntry = _imageControl.Images.GetValueOrDefault(image.Source.Path);
        var replacement = default(ImageSideViewModel);
        string? replacementError = null;
        if (!string.IsNullOrWhiteSpace(imageControlEntry?.Replacement))
        {
            try
            {
                var replacementPath = ResolveReplacement(imageControlEntry.Replacement);
                replacement = ImageSideViewModel.Replacement(replacementPath, File.ReadAllBytes(replacementPath));
            }
            catch (Exception error) when (error is IOException or InvalidDataException
                                              or NotSupportedException or UnauthorizedAccessException
                                              or ArgumentException)
            {
                replacementError = $"Não foi possível carregar a substituição: {error.Message}";
            }
        }

        var directoryDescription = _directoryIndex.Directories.GetValueOrDefault(image.Source.Group)?.Description ?? "";
        image.DirectoryDescription = directoryDescription;
        image.Description = imageControlEntry?.Description ?? "";
        Panel = new ImageViewerViewModel(image.Source, replacement, directoryDescription,
            image.Description, replacementError, description =>
            {
                var entry = GetImageEntry(image.Source.Path);
                entry.Description = description;
                image.Description = description;
                try
                {
                    ProjectIndexes.Save(Project.ImageTranslationControlPath, _imageControl);
                    IndexError = null;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    IndexError = $"Não foi possível salvar image-translation-control.json: {error.Message}";
                }
            });
        EditorError = null;
    }

    private void RebuildGroups()
    {
        var filter = FileFilter.Trim();
        Groups.Clear();
        foreach (var group in _files.Where(f => filter.Length == 0
                                                || f.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                                || f.Info.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                                || f.Info.DirectoryKey.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(f => string.IsNullOrWhiteSpace(f.Info.DirectoryKey) ? f.Info.Group : f.Info.DirectoryKey)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var defaultLabel = group.First().Info.Group;
            var vm = CreateDirectoryGroup(group.Key, defaultLabel);
            foreach (var file in group)
            {
                file.DirectoryDescription = vm.Description;
                vm.Items.Add(file);
            }
            Groups.Add(vm);
        }
    }

    private DirectoryGroupViewModel CreateDirectoryGroup(string key, string defaultLabel)
    {
        var entry = _directoryIndex.Directories.GetValueOrDefault(key) ?? new DirectoryActiveEntry();
        return new DirectoryGroupViewModel(key, defaultLabel, entry.Label, entry.Description, SaveDirectoryGroup);
    }

    private void SaveDirectoryGroup(DirectoryGroupViewModel group)
    {
        var entry = _directoryIndex.Directories.GetValueOrDefault(group.DirectoryKey);
        if (entry is null)
        {
            entry = new DirectoryActiveEntry();
            _directoryIndex.Directories[group.DirectoryKey] = entry;
        }
        entry.Label = group.Label;
        entry.Description = group.Description;
        foreach (var file in _files.Where(file => string.Equals(
                     string.IsNullOrWhiteSpace(file.Info.DirectoryKey) ? file.Info.Group : file.Info.DirectoryKey,
                     group.DirectoryKey, StringComparison.OrdinalIgnoreCase)))
            file.DirectoryDescription = group.Description;
        foreach (var image in ImageGroups.SelectMany(item => item.Items).OfType<GameImageItemViewModel>()
                     .Where(image => string.Equals(image.Source.Group, group.DirectoryKey, StringComparison.OrdinalIgnoreCase)))
            image.DirectoryDescription = group.Description;
        try
        {
            ProjectIndexes.Save(Project.DirectoryActiveIndexPath, _directoryIndex);
            IndexError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            IndexError = $"Não foi possível salvar directory-active-index.json: {error.Message}";
        }
    }

    private ImageTranslationEntry GetImageEntry(string key)
    {
        if (_imageControl.Images.TryGetValue(key, out var entry)) return entry;
        entry = new ImageTranslationEntry();
        _imageControl.Images.Add(key, entry);
        return entry;
    }

    private string ResolveReplacement(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("O caminho de substituição deve ser relativo à pasta do projeto.");
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(Project.Root, relativePath));
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Caminho de substituição inválido: {error.Message}", error);
        }
        var root = Path.GetFullPath(Project.Root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O caminho da substituição precisa permanecer dentro da pasta do projeto.");
        return fullPath;
    }

    private GameImageItemViewModel? FindImage(string path) =>
        ImageGroups.SelectMany(group => group.Items).OfType<GameImageItemViewModel>()
            .FirstOrDefault(image => string.Equals(image.Source.Path, path, StringComparison.OrdinalIgnoreCase));

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

    private async Task LoadImagesAsync()
    {
        if (IsoWarning is not null)
        {
            ImageStatus = "A ISO do projeto não está disponível.";
            return;
        }

        try
        {
            var images = await Task.Run(() =>
            {
                using var iso = Project.OpenIso();
                return Project.Engine.DiscoverImages(iso);
            });
            ImageGroups.Clear();
            foreach (var group in images.GroupBy(image => image.Group)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var node = CreateDirectoryGroup(group.Key, Path.GetFileName(group.Key));
                foreach (var source in group.OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new GameImageItemViewModel(source)
                    {
                        DirectoryDescription = node.Description,
                        Description = _imageControl.Images.GetValueOrDefault(source.Path)?.Description ?? "",
                    };
                    node.Items.Add(item);
                }
                ImageGroups.Add(node);
            }
            ImageStatus = images.Count == 0
                ? "Nenhuma imagem GIM compatível foi encontrada no catálogo do jogo."
                : $"{images.Count} imagem(ns) GIM · selecione uma para comparar.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException
                                          or InvalidOperationException or NotSupportedException)
        {
            ImageStatus = $"Não foi possível carregar as imagens: {error.Message}";
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
