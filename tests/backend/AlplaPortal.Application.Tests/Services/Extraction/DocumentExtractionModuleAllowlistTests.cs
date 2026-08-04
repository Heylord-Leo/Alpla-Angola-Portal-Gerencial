using System.Text;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Extraction;

/// <summary>
/// <c>sourceContext</c> is an OCR module <b>allowlist key</b>, not a free-text hint.
///
/// <para>A value that is not a configured, enabled row in <c>OcrModuleConfigs</c> makes the
/// extraction return <c>Success = false</c> <b>before any provider is called</b> — no pages, no
/// tokens, every field null. Wrapped in the legacy envelope that is still an HTTP 200, so a caller
/// reading the status code alone sees an empty document and believes it was read.</para>
///
/// <para>That is exactly how the multi-document PAYMENT flow shipped broken: it passed
/// <c>"PAYMENT"</c>, which is not a module. These tests pin the behaviour so the next caller finds
/// out from a red test instead of from an empty form.</para>
/// </summary>
public class DocumentExtractionModuleAllowlistTests
{
    private const string ConfiguredModule = "REQUESTS";

    private sealed class RecordingProvider : IDocumentExtractionProvider
    {
        public string Name => "OPENAI";
        public int Calls { get; private set; }

        public Task<ExtractionResultDto> ExtractAsync(
            Stream fileStream, string fileName, string? sourceContext = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ExtractionResultDto
            {
                Success = true,
                QualityScore = 0.9m,
                ProviderName = "OPENAI",
                Header = new ExtractionHeaderDto { DocumentNumber = "FT 2026/118" }
            });
        }
    }

    private static (DocumentExtractionService service, RecordingProvider provider) Build(
        IEnumerable<OcrModuleConfigDto> modules, bool globallyEnabled = true)
    {
        var provider = new RecordingProvider();

        var options = new DocumentExtractionOptions
        {
            IsEnabled = globallyEnabled,
            DefaultProvider = "OPENAI",
            OpenAi = new OpenAiSettings { Enabled = true, TimeoutSeconds = 30 }
        };

        var settings = new Mock<IDocumentExtractionSettingsService>();
        settings.Setup(s => s.GetEffectiveSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(options);
        settings.Setup(s => s.GetModuleSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(modules.ToList());

        var adminLog = new Mock<AdminLogWriter>(MockBehavior.Loose, null!, null!, null!);
        adminLog.Setup(a => a.WriteAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

        var service = new DocumentExtractionService(
            new[] { provider },
            settings.Object,
            NullLogger<DocumentExtractionService>.Instance,
            adminLog.Object);

        return (service, provider);
    }

    private static List<OcrModuleConfigDto> SeededModules() => new()
    {
        new OcrModuleConfigDto { Id = 1, ModuleKey = "REQUESTS", DisplayName = "Requests & Buy2Pay", IsEnabled = true, AllowedExtensions = ".pdf,.jpg,.jpeg,.png" },
        new OcrModuleConfigDto { Id = 2, ModuleKey = "CONTRACTS", DisplayName = "Contracts Management", IsEnabled = true, AllowedExtensions = ".pdf,.jpg,.jpeg,.png" }
    };

    private static MemoryStream Pdf() => new(Encoding.UTF8.GetBytes("%PDF-1.4 fake"));

    // ── The regression ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("PAYMENT")]          // what the multi-document flow wrongly sent
    [InlineData("payment_request")]  // the provider's strategy-hint vocabulary — not a module
    [InlineData("quotation")]        // likewise
    [InlineData("CONTRACT")]         // singular; the module row is CONTRACTS
    public async Task ExtractAsync_Fails_WithoutCallingProvider_WhenModuleIsNotConfigured(string sourceContext)
    {
        var (service, provider) = Build(SeededModules());

        var result = await service.ExtractAsync(Pdf(), "FT_FT0026S7117N_881.pdf", sourceContext);

        Assert.False(result.Success);
        // An unconfigured module must be refused BEFORE any provider request is made — zero pages
        // and zero tokens is the signature of this failure.
        Assert.Equal(0, provider.Calls);
        Assert.Null(result.Header.DocumentNumber);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ExtractAsync_Fails_WithoutCallingProvider_WhenModuleIsDisabled()
    {
        var modules = SeededModules();
        modules[0].IsEnabled = false;

        var (service, provider) = Build(modules);

        var result = await service.ExtractAsync(Pdf(), "invoice.pdf", ConfiguredModule);

        Assert.False(result.Success);
        Assert.Equal(0, provider.Calls);
    }

    // ── What must keep working ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_ReachesProvider_ForTheConfiguredRequestsModule()
    {
        var (service, provider) = Build(SeededModules());

        var result = await service.ExtractAsync(Pdf(), "invoice.pdf", ConfiguredModule);

        Assert.True(result.Success);
        // REQUESTS governs both quotation and payment extraction, so the multi-document payment
        // flow must reach the provider through it.
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ExtractAsync_ReachesProvider_WhenNoSourceContextIsGiven()
    {
        var (service, provider) = Build(SeededModules());

        var result = await service.ExtractAsync(Pdf(), "invoice.pdf", sourceContext: null);

        Assert.True(result.Success);
        Assert.Equal(1, provider.Calls);   // a null context skips the allowlist entirely
    }

    [Fact]
    public async Task ExtractAsync_Fails_WithoutCallingProvider_WhenExtensionIsNotAllowed()
    {
        var (service, provider) = Build(SeededModules());

        var result = await service.ExtractAsync(Pdf(), "invoice.docx", ConfiguredModule);

        Assert.False(result.Success);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ExtractAsync_Fails_WithoutCallingProvider_WhenExtractionIsGloballyDisabled()
    {
        var (service, provider) = Build(SeededModules(), globallyEnabled: false);

        var result = await service.ExtractAsync(Pdf(), "invoice.pdf", ConfiguredModule);

        Assert.False(result.Success);
        Assert.Equal(0, provider.Calls);
    }

    // ── The envelope must not disguise the failure ──────────────────────────────────────────

    [Fact]
    public void MapToLegacyOcrResult_PreservesFailure_AndReportsZeroWork()
    {
        var failed = new ExtractionResultDto { Success = false };

        var envelope = ExtractionMapper.MapToLegacyOcrResult(failed, "FT_FT0026S7117N_881.pdf");

        Assert.False(envelope.Success);
        Assert.Equal("ERROR", envelope.Status!.Code);
        Assert.Equal(0, envelope.Metadata!["pagesProcessed"]);
        Assert.Equal(0, envelope.Metadata!["totalTokens"]);
        Assert.Null(envelope.Integration!.HeaderSuggestions!.DocumentNumber!.Value);
        Assert.Empty(envelope.Integration.LineItemSuggestions!);
    }

    [Fact]
    public void MapToLegacyOcrResult_MayStillCarryAFilenameOnlyClassification_OnFailure()
    {
        // The fallback classifier reads the FILENAME, so a failed extraction can still produce a
        // low-confidence suggestion. It describes the name of the file, not the document — which is
        // why the client must gate on Success rather than on the presence of a classification.
        var failed = new ExtractionResultDto { Success = false };

        var envelope = ExtractionMapper.MapToLegacyOcrResult(failed, "FT_FT0026S7117N_881.pdf");

        Assert.False(envelope.Success);

        var classification = envelope.Integration!.HeaderSuggestions!.DocumentClassification;
        if (classification != null)
        {
            // A filename guess must never reach the confidence of an actual reading.
            Assert.True(classification.Confidence < 0.7m);
        }
    }
}
