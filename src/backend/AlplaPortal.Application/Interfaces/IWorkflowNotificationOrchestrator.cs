using AlplaPortal.Domain.Events;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// Central notification orchestrator for workflow events.
/// Resolves recipients, builds messages, and dispatches to configured channels
/// (in-app notifications and email) for each event type.
/// </summary>
public interface IWorkflowNotificationOrchestrator
{
    /// <summary>
    /// Processes a workflow event: resolves recipients, creates in-app notifications,
    /// and sends email notifications as configured per event type.
    /// This method is fire-and-forget safe — all internal errors are caught and logged.
    /// </summary>
    Task EmitAsync(WorkflowEvent workflowEvent);
}
