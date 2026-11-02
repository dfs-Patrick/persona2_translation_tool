using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTranslationEngine.App.Services;
using GameTranslationEngine.Core.Editing;

namespace GameTranslationEngine.App.ViewModels;

/// <summary>One line of the glossary page.</summary>
public partial class GlossaryTermViewModel(GlossaryTerm term, Action changed, Action<GlossaryTermViewModel> remove) : ObservableObject
{
    public GlossaryTerm Term { get; } = term;

    public string Original
    {
        get => Term.Original;
        set => Set(Term.Original, value, v => Term.Original = v);
    }

    public string Translation
    {
        get => Term.Translation;
        set => Set(Term.Translation, value, v => Term.Translation = v);
    }

    public string Description
    {
        get => Term.Description;
        set => Set(Term.Description, value, v => Term.Description = v);
    }

    public bool MatchCase
    {
        get => Term.MatchCase;
        set => Set(Term.MatchCase, value, v => Term.MatchCase = v);
    }

    [RelayCommand]
    private void Remove() => remove(this);

    private void Set<T>(T current, T value, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        apply(value);
        OnPropertyChanged(name);
        changed();
    }
}

/// <summary>B2 · The glossary page: terms of reference for the whole translation.</summary>
public partial class GlossaryViewModel : ObservableObject
{
    private readonly EditorServices _services;
    private readonly string _path;
    private readonly List<GlossaryTermViewModel> _all;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _saveError;

    public GlossaryViewModel(EditorServices services, string path)
    {
        _services = services;
        _path = path;
        _all = services.Glossary.Terms.Select(Wrap).ToList();
        ApplyFilter();
    }

    public ObservableCollection<GlossaryTermViewModel> Visible { get; } = [];
    public string Summary => $"{_all.Count} termo(s) · {_path}";
    public string? LoadError => _services.GlossaryError;
    public bool IsEmpty => Visible.Count == 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>New term at the top, optionally filled from text selected in the editor.</summary>
    public GlossaryTermViewModel AddTerm(string original = "", string translation = "")
    {
        var term = new GlossaryTerm { Original = original, Translation = translation };
        _services.Glossary.Terms.Insert(0, term);
        var vm = Wrap(term);
        _all.Insert(0, vm);
        SearchText = "";
        ApplyFilter();
        OnChanged();
        return vm;
    }

    [RelayCommand]
    private void Add() => AddTerm();

    [RelayCommand]
    public void Save()
    {
        if (!IsDirty) return;
        try
        {
            _services.SaveGlossary();
            IsDirty = false;
            SaveError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = $"Não foi possível salvar o glossário: {error.Message}";
        }
    }

    private GlossaryTermViewModel Wrap(GlossaryTerm term) => new(term, OnChanged, Remove);

    private void Remove(GlossaryTermViewModel term)
    {
        _services.Glossary.Terms.Remove(term.Term);
        _all.Remove(term);
        Visible.Remove(term);
        OnChanged();
    }

    private void OnChanged()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        Visible.Clear();
        foreach (var term in _all.Where(t => search.Length == 0
                                             || t.Original.Contains(search, StringComparison.OrdinalIgnoreCase)
                                             || t.Translation.Contains(search, StringComparison.OrdinalIgnoreCase)
                                             || t.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
            Visible.Add(term);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
