# Pokémon con más vida y sombreros — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que los Pokémon (skin `pokemon`) se muevan con más personalidad y puedan llevar los mismos gorros/sombreros que Claudio.

**Architecture:** Todo vive en `mascota.cs` (C# WinForms, GDI+, sin tests). Se añade un arnés de tests mínimo que compila `mascota.cs` junto a `tests/MascotaTests.cs` con `csc /main:MascotaTests`, de modo que las funciones nuevas puras (`internal static`) se prueban sin abrir la ventana. La lógica de dibujo y del bucle (`OnPaint`, `OnTick`) solo llama a esas funciones puras.

**Tech Stack:** C# 5 / .NET Framework 4 (`csc.exe` de `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\`), System.Drawing, System.Windows.Forms.

**Spec:** Petición del usuario (2026-11-20): *"añadirle más vida a los modelos de Pokémon. Además se les podría añadir sombreros y gorros como los que tiene el aspecto Claude"*. Decisiones de diseño tomadas en este plan (a validar en la revisión):
- **Vida:** (a) contoneo al andar (inclinación ±4°), (b) "Saludar" = dos saltitos + balanceo, (c) "Bostezar" = estirarse hacia arriba y soltar el aire, (d) "Programar" no tiene sentido sin portátil → en Pokémon se sustituye por "Mirar alrededor" (se gira a un lado y al otro). El menú "Hacer..." vuelve a aparecer con Pokémon (sin "Programar").
- **Sombreros:** Navidad, Bruja y Fiesta se dibujan sobre la parte más alta del sprite, centrados en la "cabeza" detectada en el PNG, con tamaño de celda ajustado al ancho de esa cabeza. **Gafas no** (no se sabe dónde están los ojos de cada Pokémon): se oculta en el menú con Pokémon y, si estaba elegida o toca por fecha (julio–agosto), no se dibuja nada. Claudio OG sigue sin accesorios.
- El parpadeo y la mirada de Claudio no se aplican (los sprites no tienen ojos separables).
- **Revisión del usuario (2026-11-20):** *"Si el gorro está sobre las orejas o los cuernos no lo añadas."* → si lo alto del sprite son orejas/cuernos (franja superior partida en varios trozos, o mucho más estrecha que el sprite), ese Pokémon no lleva gorro (`HatFits`).

## Global Constraints

- Un único archivo de código: `mascota.cs`. Debe seguir compilando con `mascota.vbs` (`csc /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll`) — nada de sintaxis C# > 5 (`$""`, `?.`, `=>` en miembros, `out var`).
- No cambiar el formato de `config.txt` (claves `aspecto`, `accesorio`, etc. ya existen; `accesorio` se reutiliza tal cual).
- Claudio (`skin == "claudio"`) debe verse y comportarse exactamente igual que antes.
- Repintar solo cuando cambie algo visible (`InvalidateIfChanged`); todo valor nuevo que afecte al dibujo entra en esa comparación.
- Comentarios y textos de menú en español, sin tildes en el código (estilo actual del archivo).

## Review Focus

1. **Pokémon con orejas/cuernos/antenas en lo alto** (Pikachu, Clawitzer): no se dibuja el gorro → `HatFits` (tests en Task 2); con cabeza redonda sí se dibuja.
2. **Cambiar de Pokémon o de tamaño (PX) con sombrero puesto**: la caja de la cabeza se recalcula con el sprite nuevo, no se queda la del anterior → se calcula en `BuildSprites`, que ya se invalida con `sprPX = -1` (verificación manual en Task 3).
3. **Gafas elegidas (o julio–agosto en automático) y luego pasar a Pokémon**: no se dibujan gafas y no hay excepción; al volver a Claudio reaparecen → `SpriteAcc` (test en Task 2).
4. **Andar por paredes/techo o ser arrastrado**: el contoneo no debe sumarse cuando está en el aire o arrastrado (`Free`) ni romper los ángulos 90/180/270 → `WaddleAngle` solo con `Mode.Floor` (test en Task 1).
5. **Animación automática "Programar" con Pokémon** (sale sola por aburrimiento, `mascota.cs:2330`): no debe agacharse 15 s mirando a la nada → se reconvierte a `Anim.Look` en `StartAnim` (test en Task 1).

---

### Task 0: Arnés de tests

**Files:**
- Create: `tests/MascotaTests.cs`
- Create: `tests/run-tests.bat`
- Modify: `.gitignore` (añadir `tests/tests.exe`)

**Interfaces:**
- Produces: `static class MascotaTests { static int Main() }` con un helper `static void Eq(object esperado, object real, string nombre)` que imprime `OK nombre` / `FALLO nombre: esperado X, real Y` y cuenta fallos; `Main` devuelve el nº de fallos. `run-tests.bat` compila con
  `"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /main:MascotaTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:tests\tests.exe mascota.cs tests\MascotaTests.cs` (con fallback a `Framework\`) y ejecuta `tests\tests.exe`, saliendo con su código.

- [ ] **Step 1:** Escribir el arnés con un único test `AutoAcc_Nochevieja`: `Eq(Acc.Fiesta, PetForm.AutoAcc(new DateTime(2026,12,31)), ...)`. `AutoAcc` (`mascota.cs:1408`) es `static` privado → cambiarlo a `internal static`.
- [ ] **Step 2:** Ejecutar `tests\run-tests.bat` desde la raíz. Esperado: `OK AutoAcc_Nochevieja`, código de salida 0.
- [ ] **Step 3:** Confirmar que `mascota.vbs` sigue compilando (borrar `mascota.exe` y lanzarlo, o ejecutar el `csc` del vbs a mano). Esperado: compila sin errores.
- [ ] **Step 4:** Commit `test: arnes minimo de tests compilando mascota.cs`.

### Task 1: Más vida para los Pokémon

**Files:**
- Modify: `mascota.cs` — `enum Anim` (`:201`), menú `miDo` (`:668-672`), `ApplySkin` (`:953-955`), `CurAngle` (`:1272`), `SqS` (`:1293`), `InvalidateIfChanged`/`AnimKey` (`:1327-1344`), `StartAnim` (`:2218`), bucle de animación en `Think` (`~:2320`), rama sprite de `OnPaint` (`:1555-1567`).
- Test: `tests/MascotaTests.cs`

**Interfaces:**
- Produces (todas `internal static` en `PetForm`):
  - `enum Anim { None, Code, Yawn, Wave, Look }` (añadir `Look` al final).
  - `Anim SpriteAnim(Anim a)` → `Code` ⇒ `Look`; el resto igual.
  - `float WaddleAngle(bool walking, bool paused, bool onFloor, int frame)` → `0` si no anda, está en pausa o no está en el suelo; si no, `frame == 0 ? -4f : 4f`.
  - `float WaveTilt(int animT)` → balanceo ±6° : `(float)(6 * Math.Sin(animT * 0.35))` redondeado a entero (para no repintar cada tick).
  - `double YawnSquash(int animT, int animLen)` → estirarse: `-0.15` sostenido entre `animT` 10 y `animLen-20`, rampa lineal de 0 a -0.15 en los primeros 10 ticks, y en los últimos 20 ticks un achatado `+0.10` que vuelve a 0 (soltar el aire).
  - `bool LookFlipped(int animT, int animLen)` → `true` en el tercio central (`animLen/3 <= animT < 2*animLen/3`).
  - `bool WaveHopAt(int animT)` → `animT == 2 || animT == 26`.

- [ ] **Step 1: Tests que fallan:** `SpriteAnim_CodeEsLook` (`Look`), `SpriteAnim_WaveIgual`, `Waddle_Parado` (`WaddleAngle(false,false,true,1) == 0`), `Waddle_Pared` (`WaddleAngle(true,false,false,1) == 0`), `Waddle_Pasos` (`-4` con frame 0, `4` con frame 1), `WaveTilt_Inicio` (`WaveTilt(0) == 0`), `Yawn_Estira` (`YawnSquash(30,70) == -0.15`), `Yawn_Suelta` (`YawnSquash(55,70) > 0`), `Yawn_Fin` (`YawnSquash(70,70) == 0`), `Look_Centro` (`LookFlipped(45,90)` true; `LookFlipped(10,90)` false), `WaveHop` (true en 2 y 26, false en 3).
- [ ] **Step 2:** `tests\run-tests.bat` → FALLO de compilación (funciones no definidas).
- [ ] **Step 3: Implementar** las funciones de arriba y conectarlas, todo condicionado a `useSprite`:
  - `StartAnim`: si `useSprite`, `a = SpriteAnim(a)`; para `Look`, `len = 90`.
  - `CurAngle`: si `!Free && useSprite` devolver `ModeAngle() + WaddleAngle(walking, paused, mode == Mode.Floor, frame) + (anim == Anim.Wave ? WaveTilt(animT) : 0)`.
  - `SqS`: si `useSprite`, el término de `Anim.Code` no se aplica y se suma `YawnSquash(animT, animLen)` cuando `anim == Anim.Yawn`.
  - Bucle (`~:2321`, tras `++animT`): si `useSprite && anim == Anim.Wave && WaveHopAt(animT) && hop <= 0`, poner `hopV = 4`.
  - `Flip()`: si `useSprite && anim == Anim.Look && LookFlipped(animT, animLen)`, devolver el contrario.
  - `AnimKey`: añadir `case Anim.Look: return 50 + (LookFlipped(animT, animLen) ? 1 : 0);`.
  - Menú: guardar `miDoCode` (el item "Programar") y en `ApplySkin` poner `miDo.Visible = skin != "og"` y `miDoCode.Visible = !useSprite`.
- [ ] **Step 4:** `tests\run-tests.bat` → todos `OK`, salida 0.
- [ ] **Step 5: Verificación manual** (`mascota.vbs`, con un Pokémon): anda contoneándose; "Hacer → Saludar" da dos saltitos balanceándose; "Bostezar" se estira y se achata; no aparece "Programar". Cambiar a Claudio: todo igual que antes (incluido "Programar").
- [ ] **Step 6:** Commit `feat: contoneo, saludo, bostezo y mirar alrededor para Pokemon`.

### Task 2: Geometría de sombreros (funciones puras + datos compartidos)

**Files:**
- Modify: `mascota.cs` — `DrawAcc` (`:1424-1456`) y zona de accesorios.
- Test: `tests/MascotaTests.cs`

**Interfaces:**
- Produces (`internal static` en `PetForm`):
  - `int[][] HatShape(Acc a)` → filas `{x0, x1, fila, color}` con color `0=bWhite 1=bRed 2=bPurple 3=bGold 4=bPink 5=bBlue`, copiando **exactamente** los `Row(...)` actuales de Navidad, Bruja y Fiesta (`:1427-1447`); `null` para `Ninguno` y `Gafas`.
  - `Acc SpriteAcc(Acc a)` → `Gafas` ⇒ `Ninguno`; el resto igual.
  - `Rectangle HeadBox(Bitmap b)` → `top` = primera fila con algún píxel de alfa ≥ 128; franja = filas `top .. top + max(2, b.Height/6) - 1`; `X`/`Width` = mínimo y máximo `x` opaco dentro de la franja; `Rectangle.Empty` si el bitmap es todo transparente.
  - `int HatCell(int headWidthPx, int px)` → `Math.Max(Math.Max(2, px / 2), Math.Min(px, (int)Math.Round(headWidthPx / 10.0)))`.
  - `int HatCol(int col, bool flip)` → `flip ? 13 - col : col`.
  - `bool HatFits(Bitmap b, Rectangle head)` → `false` si `head` está vacío; `false` si en la fila central de la franja (`head.Y + head.Height/2`) hay 2 o más tramos opacos separados por huecos de más de 1 px (dos orejas/cuernos); `false` si `head.Width < 0.35 * b.Width` (oreja/cuerno único); si no, `true`.
- `DrawAcc` pasa a recorrer `HatShape(accNow)` para los gorros (resultado idéntico en Claudio); el caso `Gafas` se queda como está.

- [ ] **Step 1: Tests que fallan:** `HatShape_Navidad` (6 filas, primera `{2,11,-1,0}`), `HatShape_Gafas` (`null`), `SpriteAcc_Gafas` (`Ninguno`), `SpriteAcc_Bruja` (`Bruja`), `HeadBox_Vacio` (bitmap 10×10 transparente ⇒ `Empty`), `HeadBox_Oreja` (bitmap 60×60 con un bloque opaco 20..39 × 30..59 y una "oreja" 25..27 × 10..29 ⇒ `Y == 10`, franja de 10 filas, `X == 25`, `Width == 3`), `HeadBox_Cabeza` (bloque 10..49 × 5..59 ⇒ `X == 10`, `Width == 40`), `HatCell_Clamp` (`HatCell(3,6) == 3`, `HatCell(60,6) == 6`, `HatCell(1000,6) == 6`), `HatCol_Flip` (`HatCol(10,true) == 3`), `HatFits_DosOrejas` (60×60: cuerpo 10..49 × 30..59, orejas 12..17 y 42..47 × 10..29 ⇒ false), `HatFits_UnCuerno` (60×60: cuerpo 10..49 × 30..59, cuerno 28..31 × 5..29 ⇒ false), `HatFits_Redonda` (bloque 10..49 × 5..59 ⇒ true).
- [ ] **Step 2:** `tests\run-tests.bat` → FALLO de compilación.
- [ ] **Step 3:** Implementar las funciones y refactorizar `DrawAcc`. Para `HeadBox` usar `LockBits` o `GetPixel` (el sprite es pequeño, ≤ ~224 px; `GetPixel` vale porque solo se llama en `BuildSprites`).
- [ ] **Step 4:** `tests\run-tests.bat` → todos `OK`.
- [ ] **Step 5:** Verificación manual con Claudio: los cuatro accesorios se ven píxel a píxel igual que antes.
- [ ] **Step 6:** Commit `refactor: formas de gorros como datos y geometria de cabeza para sprites`.

### Task 3: Dibujar sombreros sobre el Pokémon y activar el menú

**Files:**
- Modify: `mascota.cs` — campos junto a `spr, sprFlip` (`:470`), `BuildSprites` (`:1195-1212`), rama sprite de `OnPaint` (`:1555-1567`), `ApplySkin` (`:953-955`), `UpdateAccChecks`/menú de accesorios (`:674-684`).

**Interfaces:**
- Consumes: `HatShape`, `SpriteAcc`, `HeadBox`, `HatCell`, `HatCol` (Task 2); `scX`, `scY`, `anX`, `anY` y el rectángulo de dibujo del sprite que ya calcula `OnPaint`.
- Produces: campo `Rectangle sprHead` (cabeza en coordenadas de `spr`, calculada en `BuildSprites` tras crear `spr`); `void DrawHat(Graphics g, Rectangle dst, bool flip)` donde `dst` es el rectángulo donde se dibujó el sprite.

- [ ] **Step 1:** En `BuildSprites`, tras generar `spr`, `sprHead = HeadBox(spr)` y, si `!HatFits(spr, sprHead)`, `sprHead = Rectangle.Empty` (sin gorro).
- [ ] **Step 2:** Implementar `DrawHat`. Algoritmo (no lo fija la firma):
  ```
  shape = HatShape(SpriteAcc(accNow)); si null o sprHead vacío → return
  cell = HatCell(sprHead.Width, PX); cw = cell*scX; ch = cell*scY
  hx = flip ? spr.Width - sprHead.Right : sprHead.X        // espejo de la caja
  cx = dst.X + (hx + sprHead.Width/2.0) * scX               // centro de la cabeza en pantalla
  topY = dst.Y + sprHead.Y * scY + ch                       // el ala "se encaja" una celda
  por cada fila {x0,x1,r,c}: a = HatCol(x0,flip), b = HatCol(x1,flip), (a,b) = (min,max)
      FillRectangle(brush[c], round(cx + (a-7)*cw), round(topY + r*ch), round((b-a+1)*cw), round(ch))
  ```
  Llamarlo en `OnPaint` justo después del `DrawImage` del sprite y antes de `DrawDecor`, pasando el mismo `Rectangle` del `DrawImage`.
- [ ] **Step 3:** Menú: en `ApplySkin`, `miAcc.Visible = skin != "og"` y `miAccItems[5].Visible = !useSprite` (índice de "Gafas de sol" en `AccNames`). Actualizar los comentarios de `:615`, `:954`, `:957` y `:1214`/`:1556` que aún dicen "Clawitzer" o "solo Claudio".
- [ ] **Step 4:** `tests\run-tests.bat` → todos `OK`, y compilar con `mascota.vbs`.
- [ ] **Step 5: Verificación manual** con al menos Clawitzer, Pikachu (25) y un Pokémon alto (p. ej. Onix, 95): los tres gorros quedan centrados sobre lo alto del sprite, giran con él al andar por pared/techo, se espejan al darse la vuelta, siguen el saltito y el squash; "Gafas" no aparece en el menú; con "Automático" en julio no se dibuja nada; cambiar de Pokémon y de tamaño con el gorro puesto lo recoloca; al volver a Claudio todo igual que antes.
- [ ] **Step 6:** Commit `feat: gorros y sombreros para los Pokemon`.
