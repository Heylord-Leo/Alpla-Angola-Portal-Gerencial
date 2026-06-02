using AlplaPortal.Application.Interfaces.Operations;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Config-based pipeline detector for AlplaPROD plants.
///
/// Phase 2 implementation: uses the PipelineModel from appsettings.json
/// configuration via <see cref="AlplaProdConnectionFactory"/>.
///
/// Runtime detection (querying actual table row counts) is deferred to Phase 4.
/// </summary>
public class OperationsPipelineDetector : IOperationsPipelineDetector
{
    private readonly AlplaProdConnectionFactory _factory;

    public OperationsPipelineDetector(AlplaProdConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public AlplaProdPipelineModel DetectPipelineModel(AlplaProdPlant plant)
    {
        return _factory.GetPlantPipelineModel(plant);
    }

    /// <inheritdoc />
    public int GetExpectedEventCount(AlplaProdPipelineModel model) => model switch
    {
        AlplaProdPipelineModel.STANDARD => 10,
        AlplaProdPipelineModel.INHOUSE => 7,
        AlplaProdPipelineModel.PARTIAL => 10, // treat partial as standard for counting
        _ => 10,
    };
}
