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
    public const string EbootPath = "PSP_GAME/SYSDIR/EBOOT.BIN";

    private readonly string _isoPath;
    private readonly string _messagesDirectory;
    private readonly FontProfile? _font;
    /// <summary>Reads the original ISO (the game's own character table).</summary>
    private readonly MessageContext _isoContext;
    /// <summary>Encodes the translation (the font profile's character table).</summary>
    private readonly MessageContext _context;

    /// <param name="font">Font profile to install; null keeps the game's font and character table.</param>
    public TranslationBuilder(string isoPath, string messagesDirectory, FontProfile? font)
    {
        _isoPath = isoPath;
        _messagesDirectory = messagesDirectory;
        _font = font;
        var constants = ConstantTables.LoadInnocentSin();
        _isoContext = new MessageContext(EventEncoding.LoadInnocentSinEnglish(), constants);
        _context = font is null ? _isoContext : new MessageContext(font.Encoding, constants);
    }

    public FontProfile? Font => _font;

    /// <summary>C1/C2 only: checks every translation without writing anything.</summary>
    public BuildReport Validate(IProgress<(string Step, double Fraction)>? progress = null, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        using var iso = IsoIndex.Read(new FileByteSource(_isoPath));
        var issues = new List<BuildIssue>();
        var (compiled, files) = Compile(iso, issues, progress, cancellation);
        return Report(issues, files, compiled, [], null, watch.Elapsed);
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
        var (compiled, files) = Compile(iso, issues, progress, cancellation);
        if (issues.Any(i => i.Level == IssueLevel.Error))
            return Report(issues, files, compiled, [], null, watch.Elapsed);

        // C3/C5 · scripts and containers, one CPK entry per task.
        var catalog = TextSourceCatalog.LoadInnocentSin();
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
            var content = cpk.ReadEntry(cpkEntry);
            var entry = rebuilder.Rebuild(group.Key, content, group.ToList());
            rebuilt.Add((entry, OriginalLayout(group.Key, content)));
            progress?.Report(("Remontando os containers", (double)Interlocked.Increment(ref done) / groups.Count));
        });

        // C4 · the font pages.
        var fontTables = FontInstaller.Tables.LoadInnocentSin();
        var replacements = rebuilt.ToDictionary(r => FindEntry(cpk, r.Entry.Entry).Id, r => r.Entry.Data);
        IReadOnlyDictionary<int, RgbaImage>? pages = null;
        byte[]? syscg = null;
        if (_font is not null)
        {
            progress?.Report(("Gerando a fonte", 0));
            var entry = cpk.FindByName(fontTables.PagesFile)
                        ?? throw new InvalidDataException($"{fontTables.PagesFile} não encontrado no CPK.");
            (syscg, pages) = FontInstaller.BuildPages(cpk.ReadEntry(entry), _font);
            replacements[entry.Id] = syscg;
        }

        // C6 · the CPK.
        var cpkPlan = CpkRebuild.Plan(cpk, replacements);

        // C7/C8 · EBOOT and placement in the ISO.
        var cpkFile = iso.Get(catalog.CpkPath);
        var elf = ElfImage.Parse(ReadExecutable(iso));
        var placement = IsoRebuild.Plan(iso, [(catalog.CpkPath, cpkPlan.Length), (EbootPath, elf.Data.Length)]);
        var patcher = new EbootPatcher(elf);
        var tables = EbootTable.LoadInnocentSin();
        foreach (var (entry, originalLayout) in rebuilt)
        {
            foreach (var table in tables.Where(t => t.File.Equals(entry.Entry.Name, StringComparison.OrdinalIgnoreCase)))
                patcher.UpdateTable(table, originalLayout!, entry.Layout!);
        }
        if (_font is not null) FontInstaller.PatchEboot(elf, _font, pages!, fontTables);
        var cpkPlacement = placement[catalog.CpkPath];
        if (cpkPlacement.Lba != cpkFile.Lba || cpkPlacement.Length != cpkFile.Size)
            patcher.UpdateDiscLocation(cpkFile.Lba, cpkFile.Size, cpkPlacement.Lba, (uint)cpkPlacement.Length);

        progress?.Report(("Gravando a ISO", 0));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        placement.Write(output,
        [
            new IsoReplacement(catalog.CpkPath, cpkPlan.Length, stream => cpkPlan.WriteTo(stream, cancellation: cancellation)),
            new IsoReplacement(EbootPath, elf.Data.Length, stream => stream.Write(elf.Data)),
        ], new Progress<double>(f => progress?.Report(("Gravando a ISO", f))), cancellation);

        // C9 · read the new ISO back and check it.
        progress?.Report(("Conferindo a ISO gerada", 0));
        Verify(output, compiled, rebuilt.Select(r => r.Entry).ToList(), tables, cpkPlacement, issues, cancellation);
        if (_font is not null) VerifyFont(output, syscg!, fontTables, issues);

        var names = rebuilt.Select(r => r.Entry.Entry.Name).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return Report(issues, files, compiled, names, issues.Any(i => i.Level == IssueLevel.Error) ? null : output, watch.Elapsed);
    }

    private (List<CompiledText> Compiled, int Files) Compile(IsoIndex iso, List<BuildIssue> issues,
        IProgress<(string Step, double Fraction)>? progress, CancellationToken cancellation)
    {
        progress?.Report(("Lendo os textos da ISO", 0));
        var texts = new TextExtractor(iso, context: _isoContext)
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

    private static byte[] ReadExecutable(IsoIndex iso)
    {
        var data = iso.Open(EbootPath).ReadAll();
        return PrxDecrypter.IsEncrypted(data) ? PrxDecrypter.Decrypt(data) : data;
    }

    private void Verify(string output, IReadOnlyList<CompiledText> compiled, IReadOnlyList<RebuiltEntry> rebuilt,
        IReadOnlyList<EbootTable> tables, IsoPlacement cpkPlacement, List<BuildIssue> issues, CancellationToken cancellation)
    {
        using var iso = IsoIndex.Read(new FileByteSource(output));
        var texts = new TextExtractor(iso, context: _context).ExtractAll(cancellation: cancellation);
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

        var elf = ElfImage.Parse(iso.Open(EbootPath).ReadAll());
        var patcher = new EbootPatcher(elf);
        var catalog = TextSourceCatalog.LoadInnocentSin();
        var cpk = CpkIndex.Read(iso.Open(catalog.CpkPath));
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
            issues.Add(new BuildIssue(IssueLevel.Error, EbootPath, null, "o EBOOT não aponta para a posição do CPK na ISO gerada."));
    }

    private void VerifyFont(string output, byte[] syscg, FontInstaller.Tables tables, List<BuildIssue> issues)
    {
        using var iso = IsoIndex.Read(new FileByteSource(output));
        var cpk = CpkIndex.Read(iso.Open(TextSourceCatalog.LoadInnocentSin().CpkPath));
        if (!cpk.ReadEntry(cpk.FindByName(tables.PagesFile)!).AsSpan().SequenceEqual(syscg))
            issues.Add(new BuildIssue(IssueLevel.Error, tables.PagesFile, null, "as páginas da fonte na ISO gerada não são as esperadas."));
        if (!FontInstaller.IsInstalled(ElfImage.Parse(iso.Open(EbootPath).ReadAll()), _font!, tables))
            issues.Add(new BuildIssue(IssueLevel.Error, EbootPath, null, "a tabela de caracteres da fonte não foi aplicada ao EBOOT."));
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
