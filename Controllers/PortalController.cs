using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BkIncidentes.Controllers;

public abstract class PortalController : Controller
{
    /// <summary>Mensaje que sobrevive a una redirección (patrón POST → redirect → GET).</summary>
    protected void Flash(string kind, string message)
    {
        TempData["flash_kind"] = kind;
        TempData["flash_msg"] = message;
    }

    /// <summary>Mensaje para la misma respuesta (cuando se vuelve a mostrar un formulario con errores).</summary>
    protected void FlashNow(string kind, string message)
    {
        ViewData["flash_kind"] = kind;
        ViewData["flash_msg"] = message;
    }

    protected string Actor => User.FindFirstValue("display_name") ?? User.Identity?.Name ?? "?";

    protected int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
