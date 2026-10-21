# Portal de gestión de incidentes (prototipo)

Miniportal web para centralizar incidentes de POS, kioscos, conectividad, impresoras, menús digitales y facturación.
**Todos los datos son ficticios.** No se conecta a ningún sistema real ni envía información a terceros.

**Stack:** C# / ASP.NET Core 8 (MVC + Razor) · persistencia en archivo JSON · **cero paquetes NuGet**.

**Colores:** paleta inspirada en BK (marrón `#502314`, rojo `#D62300`, naranja `#FF8732`, crema `#F5EBDC`), definida como variables en `wwwroot/css/style.css`. Valores aproximados, sin logotipo ni marca; reemplácelos por los de la guía oficial si dispone de ella.

## Arranque rápido

Requisito: **.NET SDK 8.0 o superior** (`dotnet --version`). No hay base de datos que instalar ni paquetes que restaurar.

```bash
dotnet run                    # http://localhost:5000
```

La primera vez crea `App_Data/portal.json` y lo siembra solo. Otro puerto: `dotnet run --urls http://localhost:8080`.

### Usuarios de prueba (ficticios)

| Rol | Usuario | Contraseña | Puede |
|---|---|---|---|
| Administrador | `admin` | `Admin-Demo-2026` | Crear, editar, gestionar, resolver, cerrar y reabrir; usar el asistente |
| Consulta | `consulta` | `Consulta-Demo-2026` | Solo ver dashboard, listado y detalle |

Para publicar una demo con otras contraseñas: variables de entorno `BK_DEMO_ADMIN_PASSWORD` y `BK_DEMO_CONSULTA_PASSWORD`
antes del primer arranque o de `--reset` (con ellas definidas, el login deja de mostrar las credenciales).

### Reiniciar la base

```bash
dotnet run -- --reset         # borra App_Data/portal.json, siembra de nuevo y arranca
# o simplemente borrar la carpeta App_Data/ y arrancar
```

### Pruebas

```bash
dotnet run -- --selftest      # 67 comprobaciones internas; devuelve código 0 si todas pasan
```

Cubren asistente, ciclo de vida, edición, filtros, analítica, responsables, campanita, validación, hash de contraseñas,
persistencia tras reinicio y 60 altas concurrentes. Además se recorrió el portal por HTTP (login, roles, CSRF, crear, editar, transiciones, filtros, XSS,
cabeceras) y en Chromium (escritorio y móvil, sin errores de consola ni de CSP).

### Docker (opcional)

```bash
docker build -t portal-incidentes . && docker run -p 8000:8000 -v bkdata:/data portal-incidentes
```

## Qué incluye

- **Incidentes:** registro, consulta, edición, gestión, resolución, cierre y reapertura (con motivo obligatorio). Historial completo (quién, cuándo, qué).
- **Campos:** ID (`INC-0001`), restaurante, categoría, descripción, prioridad (crítica/alta/media/baja), estado (nuevo/en gestión/resuelto/cerrado), fecha y responsable.
- **Listado:** filtros por restaurante, prioridad, estado, categoría y responsable, búsqueda por ID o texto, paginación, estados vacíos.
- **Dashboard:** totales, críticos abiertos, abiertos y cerrados; **incidente más repetitivo**, **tiempo de atención**, **tiempo de solución**, **casos repetidos y reabiertos**; **incidentes por responsable** (total, abiertos, críticos, tiempos de atención y solución, y categorías de cada persona; el nombre abre el listado filtrado); periodo (7, 30, 90 días o todo).
- **Campanita de notificaciones:** en la barra superior muestra la actividad reciente (creado, editado, en gestión, resuelto, cerrado, reabierto) con quién, cuándo y de qué incidente; el contador cuenta lo no visto por cada persona y se limpia al abrirla. Se actualiza sola cada 20 s.
- **Asistente de triaje:** sugiere categoría, prioridad y primer paso de diagnóstico.
- **Semilla:** 16 incidentes en 5 sucursales de ejemplo (`Data/seed_incidents.json`), con reaperturas y repetidos. Fechas relativas al momento de sembrar.

### Ciclo de vida

```
nuevo ──gestionar──▶ en gestión ──resolver──▶ resuelto ──cerrar──▶ cerrado
  └────────────resolver──────────▶ ▲              │                   │
                                   └────reabrir───┴───────reabrir─────┘
```
Un incidente cerrado no se edita; hay que reabrirlo. Cada transición se valida y se escribe bajo el mismo candado, así dos personas no se pisan.

### Cómo se calculan las métricas

| Métrica | Definición |
|---|---|
| Abiertos | Estados nuevo + en gestión. «Resueltos» se cuentan aparte (arreglados, pendientes de cierre) |
| Críticos abiertos | Prioridad crítica y aún abiertos |
| Tiempo de atención | Creación → primera gestión (o resolución directa) |
| Tiempo de solución | Creación → resuelto. Si se reabre, se mide con la nueva resolución; el historial conserva lo anterior |
| Caso repetido | Mismo restaurante y categoría que otro incidente de los 7 días previos (sobre todo el historial) |
| Por responsable | Agrupa por el nombre del responsable (sin distinguir mayúsculas ni espacios repetidos); los sin asignar van aparte. Abiertos = nuevo + en gestión; los tiempos usan las mismas definiciones de arriba |
| Incidente más repetitivo | Categoría con más incidentes en el periodo |

Las horas se guardan en UTC y se muestran en hora de Costa Rica (UTC−6, sin horario de verano).

## Arquitectura y decisiones

```
Program.cs                    configuración: cookies, antiforgery, cabeceras de seguridad, pipeline
SelfTest.cs                   pruebas internas (dotnet run -- --selftest)
Domain/Catalog.cs             categorías, prioridades, estados, transiciones y entidades (fuente única)
Data/DataStore.cs             IDataStore + almacén en archivo JSON (escritura atómica, candado)
Data/Seeder.cs                siembra; lee Data/seed_incidents.json
Services/IncidentService.cs   reglas de negocio (crear, editar, transiciones, consultas)
Services/Assistant.cs         asistente de triaje por reglas
Services/Analytics.cs         cálculo del dashboard (incluye por responsable)
Services/NotificationService.cs  campanita: actividad reciente y lo no visto por persona
Services/PasswordHasher.cs    PBKDF2-SHA256 con sal
Controllers/                  Account, Dashboard, Incidents (+ API del asistente), Notifications (API de la campanita), Error
Views/, wwwroot/              interfaz Razor, CSS y JS propios (sin CDN)
docs/                         caso crítico (PDF/MD), declaración de IA
```

**Por qué este diseño**
- **Una sola aplicación,** sin frontend separado ni paso de compilación de JS.
- **Sin paquetes NuGet:** `dotnet run` funciona incluso sin internet y la superficie de dependencias es cero.
- **Persistencia en JSON, detrás de `IDataStore`.** Es una decisión consciente: el entorno donde se construyó no podía descargar paquetes (por eso no hay SQLite/EF Core), y se quería un portal que se ejecutara y verificara de verdad. El archivo se escribe de forma atómica (temporal + reemplazo) y bajo candado. **Límites:** un solo proceso, sin consultas SQL, pensado para cientos o pocos miles de registros. Cambiar a SQLite/EF Core implica implementar `IDataStore`; controladores y vistas no cambian.
- **Sin CDN en el navegador:** permite una CSP estricta y funciona sin internet.

Modelo de datos: usuarios, restaurantes, incidentes (con marcas de creación, primera atención, resolución, cierre y contador de reaperturas) y eventos (historial). De ahí salen todas las métricas.

## Seguridad

- **Permisos en el servidor:** `[Authorize(Roles = "admin")]` en toda ruta de escritura; un usuario de consulta recibe 403 aunque forje la petición con un token CSRF válido. Ocultar botones es solo cortesía.
- **Validación en el servidor:** listas cerradas para categoría, prioridad y restaurante; longitudes; caracteres de control eliminados; el responsable solo admite letras, números y puntuación básica.
- **CSRF:** antiforgery de ASP.NET Core en todo POST (formularios y API, esta última por cabecera `X-CSRF-Token`).
- **Contraseñas** con PBKDF2-SHA256 (210 000 iteraciones, sal aleatoria, comparación en tiempo constante; se verifica un hash falso si el usuario no existe). Cookie `HttpOnly`, `SameSite=Lax`, 8 h (`Secure` con `BK_SECURE_COOKIES=1` bajo HTTPS).
- **Límite de intentos:** 5 fallos por usuario e IP bloquean 10 minutos.
- **Inyección:** Razor escapa la salida (comprobado con `<script>`); el JS usa `textContent`, nunca `innerHTML`; no hay SQL.
- **Cabeceras:** CSP sin `unsafe-inline`, `X-Frame-Options: DENY`, `nosniff`, `Cache-Control: no-store`. Redirección posterior al login restringida a rutas locales.
- **Secretos:** no hay claves de API. Las claves de cifrado de cookies se generan en `App_Data/keys/` (en `.gitignore`, no viajan en el ZIP).

### Limitaciones conocidas de seguridad
El límite de intentos vive en memoria; las claves de cifrado se guardan sin cifrar en `App_Data/keys/` (aceptable en un prototipo; en producción, usar un almacén de claves); sin HTTPS propio (se espera un proxy); sin gestión de usuarios ni recuperación de contraseña; sin registro de accesos. Es un prototipo.

## Asistente de triaje

**Qué hace:** a partir de la descripción propone categoría, prioridad y un primer paso de diagnóstico, y **explica en qué palabras se basó**.

**Cómo funciona:** reglas determinísticas (palabras clave por categoría; patrones de severidad como «pedidos del kiosco fallan», «sin internet»; patrones de alcance como «todos» o «tres restaurantes»; y de baja urgencia como «no urgente»). Sin IA ni red.

**Es controlable:** «Sugerir» solo muestra una propuesta; «Aplicar» rellena los campos, que siguen editables. Nada se guarda automáticamente. Al crear, el servidor recalcula la sugerencia y la guarda junto con lo que la persona eligió; el dashboard muestra el porcentaje de coincidencia, una medida real de cuánto se corrige al asistente.

**Costos:** ninguno (sin API, tokens ni licencias). **Datos:** la descripción no sale del servidor ni va a terceros.

**Limitaciones:** no entiende negaciones ni contexto («la impresora funciona bien, el problema es el router» puede sugerir impresoras); depende de vocabulario en español; con categorías mezcladas puede haber empate (lo indica); no conoce el historial del restaurante ni la criticidad real del negocio; sin coincidencias no inventa nada. Una evolución natural sería un modelo de lenguaje con datos anonimizados, manteniendo este mismo contrato (propuesta revisable + explicación).

### Declaración sobre IA

- **El asistente no usa IA generativa ni aprendizaje automático:** son reglas determinísticas (palabras clave y patrones) que se ejecutan dentro de la aplicación.
- **Sin costo:** no hay API, tokens ni licencias.
- **Sin envío de datos a terceros:** la aplicación no hace llamadas de red hacia servicios externos y no incluye claves ni secretos. Todos los datos son ficticios; no se usó información real de BK ni de sus proveedores.
- **Uso de IA en el desarrollo:** se usó una herramienta de IA como apoyo para construir el prototipo (código). El detalle esta en [`docs/DECLARACION_IA.md`](docs/DECLARACION_IA.md).

## Demo

- URL de código fuente: https://github.com/daniel17cr/portal-incidentes  
- URL portal: https://portal-incidentes.onrender.com/login

**Nota sobre la demo en línea:** está publicada en un plan gratuito de Render, con estas limitaciones:
- Se duerme tras 15 minutos sin tráfico; la primera visita puede tardar cerca de 1 minuto en cargar.
- El disco es efímero: al dormirse, reiniciarse o redesplegarse, los datos vuelven a la semilla (los 16 incidentes de ejemplo). Lo que se cree durante la revisión no se conserva.
- Es un prototipo con datos ficticios, no un entorno de producción.

El proyecto se puede ejecutar localmente con `dotnet run`.

## Documentos de la entrega

| Archivo | Contenido |
|---|---|
| `docs/CASO_CRITICO.pdf` | Caso crítico de una página |
| `docs/DECLARACION_IA.md` | Herramientas de IA usadas y validaciones |
| `docs/AUTOEVALUACION.md` | Autoevaluación (máx. 200 palabras) |
| `Data/seed_incidents.json` + `Data/Seeder.cs` | Datos de ejemplo y script de inicialización |
