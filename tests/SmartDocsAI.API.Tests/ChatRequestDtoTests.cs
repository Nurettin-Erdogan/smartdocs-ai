using System.ComponentModel.DataAnnotations;
using SmartDocsAI.API.DTOs;

namespace SmartDocsAI.API.Tests;

public sealed class ChatRequestDtoTests
{
    [Fact]
    public void Validation_AcceptsUpToFiftySelectedDocuments()
    {
        var request = new ChatRequestDto
        {
            Question = "Bu belgeleri özetle",
            DocumentIds = Enumerable.Range(1, 50).ToList()
        };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validation_RejectsMoreThanFiftySelectedDocuments()
    {
        var request = new ChatRequestDto
        {
            Question = "Bu belgeleri özetle",
            DocumentIds = Enumerable.Range(1, 51).ToList()
        };

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(ChatRequestDto.DocumentIds)));
    }

    private static List<ValidationResult> Validate(ChatRequestDto request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);
        return results;
    }
}
