# ToolDock — Documentación de usuario

Guía de uso de la versión actual. ToolDock es un trabajo en progreso: pueden quedar errores o detalles inestables.

Índice: [qué es](#qué-es), [primeros pasos](#primeros-pasos), [el dock](#el-dock), [bandeja](#bandeja-del-sistema), [configuración](#configuración), [archivos](#archivos-de-la-aplicación), [varias instancias](#varias-instancias), [arrancar al inicio](#arrancar-al-inicio), [problemas](#solución-de-problemas), [límites](#límites-actuales).

## Qué es

ToolDock es un dock secundario para Windows 11. Muestra accesos a programas y aplicaciones de la Microsoft Store junto a un borde de la pantalla.

No sustituye la barra de tareas. Puedes tenerlo siempre visible, ocultarlo hasta acercar el cursor, o reservar su espacio para que las ventanas maximizadas no lo tapen.

Está pensado para usarse en portable:

- No hace falta un instalador.
- La configuración no se guarda en el Registro de Windows.
- Basta con el ejecutable dentro de una carpeta propia. Copiar esa carpeta conserva ajustes, presets y colores.

## Primeros pasos

1. Coloca `ToolDock.exe` en una carpeta vacía, por ejemplo `D:\Apps\ToolDock\`.
2. Ábrelo. Aparece el dock y un icono en la bandeja del sistema.
3. Clic derecho en el icono de la bandeja y elige **Configuración**.
4. En **Tamaño y posición** elige el borde (arriba, abajo, izquierda o derecha) y el tamaño.
5. Agrega accesos arrastrándolos desde el Explorador, o desde **Accesos de apps**.

El idioma de la interfaz se cambia en **General**. Por defecto está en inglés y con tema oscuro.

## El dock

### Abrir una aplicación

Clic izquierdo sobre el icono. Si la ruta ya no existe, ToolDock avisa y no la abre.

Con **Abrir las apps en la pantalla del dock** (Comportamiento), ToolDock intenta mover la ventana nueva a la pantalla donde está el dock. Windows decide la pantalla al crear el proceso; el traslado ocurre después y no siempre es posible, sobre todo en apps de la Store.

### Agregar accesos

Se aceptan dos tipos de archivo:

- Programas: `.exe`
- Accesos directos de Windows: `.lnk`

Formas de agregarlos:

- Arrastrar uno o varios desde el Explorador y soltarlos sobre el dock. Si el dock está autooculto, acercar el archivo al borde lo muestra.
- En Configuración, **Accesos de apps** → **Agregar acceso directo…**
- En la misma sección, **Agregar app UWP…** abre la lista de aplicaciones empaquetadas de la Microsoft Store.

Un mismo archivo no se agrega dos veces. Una app UWP se identifica por su identificador de aplicación (AUMID), no por una ruta de disco.

### Orden e iconos

- Arrastra un icono dentro del dock para cambiar su lugar.
- Clic derecho sobre un icono:
  - **Quitar del dock**
  - **Asignar icono personalizado…** (`.ico`, `.png`, `.jpg`, `.exe` o `.dll`)
  - **Restablecer icono**, si ya hay uno personalizado
- Doble clic o clic derecho en una zona vacía del dock abre un menú rápido: configuración, recargar, posición, alineación de los iconos, autoocultar y bloquear accesos.

**Bloquear accesos directos** impide reordenarlos. Sigue siendo posible abrirlos, quitarlos y agregar otros. La opción está en el menú de la zona vacía del dock.

**Iconos de alta calidad** (Accesos de apps) extrae los iconos a mayor resolución. El hover se ve mejor en algunos accesos y la aplicación usa un poco más de memoria.

### Efecto al pasar el cursor

Con **Activar efectos al pasar el cursor**, los iconos cercanos al puntero crecen y se separan para no montarse. La magnificación va de 0 % a 150 %. El radio se ajusta en centímetros; por dentro se guarda en píxeles lógicos.

El radio del hover es independiente de la distancia de activación del autoocultado. Su rango es de 20–400 píxeles lógicos (aproximadamente 0,53–10,58 cm), aunque la distancia de activación sea menor.

## Bandeja del sistema

Clic izquierdo abre Configuración en **General**. **Configuración** en el menú contextual del dock o de la bandeja abre la misma sección. Clic derecho en el icono de la bandeja abre su menú:

| Opción | Qué hace |
|---|---|
| Configuración | Abre la ventana de ajustes |
| Poner encima | Muestra el dock y lo trae al frente |
| Desactivar dock | Lo oculta solo en esta sesión. Al cerrar y volver a abrir, vuelve a estar activo |
| Solución de problemas | Carpeta del programa, Recargar dock, Recargar app y los tres reinicios. Recargar dock vuelve a leer iconos, DPI y posición sin cerrar la aplicación. Recargar app guarda, cierra y vuelve a abrir ToolDock (abre Configuración al arrancar) |
| Salir | Guarda y cierra |

Si hay más de una instancia, el icono de la bandeja muestra el número, por ejemplo `ToolDock (2)`.

## Configuración

La ventana tiene una barra lateral. En **Tamaño y posición**, **Apariencia**, **Accesos de apps** y **Comportamiento**, **Restablecer** devuelve esa sección al preset de referencia. En **General**, restaura el idioma inglés y el tema oscuro.

El preset de referencia es el seleccionado. Si el preset actual es **Personalizado**, se usa el último preset real que habías elegido (por defecto, **Predeterminado**).

Doble clic en un deslizador restaura ese control con el mismo criterio.

Abajo de la ventana:

- **Restablecer preajuste**: vuelve a cargar el preset de referencia.
- **Restablecer todo a predeterminado**: idioma inglés, tema oscuro y el resto de valores de fábrica. No borra los accesos del dock, ni los presets guardados, ni los colores personalizados.
- **Desactivar dock**: oculta el dock en esta sesión. Mientras está oculto, el botón dice **Activar dock** y queda resaltado. No se guarda: al reiniciar, el dock vuelve.

### Información

Resume el uso y los archivos de configuración. **Arrancar al inicio** explica cómo crear un acceso directo en Inicio; no es un interruptor de arranque automático. Información también explica cómo abrir varias instancias.

### General

- **Idioma:** español o inglés. También cambia los textos de la bandeja.
- **Modo oscuro:** tema de Configuración y de los menús. El color del propio dock se elige en Apariencia.
- **Pantalla:** pantalla donde se ancla el dock. **Predeterminado (principal)** sigue a la pantalla principal.
- **Presets de configuración:** ver [Presets](#presets).

### Atajos de teclado

Tres atajos globales. Pulsa el botón, luego la combinación (puede incluir Ctrl, Alt, Mayús y la tecla Windows). El botón de borrar deja el atajo sin asignar.

| Atajo | Efecto |
|---|---|
| Mostrar/Ocultar dock | Alterna la visibilidad cuando autoocultar está apagado |
| Poner el dock encima | Equivale a **Poner encima** de la bandeja |
| Desactivar dock | Activa o desactiva el dock en esta sesión |

Con autoocultar activo, solo se atenúa la etiqueta **Mostrar/Ocultar dock**. Puedes asignar, cambiar o borrar la combinación, pero no estará activa hasta desactivar autoocultar. **Poner el dock encima** y **Desactivar dock** siguen disponibles.

**Poner el dock encima** muestra y trae el dock al frente una vez. No alterna su visibilidad ni activa la opción permanente **Siempre encima**.

Si otra aplicación ya registró la misma combinación, ToolDock no puede usarla.

### Tamaño y posición

Las medidas de la interfaz están en píxeles lógicos (los mismos que usa Windows al escalar la pantalla). En una pantalla con escala al 150 %, un valor de 48 px lógicos ocupa más píxeles físicos. La pestaña **Detalles** muestra la equivalencia.

| Control | Qué hace | Intervalo habitual |
|---|---|---|
| Posición del dock | Borde: arriba, abajo, izquierda, derecha. En los laterales el dock pasa a vertical | — |
| Ajustar ancho del dock automáticamente | El largo del dock sigue al número de iconos | — |
| Ancho del dock (px) | Largo a lo largo del borde | Mínimo según iconos; máximo, el área de trabajo |
| Alto del dock (px) | Grosor del dock | 32–64 |
| Offset de alineación (px) | Desplaza el dock a lo largo del borde | −1500 a 1500 en el deslizador |
| Separación del borde (px) | Hueco entre el dock y el borde de la pantalla | 0–600 |

### Apariencia

**Background (fondo)**

- **Esquinas redondeadas** y **Radio de las esquinas (px)**, de 0 a 32, con casillas independientes para cada esquina.
- **Color de fondo**, con una lista de colores integrados.
- **Opacidad del dock**, de 0 (transparente) a 100 (opaco). El valor de fábrica es 60.

**Outline (contorno)**

- **Mostrar contorno** lo activa o desactiva; **Contorno en los bordes** permite elegir cada lado por separado (superior, inferior, izquierdo, derecho). Los lados son del propio dock, no de la pantalla. Con el dock arriba, el borde inferior es el que mira al escritorio.
- Color, opacidad (fábrica: 20) y **Grosor del contorno (px)** de 0,25 a 6 píxeles lógicos. Los valores fraccionarios usan antialias; el aspecto depende de la escala de pantalla.

**Custom Colors (colores personalizados)**

Escribe un color HEX (`#RRGGBB` o `#RGB`; si omites `#`, se añade), ponle un nombre y guárdalo. Queda en `Colors_TD.json` y puedes volver a elegirlo o borrarlo. Sirve tanto para el fondo como para el contorno.

**Seleccionar color**, junto al campo HEX, abre un selector visual con un espectro de color, un deslizador de valor, vistas previas y campos HEX, RGB o HSV editables. **Listo** copia el color elegido al campo HEX; **Cancelar** lo deja como estaba. Escribe un nombre y usa **Guardar color** para guardarlo como siempre.

### Accesos de apps

- **Guardar accesos** exporta solo la lista de iconos (rutas, nombres, icono personalizado y apps UWP). No exporta colores ni el resto de ajustes. El nombre sugerido es `Shortcuts_TD.json`.
- **Cargar accesos** sustituye la lista actual por la del archivo. Se ignoran los `.exe` y `.lnk` que ya no existan.
- **Quitar todos los accesos** vacía el dock. Pide confirmación y no se puede deshacer.
- En **Buttons (botones)**, **Alineación de botones** coloca los iconos: **Izquierda**, **Centro** o **Derecha**; **Arriba**, **Medio** o **Abajo** si el dock es vertical.
- **Tamaño de icono (px)**: 16–32. Fábrica: 24.
- **Espaciado entre iconos (px)**: 5–44. Fábrica: 19.
- **Iconos de alta calidad** y **Hover effects (efectos al pasar el cursor)**, descritos más arriba.

### Comportamiento

| Opción | Efecto |
|---|---|
| Siempre encima | El dock queda por encima de las ventanas normales |
| No mostrar sobre apps en pantalla completa | Oculta el dock mientras una app a pantalla completa tiene el foco en la pantalla del dock; activa por defecto |
| Abrir las apps en la pantalla del dock | Intenta mover la ventana abierta a la pantalla del dock |
| Reservar espacio en el área de trabajo | Windows trata el dock como una barra: las ventanas maximizadas no lo cubren. No se puede combinar con autoocultar |
| Autoocultar | El dock se esconde en su borde y vuelve a salir según el modo elegido |
| Ocultar detrás de la barra de tareas | Solo si autoocultar y siempre encima están activos |
| Ocultar cuando hay una ventana maximizada | Con autoocultar activo, una ventana maximizada en la pantalla del dock permite que se oculte |
| Ocultar cuando una ventana se superpone al dock | Con autoocultar activo, una ventana superpuesta al dock permite que se oculte, aunque no esté maximizada |

La ocultación sobre pantalla completa funciona con autoocultar activo o inactivo.

Las dos condiciones de ventana están desactivadas por defecto y son excluyentes. Al activar una, el dock permanece visible mientras no se cumpla esa condición. La superposición se comprueba incluso mientras arrastras una ventana, con los retrasos configurados para mostrar y ocultar. Las ventanas minimizadas u ocultas, el escritorio y la barra de tareas no activan estas condiciones. Sin ninguna activada, funciona el autoocultado normal.

**Mostrar dock al...** selecciona cómo mostrar el dock con el cursor:

| Modo | Cómo mostrarlo |
|---|---|
| Tocar el borde | Toca el píxel físico extremo del borde elegido de la pantalla, dentro del tramo de activación del dock, y permanece ahí durante el retraso para mostrar. Se usa el borde completo, incluso cuando la barra de tareas reduce el área de trabajo |
| Acercar el cursor al borde | Acerca el cursor al dock dentro de la distancia de activación configurada |

Otros controles de autoocultado:

- **Distancia de activación (cm)**: distancia de proximidad, independiente del radio del hover. Rango: unos 0,03–10,58 cm (1–400 píxeles lógicos); valor de fábrica: unos 1,6 cm.
- **Animar autoocultado (deslizamiento)**: activa la animación de deslizamiento.
- **Velocidad del deslizamiento (ms)**: duración de la animación, de 0–1200 ms (fábrica: 150). Los valores mayores hacen el deslizamiento más lento.
- **Delay para mostrar dock (ms)** y **Delay para ocultar dock (ms)**: 0–4000 ms; valores de fábrica: 150 y 250, respectivamente.

El cursor puede seguir mostrando el dock cuando se cumple una condición de ventana. Arrastrar archivos al dock, **Poner encima** de la bandeja y el atajo **Poner el dock encima** también lo muestran.

### Detalles

No edita nada. Muestra, para la pantalla y el DPI actuales, el tamaño efectivo del dock, de los iconos y del hover, además del área de trabajo y la resolución.

### Solución de problemas

| Acción | Qué borra o cambia |
|---|---|
| Abrir carpeta del programa | Abre la carpeta del `.exe` en el Explorador |
| Refrescar ventana | Actualiza la ventana de ajustes sin reiniciar la aplicación |
| Recargar app | Guarda, cierra y vuelve a abrir ToolDock. Ajustes se abre al arrancar |
| Generar registro detallado | Crea un `.txt` con configuración, pantallas, DPI y estado del dock, para adjuntar a un informe |
| Borrar todos los colores personalizados | Elimina `Colors_TD.json`. Los colores integrados siguen |
| Borrar todos los presets personalizados | Elimina `Presets_TD.json` y aplica el preset Predeterminado |
| Restablecer esta instancia | Cierra esta instancia, elimina su configuración y las copias de seguridad asociadas, y la vuelve a abrir en Ajustes. No pide confirmación |
| Restablecer todas las instancias | Cierra todas las instancias de este ejecutable, elimina todas las configuraciones de instancia de su carpeta y sus copias de seguridad, y vuelve a abrir el mismo número de instancias. Ajustes no se abre. No pide confirmación |
| Reinicio completo | Cierra todas las instancias de este ejecutable y envía a la Papelera todos los JSON de su carpeta, junto con `Backups_TD/` y `Temp_TD/`. Después abre una instancia sin Ajustes. Pide confirmación |

Los restablecimientos de instancias eliminan los ajustes y las listas de accesos afectados; al restablecer todas, también se eliminan configuraciones de instancias que no estén ejecutándose. Se conservan los presets guardados, los colores personalizados y los archivos de accesos exportados. El reinicio completo también incluye los JSON que otras herramientas hayan colocado en la carpeta del programa; usa una carpeta propia para ToolDock.

## Presets

Un preset guarda tamaño, posición, apariencia, iconos (tamaño y hover, no la lista de accesos) y comportamiento.

- **Predeterminado** viene con la aplicación y no se puede borrar.
- **Personalizado** aparece cuando cambias algo respecto al preset cargado.
- Para conservar un ajuste, escribe un nombre en **Guardar personalizado como nuevo preset** y pulsa **Guardar preajuste**. Si el nombre ya existe, pregunta antes de sobrescribir.
- **Borrar preajuste** elimina el preset seleccionado, si es uno tuyo.

Los presets antiguos, de cuando las medidas no estaban en píxeles lógicos, se descartan al leer `Presets_TD.json`.

## Archivos de la aplicación

Todos viven en la misma carpeta que `ToolDock.exe`:

| Archivo | Para qué sirve |
|---|---|
| `config.json` | Ajustes y accesos de la primera instancia |
| `config.instanceN.json` | Lo mismo para la instancia N, si N es 2 o más |
| `Presets_TD.json` | Presets que has guardado |
| `Colors_TD.json` | Colores con nombre |
| `Shortcuts_TD.json` | Solo si exportas la lista de accesos; puedes guardarlo con otro nombre |
| `Backups_TD/` | Copia anterior de la configuración, guardada como `.bak`. Si un JSON queda a medias (por ejemplo al apagar el equipo), ToolDock intenta recuperar esta copia |
| `Temp_TD/` | Archivos temporales usados al guardar JSON; se elimina al quedar vacía |
| `*.corrupt` | Archivo ilegible apartado para no pisar la copia de seguridad |

Hacer una copia de esa carpeta guarda la configuración. No hace falta copiar el `.exe` si solo quieres el respaldo de los datos.

`Temp_TD/` se gestiona automáticamente y no es necesaria en una copia de seguridad.

## Varias instancias

Puedes abrir ToolDock más de una vez, hasta 32. Cada instancia tiene sus propios ajustes, accesos e icono de bandeja. Los presets guardados y los colores personalizados comparten los mismos archivos en la carpeta de la aplicación.

Para lanzar varias de golpe:

```text
ToolDock.exe --instances 2
```

También vale `--instances=2`.

## Arrancar al inicio

ToolDock sigue siendo portable y no se registra para arrancar automáticamente. Usa un acceso directo en la carpeta de Inicio de Windows para abrirlo al iniciar sesión.

Si quieres que arranque al iniciar sesión:

1. Pulsa `Win + R`, escribe `shell:startup` y acepta.
2. Crea ahí un acceso directo a `ToolDock.exe`.
3. Si quieres dos docks, en las propiedades del acceso directo añade al final del destino: `--instances 2`.

## Límites actuales

- Orientado a Windows 11.
- Al soltar archivos solo entran `.exe` y `.lnk`. El resto se ignora. Las apps de la Store se agregan desde el selector UWP.
- El efecto visual y el contorno fino dependen del DPI y de cómo compone Windows la pantalla.
- Mover una app a la pantalla del dock no está garantizado: algunas ventanas eligen su pantalla ellas mismas.
- **Desactivar dock** dura solo hasta cerrar la aplicación.
