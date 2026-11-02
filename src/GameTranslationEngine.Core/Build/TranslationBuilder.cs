using System.Collections.Concurrent;
using System.Diagnostics;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Eboot;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Fonts;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Build;

/// <summary>Outcome of a validation or build.</summary>
public sealed record BuildReport(
    IReadOnlyList<BuildIssue> Issues,
    int TbfFiles,
    int TranslatedMessages,
    int ChangedMessages,
    int ChangedScripts,
    IReadOnlyList<string> RebuiltEntries,
    string? OutputPath,
    TimeSpan Elapsed)
{
    public int Errors => Issues.Count(i => i.Level == IssueLevel.Error);
    public int Warnings => Issues.Count(i => i.Level == IssueLevel.Warning);
    public bool Succeeded => Errors == 0;
}

/// <summary>
/// C1–C9 · From the project's TBF files to a translated ISO. The original ISO
/// is only read; everything is assembled in memory except the final ISO.
/// </summary>
public sealed class TranslationBuilder : GameTranslationEngine.Core.Engines.IProjectCompiler
{
    public const string EbootPath = GameBuildSpec.DefaultEbootPath;

    private readonly string _isoPath;
    private readonly string _messagesDirectory;
    private readonly GameBuildSpec _game;
    private readonly string? _imageControlPath;
    private readonly string? _projectRoot;
    private readonly MessageContext _isoContext;
    private readonly MessageContext _context;

    /// <summary>Innocent Sin. <paramref name="font"/>: profile to install; null keeps the game's font and character table.</summary>
    public TranslationBuilder(string isoPath, string messagesDirectory, FontProfile? font)
        : this(isoPath, messagesDirectory, GameBuildSpec.InnocentSin(font))
    {
    }

    public TranslationBuilder(string isoPath, string messagesDirectory, GameBuildSpec game)
        : this(isoPath, messagesDirectory, game, null, null)
    {
    }

    public TranslationBuilder(string isoPath, string messagesDirectory, GameBuildSpec game,
        string? imageControlPath, string? projectRoot)
    {
        _isoPath = isoPath;
        _messagesDirectory = messagesDirectory;
        _game = game;
        _imageControlPath = imageControlPath;
        _projectRoot = projectRoot;
        _isoContext = game.IsoContext;
        _context = game.Context;
    }

    /// <summary>C1/C2 only: checks every translation without writing anything.</summary>
    public BuildReport Validate(IProgress<(string Step, double Fraction)>? progress = null, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        using var iso = IsoIndex.Read(new FileByteSource(_isoPath));
        var issues = new List<BuildIssue>();
        var imagePlan = PrepareImageReplacements(iso, issues, cancellation);
        var (compiled, files) = Compile(iso, issues, progress, cancellation);
        return Report(issues, files, compiled, imagePlan?.Entries.Values.Select(e => e.Name).ToList() ?? [], null,
            watch.Elapsed);
    }

    /// <summary>C1–C9: compiles the translation and writes the ISO to <paramref name="output"/>.</summary>
    public BuildReport Build(string output, IProgress<(string Step, double Fraction)>? progress = null,
        CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        if (Path.GetFullPath(output).Equals(Path.GetFullPath(_isoPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A ISO gerada não pode substituir a ISO original.");

        using var iso = IsoIndex.Read(new FileByteSource(_isoPath));
        var issues = new List<BuildIssue>();
        var imagePlan = PrepareImageReplacements(iso, issues, cancellation);
        var (compiled, files) = Compile(iso, issues, progress, cancellation);
        if (issues.Any(i => i.Level == IssueLevel.Error) || imagePlan is null)
            return Report(issues, files, compiled, [], null, watch.Elapsed);

        // C3/C5 · scripts and containers, one CPK entry per task.
        var catalog = _game.Catalog;
        var cpk = CpkIndex.Read(iso.Open(catalog.CpkPath));
        var changed = compiled.Where(c => c.HasChanges).ToList();
        var rebuilt = new ConcurrentBag<(RebuiltEntry Entry, IReadOnlyList<EntryLayout>? OriginalLayout)>();
        var rebuilder = new ContainerRebuilder(_isoContext);
        var groups = changed.GroupBy(c => c.Source.Source.Entry).ToList();
        var done = 0;
        progress?.Report(("Remontando os containers", 0));
        Parallel.ForEach(groups, new ParallelOptions { CancellationToken = cancellation }, group =>
        {
            var cpkEntry = FindEntry(cpk, group.Key);
            var originalContent = cpk.ReadEntry(cpkEntry);
            var content = imagePlan!.Entries.TryGetValue(cpkEntry.Id, out var imageEntry)
                ? imageEntry.Data
                : originalContent;
            var entry = rebuilder.Rebuild(group.Key, content, group.ToList());
            rebuilt.Add((entry, OriginalLayout(group.Key, originalContent)));
            progress?.Report(("Remontando os containers", (double)Interlocked.Increment(ref done) / groups.Count));
        });

        // C4 · the font pages.
        var font = _game.Font;
        var replacements = imagePlan!.Entries.ToDictionary(pair => pair.Key, pair => pair.Value.Data);
        foreach (var item in rebuilt)
            replacements[FindEntry(cpk, item.Entry.Entry).Id] = item.Entry.Data;
        byte[]? fontPages = null;
        if (font is not null)
        {
            progress?.Report(("Gerando a fonte", 0));
            var entry = cpk.FindByName(font.PagesFile)
                        ?? throw new InvalidDataException($"{font.PagesFile} não encontrado no CPK.");
            fontPages = font.BuildPages(cpk.ReadEntry(entry));
            replacements[entry.Id] = fontPages;
        }

        // C6 · the CPK.
        var cpkPlan = CpkRebuild.Plan(cpk, replacements);

        // C7 · EBOOT: container tables, sizes the code loads as immediates, font. The
        // font may make the EBOOT grow (EP), so the ISO placement comes after it.
        var cpkFile = iso.Get(catalog.CpkPath);
        var elf = ElfImage.Parse(ReadExecutable(iso));
        var patcher = new EbootPatcher(elf);
        var tables = _game.Tables;
        var rebuiltById = rebuilt.ToDictionary(r => FindEntry(cpk, r.Entry.Entry).Id);
        var modifiedContainerIds = rebuiltById.Keys.Concat(imagePlan.Entries.Keys).Distinct().ToList();
        foreach (var id in modifiedContainerIds)
        {
            var cpkEntry = cpk.FindById(id) ?? throw new InvalidDataException($"Entrada {id} não existe no CPK.");
            var hasTextChanges = rebuiltById.TryGetValue(id, out var textBuild);
            var imageBuild = imagePlan.Entries.GetValueOrDefault(id);
            var originalLayout = hasTextChanges ? textBuild.OriginalLayout : imageBuild?.OriginalLayout;
            var newLayout = hasTextChanges ? textBuild.Entry.Layout : imageBuild?.NewLayout;
            foreach (var table in tables.Where(t => t.File.Equals(cpkEntry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (originalLayout is null || newLayout is null)
                    throw new InvalidDataException($"Não foi possível determinar o layout de {cpkEntry.Name}.");
                patcher.UpdateTable(table, originalLayout, newLayout);
            }
            foreach (var (file, addresses) in _game.SizeInstructions.Where(s => s.File.Equals(cpkEntry.Name, StringComparison.OrdinalIgnoreCase)))
                patcher.UpdateImmediate(addresses, Align16(checked((int)cpkEntry.ExtractedSize)),
                    Align16(replacements[id].Length), file);
        }
        if (font is not null) elf = font.PatchExecutable(elf);
        patcher = new EbootPatcher(elf);

        // C8 · placement in the ISO.
        var placement = IsoRebuild.Plan(iso, [(catalog.CpkPath, cpkPlan.Length), (_game.EbootPath, elf.Data.Length)]);
        var cpkPlacement = placement[catalog.CpkPath];
        if (cpkPlacement.Lba != cpkFile.Lba || cpkPlacement.Length != cpkFile.Size)
            patcher.UpdateDiscLocation(cpkFile.Lba, cpkFile.Size, cpkPlacement.Lba, (uint)cpkPlacement.Length);

        progress?.Report(("Gravando a ISO", 0));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        placement.Write(output,
        [
            new IsoReplacement(catalog.CpkPath, cpkPlan.Length, stream => cpkPlan.WriteTo(stream, cancellation: cancellation)),
            new IsoReplacement(_game.EbootPath, elf.Data.Length, stream => stream.Write(elf.Data)),
        ], new Progress<double>(f => progress?.Report(("Gravando a ISO", f))), cancellation);
        foreach (var (path, newName) in _game.HiddenDirectories) IsoHide.RenameDirectory(output, path, newName);

        // C9 · read the new ISO back and check it.
        progress?.Report(("Conferindo a ISO gerada", 0));
        Verify(output, compiled, rebuilt.Select(r => r.Entry).ToList(), replacements, tables, cpkPlacement, issues,
            cancellation);
        if (font is not null) VerifyFont(output, font, fontPages!, issues);

        var names = modifiedContainerIds.Select(id => cpk.FindById(id)!.Name)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        return Report(issues, files, compiled, names, issues.Any(i => i.Level == IssueLevel.Error) ? null : output, watch.Elapsed);
    }

    private (List<CompiledText> Compiled, int Files) Compile(IsoIndex iso, List<BuildIssue> issues,
        IProgress<(string Step, double Fraction)>? progress, CancellationToken cancellation)
    {
        progress?.Report(("Lendo os textos da ISO", 0));
        var texts = new TextExtractor(iso, _game.Catalog, _isoContext)
            .ExtractAll(new Progress<double>(f => progress?.Report(("Lendo os textos da ISO", f))), cancellation);
        var documents = LoadDocuments(issues, out var files);

        progress?.Report(("Validando e codificando a tradução", 0));
        var compiler = new MessageCompiler(_context);
        var compiled = new List<CompiledText>(texts.Count);
        foreach (var text in texts)
        {
            cancellation.ThrowIfCancellationRequested();
            documents.TryGetValue(TranslationArea.Key(text.OriginalDirectory, text.OriginalFilename), out var document);
            compiled.Add(compiler.Compile(text, document, issues));
        }
        return (compiled, files);
    }

    private ImageReplacementPlan? PrepareImageReplacements(
        IsoIndex iso,
        List<BuildIssue> issues,
        CancellationToken cancellation)
    {
        if (_imageControlPath is null && _projectRoot is null)
            return new ImageReplacementPlan(new Dictionary<int, RebuiltImageEntry>(), []);
        if (_imageControlPath is null || _projectRoot is null)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, _imageControlPath ?? "image-translation-control.json", null,
                "O caminho do índice ou da pasta do projeto não foi configurado."));
            return null;
        }
        if (!File.Exists(_imageControlPath))
        {
            issues.Add(new BuildIssue(IssueLevel.Error, _imageControlPath, null,
                "Índice de substituições de imagem não encontrado."));
            return null;
        }

        try
        {
            var control = ProjectIndexes.LoadImages(_imageControlPath);
            if (!control.Images.Values.Any(entry => !string.IsNullOrWhiteSpace(entry.Replacement)))
                return new ImageReplacementPlan(new Dictionary<int, RebuiltImageEntry>(), []);

            var cpk = CpkIndex.Read(iso.Open(_game.Catalog.CpkPath));
            return ImageReplacementBuilder.Build(iso, cpk, _game.Catalog.CpkPath, _projectRoot, control,
                cancellation);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException
                                          or UnauthorizedAccessException or ArgumentException)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, _imageControlPath, null,
                $"Substituição de imagem inválida: {error.Message}"));
            return null;
        }
    }

    /// <summary>The project's message TBF files, by the original location in their header.</summary>
    private Dictionary<string, TbfDocument> LoadDocuments(List<BuildIssue> issues, out int files)
    {
        var documents = new Dictionary<string, TbfDocument>(StringComparer.OrdinalIgnoreCase);
        files = 0;
        if (!Directory.Exists(_messagesDirectory)) return documents;
        foreach (var path in Directory.EnumerateFiles(_messagesDirectory, "*.tbf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(_messagesDirectory, path).Replace('\\', '/');
            TbfDocument document;
            try
            {
                document = TbfDocument.Load(path);
            }
            catch (InvalidDataException error)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, relative, null, $"TBF inválido: {error.Message}"));
                continue;
            }
            files++;
            if (document.IsScript)
            {
                if (document.TranslatedCount > 0)
                    issues.Add(new BuildIssue(IssueLevel.Warning, relative, null,
                        "TBF de script (.ef) da v2: o texto dele não é usado; traduza o TBF de mensagens correspondente."));
                continue;
            }
            var key = TranslationArea.Key(document.OriginalDirectory, document.OriginalFilename);
            if (!documents.TryAdd(key, document))
                issues.Add(new BuildIssue(IssueLevel.Warning, relative, null, "outro TBF já traduz o mesmo script; este foi ignorado."));
        }
        return documents;
    }

    private static CpkEntry FindEntry(CpkIndex cpk, SourceStep step) =>
        (step.CpkId is { } id ? cpk.FindById(id) : null) ?? cpk.FindByName(step.Name)
        ?? throw new InvalidDataException($"Entrada {step.Name} não encontrada no CPK.");

    private static IReadOnlyList<EntryLayout>? OriginalLayout(SourceStep step, ReadOnlyMemory<byte> content) => step.Type switch
    {
        "ear" => Ear.Split(content).Select(e => new EntryLayout(e.Offset, e.Data.Length)).ToList(),
        "bnp" or "pdemo" => Bnp.Split(content).Select(e => new EntryLayout(e.Offset, e.Data.Length)).ToList(),
        _ => null,
    };

    private static int Align16(int value) => (value + 15) & ~15;

    private byte[] ReadExecutable(IsoIndex iso)
    {
        var data = iso.Open(_game.EbootPath).ReadAll();
        return PrxDecrypter.IsEncrypted(data) ? PrxDecrypter.Decrypt(data) : data;
    }

    private void Verify(string output, IReadOnlyList<CompiledText> compiled, IReadOnlyList<RebuiltEntry> rebuilt,
        IReadOnlyDictionary<int, byte[]> replacements, IReadOnlyList<EbootTable> tables, IsoPlacement cpkPlacement,
        List<BuildIssue> issues, CancellationToken cancellation)
    {
        using var iso = IsoIndex.Read(new FileByteSource(output));
        var texts = new TextExtractor(iso, _game.Catalog, _context).ExtractAll(cancellation: cancellation);
        for (var i = 0; i < compiled.Count; i++)
        {
            var expected = compiled[i];
            var found = texts[i];
            var same = found.OriginalBytes.Count == expected.Messages.Count
                       && found.OriginalBytes.Zip(expected.Messages).All(p => p.First.AsSpan().SequenceEqual(p.Second));
            if (!same)
                issues.Add(new BuildIssue(IssueLevel.Error, expected.Source.Source.TbfPath, null,
                    "a ISO gerada não contém as mensagens compiladas."));
        }

        var elf = ElfImage.Parse(iso.Open(_game.EbootPath).ReadAll());
        var patcher = new EbootPatcher(elf);
        var catalog = _game.Catalog;
        var cpk = CpkIndex.Read(iso.Open(catalog.CpkPath));
        foreach (var (id, expected) in replacements)
        {
            var entry = cpk.FindById(id);
            if (entry is null || !cpk.ReadEntry(entry).AsSpan().SequenceEqual(expected))
                issues.Add(new BuildIssue(IssueLevel.Error, entry?.FullName ?? id.ToString(), null,
                    "a ISO gerada não contém a substituição de imagem/arquivo esperada."));
        }
        foreach (var entry in rebuilt)
        {
            var content = cpk.ReadEntry(FindEntry(cpk, entry.Entry));
            if (!content.AsSpan().SequenceEqual(entry.Data))
                issues.Add(new BuildIssue(IssueLevel.Error, entry.Entry.Name, null, "conteúdo diferente do remontado."));
            foreach (var table in tables.Where(t => t.File.Equals(entry.Entry.Name, StringComparison.OrdinalIgnoreCase)))
                if (!patcher.Matches(table, OriginalLayout(entry.Entry, content)!))
                    issues.Add(new BuildIssue(IssueLevel.Error, entry.Entry.Name, null, "a tabela do EBOOT não corresponde ao container gerado."));
        }
        var cpkFile = iso.Get(catalog.CpkPath);
        if (cpkFile.Lba != cpkPlacement.Lba || cpkFile.Size != cpkPlacement.Length)
            issues.Add(new BuildIssue(IssueLevel.Error, catalog.CpkPath, null, "posição do CPK na ISO diferente da planejada."));
        if (elf.FindInSections(System.Text.Encoding.ASCII.GetBytes(EbootPatcher.DiscPath(cpkFile.Lba, cpkFile.Size) + "\0")).Count == 0)
            issues.Add(new BuildIssue(IssueLevel.Error, _game.EbootPath, null, "o EBOOT não aponta para a posição do CPK na ISO gerada."));
        foreach (var (path, _) in _game.HiddenDirectories)
            if (iso.Find(path) is not null)
                issues.Add(new BuildIssue(IssueLevel.Error, path, null, "a pasta deveria estar escondida na ISO gerada."));
    }

    private void VerifyFont(string output, IBuildFont font, byte[] pages, List<BuildIssue> issues)
    {
        using var iso = IsoIndex.Read(new FileByteSource(output));
        var cpk = CpkIndex.Read(iso.Open(_game.Catalog.CpkPath));
        if (!cpk.ReadEntry(cpk.FindByName(font.PagesFile)!).AsSpan().SequenceEqual(pages))
            issues.Add(new BuildIssue(IssueLevel.Error, font.PagesFile, null, "as páginas da fonte na ISO gerada não são as esperadas."));
        if (!font.IsInstalled(ElfImage.Parse(iso.Open(_game.EbootPath).ReadAll())))
            issues.Add(new BuildIssue(IssueLevel.Error, _game.EbootPath, null, "a fonte não foi aplicada ao EBOOT."));
    }

    private static BuildReport Report(List<BuildIssue> issues, int files, IReadOnlyList<CompiledText> compiled,
        IReadOnlyList<string> rebuilt, string? output, TimeSpan elapsed) => new(
        issues.OrderBy(i => i.Level).ThenBy(i => i.File, StringComparer.OrdinalIgnoreCase).ToList(),
        files,
        compiled.Sum(c => c.Translated),
        compiled.Sum(c => c.Changed),
        compiled.Count(c => c.HasChanges),
        rebuilt,
        output,
        elapsed);
}
