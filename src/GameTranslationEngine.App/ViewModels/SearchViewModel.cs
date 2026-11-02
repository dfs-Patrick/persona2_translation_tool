using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.App.ViewModels;

public sealed record SearchScopeOption(SearchScope Scope, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A result line: where the text is, and the piece of the line around it.</summary>
public sealed class SearchHitViewModel(SearchHit hit)
{
    private const int Context = 28;

    public SearchHit Hit { get; } = hit;

    public string Location =>
        $"{Hit.File.Group} · {Hit.File.Label} · {Hit.Key} · {(Hit.InTranslation ? "tradução" : "original")}"
        + (Hit.Occurrences > 1 ? $" ({Hit.Occurrences}×)" : "");

    // The line of the first occurrence, cut around it; line breaks shown as "⏎".
    public string Before
    {
        get
        {
            var from = Math.Max(0, Hit.Start - Context);
            var text = Hit.Text[from..Hit.Start].Replace("\n", " ⏎ ");
            return from > 0 ? "…" + text : text;
        }
    }

    public string Match => Hit.Text.Substring(Hit.Start, Hit.Length).Replace("\n", " ⏎ ");

    public string After
    {
        get
        {
            var start = Hit.Start + Hit.Length;
            var to = Math.Min(Hit.Text.Length, start + Context);
            var text = Hit.Text[start..to].Replace("\n", " ⏎ ");
            return to < Hit.Text.Length ? text + "…" : text;
        }
    }
}

/// <summary>B5 · Search in every TBF of the project, in the original (before), the translation (after) or both.</summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TbfFileInfo>> _files;
    private readonly Action<SearchHit> _open;
    private readonly Action<string, SearchScope, bool> _highlight;
    private CancellationTokenSource? _pending;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SearchScopeOption _scope;
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private string _summary = "Digite ao menos 2 caracteres.";
    [ObservableProperty] private SearchHitViewModel? _selected;

    public SearchViewModel(Func<IReadOnlyList<TbfFileInfo>> files, Action<SearchHit> open, Action<string, SearchScope, bool> highlight)
    {
        _files = files;
        _open = open;
        _highlight = highlight;
        _scope = Scopes[0];
    }

    public IReadOnlyList<SearchScopeOption> Scopes { get; } =
    [
        new(SearchScope.Both, "Original e tradução"),
        new(SearchScope.Original, "Só o original (antes)"),
        new(SearchScope.Translation, "Só a tradução (depois)"),
    ];

    public ObservableCollection<SearchHitViewModel> Results { get; } = [];

    partial void OnQueryChanged(string value) => Schedule();
    partial void OnScopeChanged(SearchScopeOption value) => Schedule();
    partial void OnMatchCaseChanged(bool value) => Schedule();

    partial void OnSelectedChanged(SearchHitViewModel? value)
    {
        if (value is not null) _open(value.Hit);
    }

    /// <summary>Searches again (e.g. after the files changed).</summary>
    public void Refresh() => Schedule(delay: 0);

    // Typing restarts the wait: the search runs once the user pauses.
    private async void Schedule(int delay = 250)
    {
        _pending?.Cancel();
        var cancel = _pending = new CancellationTokenSource();
        var query = Query;
        var scope = Scope.Scope;
        var matchCase = MatchCase;
        _highlight(query.Trim().Length >= 2 ? query : "", scope, matchCase);
        if (query.Trim().Length < 2)
        {
            Results.Clear();
            Summary = "Digite ao menos 2 caracteres.";
            return;
        }
        try
        {
            await Task.Delay(delay, cancel.Token);
            var files = _files();
            var result = await Task.Run(() => TranslationSearch.Search(files, query, scope, matchCase, cancellation: cancel.Token), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            Results.Clear();
            foreach (var hit in result.Hits) Results.Add(new SearchHitViewModel(hit));
            Summary = result.TotalHits == 0
                ? "Nada encontrado."
                : $"{result.TotalHits} mensagem(ns) em {result.Files} arquivo(s)"
                  + (result.Truncated ? $"; mostrando as {result.Hits.Count} primeiras" : "")
                  + $" · {result.Elapsed.TotalMilliseconds:N0} ms";
        }
        catch (OperationCanceledException)
        {
        }
    }
}
