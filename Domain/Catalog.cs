using System.Text.Json.Serialization;

namespace BkIncidentes.Domain;

/// <summary>Catálogos del dominio: única fuente de verdad para categorías, prioridades y estados.</summary>
public static class Catalog
{
    public static readonly IReadOnlyList<(string Key, string Label)> Categories = new[]
    {
        ("pos", "POS"), ("kiosco", "Kiosco"), ("conectividad", "Conectividad"),
        ("impresoras", "Impresoras"), ("menu_digital", "Menú digital"), ("facturacion", "Facturación"),
    };

    public static readonly IReadOnlyList<(string Key, string Label)> Priorities = new[]
    {
        ("critica", "Crítica"), ("alta", "Alta"), ("media", "Media"), ("baja", "Baja"),
    };

    public static readonly IReadOnlyList<(string Key, string Label)> Statuses = new[]
    {
        ("nuevo", "Nuevo"), ("en_gestion", "En gestión"), ("resuelto", "Resuelto"), ("cerrado", "Cerrado"),
    };

    public const string Admin = "admin";
    public const string Consulta = "consulta";
    public static readonly IReadOnlyDictionary<string, string> Roles =
        new Dictionary<string, string> { [Admin] = "Administrador", [Consulta] = "Consulta" };

    /// <summary>"Abiertos" = pendientes de trabajo. "Resuelto" queda aparte: ya se arregló pero falta cerrar.</summary>
    public static readonly string[] OpenStatuses = { "nuevo", "en_gestion" };

    /// <summary>acción -> (estados de origen permitidos, estado destino, clave del evento en el historial)</summary>
    public static readonly IReadOnlyDictionary<string, (string[] From, string Target, string EventKey)> Transitions =
        new Dictionary<string, (string[], string, string)>
        {
            ["gestionar"] = (new[] { "nuevo" }, "en_gestion", "en_gestion"),
            ["resolver"] = (new[] { "nuevo", "en_gestion" }, "resuelto", "resuelto"),
            ["cerrar"] = (new[] { "resuelto" }, "cerrado", "cerrado"),
            ["reabrir"] = (new[] { "resuelto", "cerrado" }, "en_gestion", "reabierto"),
        };

    public static readonly IReadOnlyDictionary<string, string> TransitionLabels = new Dictionary<string, string>
    {
        ["gestionar"] = "Pasar a en gestión", ["resolver"] = "Marcar como resuelto",
        ["cerrar"] = "Cerrar incidente", ["reabrir"] = "Reabrir incidente",
    };

    public static readonly IReadOnlyDictionary<string, string> EventLabels = new Dictionary<string, string>
    {
        ["creado"] = "Incidente creado", ["editado"] = "Datos editados", ["en_gestion"] = "Pasó a en gestión",
        ["resuelto"] = "Marcado como resuelto", ["cerrado"] = "Cerrado", ["reabierto"] = "Reabierto",
    };

    /// <summary>Un incidente es «repetido» si hubo otro del mismo restaurante y categoría en estos días previos.</summary>
    public const int RepeatWindowDays = 7;

    /// <summary>Costa Rica no usa horario de verano: UTC-6 fijo.</summary>
    public const int LocalUtcOffsetHours = -6;

    public const int DescriptionMin = 10, DescriptionMax = 1000, ResponsibleMax = 80, NoteMax = 300;

    public static string CategoryLabel(string? key) => Categories.FirstOrDefault(c => c.Key == key).Label ?? "—";
    public static string PriorityLabel(string? key) => Priorities.FirstOrDefault(c => c.Key == key).Label ?? "—";
    public static string StatusLabel(string? key) => Statuses.FirstOrDefault(c => c.Key == key).Label ?? "—";
    public static bool IsCategory(string? k) => Categories.Any(c => c.Key == k);
    public static bool IsPriority(string? k) => Priorities.Any(c => c.Key == k);
    public static bool IsStatus(string? k) => Statuses.Any(c => c.Key == k);
}

public sealed class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = Catalog.Consulta;
    public string PasswordHash { get; set; } = "";
    /// <summary>Último evento que la persona ya vio en la campanita. Null = aún no se inicializó (no hereda el historial).</summary>
    public int? LastSeenEventId { get; set; }
}

public sealed class Restaurant
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Todas las fechas se guardan en UTC (Kind = Utc).</summary>
public sealed class Incident
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "media";
    public string Status { get; set; } = "nuevo";
    public string Responsible { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int ReopenCount { get; set; }
    public string? SuggestedCategory { get; set; }
    public string? SuggestedPriority { get; set; }
    public int? CreatedBy { get; set; }

    public Incident Clone() => (Incident)MemberwiseClone();
}

public sealed class IncidentEvent
{
    public int Id { get; set; }
    public int IncidentId { get; set; }
    public DateTime At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>Documento completo que se persiste (equivalente a las tablas de una base de datos).</summary>
public sealed class DataSet
{
    public List<AppUser> Users { get; set; } = new();
    public List<Restaurant> Restaurants { get; set; } = new();
    public List<Incident> Incidents { get; set; } = new();
    public List<IncidentEvent> Events { get; set; } = new();
    public int NextIncidentId { get; set; } = 1;
    public int NextEventId { get; set; } = 1;
}
