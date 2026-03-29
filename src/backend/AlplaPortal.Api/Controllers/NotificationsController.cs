using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid CurrentUserId 
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(id) ? Guid.Empty : Guid.Parse(id);
        }
    }

    private List<string> CurrentUserRoles => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications()
    {
        var notifications = await _notificationService.GetNotificationsAsync(CurrentUserId, CurrentUserRoles);
        return Ok(notifications);
    }

    [HttpPost("{id}/read")]
    public async Task<ActionResult> MarkAsRead(string id)
    {
        await _notificationService.MarkAsReadAsync(CurrentUserId, id);
        return NoContent();
    }

    [HttpPost("mark-all-read")]
    public async Task<ActionResult> MarkAllAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(CurrentUserId);
        return NoContent();
    }

    [HttpPost("clear-read")]
    public async Task<ActionResult> ClearRead()
    {
        await _notificationService.ClearReadAsync(CurrentUserId);
        return NoContent();
    }
}
