using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BkIncidentes.Domain;

namespace BkIncidentes.Services;

public sealed class Suggestion
{
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("category_label")] public string? CategoryLabel { get; set; }
    [JsonPropertyName("priority")] public string Priority { get; set; } = "media";
    [JsonPropertyName("priority_label")] public string PriorityLabel { get; set; } = "";
    [JsonPropertyName("confidence")] public string? Confidence { get; set; }
    [JsonPropertyName("first_step")] public string FirstStep { get; set; } = "";
    [JsonPropertyName("reasons")] public List<string> Reasons { get; set; } = new();
}

/// <summary>
/// Asistente de triaje por reglas determinísticas. Sin IA, sin red, sin costo: corre dentro del proceso.
/// Es una propuesta que la persona revisa y corrige; cada sugerencia explica en qué palabras se basó.
/// </summary>
public static class Assistant
{
    // Mapeo explícito de acentos: no depende de ICU ni de string.Normalize(), que con
    // InvariantGlobalization no descompone caracteres no ASCII.
    private const string Accented = "áàäâãéèëêíìïîóòöôõúùüûñÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑ";
    private const string Plain = "aaaaaeeeeiiiiooooouuuunAAAAAEEEEIIIIOOOOOUUUUN";

    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var i = Accented.IndexOf(c);
            sb.Append(i >= 0 ? Plain[i] : c);
        }
        var clean = Regex.Replace(sb.ToString().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        return $" {clean} ";
    }

    // Un '*' final significa «cualquier terminación».
    private static readonly Dictionary<string, string[]> CategoryKeywords = new()
    {
        ["kiosco"] = new[] { "kiosc*", "kiosk*", "autoservicio", "autopedido", "self service", "pedido en pantalla" },
        ["pos"] = new[] { "pos", "caja", "cajas", "cajero", "cajera", "punto de venta", "datafono*", "terminal de pago",
                          "tarjeta*", "cobro*", "cobrar", "cierre de caja", "descuadr*" },
        ["conectividad"] = new[] { "internet", "wifi", "wi fi", "red", "router", "switch", "vpn", "enlace", "latencia",
                                   "conectividad", "conexion", "firewall", "dns", "modem", "access point", "ping" },
        ["impresoras"] = new[] { "impresora*", "imprim*", "impres*", "comanda*", "papel", "rollo*", "termica", "toner", "tinta" },
        ["menu_digital"] = new[] { "menu", "menus", "menu digital", "pantalla de menu", "cartelera", "digital signage",
                                   "menu board", "combo*", "promocion*", "foto*", "precios en pantalla" },
        ["facturacion"] = new[] { "factur*", "comprobante*", "hacienda", "nota de credito", "consecutivo*", "xml",
                                  "clave numerica", "iva", "tributacion" },
    };

    private static readonly string[] CriticalPatterns =
    {
        @"\bno (se )?(puede|pueden|logra|logran)\w* (cobrar|vender|facturar|procesar)",
        @"\bno (cobra|cobran|vende|venden|factura|facturan|procesa|procesan)\w*",
        @"\bsin (ventas|internet|conexion|sistema|energia)",
        @"\bcaid[oa]s?\b",
        @"\bfuera de servicio",
        @"\bno enciende",
        @"\bno (hay|tiene|tienen) (internet|conexion)",
        @"\bpedidos?(?: \w+){0,3} (fallan|fallando|no llegan|no entran|no procesan|perdidos)",
        @"\btodos? los pedidos",
    };
    private static readonly string[] HighPatterns =
    {
        @"\bfalla\w*", @"\berror\w*", @"\bno imprim\w*", @"\brechaz\w*",
        @"\bse (cae|caen|congela|congelan|cuelga|cuelgan|reinicia|reinician)",
        @"\bintermiten\w*", @"\bno sincroniza\w*", @"\bno carga\w*", @"\bbloquead\w*",
        @"\bcongelad\w*", @"\bno responde\w*",
    };
    private static readonly string[] ScopePatterns =
    {
        @"\btod[oa]s?\b", @"\bvari[oa]s\b",
        @"\b(dos|tres|cuatro|cinco|multiples) (restaurantes|locales|sucursales|tiendas|kioscos|cajas)",
        @"\bhora pico\b",
    };
    private static readonly string[] MinorPatterns =
    {
        @"\bno urgente\b", @"\bcuando pueda\b", @"\bsugerencia\b", @"\bmejora\b", @"\bcosmetic\w*", @"\bortografi\w*",
    };

    private static readonly Dictionary<string, string> FirstSteps = new()
    {
        ["kiosco"] = "Confirme si el fallo es de un solo kiosco o de todos los del local. Reinicie la aplicación del " +
                     "kiosco, verifique su conectividad y haga un pedido de prueba. Si hubo cobros sin pedido, no " +
                     "reintente: concilie primero para no duplicar ventas.",
        ["pos"] = "Verifique si falla una sola caja o todas. Anote el mensaje de error exacto y la hora del primer " +
                  "fallo, revise la red y el datáfono, y pruebe una venta de prueba.",
        ["conectividad"] = "Revise el estado del router y del enlace, haga ping a la puerta de enlace y a un destino " +
                           "externo, y valide DNS. Determine si afecta a todos los equipos del local o solo a algunos.",
        ["impresoras"] = "Revise papel, tapa y cable o red de la impresora, imprima la página de autodiagnóstico y " +
                         "confirme que el POS apunta a la impresora correcta.",
        ["menu_digital"] = "Identifique qué pantallas fallan y si muestran contenido en caché. Revise la conexión del " +
                           "reproductor y la última publicación del menú, y compare los precios con los del POS.",
        ["facturacion"] = "Identifique el comprobante afectado y el mensaje de rechazo. Verifique consecutivo y datos del " +
                          "cliente, y la conexión con el servicio de facturación. No emita duplicados sin validar.",
    };
    private static readonly Dictionary<string, string> PriorityNotes = new()
    {
        ["critica"] = "Avise a Operaciones y active el plan de contingencia (vender por el canal que sí funciona).",
        ["alta"] = "Atender en la próxima hora.",
    };
    private const string NoMatchStep =
        "No hubo coincidencias claras. Pida más detalle (qué equipo, desde cuándo, qué mensaje de error " +
        "aparece) y seleccione la categoría manualmente.";

    private sealed record Kw(string Word, Regex Rx);

    private static readonly Dictionary<string, List<Kw>> CategoryRegex = CategoryKeywords.ToDictionary(
        kv => kv.Key, kv => kv.Value.Select(BuildKw).ToList());

    private static Kw BuildKw(string keyword)
    {
        var kw = Normalize(keyword).Trim();
        var pattern = keyword.EndsWith('*')
            ? @"\b" + Regex.Escape(kw)
            : @"\b" + Regex.Escape(kw) + @"(?:s|es)?\b";
        return new Kw(keyword.TrimEnd('*'), new Regex(pattern, RegexOptions.CultureInvariant));
    }

    private static List<string> Hits(string[] patterns, string text)
    {
        var found = new List<string>();
        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.CultureInvariant);
            if (m.Success) found.Add(m.Value.Trim());
        }
        return found;
    }

    public static Suggestion Suggest(string? description)
    {
        var text = Normalize(description ?? "");

        // Puntaje por categoría; el desempate respeta el orden del catálogo.
        var order = Catalog.Categories.Select(c => c.Key).ToList();
        var scores = new Dictionary<string, int>();
        var matched = new Dictionary<string, List<string>>();
        foreach (var (cat, regs) in CategoryRegex)
        {
            var words = regs.Where(r => r.Rx.IsMatch(text)).Select(r => r.Word).ToList();
            scores[cat] = words.Count;
            matched[cat] = words;
        }
        var ranked = scores.OrderByDescending(kv => kv.Value).ThenBy(kv => order.IndexOf(kv.Key)).ToList();
        var best = ranked[0];
        var second = ranked[1].Value;

        string? category = best.Value > 0 ? best.Key : null;
        string? confidence;
        if (best.Value == 0) confidence = null;
        else if (best.Value >= 2 && best.Value > second) confidence = "alta";
        else if (best.Value > second) confidence = "media";
        else confidence = "baja"; // empate entre categorías: la persona debe decidir

        var critical = Hits(CriticalPatterns, text);
        var high = Hits(HighPatterns, text);
        var scope = Hits(ScopePatterns, text);
        var minor = Hits(MinorPatterns, text);

        var score = 2; // media por defecto
        if (critical.Count > 0) score += 3;
        else if (high.Count > 0) score += 1;
        if ((critical.Count > 0 || high.Count > 0) && scope.Count > 0) score += 1;
        if (minor.Count > 0) score -= 2;
        var priority = score >= 5 ? "critica" : score >= 3 ? "alta" : score == 2 ? "media" : "baja";

        var reasons = new List<string>();
        if (category != null)
        {
            reasons.Add($"Categoría por coincidencias: {string.Join(", ", matched[category])}.");
            if (confidence == "baja")
            {
                var others = ranked.Skip(1).Where(kv => kv.Value == best.Value).Select(kv => Catalog.CategoryLabel(kv.Key));
                reasons.Add($"Empate con: {string.Join(", ", others)}. Revise la categoría con cuidado.");
            }
        }
        else reasons.Add("Ninguna palabra clave de categoría coincidió.");
        if (critical.Count > 0) reasons.Add($"Señales de impacto crítico: {string.Join(", ", critical)}.");
        if (high.Count > 0) reasons.Add($"Señales de falla: {string.Join(", ", high)}.");
        if (scope.Count > 0) reasons.Add($"Señales de alcance amplio: {string.Join(", ", scope)}.");
        if (minor.Count > 0) reasons.Add($"Señales de baja urgencia: {string.Join(", ", minor)}.");
        if (critical.Count == 0 && high.Count == 0 && minor.Count == 0)
            reasons.Add("Sin señales de severidad: prioridad media por defecto.");

        var step = category != null && FirstSteps.TryGetValue(category, out var s) ? s : NoMatchStep;
        if (PriorityNotes.TryGetValue(priority, out var note)) step = $"{note} {step}";

        return new Suggestion
        {
            Category = category,
            CategoryLabel = category == null ? null : Catalog.CategoryLabel(category),
            Priority = priority,
            PriorityLabel = Catalog.PriorityLabel(priority),
            Confidence = confidence,
            FirstStep = step,
            Reasons = reasons,
        };
    }
}
