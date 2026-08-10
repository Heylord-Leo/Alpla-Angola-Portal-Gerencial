using System;
using System.Globalization;
using System.Reflection;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Infrastructure.Services.Extraction;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Extraction;

/// <summary>
/// Dates cross the wire as unambiguous ISO, and a calendar day survives the crossing.
/// </summary>
///
/// <remarks>
/// <para>A document printed <c>10/08/2026</c> — 10 August, in Angola — showed as
/// <c>08/10/2026</c> in the Portal. The value itself was never wrong: <c>2026-08-10</c> was stored
/// correctly, and a native <c>&lt;input type="date"&gt;</c> rendered it through the BROWSER's en-US
/// locale. That is fixed in the UI by using the locale-independent <c>DateInput</c>.</para>
///
/// <para>What is pinned here is the layer that could genuinely corrupt the value: the contract that
/// the model returns <c>YYYY-MM-DD</c> and that the mapping carries it through untouched, with no
/// parse, no reformat and no timezone anywhere near it.</para>
/// </remarks>
public class DocumentDateExtractionTests
{
    private static ExtractionResultDto MapFromJson(string json)
    {
        var method = typeof(OpenAiDocumentExtractionProvider)
            .GetMethod("MapFromJson", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var provider = (OpenAiDocumentExtractionProvider)
            System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(OpenAiDocumentExtractionProvider));

        return (ExtractionResultDto)method.Invoke(provider, new object[] { json })!;
    }

    private static string SystemPrompt()
    {
        var method = typeof(OpenAiDocumentExtractionProvider)
            .GetMethod("GetSystemPrompt", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var provider = (OpenAiDocumentExtractionProvider)
            System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(OpenAiDocumentExtractionProvider));

        return (string)method.Invoke(provider, Array.Empty<object>())!;
    }

    private static string HeaderJson(string documentDate, string dueDate = "null") => $@"{{
        ""header"": {{
            ""supplierName"": ""Kimbala Industrial"",
            ""documentDate"": {documentDate},
            ""dueDate"": {dueDate}
        }},
        ""items"": [],
        ""qualityScore"": 0.95
    }}";

    // ── The mapping carries the value verbatim ───────────────────────────────────────────────

    /// <summary>
    /// The reported document. 10 August must survive as 10 August — not become 8 October.
    /// </summary>
    [Fact]
    public void KimbalaDocumentDate_IsCarriedAsAugustTenth()
    {
        var result = MapFromJson(HeaderJson(@"""2026-08-10"""));

        Assert.Equal("2026-08-10", result.Header!.DocumentDate);

        // Stated as the failure, not just the success: 2026-10-08 is what a US month-first reading
        // of '10/08/2026' produces, and it is the value that displayed as 08/10/2026.
        Assert.NotEqual("2026-10-08", result.Header.DocumentDate);
    }

    /// <summary>
    /// Every printed form in the report, once the model has re-encoded it. The mapping must not
    /// touch any of them.
    /// </summary>
    [Theory]
    [InlineData("2026-08-10")]   // printed 10/08/2026
    [InlineData("2026-07-27")]   // printed 27/07/2026 and 27.07.2026
    [InlineData("2026-12-31")]
    [InlineData("2026-01-01")]
    public void IsoDatesPassThroughUnchanged(string iso)
    {
        Assert.Equal(iso, MapFromJson(HeaderJson($@"""{iso}""")).Header!.DocumentDate);
    }

    /// <summary>
    /// 27/07 cannot be a US date at all — month 27 does not exist. It must map cleanly rather than
    /// throw or come back empty.
    /// </summary>
    [Fact]
    public void ADateImpossibleUnderUsReading_MapsWithoutFailing()
    {
        var result = MapFromJson(HeaderJson(@"""2026-07-27"""));

        Assert.Equal("2026-07-27", result.Header!.DocumentDate);
        Assert.True(result.Success);
    }

    /// <summary>
    /// The due date was requested by the prompt and declared on the envelope, but no property
    /// existed to receive it, so it arrived null on every document.
    /// </summary>
    [Fact]
    public void DueDateIsNoLongerDropped()
    {
        var result = MapFromJson(HeaderJson(@"""2026-08-10""", @"""2026-09-09"""));

        Assert.Equal("2026-09-09", result.Header!.DueDate);
    }

    [Fact]
    public void AbsentDatesStayNull()
    {
        var result = MapFromJson(HeaderJson("null"));

        Assert.Null(result.Header!.DocumentDate);
        Assert.Null(result.Header.DueDate);
    }

    /// <summary>
    /// No <c>DateTime</c> is constructed anywhere along this path, so no timezone can shift the day.
    /// Proven by round-tripping the value through the mapping and comparing the calendar date parsed
    /// under a deliberately awkward culture.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-PT")]
    [InlineData("de-DE")]
    public void NoCultureOrTimezoneShiftsTheDay(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var carried = MapFromJson(HeaderJson(@"""2026-08-10""")).Header!.DocumentDate!;

            Assert.Equal("2026-08-10", carried);

            var parsed = DateTime.ParseExact(carried, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None);
            Assert.Equal(2026, parsed.Year);
            Assert.Equal(8, parsed.Month);
            Assert.Equal(10, parsed.Day);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ── The prompt contract ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The instruction is the fix on the model side. Removing it would let a localized string back
    /// into the field with every mapping test above still green.
    /// </summary>
    [Theory]
    [InlineData("YYYY-MM-DD")]
    [InlineData("DAY FIRST")]
    [InlineData("dd/MM/yyyy")]
    [InlineData("NEVER THE VISUAL FORMAT")]
    public void PromptDemandsIsoAndDayFirstReading(string expected)
    {
        Assert.Contains(expected, SystemPrompt(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The worked examples from the report are in the prompt, including the ambiguous case that
    /// actually failed — both numbers ≤ 12, where the convention has to carry the decision.
    /// </summary>
    [Theory]
    [InlineData("10/08/2026")]
    [InlineData("2026-08-10")]
    [InlineData("27.07.2026")]
    [InlineData("August 10, 2026")]
    [InlineData("NOT 8 October")]
    public void PromptCarriesTheWorkedDateExamples(string expected)
    {
        Assert.Contains(expected, SystemPrompt(), StringComparison.Ordinal);
    }

    /// <summary>An undecidable date is null, never a guess.</summary>
    [Fact]
    public void PromptPrefersNullOverGuessingAUsReading()
    {
        var prompt = SystemPrompt();

        Assert.Contains("return null", prompt, StringComparison.Ordinal);
        Assert.Contains("Never guess a US", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void InvoicePromptVersionRecordsTheDateRevision()
    {
        var field = typeof(OpenAiDocumentExtractionProvider)
            .GetField("InvoicePromptVersion", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal("v2.3-iso-dates", (string)field.GetRawConstantValue()!);
    }
}
