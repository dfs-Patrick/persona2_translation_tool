using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.Tests;

public class TmpBuild
{
    [Fact]
    public void Build()
    {
        var root = Environment.GetEnvironmentVariable("SMT_TMP_PROJECT");
        if (root is null) return;
        var project = TranslationProject.Open(root);
        var compiler = project.CreateBuilder();
        var name = Environment.GetEnvironmentVariable("SMT_TMP_OUT") ?? "smt-ptbr-v3.cue";
        var output = Path.Combine(root, "build", name);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var report = compiler.Build(output);
        File.WriteAllText(Path.Combine(root, "build", "relatorio.txt"),
            $"ok={report.Succeeded} alterados={report.ChangedScripts}\n" + string.Join("\n", report.Issues));
    }
}
