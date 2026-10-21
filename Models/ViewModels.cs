using System.Globalization;
using BkIncidentes.Domain;
using BkIncidentes.Services;

namespace BkIncidentes.Models;

public sealed class IncidentForm
{
    public string? RestaurantId { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? Description { get; set; }
    public string? Responsible { get; set; }
}

public sealed class SuggestRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed record BarVm(int Pct, string Label);

public sealed class LoginVm
{
    public List<(string User, string Role, string Password)>? Demo { get; init; }
}

public sealed class FormVm
{
    public bool IsNew { get; init; }
    public Incident? Incident { get; init; }
    public Dictionary<string, string> Values { get; init; } = new();
    public Dictionary<string, string> Errors { get; init; } = new();
    public List<Restaurant> Restaurants { get; init; } = new();
    public string V(string key) => Values.GetValueOrDefault(key, "");
}

public sealed class ListVm
{
    public PagedIncidents Data { get; init; } = new();
    public IncidentFilter F { get; init; } = new();
    public List<Restaurant> Restaurants { get; init; } = new();
    public List<string> Responsibles { get; init; } = new();
    public bool AnyUnassigned { get; init; }

    /// <summary>Filtros actuales como datos de ruta (para conservarlos al paginar).</summary>
    public Dictionary<string, string?> RouteData(int? page = null)
    {
        var d = new Dictionary<string, string?>();
        if (F.Restaurant != null) d["restaurante"] = F.Restaurant.ToString();
        if (F.Priority != "") d["prioridad"] = F.Priority;
        if (F.Status != "") d["estado"] = F.Status;
        if (F.Category != "") d["categoria"] = F.Category;
        if (F.Responsible != "") d["responsable"] = F.Responsible;
        if (F.Q != "") d["q"] = F.Q;
        if (page != null) d["pagina"] = page.ToString();
        return d;
    }
}

public sealed class DetailVm
{
    public IncidentRow Row { get; init; } = new();
    public List<IncidentEvent> Events { get; init; } = new();
    public double? Atencion { get; init; }
    public double? Solucion { get; init; }
}

public sealed class DashboardVm
{
    public DashboardData D { get; init; } = new();
    public int? Dias { get; init; }
}

public sealed class ErrorVm
{
    public int Code { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>Formato de fechas, duraciones y códigos para las vistas.</summary>
public static class Fmt
{
    public static string Code(int id) => $"INC-{id:0000}";

    /// <summary>Las horas se guardan en UTC y se muestran en hora de Costa Rica (UTC-6).</summary>
    public static string LocalDt(DateTime? dt) =>
        dt == null ? "—" : dt.Value.AddHours(Catalog.LocalUtcOffsetHours).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Duration(double? seconds)
    {
        if (seconds == null) return "—";
        var s = (long)seconds.Value;
        if (s < 60) return "menos de 1 min";
        var minutes = s / 60;
        var days = minutes / 1440;
        var rem = minutes % 1440;
        var hours = rem / 60;
        var mins = rem % 60;
        if (days > 0) return hours > 0 ? $"{days} d {hours} h" : $"{days} d";
        if (hours > 0) return mins > 0 ? $"{hours} h {mins} min" : $"{hours} h";
        return $"{mins} min";
    }

    public static string Plural(int n, string one, string many) => n == 1 ? one : many;

    public static string ShortName(string name) => name.Replace(" (dato de ejemplo)", "");

    public static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)].TrimEnd() + "…";
}
