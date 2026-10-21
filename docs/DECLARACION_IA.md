# Declaración de uso de IA

## Herramientas utilizadas
- **Claude (Anthropic)**, modelo Claude Sonnet 5.5, mediante claude.ai con herramientas de ejecución de código y de archivos.
- Ninguna otra herramienta de IA. El asistente de triaje **dentro** de la aplicación no usa IA: son reglas determinísticas.

## Trabajo realizado con IA
- Recomendación de diseño de la arquitectura.
- Generación del código fuente completo (backend, plantillas, CSS, JS), el script y los datos de semilla.
- Durante la construcción, la IA ejecutó en su entorno: las 53 pruebas internas (pasan), el servidor real recorrido por HTTP (login, roles, CSRF, crear, editar, transiciones, filtros, XSS, cabeceras) y un navegador automatizado (Chromium) que probó el asistente y las vistas de escritorio y móvil, vigilando errores de consola y de CSP.
- Entorno de construcción: Windows con .NET SDK 8. NuGet no era accesible, por eso el proyecto no usa paquetes externos y persiste en un archivo JSON detrás de la interfaz `IDataStore`. Se compiló con 0 errores y 0 advertencias y se probó en Windows.

