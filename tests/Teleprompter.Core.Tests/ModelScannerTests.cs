using System;
using System.IO;
using FluentAssertions;
using Teleprompter.Core.Languages;
using Teleprompter.Speech;

namespace Teleprompter.Core.Tests;

public sealed class ModelScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tp-models-" + Guid.NewGuid().ToString("N"));

    public ModelScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder cleanup is best effort.
        }
    }

    private string Root(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Touch(string dir, params string[] files)
    {
        foreach (string file in files)
        {
            string path = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
    }

    /// <summary>Current Vosk layout: am/, conf/, graph/.</summary>
    private static string AddVoskModel(string root, string folder)
    {
        string dir = Path.Combine(root, folder);
        Touch(dir, "am/final.mdl", "conf/mfcc.conf", "graph/HCLr.fst", "graph/Gr.fst");
        return dir;
    }

    /// <summary>Older Vosk layout with every file at the top level (e.g. Portuguese, Turkish).</summary>
    private static string AddFlatVoskModel(string root, string folder)
    {
        string dir = Path.Combine(root, folder);
        Touch(dir, "final.mdl", "mfcc.conf", "HCLr.fst", "Gr.fst");
        return dir;
    }

    private static string AddSherpaModel(string root, string folder)
    {
        string dir = Path.Combine(root, folder);
        Touch(dir, "tokens.txt", "encoder-epoch-99-avg-1.int8.onnx");
        return dir;
    }

    [Fact]
    public void Scan_FindsVoskModelsByLanguage()
    {
        string root = Root("user");
        string pl = AddVoskModel(root, "vosk-model-small-pl-0.22");
        string en = AddVoskModel(root, "vosk-model-small-en-us-0.15");

        InstalledModels models = ModelScanner.Scan(new[] { root });

        models.VoskDirFor("pl").Should().Be(pl);
        models.VoskDirFor("en").Should().Be(en);
        models.VoskDirFor("de").Should().BeNull();
        models.LanguageCodes.Should().BeEquivalentTo("en", "pl");
        models.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Scan_FindsTheOlderFlatVoskLayout()
    {
        string root = Root("user");
        string pt = AddFlatVoskModel(root, "vosk-model-small-pt-0.3");

        ModelScanner.Scan(new[] { root }).VoskDirFor("pt").Should().Be(pt);
    }

    [Fact]
    public void Scan_AcceptsTheStaticGraphOfLargeModels()
    {
        string root = Root("user");
        string dir = Path.Combine(root, "vosk-model-en-us-0.22");
        Touch(dir, "am/final.mdl", "conf/mfcc.conf", "graph/HCLG.fst");

        ModelScanner.Scan(new[] { root }).VoskDirFor("en").Should().Be(dir);
    }

    [Theory]
    [InlineData("am/final.mdl")]
    [InlineData("am/final.mdl", "conf/mfcc.conf")]
    [InlineData("am/final.mdl", "conf/mfcc.conf", "graph/HCLr.fst")]
    [InlineData("final.mdl", "HCLr.fst", "Gr.fst")]
    public void Scan_IgnoresHalfExtractedModels(params string[] files)
    {
        string root = Root("user");
        Touch(Path.Combine(root, "vosk-model-small-pl-0.22"), files);

        ModelScanner.Scan(new[] { root }).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Scan_IgnoresStagingFolders()
    {
        string root = Root("user");
        AddVoskModel(root, ".unpack-vosk-model-small-pl-0.22");
        AddVoskModel(Path.Combine(root, ".unpack-vosk-model-small-de-0.15"), "vosk-model-small-de-0.15");
        AddSherpaModel(root, ".unpack-sherpa-onnx-streaming-zipformer2-id");

        ModelScanner.Scan(new[] { root }).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Scan_UsesAModelOfAnUnknownLanguage_AsAFallbackOnly()
    {
        // Older versions ran any Vosk model found; a hand-installed model for
        // a language the catalog does not know must keep working.
        string root = Root("user");
        string other = AddVoskModel(root, "vosk-model-small-vn-0.4");

        InstalledModels models = ModelScanner.Scan(new[] { root });

        models.IsEmpty.Should().BeFalse();
        models.VoskDirFor("en").Should().Be(other);
        models.HasPackFor("en").Should().BeFalse("the picker must still offer a real download");
        models.LanguageCodes.Should().BeEmpty();
    }

    [Fact]
    public void Scan_PrefersALanguagesOwnPack_OverTheFallback()
    {
        string root = Root("user");
        AddVoskModel(root, "my-renamed-model");
        string pl = AddVoskModel(root, "vosk-model-small-pl-0.22");

        InstalledModels models = ModelScanner.Scan(new[] { root });

        models.VoskDirFor("pl").Should().Be(pl);
        models.HasPackFor("pl").Should().BeTrue();
    }

    [Fact]
    public void Scan_PrefersTheCatalogPack_ThenSmallModels()
    {
        string root = Root("user");
        AddVoskModel(root, "vosk-model-en-us-0.22");
        AddVoskModel(root, "vosk-model-small-en-in-0.4");
        string catalog = AddVoskModel(root, "vosk-model-small-en-us-0.15");
        AddVoskModel(root, "vosk-model-de-0.21");
        string smallDe = AddVoskModel(root, "vosk-model-small-de-zamia-0.3");

        InstalledModels models = ModelScanner.Scan(new[] { root });

        models.VoskDirFor("en").Should().Be(catalog);
        models.VoskDirFor("de").Should().Be(smallDe, "small models support the script lock");
    }

    [Fact]
    public void Scan_EarlierRootsWin()
    {
        string user = Root("user");
        string bundled = Root("bundled");
        string userPl = AddVoskModel(user, "vosk-model-small-pl-0.22");
        AddVoskModel(bundled, "vosk-model-small-pl-0.22");
        string bundledDe = AddVoskModel(bundled, "vosk-model-small-de-0.15");

        InstalledModels models = ModelScanner.Scan(new[] { user, bundled });

        models.VoskDirFor("pl").Should().Be(userPl);
        models.VoskDirFor("de").Should().Be(bundledDe);
    }

    [Fact]
    public void Scan_SkipsMissingRoots()
    {
        ModelScanner.Scan(new[] { Path.Combine(_root, "missing") }).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Scan_FindsTheIndonesianSherpaPack()
    {
        string root = Root("user");
        string id = AddSherpaModel(root, VoiceLanguageCatalog.Find("id")!.ModelFolder);

        InstalledModels models = ModelScanner.Scan(new[] { root });

        models.SherpaDirFor("id").Should().Be(id);
        models.SherpaDirFor("en").Should().BeNull();
        models.HasPackFor("id").Should().BeTrue();
        models.LanguageCodes.Should().BeEquivalentTo("id");
    }

    [Fact]
    public void Scan_TreatsOtherSherpaModelsAsEnglish()
    {
        // The app has always run hand-installed sherpa-onnx models as English.
        string root = Root("user");
        string sherpa = AddSherpaModel(root, "sherpa-onnx-streaming-zipformer-en-2023-06-26");

        InstalledModels models = ModelScanner.Scan(new[] { root });

        models.SherpaDirFor("en").Should().Be(sherpa);
        models.SherpaDirFor("pl").Should().BeNull();
        models.HasPackFor("en").Should().BeTrue();
        models.HasPackFor("pl").Should().BeFalse();
        models.LanguageCodes.Should().BeEquivalentTo("en");
    }

    [Fact]
    public void Override_AppliesToEveryLanguage()
    {
        string dir = AddVoskModel(Root("custom"), "anything");

        InstalledModels models = ModelScanner.FromOverride(dir);

        models.VoskDirFor("pl").Should().Be(dir);
        models.VoskDirFor("en").Should().Be(dir);
        models.HasPackFor("tr").Should().BeTrue();
    }

    [Fact]
    public void Override_WithASherpaFolder_IsUsedAsTheSherpaModel()
    {
        string dir = AddSherpaModel(Root("custom"), "my-sherpa");

        InstalledModels models = ModelScanner.FromOverride(dir);

        models.SherpaDirFor("en").Should().Be(dir);
        models.VoskDirFor("en").Should().BeNull();
    }
}
