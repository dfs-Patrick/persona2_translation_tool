using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Tests;

public class TbfTests
{
    // Same shape and formatting as JSON.stringify(file, null, 2) in v2.
    private const string Sample =
        "{\n" +
        "  \"header\": {\n" +
        "    \"originalDirectory\": \"PSP_GAME/USRDIR/pack/P2PT_ALL.cpk$/event.bin$/e101.efb$\",\n" +
        "    \"originalFilename\": \"script.msg\",\n" +
        "    \"context\": {}\n" +
        "  },\n" +
        "  \"translation\": [\n" +
        "    {\n" +
        "      \"info\": {\n" +
        "        \"msg_key\": \"msg_0\",\n" +
        "        \"before_msg\": \"msg_0:\",\n" +
        "        \"after_msg\": \"[wait][clear]\"\n" +
        "      },\n" +
        "      \"text\": {\n" +
        "        \"before\": \"Tatsuya, \\\"wait\\\" <here> & now.\\nNext line\",\n" +
        "        \"after\": \"Tatsuya, \\\"espere\\\" <aqui> & agora. Ação!\"\n" +
        "      },\n" +
        "      \"extra\": 1\n" +
        "    }\n" +
        "  ]\n" +
        "}\n";

    [Fact]
    public void Round_trip_is_byte_identical_to_v2_format()
    {
        Assert.Equal(Sample, TbfDocument.Parse(Sample).Serialize());
    }

    [Fact]
    public void Editing_changes_only_the_translation_and_keeps_unknown_fields()
    {
        var doc = TbfDocument.Parse(Sample.Replace("\n", "\r\n"));
        doc.Entries[0].Translation = "Novo texto";
        var expected = Sample
            .Replace("Tatsuya, \\\"espere\\\" <aqui> & agora. Ação!", "Novo texto")
            .Replace("\n", "\r\n");
        Assert.Equal(expected, doc.Serialize());
    }

    [Fact]
    public void Rejects_files_without_translation_list()
    {
        Assert.Throws<InvalidDataException>(() => TbfDocument.Parse("{\"header\":{}}"));
    }

    [Fact]
    public void Describes_location_for_the_browser()
    {
        var (group, label) = TbfLocation.Describe(
            "PSP_GAME/USRDIR/pack/P2PT_ALL.cpk$/event.bin$/e0101.bin$/8.efb$", "script.msg");
        Assert.Equal("Eventos", group);
        Assert.Equal("e0101", label);
    }
}
