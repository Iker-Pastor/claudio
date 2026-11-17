# Mascota Claude 🟧

Una mascota virtual de escritorio para Windows, hecha en C# + Windows Forms. Es un pequeño bicho naranja de estilo pixel-art que vive encima de tus ventanas, pasea por la pantalla y reacciona al ratón.

## Características

- **Pasea sola** por el suelo de la pantalla, con parpadeo y animación de patas.
- **Trepa por los bordes**: sube por las paredes exteriores y camina por el techo.
- **Multi-monitor**: cruza de una pantalla a otra, incluso con escalones entre monitores de distinta altura.
- **Física**: cógela con el ratón y cuelga del punto donde la agarres, balanceándose y girando. Suéltala en movimiento para lanzarla: vuela, gira y rebota (las pequeñas rebotan más, las grandes son más pesadas).
- **Click**: da un saltito y trae la app de Claude al frente (la abre si no está abierta).
- **Rueda del ratón** encima de ella: la hace crecer o encoger.
- **Click derecho**: menú con *Abrir Claude*, *Quieta / Pasear* y *Salir*.

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
| `mascota.vbs` | Lanzador: compila `mascota.exe` si hace falta y lo arranca |
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
