# ToolDock — Documentación de usuario

Guía de uso de la versión actual. ToolDock es un trabajo en progreso: pueden quedar errores o detalles inestables.

Índice: [qué es](#qué-es), [primeros pasos](#primeros-pasos), [el dock](#el-dock), [bandeja](#bandeja-del-sistema), [configuración](#configuración), [archivos](#archivos-de-la-aplicación), [varias instancias](#varias-instancias), [inicio con Windows](#inicio-con-windows), [problemas](#solución-de-problemas), [límites](#límites-actuales).

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

Con **Abrir las apps en la pantalla del dock** (Comportamiento), ToolDock intenta mover la ventana nueva al monitor donde está el dock. Windows decide el monitor al crear el proceso; el traslado ocurre después y no siempre es posible, sobre todo en apps de la Store.

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

Clic izquierdo abre **Atajos de teclado**. Clic derecho abre el menú:

| Opción | Qué hace |
|---|---|
| Configuración | Abre la ventana de ajustes |
| Poner encima | Muestra el dock y lo trae al frente |
| Desactivar dock | Lo oculta solo en esta sesión. Al cerrar y volver a abrir, vuelve a estar activo |
| Recargar dock | Vuelve a leer iconos, DPI y posición sin cerrar la aplicación |
| Recargar app | Guarda, cierra y vuelve a abrir ToolDock (abre Configuración al arrancar) |
| Solución de problemas | Carpeta del programa y los tres reinicios |
| Salir | Guarda y cierra |

Si hay más de una instancia, el icono de la bandeja muestra el número, por ejemplo `ToolDock (2)`.

## Configuración

La ventana tiene una barra lateral. Cada sección, salvo Información y Detalles, tiene un botón **Restablecer** que devuelve solo esa sección a los valores del preset de referencia.

El preset de referencia es el seleccionado. Si el preset actual es **Personalizado**, se usa el último preset real que habías elegido (por defecto, **Predeterminado**).

Doble clic en un deslizador restaura ese control con el mismo criterio.

Abajo de la ventana:

- **Restablecer preajuste**: vuelve a cargar el preset de referencia.
- **Restablecer todo a predeterminado**: idioma inglés, tema oscuro y el resto de valores de fábrica. No borra los accesos del dock, ni los presets guardados, ni los colores personalizados.
- **Refrescar ventana**: actualiza la ventana de configuración.
- **Recargar app**: reinicia ToolDock.

### Información

Resume el uso, lista los archivos de configuración y muestra cómo iniciar con Windows y abrir más de una instancia. No cambia ajustes.

### General

- **Idioma:** español o inglés. También cambia los textos de la bandeja.
- **Modo oscuro:** tema de Configuración y de los menús. El color del propio dock se elige en Apariencia.
- **Desactivar dock:** igual que la opción de la bandeja. No se guarda: al reiniciar, el dock vuelve.
- **Pantalla:** monitor donde se ancla el dock. **Predeterminado (principal)** sigue al monitor principal.
- **Presets de configuración:** ver [Presets](#presets).

### Atajos de teclado

Tres atajos globales. Pulsa el botón, luego la combinación (puede incluir Ctrl, Alt, Mayús y la tecla Windows). El botón de borrar deja el atajo sin asignar.

| Atajo | Efecto |
|---|---|
| Mostrar/Ocultar dock | Alterna la visibilidad cuando autoocultar está apagado |
| Poner el dock encima | Equivale a **Poner encima** de la bandeja |
| Desactivar dock | Activa o desactiva el dock en esta sesión |

Con autoocultar activo, solo se atenúa la etiqueta **Mostrar/Ocultar dock**. Puedes asignar, cambiar o borrar la combinación, pero no estará activa hasta desactivar autoocultar. **Poner el dock encima** y **Desactivar dock** siguen disponibles.

Si otra aplicación ya registró la misma combinación, ToolDock no puede usarla.

### Tamaño y posición

Las medidas de la interfaz están en píxeles lógicos (los mismos que usa Windows al escalar la pantalla). En un monitor al 150 %, un valor de 48 px lógicos ocupa más píxeles físicos. La pestaña **Detalles** muestra la equivalencia.

| Control | Qué hace | Intervalo habitual |
|---|---|---|
| Posición | Borde: arriba, abajo, izquierda, derecha. En los laterales el dock pasa a vertical | — |
| Ajustar ancho automáticamente | El largo del dock sigue al número de iconos | — |
| Ancho general | Largo a lo largo del borde | Mínimo según iconos; máximo, el área de trabajo |
| Alto general | Grosor del dock | 32–64 |
| Offset de alineación | Desplaza el grupo de iconos a lo largo del borde | −1500 a 1500 en el deslizador |
| Separación del borde | Hueco entre el dock y el borde de la pantalla | 0–600 |

### Apariencia

**Fondo**

- Esquinas redondeadas, con radio de 0 a 32 y casillas independientes para cada esquina.
- Color de fondo, con una lista de colores integrados.
- Opacidad del dock, de 0 (transparente) a 100 (opaco). El valor de fábrica es 60.

**Contorno**

- Se puede activar o apagar, y elegir cada lado por separado (superior, inferior, izquierdo, derecho). Los lados son del propio dock, no de la pantalla. Con el dock arriba, el borde inferior es el que mira al escritorio.
- Color, opacidad (fábrica: 20) y grosor de 0,25 a 6. 1 px es el trazo más fino que cabe en un píxel físico. Por debajo de 1 el contorno se dibuja con antialias y se ve más fino.

**Colores personalizados**

Escribe un color HEX (`#RRGGBB` o `#RGB`; si omites `#`, se añade), ponle un nombre y guárdalo. Queda en `Colors_TD.json` y puedes volver a elegirlo o borrarlo. Sirve tanto para el fondo como para el contorno.

### Accesos de apps

- **Guardar accesos** exporta solo la lista de iconos (rutas, nombres, icono personalizado y apps UWP). No exporta colores ni el resto de ajustes. El nombre sugerido es `Shortcuts_TD.json`.
- **Cargar accesos** sustituye la lista actual por la del archivo. Se ignoran los `.exe` y `.lnk` que ya no existan.
- **Quitar todos los accesos** vacía el dock. Pide confirmación y no se puede deshacer.
- Alineación de los iconos: inicio, centro o final del dock (izquierda/centro/derecha, o arriba/centro/abajo si es vertical).
- Tamaño de icono: 16–32. Fábrica: 24.
- Espaciado: 5–44. Fábrica: 19.
- Iconos de alta calidad y efecto hover, descritos más arriba.

### Comportamiento

| Opción | Efecto |
|---|---|
| Siempre encima | El dock queda por encima de las ventanas normales |
| No mostrar sobre apps en pantalla completa | Oculta el dock mientras una app a pantalla completa tiene el foco en el monitor del dock; activa por defecto |
| Abrir las apps en la pantalla del dock | Intenta mover la ventana abierta al monitor del dock |
| Reservar espacio en el área de trabajo | Windows trata el dock como una barra: las ventanas maximizadas no lo cubren. No se puede combinar con autoocultar |
| Autoocultar | El dock se esconde en su borde y vuelve a salir según el modo elegido |
| Ocultar detrás de la barra de tareas | Solo si autoocultar y siempre encima están activos |
| Ocultar cuando hay una ventana maximizada | Con autoocultar activo, una ventana maximizada en el monitor del dock permite que se oculte |
| Ocultar cuando una ventana se superpone al dock | Con autoocultar activo, una ventana superpuesta al dock permite que se oculte, aunque no esté maximizada |

La ocultación sobre pantalla completa funciona con autoocultar activo o inactivo. Las configuraciones nuevas y el preset Predeterminado la activan; al cargar una configuración existente que la tenga guardada como apagada, se conserva esa elección.

Las dos condiciones de ventana están desactivadas por defecto. Activar una desactiva la otra. En modo intersección, el dock permanece visible cuando ninguna ventana se superpone con él, incluso si hay una ventana maximizada que no lo toca. Sin ninguna activada, funciona el autoocultado normal. El cursor sigue mostrando el dock según el modo y los retrasos elegidos. Las ventanas minimizadas u ocultas, el escritorio y la barra de tareas no activan estas condiciones.

**Ocultar cuando una ventana se superpone al dock** también se actualiza mientras arrastras una ventana: ocultar y mostrar no esperan a que sueltes el botón del ratón. Se respetan los retrasos configurados. Si la ventana vuelve a superponerse antes de terminar el retraso de aparición, se cancela la aparición pendiente, salvo que el cursor esté en la zona de revelado del dock.

Con autoocultar activo también puedes ajustar:

- **Mostrar dock al…**: **Tocar el borde** o **Acercar el cursor al borde** (proximidad).
- Distancia de activación para proximidad, en centímetros: 1–400 píxeles lógicos (aproximadamente 0,03–10,58 cm), independiente del radio del hover. Fábrica: unos 1,6 cm (60 px lógicos a 96 DPI).
- Animación de deslizamiento y su duración (0–1200 ms; fábrica 150).
- Espera para mostrar (0–4000 ms; fábrica 150) y para ocultar (fábrica 250).

**Tocar el borde** exige que el punto activo del cursor (normalmente la punta de la flecha) alcance el píxel físico extremo del borde elegido en el monitor del dock, dentro de su tramo de activación. Se usa el borde de la pantalla completa, incluso cuando la barra de tareas reduce el área de trabajo. Pasar sobre la franja visible del dock oculto no activa este modo.

Mantén el cursor en ese borde durante el retraso para mostrar. Si lo apartas, se cancela la aparición pendiente; al volver a tocarlo, el retraso empieza de nuevo. Arrastrar archivos al dock y usar **Poner encima** desde la bandeja o el atajo **Poner el dock encima** siguen funcionando.

Con la escala de pantalla al 100 %, una distancia de proximidad de 1 píxel lógico puede coincidir con el contacto exacto en el borde inferior o derecho. El modo de tocar el borde siempre exige el píxel físico extremo, independientemente de la escala.

### Detalles

No edita nada. Muestra, para el monitor y el DPI actuales, el tamaño efectivo del dock, de los iconos y del hover, además del área de trabajo y la resolución.

### Solución de problemas

| Acción | Qué borra o cambia |
|---|---|
| Abrir carpeta del programa | Abre la carpeta del `.exe` en el Explorador |
| Generar registro detallado | Crea un `.txt` con configuración, monitores, DPI y estado del dock, para adjuntar a un informe |
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

Cambiar de preset recalcula el tamaño y la posición del dock junto al borde, incluso al volver a Predeterminado. La corrección del desplazamiento tras cambiar de preset no requiere borrar ni recrear los presets compatibles.

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
| `*.corrupt-*` | Archivo ilegible apartado para no pisar la copia de seguridad |

Hacer una copia de esa carpeta guarda la configuración. No hace falta copiar el `.exe` si solo quieres el respaldo de los datos.

Al iniciar, ToolDock elimina de `Temp_TD/` los `.tmp` sobrantes de guardados interrumpidos. Los archivos que otra instancia todavía tenga abiertos se conservan. Esta carpeta temporal no es necesaria en una copia de seguridad.

## Varias instancias

Puedes abrir ToolDock más de una vez, hasta 32. Cada instancia tiene su propio JSON y su propio icono de bandeja.

Para lanzar varias de golpe:

```text
ToolDock.exe --instances 2
```

También vale `--instances=2`. Las instancias extra reciben `--spawned` para no volver a multiplicarse.

## Inicio con Windows

ToolDock no ofrece un interruptor de «iniciar con Windows», para no tocar el Registro, el Programador de tareas ni servicios del sistema.

Si quieres que arranque al iniciar sesión:

1. Pulsa `Win + R`, escribe `shell:startup` y acepta.
2. Crea ahí un acceso directo a `ToolDock.exe`.
3. Si quieres dos docks, en las propiedades del acceso directo añade al final del destino: `--instances 2`.

## Límites actuales

- Orientado a Windows 11.
- Al soltar archivos solo entran `.exe` y `.lnk`. El resto se ignora. Las apps de la Store se agregan desde el selector UWP.
- El efecto visual y el contorno fino dependen del DPI y de cómo compone Windows la pantalla.
- Mover una app al monitor del dock no es garantizado: algunas ventanas eligen su monitor ellas mismas.
- **Desactivar dock** dura solo hasta cerrar la aplicación.
- El sistema de «fijar» opciones en un panel de acceso rápido existe en el código, pero está oculto en la interfaz.
