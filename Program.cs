using System.Security.Claims;
using BkIncidentes.Controllers;
using BkIncidentes.Data;
using BkIncidentes.Domain;
using BkIncidentes.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

// Uso:  dotnet run                  arranca el portal en http://localhost:5000
//       dotnet run -- --reset       borra la base y la siembra de nuevo
//       dotnet run -- --selftest    ejecuta las pruebas internas y termina
if (args.Contains("--selftest")) return SelfTest.Run();

var builder = WebApplication.CreateBuilder(args);
var env = builder.Environment;
var secureCookies = Environment.GetEnvironmentVariable("BK_SECURE_COOKIES") == "1";

var dataDir = Environment.GetEnvironmentVariable("BK_DATA_DIR") ?? Path.Combine(env.ContentRootPath, "App_Data");
var dbPath = Path.Combine(dataDir, "portal.json");
var seedFile = Path.Combine(env.ContentRootPath, "Data", "seed_incidents.json");
if (!File.Exists(seedFile)) seedFile = Path.Combine(AppContext.BaseDirectory, "Data", "seed_incidents.json");

if (args.Contains("--reset") && File.Exists(dbPath))
{
    File.Delete(dbPath);
    Console.WriteLine("Base reiniciada: se volverá a sembrar con datos de ejemplo.");
}

var store = new JsonFileDataStore(dbPath, d =>
{
    Seeder.SeedBase(d, seedFile);
    Seeder.SeedIncidents(d, seedFile);
});
builder.Services.AddSingleton<IDataStore>(store);
builder.Services.AddSingleton<IncidentService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<LoginThrottle>();

builder.Services.AddControllersWithViews(o =>
{
    // CSRF en TODA petición que modifica datos (formularios y API).
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.Configure<FormOptions>(o => o.ValueCountLimit = 64);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-Token";
    o.Cookie.Name = ".bk.csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

// Las claves de cifrado de cookies viven junto a la base; la carpeta App_Data está en .gitignore.
builder.Services.AddDataProtection()
    .SetApplicationName("bk-incidentes")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = ".bk.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = false;
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/error/403";
        o.ReturnUrlParameter = "next";
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 401;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        // 403 real (no redirección): lo muestra la página de errores.
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
        // Si el usuario ya no existe, la sesión deja de valer.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var exists = id != null && ctx.HttpContext.RequestServices.GetRequiredService<IDataStore>()
                .Read(d => d.Users.Any(u => u.Id.ToString() == id));
            if (!exists)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler("/error/500");
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "same-origin";
    h["X-Frame-Options"] = "DENY";
    if (!ctx.Request.Path.StartsWithSegments("/css") && !ctx.Request.Path.StartsWithSegments("/js"))
        h["Cache-Control"] = "no-store";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine($"Base de datos: {dbPath}");
app.Run();
return 0;
