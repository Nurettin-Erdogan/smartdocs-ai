using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class ClaimVerificationServiceTests
{
    private static readonly IReadOnlyList<EvidenceSource> Sources =
    [
        new EvidenceSource(
            "Başvuru",
            1,
            "Ad Soyad: Nurettin Erdoğan. Bölüm: Bilgisayar Mühendisliği.")
    ];

    [Fact]
    public void Verify_MarksClaimVerifiedWhenQuoteExistsInSource()
    {
        var modelAnswer = new ModelAnswerInput(
            "Öğrenci Nurettin Erdoğan.",
            [
                new ModelClaimInput(
                    "Öğrenci adı Nurettin Erdoğan",
                    1,
                    "Ad Soyad: Nurettin Erdoğan")
            ]);

        var result = ClaimVerificationService.Verify(Sources, modelAnswer);

        Assert.Equal("verified", result.Verification.Status);
        Assert.Equal(100, result.Verification.Score);
        Assert.Equal(modelAnswer.Answer, result.Answer);
        Assert.True(result.Verification.Claims[0].Verified);
    }

    [Fact]
    public void Verify_RewritesPartialAnswersToSupportedClaimsOnly()
    {
        var modelAnswer = new ModelAnswerInput(
            "Her şey doğru.",
            [
                new ModelClaimInput(
                    "Öğrenci adı Nurettin Erdoğan",
                    1,
                    "Ad Soyad: Nurettin Erdoğan"),
                new ModelClaimInput(
                    "Şehir Londra",
                    1,
                    "şehir Londra")
            ]);

        var result = ClaimVerificationService.Verify(Sources, modelAnswer);

        Assert.Equal("partial", result.Verification.Status);
        Assert.Equal(50, result.Verification.Score);
        Assert.Contains("Öğrenci adı Nurettin Erdoğan", result.Answer);
        Assert.Contains("Doğrulanamayan iddialar yanıttan çıkarıldı.", result.Answer);
        Assert.DoesNotContain("Londra", result.Answer);
    }

    [Fact]
    public void Verify_RejectsAnswersWithoutEvidence()
    {
        var modelAnswer = new ModelAnswerInput(
            "Tahmini cevap",
            [new ModelClaimInput("Şehir Paris", 1, "şehir Paris")]);

        var result = ClaimVerificationService.Verify(Sources, modelAnswer);

        Assert.Equal("insufficient", result.Verification.Status);
        Assert.Equal(0, result.Verification.Score);
        Assert.StartsWith("Bu soruyu belge içinden doğrulayacak yeterli kanıt", result.Answer);
    }

    [Fact]
    public void TryParseModelAnswer_ReadsJsonPayload()
    {
        const string payload = """
            {
              "answer": "Kısa cevap",
              "claims": [
                { "claim": "İddia", "sourceId": 1, "quote": "kanıt metni burada" }
              ]
            }
            """;

        var parsed = ClaimVerificationService.TryParseModelAnswer(payload);

        Assert.NotNull(parsed);
        Assert.Equal("Kısa cevap", parsed!.Answer);
        Assert.Single(parsed.Claims);
        Assert.Equal(1, parsed.Claims[0].SourceId);
    }
}
