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
}
