using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.App.ViewModels;

/// <summary>One message in the editor: original on the left, translation on the right.</summary>
public partial class MessageViewModel(TbfEntry entry, EditorServices services, Action changed) : ObservableObject
{
    public TbfEntry Entry { get; } = entry;
    public EditorServices Services { get; } = services;
    public string Key => Entry.Key;
    public string Original => Entry.Original;
    public string AfterMessage => Entry.AfterMessage;
    public bool HasAfterMessage => Entry.AfterMessage.Length > 0;
    public int Number => Entry.Index + 1;

    public string Translation
    {
        get => Entry.Translation;
        set
        {
            if (value == Entry.Translation) return;
            Entry.Translation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTranslated));
            OnPropertyChanged(nameof(LengthInfo));
            RefreshGlossary();
            changed();
        }
    }

    public bool IsTranslated => Entry.IsTranslated;

    /// <summary>Longest line of each side: the game's text box has a fixed width.</summary>
    public string LengthInfo => $"maior linha: {LongestLine(Original)} → {LongestLine(Translation)}";

    /// <summary>B2 · Glossary terms of the original whose translation is not in the translated text.</summary>
    public string? GlossaryHint
    {
        get
        {
            var missing = Services.Glossary.Missing(Original, Translation);
            return missing.Count == 0
                ? null
                : "Glossário: " + string.Join(" · ", missing.Select(t => $"{t.Original} → {t.Translation}"));
        }
    }

    public void RefreshGlossary() => OnPropertyChanged(nameof(GlossaryHint));

    [RelayCommand]
    private void CopyOriginal() => Translation = Original;

    [RelayCommand]
    private void Clear() => Translation = "";

    private static int LongestLine(string text) =>
        text.Length == 0 ? 0 : text.Split('\n').Max(line => line.TrimEnd('\r').Length);
}

public enum MessageFilter { Todas, Pendentes, Traduzidas }

/// <summary>Editor for one TBF file.</summary>
public partial class TbfEditorViewModel : ObservableObject
{
    private readonly List<MessageViewModel> _all;

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private MessageFilter _filter = MessageFilter.Todas;
    [ObservableProperty] private string? _saveError;
    /// <summary>The message the list shows selected (and scrolls to).</summary>
    [ObservableProperty] private MessageViewModel? _selected;

    public TbfEditorViewModel(TbfFileInfo file, TbfDocument document, EditorServices services)
    {
        File = file;
        Document = document;
        Services = services;
        _all = document.Entries.Select(e => new MessageViewModel(e, services, OnChanged)).ToList();
        services.Changed += RefreshGlossary;
        ApplyFilter();
    }

    public EditorServices Services { get; }

    /// <summary>Stops listening to the project; called when another file replaces this one.</summary>
    public void Detach() => Services.Changed -= RefreshGlossary;

    private void RefreshGlossary()
    {
        foreach (var message in _all) message.RefreshGlossary();
    }

    public TbfFileInfo File { get; }
    public TbfDocument Document { get; }
    public ObservableCollection<MessageViewModel> Visible { get; } = [];
    public IReadOnlyList<MessageFilter> Filters { get; } = Enum.GetValues<MessageFilter>();

    public string Heading => $"{File.Group} · {File.Label}";

    /// <summary>Team notes of the scene (header.context), saved with the file.</summary>
    public string ContextLocation
    {
        get => Document.ContextLocation;
        set
        {
            if (value == Document.ContextLocation) return;
            Document.ContextLocation = value;
            OnPropertyChanged();
            IsDirty = true;
        }
    }

    public string ContextSummary
    {
        get => Document.ContextSummary;
        set
        {
            if (value == Document.ContextSummary) return;
            Document.ContextSummary = value;
            OnPropertyChanged();
            IsDirty = true;
        }
    }
    public string Location => $"{Document.OriginalDirectory}/{Document.OriginalFilename}";
    public int Total => _all.Count;
    public int TranslatedCount => _all.Count(m => m.IsTranslated);
    public string ProgressText => $"{TranslatedCount} de {Total} traduzidas";
    public double ProgressPercent => Total == 0 ? 0 : 100.0 * TranslatedCount / Total;
    public bool IsEmpty => Visible.Count == 0;

    /// <summary>Raised after saving so the file list can refresh its counters.</summary>
    public event Action<TbfEditorViewModel>? Saved;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterChanged(MessageFilter value) => ApplyFilter();

    [RelayCommand]
    public void Save()
    {
        if (!IsDirty) return;
        try
        {
            Document.Save(File.FullPath);
            IsDirty = false;
            SaveError = null;
            Saved?.Invoke(this);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = $"Não foi possível salvar: {error.Message}";
        }
    }

    /// <summary>Shows message <paramref name="index"/>: clears the filters and selects it.</summary>
    public void Reveal(int index)
    {
        if (index < 0 || index >= _all.Count) return;
        if (Filter != MessageFilter.Todas) Filter = MessageFilter.Todas;
        if (SearchText.Length > 0) SearchText = "";
        Selected = null;
        Selected = _all[index];
    }

    private void OnChanged()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercent));
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        Visible.Clear();
        foreach (var message in _all)
        {
            if (Filter == MessageFilter.Pendentes && message.IsTranslated) continue;
            if (Filter == MessageFilter.Traduzidas && !message.IsTranslated) continue;
            if (search.Length > 0
                && !message.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !message.Original.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !message.Translation.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            Visible.Add(message);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }
}
