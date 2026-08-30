using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class GroundedAnswerNormalizerTests
{
    [Theory]
    [InlineData("Nurettin Erdogan", "NURETTİN ERDOĞAN", "Nurettin Erdoğan")]
    [InlineData("ISTANBUL", "İSTANBUL", "İSTANBUL")]
    [InlineData("ogrenci", "ÖĞRENCİ", "öğrenci")]
    public void RestoreSourceSpelling_UsesDocumentDiacritics(
        string answer,
        string source,
        string expected)
    {
        var result = GroundedAnswerNormalizer.RestoreSourceSpelling(answer, source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RestoreSourceSpelling_PrefersDiacriticContentOverAsciiFilename()
    {
        const string source = "[Belge: Nurettin_Erdogan_CV] NURETTİN ERDOĞAN";

        var result = GroundedAnswerNormalizer.RestoreSourceSpelling(
            "Öğrenci ismi: Nurettin Erdogan",
            source);

        Assert.Equal("Öğrenci ismi: Nurettin Erdoğan", result);
    }

    [Fact]
    public void RestoreSourceSpelling_DoesNotInventWordsMissingFromSource()
    {
        var result = GroundedAnswerNormalizer.RestoreSourceSpelling(
            "Ankara'da okuyor.",
            "İstanbul Atlas Üniversitesi");

        Assert.Equal("Ankara'da okuyor.", result);
    }
}
