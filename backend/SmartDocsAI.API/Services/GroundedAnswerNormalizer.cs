using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartDocsAI.API.Services;

public static partial class GroundedAnswerNormalizer
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    public static string RestoreSourceSpelling(string answer, string sourceContext)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(sourceContext))
        {
            return answer;
        }

        var groundedWords = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in WordRegex().Matches(sourceContext))
        {
            var sourceWord = match.Value;
            var folded = FoldToAscii(sourceWord);

            // ASCII-only words do not provide any spelling information that the
            // model does not already have. Prefer the document's diacritic form
            // even when an ASCII filename appears earlier in the prompt.
            if (!string.Equals(sourceWord, folded, StringComparison.OrdinalIgnoreCase))
            {
                groundedWords.TryAdd(folded, sourceWord);
            }
        }

        return WordRegex().Replace(answer, match =>
        {
            var answerWord = match.Value;
            return groundedWords.TryGetValue(FoldToAscii(answerWord), out var sourceWord)
                ? ApplyAnswerCasing(sourceWord, answerWord)
                : answerWord;
        });
    }

    private static string FoldToAscii(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character switch
            {
                'ı' or 'İ' => 'i',
                _ => char.ToLowerInvariant(character)
            });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ApplyAnswerCasing(string sourceWord, string answerWord)
    {
        if (answerWord == answerWord.ToUpper(TurkishCulture))
        {
            return sourceWord.ToUpper(TurkishCulture);
        }

        if (answerWord == answerWord.ToLower(TurkishCulture))
        {
            return sourceWord.ToLower(TurkishCulture);
        }

        if (char.IsUpper(answerWord[0]) &&
            answerWord[1..] == answerWord[1..].ToLower(TurkishCulture))
        {
            var lowered = sourceWord.ToLower(TurkishCulture);
            return char.ToUpper(lowered[0], TurkishCulture) + lowered[1..];
        }

        return sourceWord;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}
