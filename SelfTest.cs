using BkIncidentes.Controllers;
using BkIncidentes.Data;
using BkIncidentes.Domain;
using BkIncidentes.Models;
using BkIncidentes.Services;

/// <summary>
/// Pruebas internas sin paquetes externos:  dotnet run -- --selftest
/// Cubren asistente, reglas de negocio, persistencia, semilla, analítica, validación y concurrencia.
/// </summary>
public static class SelfTest
{
    private static int _passed, _failed;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _passed++; else _failed++;
        Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {name}{(ok || detail == "" ? "" : "  → " + detail)}");
    }

    private static IncidentData Sample(string cat = "pos", string prio = "alta", string desc = "La caja 2 no cobra con tarjeta desde hace una hora") => new()
    {
        RestaurantId = 1, Category = cat, Priority = prio, Description = desc, Responsible = "Ana Mora",
    };

    public static int Run()
    {
        var seedFile = Path.Combine(Directory.GetCurrentDirectory(), "Data", "seed_incidents.json");
        if (!File.Exists(seedFile)) seedFile = Path.Combine(AppContext.BaseDirectory, "Data", "seed_incidents.json");
        var tmp = Path.Combine(Path.GetTempPath(), "bk-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Console.WriteLine("Asistente de triaje");
            var s = Assistant.Suggest("Los pedidos del kiosco fallan en tres restaurantes; el mostrador sigue vendiendo.");
            Check("caso crítico: categoría kiosco", s.Category == "kiosco", s.Category ?? "null");
            Check("caso crítico: prioridad crítica", s.Priority == "critica", s.Priority);
            Check("caso crítico: primer paso menciona no duplicar ventas", s.FirstStep.Contains("duplicar ventas"));
            Check("explica sus razones", s.Reasons.Count >= 2);
            var s2 = Assistant.Suggest("La impresora de cocina no imprime, se acabó el papel.");
            Check("impresora que no imprime → impresoras / alta", s2.Category == "impresoras" && s2.Priority == "alta", $"{s2.Category}/{s2.Priority}");
            var s3 = Assistant.Suggest("Mejora cosmética del menú digital, no urgente.");
            Check("baja urgencia → baja", s3.Priority == "baja" && s3.Category == "menu_digital", $"{s3.Category}/{s3.Priority}");
            var s4 = Assistant.Suggest("Algo raro pasó ayer con el tema aquel");
            Check("sin coincidencias no inventa categoría", s4.Category == null && s4.Priority == "media");
            Check("acentos y mayúsculas no importan", Assistant.Suggest("FACTURACIÓN: comprobante rechazado por Hacienda").Category == "facturacion");

            Console.WriteLine("Contraseñas");
            var h = PasswordHasher.Hash("Secreta-123");
            Check("verifica contraseña correcta", PasswordHasher.Verify("Secreta-123", h));
            Check("rechaza contraseña incorrecta", !PasswordHasher.Verify("otra", h));
            Check("hashes distintos por sal", PasswordHasher.Hash("Secreta-123") != h);
            Check("rechaza hash malformado", !PasswordHasher.Verify("x", "basura"));

            Console.WriteLine("Semilla");
            var seedPath = Path.Combine(tmp, "seed.json");
            var seeded = new JsonFileDataStore(seedPath, d => { Seeder.SeedBase(d, seedFile); Seeder.SeedIncidents(d, seedFile); });
            var svc = new IncidentService(seeded);
            var all = svc.All();
            Check("al menos 12 incidentes", all.Count >= 12, all.Count.ToString());
            Check("al menos 4 restaurantes con incidentes", all.Select(r => r.Incident.RestaurantId).Distinct().Count() >= 4);
            Check("hay reabiertos y repetidos para el dashboard", all.Any(r => r.Incident.ReopenCount > 0));
            Check("dos usuarios (admin y consulta)", seeded.Read(d => d.Users.Select(u => u.Role).OrderBy(x => x).SequenceEqual(new[] { "admin", "consulta" })));
            Check("sin eventos en el futuro", seeded.Read(d => d.Events.All(e => e.At <= DateTime.UtcNow.AddMinutes(1))));

            Console.WriteLine("Ciclo de vida");
            var id = svc.Create(Sample(), "t", IncidentService.UtcNow(), Assistant.Suggest("x"));
            Check("nace en estado nuevo", svc.Get(id)!.Incident.Status == "nuevo");
            Check("no se puede cerrar un incidente nuevo", !svc.Transition(id, "cerrar", "t", IncidentService.UtcNow()).Ok);
            Check("gestionar OK", svc.Transition(id, "gestionar", "t", IncidentService.UtcNow()).Ok);
            Check("no se puede gestionar dos veces", !svc.Transition(id, "gestionar", "t", IncidentService.UtcNow()).Ok);
            Check("resolver OK", svc.Transition(id, "resolver", "t", IncidentService.UtcNow(), "listo").Ok);
            Check("cerrar OK", svc.Transition(id, "cerrar", "t", IncidentService.UtcNow()).Ok);
            Check("no se puede resolver un cerrado", !svc.Transition(id, "resolver", "t", IncidentService.UtcNow()).Ok);
            Check("reabrir OK", svc.Transition(id, "reabrir", "t", IncidentService.UtcNow(), "volvió a fallar").Ok);
            var re = svc.Get(id)!.Incident;
            Check("reabierto: en gestión, contador 1, sin fechas de cierre",
                re.Status == "en_gestion" && re.ReopenCount == 1 && re.ResolvedAt == null && re.ClosedAt == null);
            Check("historial completo", svc.Events(id).Select(e => e.Action).SequenceEqual(new[] { "creado", "en_gestion", "resuelto", "cerrado", "reabierto" }));
            Check("acción inexistente rechazada", !svc.Transition(id, "borrar", "t", IncidentService.UtcNow()).Ok);

            Console.WriteLine("Edición");
            var changes = svc.Update(id, Sample("kiosco", "critica", "Descripción nueva más larga"), "t", IncidentService.UtcNow());
            Check("registra qué cambió", changes.Count >= 3 && svc.Events(id).Last().Action == "editado");
            Check("sin cambios no genera evento", svc.Update(id, Sample("kiosco", "critica", "Descripción nueva más larga"), "t", IncidentService.UtcNow()).Count == 0);

            Console.WriteLine("Filtros");
            var f = new IncidentFilter { Priority = "critica", Status = "abiertos" };
            var crit = svc.List(f, 1);
            Check("filtro crítica + abiertos", crit.Rows.All(r => r.Incident.Priority == "critica" && r.Incident.Status is "nuevo" or "en_gestion"));
            Check("filtro por restaurante", svc.List(new IncidentFilter { Restaurant = 1 }, 1).Rows.All(r => r.Incident.RestaurantId == 1));
            Check("búsqueda por código INC-000N", svc.List(new IncidentFilter { Q = "INC-0001" }, 1).Rows.Any(r => r.Incident.Id == 1));
            Check("búsqueda por texto", svc.List(new IncidentFilter { Q = "kiosco" }, 1).Total > 0);
            Check("paginación (15 por página)", svc.List(new IncidentFilter(), 1).Rows.Count == Math.Min(15, svc.List(new IncidentFilter(), 1).Total));
            Check("filtros sin resultados → vacío", svc.List(new IncidentFilter { Q = "zzzz-no-existe" }, 1).Total == 0);

            Console.WriteLine("Analítica");
            var dash = Analytics.Compute(svc.All(), IncidentService.UtcNow());
            Check("incidente más repetitivo = kiosco", dash.TopCategory?.Key == "kiosco", dash.TopCategory?.Key ?? "null");
            Check("hay casos repetidos", dash.RepetidosN > 0, dash.RepetidosN.ToString());
            Check("hay reabiertos", dash.ReabiertosN > 0);
            Check("tiempo de atención calculado", dash.Atencion.Avg > 0 && dash.Atencion.N > 0);
            Check("tiempo de solución calculado", dash.Solucion.Avg > 0 && dash.Solucion.N > 0);
            Check("totales consistentes", dash.Total == dash.Abiertos + dash.Resueltos + dash.Cerrados, $"{dash.Total} vs {dash.Abiertos}+{dash.Resueltos}+{dash.Cerrados}");
            Check("periodo de 7 días reduce el total", Analytics.Compute(svc.All(), IncidentService.UtcNow(), 7).Total < dash.Total);
            Check("duraciones legibles", Fmt.Duration(45) == "menos de 1 min" && Fmt.Duration(3600 + 120) == "1 h 2 min" && Fmt.Duration(90000) == "1 d 1 h");
            Check("hora local = UTC-6", Fmt.LocalDt(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)) == "01/01/2026 06:00");

            Console.WriteLine("Validación");
            var ids = new HashSet<int> { 1, 2 };
            var ok = IncidentsController.Validate(new IncidentForm { RestaurantId = "1", Category = "pos", Priority = "alta", Description = "Descripción válida larga", Responsible = "Ana Mora" }, ids);
            Check("formulario válido", ok.Errors.Count == 0);
            var bad = IncidentsController.Validate(new IncidentForm { RestaurantId = "99", Category = "hack", Priority = "urgente", Description = "corta", Responsible = "<script>" }, ids);
            Check("rechaza restaurante, categoría, prioridad, descripción y responsable inválidos", bad.Errors.Count == 5, bad.Errors.Count.ToString());
            Check("elimina caracteres de control", IncidentsController.CleanText("a\u0000b\u0007c  ") == "abc");
            var empty = IncidentsController.Validate(new IncidentForm(), ids);
            Check("campos nulos no rompen la validación", empty.Errors.Count >= 4);

            Console.WriteLine("Responsables");
            var resp = dash.Responsables;
            Check("hay filas por responsable", resp.Count >= 2, resp.Count.ToString());
            Check("los totales por responsable suman el total", resp.Sum(r => r.Total) == dash.Total, $"{resp.Sum(r => r.Total)} vs {dash.Total}");
            Check("abiertos por responsable suman los abiertos", resp.Sum(r => r.Abiertos) == dash.Abiertos);
            var ana = resp.FirstOrDefault(r => r.Name == "Ana Mora");
            Check("Ana Mora tiene incidentes y categorías", ana != null && ana.Total > 0 && ana.Categorias.Sum(c => c.Count) == ana.Total);
            Check("sin asignar va al final", resp.Count(r => r.FilterValue == IncidentService.NoResponsible) == 1 && resp[^1].FilterValue == IncidentService.NoResponsible);
            var mixed = new IncidentService(new JsonFileDataStore(Path.Combine(tmp, "mixed.json"), d => Seeder.SeedBase(d, seedFile)));
            mixed.Create(Sample(desc: "Primer incidente de prueba largo"), "t", IncidentService.UtcNow(), Assistant.Suggest("x"));
            var s5 = Sample(desc: "Segundo incidente de prueba largo"); s5.Responsible = "  ANA   mora ";
            mixed.Create(s5, "t", IncidentService.UtcNow(), Assistant.Suggest("x"));
            Check("agrupa sin distinguir mayúsculas ni espacios", Analytics.Compute(mixed.All(), IncidentService.UtcNow()).Responsables.Count(r => r.Name != "Sin asignar") == 1);
            var byAna = svc.List(new IncidentFilter { Responsible = "ana mora" }, 1);
            Check("filtro por responsable", byAna.Total > 0 && byAna.Rows.All(r => IncidentService.RespKey(r.Incident.Responsible) == "ana mora"));
            var none = svc.List(new IncidentFilter { Responsible = IncidentService.NoResponsible }, 1);
            Check("filtro sin asignar", none.Total > 0 && none.Rows.All(r => r.Incident.Responsible.Trim() == ""));

            Console.WriteLine("Campanita");
            var notes = new NotificationService(seeded);
            var uid = seeded.Read(d => d.Users.First(u => u.Role == "admin").Id);
            var first = notes.Feed(uid);
            Check("primera consulta: el historial no cuenta como nuevo", first.Unread == 0 && first.Items.Count == NotificationService.FeedSize);
            var nid = svc.Create(Sample(desc: "Incidente para la campanita de prueba"), "Ana Mora", IncidentService.UtcNow(), Assistant.Suggest("x"));
            var f1 = notes.Feed(uid);
            Check("una acción nueva suma uno", f1.Unread == 1 && f1.Items[0].IncidentId == nid && f1.Items[0].Action == "creado" && f1.Items[0].Unread);
            svc.Transition(nid, "gestionar", "Ana Mora", IncidentService.UtcNow());
            svc.Update(nid, Sample("pos", "media", "Descripción editada para la campanita"), "Ana Mora", IncidentService.UtcNow());
            svc.Transition(nid, "resolver", "Ana Mora", IncidentService.UtcNow(), "ok");
            svc.Transition(nid, "cerrar", "Ana Mora", IncidentService.UtcNow());
            svc.Transition(nid, "reabrir", "Ana Mora", IncidentService.UtcNow(), "volvió a fallar");
            var f2 = notes.Feed(uid);
            Check("editar, gestionar, resolver, cerrar y reabrir aparecen", f2.Unread == 6 && f2.Items.Take(5).Select(i => i.Action).SequenceEqual(new[] { "reabierto", "cerrado", "resuelto", "editado", "en_gestion" }), string.Join(",", f2.Items.Take(5).Select(i => i.Action)));
            var other = seeded.Read(d => d.Users.First(u => u.Role == "consulta").Id);
            notes.Feed(other);
            svc.Transition(nid, "resolver", "Ana Mora", IncidentService.UtcNow());
            Check("cada persona lleva su propia cuenta", notes.Feed(uid).Unread == 7 && notes.Feed(other).Unread == 1);
            notes.MarkAllRead(uid);
            Check("marcar como leídas deja la campanita en cero", notes.Feed(uid).Unread == 0 && notes.Feed(other).Unread == 1);
            Check("usuario inexistente no rompe", notes.Feed(9999).Unread == 0);

            Console.WriteLine("Persistencia y concurrencia");
            var reopened = new IncidentService(new JsonFileDataStore(seedPath));
            Check("los datos sobreviven a un reinicio", reopened.Get(id)?.Incident.ReopenCount == 1 && reopened.All().Count == svc.All().Count);
            Check("no queda archivo temporal", !File.Exists(seedPath + ".tmp"));
            var before = svc.All().Count;
            Parallel.For(0, 60, _ => svc.Create(Sample(), "t", IncidentService.UtcNow(), Assistant.Suggest("x")));
            var after = svc.All();
            Check("60 altas concurrentes sin pérdidas", after.Count == before + 60, $"{after.Count - before}");
            Check("identificadores únicos", after.Select(r => r.Incident.Id).Distinct().Count() == after.Count);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* limpieza best-effort */ }
        }

        Console.WriteLine($"\n{_passed} correctas, {_failed} con falla.");
        return _failed == 0 ? 0 : 1;
    }
}
