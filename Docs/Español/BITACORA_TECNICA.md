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
    DockMetrics.cs         DIP, cm, % de la pantalla, DPI
    DockItemViewModel.cs   icono en pantalla (escala y desplazamiento del hover)
    PresetModels.cs        preset y su instantánea (esquema 3)
    DockShortcutsFileExport.cs   export/import solo de accesos
  Services/
    AppPaths.cs            carpeta de datos = directorio del proceso, no el temp del single-file
    ConfigService.cs       config.json
    DurableFile.cs         lectura con reintentos, escritura atómica, .bak, cuarentena
    TroubleshootingReset.cs proceso de restablecimiento, borrado/Papelera y reapertura
    PresetService.cs       Presets_TD.json
    CustomColorService.cs  Colors_TD.json
    DockShortcutsFileService.cs
    DockPresetReset.cs     aplicar Predeterminado / preset de referencia
    LocalizationService.cs textos ES/EN en un solo sitio
    IconService.cs         extracción Win32 (PrivateExtractIcons / SHGetFileInfo)
    UwpAppService.cs       shell:AppsFolder, AUMID, icono de paquete
    DisplayService.cs      pantallas
    DockLaunchPlacement.cs mueve la ventana nueva a la pantalla del dock
    NativeTrayIcon.cs      icono de bandeja nativo
    DockContextMenuTheme.cs
  Interop/
    User32.cs, Shell32.cs, DisplayConfig.cs
```

## Terminología de la interfaz y nombres internos

La documentación de usuario sigue las etiquetas visibles de `LocalizationService` y su uso en `SettingsWindow` y el menú de la bandeja. Los identificadores internos no siempre coinciden con esas etiquetas; se conservan al hablar de código o de campos de configuración serializados.

| Interfaz en inglés | Interfaz en español | Nombre interno / significado |
|---|---|---|
| Display | Pantalla | `MonitorSelect`, `MonitorDeviceName`, `MonitorInfo`; el código y las API de Windows siguen usando «monitor» |
| Open apps on dock's display | Abrir las apps en la pantalla del dock | `OpenAppsOnDockMonitor` |
| Show/Hide dock | Mostrar/Ocultar dock | `HotkeyShowHide`, `ShowHideHotkey`; alterna la visibilidad, inactivo con autoocultar |
| Set dock on top | Poner el dock encima | `HotkeyShowDock`, `ShowDockHotkey`; muestra y trae el dock al frente una vez |
| Set on top | Poner encima | `TrayShowDock`, `ShowDock()`; acción de bandeja para la misma operación |
| Always on top | Siempre encima | `AlwaysOnTop`; preferencia persistente de orden de ventanas, distinta de la acción puntual |
| Alignment offset (px) | Offset de alineación (px) | `Offset`; desplaza el dock a lo largo del borde elegido |
| Offset from edge (px) | Separación del borde (px) | `AlignmentOffset`; aleja el dock de ese borde |
| Button alignment | Alineación de botones | `ButtonAlignment`; alineación de los iconos de accesos dentro del dock |
| Hotkeys | Atajos de teclado | `CategoryQuickAccess` / `QuickAccess`; esta categoría también contiene el panel oculto de ajustes fijados |
| Launch on startup | Arrancar al inicio | `InfoHeadingStartup`; instrucciones para un acceso directo en Inicio, no un ajuste |

`Background`, `Outline`, `Custom Colors`, `Buttons` y `Hover effects` son actualmente encabezados en inglés en ambos idiomas de la interfaz. La guía en español conserva esas etiquetas y añade explicaciones en español. Usa las etiquetas de la interfaz en las instrucciones de uso y los nombres internos al explicar la implementación; cambiar una etiqueta no implica renombrar el campo de configuración.

## Arranque

`ShutdownMode` es `OnExplicitShutdown`: cerrar la ventana del dock no sale de la aplicación. La salida es el menú de bandeja, que llama a `SaveNow` antes de `Shutdown`.

Argumentos (`App.ParseStartupOptions`; `TroubleshootingReset.TryRunWorker` atiende primero las solicitudes de restablecimiento):

| Argumento | Efecto |
|---|---|
| `--instances N` o `--instances=N` | Lanza N instancias (1–32). Las extra llevan `--spawned` |
| `--spawned` | Esta instancia no vuelve a multiplicarse |
| `--open-settings` | Abre Configuración al terminar de arrancar |
| `--reset-worker` | Proceso interno: espera a que las instancias se cierren, borra o recicla los archivos y vuelve a abrir la aplicación |

Las instancias se reservan con un `Mutex` local cuyo nombre incluye un hash de la carpeta de datos. La primera usa `config.json`; la N (≥ 2) usa `config.instanceN.json`. Tope práctico: 32.

La bandeja es un icono nativo (`NativeTrayIcon`), no `NotifyIcon` de WinForms. El clic izquierdo abre Atajos de teclado. El menú contextual se tematiza con el tema claro/oscuro.

El proceso auxiliar de restablecimiento usa el mismo ejecutable y actúa antes de reservar una instancia. Espera a que salgan los procesos afectados antes de modificar archivos. Los restablecimientos de instancia eliminan la familia de configuración mediante `DurableFile.DeleteFamily`; el de todas busca cada `config.json` / `config.instanceN.json` de la carpeta de datos y vuelve a abrir el número de instancias que estaban ejecutándose. El reinicio completo usa `SHFileOperationW` para enviar a la Papelera todos los JSON de esa carpeta y `Backups_TD/` y `Temp_TD/`, y abre una instancia. La búsqueda de procesos se limita a la misma ruta del ejecutable. Solo el reinicio completo pide confirmación.

## Modelo de configuración

`DockConfig` implementa `INotifyPropertyChanged`. `ConfigSchemaVersion` actual es **3**: las medidas canónicas son píxeles lógicos (DIP a 96). WPF los escala con el DPI de la pantalla. Centímetros y porcentajes del span son solo presentación (`JsonIgnore`) y se recalculan en `DockMetrics`.

`DockConfig.IsLoading` evita recortar valores mientras el JSON todavía no está completo. `ArmStartupWidthGuard` retrasa unos 12 segundos el recorte del ancho si el área de trabajo aún es falsa (shell arrancando o pantalla apagada).

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
| HoverEffectRadius | 20 | 400 | 40 |
| AutoHideProximityDistance | 1 | 400 | 60 |
| Offset | −4000 | 4000 | 0 |
| AlignmentOffset | 0 | 2000 | 0 |
| Deslizamiento autohide | 0 | 1200 ms | 150 |
| Retrasos autohide | 0 | 4000 ms | mostrar 150, ocultar 250 |

Otros valores de fábrica: borde superior, alineación centrada, esquinas redondeadas en los cuatro lados, fondo `#1F1F1F`, contorno blanco en los cuatro lados, hover activo, autohide apagado, revelación por proximidad, siempre encima activo. Idioma inglés, tema oscuro.

`DoNotShowOverFullscreen` parte de `true` en configuraciones nuevas, `ResetBehaviour` y el preset Predeterminado. Un valor `false` guardado en una configuración existente se conserva al cargar.

Combinaciones que el modelo rechaza si no está cargando:

- Ocultar detrás de la barra de tareas exige autohide y siempre encima.
- Reservar el área de trabajo es incompatible con autohide.
- Ocultar con ventana maximizada y ocultar por superposición exigen autohide.

El HEX inválido no se aplica: `NormalizeColorInput` añade `#` y `IsValidHexColor` acepta `#RGB` o `#RRGGBB`. No hay estado visual de error en el cuadro; el valor anterior se conserva.

## Persistencia

`AppPaths.DataDirectory` es el directorio de `Environment.ProcessPath`. En un ejecutable de un solo archivo no se usa `AppContext.BaseDirectory`, porque ese directorio puede ser la extracción temporal y cambiaría en cada arranque.

`DurableFile` lee con reintentos ante bloqueos breves (típico al iniciar sesión). Escribe a un temporal y reemplaza. La configuración anterior queda como `.bak` en `Backups_TD`, junto al ejecutable. Un JSON vacío o ilegible se aparta (`Quarantine`) antes de restaurar la copia, para no pisar el `.bak` bueno. Si el archivo existe pero no se puede leer, `ConfigService` no persiste encima: un guardado en ese momento sustituiría la configuración por los valores por defecto.

El guardado de la ventana principal va con debounce (250 ms) y otro guardado explícito al salir o antes de reiniciar.

Los temporales de escritura JSON usan `Temp_TD/.<archivo>.<pid>.tmp` dentro de la carpeta de datos de destino. El directorio temporal se elimina cuando queda vacío. El arranque llama a `DurableFile.ClearStaleTemps` antes de cargar la configuración de la instancia; borra los `.tmp` sobrantes y conserva los archivos bloqueados por otra escritura.

Presets (`PresetService`): se ignoran los que no traen `DataSchemaVersion >= 3`. Así se descartan unidades de versiones anteriores en lugar de reinterpretarlas.

`config.json`, `Presets_TD.json` y `Colors_TD.json` están fuera del repositorio (`.gitignore` cubre `config.json`; los otros se generan al usar la app).

## Dock en pantalla

`MainWindow` es una ventana sin borde, excluida de Alt+Tab. La posición sale del borde elegido, la pantalla (`MonitorDeviceName` o la principal), el offset y la separación del borde. Izquierda y derecha cambian el `ItemsPanel` a vertical.

Z-order (`ApplyZOrder` / `SetDockWindowZOrder`):

- Siempre encima, o detrás de la barra de tareas cuando esa opción está armada.
- No dibujar sobre pantalla completa.
- Reservar área de trabajo mediante la API de área de trabajo, en exclusión con autohide.

Autohide: temporizadores de mostrar y ocultar, modo borde o proximidad, animación de deslizamiento opcional. Un arrastre de archivos desde el Explorador revela el dock si está oculto.

`HideWhenMaximized` y `HideWhenWindowIntersects` son modos excluyentes, desactivados por defecto: seleccionar uno desactiva el otro en el modelo. Las configuraciones y presets anteriores con ambos activos conservan intersección, independientemente del orden de las propiedades JSON. Sin ninguno activo, funciona el autoocultado normal. La detección ignora ventanas invisibles, minimizadas, cloaked y del shell. La intersección usa el marco visible del dock en su posición de reposo, incluso cuando el contenido está oculto o deslizándose, y compara coordenadas de pantalla para admitir ventanas que cruzan pantallas. Sin superposición, este modo mantiene visible el dock aunque haya una ventana maximizada.

Hover: no es un `Storyboard`. `MouseMove` y `PointerTrackerTimer_Tick` actualizan la escala de cada icono según su distancia al puntero y lo desplazan para que el crecimiento no se superponga (`UpdateFisheyeScale`). Su radio tiene un rango fijo de 20–400 DIP, independiente de la distancia de activación del autohide de 1–400 DIP, también después de cargar ajustes, aplicar presets o refrescar las métricas DPI.

Lanzamiento: `UseShellExecute` para rutas de disco. UWP usa `shell:AppsFolder\<AUMID>`. Si `OpenAppsOnDockMonitor` está activo, `DockLaunchPlacement` observa durante 5 segundos las ventanas de primer nivel nuevas (o la que pasa a primer plano) y las mueve a la pantalla del dock, con un tope de dos movimientos por ventana.

Drop externo: solo `.exe` y `.lnk`, sin duplicados por ruta. El selector de archivos ofrece el mismo filtro, más «todos los archivos», pero `TryAddDockItem` sigue rechazando otras extensiones. UWP entra por `UwpAppService.GetInstalledApps` (ítems de `shell:AppsFolder` cuyo path contiene `!`).

Iconos: `IconService` elige un tamaño de extracción según DPI y hover. `HighQualityIcons` sube a 256 px. El orden es icono personalizado, logo UWP, `IconLocation` del `.lnk`, icono del destino del acceso, un `.ico` junto al ejecutable y, al final, el icono asociado del shell. Un `IconPath` opcional (`.ico`, `.png`, `.jpg`, `.exe`, `.dll`) sustituye al icono del destino.

Hotkeys globales: tres ids (`0xB001` mostrar/ocultar, `0xB002` poner encima, `0xB003` desactivar). Modificadores en el formato de `RegisterHotKey` (Alt 1, Control 2, Mayús 4, Win 8).

`ShowHideHotkey` sigue siendo editable y se guarda con autoocultar activo, pero `RegisterHotkeys` omite su registro. En Ajustes solo se atenúa su etiqueta; los botones de asignar y borrar siguen activos. Los otros dos atajos se registran independientemente del autoocultado.

## Interfaz de ajustes que no está activa

Los botones de «fijar» (`PinButton`) y el panel de ajustes fijados dentro de Atajos de teclado tienen `Visibility=Collapsed`. El código de anclar y desanclar se conserva para poder reactivarlo. No es una función de usuario.

El botón Cerrar de la ventana de ajustes también está oculto; cierra la misma X de la ventana.

## Decisiones que conviene no revertir sin motivo

1. **Portable de verdad.** Datos junto al ejecutable, sin Registro ni registro de arranque automático. **Arrancar al inicio** explica cómo crear un acceso directo en `shell:startup`.
2. **Escritura durable.** Un `WriteAllText` cortado al apagar Windows dejaba JSON a medias y el siguiente arranque lo pisaba con valores de fábrica.
3. **Píxeles lógicos como unidad canónica (esquema 3).** Mezclar px físicos, cm y porcentajes en el JSON hacía que el mismo archivo se viera distinto al cambiar de pantalla. Cm y % se derivan al pintar.
4. **No guardar si el archivo está bloqueado.** Mejor mostrar valores por defecto en memoria que destruir el JSON.
5. **Guardia de ancho al inicio.** El área de trabajo puede ser mentira los primeros segundos. Recortar el ancho guardado en ese momento lo dejaba estrecho para siempre.
6. **Un solo servicio de textos.** Castellano e inglés salen de `LocalizationService`. Añadir una cadena implica las dos ramas del `switch`.
7. **Single-file sin compresión.** La compresión del bundle obligaba a descomprimir ensamblados en RAM privada.

## Pendiente conocido

No es un compromiso de roadmap. Es lo que el código deja abierto:

- El anclaje de opciones a un panel rápido está implementado y oculto.
- No hay feedback visual cuando el HEX es inválido; el cuadro simplemente no aplica el cambio.
- `DockLaunchPlacement` es heurístico: no todas las apps se dejan mover.
- El efecto hover no tiene easing configurable; la escala sigue la posición del puntero.
- Hay comprobaciones de regresión en `tests/ToolDock.RegressionTests`; el movimiento de ventanas y las animaciones aún requieren comprobaciones interactivas en pantallas reales.

## Cómo seguir esta bitácora

Mantén las secciones anteriores alineadas con la aplicación actual. Cuando un cambio afecte al comportamiento, la persistencia o los límites, añade una entrada con fecha para registrar su implementación y validación. Los README deben presentar la aplicación y las guías deben explicar su uso actual; los detalles de correcciones de commits van aquí.

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

## 2026-10-27 — Documentación y nombres de la interfaz

- Commit: `d1914f8`. Actualizó los nombres de la interfaz en inglés, retiró la afirmación obsoleta de que no existía licencia e ignoró los artefactos locales de `graphify-out/`. Los README identifican la licencia MIT; estos ajustes no cambian el comportamiento de la aplicación.
- Validación: revisión del diff del commit y de la documentación actual durante esta pasada documental.

## 2026-10-28 — Superposición de ventanas y distancias independientes

- Commit: `4ffc2e8`.
- Cambios: nueva opción de ocultar por intersección junto a ocultar con ventana maximizada; distancia de activación desde 1 DIP y eliminación de su límite sobre el radio del hover. Cada ajuste conserva su propio rango. Los esquemas de configuración y presets siguen en versión 3; en archivos existentes, la nueva condición queda desactivada.
- Archivos: modelos de configuración y presets, ventanas principal y de ajustes, interoperabilidad Win32, localización, restablecimiento de presets y `tests/ToolDock.RegressionTests`.
- Riesgos: la geometría entre pantallas y las animaciones necesitan comprobaciones interactivas; la intersección debe usar el marco en reposo para evitar ciclos de ocultar/mostrar.
- Comando de validación desde la raíz del repositorio: `dotnet run --project tests/ToolDock.RegressionTests` (sin paquetes externos de pruebas).
- Ajuste posterior: modos de ventana excluyentes y nuevo orden de toggles en Comportamiento. Las pruebas cubren el cambio desde ambos controles, JSON/presets anteriores con ambos activos, el orden solicitado y ventanas maximizadas sin intersección.
- Resultado: recompilaciones Debug y Release correctas, con 0 advertencias y 0 errores; los 8 grupos de regresión pasaron, incluidos filtros de ventanas nativas, combinación de condiciones, contacto entre bordes y estabilidad del área visible/oculta en los cuatro bordes. El hardware con DPI mixto y la animación visual quedan como comprobaciones manuales.

## 2026-10-28 — Atajos, guardados temporales, pantalla completa y restablecimientos

- `a060cda`: habilitó los botones de asignar/borrar el atajo Mostrar/Ocultar con autoocultar activo. La combinación guardada sigue sin registrarse hasta desactivar autoocultar; solo se atenúa la etiqueta.
- `2245c35`: tradujo al inglés los comentarios del código, XAML y proyecto. Los textos de la interfaz y la documentación en español conservaron su idioma; no cambió el comportamiento.
- `1c5b719`: movió los temporales de escritura JSON a `Temp_TD/`, elimina la carpeta cuando queda vacía y limpia al iniciar los `.tmp` sobrantes, conservando los archivos que otra instancia todavía tenga abiertos.
- `1672b14`: activó la ocultación sobre pantalla completa en configuraciones nuevas, restablecimientos de comportamiento del modelo y el preset Predeterminado. Las configuraciones existentes que guardan explícitamente `false` conservan ese valor.
- `fcda0cf`: sustituyó los antiguos reinicios suave/completo/ultra por Restablecer esta instancia, Restablecer todas las instancias y Reinicio completo. Los restablecimientos de instancia borran las familias de configuración y conservan presets, colores y exportaciones compartidos. El reinicio completo envía a la Papelera todos los JSON de la carpeta de la aplicación, incluidos los ajenos a ToolDock, junto con las carpetas de copias y temporales. El proceso auxiliar del mismo ejecutable cierra las instancias afectadas antes de modificar archivos; esta instancia reabre Ajustes, todas restaura la cantidad en ejecución sin Ajustes y el completo abre una instancia sin Ajustes.
- Archivos: ciclo de vida de la app, `TroubleshootingReset`, `DurableFile`, interfaz de ajustes, valores predeterminados de configuración/presets, localización y ejecutor de regresiones; la traducción de comentarios también afectó a otros archivos de código.
- Validación: contraste con los diffs y el código actual. Los registros de los commits indican 8/8 grupos Debug para los temporales y 9/9 para el cambio de restablecimientos. La cobertura automática de restablecimientos comprueba la clasificación de nombres de archivo, no toda la secuencia de cierre/borrado/Papelera/reapertura. No se ejecutaron restablecimientos durante esta pasada documental.

## 2026-10-29 — Alineación estable al cambiar de preset

- Commit: `ad36e81`.
- Cambios: `UpdateDockVisualsAndLayout` impide actualizaciones de layout WPF reentrantes y encola una solicitud anidada hasta terminar la actual. Evita que un preset que cambia varios tamaños deje el dock desplazado respecto al borde, incluso al volver a Predeterminado con separación cero. Los JSON de presets compatibles no necesitan migración ni sustitución.
- Archivos: `ToolDock/MainWindow.xaml.cs`.
- Validación: revisión del commit y de la protección/actualización diferida actual. El cambio visual de presets sigue siendo una comprobación interactiva; no se hizo una nueva validación de ejecución durante esta pasada documental.

## 2026-10-29 — Revelado en el borde exacto de la pantalla

- Commit: `b69fb96`.
- Cambios: `EdgeTouch` sustituye el margen de activación de 4 DIP por una comparación de la posición original de `GetCursorPos` con `MonitorInfo.BoundsPx`: `Left`, `Top`, `Right - 1` o `Bottom - 1`. La detección usa los límites completos de la pantalla y el tramo del marco del dock en reposo con su margen existente. `MouseEnter` sobre la franja oculta no puede saltarse esta comprobación; salir del borde cancela el temporizador de aparición, que también verifica el contacto antes de mostrar. Proximidad y las acciones explícitas de mostrar conservan su comportamiento.
- Archivos: `ToolDock/MainWindow.xaml.cs`, `tests/ToolDock.RegressionTests/NativeAutoHideTests.cs` y su README. Los README de la raíz y las guías de usuario en ambos idiomas describen el comportamiento actualizado.
- Riesgos: el comportamiento visual en pantallas reales con DPI mixto queda como comprobación manual. Al 100 % de escala, la proximidad de 1 DIP puede coincidir con el contacto exacto en el borde inferior/derecho.
- Validación: compilación Debug y publicación portable Release correctas; los 9 grupos de regresión pasaron. Se cubren los cuatro bordes, píxeles interiores/exteriores, coordenadas negativas de pantalla, áreas de trabajo reservadas, métricas DPI simuladas de 96/120/144/192, entrada del ratón y aparición diferida. Las pruebas nativas se ejecutaron fuera del sandbox para leer el cursor sin moverlo. NuGet emitió `NU1900` al consultar vulnerabilidades; no impidió compilar ni probar. Esta actualización documental se contrastó con la implementación; no se repitieron las pruebas por cambios solo de texto.

## 2026-10-29 — Autoocultado por intersección durante el arrastre de ventanas

- Cambios: el modo de intersección puede iniciar su cuenta atrás de ocultación con un botón del ratón pulsado. Salir de la superposición solicita mostrar sin esperar a soltarlo; volver antes de terminar el retraso cancela esa solicitud, salvo que el cursor pida revelar. Al dispararse, el temporizador de aparición vuelve a comprobar la intersección y la activación por cursor, también en modo Proximity. Se conservan los retrasos, la activación por cursor y la protección de botón pulsado en autoocultado normal/solo con ventana maximizada. El bloque de decisiones existente queda en `UpdateAutohideState` para que las pruebas aporten el estado del botón sin manipular la entrada real.
- Archivos: `ToolDock/MainWindow.xaml.cs`, pruebas nativas de regresión y su README, y ambas guías de usuario.
- Validación: la nueva regresión con botón pulsado falló con el comportamiento anterior; después de corregirlo pasaron los 9 grupos Debug. Las pruebas cubren ambos modos de revelado, cancelación de ocultación/aparición pendientes, aparición sin retraso y los otros modos de autoocultado. NuGet conservó la advertencia `NU1900` de su servicio de vulnerabilidades; la compilación y las comprobaciones terminaron.
- Límites: las pruebas simulan las entradas del seguimiento; el arrastre interactivo de ventanas y la animación en pantallas reales siguen siendo comprobaciones manuales. La activación por cursor conserva la prioridad al arrastrar cerca de la zona de revelado del dock.
