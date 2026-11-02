using Avalonia.Threading;
using GameTranslationEngine.Core.Editing;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.App.Services;

/// <summary>
/// What every message editor of a project shares: the glossary (B2), the
/// spell checker (B3) and the analyzer that marks text for the highlighting (B4).
/// </summary>
public sealed class EditorServices
{
    private readonly TranslationProject _project;

    public EditorServices(TranslationProject project)
    {
        _project = project;
        try
        {
            Glossary = Glossary.Load(project.GlossaryPath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Glossary = new Glossary();
            GlossaryError = $"Glossário não carregado ({project.GlossaryPath}): {error.Message}";
        }
        Analyzer = new MessageAnalyzer(project.Engine.CommandNames, Glossary, null);
        _ = LoadSpellerAsync();
    }

    public Glossary Glossary { get; }
    public string? GlossaryError { get; }
    public SpellChecker? Speller { get; private set; }
    public string SpellerStatus { get; private set; } = "Carregando o dicionário pt-BR…";
    public MessageAnalyzer Analyzer { get; private set; }

    /// <summary>B5 · Text of the global search, marked in the editors ("" = none).</summary>
    public string SearchQuery { get; private set; } = "";
    public SearchScope SearchScope { get; private set; }
    public bool SearchMatchCase { get; private set; }
    public System.Text.RegularExpressions.Regex? SearchPattern { get; private set; }

    public void SetSearch(string query, SearchScope scope, bool matchCase)
    {
        if (query == SearchQuery && scope == SearchScope && matchCase == SearchMatchCase) return;
        (SearchQuery, SearchScope, SearchMatchCase) = (query, scope, matchCase);
        SearchPattern = TranslationSearch.CreatePattern(query, matchCase);
        Changed?.Invoke();
    }

    /// <summary>Raised on the UI thread when the marks may have changed (glossary edited, word added, dictionary loaded).</summary>
    public event Action? Changed;

    /// <summary>"Add to glossary" from an editor: the text, and whether it came from the translation.</summary>
    public event Action<string, bool>? AddTermRequested;

    public void RequestAddTerm(string text, bool fromTranslation) => AddTermRequested?.Invoke(text, fromTranslation);

    public void SaveGlossary()
    {
        Glossary.Save(_project.GlossaryPath);
        GlossaryChanged();
    }

    /// <summary>Call after editing the terms, so the editors mark the new ones.</summary>
    public void GlossaryChanged()
    {
        Glossary.Invalidate();
        Speller?.SetGlossary(Glossary);
        Changed?.Invoke();
    }

    public void AddWord(string word)
    {
        if (Speller is null) return;
        try
        {
            Speller.AddWord(word);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SpellerStatus = $"Não foi possível gravar {_project.DictionaryPath}: {error.Message}";
        }
        Changed?.Invoke();
    }

    private async Task LoadSpellerAsync()
    {
        try
        {
            var speller = await SpellChecker.LoadPortugueseAsync(_project.DictionaryPath);
            speller.SetGlossary(Glossary);
            Dispatcher.UIThread.Post(() =>
            {
                Speller = speller;
                SpellerStatus = "Ortografia: pt-BR";
                Analyzer = new MessageAnalyzer(_project.Engine.CommandNames, Glossary, speller);
                Changed?.Invoke();
            });
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SpellerStatus = $"Ortografia indisponível: {error.Message}";
                Changed?.Invoke();
            });
        }
    }
}
