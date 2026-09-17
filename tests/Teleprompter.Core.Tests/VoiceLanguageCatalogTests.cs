using System;
using System.Linq;
using FluentAssertions;
using Teleprompter.Core.Languages;

namespace Teleprompter.Core.Tests;

public sealed class VoiceLanguageCatalogTests
{
    [Fact]
    public void All_StartsWithEnglish_AndHasUniqueCodesAndFolders()
    {
        VoiceLanguageCatalog.All[0].Should().BeSameAs(VoiceLanguageCatalog.English);
        VoiceLanguageCatalog.All.Select(l => l.Code).Should().OnlyHaveUniqueItems();
        VoiceLanguageCatalog.All.Select(l => l.ModelFolder).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("pl")]
    [InlineData("id")]
    public void All_IncludesRequestedLanguages(string code)
    {
        VoiceLanguageCatalog.Find(code).Should().NotBeNull();
    }

    [Fact]
    public void All_DownloadsAreHttpsFilesWithExactSizes()
    {
        foreach (VoiceLanguage language in VoiceLanguageCatalog.All)
        {
            language.Files.Should().NotBeEmpty(language.Code);
            foreach (VoicePackFile file in language.Files)
            {
                file.Url.Should().StartWith("https://", language.Code);
                file.Url.Should().EndWith("/" + file.Name, language.Code);
                file.Bytes.Should().BePositive(language.Code);
            }
        }
    }

    [Fact]
    public void VoskPacks_AreOneZipNamedAfterTheModelFolder()
    {
        foreach (VoiceLanguage language in VoiceLanguageCatalog.All.Where(l => l.Kind == VoicePackKind.Vosk))
        {
            language.Files.Should().ContainSingle(language.Code)
                .Which.Name.Should().Be(language.ModelFolder + ".zip");
        }
    }

    [Fact]
    public void Indonesian_IsAStreamingSherpaPack_PinnedToOneRevision()
    {
        VoiceLanguage indonesian = VoiceLanguageCatalog.Find("id")!;

        indonesian.Kind.Should().Be(VoicePackKind.SherpaOnnx);
        indonesian.Files.Select(f => f.Name).Should().Contain("tokens.txt");
        indonesian.Files.Should().OnlyContain(f => !f.Url.Contains("/resolve/main/"),
            "a moving branch could change the model under installed apps");
    }

    [Fact]
    public void All_AfterEnglish_IsSortedByEnglishName()
    {
        VoiceLanguageCatalog.All.Skip(1).Select(l => l.EnglishName)
            .Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("pl", "Polish · Polski")]
    [InlineData("en", "English")]
    public void DisplayName_ShowsEnglishAndNativeName(string code, string expected)
    {
        VoiceLanguageCatalog.Find(code)!.DisplayName.Should().Be(expected);
    }

    [Fact]
    public void DownloadSize_AddsUpAllFiles_AndRoundsMegabytesUp()
    {
        var language = VoiceLanguageCatalog.English with
        {
            Files = new[]
            {
                new VoicePackFile("a.onnx", "https://example.com/a.onnx", 1_048_576),
                new VoicePackFile("tokens.txt", "https://example.com/tokens.txt", 1)
            }
        };

        language.DownloadBytes.Should().Be(1_048_577);
        language.DownloadMegabytes.Should().Be(2);
    }

    [Theory]
    [InlineData("PL", "pl")]
    [InlineData("de", "de")]
    public void Find_IgnoresCase(string code, string expected)
    {
        VoiceLanguageCatalog.Find(code)!.Code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    public void Find_ReturnsNull_ForUnknownCodes(string? code)
    {
        VoiceLanguageCatalog.Find(code).Should().BeNull();
    }

    [Theory]
    [InlineData("vosk-model-small-pl-0.22", "pl")]
    [InlineData("VOSK-MODEL-SMALL-DE-0.15", "de")]
    [InlineData("vosk-model-en-us-0.22", "en")]
    [InlineData("vosk-model-small-en-in-0.4", "en")]
    [InlineData("vosk-model-small-cs-0.4-rhasspy", "cs")]
    public void FromModelFolder_RecognizesVoskFolderNames(string folder, string expected)
    {
        VoiceLanguageCatalog.FromModelFolder(folder)!.Code.Should().Be(expected);
    }

    [Fact]
    public void FromModelFolder_RecognizesEveryCatalogFolder()
    {
        foreach (VoiceLanguage language in VoiceLanguageCatalog.All)
        {
            VoiceLanguageCatalog.FromModelFolder(language.ModelFolder).Should().BeSameAs(language);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("my-model")]
    [InlineData("vosk-model-el-gr-0.7")]
    [InlineData("sherpa-onnx-streaming-zipformer-en-2023-06-26")]
    public void FromModelFolder_ReturnsNull_ForUnknownFolders(string? folder)
    {
        VoiceLanguageCatalog.FromModelFolder(folder).Should().BeNull();
    }

    [Fact]
    public void ResolveActive_KeepsTheSavedChoice_EvenBeforeItsPackIsInstalled()
    {
        VoiceLanguageCatalog.ResolveActive("pl", new[] { "en" }).Code.Should().Be("pl");
    }

    [Fact]
    public void ResolveActive_WithoutSavedChoice_UsesTheInstalledPack()
    {
        // An upgrade from a version without a language setting must keep
        // following the voice pack the user already downloaded.
        VoiceLanguageCatalog.ResolveActive(null, new[] { "de" }).Code.Should().Be("de");
    }

    [Fact]
    public void ResolveActive_WithSeveralPacks_PrefersCatalogOrder()
    {
        VoiceLanguageCatalog.ResolveActive(null, new[] { "pl", "en", "de" }).Code.Should().Be("en");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("xx")]
    public void ResolveActive_FallsBackToEnglish(string? saved)
    {
        VoiceLanguageCatalog.ResolveActive(saved, Array.Empty<string>())
            .Should().BeSameAs(VoiceLanguageCatalog.English);
    }
}
