using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartDocsAI.API.Services;

public sealed record EvidenceSource(string Title, int PageNumber, string Content);

public sealed record ModelClaimInput(string? Claim, int? SourceId, string? Quote);

public sealed record ModelAnswerInput(string Answer, IReadOnlyList<ModelClaimInput> Claims);

public sealed record VerificationClaimResult(
    string Text,
    int? SourceIndex,
    string? SourceTitle,
    int? PageNumber,
    string Quote,
    bool Verified);

public sealed record AnswerVerificationResult(
    string Status,
    int Score,
    int SupportedClaims,
    int TotalClaims,
    string Summary,
    IReadOnlyList<VerificationClaimResult> Claims);

public sealed record ClaimVerificationOutcome(
    string Answer,
    AnswerVerificationResult Verification);

/// <summary>
/// Ports the Vercel/Gemini claim-evidence verifier so the .NET path can
/// reject or trim answers that lack a verbatim document quote.
/// </summary>
public static class ClaimVerificationService
{
    private static readonly HashSet<string> EvidenceStopWords = new(StringComparer.Ordinal)
    {
        "acaba", "ama", "bana", "belgede", "belgenin", "bir", "bunu", "icin", "ile",
        "ise", "olarak", "olan", "ve", "veya"
    };

    public static ModelAnswerInput? TryParseModelAnswer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var json = Regex.Replace(value.Trim(), "^```(?:json)?\\s*", string.Empty, RegexOptions.IgnoreCase);
        json = Regex.Replace(json, "\\s*```$", string.Empty).Trim();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("answer", out var answerElement) ||
                answerElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var answer = answerElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(answer) ||
                !document.RootElement.TryGetProperty("claims", out var claimsElement) ||
                claimsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var claims = new List<ModelClaimInput>();
            foreach (var claimElement in claimsElement.EnumerateArray())
            {
                if (claimElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? claim = null;
                int? sourceId = null;
                string? quote = null;

                if (claimElement.TryGetProperty("claim", out var claimText) &&
                    claimText.ValueKind == JsonValueKind.String)
                {
                    claim = claimText.GetString();
                }

                if (claimElement.TryGetProperty("sourceId", out var sourceElement) &&
                    sourceElement.TryGetInt32(out var parsedSourceId))
                {
                    sourceId = parsedSourceId;
                }

                if (claimElement.TryGetProperty("quote", out var quoteElement) &&
                    quoteElement.ValueKind == JsonValueKind.String)
                {
                    quote = quoteElement.GetString();
                }

                claims.Add(new ModelClaimInput(claim, sourceId, quote));
            }

            return new ModelAnswerInput(answer, claims);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ClaimVerificationOutcome Verify(
        IReadOnlyList<EvidenceSource> sources,
        ModelAnswerInput modelAnswer)
    {
        var claims = modelAnswer.Claims
            .Take(8)
            .SelectMany(candidate =>
            {
                var text = (candidate.Claim ?? string.Empty).Trim();
                if (text.Length > 320)
                {
                    text = text[..320];
                }

                var quote = (candidate.Quote ?? string.Empty).Trim();
                if (quote.Length > 600)
                {
                    quote = quote[..600];
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Array.Empty<VerificationClaimResult>();
                }

                int? sourceIndex = null;
                if (candidate.SourceId is int sourceId &&
                    sourceId >= 1 &&
                    sourceId <= sources.Count)
                {
                    sourceIndex = sourceId - 1;
                }

                var source = sourceIndex is null ? null : sources[sourceIndex.Value];
                var normalizedQuote = ComparableText(quote);
                var coverage = EvidenceCoverage(text, quote);
                var verified = source is not null &&
                    normalizedQuote.Length >= 8 &&
                    ComparableText(source.Content).Contains(normalizedQuote, StringComparison.Ordinal) &&
                    coverage >= 0.4;

                return
                [
                    new VerificationClaimResult(
                        text,
                        sourceIndex is null ? null : sourceIndex.Value + 1,
                        source?.Title,
                        source?.PageNumber,
                        quote,
                        verified)
                ];
            })
            .ToList();

        var supported = claims.Where(claim => claim.Verified).ToList();
        var totalClaims = claims.Count;
        var supportedClaims = supported.Count;
        var score = totalClaims == 0
            ? 0
            : (int)Math.Round(supportedClaims * 100d / totalClaims, MidpointRounding.AwayFromZero);
        var status = totalClaims > 0 && supportedClaims == totalClaims
            ? "verified"
            : supportedClaims > 0
                ? "partial"
                : "insufficient";
        var summary = status switch
        {
            "verified" => "Yanıttaki tüm iddiaların birebir belge kanıtı sunucu tarafından doğrulandı.",
            "partial" => "Yanıtın yalnızca belge içinde doğrulanabilen iddiaları gösteriliyor.",
            _ => "Bu yanıtı destekleyen birebir belge kanıtı doğrulanamadı."
        };

        var answer = status switch
        {
            "verified" => modelAnswer.Answer,
            "partial" => string.Join('\n', supported.Select(claim => $"• {claim.Text}")) +
                "\n\nDoğrulanamayan iddialar yanıttan çıkarıldı.",
            _ => "Bu soruyu belge içinden doğrulayacak yeterli kanıt bulamadım. Belgedeki ifadeye daha yakın ve açık bir soru deneyebilirsin."
        };

        return new ClaimVerificationOutcome(
            answer,
            new AnswerVerificationResult(
                status,
                score,
                supportedClaims,
                totalClaims,
                summary,
                claims));
    }

    private static string ComparableText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLower(new CultureInfo("tr-TR"));
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    private static IReadOnlyList<string> EvidenceTerms(string value)
    {
        var folded = ComparableText(value)
            .Normalize(NormalizationForm.FormD);
        var withoutMarks = new string(folded
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Replace("ı", "i", StringComparison.Ordinal);
        withoutMarks = Regex.Replace(withoutMarks, "[^a-z0-9\\s]", " ");
        return withoutMarks
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 3 && !EvidenceStopWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool SharesEvidenceStem(string left, string right)
    {
        if (left == right || left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal))
        {
            return true;
        }

        var length = Math.Min(6, Math.Min(left.Length, right.Length));
        return length >= 4 && left.AsSpan(0, length).SequenceEqual(right.AsSpan(0, length));
    }

    private static double EvidenceCoverage(string claim, string quote)
    {
        var claimTerms = EvidenceTerms(claim);
        var quoteTerms = EvidenceTerms(quote);
        if (claimTerms.Count == 0 || quoteTerms.Count == 0)
        {
            return 0;
        }

        var matches = claimTerms.Count(claimTerm =>
            quoteTerms.Any(quoteTerm => SharesEvidenceStem(claimTerm, quoteTerm)));
        return matches / (double)claimTerms.Count;
    }
}
