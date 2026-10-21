using BkIncidentes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BkIncidentes.Controllers;

/// <summary>API de la campanita. La ven ambos roles (es solo lectura del historial); marcar como visto
/// solo cambia el estado de la propia persona y exige token CSRF como cualquier POST.</summary>
[Authorize]
public class NotificationsController : PortalController
{
    private readonly NotificationService _notes;

    public NotificationsController(NotificationService notes) => _notes = notes;

    [HttpGet("/api/notificaciones")]
    public IActionResult Feed() => Ok(_notes.Feed(UserId));

    [HttpPost("/api/notificaciones/leer")]
    public IActionResult MarkRead()
    {
        _notes.MarkAllRead(UserId);
        return Ok(_notes.Feed(UserId));
    }
}
