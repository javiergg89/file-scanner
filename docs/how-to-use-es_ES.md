[English](how-to-use.md) · Español

# Cómo usar File Scanner

La app se abre en inglés. Para pasarla a español, haz clic en el engranaje (*Settings*) de la barra de título, elige *Español* en *Language* y haz clic en *Restart now*. Esta guía usa los textos de la app en español. Volver al [README](../README-es_ES.md).

## Elegir qué escanear

La app escanea tu carpeta personal al abrirse. Para cambiarlo:

1. Haz clic en el botón de la barra de título que muestra qué se escanea (al principio, *Carpeta personal*).
2. Marca *Carpeta personal*, *Archivos de programa*, *Archivos de programa (x86)*, *ProgramData* o una unidad entera (*Unidad C: completa · 120 GB usados*).
3. Haz clic en *Escanear*.

Cuando las carpetas escaneadas están en más de una unidad, el gráfico empieza en *Este equipo*, con una rama por unidad.

Para escanear cualquier otra carpeta, incluida una carpeta compartida de red, usa una de estas opciones:

- *Elegir carpeta…* en el mismo menú.
- Arrastra la carpeta a la ventana. Si sueltas un archivo, aparece *Solo se pueden escanear carpetas. Suelta una carpeta.*
- En el Explorador de archivos, haz clic derecho en la carpeta o en una unidad y elige *Escanear con File Scanner*. El instalador añade esta opción en el idioma elegido durante la instalación.
- Ejecuta `FileScanner.exe "D:\Photos"` desde un terminal.

La app escanea entonces solo esa carpeta. Si no existe, escanea tu carpeta personal. La carpeta sigue en el menú *Qué escanear* hasta que cierres la app, así que puedes combinarla con las demás opciones y hacer clic en *Escanear* otra vez.

## Leer las cifras de arriba

La fila bajo la barra de título tiene cuatro cifras.

| Cifra | Significado |
|---|---|
| *Libre tras la limpieza* | Espacio libre en la unidad de tu carpeta personal más lo marcado en esa unidad. Lo marcado en otras unidades aparece como «+2 GB en D:». Lo que se envía a la Papelera de reciclaje ocupa espacio hasta que la vacíes. |
| *Seguro para eliminar* | Total de las recomendaciones de *Seguro para eliminar*. |
| *Revisar primero y restos* | Total de las recomendaciones de *Revisar primero* y *Posibles restos de programas*. |
| *Sin escanear* | Espacio usado en la unidad fuera de las carpetas escaneadas (sistema y programas). |

## Recorrer las carpetas

El panel izquierdo tiene seis pestañas: *Anillos*, *Mapa*, *Lista*, *Tipos*, *Más grandes* y *Duplicados*.

En *Anillos* y *Mapa*, haz clic en un sector o recuadro para entrar en esa carpeta, o en *Archivos* para elegir en el panel derecho qué archivos eliminar. Para subir, haz clic en el centro de los anillos, pulsa Retroceso o usa el botón atrás del ratón. El botón de información junto a *Uso del disco* muestra estos pasos. La información emergente de cada sector o recuadro muestra su ruta, tamaño y número de archivos.

Haz clic derecho en un sector o recuadro para ver más opciones: *Entrar* (solo en carpetas con subcarpetas), *Abrir en el Explorador de archivos*, *Copiar ruta*, *Exportar a CSV…*, *Elegir qué eliminar aquí…*, *Añadir a «Elegido por ti»* o *Quitar de «Elegido por ti»*, *Volver a escanear esta carpeta* y *Eliminar…*. En *Archivos* el menú tiene *Elegir qué archivos eliminar…* en vez de *Elegir qué eliminar aquí…*. Las carpetas protegidas (consulta [Eliminar](#eliminar)) no tienen *Eliminar…* ni las opciones de *Elegido por ti*.

*Lista* muestra la misma carpeta como tabla: una fila por subcarpeta, más *Archivos* para sus archivos sueltos, con tamaño, porcentaje de la carpeta, número de archivos y fecha de modificación, de mayor a menor. Haz clic en el encabezado de una columna para ordenar por ella y otra vez para invertir el orden. Haz clic en una fila o pulsa Intro o Espacio para entrar en esa carpeta. La fila *Archivos* abre sus archivos en el panel derecho. Haz clic derecho en una fila para ver el mismo menú que en el gráfico.

### Color por antigüedad

La lista *Colorear el gráfico*, debajo del gráfico, cambia entre *Color por categoría* (la opción por defecto) y *Color por antigüedad*. Por antigüedad, cada carpeta toma el color del cambio más reciente en cualquier nivel dentro de ella:

- Azul: menos de 1 mes.
- Morado: de 1 a 6 meses.
- Amarillo: de 6 a 12 meses.
- Naranja: de 1 a 3 años.
- Rojo: más de 3 años.

En este modo la información emergente también muestra esa fecha. Las carpetas marcadas para eliminar son verdes en los dos modos.

### Buscar una carpeta

1. Haz clic en el cuadro de búsqueda al final de la barra de ruta, o pulsa Ctrl+F.
2. Escribe parte del nombre de una carpeta. El cuadro muestra, de todo el escaneo, las 20 carpetas más grandes cuyo nombre contiene el texto, con su ubicación y tamaño.
3. Elige una, o pulsa Intro para la primera, para entrar en ella en el gráfico.

Con `*` o `?`, el texto tiene que coincidir con el nombre entero: `node_*` encuentra `node_modules` pero no `my_node_app`.

## Espacio por tipo de archivo

*Tipos* muestra el espacio que ocupa cada extensión (`.mp4`, `.iso`, `.psd`…) en la carpeta de la barra de ruta y sus subcarpetas, de mayor a menor, con su porcentaje de la carpeta y su número de archivos. La lista cambia con la carpeta que abres.

## Archivos más grandes

*Más grandes* muestra los 100 archivos más grandes de la carpeta de la barra de ruta y sus subcarpetas, con carpeta, fecha y tamaño.

Marca un archivo para añadirlo a *Elegido por ti*. Haz clic derecho en él para *Abrir*, *Mostrar en el Explorador de archivos*, *Copiar ruta*, *Mostrar carpeta en el gráfico*, *Añadir a «Elegido por ti»* o *Quitar de «Elegido por ti»*, o *Eliminar…*. Un archivo que ya elimina otra recomendación aparece con una marca parcial y la nota *Se elimina con «…»*.

## Recomendaciones

El panel derecho muestra las recomendaciones en grupos:

- *Seguro para eliminar* tiene cachés que su herramienta vuelve a descargar sola, archivos temporales, volcados de memoria, cachés de navegadores y de apps, cachés de shaders de GPU, la caché de miniaturas y las descargas de Windows Update.
- *Revisar primero* tiene datos que cuesta tiempo recuperar: el almacén de pnpm, los paquetes NuGet, Maven, Cargo y las toolchains de Rust, el Android SDK, los modelos de Ollama y de Hugging Face, las cachés de Unreal y Unity, la caché del instalador de Visual Studio, Descargas, *Instalación anterior de Windows* (`Windows.old`) y `node_modules` o carpetas de build (`target`, `obj`) en proyectos sin cambios en 30 días.
- *Posibles restos de programas* tiene carpetas que no usa ningún programa instalado (consulta más abajo).
- *Elegido por ti* tiene carpetas y archivos que añades tú.
- *Ha crecido* tiene carpetas que han crecido desde el escaneo guardado (consulta más abajo).

Un grupo aparece solo cuando tiene algo. Al abrir la app no hay nada marcado. Algunos títulos dicen qué hacer antes, por ejemplo cerrar el navegador, o que hacen falta permisos de administrador.

El menú contextual de una recomendación tiene *Ver contenido*, *Mostrar en el gráfico*, *Abrir en el Explorador de archivos*, *Copiar rutas*, *Marcar todo*, *Desmarcar todo* y *Eliminar lo marcado…*.

### Marcar qué eliminar

1. Marca una recomendación entera. *Marcar los seguros* marca todo el grupo *Seguro para eliminar* y desmarca el resto.
2. Para elegir dentro de una recomendación, haz clic en el botón de flecha al final de su fila o haz doble clic en ella. El panel muestra su contenido.
3. Marca subcarpetas y archivos concretos. Una carpeta con algo sin marcar dentro aparece con una marca parcial.
4. En esta vista los botones pasan a ser *Marcar todo*, *Copiar script (todo)* y *Eliminar todo lo marcado…*. *Volver a las recomendaciones* (la flecha de arriba) vuelve a la lista.

La mayoría de las recomendaciones vacían una carpeta y la conservan. En esta vista, haz clic derecho en esa carpeta y elige *Eliminar el contenido…* para vaciar solo esa.

El texto sobre los botones muestra el total marcado, por ejemplo «2,4 GB marcados · 3 grupos». *Desmarcar todo* quita todas las marcas.

Para añadir una carpeta desde el gráfico, haz clic derecho en ella y elige *Añadir a «Elegido por ti»*. *Quitar de «Elegido por ti»* la saca.

### Posibles restos de programas

La app busca en Archivos de programa, Archivos de programa (x86) y ProgramData cuando los escaneas, y siempre en AppData\Local, AppData\Local\Programs y AppData\Roaming. Una carpeta de esas ubicaciones aparece en este grupo cuando no ha cambiado en 60 días, ocupa al menos 20 MB y ningún programa instalado tiene sus archivos en ella ni coincide con su nombre o su editor.

Las apps de Microsoft Store, los programas portables y las partidas guardadas también pueden cumplir estas reglas. Abre cada carpeta antes de eliminarla.

## Eliminar

1. Marca los elementos y haz clic en *Eliminar…*.
2. El cuadro de diálogo muestra hasta 5 rutas, el número de elementos y el tamaño total.
3. Elige *Enviar a la papelera* (la opción por defecto), *Eliminar definitivamente* o *Cancelar*.

Las eliminaciones se hacen una tras otra en una cola, y puedes seguir usando la app mientras tanto. El botón de la cola en la barra de título muestra el progreso; haz clic en él para ver cada tarea. *Quitar terminados* borra de la cola las tareas terminadas. El cuadro de progreso de Windows muestra la velocidad de cada eliminación y permite cancelarla. Una tarea cancelada muestra cuántos elementos no se eliminaron.

Cuando algunos elementos no se pueden eliminar (en uso o con acceso denegado), la app dice cuántos son y muestra el primero.

La app nunca elimina tu perfil, AppData y sus carpetas Local, LocalLow y Roaming, Escritorio, Documentos, Imágenes, Vídeos, Música, Descargas, Windows, Archivos de programa, Archivos de programa (x86), ProgramData ni la raíz de una unidad, ni una carpeta que contenga alguna de ellas. Sí puede eliminar lo que hay dentro.

Eliminar dentro de Archivos de programa, ProgramData o Windows requiere permisos de administrador. La app funciona sin ellos, así que ofrece *Copiar comando* para lo que no pudo eliminar. Ejecuta ese comando en PowerShell como administrador.

### Copiar un script en su lugar

*Copiar script* pone en el portapapeles un script de PowerShell con los elementos marcados. Léelo y ejecútalo tú. El script no sigue junctions ni enlaces simbólicos, así que no elimina sus destinos.

### Vaciar la Papelera de reciclaje

El botón *Papelera* de la barra de título muestra su tamaño. Haz clic en él y confirma con *Vaciar*.

## Buscar duplicados

1. Abre la pestaña *Duplicados*.
2. Elige un tamaño mínimo: *1 MB o más*, *10 MB o más* (la opción por defecto) o *100 MB o más*.
3. Haz clic en *Buscar duplicados*. *Cancelar* detiene la búsqueda.

La búsqueda compara por pasos: mismo tamaño, luego los primeros y los últimos 64 KB, y luego un SHA-256 completo de lo que sigue coincidiendo. Los hard links cuentan una vez porque no ocupan espacio extra. Los archivos de OneDrive que están solo en la nube se omiten para no descargarlos.

La búsqueda omite las carpetas donde es normal que haya copias: AppData, Archivos de programa, Archivos de programa (x86), ProgramData, las carpetas cuyo nombre empieza por un punto (`.git`) y las carpetas llamadas `node_modules`, `bower_components`, `vendor`, `packages`, `bin`, `obj`, `dist`, `build`, `out`, `target`, `venv`, `env`, `__pycache__`, `site-packages`, `Pods`, `DerivedData`, `Windows`, `$Recycle.Bin` o `System Volume Information`.

La pestaña tiene sus propias marcas. *Marcar copias de más* marca todas las copias menos la más antigua de cada grupo. *Desmarcar todo*, *Copiar script* y *Eliminar…* funcionan como en las recomendaciones.

La app no deja marcar ni eliminar la última copia de un grupo. Al contar las copias, tiene en cuenta también lo que van a eliminar las recomendaciones.

### Exclusiones

Haz clic en el botón *No es duplicado* de un grupo para ocultarlo. Haz clic derecho en una copia para excluir su carpeta o su extensión, o para marcar el grupo como *No es duplicado*. Haz clic en *Exclusiones* para ver y editar las listas; el botón muestra cuántas hay, por ejemplo *Exclusiones (3)*.

| Sección | Qué contiene |
|---|---|
| *Carpetas excluidas* | Rutas completas. Escribe una o usa *Examinar…* y haz clic en *Añadir*. También se omiten sus subcarpetas. |
| *Tipos de archivo excluidos* | Extensiones separadas por comas, solo la última parte (`gz`, no `tar.gz`). |
| *Grupos que no son duplicados* | Grupos que se reconocen por su contenido, así que siguen ocultos aunque las copias cambien de sitio. |

Las exclusiones se aplican al momento a los resultados en pantalla. Vuelve a buscar para ver archivos que excluiste antes.

## Exportar a CSV

1. Haz clic derecho en un sector, recuadro o fila.
2. Elige *Exportar a CSV…* e indica dónde guardarlo.

El archivo tiene esa carpeta y sus subcarpetas hasta tres niveles de profundidad, de mayor a menor, con ruta, tamaño en bytes, número de archivos y fecha de modificación. Está en UTF-8 con BOM, así que Excel muestra bien las tildes. El separador es el de listas de la configuración regional de Windows («,» con el formato de Estados Unidos, «;» con el de España). Los encabezados de columna siguen el idioma de la app. Los archivos sueltos no tienen fila; cuentan en el tamaño de su carpeta.

## Cambios en directo y nuevos escaneos

La app vigila las carpetas escaneadas. Los cambios hechos fuera de ella, por ejemplo eliminar una carpeta en el Explorador de archivos, aparecen en menos de 2 segundos sin volver a escanear. Si hay demasiados cambios a la vez, o una carpeta no se puede vigilar, un aviso te pide que vuelvas a escanear.

Para leer de nuevo una sola carpeta, haz clic derecho en ella y elige *Volver a escanear esta carpeta*. *Cancelar* detiene un escaneo en curso.

## Escaneos guardados y crecimiento

Cada escaneo se guarda, comprimido, en `%LOCALAPPDATA%\FileScanner`. Cuando abres la app con las mismas carpetas, muestra al momento el escaneo guardado y vuelve a escanear en segundo plano. Tus marcas y la carpeta abierta se mantienen.

Después de ese nuevo escaneo, la app compara los dos:

- Las carpetas que han crecido 100 MB o más aparecen en *Ha crecido*, hasta 10, de mayor a menor crecimiento. Una carpeta que solo ha crecido por una subcarpeta que ya está en la lista no aparece.
- La información emergente de cada sector o recuadro muestra su crecimiento, por ejemplo «+2,3 GB desde el escaneo de hace 3 días».

Haz clic en una fila de *Ha crecido*, o pulsa Intro o Espacio, para entrar en esa carpeta en el gráfico. Haz clic derecho en ella para ver el menú del gráfico.

El crecimiento se mide respecto al escaneo guardado que se carga al abrir la app. Los nuevos escaneos mantienen esa referencia. Escanear otro conjunto de carpetas empieza una referencia nueva. Cada conjunto de carpetas tiene su propio escaneo guardado; los escaneos guardados que llevan 30 días sin usarse se eliminan.

## Configuración

El engranaje de la barra de título abre la configuración. *Apariencia* puede ser *Usar la configuración de Windows* (la opción por defecto), *Claro* u *Oscuro*, y se aplica al momento. *Idioma* puede ser *English* (la opción por defecto) o *Español*, y se aplica al reiniciar; la app ofrece *Reiniciar ahora*.

La configuración se guarda en `%LOCALAPPDATA%\FileScanner\settings.json`. Elimina ese archivo para volver a los valores por defecto.

## Teclado y ratón

| Tecla | Acción |
|---|---|
| Ctrl+F | Buscar carpetas |
| Retroceso o botón atrás del ratón | Subir un nivel en el gráfico o en la recomendación abierta |
| Tab y luego las flechas | Ir al gráfico y luego moverse entre los sectores o recuadros del primer nivel |
| Mayús+F10 o tecla de menú | Abrir el menú contextual |
| Intro o Espacio | Entrar en el sector o recuadro con el foco, o en la fila seleccionada de *Lista* o *Ha crecido* |

## Archivos que guarda la app

Todos en `%LOCALAPPDATA%\FileScanner`:

- Los escaneos guardados.
- `settings.json`.
- `duplicates-exclusions.json`.
- `crash.log`, que solo se escribe si la app se cierra por un error.

Al desinstalar se elimina esta carpeta. Después de una instalación para todos los usuarios, se queda en el perfil de cada usuario. La app se instala en `%LOCALAPPDATA%\Programs\File Scanner` (en Archivos de programa si es para todos los usuarios).

## Límites

- El escaneo no guarda los nombres de los archivos. *Tipos* y *Más grandes* vuelven a leer las carpetas, y la búsqueda solo encuentra carpetas.
- *Volver a escanear* lee otra vez todas las carpetas escaneadas, incluidas las que no han cambiado.
- En modo de demostración (`--demo`) la app no escanea nada y no cambia ni abre nada en el disco. Eliminar, exportar, abrir, copiar al portapapeles, vaciar la Papelera de reciclaje, volver a escanear una carpeta y guardar la configuración están desactivados. Las exclusiones de duplicados se aplican, pero no se guardan.
