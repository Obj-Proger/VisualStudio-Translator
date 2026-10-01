using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class TranslationValidatorTests
{
    private const string EnglishSentence = "Returns the number of items in the list.";
    private const string RussianSentence = "Возвращает количество элементов в списке.";

    private static readonly TranslationValidationOptions Russian = new() { TargetScripts = [WritingScript.Cyrillic] };

    // The validator only reads ProtectedSegment.Text, so most tests can skip building real
    // placeholder maps and write the protected text out by hand.
    private static ProtectedSegment Source(string text) =>
        new() { Text = text, Placeholders = new Dictionary<int, ProtectedPlaceholder>() };

    private static IEnumerable<ValidationIssueKind> Kinds(TranslationValidationResult result) =>
        result.Issues.Select(issue => issue.Kind);

    [Fact]
    public void Validate_FaithfulTranslation_IsValid()
    {
        TranslationValidationResult result = TranslationValidator.Validate(Source(EnglishSentence), RussianSentence, Russian);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_RealProtectedSegmentTranslatedKeepingTokens_IsValid()
    {
        ProtectedSegment source = MarkupProtector.Protect(
        [
            new TextRun { Text = "Returns the " },
            new EmphasisRun { Content = [new TextRun { Text = "number" }] },
            new TextRun { Text = " of items in " },
            new CodeSpan { Text = "List<T>" },
            new TextRun { Text = "." },
        ]);

        TranslationValidationResult result = TranslationValidator.Validate(
            source, "Возвращает ⟦0:o⟧количество⟦0:c⟧ элементов в ⟦1⟧.", Russian);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyTranslationOfNonEmptySource_ReportsOnlyEmptyTranslation()
    {
        TranslationValidationResult result = TranslationValidator.Validate(Source("Use ⟦0⟧ now."), "   ");

        Kinds(result).Should().Equal(ValidationIssueKind.EmptyTranslation);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyTranslationOfEmptySource_IsValid()
    {
        TranslationValidationResult result = TranslationValidator.Validate(Source(string.Empty), string.Empty);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    // --- Placeholder integrity ---

    [Fact]
    public void Validate_MissingPlaceholder_IsReportedWithItsToken()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Use ⟦0⟧ to start the background work."), "Используйте для запуска фоновой работы.");

        Kinds(result).Should().Equal(ValidationIssueKind.MissingPlaceholder);
        result.Issues[0].Detail.Should().Be("⟦0⟧");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_UnexpectedPlaceholder_IsReportedWithItsToken()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Use ⟦0⟧ to start the background work."), "Используйте ⟦0⟧ и ⟦7⟧ для запуска фоновой работы.");

        Kinds(result).Should().Equal(ValidationIssueKind.UnexpectedPlaceholder);
        result.Issues[0].Detail.Should().Be("⟦7⟧");
    }

    [Fact]
    public void Validate_DuplicatedPlaceholder_IsReportedWithItsToken()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Call ⟦0⟧ now, please wait."), "Вызовите ⟦0⟧ сейчас ⟦0⟧, пожалуйста подождите.");

        Kinds(result).Should().Equal(ValidationIssueKind.DuplicatedPlaceholder);
        result.Issues[0].Detail.Should().Be("⟦0⟧");
    }

    [Fact]
    public void Validate_ReorderedNodePlaceholders_IsValid()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Compare ⟦0⟧ with ⟦1⟧ before returning."), "Перед возвратом сравните ⟦1⟧ с ⟦0⟧.", Russian);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WrapperMarkersInWrongOrder_ReportsUnbalancedWrapper()
    {
        // Same tokens as the source, so the count check passes - only nesting is wrong.
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Note that ⟦0:o⟧this⟦0:c⟧ is important."), "Обратите внимание что ⟦0:c⟧это⟦0:o⟧ важно.");

        Kinds(result).Should().Equal(ValidationIssueKind.UnbalancedWrapper);
    }

    [Fact]
    public void Validate_LostCloseMarker_ReportsMissingOnly()
    {
        // The nesting check is skipped when tokens already disagree, so one lost marker
        // is one issue, not two.
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Note that ⟦0:o⟧this⟦0:c⟧ is important."), "Обратите внимание что ⟦0:o⟧это важно.");

        Kinds(result).Should().Equal(ValidationIssueKind.MissingPlaceholder);
        result.Issues[0].Detail.Should().Be("⟦0:c⟧");
    }

    [Fact]
    public void Validate_PlaceholderWithLocalizedDigits_IsMissingPlusUnexpected()
    {
        // A provider translating to Arabic may turn the ASCII digit of a token into an
        // Arabic-Indic one. The token pattern still matches it, but it is not an id the
        // source used, so the original is missing and the new one is unexpected.
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Use ⟦0⟧ to start the background work."), "استخدم ⟦\u0660⟧ لبدء العمل في الخلفية.");

        Kinds(result).Should().Equal(ValidationIssueKind.MissingPlaceholder, ValidationIssueKind.UnexpectedPlaceholder);
        result.Issues[0].Detail.Should().Be("⟦0⟧");
        result.Issues[1].Detail.Should().Be("⟦\u0660⟧");
    }

    // --- Length ratio ---

    [Fact]
    public void Validate_TranslationMuchShorterThanSource_ReportsTooShort()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("This method returns the total number of elements contained in the collection."), "Всего.");

        Kinds(result).Should().Equal(ValidationIssueKind.TranslationTooShort);
        result.Issues[0].Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_TranslationMuchLongerThanSource_ReportsTooLong()
    {
        string looping = string.Concat(Enumerable.Repeat("слово ", 60)) + "слово.";

        TranslationValidationResult result = TranslationValidator.Validate(Source("Gets the current value."), looping);

        Kinds(result).Should().Equal(ValidationIssueKind.TranslationTooLong);
    }

    // --- Target script ---

    [Fact]
    public void Validate_TranslationInWrongScript_ReportsWrongScript()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), "Gives back how many entries the list has.", Russian);

        Kinds(result).Should().Equal(ValidationIssueKind.WrongScript);
    }

    [Fact]
    public void Validate_TranslationKeepingUntranslatedIdentifiers_IsValid()
    {
        // Latin product names that are not glossary terms stay Latin in a Russian
        // sentence; the script check must tolerate that.
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Gets the Roslyn Workspace instance."), "Получает экземпляр Roslyn Workspace.", Russian);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_LatinTarget_SkipsScriptCheck()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), RussianSentence, new TranslationValidationOptions { TargetScripts = [WritingScript.Latin] });

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_JapaneseTranslation_AcceptsHanHiraganaAndKatakanaAndIdeographicFullStop()
    {
        TranslationValidationOptions japanese = new()
        {
            TargetScripts = [WritingScript.Han, WritingScript.Hiragana, WritingScript.Katakana],
        };

        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), "リスト内の項目数を返します。", japanese);

        result.Issues.Should().BeEmpty();
    }

    // --- Identity with the source ---

    [Fact]
    public void Validate_TranslationIdenticalToSource_ReportsIdenticalToSource()
    {
        TranslationValidationResult result = TranslationValidator.Validate(Source(EnglishSentence), EnglishSentence);

        Kinds(result).Should().Equal(ValidationIssueKind.IdenticalToSource);
    }

    [Fact]
    public void Validate_TranslationIdenticalIgnoringCaseAndEdgeWhitespace_ReportsIdenticalToSource()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), " RETURNS THE NUMBER OF ITEMS IN THE LIST. ");

        Kinds(result).Should().Equal(ValidationIssueKind.IdenticalToSource);
    }

    [Fact]
    public void Validate_IdenticalWhenNotRequiredDifferent_IsValid()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), EnglishSentence, new TranslationValidationOptions { RequireDifferentFromSource = false });

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_VeryShortSource_SkipsStatisticalChecks()
    {
        // "Id" stays "Id": too little text for identity, length or script to mean anything.
        TranslationValidationResult result = TranslationValidator.Validate(Source("Id"), "Id", Russian);

        result.Issues.Should().BeEmpty();
    }

    // --- Sanity ---

    [Theory]
    [InlineData(0x0007)] // BEL, a control character
    [InlineData(0xFFFD)] // replacement character
    [InlineData(0xD800)] // unpaired high surrogate
    public void Validate_InvalidCharacterOnlyInTranslation_ReportsInvalidCharacters(int codeUnit)
    {
        // Passed as a code unit rather than a string so the test case itself stays
        // printable - a lone surrogate in a theory argument upsets display names.
        string translation = $"Возвращает количество{(char)codeUnit} элементов в списке.";

        TranslationValidationResult result = TranslationValidator.Validate(Source(EnglishSentence), translation, Russian);

        Kinds(result).Should().Equal(ValidationIssueKind.InvalidCharacters);
    }

    [Fact]
    public void Validate_InvalidCharacterAlsoInSource_IsNotReported()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Gets the value\uFFFD of the item."), "Получает значение\uFFFD элемента.");

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_LineBreakInTranslation_IsNotInvalid()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), "Возвращает количество\nэлементов в списке.", Russian);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingFinalPunctuation_IsWarningNotError()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source(EnglishSentence), "Возвращает количество элементов в списке", Russian);

        Kinds(result).Should().Equal(ValidationIssueKind.PossiblyTruncated);
        result.Issues[0].Severity.Should().Be(ValidationSeverity.Warning);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SourceWithoutFinalPunctuation_DoesNotRequireItInTranslation()
    {
        TranslationValidationResult result = TranslationValidator.Validate(
            Source("Returns the number of items"), "Возвращает количество элементов", Russian);

        result.Issues.Should().BeEmpty();
    }
}