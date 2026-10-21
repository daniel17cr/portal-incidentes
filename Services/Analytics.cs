using BkIncidentes.Domain;

namespace BkIncidentes.Services;

public sealed class Stats
{
    public int N { get; init; }
    public double? Avg { get; init; }
    public double? Median { get; init; }
    public double? Max { get; init; }
}

public sealed class DistRow
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public int Count { get; set; }
    public int Pct { get; set; }
    public int Bar { get; set; }
}

public sealed class BreakdownRow
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public int Count { get; init; }
    public int Repetidos { get; init; }
    public double? Atencion { get; init; }
    public double? Solucion { get; init; }
}

public sealed class Reincidencia
{
    public string Restaurante { get; init; } = "";
    public string Categoria { get; init; } = "";
    public int Incidentes { get; init; }
    public int Repetidos { get; init; }
}

public sealed class ReabiertoRow
{
    public int Id { get; init; }
    public string Restaurante { get; init; } = "";
    public string Categoria { get; init; } = "";
    public int Veces { get; init; }
}

public sealed class ResponsibleRow
{
    /// <summary>Nombre para mostrar («Sin asignar» si no hay responsable).</summary>
    public string Name { get; init; } = "";
    /// <summary>Valor para filtrar el listado de incidentes.</summary>
    public string FilterValue { get; init; } = "";
    public int Total { get; init; }
    public int Abiertos { get; init; }
    public int Resueltos { get; init; }
    public int Cerrados { get; init; }
    public int CriticosAbiertos { get; init; }
    public double? Atencion { get; init; }
    public double? Solucion { get; init; }
    public int Bar { get; set; }
    /// <summary>Incidentes por categoría, de mayor a menor.</summary>
    public List<(string Label, int Count)> Categorias { get; init; } = new();
}

public sealed class DashboardData
{
    public List<ResponsibleRow> Responsables { get; init; } = new();
    public int Total, Abiertos, CriticosAbiertos, CriticosTotal, Resueltos, Cerrados;
    public List<DistRow> Categories { get; init; } = new();
    public DistRow? TopCategory { get; init; }
    public List<DistRow> Restaurants { get; init; } = new();
    public Stats Atencion { get; init; } = new();
    public Stats Solucion { get; init; } = new();
    public List<BreakdownRow> PerCategory { get; init; } = new();
    public List<BreakdownRow> PerPriority { get; init; } = new();
    public int EsperandoN;
    public double? EsperandoOldest;
    public int RepetidosN, RepetidosPct, Ventana;
    public List<Reincidencia> Reincidencias { get; init; } = new();
    public int ReabiertosN, ReabiertosTotal;
    public List<ReabiertoRow> Reabiertos { get; init; } = new();
    public int AsistenteN;
    public int? AsistenteCategoriaPct, AsistentePrioridadPct;
}

/// <summary>
/// Métricas del dashboard.
/// Tiempo de atención = creación → primera gestión (o resolución directa).
/// Tiempo de solución = creación → resuelto (vigente: si se reabre, se recalcula).
/// Caso repetido = mismo restaurante y categoría que otro incidente de los 7 días previos.
/// </summary>
public static class Analytics
{
    private sealed class Item
    {
        public Incident I = null!;
        public string RestaurantName = "";
        public int? RepeatOf;
        public double? Tta, Ttr;
    }

    private static Stats MakeStats(List<double> v)
    {
        if (v.Count == 0) return new Stats();
        var s = v.OrderBy(x => x).ToList();
        var med = s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0;
        return new Stats { N = v.Count, Avg = v.Average(), Median = med, Max = s[^1] };
    }

    private static double? AvgOrNull(IEnumerable<double> v)
    {
        var l = v.ToList();
        return l.Count == 0 ? null : l.Average();
    }

    public static DashboardData Compute(IReadOnlyList<IncidentRow> rows, DateTime now, int? dias = null)
    {
        var items = rows.Select(r =>
        {
            var i = r.Incident;
            return new Item
            {
                I = i,
                RestaurantName = r.RestaurantName,
                Tta = i.FirstResponseAt != null ? (i.FirstResponseAt.Value - i.CreatedAt).TotalSeconds : null,
                Ttr = (i.Status is "resuelto" or "cerrado") && i.ResolvedAt != null
                    ? (i.ResolvedAt.Value - i.CreatedAt).TotalSeconds : null,
            };
        }).OrderBy(x => x.I.CreatedAt).ThenBy(x => x.I.Id).ToList();

        // Repetidos sobre TODO el historial, para no perder el antecedente de un caso fuera de la ventana de análisis.
        var history = new Dictionary<(int, string), List<Item>>();
        foreach (var d in items)
        {
            var key = (d.I.RestaurantId, d.I.Category);
            if (!history.TryGetValue(key, out var list)) history[key] = list = new List<Item>();
            var recent = list.Where(p => d.I.CreatedAt - p.I.CreatedAt <= TimeSpan.FromDays(Catalog.RepeatWindowDays)).ToList();
            d.RepeatOf = recent.Count > 0 ? recent[^1].I.Id : null;
            list.Add(d);
        }

        var scope = dias == null ? items : items.Where(d => d.I.CreatedAt >= now - TimeSpan.FromDays(dias.Value)).ToList();
        var total = scope.Count;
        var open = scope.Where(d => Catalog.OpenStatuses.Contains(d.I.Status)).ToList();

        List<DistRow> Distribution(IEnumerable<(string Key, string Label)> keysLabels, Func<Item, string> field)
        {
            var counts = scope.GroupBy(field).ToDictionary(g => g.Key, g => g.Count());
            var rowsOut = keysLabels.Select(kl => new DistRow { Key = kl.Key, Label = kl.Label, Count = counts.GetValueOrDefault(kl.Key) }).ToList();
            var mx = rowsOut.Count == 0 ? 0 : rowsOut.Max(r => r.Count);
            foreach (var r in rowsOut)
            {
                r.Pct = total > 0 ? (int)Math.Round(r.Count * 100.0 / total) : 0;
                r.Bar = mx > 0 ? (int)Math.Round(r.Count * 100.0 / mx) : 0;
            }
            return rowsOut;
        }

        var categories = Distribution(Catalog.Categories, d => d.I.Category).OrderByDescending(r => r.Count).ToList();
        var top = categories.Count > 0 && categories[0].Count > 0 ? categories[0] : null;

        var names = new Dictionary<int, string>();
        foreach (var d in scope) names[d.I.RestaurantId] = d.RestaurantName;
        var restaurants = Distribution(
                names.OrderBy(kv => kv.Value, StringComparer.Ordinal).Select(kv => (kv.Key.ToString(), kv.Value)),
                d => d.I.RestaurantId.ToString())
            .OrderByDescending(r => r.Count).ToList();

        List<BreakdownRow> Breakdown(IEnumerable<(string Key, string Label)> keysLabels, Func<Item, string> field) =>
            keysLabels.Select(kl =>
            {
                var g = scope.Where(d => field(d) == kl.Key).ToList();
                return new BreakdownRow
                {
                    Key = kl.Key, Label = kl.Label, Count = g.Count,
                    Repetidos = g.Count(d => d.RepeatOf != null),
                    Atencion = AvgOrNull(g.Where(d => d.Tta != null).Select(d => d.Tta!.Value)),
                    Solucion = AvgOrNull(g.Where(d => d.Ttr != null).Select(d => d.Ttr!.Value)),
                };
            }).ToList();

        var perCategory = Breakdown(Catalog.Categories, d => d.I.Category).OrderByDescending(r => r.Count).ToList();
        var perPriority = Breakdown(Catalog.Priorities, d => d.I.Priority);

        var waiting = scope.Where(d => d.I.Status == "nuevo").ToList();

        // Por responsable: se agrupa sin distinguir mayúsculas ni espacios repetidos; los sin asignar van al final.
        var responsables = scope.GroupBy(d => IncidentService.RespKey(d.I.Responsible)).Select(g =>
        {
            var unassigned = g.Key == "";
            return new ResponsibleRow
            {
                Name = unassigned ? "Sin asignar" : System.Text.RegularExpressions.Regex.Replace(g.First().I.Responsible.Trim(), @"\s+", " "),
                FilterValue = unassigned ? IncidentService.NoResponsible : g.Key,
                Total = g.Count(),
                Abiertos = g.Count(d => Catalog.OpenStatuses.Contains(d.I.Status)),
                Resueltos = g.Count(d => d.I.Status == "resuelto"),
                Cerrados = g.Count(d => d.I.Status == "cerrado"),
                CriticosAbiertos = g.Count(d => d.I.Priority == "critica" && Catalog.OpenStatuses.Contains(d.I.Status)),
                Atencion = AvgOrNull(g.Where(d => d.Tta != null).Select(d => d.Tta!.Value)),
                Solucion = AvgOrNull(g.Where(d => d.Ttr != null).Select(d => d.Ttr!.Value)),
                Categorias = g.GroupBy(d => d.I.Category)
                    .Select(c => (Catalog.CategoryLabel(c.Key), c.Count()))
                    .OrderByDescending(c => c.Item2).ThenBy(c => c.Item1, StringComparer.Ordinal).ToList(),
            };
        })
        .OrderBy(r => r.FilterValue == IncidentService.NoResponsible ? 1 : 0)
        .ThenByDescending(r => r.Abiertos).ThenByDescending(r => r.Total)
        .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var maxResp = responsables.Count == 0 ? 0 : responsables.Max(r => r.Total);
        foreach (var r in responsables) r.Bar = maxResp > 0 ? (int)Math.Round(r.Total * 100.0 / maxResp) : 0;

        var repeated = scope.Where(d => d.RepeatOf != null).ToList();
        var groups = scope.GroupBy(d => (d.I.RestaurantId, d.I.Category)).Select(g => new
        {
            Restaurante = g.Last().RestaurantName,
            Categoria = Catalog.CategoryLabel(g.Key.Category),
            Incidentes = g.Count(),
            Repetidos = g.Count(d => d.RepeatOf != null),
            Ultimo = g.Last().I.CreatedAt,
        }).Where(g => g.Repetidos > 0)
          .OrderByDescending(g => g.Repetidos).ThenByDescending(g => g.Incidentes).ThenByDescending(g => g.Ultimo)
          .Take(6)
          .Select(g => new Reincidencia { Restaurante = g.Restaurante, Categoria = g.Categoria, Incidentes = g.Incidentes, Repetidos = g.Repetidos })
          .ToList();

        var reopened = scope.Where(d => d.I.ReopenCount > 0).OrderByDescending(d => d.I.ReopenCount).ToList();

        var withSug = scope.Where(d => d.I.SuggestedCategory != null).ToList();
        var n = withSug.Count;

        return new DashboardData
        {
            Responsables = responsables,
            Total = total,
            Abiertos = open.Count,
            CriticosAbiertos = open.Count(d => d.I.Priority == "critica"),
            CriticosTotal = scope.Count(d => d.I.Priority == "critica"),
            Resueltos = scope.Count(d => d.I.Status == "resuelto"),
            Cerrados = scope.Count(d => d.I.Status == "cerrado"),
            Categories = categories,
            TopCategory = top,
            Restaurants = restaurants,
            Atencion = MakeStats(scope.Where(d => d.Tta != null).Select(d => d.Tta!.Value).ToList()),
            Solucion = MakeStats(scope.Where(d => d.Ttr != null).Select(d => d.Ttr!.Value).ToList()),
            PerCategory = perCategory,
            PerPriority = perPriority,
            EsperandoN = waiting.Count,
            EsperandoOldest = waiting.Count == 0 ? null : waiting.Max(d => (now - d.I.CreatedAt).TotalSeconds),
            RepetidosN = repeated.Count,
            RepetidosPct = total > 0 ? (int)Math.Round(repeated.Count * 100.0 / total) : 0,
            Ventana = Catalog.RepeatWindowDays,
            Reincidencias = groups,
            ReabiertosN = reopened.Count,
            ReabiertosTotal = reopened.Sum(d => d.I.ReopenCount),
            Reabiertos = reopened.Take(5).Select(d => new ReabiertoRow
            {
                Id = d.I.Id, Restaurante = d.RestaurantName, Categoria = Catalog.CategoryLabel(d.I.Category), Veces = d.I.ReopenCount,
            }).ToList(),
            AsistenteN = n,
            AsistenteCategoriaPct = n > 0 ? (int)Math.Round(withSug.Count(d => d.I.SuggestedCategory == d.I.Category) * 100.0 / n) : null,
            AsistentePrioridadPct = n > 0 ? (int)Math.Round(withSug.Count(d => d.I.SuggestedPriority == d.I.Priority) * 100.0 / n) : null,
        };
    }
}
