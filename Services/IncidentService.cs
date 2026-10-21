using System.Text.RegularExpressions;
using BkIncidentes.Data;
using BkIncidentes.Domain;

namespace BkIncidentes.Services;

public sealed class IncidentData
{
    public int RestaurantId { get; set; }
    public string Category { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Description { get; set; } = "";
    public string Responsible { get; set; } = "";
}

public sealed class IncidentFilter
{
    public int? Restaurant { get; set; }
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";   // "", "abiertos" o un estado
    public string Category { get; set; } = "";
    public string Q { get; set; } = "";
    /// <summary>Nombre del responsable, o <see cref="IncidentService.NoResponsible"/> para los sin asignar.</summary>
    public string Responsible { get; set; } = "";
    public bool Active => Restaurant != null || Priority != "" || Status != "" || Category != "" || Q != "" || Responsible != "";
}

public sealed class IncidentRow
{
    public Incident Incident { get; init; } = new();
    public string RestaurantName { get; init; } = "";
}

public sealed class PagedIncidents
{
    public List<IncidentRow> Rows { get; init; } = new();
    public int Total { get; init; }
    public int Page { get; init; }
    public int Pages { get; init; }
    public bool AnyIncidents { get; init; }
}

/// <summary>Reglas de negocio. Las usan los controladores y también la semilla, de modo que los datos de
/// ejemplo pasan exactamente por las mismas transiciones que los reales.</summary>
public sealed class IncidentService
{
    public const int PageSize = 15;
    private readonly IDataStore _store;

    public IncidentService(IDataStore store) => _store = store;

    public static DateTime UtcNow()
    {
        var n = DateTime.UtcNow;
        return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, DateTimeKind.Utc);
    }

    /// <summary>Valor del filtro para «sin responsable».</summary>
    public const string NoResponsible = "__none";

    /// <summary>Clave para agrupar responsables: sin importar mayúsculas ni espacios repetidos.</summary>
    public static string RespKey(string? responsible) =>
        Regex.Replace((responsible ?? "").Trim(), @"\s+", " ").ToLowerInvariant();

    // ------------------------------------------------------------------ lectura
    /// <summary>Responsables que aparecen en algún incidente (primer nombre tal como se escribió), ordenados.</summary>
    public List<string> Responsibles() => _store.Read(d =>
        d.Incidents.Where(i => RespKey(i.Responsible) != "")
            .GroupBy(i => RespKey(i.Responsible))
            .Select(g => Regex.Replace(g.First().Responsible.Trim(), @"\s+", " "))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList());

    public bool AnyUnassigned() => _store.Read(d => d.Incidents.Any(i => RespKey(i.Responsible) == ""));

    public List<Restaurant> Restaurants() =>
        _store.Read(d => d.Restaurants.OrderBy(r => r.Name, StringComparer.CurrentCulture)
            .Select(r => new Restaurant { Id = r.Id, Code = r.Code, Name = r.Name }).ToList());

    public IncidentRow? Get(int id) => _store.Read(d =>
    {
        var i = d.Incidents.FirstOrDefault(x => x.Id == id);
        if (i == null) return null;
        return new IncidentRow
        {
            Incident = i.Clone(),
            RestaurantName = d.Restaurants.FirstOrDefault(r => r.Id == i.RestaurantId)?.Name ?? "?",
        };
    });

    public List<IncidentEvent> Events(int id) => _store.Read(d =>
        d.Events.Where(e => e.IncidentId == id).OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new IncidentEvent { Id = e.Id, IncidentId = e.IncidentId, At = e.At, Actor = e.Actor, Action = e.Action, Detail = e.Detail })
            .ToList());

    /// <summary>Copia de todos los incidentes con el nombre de su restaurante (para el dashboard).</summary>
    public List<IncidentRow> All() => _store.Read(d =>
    {
        var names = d.Restaurants.ToDictionary(r => r.Id, r => r.Name);
        return d.Incidents.Select(i => new IncidentRow
        {
            Incident = i.Clone(),
            RestaurantName = names.GetValueOrDefault(i.RestaurantId, "?"),
        }).ToList();
    });

    public PagedIncidents List(IncidentFilter f, int page)
    {
        return _store.Read(d =>
        {
            var names = d.Restaurants.ToDictionary(r => r.Id, r => r.Name);
            IEnumerable<Incident> q = d.Incidents;
            if (f.Restaurant != null) q = q.Where(i => i.RestaurantId == f.Restaurant);
            if (f.Priority != "") q = q.Where(i => i.Priority == f.Priority);
            if (f.Status == "abiertos") q = q.Where(i => Catalog.OpenStatuses.Contains(i.Status));
            else if (f.Status != "") q = q.Where(i => i.Status == f.Status);
            if (f.Category != "") q = q.Where(i => i.Category == f.Category);
            if (f.Responsible != "")
            {
                var want = f.Responsible == NoResponsible ? "" : RespKey(f.Responsible);
                q = q.Where(i => RespKey(i.Responsible) == want);
            }
            if (f.Q != "")
            {
                var m = Regex.Match(f.Q, @"^(?:inc-?)?(\d{1,9})$", RegexOptions.IgnoreCase);
                int? idQuery = m.Success ? int.Parse(m.Groups[1].Value) : null;
                q = q.Where(i => (idQuery != null && i.Id == idQuery)
                                 || i.Description.Contains(f.Q, StringComparison.OrdinalIgnoreCase));
            }
            var filtered = q.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).ToList();
            var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
            var p = Math.Min(Math.Max(1, page), pages);
            return new PagedIncidents
            {
                Rows = filtered.Skip((p - 1) * PageSize).Take(PageSize).Select(i => new IncidentRow
                {
                    Incident = i.Clone(),
                    RestaurantName = names.GetValueOrDefault(i.RestaurantId, "?"),
                }).ToList(),
                Total = filtered.Count,
                Page = p,
                Pages = pages,
                AnyIncidents = d.Incidents.Count > 0,
            };
        });
    }

    // ---------------------------------------------------------------- escritura
    private static void AddEvent(DataSet d, int incidentId, DateTime at, string actor, string action, string detail = "") =>
        d.Events.Add(new IncidentEvent
        {
            Id = d.NextEventId++, IncidentId = incidentId, At = at, Actor = actor, Action = action, Detail = detail,
        });

    /// <summary>Crea un incidente en estado «nuevo». <paramref name="suggestion"/> es lo que el asistente propuso
    /// (se guarda para medir cuánto lo corrige la persona).</summary>
    public int Create(IncidentData data, string actor, DateTime at, Suggestion suggestion, int? userId = null) =>
        _store.Write(d => CreateIn(d, data, actor, at, suggestion, userId));

    /// <summary>Lógica de creación sobre el documento (también la usa la semilla, antes de que exista el almacén).</summary>
    public static int CreateIn(DataSet d, IncidentData data, string actor, DateTime at, Suggestion suggestion, int? userId = null)
    {
        var inc = new Incident
        {
            Id = d.NextIncidentId++,
            RestaurantId = data.RestaurantId,
            Category = data.Category,
            Description = data.Description,
            Priority = data.Priority,
            Status = "nuevo",
            Responsible = data.Responsible,
            CreatedAt = at,
            SuggestedCategory = suggestion.Category,
            SuggestedPriority = suggestion.Priority,
            CreatedBy = userId,
        };
        d.Incidents.Add(inc);
        AddEvent(d, inc.Id, at, actor, "creado",
            $"Prioridad {Catalog.PriorityLabel(data.Priority)} · Categoría {Catalog.CategoryLabel(data.Category)}");
        return inc.Id;
    }

    /// <summary>Edita campos descriptivos (no el estado). Devuelve la lista de cambios realizados.</summary>
    public List<string> Update(int id, IncidentData data, string actor, DateTime at) =>
        _store.Write(d =>
        {
            var inc = d.Incidents.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
            var names = d.Restaurants.ToDictionary(r => r.Id, r => r.Name);
            var changes = new List<string>();
            if (inc.RestaurantId != data.RestaurantId)
                changes.Add($"Restaurante: {names.GetValueOrDefault(inc.RestaurantId, "?")} → {names[data.RestaurantId]}");
            if (inc.Category != data.Category)
                changes.Add($"Categoría: {Catalog.CategoryLabel(inc.Category)} → {Catalog.CategoryLabel(data.Category)}");
            if (inc.Priority != data.Priority)
                changes.Add($"Prioridad: {Catalog.PriorityLabel(inc.Priority)} → {Catalog.PriorityLabel(data.Priority)}");
            if (inc.Responsible != data.Responsible)
                changes.Add($"Responsable: {(inc.Responsible == "" ? "(sin asignar)" : inc.Responsible)} → {(data.Responsible == "" ? "(sin asignar)" : data.Responsible)}");
            if (inc.Description != data.Description) changes.Add("Descripción modificada");
            if (changes.Count == 0) return changes;
            inc.RestaurantId = data.RestaurantId;
            inc.Category = data.Category;
            inc.Priority = data.Priority;
            inc.Responsible = data.Responsible;
            inc.Description = data.Description;
            AddEvent(d, id, at, actor, "editado", string.Join("; ", changes));
            return changes;
        });

    /// <summary>Aplica gestionar / resolver / cerrar / reabrir. La validación del estado actual y la
    /// escritura ocurren bajo el mismo candado, así dos personas no se pisan.</summary>
    public (bool Ok, string Error) Transition(int id, string action, string actor, DateTime at, string note = "") =>
        _store.Write(d => TransitionIn(d, id, action, actor, at, note));

    public static (bool Ok, string Error) TransitionIn(DataSet d, int id, string action, string actor, DateTime at, string note = "")
    {
        if (!Catalog.Transitions.TryGetValue(action, out var tr)) return (false, "Acción no válida.");
        var inc = d.Incidents.FirstOrDefault(x => x.Id == id);
        if (inc == null) return (false, "El incidente no existe.");
        if (!tr.From.Contains(inc.Status)) return (false, "Esa acción no es posible desde el estado actual del incidente.");

        inc.Status = tr.Target;
        switch (action)
        {
            case "gestionar":
                inc.FirstResponseAt ??= at;
                break;
            case "resolver":
                inc.ResolvedAt = at;
                inc.FirstResponseAt ??= at;
                break;
            case "cerrar":
                inc.ClosedAt = at;
                break;
            case "reabrir":
                inc.ReopenCount++;
                inc.ResolvedAt = null;
                inc.ClosedAt = null;
                inc.FirstResponseAt ??= at;
                break;
        }
        AddEvent(d, id, at, actor, tr.EventKey, note);
        return (true, "");
    }
}
