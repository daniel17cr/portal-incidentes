# Autoevaluación

**Qué funciona.** Portal ASP.NET Core 8 con login de dos roles y permisos validados en el servidor; alta, edición, gestión, resolución, cierre y reapertura con historial; filtros; dashboard con incidente más repetitivo, tiempos de atención y solución, repetidos, reabiertos e incidentes por responsable; campanita de actividad; asistente de triaje por reglas, revisable y corregible; semilla de 16 incidentes ficticios. 67 pruebas internas pasan. Demo publicada en Render.

**Qué falta.** Persistencia en JSON de un solo proceso, no SQL; en la demo gratuita los datos vuelven a la semilla al dormirse el servicio. El límite de intentos de login vive en memoria. No hay gestión de usuarios ni recuperación de contraseña. El asistente no entiende negaciones. Sin pruebas de integración ni CI.

**Con 8 horas más.** Migraría a SQL con EF Core detrás de la misma interfaz (3 h); gestión de usuarios y auditoría de accesos (2 h); pruebas de integración y CI (2 h); y ajustaría el asistente con casos anonimizados, evaluando un modelo de lenguaje sin enviar datos reales (1 h).
