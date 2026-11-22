# ConsGHost

[English](../../README.md) · [简体中文](README.zh-CN.md) · [Русский](README.ru.md) · **Español** · [Français](README.fr.md)

ConsGHost (CGH) carga mods escritos en C# y C++ en Garry's Mod para Windows x64. Cada mod puede ejecutarse dentro del juego o en un proceso propio. Los mods también pueden incluir complementos normales de Garry's Mod en carpetas o archivos `.gma`.

## Instalación y arranque

1. Instala [.NET 10 Desktop Runtime para Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) e inicia sesión en Steam. Necesitas Windows 10 u 11 y una instalación de Garry's Mod que incluya `gmod_win64.exe`.
2. Descarga `CGH-<versión>-win-x64.zip` desde la [página de versiones de GitHub](https://github.com/LytharaLab/ConsGHost/releases) y descomprime todo el contenido en una carpeta con permisos de escritura. Conserva la estructura del archivo.
3. Abre `ConsGHost.Host.exe`. En **Ajustes**, la última opción de la barra lateral, selecciona la carpeta que contiene `gmod_win64.exe`. En Steam puedes abrirla desde **Administrar → Ver archivos locales**.
4. Revisa los mods habilitados en el gestor y arranca el juego desde CGH. El paquete incluye BridgeDemo y Observer, dos ejemplos de llamadas entre C# y C++ y de intercambio de mensajes entre procesos.

CGH arranca Garry's Mod con `-insecure` de forma predeterminada para el desarrollo local. Usa sesiones locales y carga solo mods y DLL adicionales de confianza. Un proceso independiente aísla los fallos del mod, pero conserva los permisos de tu usuario de Windows.

CGH usa por defecto el idioma de visualización de Windows. Si ese idioma no está disponible, usa el inglés. En **Ajustes → Idioma de la interfaz** puedes elegir inglés, chino simplificado, ruso, español o francés. Los cambios se aplican al instante y se guardan para el próximo inicio.

## Instalación de mods

Crea una carpeta por mod dentro de `Mods/`, junto a `ConsGHost.Host.exe`. Cada mod necesita `cghm_metadata.json` y puede incluir `Managed/`, `Native/`, `Resources/`, `Addons/` y la plantilla `config.default.json`. CGH crea `config.json` al detectar por primera vez un mod con plantilla. La [guía de desarrollo de mods](../mod-development.md) describe el SDK y el formato de metadatos.

CGH guarda los ajustes en `cgh_settings.json`, los registros en `Sessions/` y los datos de los mods en `Data/<mod-id>/`. Para actualizar, descomprime la nueva versión en otra carpeta y migra los mods y datos que quieras conservar. Guarda las configuraciones de los mods y revisa los ajustes antes de arrancar.

## Compilación desde el código fuente

Instala el SDK de .NET 10, Visual Studio 2022 Build Tools con **Desarrollo para el escritorio con C++**, un SDK de Windows y CMake 3.24 o posterior. Ejecuta estos comandos en PowerShell desde la raíz del repositorio.

```powershell
./build.ps1
./artifacts/CGH/ConsGHost.Host.exe
```

`./test.ps1` ejecuta las pruebas nativas, de integración y de la interfaz de escritorio. `./package.ps1` genera el ZIP para Windows x64 y su suma SHA-256 en `artifacts/`. Consulta la [guía de verificación](../verification.md).

## Documentación del proyecto

- [Arquitectura](../architecture.md)
- [Registro de cambios](../../CHANGELOG.md)
- [Licencia y avisos de terceros](../../NOTICE.md)
- [Informes de seguridad y datos de diagnóstico](../../SECURITY.md)

ConsGHost es un proyecto independiente de la comunidad. Su código y documentación propios se distribuyen bajo la [licencia MIT](../../LICENSE).
