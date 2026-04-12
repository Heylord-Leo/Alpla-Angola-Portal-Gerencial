using AlplaPortal.Application.DTOs.Requests;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlplaPortal.Application.Interfaces;

public interface INotificationService
{
    Task<IEnumerable<NotificationDto>> GetNotificationsAsync(Guid userId, List<string> roles);
    Task MarkAsReadAsync(Guid userId, string notificationId);
    Task MarkAllAsReadAsync(Guid userId);
    Task ClearReadAsync(Guid userId);
    Task CreateNotificationAsync(Guid userId, string title, string message, string type, string targetPath);

    /// <summary>
    /// Dedup-aware notification creation. Checks if a notification with the same
    /// correlationId + userId already exists before inserting.
    /// </summary>
    /// <returns>True if created, false if skipped due to dedup.</returns>
    Task<bool> CreateNotificationWithDedupAsync(Guid userId, string title, string message, string type, string targetPath, Guid correlationId, string? category = null);
}
