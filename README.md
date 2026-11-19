# Mascota Claude 🟧

Una mascota virtual de escritorio para Windows, hecha en C# + Windows Forms. Es un pequeño bicho naranja de estilo pixel-art que vive encima de tus ventanas, pasea por la pantalla y reacciona al ratón.

## Características

- **Pasea sola** por el suelo de la pantalla, con parpadeo y animación de patas.
- **Trepa por los bordes**: sube por las paredes exteriores y camina por el techo.
- **Multi-monitor**: cruza de una pantalla a otra, incluso con escalones entre monitores de distinta altura.
- **Física**: cógela con el ratón y cuelga del punto donde la agarres, balanceándose y girando. Suéltala en movimiento para lanzarla: vuela, gira y rebota (las pequeñas rebotan más, las grandes son más pesadas).
- **Click**: da un saltito y trae la app de Claude al frente (la abre si no está abierta).
- **Rueda del ratón** encima de ella: la hace crecer o encoger.
- **Click derecho**: menú con *Abrir Claude*, *Mostrar mascota*, *Color*, *Aspecto*, *Quieta / Pasear*, *Teams en verde*, *Bocadillos* y *Salir*.
- **Aspecto**: click derecho → *Aspecto* para cambiar entre Claudio, **Claudio OG** (el diseño original, con ojos de píxel y sin cara nueva) y **cualquier Pokémon** (*Elegir Pokémon...*: buscador por nombre o número de los 1025 de la PokeAPI, con casilla *Shiny*). Usa los sprites 2D estilo Gen 5 de [PokeAPI/sprites](https://github.com/PokeAPI/sprites): se descargan la primera vez y se guardan en `%LOCALAPPDATA%MascotaClaudesprites` (Clawitzer viene incrustado, funciona sin conexión). Con un Pokémon, el menú *Color* ofrece *Normal* o *Shiny* en vez de la paleta. Se voltea según hacia dónde camina (algunos sprites miran al otro lado) y no dibuja los ojos, la boca, los accesorios ni la animación de programar de Claudio; los bocadillos, el ánimo, el sueño y las partículas siguen funcionando. Los Pokémon que vas usando (hasta 10) aparecen en el propio menú *Aspecto* para volver a ellos con un click. Con un Pokémon o Claudio OG no salen *Hacer...* ni *Accesorios*. La elección se guarda.
- **Icono junto al reloj**: mientras corre, hay un icono en la bandeja del sistema (puede estar en los iconos ocultos) con el mismo menú que el click derecho. Click izquierdo = mostrar / ocultar la mascota (oculta se queda quieta, sin avisos; *Teams en verde* sigue activo). *Salir* la cierra del todo.
- **Color**: click derecho → *Color* para elegir uno predefinido (naranja Claude, rojo, azul, verde, morado, rosa, amarillo, blanco) o uno personalizado, o el modo *Arcoíris*, en el que va cambiando de color sin parar. La elección se guarda.

- **Estados de ánimo**: *feliz* (si la acaricias moviendo el ratón por encima sin pulsar: ojos ^^ y corazoncitos y anda más rápido), *aburrida* (sin interacción ~10 min: anda más lenta, ojos entornados y suspira), *mareada* (si la lanzas o rebota mucho en poco tiempo: ojos en espiral y se tambalea unos segundos) y *dormida*.
- **Bocadillos de texto** con frases cortas en español: salen de vez en cuando (cada 3-8 min) y como reacción (al lanzarla, acariciarla, marearse o despertarse). No roban el foco ni captan clicks. Las frases están en arrays al principio de `PetForm` en `mascota.cs`, fáciles de editar. Se pueden silenciar desde el menú (*Bocadillos*, se guarda).
- **Dormir**: si no hay actividad en el equipo ~5 min (o es de noche, 01:00-07:00, con ~1 min de inactividad) se deja caer al suelo si estaba en una pared o el techo, se tumba con los ojos cerrados y "Zzz". Se despierta al volver a usar el ratón o el teclado.
- **Ojos que miran**: las pupilas se desplazan hacia el cursor y, muy de vez en cuando, si el cursor está cerca en su mismo suelo, lo persigue unos segundos.

- **Subirse a ventanas**: camina por el borde superior de las ventanas visibles de otras apps (ignora minimizadas, ocultas, sin título, "cloaked" y maximizadas). Si la ventana se mueve, la acompaña; si se cierra, se minimiza o la tapa otra, cae con física. Al caer puede aterrizar en una ventana y a veces salta a una cercana. Opción de menú *Subir a ventanas* (se guarda). La lista de ventanas se refresca ~1 vez por segundo.
- **Reacciona a eventos** (bocadillo + ánimo): batería baja (<20 % sin cargador, una vez por bajada), cargador conectado y "¡Claude te ha respondido!" cuando la ventana de Claude parpadea en la barra de tareas (hook `HSHELL_FLASH`; depende de que la app de Claude pida atención al terminar, y se ignora si pasan menos de 15 s desde el último aviso).
- **Recordatorios personalizables** en un archivo de texto, `recordatorios.txt`, que la mascota crea al arrancar en su misma carpeta, junto a `mascota.vbs` (si tenías uno de la versión anterior en `%LOCALAPPDATA%\MascotaClaude`, lo copia; está en `.gitignore` para que tus avisos no choquen al actualizar) (menú *Recordatorios* → *Editar recordatorios.txt...* lo abre en el Bloc de notas; *Abrir carpeta* lo muestra en el Explorador). Una línea por aviso, `CUÁNDO | MENSAJE | ÁNIMO`, y al guardar la mascota lo recarga sola en unos segundos (te avisa de cuántos hay activos o de la línea que no entiende). Ejemplos:

  ```
  L-V 15:00 | ¡Hora de salir! | feliz
  cada 60 | Toca beber agua.
  cada 20 09:00-19:00 | Mira algo lejano 20 segundos.
  laborables 13:45 | ¡A comer!
  S,D 11:00 | ¿Salimos a pasear?
  24/12 18:00 | ¡Feliz Nochebuena! | feliz
  ```

  *Cuándo*: una hora fija (`HH:MM`) o `cada N` minutos (`cada 2h` también vale), opcionalmente con una franja (`09:00-18:00`), días (`L M X J V S D`, rangos `L-V`, listas `L,X,V`, `laborables`, `finde`) y/o una fecha (`24/12` todos los años o `2026-12-24`). *Ánimo* (opcional): `normal`, `feliz` (da un saltito), `aburrida` o `mareada`. Las líneas con `#` se ignoran. El archivo se crea la primera vez con "¡Hora de salir!" activo y ejemplos comentados (si tenías activados agua, pausa activa o gimnasio en el menú antiguo, salen ya activados). El *Pomodoro* (25/5 min) sigue en el menú y no se guarda. Los avisos salen aunque silencies los bocadillos, duran más y no se pisan con los normales; si está dormida, los de hora fija esperan hasta 10 min a que despierte y los de `cada N` se saltan esa vez.

- **Animaciones**: *programa* (busca en su costado, saca un portátil pequeño de perfil hacia el lado al que mira, lo abre en tres pasos, se agacha hacia él y teclea con la manita mientras la pantalla parpadea; al acabar lo cierra y lo guarda; dura ~9-14 s), bosteza (boca grande y ojos cerrados; antes de dormirse, cada ~3-5 min al azar junto a *programar* y mucho más a menudo si está aburrida) y saluda con un bracito que se inclina de lado a lado y cara contenta (al hacer click, que sustituye al saltito, al despertarse y cuando el cursor se acerca tras ~2 min sin verlo). Las espontáneas solo ocurren de pie en el suelo o sobre una ventana. Puedes lanzarlas a mano desde el menú *Hacer...* (*Saludar*, *Bostezar*, *Programar*).
- **Más adorable**: ojos alargados (2x3 píxeles) con brillo y parpadeo en tres fases, cara sin boca salvo en acciones (bostezo, sonrisa al saludar y "o" de sorpresa mientras la agarras o vuela), *squash & stretch* (se achata al aterrizar y se alarga al saltar), respiración suave, rebotito al andar, saltitos ocasionales cuando está feliz y se sacude al soltarla en el suelo. Los bocadillos incluyen frases más tiernas (`:3`, `^^`, `<3`).
- **Partículas**: polvillo al aterrizar o rebotar fuerte y estrellitas mientras está mareada (los corazones son los del estado feliz). Se dibujan en una ventana overlay ligera y click-through que solo existe mientras quedan partículas (máx. 20), así el click sigue contando solo sobre el cuerpo.
- **Accesorios por fecha**: gorro de Navidad (1 dic - 6 ene), sombrero de bruja (25-31 oct), gorro de fiesta (31 dic - 1 ene, tiene prioridad) y gafas de sol (julio y agosto). Giran con ella en paredes y techo y escalan con su tamaño. Menú *Accesorios*: *Automático*, *Ninguno* o forzar cada uno para probarlos (se guarda).

## Requisitos

- Windows 10 u 11
- .NET Framework 4 (incluido en Windows). No requiere instalar nada más: el compilador de C# que trae Windows la compila sola la primera vez.
- Consumo aproximado: ~30 MB de RAM y ~1-2 % de un núcleo de CPU.

## Uso

Haz doble clic en `mascota.vbs`. La primera vez (y cada vez que modifiques `mascota.cs`) compila `mascota.exe` y después lo lanza sin ventana de consola.

### Arranque automático con Windows

| Archivo | Acción |
|---|---|
| `activar-inicio.bat` | Crea un acceso directo en la carpeta Inicio para que arranque al encender el PC |
| `desactivar-inicio.bat` | Elimina ese acceso directo |

## Archivos

| Archivo | Descripción |
|---|---|
| `mascota.cs` | Código principal (sprite, física, animación, menú) |
| `clawitzer.png` / `clawitzer-shiny.png` | Sprites de Clawitzer (normal y shiny) incrustados en el exe al compilar; el resto de Pokémon se descargan de PokeAPI/sprites |
| `mascota.vbs` | Lanzador: compila `mascota.exe` si hace falta y lo arranca |
| `recordatorios.txt` | Tus recordatorios (lo crea la mascota al arrancar; no se sube al repositorio) |
| `activar-inicio.bat` / `desactivar-inicio.bat` | Gestión del arranque automático |

## Personalización

En la parte superior de `mascota.cs` puedes ajustar (se recompila sola al relanzar `mascota.vbs`; cierra antes la mascota abierta):

- `PX`: tamaño de cada "píxel" del sprite.
- `Body`, `LegsA`, `LegsB`: el sprite (cada carácter es un píxel; `X` = cuerpo, `E` = ojo, `.` = vacío).
- `Orange` / `Black`: colores.
- `GRAV`, `MAXV`, `BOUNCE`: gravedad, velocidad máxima de lanzamiento y rebote.

## Notas

- Los scripts se ejecutan con `-ExecutionPolicy Bypass` solo para este proceso; no cambian la configuración del sistema.
- Para detenerla: click derecho → *Salir*.
- Proyecto no oficial; no está afiliado a Anthropic. "Claude" es una marca de Anthropic.

## Licencia

[MIT](LICENSE)
