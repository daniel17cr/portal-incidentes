using BkIncidentes.Models;
using BkIncidentes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BkIncidentes.Controllers;

public class HomeController : PortalController
{
    [AllowAnonymous]
    [HttpGet("/")]
    public IActionResult Index() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : RedirectToAction("Login", "Account");
}

[Authorize]
public class DashboardController : PortalController
{
    private readonly IncidentService _svc;

    public DashboardController(IncidentService svc) => _svc = svc;

    [HttpGet("/dashboard")]
    public IActionResult Index([FromQuery] string? dias)
    {
        int? d = dias is "7" or "30" or "90" ? int.Parse(dias) : null;
        var data = Analytics.Compute(_svc.All(), IncidentService.UtcNow(), d);
        return View(new DashboardVm { D = data, Dias = d });
    }
}
