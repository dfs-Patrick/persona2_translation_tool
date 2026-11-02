using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Tests;

public class ProjectTests
{
    private const string E0000 = "PSP_GAME/USRDIR/pack/P2PT_ALL.cpk$/event.bin$/e0000.bin$/8.efb$";

    [IsoFact]
    public async Task Creating_a_project_generates_the_translation_area()
    {
        await WithTemporaryFolder(async root =>
        {
            var created = await TranslationProject.CreateAsync(Path.Combine(root, "proj"), "Teste", TestIso.Path!);
            Assert.Equal("ULUS-10584", created.File.Iso.GameId);
            Assert.Equal(64, created.File.Iso.Sha256.Length);
            Assert.True(File.Exists(created.DirectoryActiveIndexPath));
            Assert.True(File.Exists(created.ImageTranslationControlPath));

            var opened = TranslationProject.Open(created.ProjectFilePath);
            Assert.Null(opened.CheckIso());
            var files = opened.ListTranslationFiles();
            Assert.Equal(617, files.Count);
            Assert.Contains(files, f => f.RelativePath == "event.bin/e0000.tbf" && f.Group == "Eventos" && f.Label == "e0000");
        });
    }

    [Fact]
    public void Project_indices_roundtrip_directory_labels_and_image_replacements()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gte-index-{Guid.NewGuid():N}", "translation");
        try
        {
            var directoriesPath = Path.Combine(root, "directory-active-index.json");
            var imagesPath = Path.Combine(root, "image-translation-control.json");
            var directories = ProjectIndexes.LoadDirectories(directoriesPath);
            directories.Directories["card.bin"] = new DirectoryActiveEntry
            {
                Label = "Retratos",
                Description = "Retratos dos personagens.",
            };
            ProjectIndexes.Save(directoriesPath, directories);

            var images = ProjectIndexes.LoadImages(imagesPath);
            images.Images["card.bin/portrait.gim"] = new ImageTranslationEntry
            {
                Replacement = "translation/images/portrait.png",
                Description = "Foto de personagem; paleta adaptada.",
            };
            ProjectIndexes.Save(imagesPath, images);

            var loadedDirectories = ProjectIndexes.LoadDirectories(directoriesPath);
            var loadedImages = ProjectIndexes.LoadImages(imagesPath);
            Assert.Equal(("Retratos", "Retratos dos personagens."),
                (loadedDirectories.Directories["CARD.BIN"].Label, loadedDirectories.Directories["card.bin"].Description));
            Assert.Equal("translation/images/portrait.png",
                loadedImages.Images["card.bin/portrait.gim"].Replacement);
            Assert.Equal("Foto de personagem; paleta adaptada.",
                loadedImages.Images["card.bin/portrait.gim"].Description);
        }
        finally
        {
            var projectRoot = Directory.GetParent(root)!.FullName;
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public void Projects_from_the_P2Tool_days_open_and_keep_their_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gte-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, TranslationProject.LegacyFileName),
                "{ \"formatVersion\": 1, \"name\": \"Antigo\", \"iso\": { \"path\": \"x.iso\" }, \"font\": \"pt-br\" }");
            var project = TranslationProject.Open(root);
            Assert.Equal("Antigo", project.Name);
            Assert.Equal("p2is-psp-us", project.Engine.Id);
            Assert.Equal(Path.Combine(root, TranslationProject.LegacyFileName), project.ProjectFilePath);
            project.Save();
            Assert.False(File.Exists(Path.Combine(root, TranslationProject.FileName)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scene_context_is_kept_in_the_tbf_header()
    {
        var doc = TbfDocument.Parse(TbfDocument.Create(E0000, "script.msg", [("msg_0", "msg_0:", "Hi", "")]).Serialize()
            .Replace("\"context\": {}", "\"context\": {\n      \"v2\": 1\n    }"));
        Assert.Equal("", doc.ContextLocation);
        doc.ContextLocation = "Prisão de Sumaru";
        doc.ContextSummary = "Curinga confronta Tatsuya.";
        var again = TbfDocument.Parse(doc.Serialize());
        Assert.Equal(("Prisão de Sumaru", "Curinga confronta Tatsuya."), (again.ContextLocation, again.ContextSummary));
        Assert.Contains("\"v2\": 1", again.Serialize());
        again.ContextSummary = "";
        Assert.DoesNotContain("summary", again.Serialize());
    }

    [IsoFact]
    public async Task Existing_translations_are_never_overwritten()
    {
        await WithTemporaryFolder(async root =>
        {
            var project = await TranslationProject.CreateAsync(Path.Combine(root, "proj"), "Teste", TestIso.Path!);
            var path = Path.Combine(project.MessagesDirectory, "event.bin", "e0000.tbf");
            var doc = TbfDocument.Load(path);
            doc.Entries[0].Translation = "Tradução feita à mão";
            doc.Save(path);

            var report = project.GenerateTranslationArea();
            Assert.Equal((0, 617), (report.Created, report.Kept));
            Assert.Equal("Tradução feita à mão", TbfDocument.Load(path).Entries[0].Translation);
        });
    }

    [IsoFact]
    public async Task Imported_v2_files_take_the_place_of_generated_ones()
    {
        await WithTemporaryFolder(async root =>
        {
            // A v2 workspace: flat names, same original location as a generated file.
            var v2 = Path.Combine(root, "v2");
            Directory.CreateDirectory(v2);
            TbfDocument.Create(E0000, "script.msg", [("msg_0", "msg_0:", "Hello", "[end]\n")])
                .Save(Path.Combine(v2, "0000_script.msg.tbf"));
            File.WriteAllText(Path.Combine(v2, "broken.tbf"), "{}");

            var folder = Path.Combine(root, "proj");
            Directory.CreateDirectory(Path.Combine(folder, "translation", "messages"));
            var imported = Path.Combine(folder, "translation", "messages", "0000_script.msg.tbf");
            File.Copy(Path.Combine(v2, "0000_script.msg.tbf"), imported);

            var project = await TranslationProject.CreateAsync(folder, "Teste", TestIso.Path!);
            Assert.False(File.Exists(Path.Combine(project.MessagesDirectory, "event.bin", "e0000.tbf")));
            Assert.Equal(617, project.ListTranslationFiles().Count);

            var again = project.ImportTbf(v2);
            Assert.Equal((0, 1, 1), (again.Copied, again.Skipped, again.Invalid.Count));
        });
    }

    private static async Task WithTemporaryFolder(Func<string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "p2tool-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await test(root);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
