using BkIncidentes.Data;
using BkIncidentes.Domain;
using BkIncidentes.Models;

namespace BkIncidentes.Services;

public sealed class NotificationItem
{
    public int Id { get; init; }
    public int IncidentId { get; init; }
    public string Code { get; init; } = "";
    public string Restaurant { get; init; } = "";
    public string Action { get; init; } = "";
    public string Label { get; init; } = "";
    public string Actor { get; init; } = "";
    public string Detail { get; init; } = "";
    public string When { get; init; } = "";
    public bool Unread { get; init; }
}

public sealed class NotificationFeed
{
    public int Unread { get; init; }
    public List<NotificationItem> Items { get; init; } = new();
}

/// <summary>
/// Campanita: muestra las acciones recientes sobre incidentes (crear, editar, gestionar, resolver, cerrar, reabrir).
/// Sale del mismo historial que ya existe; lo único que se guarda por persona es hasta qué evento ya vio.
/// </summary>
public sealed class NotificationService
{
    public const int FeedSize = 15;
    private readonly IDataStore _store;

    public NotificationService(IDataStore store) => _store = store;

    /// <summary>
    /// Devuelve las últimas acciones y cuántas son nuevas para esa persona. La primera vez que se consulta
    /// se marca todo lo anterior como visto: una cuenta nueva no hereda el historial como «pendiente».
    /// </summary>
    public NotificationFeed Feed(int? userId)
    {
        // Lectura barata en cada consulta; se escribe a disco solo la primera vez, para inicializar.
        var seen = userId == null ? int.MaxValue
            : _store.Read(d => d.Users.Any(x => x.Id == userId)
                ? d.Users.First(x => x.Id == userId).LastSeenEventId : (int?)int.MaxValue);
        if (seen == null)
        {
            seen = _store.Write(d =>
            {
                var u = d.Users.FirstOrDefault(x => x.Id == userId);
                if (u == null) return int.MaxValue;
                u.LastSeenEventId ??= d.Events.Count == 0 ? 0 : d.Events.Max(e => e.Id);
                return u.LastSeenEventId.Value;
            });
        }
        var lastSeen = seen.Value;

        return _store.Read(d =>
        {
            var byId = d.Incidents.ToDictionary(i => i.Id);
            var names = d.Restaurants.ToDictionary(r => r.Id, r => r.Name);
            var unread = d.Events.Count(e => e.Id > lastSeen);
            var items = d.Events.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(FeedSize).Select(e =>
            {
                byId.TryGetValue(e.IncidentId, out var inc);
                return new NotificationItem
                {
                    Id = e.Id,
                    IncidentId = e.IncidentId,
                    Code = Fmt.Code(e.IncidentId),
                    Restaurant = inc == null ? "" : Fmt.ShortName(names.GetValueOrDefault(inc.RestaurantId, "")),
                    Action = e.Action,
                    Label = Catalog.EventLabels.GetValueOrDefault(e.Action, e.Action),
                    Actor = e.Actor,
                    Detail = Fmt.Truncate(e.Detail, 140),
                    When = Fmt.LocalDt(e.At),
                    Unread = e.Id > lastSeen,
                };
            }).ToList();
            return new NotificationFeed { Unread = unread, Items = items };
        });
    }

    /// <summary>Marca como vistas todas las acciones registradas hasta ahora.</summary>
    public void MarkAllRead(int? userId)
    {
        if (userId == null) return;
        _store.Write(d =>
        {
            var u = d.Users.FirstOrDefault(x => x.Id == userId);
            if (u != null) u.LastSeenEventId = d.Events.Count == 0 ? 0 : d.Events.Max(e => e.Id);
            return 0;
        });
    }
}
