using System.Text.RegularExpressions;
using BkIncidentes.Domain;
using BkIncidentes.Models;
using BkIncidentes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BkIncidentes.Controllers;

[Authorize]
public class IncidentsController : PortalController
{
    private static readonly Regex ControlChars = new(@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", RegexOptions.Compiled);
    private static readonly Regex ResponsibleOk = new(@"^[\w .,'()\-]*$", RegexOptions.Compiled);

    private readonly IncidentService _svc;

    public IncidentsController(IncidentService svc) => _svc = svc;

    // ------------------------------------------------------------ utilidades
    public static string CleanText(string? value)
    {
        var v = (value ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        return ControlChars.Replace(v, "").Trim();
    }

    /// <summary>La validación vive en el servidor; el HTML solo ayuda a la persona usuaria.</summary>
    public static (IncidentData Data, Dictionary<string, string> Errors) Validate(IncidentForm f, ISet<int> restaurantIds)
    {
        var errors = new Dictionary<string, string>();
        int rid = 0;
        if (!int.TryParse(f.RestaurantId, out rid) || !restaurantIds.Contains(rid))
            errors["restaurant_id"] = "Seleccione un restaurante de la lista.";
        var category = f.Category ?? "";
        if (!Catalog.IsCategory(category)) errors["category"] = "Seleccione una categoría válida.";
        var priority = f.Priority ?? "";
        if (!Catalog.IsPriority(priority)) errors["priority"] = "Seleccione una prioridad válida.";

        var description = CleanText(f.Description);
        if (description.Length < Catalog.DescriptionMin)
            errors["description"] = $"Describa el incidente (mínimo {Catalog.DescriptionMin} caracteres).";
        else if (description.Length > Catalog.DescriptionMax)
            errors["description"] = $"La descripción no puede superar {Catalog.DescriptionMax} caracteres.";

        var responsible = CleanText(f.Responsible);
        if (responsible.Length > Catalog.ResponsibleMax)
            errors["responsible"] = $"El responsable no puede superar {Catalog.ResponsibleMax} caracteres.";
        else if (!ResponsibleOk.IsMatch(responsible))
            errors["responsible"] = "El responsable solo admite letras, números, espacios y . , ' ( ) -";

        return (new IncidentData
        {
            RestaurantId = rid, Category = category, Priority = priority,
            Description = description, Responsible = responsible,
        }, errors);
    }

    private static Dictionary<string, string> ValuesOf(IncidentForm f) => new()
    {
        ["restaurant_id"] = f.RestaurantId ?? "", ["category"] = f.Category ?? "", ["priority"] = f.Priority ?? "",
        ["description"] = f.Description ?? "", ["responsible"] = f.Responsible ?? "",
    };

    private static Dictionary<string, string> ValuesOf(Incident i) => new()
    {
        ["restaurant_id"] = i.RestaurantId.ToString(), ["category"] = i.Category, ["priority"] = i.Priority,
        ["description"] = i.Description, ["responsible"] = i.Responsible,
    };

    private ISet<int> RestaurantIds(List<Restaurant> rests) => rests.Select(r => r.Id).ToHashSet();

    // ---------------------------------------------------------------- listado
    [HttpGet("/incidentes")]
    public IActionResult Index([FromQuery] string? restaurante, [FromQuery] string? prioridad, [FromQuery] string? estado,
                               [FromQuery] string? categoria, [FromQuery] string? q, [FromQuery] string? responsable,
                               [FromQuery] int pagina = 1)
    {
        var resp = CleanText(responsable);
        if (resp.Length > Catalog.ResponsibleMax || (resp != IncidentService.NoResponsible && !ResponsibleOk.IsMatch(resp))) resp = "";
        // Lo que no sea válido se ignora.
        var f = new IncidentFilter
        {
            Restaurant = int.TryParse(restaurante, out var rid) && rid > 0 ? rid : null,
            Priority = Catalog.IsPriority(prioridad) ? prioridad! : "",
            Status = estado == "abiertos" || Catalog.IsStatus(estado) ? estado! : "",
            Category = Catalog.IsCategory(categoria) ? categoria! : "",
            Q = CleanText(q).Length > 80 ? CleanText(q)[..80] : CleanText(q),
            Responsible = resp,
        };
        return View(new ListVm
        {
            Data = _svc.List(f, pagina), F = f, Restaurants = _svc.Restaurants(),
            Responsibles = _svc.Responsibles(), AnyUnassigned = _svc.AnyUnassigned(),
        });
    }

    // ------------------------------------------------------------------- alta
    [Authorize(Roles = Catalog.Admin)]
    [HttpGet("/incidentes/nuevo")]
    public IActionResult New() =>
        View("Form", new FormVm
        {
            IsNew = true,
            Values = new() { ["priority"] = "media" },
            Restaurants = _svc.Restaurants(),
        });

    [Authorize(Roles = Catalog.Admin)]
    [HttpPost("/incidentes")]
    public IActionResult Create([FromForm] IncidentForm form)
    {
        var rests = _svc.Restaurants();
        var (data, errors) = Validate(form, RestaurantIds(rests));
        if (errors.Count > 0)
        {
            FlashNow("error", "Revise los campos marcados: el incidente no se guardó.");
            Response.StatusCode = 400;
            return View("Form", new FormVm { IsNew = true, Values = ValuesOf(form), Errors = errors, Restaurants = rests });
        }
        var id = _svc.Create(data, Actor, IncidentService.UtcNow(), Assistant.Suggest(data.Description), UserId);
        Flash("success", $"Incidente {Fmt.Code(id)} registrado correctamente.");
        return RedirectToAction("Detail", new { id });
    }

    // ---------------------------------------------------------------- detalle
    [HttpGet("/incidentes/{id:int}")]
    public IActionResult Detail(int id)
    {
        var row = _svc.Get(id);
        if (row == null) return NotFound();
        var i = row.Incident;
        return View(new DetailVm
        {
            Row = row,
            Events = _svc.Events(id),
            Atencion = i.FirstResponseAt != null ? (i.FirstResponseAt.Value - i.CreatedAt).TotalSeconds : null,
            Solucion = i.ResolvedAt != null ? (i.ResolvedAt.Value - i.CreatedAt).TotalSeconds : null,
        });
    }

    // ----------------------------------------------------------------- edición
    [Authorize(Roles = Catalog.Admin)]
    [HttpGet("/incidentes/{id:int}/editar")]
    public IActionResult Edit(int id)
    {
        var row = _svc.Get(id);
        if (row == null) return NotFound();
        if (row.Incident.Status == "cerrado")
        {
            Flash("error", "Un incidente cerrado no se edita. Reábralo primero si necesita cambiarlo.");
            return RedirectToAction("Detail", new { id });
        }
        return View("Form", new FormVm { IsNew = false, Incident = row.Incident, Values = ValuesOf(row.Incident), Restaurants = _svc.Restaurants() });
    }

    [Authorize(Roles = Catalog.Admin)]
    [HttpPost("/incidentes/{id:int}/editar")]
    public IActionResult Update(int id, [FromForm] IncidentForm form)
    {
        var row = _svc.Get(id);
        if (row == null) return NotFound();
        if (row.Incident.Status == "cerrado")
        {
            Flash("error", "Un incidente cerrado no se edita. Reábralo primero si necesita cambiarlo.");
            return RedirectToAction("Detail", new { id });
        }
        var rests = _svc.Restaurants();
        var (data, errors) = Validate(form, RestaurantIds(rests));
        if (errors.Count > 0)
        {
            FlashNow("error", "Revise los campos marcados: los cambios no se guardaron.");
            Response.StatusCode = 400;
            return View("Form", new FormVm { IsNew = false, Incident = row.Incident, Values = ValuesOf(form), Errors = errors, Restaurants = rests });
        }
        var changes = _svc.Update(id, data, Actor, IncidentService.UtcNow());
        Flash(changes.Count > 0 ? "success" : "info", changes.Count > 0 ? "Cambios guardados." : "No había cambios que guardar.");
        return RedirectToAction("Detail", new { id });
    }

    // ------------------------------------------------------------- transiciones
    [Authorize(Roles = Catalog.Admin)]
    [HttpPost("/incidentes/{id:int}/transicion")]
    public IActionResult Transition(int id, [FromForm] string? action, [FromForm] string? note)
    {
        if (_svc.Get(id) == null) return NotFound();
        action ??= "";
        var n = CleanText(note);
        if (n.Length > Catalog.NoteMax)
        {
            Flash("error", $"La nota no puede superar {Catalog.NoteMax} caracteres.");
            return RedirectToAction("Detail", new { id });
        }
        if (action == "reabrir" && n.Length < 5)
        {
            Flash("error", "Indique el motivo de la reapertura (mínimo 5 caracteres).");
            return RedirectToAction("Detail", new { id });
        }
        var (ok, err) = _svc.Transition(id, action, Actor, IncidentService.UtcNow(), n);
        if (ok)
        {
            Flash("success", action switch
            {
                "gestionar" => "El incidente pasó a en gestión.",
                "resolver" => "Incidente marcado como resuelto.",
                "cerrar" => "Incidente cerrado.",
                _ => "Incidente reabierto.",
            });
        }
        else Flash("error", err);
        return RedirectToAction("Detail", new { id });
    }

    // --------------------------------------------------------------- asistente
    [Authorize(Roles = Catalog.Admin)]
    [HttpPost("/api/sugerir")]
    public IActionResult Suggest([FromBody] SuggestRequest? req)
    {
        var description = CleanText(req?.Description);
        if (description.Length < Catalog.DescriptionMin)
            return BadRequest(new { error = $"Escriba al menos {Catalog.DescriptionMin} caracteres para sugerir." });
        if (description.Length > Catalog.DescriptionMax) description = description[..Catalog.DescriptionMax];
        return Ok(Assistant.Suggest(description));
    }
}

public class ErrorController : PortalController
{
    [AllowAnonymous]
    [HttpGet("/error/{code:int}")]
    [HttpPost("/error/{code:int}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Index(int code)
    {
        var (title, message) = code switch
        {
            400 => ("Solicitud no válida", "El formulario expiró o no es válido. Recargue la página e intente de nuevo."),
            401 => ("Sesión no iniciada", "Ingrese para continuar."),
            403 => ("Sin permiso", "Su rol no permite realizar esta acción."),
            404 => ("No encontrado", "La página o el incidente no existe."),
            413 => ("Solicitud demasiado grande", "El envío supera el tamaño permitido."),
            _ => ("Error interno", "Ocurrió un error inesperado. Intente de nuevo."),
        };
        var status = code is 400 or 401 or 403 or 404 or 413 ? code : 500;
        Response.StatusCode = status;
        if (Request.Headers.Accept.ToString().Contains("application/json") || Request.Headers.ContainsKey("X-CSRF-Token"))
            return StatusCode(status, new { error = message });
        return View("Index", new ErrorVm { Code = status, Title = title, Message = message });
    }
}
