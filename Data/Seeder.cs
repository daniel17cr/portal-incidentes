using System.Text.Json;
using BkIncidentes.Domain;
using BkIncidentes.Services;

namespace BkIncidentes.Data;

/// <summary>
/// Siembra de datos FICTICIOS: usuarios de prueba, sucursales de ejemplo e incidentes.
/// Los incidentes se reproducen con las mismas funciones de <see cref="IncidentService"/> que usa la aplicación,
/// con fechas relativas al momento de sembrar, para que el dashboard siempre tenga datos recientes.
/// </summary>
public static class Seeder
{
    public const string SeedActor = "Datos de ejemplo";

    /// <summary>Credenciales FICTICIAS de demostración. Se pueden cambiar con BK_DEMO_ADMIN_PASSWORD / BK_DEMO_CONSULTA_PASSWORD.</summary>
    public static IReadOnlyList<(string User, string Display, string Role, string Password)> DemoUsers() => new[]
    {
        ("admin", "Ana Admin (demo)", Catalog.Admin, Environment.GetEnvironmentVariable("BK_DEMO_ADMIN_PASSWORD") ?? "Admin-Demo-2026"),
        ("consulta", "Carlos Consulta (demo)", Catalog.Consulta, Environment.GetEnvironmentVariable("BK_DEMO_CONSULTA_PASSWORD") ?? "Consulta-Demo-2026"),
    };

    public static bool CustomPasswords =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BK_DEMO_ADMIN_PASSWORD")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BK_DEMO_CONSULTA_PASSWORD"));

    /// <summary>Carga usuarios de prueba y sucursales de ejemplo en el documento.</summary>
    public static void SeedBase(DataSet d, string seedFile)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(seedFile)).RootElement;
        foreach (var (user, display, role, password) in DemoUsers())
            d.Users.Add(new AppUser { Id = d.Users.Count + 1, Username = user, DisplayName = display, Role = role, PasswordHash = PasswordHasher.Hash(password) });
        foreach (var r in doc.GetProperty("restaurants").EnumerateArray())
            d.Restaurants.Add(new Restaurant { Id = d.Restaurants.Count + 1, Code = r.GetProperty("code").GetString()!, Name = r.GetProperty("name").GetString()! });
    }

    public static void SeedIncidents(DataSet d, string seedFile)
    {
        // Se usan las mismas funciones del servicio, directamente sobre el documento (aún no hay concurrencia).
        var doc = JsonDocument.Parse(File.ReadAllText(seedFile)).RootElement;
        var now = IncidentService.UtcNow();
        var codes = d.Restaurants.ToDictionary(r => r.Code, r => r.Id);
        foreach (var item in doc.GetProperty("incidents").EnumerateArray())
        {
            var created = now - TimeSpan.FromDays(item.GetProperty("days_ago").GetDouble());
            created = new DateTime(created.Year, created.Month, created.Day, created.Hour, created.Minute, created.Second, DateTimeKind.Utc);
            var description = item.GetProperty("description").GetString()!;
            var data = new IncidentData
            {
                RestaurantId = codes[item.GetProperty("restaurant").GetString()!],
                Category = item.GetProperty("category").GetString()!,
                Priority = item.GetProperty("priority").GetString()!,
                Description = description,
                Responsible = item.GetProperty("responsible").GetString()!,
            };
            var id = IncidentService.CreateIn(d, data, SeedActor, created, Assistant.Suggest(description));
            foreach (var step in item.GetProperty("timeline").EnumerateArray())
            {
                var at = created + TimeSpan.FromMinutes(step.GetProperty("after_min").GetDouble());
                if (at > now) break; // no inventar eventos en el futuro
                var note = step.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
                var (ok, err) = IncidentService.TransitionIn(d, id, step.GetProperty("action").GetString()!, SeedActor, at, note);
                if (!ok) throw new InvalidOperationException($"Semilla inconsistente ({description[..Math.Min(40, description.Length)]}…): {err}");
            }
        }
    }
}
