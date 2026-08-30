using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class SmartAnswerRouterTests
{
    [Theory]
    [InlineData("naber", "İyiyim, teşekkürler. Sen nasılsın?")]
    [InlineData("Nasılsın?", "İyiyim, teşekkürler. Sen nasılsın?")]
    [InlineData("merhaba!", "Merhaba! Sana nasıl yardımcı olabilirim?")]
    [InlineData("selam", "Selam! Sana nasıl yardımcı olabilirim?")]
    [InlineData("günaydın", "Günaydın! Sana nasıl yardımcı olabilirim?")]
    [InlineData("iyi akşamlar", "İyi akşamlar! Sana nasıl yardımcı olabilirim?")]
    [InlineData("ben de iyiyim", "Buna sevindim! Sana nasıl yardımcı olabilirim?")]
    [InlineData("görüşürüz", "Görüşürüz! İhtiyacın olduğunda buradayım.")]
    public void TryGetConversationReply_AnswersSmallTalkNaturally(
        string question,
        string expected)
    {
        var handled = SmartAnswerRouter.TryGetConversationReply(question, out var answer);

        Assert.True(handled);
        Assert.Equal(expected, answer);
        Assert.DoesNotContain("Bilgisayar Mühendisliği", answer);
    }

    [Fact]
    public void TryGetConversationReply_DoesNotCaptureARealDocumentQuestion()
    {
        var handled = SmartAnswerRouter.TryGetConversationReply(
            "Naber kelimesi belgede kaç kez geçiyor?",
            out _);

        Assert.False(handled);
    }

    [Fact]
    public void TryGetContactAnswer_ReturnsOnlyContactFields()
    {
        const string source = """
            NURETTİN ERDOĞAN
            📞 +90 553 768 1537
            ✉ enurettin89@gmail.com
            📍 İstanbul, Türkiye
            💻 github.com/Nurettin-Erdogan
            🔗 linkedin.com/in/nurettin-e-7b5508289/
            İŞ DENEYİMİ
            Bilgisayar Mühendisliği öğrencisi.
            """;

        var handled = SmartAnswerRouter.TryGetContactAnswer(
            "İletişim bilgileri",
            source,
            out var answer);

        Assert.True(handled);
        Assert.Equal(
            "Telefon: +90 553 768 1537\n" +
            "E-posta: enurettin89@gmail.com\n" +
            "Konum: İstanbul, Türkiye\n" +
            "GitHub: github.com/Nurettin-Erdogan\n" +
            "LinkedIn: linkedin.com/in/nurettin-e-7b5508289/",
            answer);
        Assert.DoesNotContain("İŞ DENEYİMİ", answer);
        Assert.DoesNotContain("Bilgisayar Mühendisliği", answer);
    }

    [Fact]
    public void TryGetContactAnswer_ReturnsOnlyTheRequestedField()
    {
        const string source = "Telefon: +90 553 768 1537\nE-posta: enurettin89@gmail.com";

        var handled = SmartAnswerRouter.TryGetContactAnswer(
            "Telefon numarası ne?",
            source,
            out var answer);

        Assert.True(handled);
        Assert.Equal("Telefon: +90 553 768 1537", answer);
        Assert.DoesNotContain("E-posta", answer);
    }

    [Fact]
    public void TryGetContactAnswer_StopsUrlsAtConcatenatedPdfContent()
    {
        const string source =
            "💻 github.com/Nurettin-Erdogan🔗" +
            "linkedin.com/in/nurettin-e-7b5508289/HAKKIMDABilgisayar Mühendisliği";

        var handled = SmartAnswerRouter.TryGetContactAnswer(
            "GitHub ve LinkedIn bilgileri",
            source,
            out var answer);

        Assert.True(handled);
        Assert.Equal(
            "GitHub: github.com/Nurettin-Erdogan\n" +
            "LinkedIn: linkedin.com/in/nurettin-e-7b5508289/",
            answer);
        Assert.DoesNotContain("HAKKIMDA", answer);
        Assert.DoesNotContain("🔗", answer);
    }

    [Fact]
    public void TryGetContactAnswer_ReportsMissingRequestedContactInsteadOfFallingBackToModel()
    {
        var handled = SmartAnswerRouter.TryGetContactAnswer(
            "Telefon numarası nedir?",
            "Bu belgede yalnızca eğitim bilgileri bulunuyor.",
            out var answer);

        Assert.True(handled);
        Assert.Equal("Telefon bilgisi belgede yer almıyor.", answer);
    }

    [Fact]
    public void TryGetDocumentFieldAnswer_ReportsMissingAcademicAverage()
    {
        const string source = "İstanbul Atlas Üniversitesi Bilgisayar Mühendisliği öğrencisi";

        var handled = SmartAnswerRouter.TryGetDocumentFieldAnswer(
            "ortalama kaç?",
            source,
            out var answer);

        Assert.True(handled);
        Assert.Equal("Not ortalaması bilgisi belgede yer almıyor.", answer);
    }

    [Theory]
    [InlineData("GANO: 3,42", "Not ortalaması: 3.42")]
    [InlineData("Genel Not Ortalaması 3.18 / 4.00", "Not ortalaması: 3.18")]
    [InlineData("3.65 / 4.00 GPA", "Not ortalaması: 3.65")]
    public void TryGetDocumentFieldAnswer_ReturnsGroundedAcademicAverage(
        string source,
        string expected)
    {
        var handled = SmartAnswerRouter.TryGetDocumentFieldAnswer(
            "Not ortalaması nedir?",
            source,
            out var answer);

        Assert.True(handled);
        Assert.Equal(expected, answer);
    }

    [Fact]
    public void TryGetDocumentFieldAnswer_LeavesNonAcademicAveragesToTheModel()
    {
        var handled = SmartAnswerRouter.TryGetDocumentFieldAnswer(
            "Aylık ortalama satış kaç?",
            "Ocak satış 100 Şubat satış 120",
            out _);

        Assert.False(handled);
    }
}
