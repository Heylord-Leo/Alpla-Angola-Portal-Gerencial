using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<NotificationDto>> GetNotificationsAsync(Guid userId, List<string> roles)
    {
        var notifications = new List<NotificationDto>();

        try
        {
            // 1. Get Scoped Request Query (duplicated logic from BaseController for now to keep service independent)
            var query = await GetScopedRequestsQueryInternal(userId, roles);

            // 2. Calculate Operational Counts
            var opCounts = new Dictionary<string, (int Count, string Title, string Description, string TargetPath, string Type)>();

            // 2.1 Approvals
            bool isAdmin = roles.Contains(RoleConstants.SystemAdministrator);
            bool isAreaApprover = roles.Contains("Area Approver");
            bool isFinalApprover = roles.Contains("Final Approver");
            var pendingApprovals = await query.CountAsync(r => 
                (r.Status!.Code == "WAITING_AREA_APPROVAL" && (r.AreaApproverId == userId || roles.Contains("Area Approver"))) ||
                ((r.Status.Code == "WAITING_FINAL_APPROVAL" || r.Status.Code == "WAITING_COST_CENTER") && (r.FinalApproverId == userId || roles.Contains("Final Approver"))) ||
                (roles.Contains(RoleConstants.SystemAdministrator) && (r.Status.Code == "WAITING_AREA_APPROVAL" || r.Status.Code == "WAITING_FINAL_APPROVAL" || r.Status.Code == "WAITING_COST_CENTER"))
            );
            if (pendingApprovals > 0)
                opCounts[NotificationCategories.Approval] = (pendingApprovals, "Aprovações Pendentes", $"Tens {pendingApprovals} pedidos aguardando a tua decisão.", "/requests?statusCodes=WAITING_AREA_APPROVAL,WAITING_FINAL_APPROVAL,WAITING_COST_CENTER", NotificationTypes.Warning);

            // 2.2 Quotations
            if (roles.Contains("Buyer") || roles.Contains(RoleConstants.SystemAdministrator))
            {
                var waitingQuotation = await query.CountAsync(r => r.Status!.Code == "WAITING_QUOTATION");
                if (waitingQuotation > 0)
                    opCounts[NotificationCategories.Quotation] = (waitingQuotation, "Cotações Pendentes", $"{waitingQuotation} pedidos aguardam registo de cotações.", "/buyer/items", NotificationTypes.Info);
            }

            // 2.3 Receiving
            var waitingReceipt = await query.CountAsync(r => r.Status!.Code == "WAITING_RECEIPT" || r.Status.Code == "PARTIALLY_RECEIVED");
            if (waitingReceipt > 0)
                opCounts[NotificationCategories.Receipt] = (waitingReceipt, "Pronto para Receção", $"{waitingReceipt} pedidos estão com receção pendente de mercadoria.", "/receiving/workspace", NotificationTypes.Success);

            // 2.4 Finance
            if (roles.Contains("Finance") || roles.Contains(RoleConstants.SystemAdministrator))
            {
                var waitingPayment = await query.CountAsync(r => r.Status!.Code == "PO_ISSUED");
                if (waitingPayment > 0)
                    opCounts[NotificationCategories.Payment] = (waitingPayment, "Aguardando Pagamento", $"{waitingPayment} ordens de compra aguardam processamento financeiro.", "/requests?statusCodes=PO_ISSUED", NotificationTypes.Info);
            }

            // 3. Fetch Stored Read States for these Categories
            var categories = opCounts.Keys.ToList();
            var readStates = await _context.NotificationStatuses
                .Where(ns => ns.UserId == userId && categories.Contains(ns.Category))
                .ToDictionaryAsync(ns => ns.Category);

            // 4. Build Operational DTOs
            foreach (var kvp in opCounts)
            {
                var category = kvp.Key;
                var data = kvp.Value;
                bool isRead = false;

                if (readStates.TryGetValue(category, out var status))
                {
                    // Core Rule: If CurrentCount > LastReadCount, it becomes unread again
                    if (data.Count <= status.LastReadCount)
                    {
                        isRead = status.IsRead;
                    }
                }

                notifications.Add(new NotificationDto
                {
                    Id = $"op-{category.ToLower()}",
                    Title = data.Title,
                    Description = data.Description,
                    TargetPath = data.TargetPath,
                    Type = data.Type,
                    Category = category,
                    Count = data.Count,
                    IsRead = isRead,
                    IsOperational = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            // 5. Build Informational DTOs
            var infoNotifications = await _context.InformationalNotifications
                .Where(n => n.UserId == userId && !n.IsDismissed)
                .OrderByDescending(n => n.IsRead) // UX Rule: Unread first (Wait, adjustment 4 says operational first, then info. Within each group unread first)
                .ToListAsync();

            foreach (var n in infoNotifications)
            {
                notifications.Add(new NotificationDto
                {
                    Id = n.Id.ToString(),
                    Title = n.Title,
                    Description = n.Message,
                    TargetPath = n.TargetPath,
                    Type = n.Type,
                    Category = NotificationCategories.Info,
                    Count = 0,
                    IsRead = n.IsRead,
                    IsOperational = false,
                    CreatedAtUtc = n.CreatedAtUtc
                });
            }

            // 6. Final UX Ordering (Adjustment 4)
            // Operational first, then Info. Unread first within group.
            return notifications
                .OrderByDescending(n => n.IsOperational)
                .ThenBy(n => n.IsRead)
                .ThenByDescending(n => n.CreatedAtUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch notifications for User {UserId}. Returning empty list to prevent systemic UI failures.", userId);
            return new List<NotificationDto>();
        }
    }

    public async Task MarkAsReadAsync(Guid userId, string notificationId)
    {
        try
        {
            if (notificationId.StartsWith("op-"))
            {
                var category = notificationId.Substring(3).ToUpper();
                var status = await _context.NotificationStatuses
                    .FirstOrDefaultAsync(ns => ns.UserId == userId && ns.Category == category);

                // Need to get current count to set LastReadCount
                var roles = await GetUserRolesAsync(userId); // Implementation below
                var query = await GetScopedRequestsQueryInternal(userId, roles);
                int currentCount = 0;

                switch (category)
                {
                    case NotificationCategories.Approval:
                        currentCount = await query.CountAsync(r => 
                            (r.Status!.Code == "WAITING_AREA_APPROVAL" && (r.AreaApproverId == userId || roles.Contains("Area Approver"))) ||
                            ((r.Status.Code == "WAITING_FINAL_APPROVAL" || r.Status.Code == "WAITING_COST_CENTER") && (r.FinalApproverId == userId || roles.Contains("Final Approver"))) ||
                            (roles.Contains(RoleConstants.SystemAdministrator) && (r.Status.Code == "WAITING_AREA_APPROVAL" || r.Status.Code == "WAITING_FINAL_APPROVAL" || r.Status.Code == "WAITING_COST_CENTER"))
                        );
                        break;
                    case NotificationCategories.Quotation:
                        currentCount = await query.CountAsync(r => r.Status!.Code == "WAITING_QUOTATION");
                        break;
                    case NotificationCategories.Receipt:
                        currentCount = await query.CountAsync(r => r.Status!.Code == "WAITING_RECEIPT" || r.Status.Code == "PARTIALLY_RECEIVED");
                        break;
                    case NotificationCategories.Payment:
                        currentCount = await query.CountAsync(r => r.Status!.Code == "PO_ISSUED");
                        break;
                }

                if (status == null)
                {
                    status = new NotificationStatus
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Category = category,
                        IsRead = true,
                        LastReadCount = currentCount,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    _context.NotificationStatuses.Add(status);
                }
                else
                {
                    status.IsRead = true;
                    status.LastReadCount = currentCount;
                    status.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
            else if (Guid.TryParse(notificationId, out var infoId))
            {
                var n = await _context.InformationalNotifications.FindAsync(infoId);
                if (n != null && n.UserId == userId)
                {
                    n.IsRead = true;
                }
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark notification {NotificationId} as read for User {UserId}.", notificationId, userId);
            // Gracefully ignore failure as it's not critical for operation
        }
    }

    public async Task MarkAllAsReadAsync(Guid userId)
    {
        try
        {
            var roles = await GetUserRolesAsync(userId);
            var query = await GetScopedRequestsQueryInternal(userId, roles);

            // 1. Update Operational Statuses
            var categories = new[] { NotificationCategories.Approval, NotificationCategories.Quotation, NotificationCategories.Receipt, NotificationCategories.Payment };
            foreach (var category in categories)
            {
                int currentCount = category switch
                {
                    NotificationCategories.Approval => await query.CountAsync(r => 
                        (r.Status!.Code == "WAITING_AREA_APPROVAL" && (r.AreaApproverId == userId || roles.Contains("Area Approver"))) ||
                        ((r.Status.Code == "WAITING_FINAL_APPROVAL" || r.Status.Code == "WAITING_COST_CENTER") && (r.FinalApproverId == userId || roles.Contains("Final Approver"))) ||
                        (roles.Contains(RoleConstants.SystemAdministrator) && (r.Status.Code == "WAITING_AREA_APPROVAL" || r.Status.Code == "WAITING_FINAL_APPROVAL" || r.Status.Code == "WAITING_COST_CENTER"))
                    ),
                    NotificationCategories.Quotation => await query.CountAsync(r => r.Status!.Code == "WAITING_QUOTATION"),
                    NotificationCategories.Receipt => await query.CountAsync(r => r.Status!.Code == "WAITING_RECEIPT" || r.Status.Code == "PARTIALLY_RECEIVED"),
                    NotificationCategories.Payment => await query.CountAsync(r => r.Status!.Code == "PO_ISSUED"),
                    _ => 0
                };

                if (currentCount == 0) continue;

                var status = await _context.NotificationStatuses.FirstOrDefaultAsync(ns => ns.UserId == userId && ns.Category == category);
                if (status == null)
                {
                    _context.NotificationStatuses.Add(new NotificationStatus
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Category = category,
                        IsRead = true,
                        LastReadCount = currentCount,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    status.IsRead = true;
                    status.LastReadCount = currentCount;
                    status.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            // 2. Update Informational
            var infos = await _context.InformationalNotifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
            foreach (var info in infos) info.IsRead = true;

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark all notifications as read for User {UserId}.", userId);
        }
    }

    public async Task ClearReadAsync(Guid userId)
    {
        try
        {
            // Adjustment 2: Clear-read only removes informational notifications
            var readInfos = await _context.InformationalNotifications
                .Where(n => n.UserId == userId && n.IsRead)
                .ToListAsync();

            foreach (var n in readInfos)
            {
                n.IsDismissed = true;
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear read notifications for User {UserId}.", userId);
        }
    }

    public async Task CreateNotificationAsync(Guid userId, string title, string message, string type, string targetPath)
    {
        try
        {
            var notification = new InformationalNotification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                TargetPath = targetPath,
                IsRead = false,
                IsDismissed = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.InformationalNotifications.Add(notification);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Informational notification created for User {UserId}: {Title}", userId, title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create informational notification for User {UserId}.", userId);
            throw; // Re-throw to allow controller to handle or log at higher level if needed
        }
    }


    public async Task<bool> CreateNotificationWithDedupAsync(Guid userId, string title, string message, string type, string targetPath, Guid correlationId, string? category = null)
    {
        try
        {
            // Dedup check: skip if a notification with the same correlationId + userId already exists
            var exists = await _context.InformationalNotifications
                .AnyAsync(n => n.EventCorrelationId == correlationId && n.UserId == userId);

            if (exists)
            {
                _logger.LogDebug("Dedup: notification already exists for CorrelationId {CorrelationId} + User {UserId}. Skipping.", correlationId, userId);
                return false;
            }

            var notification = new InformationalNotification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                TargetPath = targetPath,
                IsRead = false,
                IsDismissed = false,
                CreatedAtUtc = DateTime.UtcNow,
                EventCorrelationId = correlationId,
                Category = category
            };

            _context.InformationalNotifications.Add(notification);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Informational notification created (dedup) for User {UserId}: {Title} (CorrelationId: {CorrelationId})", userId, title, correlationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create dedup notification for User {UserId} (CorrelationId: {CorrelationId}).", userId, correlationId);
            return false;
        }
    }
    private async Task<IQueryable<Request>> GetScopedRequestsQueryInternal(Guid userId, List<string> roles)
    {
        if (roles.Contains(RoleConstants.SystemAdministrator))
        {
            return _context.Requests.AsNoTracking();
        }

        var plantIds = await _context.UserPlantScopes.Where(s => s.UserId == userId).Select(s => s.PlantId).ToListAsync();
        var departmentIds = await _context.UserDepartmentScopes.Where(s => s.UserId == userId).Select(s => s.DepartmentId).ToListAsync();

        var query = _context.Requests.AsNoTracking().AsQueryable();
        if (plantIds.Any()) query = query.Where(r => r.PlantId.HasValue && plantIds.Contains(r.PlantId.Value));
        if (departmentIds.Any()) query = query.Where(r => departmentIds.Contains(r.DepartmentId));

        return query;
    }

    private async Task<List<string>> GetUserRolesAsync(Guid userId)
    {
        return await _context.UserRoleAssignments
            .Where(ura => ura.UserId == userId)
            .Include(ura => ura.Role)
            .Select(ura => ura.Role!.RoleName)
            .ToListAsync();
    }
}

