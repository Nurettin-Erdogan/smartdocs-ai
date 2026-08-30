using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartDocsAI.API.Services;

public static partial class SmartAnswerRouter
{
    private static readonly HashSet<string> ThanksQuestions = new(StringComparer.Ordinal)
    {
        "tesekkurler",
        "tesekkur ederim",
        "sag ol",
        "sagol",
        "eyvallah"
    };

    public static bool TryGetConversationReply(string question, out string answer)
    {
        var normalized = NormalizeIntent(question);

        answer = normalized switch
        {
            "merhaba" or "merhabalar" =>
                "Merhaba! Sana nasıl yardımcı olabilirim?",
            "selam" or "selamlar" or "hey" =>
                "Selam! Sana nasıl yardımcı olabilirim?",
            "naber" or "ne haber" or "nasilsin" or "merhaba nasilsin" =>
                "İyiyim, teşekkürler. Sen nasılsın?",
            "gunaydin" =>
                "Günaydın! Sana nasıl yardımcı olabilirim?",
            "iyi aksamlar" =>
                "İyi akşamlar! Sana nasıl yardımcı olabilirim?",
            "iyi geceler" =>
                "İyi geceler! Görüşmek üzere.",
            "iyiyim" or "ben de iyiyim" or "bende iyiyim" =>
                "Buna sevindim! Sana nasıl yardımcı olabilirim?",
            "ne yapiyorsun" or "napıyorsun" or "napiyorsun" =>
                "Buradayım; belgelerinle ilgili sorularını yanıtlamaya hazırım.",
            "gorusuruz" or "hosca kal" or "bay bay" =>
                "Görüşürüz! İhtiyacın olduğunda buradayım.",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(answer))
        {
            return true;
        }

        if (ThanksQuestions.Contains(normalized))
        {
            answer = "Rica ederim. Belgelerinle ilgili başka bir sorunu yanıtlayabilirim.";
            return true;
        }

        if (normalized is "kimsin" or "sen kimsin")
        {
            answer = "Ben SmartDocs AI; yüklediğin belgeleri kaynaklarıyla birlikte açıklayan belge asistanıyım.";
            return true;
        }

        if (normalized is "ne yapabilirsin" or "neler yapabilirsin")
        {
            answer = "Belgelerinde bilgi bulabilir, belirli alanları çıkarabilir ve yanıtın geldiği sayfayı gösterebilirim.";
            return true;
        }

        answer = string.Empty;
        return false;
    }

    public static bool TryGetContactAnswer(
        string question,
        string sourceContext,
        out string answer)
    {
        var normalized = NormalizeIntent(question);
        var wantsAll = ContainsAny(normalized, "iletisim", "irtibat");
        var wantsPhone = wantsAll || ContainsAny(normalized, "telefon", "cep telefonu", "gsm");
        var wantsEmail = wantsAll || ContainsAny(normalized, "e posta", "eposta", "email", "mail adresi");
        var wantsLinkedIn = wantsAll || normalized.Contains("linkedin", StringComparison.Ordinal);
        var wantsGitHub = wantsAll || normalized.Contains("github", StringComparison.Ordinal);
        var wantsLocation = wantsAll || ContainsAny(normalized, "adres", "konum");

        if (!wantsPhone && !wantsEmail && !wantsLinkedIn && !wantsGitHub && !wantsLocation)
        {
            answer = string.Empty;
            return false;
        }

        var results = new List<string>();
        AddMatch(results, "Telefon", wantsPhone, PhoneRegex(), sourceContext, CleanPhone);
        AddMatch(results, "E-posta", wantsEmail, EmailRegex(), sourceContext);
        AddMatch(results, "Konum", wantsLocation, LocationRegex(), sourceContext);
        AddMatch(results, "GitHub", wantsGitHub, GitHubRegex(), sourceContext, CleanUrl);
        AddMatch(results, "LinkedIn", wantsLinkedIn, LinkedInRegex(), sourceContext, CleanUrl);

        if (results.Count > 0)
        {
            answer = string.Join("\n", results);
            return true;
        }

        answer = wantsAll
            ? "İletişim bilgileri belgede yer almıyor."
            : $"{BuildMissingContactLabel(wantsPhone, wantsEmail, wantsLocation, wantsGitHub, wantsLinkedIn)} bilgisi belgede yer almıyor.";
        return true;
    }

    public static bool TryGetDocumentFieldAnswer(
        string question,
        string sourceContext,
        out string answer)
    {
        var normalizedQuestion = NormalizeIntent(question);
        var explicitlyAcademicAverage = ContainsAny(
            normalizedQuestion,
            "not ortalamasi",
            "genel not ortalamasi",
            "akademik ortalama",
            "gano",
            "agno",
            "gpa");
        var genericAverage = normalizedQuestion.Contains("ortalama", StringComparison.Ordinal);

        if (!explicitlyAcademicAverage && !genericAverage)
        {
            answer = string.Empty;
            return false;
        }

        var normalizedSource = NormalizeIntent(sourceContext);
        var looksAcademic = ContainsAny(
            normalizedSource,
            "universite",
            "ogrenci",
            "egitim",
            "muhendisligi");
        if (!explicitlyAcademicAverage && !looksAcademic)
        {
            answer = string.Empty;
            return false;
        }

        var match = AcademicAverageRegex().Match(sourceContext);
        if (!match.Success)
        {
            match = AcademicAverageBeforeLabelRegex().Match(sourceContext);
        }

        answer = match.Success
            ? $"Not ortalaması: {match.Groups["value"].Value.Replace(',', '.')}"
            : "Not ortalaması bilgisi belgede yer almıyor.";
        return true;
    }

    private static void AddMatch(
        ICollection<string> results,
        string label,
        bool requested,
        Regex regex,
        string source,
        Func<string, string>? clean = null)
    {
        if (!requested)
        {
            return;
        }

        var match = regex.Match(source);
        if (!match.Success)
        {
            return;
        }

        var value = match.Groups["value"].Value.Trim();
        value = clean?.Invoke(value) ?? value.TrimEnd('.', ',', ';');
        if (!string.IsNullOrWhiteSpace(value))
        {
            results.Add($"{label}: {value}");
        }
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private static string BuildMissingContactLabel(
        bool phone,
        bool email,
        bool location,
        bool gitHub,
        bool linkedIn)
    {
        var labels = new List<string>();
        if (phone) labels.Add("Telefon");
        if (email) labels.Add("E-posta");
        if (location) labels.Add("Konum");
        if (gitHub) labels.Add("GitHub");
        if (linkedIn) labels.Add("LinkedIn");
        return string.Join(" ve ", labels);
    }

    private static string CleanPhone(string value) =>
        Regex.Replace(value, @"\s+", " ").TrimEnd('.', ',', ';');

    private static string CleanUrl(string value) =>
        value.Trim().TrimEnd('.', ',', ';', ')');

    private static string NormalizeIntent(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character switch
            {
                'ı' or 'İ' => 'i',
                '-' or '_' => ' ',
                _ when char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) =>
                    char.ToLowerInvariant(character),
                _ => ' '
            });
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"(?<![\d])(?<value>\+?\d{1,3}[\s().-]*(?:\d[\s().-]*){9,12})(?!\d)")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?<value>[\w.!#$%&'*+/=?^`{|}~-]+@[\w-]+(?:\.[\w-]+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<value>(?:https?://)?(?:www\.)?github\.com/[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?/?)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRegex();

    [GeneratedRegex(@"(?<value>(?:https?://)?(?:www\.)?linkedin\.com/(?:in|pub)/[A-Za-z0-9%_-]+/?)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedInRegex();

    [GeneratedRegex(@"(?:📍|\b(?:Konum|Adres|Location)\b\s*[:\-])\s*(?<value>[^\r\n|•📞✉💻🔗]{2,100})", RegexOptions.IgnoreCase)]
    private static partial Regex LocationRegex();

    [GeneratedRegex(@"(?:GPA|GANO|AGNO|Genel\s+Not\s+Ortalamas[ıi]|Not\s+Ortalamas[ıi])\s*[:\-]?\s*(?<value>\d{1,3}(?:[.,]\d{1,2})?)", RegexOptions.IgnoreCase)]
    private static partial Regex AcademicAverageRegex();

    [GeneratedRegex(@"(?<value>\d{1,3}(?:[.,]\d{1,2})?)\s*(?:/\s*(?:4(?:[.,]00)?|100))?\s*(?:GPA|GANO|AGNO|Genel\s+Not\s+Ortalamas[ıi]|Not\s+Ortalamas[ıi])", RegexOptions.IgnoreCase)]
    private static partial Regex AcademicAverageBeforeLabelRegex();
}
