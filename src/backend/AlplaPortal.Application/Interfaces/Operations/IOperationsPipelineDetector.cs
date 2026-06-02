using AlplaPortal.Domain.Enums;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Detects the pipeline model (STANDARD, INHOUSE) for a given AlplaPROD plant.
///
/// Phase 2 implementation is config-based. Runtime detection (querying actual
/// table row counts) is deferred to Phase 4.
/// </summary>
public interface IOperationsPipelineDetector
{
    /// <summary>
    /// Returns the pipeline model configured for the given plant.
    /// </summary>
    Task<AlplaProdPipelineModel> DetectPipelineModelAsync(AlplaProdPlant plant, CancellationToken ct = default);

    /// <summary>
    /// Returns the expected number of distinct timeline event types for the model.
    /// STANDARD = 10 events, INHOUSE = 7 events.
    /// </summary>
    int GetExpectedEventCount(AlplaProdPipelineModel model);
}
