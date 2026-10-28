# ToolDock — Bitácora técnica

Registro del estado actual del código: qué hace la aplicación, cómo está armada y qué queda fuera de esta línea base. La guía de uso está en [DOCUMENTACION_USUARIO.md](DOCUMENTACION_USUARIO.md).

ToolDock es un trabajo en progreso. Esta bitácora describe el árbol tal como está, no un diseño cerrado.

## Plataforma

- Lenguaje: C#
- UI: WPF
- Destino: `net9.0-windows` (`ToolDock/ToolDock.csproj`)
- Publicación Release: `win-x64`, autocontenida, un solo archivo, ReadyToRun, sin símbolos de depuración
- Compresión del bundle desactivada (`EnableCompressionInSingleFile=false`) para no descomprimir ensamblados en RAM privada en cada arranque
- Iconos de aplicación: `TDicons/icon.ico` y `TDicons/trayicon.ico`

No hay instalador, ni proyecto de solución (`.sln`) en el repositorio. Se compila con el SDK:

```powershell
cd ToolDock
dotnet build
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true
```

## Mapa del código

```text
ToolDock/
  App.xaml.cs              ciclo de vida, bandeja, instancias, argumentos, reinicios
  MainWindow.xaml(.cs)     dock: layout, z-order, autohide, drag & drop, hover, hotkeys, lanzamiento
  SettingsWindow.xaml(.cs) configuración por categorías
  UwpAppPickerWindow.cs    selector de apps de la Store
  FluentChrome.cs          iconos Segoe Fluent en botones
  Models/
    DockConfig.cs          estado persistente, rangos y valores de fábrica
    DockMetrics.cs         DIP, cm, % del monitor, DPI
    DockItemViewModel.cs   icono en pantalla (escala y desplazamiento del hover)
    PresetModels.cs        preset y su instantánea (esquema 3)
    DockShortcutsFileExport.cs   export/import solo de accesos
  Services/
    AppPaths.cs            carpeta de datos = directorio del proceso, no el temp del single-file
    ConfigService.cs       config.json
    DurableFile.cs         lectura con reintentos, escritura atómica, .bak, cuarentena
    PresetService.cs       Presets_TD.json
    CustomColorService.cs  Colors_TD.json
    DockShortcutsFileService.cs
    DockPresetReset.cs     aplicar Predeterminado / preset de referencia
    LocalizationService.cs textos ES/EN en un solo sitio
    IconService.cs         extracción Win32 (PrivateExtractIcons / SHGetFileInfo)
    UwpAppService.cs       shell:AppsFolder, AUMID, icono de paquete
    DisplayService.cs      monitores
    DockLaunchPlacement.cs mueve la ventana nueva al monitor del dock
    NativeTrayIcon.cs      icono de bandeja nativo
    DockContextMenuTheme.cs
  Interop/
    User32.cs, Shell32.cs, DisplayConfig.cs
```

## Arranque

`ShutdownMode` es `OnExplicitShutdown`: cerrar la ventana del dock no sale de la aplicación. La salida es el menú de bandeja, que llama a `SaveNow` antes de `Shutdown`.

Argumentos (`App.ParseStartupOptions`):

| Argumento | Efecto |
|---|---|
| `--instances N` o `--instances=N` | Lanza N instancias (1–32). Las extra llevan `--spawned` |
| `--spawned` | Esta instancia no vuelve a multiplicarse |
| `--open-settings` | Abre Configuración al terminar de arrancar |
| `--soft-reset` | Aplica el preset Predeterminado al iniciar |
| `--hard-reset` | Borra el JSON de esta instancia y luego aplica Predeterminado |
| `--ultra-hard-reset` | Igual, y también borra `Presets_TD.json` y `Colors_TD.json` |

Las instancias se reservan con un `Mutex` local cuyo nombre incluye un hash de la carpeta de datos. La primera usa `config.json`; la N (≥ 2) usa `config.instanceN.json`. Tope práctico: 32.

La bandeja es un icono nativo (`NativeTrayIcon`), no `NotifyIcon` de WinForms. El clic izquierdo abre Atajos de teclado. El menú contextual se tematiza con el tema claro/oscuro.

## Modelo de configuración

`DockConfig` implementa `INotifyPropertyChanged`. `ConfigSchemaVersion` actual es **3**: las medidas canónicas son píxeles lógicos (DIP a 96). WPF los escala con el DPI del monitor. Centímetros y porcentajes del span son solo presentación (`JsonIgnore`) y se recalculan en `DockMetrics`.

`DockConfig.IsLoading` evita recortar valores mientras el JSON todavía no está completo. `ArmStartupWidthGuard` retrasa unos 12 segundos el recorte del ancho si el área de trabajo aún es falsa (shell arrancando o monitor apagado).

`DockDisabled` y el estado de sesión no se serializan. `LockShortcuts` sí se guarda, pero no entra en `ResetBehaviour` ni en `ResetToDefaults`.

Rangos que impone el modelo (no solo la UI):

| Campo | Mín | Máx | Fábrica |
|---|---:|---:|---:|
| IconSize | 16 | 32 | 24 |
| IconSpacing | 5 | 44 | 19 |
| DockHeight | 32 | 64 | 48 |
| DockWidth | según iconos y cantidad (mínimo de cálculo: 4) | span del área de trabajo | 990, recortado |
| DockOpacity | 0 | 100 | 60 |
| CornerRadius | 0 | 32 | 18 |
| OutlineThickness | 0,25 | 6 | 1 |
| OutlineOpacity | 0 | 100 | 20 |
| HoverMagnification | 0 | 150 | 90 |
| HoverEffectRadius | 20 | min(400, distancia de proximidad) | 40 |
| AutoHideProximityDistance | 20 | 400 | 60 |
| Offset | −4000 | 4000 | 0 |
| AlignmentOffset | 0 | 2000 | 0 |
| Deslizamiento autohide | 0 | 1200 ms | 150 |
| Retrasos autohide | 0 | 4000 ms | mostrar 150, ocultar 250 |

Otros valores de fábrica: borde superior, alineación centrada, esquinas redondeadas en los cuatro lados, fondo `#1F1F1F`, contorno blanco en los cuatro lados, hover activo, autohide apagado, revelación por proximidad, siempre encima activo. Idioma inglés, tema oscuro.

Combinaciones que el modelo rechaza si no está cargando:

- Ocultar detrás de la barra de tareas exige autohide y siempre encima.
- Reservar el área de trabajo es incompatible con autohide.
- Ocultar con ventana maximizada exige autohide.

El HEX inválido no se aplica: `NormalizeColorInput` añade `#` y `IsValidHexColor` acepta `#RGB` o `#RRGGBB`. No hay estado visual de error en el cuadro; el valor anterior se conserva.

## Persistencia

`AppPaths.DataDirectory` es el directorio de `Environment.ProcessPath`. En un ejecutable de un solo archivo no se usa `AppContext.BaseDirectory`, porque ese directorio puede ser la extracción temporal y cambiaría en cada arranque.

`DurableFile` lee con reintentos ante bloqueos breves (típico al iniciar sesión). Escribe a un temporal y reemplaza. La configuración anterior queda como `.bak` en `Backups_TD`, junto al ejecutable. Un JSON vacío o ilegible se aparta (`Quarantine`) antes de restaurar la copia, para no pisar el `.bak` bueno. Si el archivo existe pero no se puede leer, `ConfigService` no persiste encima: un guardado en ese momento sustituiría la configuración por los valores por defecto.

El guardado de la ventana principal va con debounce (250 ms) y otro guardado explícito al salir o antes de reiniciar.

Presets (`PresetService`): se ignoran los que no traen `DataSchemaVersion >= 3`. Así se descartan unidades de versiones anteriores en lugar de reinterpretarlas.

`config.json`, `Presets_TD.json` y `Colors_TD.json` están fuera del repositorio (`.gitignore` cubre `config.json`; los otros se generan al usar la app).

## Dock en pantalla

`MainWindow` es una ventana sin borde, excluida de Alt+Tab. La posición sale del borde elegido, el monitor (`MonitorDeviceName` o el principal), el offset y la separación del borde. Izquierda y derecha cambian el `ItemsPanel` a vertical.

Z-order (`ApplyZOrder` / `SetDockWindowZOrder`):

- Siempre encima, o detrás de la barra de tareas cuando esa opción está armada.
- No dibujar sobre pantalla completa.
- Reservar área de trabajo mediante la API de área de trabajo, en exclusión con autohide.

Autohide: temporizadores de mostrar y ocultar, modo borde o proximidad, animación de deslizamiento opcional. Un arrastre de archivos desde el Explorador revela el dock si está oculto.

Hover: no es un `Storyboard`. En `MouseMove` se calcula una escala por icono según la distancia al puntero y un desplazamiento para que el crecimiento no se superponga (`UpdateFisheyeScale`). El radio máximo dinámico no supera la distancia de proximidad del autohide.

Lanzamiento: `UseShellExecute` para rutas de disco. UWP usa `shell:AppsFolder\<AUMID>`. Si `OpenAppsOnDockMonitor` está activo, `DockLaunchPlacement` observa durante 5 segundos las ventanas de primer nivel nuevas (o la que pasa a primer plano) y las mueve al monitor del dock, con un tope de dos movimientos por ventana.

Drop externo: solo `.exe` y `.lnk`, sin duplicados por ruta. El selector de archivos ofrece el mismo filtro, más «todos los archivos», pero `TryAddDockItem` sigue rechazando otras extensiones. UWP entra por `UwpAppService.GetInstalledApps` (ítems de `shell:AppsFolder` cuyo path contiene `!`).

Iconos: `IconService` elige un tamaño de extracción según DPI y hover. `HighQualityIcons` sube a 256 px. El orden es icono personalizado, logo UWP, `IconLocation` del `.lnk`, icono del destino del acceso, un `.ico` junto al ejecutable y, al final, el icono asociado del shell. Un `IconPath` opcional (`.ico`, `.png`, `.jpg`, `.exe`, `.dll`) sustituye al icono del destino.

Hotkeys globales: tres ids (`0xB001` mostrar/ocultar, `0xB002` poner encima, `0xB003` desactivar). Modificadores en el formato de `RegisterHotKey` (Alt 1, Control 2, Mayús 4, Win 8).

## Interfaz de ajustes que no está activa

Los botones de «fijar» (`PinButton`) y el panel de ajustes fijados dentro de Atajos de teclado tienen `Visibility=Collapsed`. El código de anclar y desanclar se conserva para poder reactivarlo. No es una función de usuario.

El botón Cerrar de la ventana de ajustes también está oculto; cierra la misma X de la ventana.

## Decisiones que conviene no revertir sin motivo

1. **Portable de verdad.** Datos junto al ejecutable, sin Registro y sin «iniciar con Windows» dentro de la app. El usuario crea el acceso en `shell:startup` si lo quiere.
2. **Escritura durable.** Un `WriteAllText` cortado al apagar Windows dejaba JSON a medias y el siguiente arranque lo pisaba con valores de fábrica.
3. **Píxeles lógicos como unidad canónica (esquema 3).** Mezclar px físicos, cm y porcentajes en el JSON hacía que el mismo archivo se viera distinto al cambiar de monitor. Cm y % se derivan al pintar.
4. **No guardar si el archivo está bloqueado.** Mejor mostrar valores por defecto en memoria que destruir el JSON.
5. **Guardia de ancho al inicio.** El área de trabajo puede ser mentira los primeros segundos. Recortar el ancho guardado en ese momento lo dejaba estrecho para siempre.
6. **Un solo servicio de textos.** Castellano e inglés salen de `LocalizationService`. Añadir una cadena implica las dos ramas del `switch`.
7. **Single-file sin compresión.** La compresión del bundle obligaba a descomprimir ensamblados en RAM privada.

## Pendiente conocido

No es un compromiso de roadmap. Es lo que el código deja abierto:

- El anclaje de opciones a un panel rápido está implementado y oculto.
- No hay feedback visual cuando el HEX es inválido; el cuadro simplemente no aplica el cambio.
- `DockLaunchPlacement` es heurístico: no todas las apps se dejan mover.
- El efecto hover no tiene easing configurable; la escala se asigna en el movimiento del ratón.
- No hay batería de pruebas automatizadas en el repositorio.

## Cómo seguir esta bitácora

En cada cambio que altere comportamiento, persistencia o límites, añade una entrada con fecha. No reescribas el mapa de arriba si el cambio es pequeño: la entrada basta, y el mapa se actualiza cuando la estructura deje de coincidir.

```md
## YYYY-MM-DD — Título corto
- Cambios:
- Archivos:
- Riesgos:
- Validación:
```

## 2026-10-19 — Línea base documentada

- Cambios: la documentación de la raíz describía una versión anterior (blur, icono 76, ancho fijo, `config.json` vía `AppContext.BaseDirectory`, sin presets, UWP, instancias ni hotkeys). Se reescribió contra el código actual y se movió a `Docs/`. Se añadió `README.md` en la raíz.
- Archivos: `README.md`, `Docs/Español/BITACORA_TECNICA.md`, `Docs/Español/DOCUMENTACION_USUARIO.md`, `Docs/English/TECHNICAL_LOG_EN.md`, `Docs/English/USER_DOCUMENTATION_EN.md`. Se retiraron los cuatro Markdown que estaban en la raíz. `ToolDock/README.md` quedó como puntero para no contradecir esta guía.
- Riesgos: ninguno de ejecución. Quien tuviera enlaces a los Markdown de la raíz debe usar `Docs/English/` o `Docs/Español/`.
- Validación: contraste con `DockConfig`, `App.xaml.cs`, `SettingsWindow.xaml`, `LocalizationService` y los servicios de persistencia. No se ejecutó la aplicación para esta pasada de documentación.
