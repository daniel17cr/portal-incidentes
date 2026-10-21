using System.Collections.Concurrent;
using System.Security.Claims;
using BkIncidentes.Data;
using BkIncidentes.Domain;
using BkIncidentes.Models;
using BkIncidentes.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BkIncidentes.Controllers;

/// <summary>Límite de intentos: 5 fallos por IP y usuario bloquean 10 minutos. En memoria (ver README).</summary>
public sealed class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<(string, string), (int Fails, DateTime Since)> _attempts = new();

    public bool IsLocked(string ip, string user)
    {
        if (!_attempts.TryGetValue((ip, user), out var rec)) return false;
        if (DateTime.UtcNow - rec.Since > Lock)
        {
            _attempts.TryRemove((ip, user), out _);
            return false;
        }
        return rec.Fails >= MaxFailures;
    }

    public void Failure(string ip, string user) =>
        _attempts.AddOrUpdate((ip, user), _ => (1, DateTime.UtcNow),
            (_, rec) => DateTime.UtcNow - rec.Since > Lock ? (1, DateTime.UtcNow) : (rec.Fails + 1, rec.Since));

    public void Reset(string ip, string user) => _attempts.TryRemove((ip, user), out _);
}

[AllowAnonymous]
public class AccountController : PortalController
{
    private readonly IDataStore _store;
    private readonly LoginThrottle _throttle;

    public AccountController(IDataStore store, LoginThrottle throttle)
    {
        _store = store;
        _throttle = throttle;
    }

    private static LoginVm Vm() => new()
    {
        // Se muestran las credenciales ficticias salvo que se hayan cambiado por variables de entorno.
        Demo = Seeder.CustomPasswords ? null : Seeder.DemoUsers().Select(u => (u.User, u.Role, u.Password)).ToList(),
    };

    [HttpGet("/login")]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(Vm());
    }

    [HttpPost("/login")]
    public async Task<IActionResult> LoginPost([FromForm] string? username, [FromForm] string? password, [FromQuery] string? next)
    {
        var user = (username ?? "").Trim().ToLowerInvariant();
        if (user.Length > 64) user = user[..64];
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";

        if (_throttle.IsLocked(ip, user))
        {
            FlashNow("error", "Demasiados intentos fallidos. Espere unos minutos e intente de nuevo.");
            Response.StatusCode = 429;
            return View("Login", Vm());
        }

        var row = _store.Read(d => d.Users.Where(u => u.Username == user)
            .Select(u => new AppUser { Id = u.Id, Username = u.Username, DisplayName = u.DisplayName, Role = u.Role, PasswordHash = u.PasswordHash })
            .FirstOrDefault());
        // Se verifica siempre un hash (aunque el usuario no exista) para igualar tiempos de respuesta.
        var ok = PasswordHasher.Verify(password ?? "", row?.PasswordHash ?? PasswordHasher.Dummy) && row != null;
        if (!ok)
        {
            _throttle.Failure(ip, user);
            FlashNow("error", "Usuario o contraseña incorrectos.");
            Response.StatusCode = 401;
            return View("Login", Vm());
        }

        _throttle.Reset(ip, user);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, row!.Id.ToString()),
            new Claim(ClaimTypes.Name, row.Username),
            new Claim("display_name", row.DisplayName),
            new Claim(ClaimTypes.Role, row.Role),
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        Flash("success", $"Bienvenido, {row.DisplayName}.");
        // Solo rutas internas: evita redirecciones abiertas (//evil.com, https://evil.com).
        if (!string.IsNullOrEmpty(next) && Url.IsLocalUrl(next)) return LocalRedirect(next);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Flash("success", "Sesión cerrada.");
        return RedirectToAction("Login");
    }
}
