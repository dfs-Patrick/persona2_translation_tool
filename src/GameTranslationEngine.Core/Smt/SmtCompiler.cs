using GameTranslationEngine.Core.IO;
using System.Diagnostics;
using System.Text;
using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Core.Smt;

/// <summary>
/// Compiles the translations of the map scripts into DATA.BIN, in place: every
/// script keeps its size and position, so no table has to move. A raw .bin is
/// written with EDC/ECC recomputed on the touched sectors, plus a matching .cue.
/// </summary>
public sealed class SmtCompiler(TranslationProject project) : IProjectCompiler
{
    public BuildReport Validate(IProgress<(string Step, double Fraction)>? progress = null,
        CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        using var iso = project.OpenIso();
        var (patches, report) = Compile(iso, progress, cancellation);
        return report with { Elapsed = watch.Elapsed };
    }

    public BuildReport Build(string output, IProgress<(string Step, double Fraction)>? progress = null,
        CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        var bin = DiscImage.IsCue(output) ? Path.ChangeExtension(output, ".bin") : output;
        var cue = DiscImage.IsCue(output) ? output : Path.ChangeExtension(output, ".cue");
        if (Path.GetFullPath(bin).Equals(Path.GetFullPath(DiscImage.ResolveDataFile(project.IsoPath)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O disco gerado não pode substituir o original.");

        using var iso = project.OpenIso();
        var (patches, report) = Compile(iso, progress, cancellation);
        if (!report.Succeeded) return report with { Elapsed = watch.Elapsed };

        if (iso.Source is not RawSectorSource raw)
            throw new InvalidOperationException("O SMT precisa de um disco bruto (.cue/.bin).");
        RawDiscPatcher.Write(raw, bin, patches,
            new Progress<double>(f => progress?.Report(("Gravando o disco", f))), cancellation);
        File.WriteAllText(cue, $"FILE \"{Path.GetFileName(bin)}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
        return report with { OutputPath = bin, Elapsed = watch.Elapsed };
    }

    private static long DataStart(IsoIndex iso) => iso.Find(SmtDataArchive.DataPath)!.Offset;

    private const int Sector = 2048;
    private const int MaxScriptGrowth = 64 * Sector;

    private static byte[] RebuildGrowing(SmtScript script, byte[] bytes, IReadOnlyDictionary<int, ushort[]> replacements)
    {
        for (var extra = 0; ; extra += Sector)
        {
            var padded = new byte[bytes.Length + extra];
            bytes.CopyTo(padded, 0);
            try { return script.Rebuild(padded, replacements); }
            catch (InvalidOperationException) when (extra < MaxScriptGrowth) { }
        }
    }

    /// <summary>
    /// An entry holding several banks (u32 count, count + 1 offsets, then the banks):
    /// every bank is laid out again one after the other and the offsets are rewritten,
    /// so a translated bank may grow into the room of the next; the entry grows by
    /// whole sectors when needed.
    /// </summary>
    private static byte[] RebuildContainer(byte[] bytes, int index, IReadOnlyDictionary<SmtBankLocation, Dictionary<int, ushort[]>> replacements)
    {
        var locations = SmtStringBank.Locations.Where(l => l.Entry == index).OrderBy(l => l.Start).ToList();
        var count = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (count != locations.Count || locations[0].Start != 4 + 4 * (count + 1))
            throw new InvalidOperationException("o cabeçalho do contêiner de bancos não é o esperado.");
        var parts = new List<byte[]>();
        foreach (var location in locations)
        {
            var bank = SmtStringBank.Parse(bytes, location) ?? throw new InvalidOperationException("banco ilegível.");
            parts.Add(bank.Serialize(replacements.TryGetValue(location, out var r) ? r : new Dictionary<int, ushort[]>()));
        }
        var size = locations[0].Start + parts.Sum(p => p.Length);
        var result = new byte[Math.Max(bytes.Length, (size + Sector - 1) / Sector * Sector)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)count);
        var position = locations[0].Start;
        for (var i = 0; i < parts.Count; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + 4 * i), (uint)position);
            parts[i].CopyTo(result, position);
            position += parts[i].Length;
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + 4 * parts.Count), (uint)position);
        return result;
    }

    /// <summary>
    /// A bank that runs to the end of its entry may grow by whole sectors, like the
    /// scripts: the loader reads the size of every entry from the table, so the
    /// relayout below keeps it consistent. Banks with a fixed end cannot grow.
    /// </summary>
    private static byte[] RebuildBankGrowing(SmtStringBank bank, byte[] bytes, SmtBankLocation location,
        IReadOnlyDictionary<int, ushort[]> replacements)
    {
        for (var extra = 0; ; extra += Sector)
        {
            var padded = new byte[bytes.Length + extra];
            bytes.CopyTo(padded, 0);
            try { return bank.Rebuild(padded, location, replacements); }
            catch (InvalidOperationException) when (location.End < 0 && extra < MaxScriptGrowth) { }
        }
    }

    /// <summary>
    /// Entries that grew (translated scripts) push the following ones forward;
    /// the room comes from the all-zero sectors at the tail of the next entries,
    /// so DATA.BIN keeps its size. The table in the executable is rewritten.
    /// Returns every patch with an absolute offset inside the disc image.
    /// </summary>
    private static List<(long, byte[])> Relayout(SmtDataArchive archive, List<(long, byte[])> patches,
        IsoIndex iso, List<BuildIssue> issues)
    {
        var data = DataStart(iso);
        var byIndex = new Dictionary<int, byte[]>();
        foreach (var (offset, bytes) in patches)
            byIndex[archive.Entries.First(e => e.Offset == offset).Index] = bytes;

        var grown = byIndex.Where(p => p.Value.Length > archive.Entries[p.Key].Length).Select(p => p.Key).Order().ToList();
        if (grown.Count == 0)
            return [.. patches.Select(p => (data + p.Item1, p.Item2))];

        var need = grown.Sum(i => (byIndex[i].Length - archive.Entries[i].Length) / Sector);
        var first = grown[0];
        var lengths = archive.Entries.Select(e => (int)e.Length).ToArray();
        foreach (var i in grown) lengths[i] = byIndex[i].Length;

        var last = grown[^1];
        var originals = new Dictionary<int, byte[]>();
        var lastDonor = last;
        for (var i = last + 1; i < archive.Entries.Count - 1 && need > 0; i++)
        {
            if (byIndex.ContainsKey(i)) continue;
            var bytes = archive.Read(archive.Entries[i]);
            var keep = bytes.Length;
            while (keep >= Sector && need > 0 && IsZero(bytes.AsSpan(keep - Sector, Sector)))
            {
                keep -= Sector;
                need--;
            }
            if (keep == bytes.Length) continue;
            lengths[i] = keep;
            originals[i] = bytes[..keep];
            lastDonor = i;
        }
        if (need > 0)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, "DATA.BIN", null,
                $"As traduções cresceram além do espaço reaproveitável do disco (faltam {need} setores)."));
            return [];
        }

        var table = new byte[(lastDonor - first + 1) * 4];
        var result = new List<(long, byte[])>();
        foreach (var (offset, bytes) in patches)
        {
            var index = archive.Entries.First(e => e.Offset == offset).Index;
            if (index < first || index > lastDonor) result.Add((data + offset, bytes));
        }
        var position = archive.Entries[first].Offset;
        for (var i = first; i <= lastDonor; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan((i - first) * 4), (uint)position);
            var body = byIndex.TryGetValue(i, out var patched) ? patched
                : originals.TryGetValue(i, out var trimmed) ? trimmed
                : archive.Read(archive.Entries[i]);
            result.Add((data + position, body));
            position += lengths[i];
        }
        result.Add((iso.Find(SmtDataArchive.ExecutablePath)!.Offset + SmtDataArchive.TocOffset + first * 4L, table));
        return result;
    }

    /// <summary>Lines of each page of a message ({g0} separates pages; the 0 operand of [c816] does not).</summary>
    private static List<int> PageLines(IReadOnlyList<ushort> words)
    {
        var pages = new List<int> { 1 };
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i] == SmtMessageCodec.NewLine) pages[^1]++;
            else if (words[i] == 0 && (i == 0 || words[i - 1] != 0x816)) pages.Add(1);
        }
        return pages;
    }

    private static bool IsZero(ReadOnlySpan<byte> span)
    {
        foreach (var b in span) if (b != 0) return false;
        return true;
    }

    private (List<(long Offset, byte[] Data)> Patches, BuildReport Report) Compile(IsoIndex iso,
        IProgress<(string Step, double Fraction)>? progress, CancellationToken cancellation)
    {
        var archive = SmtDataArchive.Open(iso);
        var map = SmtCharMap.Load();
        var issues = new List<BuildIssue>();
        var patches = new List<(long, byte[])>();
        var rebuilt = new List<string>();
        int files = 0, translated = 0, changed = 0;

        var tbfs = Directory.Exists(Path.Combine(project.MessagesDirectory, "smt"))
            ? Directory.GetFiles(Path.Combine(project.MessagesDirectory, "smt"), "map_*.tbf")
            : [];
        for (var n = 0; n < tbfs.Length; n++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(("Compilando os scripts", (double)n / tbfs.Length));
            var path = tbfs[n];
            var name = Path.GetFileName(path);
            files++;
            TbfDocument document;
            try { document = TbfDocument.Load(path); }
            catch (InvalidDataException error)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, name, null, error.Message));
                continue;
            }
            translated += document.TranslatedCount;
            if (document.TranslatedCount == 0) continue;

            if (!int.TryParse(Path.GetFileNameWithoutExtension(name)[4..], out var index) || index >= archive.Entries.Count)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, name, null, "Nome de arquivo não corresponde a um script do disco."));
                continue;
            }
            var entry = archive.Entries[index];
            var bytes = archive.Read(entry);
            var script = SmtScript.Parse(bytes);
            if (script is null)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, name, null, "O script do disco não pôde ser lido."));
                continue;
            }

            var replacements = new Dictionary<int, ushort[]>();
            foreach (var message in document.Entries.Where(e => e.IsTranslated))
            {
                if (!message.Key.StartsWith("msg_") || !int.TryParse(message.Key[4..], out var start)
                    || !script.Messages.Any(m => m.Start == start))
                {
                    issues.Add(new BuildIssue(IssueLevel.Error, name, message.Key, "A mensagem não existe mais no script."));
                    continue;
                }
                try
                {
                    var words = SmtMessageCodec.Encode(message.Translation, map);
                    var original = script.Messages.First(m => m.Start == start);
                    var pages = script.Words.Skip(start).Take(original.Length).Count(w => w == 0);
                    if (words.Count(w => w == 0) != pages)
                    {
                        issues.Add(new BuildIssue(IssueLevel.Error, name, message.Key,
                            $"A tradução precisa manter {pages} marca(s) {{g0}} (quebra de página), como o original; tem {words.Count(w => w == 0)}."));
                        continue;
                    }
                    var originalLines = PageLines(script.Words.Skip(start).Take(original.Length).ToList());
                    var translatedLines = PageLines(words);
                    for (var p = 0; p < Math.Min(originalLines.Count, translatedLines.Count); p++)
                        if (translatedLines[p] > originalLines[p])
                        {
                            issues.Add(new BuildIssue(IssueLevel.Warning, name, message.Key,
                                $"A página {p + 1} tem {translatedLines[p]} linha(s); o original tem {originalLines[p]}. " +
                                "O jogo empilha mensagens na mesma caixa: linhas a mais podem estourá-la e travar o que vem depois (ex.: a entrada do nome)."));
                            break;
                        }
                    if (!words.SequenceEqual(script.Words.Skip(start).Take(original.Length)))
                    {
                        replacements[start] = [.. words];
                        changed++;
                    }
                }
                catch (InvalidDataException error)
                {
                    issues.Add(new BuildIssue(IssueLevel.Error, name, message.Key, error.Message));
                }
            }
            if (replacements.Count == 0) continue;

            try
            {
                patches.Add((entry.Offset, RebuildGrowing(script, bytes, replacements)));
                rebuilt.Add($"map_{index:d4}");
            }
            catch (InvalidOperationException error)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, name, null, $"Script {index}: {error.Message}"));
            }
        }

        CompileBanks(archive, map, issues, patches, rebuilt, ref files, ref translated, ref changed, cancellation);
        CompileImages(archive, iso, issues, patches, rebuilt, ref files, ref changed, cancellation);
        var fontInstalled = rebuilt.Any(r => r.StartsWith("map_") || r.StartsWith("bank_"));
        if (fontInstalled)
        {
            patches.Add(BuildFontPatch(archive, issues));
            AddKeyboardPatches(archive, map, issues, patches);
        }

        if (issues.All(i => i.Level != IssueLevel.Error))
            patches = Relayout(archive, patches, iso, issues);
        else
            patches = [.. patches.Select(p => (DataStart(iso) + p.Item1, p.Item2))];
        if (fontInstalled) patches.Add(BuildTextPatch(iso, issues));

        return (patches, new BuildReport(issues, files, translated, changed, rebuilt.Count, rebuilt, null, TimeSpan.Zero));
    }

    private void CompileImages(SmtDataArchive archive, IsoIndex iso, List<BuildIssue> issues,
        List<(long, byte[])> patches, List<string> rebuilt, ref int files, ref int changed,
        CancellationToken cancellation)
    {
        if (!File.Exists(project.ImageTranslationControlPath)) return;
        var control = project.LoadImageTranslationControl();
        var wanted = control.Images.Where(p => !string.IsNullOrWhiteSpace(p.Value.Replacement)).ToList();
        if (wanted.Count == 0) return;
        var sources = SmtImageCatalog.Discover(iso).ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(project.Root) + Path.DirectorySeparatorChar;

        foreach (var (path, item) in wanted)
        {
            cancellation.ThrowIfCancellationRequested();
            files++;
            if (!sources.TryGetValue(path, out var source) || source.Image is not TimImage tim
                || !SmtImageCatalog.TryParsePath(path, out var index, out var route))
            {
                issues.Add(new BuildIssue(IssueLevel.Error, path, null, "A imagem do índice não existe mais no disco."));
                continue;
            }
            var file = Path.GetFullPath(Path.Combine(project.Root, item.Replacement!));
            if (Path.IsPathRooted(item.Replacement!) || !file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(file))
            {
                issues.Add(new BuildIssue(IssueLevel.Error, path, null, $"Imagem de substituição não encontrada no projeto: {item.Replacement}"));
                continue;
            }

            try
            {
                var png = Png.Decode(File.ReadAllBytes(file));
                var entry = archive.Entries[index];
                var at = patches.FindIndex(p => p.Item1 == entry.Offset);
                var current = at >= 0 ? patches[at].Item2 : archive.Read(entry);
                var updated = SmtImageCatalog.Replace(current, route, original => tim.WithPixels(original, png));
                if (at >= 0) patches[at] = (entry.Offset, updated); else patches.Add((entry.Offset, updated));
                rebuilt.Add(path);
                changed++;
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, path, null, error.Message));
            }
        }
    }

    public static string BankName(SmtBankLocation location) =>
        $"bank_{location.Entry:d4}_{location.Start:x4}";

    private void CompileBanks(SmtDataArchive archive, SmtCharMap map, List<BuildIssue> issues,
        List<(long, byte[])> patches, List<string> rebuilt, ref int files, ref int translated, ref int changed,
        CancellationToken cancellation)
    {
        var pending = new List<(SmtBankLocation Location, SmtStringBank Bank, Dictionary<int, ushort[]> Replacements)>();
        foreach (var location in SmtStringBank.Locations)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = BankName(location) + ".tbf";
            var path = Path.Combine(project.MessagesDirectory, "smt", name);
            if (!File.Exists(path)) continue;
            files++;
            TbfDocument document;
            try { document = TbfDocument.Load(path); }
            catch (InvalidDataException error)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, name, null, error.Message));
                continue;
            }
            translated += document.TranslatedCount;
            if (document.TranslatedCount == 0) continue;

            var bytes = archive.Read(archive.Entries[location.Entry]);
            var bank = SmtStringBank.Parse(bytes, location);
            if (bank is null)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, name, null, "O banco de textos do disco não pôde ser lido."));
                continue;
            }

            var replacements = new Dictionary<int, ushort[]>();
            foreach (var item in document.Entries.Where(e => e.IsTranslated))
            {
                if (!item.Key.StartsWith("str_") || !int.TryParse(item.Key[4..], out var start)
                    || !bank.Strings.Any(s => s.Start == start))
                {
                    issues.Add(new BuildIssue(IssueLevel.Error, name, item.Key, "O texto não existe mais no banco."));
                    continue;
                }
                try
                {
                    var words = SmtMessageCodec.Encode(item.Translation, map);
                    var original = bank.Strings.First(s => s.Start == start);
                    if (!words.SequenceEqual(bank.Words.Skip(start).Take(original.Length)))
                    {
                        replacements[start] = [.. words];
                        changed++;
                    }
                }
                catch (InvalidDataException error)
                {
                    issues.Add(new BuildIssue(IssueLevel.Error, name, item.Key, error.Message));
                }
            }
            if (replacements.Count == 0) continue;
            pending.Add((location, bank, replacements));
        }

        foreach (var group in pending.GroupBy(p => p.Location.Entry))
        {
            var entry = archive.Entries[group.Key];
            var bytes = archive.Read(entry);
            var shared = SmtStringBank.Locations.Count(l => l.Entry == group.Key) > 1;
            try
            {
                patches.Add((entry.Offset, shared
                    ? RebuildContainer(bytes, group.Key, group.ToDictionary(p => p.Location, p => p.Replacements))
                    : RebuildBankGrowing(group.Single().Bank, bytes, group.Single().Location, group.Single().Replacements)));
                rebuilt.AddRange(group.Select(p => BankName(p.Location)));
            }
            catch (InvalidOperationException error)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, BankName(group.First().Location) + ".tbf", null, $"Banco {group.Key}: {error.Message}"));
            }
        }
    }

    /// <summary>Name-entry keyboard with the Latin letters and Portuguese labels.</summary>
    private static void AddKeyboardPatches(SmtDataArchive archive, SmtCharMap map, List<BuildIssue> issues, List<(long, byte[])> patches)
    {
        try
        {
            var pages = archive.Entries[SmtKeyboard.PagesEntry];
            var overlay = archive.Entries[SmtKeyboard.OverlayEntry];
            patches.Add((pages.Offset, SmtKeyboard.TranslatePages(archive.Read(pages), map)));
            patches.Add((overlay.Offset, SmtKeyboard.TranslateLabels(archive.Read(overlay), map)));
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, "teclado", null, error.Message));
        }
    }

    /// <summary>Variable-width text in the executable, so Latin letters are not spaced 12 pixels apart.</summary>
    private static (long, byte[]) BuildTextPatch(IsoIndex iso, List<BuildIssue> issues)
    {
        var entry = iso.Find(SmtDataArchive.ExecutablePath)!;
        var exe = iso.Open(SmtDataArchive.ExecutablePath).ReadBytes(0, (int)entry.Size);
        try
        {
            var (offset, bytes) = SmtTextPatch.Patch(exe, SmtLatinFont.Load().Advances);
            return (entry.Offset + offset, bytes);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, SmtDataArchive.ExecutablePath, null, error.Message));
            return (entry.Offset, []);
        }
    }

    private static (long, byte[]) BuildFontPatch(SmtDataArchive archive, List<BuildIssue> issues)
    {
        var entry = archive.Entries[SmtLatinFont.FontEntryIndex];
        var original = archive.Read(entry);
        if (!SmtRle.TryDecode(original, out var container))
        {
            issues.Add(new BuildIssue(IssueLevel.Error, "fonte", null, "A fonte do disco não pôde ser descompactada."));
            return (entry.Offset, original);
        }
        SmtLatinFont.Load().Install(container);
        var packed = SmtRle.Encode(container, original[4]);
        if (packed.Length > entry.Length)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, "fonte", null,
                $"A fonte com os glifos latinos passou o espaço do disco em {packed.Length - entry.Length} bytes."));
            return (entry.Offset, original);
        }
        var padded = new byte[entry.Length];
        packed.CopyTo(padded, 0);
        return (entry.Offset, padded);
    }
}
