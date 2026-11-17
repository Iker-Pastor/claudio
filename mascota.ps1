# Mascota virtual de escritorio
# - Pasea por el suelo, trepa por los bordes y anda por el techo, cruzando pantallas
# - Cogela con el raton: cuelga del punto por donde la agarras y se balancea/gira
#   si mueves el raton en circulos
# - Sueltala en movimiento para lanzarla: vuela, gira y rebota en los bordes
# - Click: da un saltito y trae Claude al frente (lo abre si no esta abierto)
# - Rueda del raton encima: crece / encoge
# - Click derecho: menu (Abrir Claude / Quieta-Pasear / Salir)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@

[System.Windows.Forms.Application]::EnableVisualStyles()

# ---------- Sprite (cada caracter = 1 pixel) ----------
$PX = 6   # tamano de cada "pixel" en pantalla
$body = @(
    "..XXXXXXXXXX..",
    "..XXXXXXXXXX..",
    "..XXEXXXXEXX..",
    "XXXXEXXXXEXXXX",
    "XXXXXXXXXXXXXX",
    "..XXXXXXXXXX..",
    "..XXXXXXXXXX.."
)
$legsA = "..X.X....X.X.."
$legsB = ".X.X......X.X."
$W = $body[0].Length * $PX   # caja logica de la mascota (cuadrada)
$H = $W
$M = 20                      # margen para que quepa al girar
$SZ = $W + 2 * $M            # tamano real de la ventana

$orange = [System.Drawing.Color]::FromArgb(217, 119, 87)
$black  = [System.Drawing.Color]::FromArgb(20, 20, 20)
$key    = [System.Drawing.Color]::Magenta

# ---------- Fisica ----------
$GRAV = 1.2        # gravedad (px / tick^2)
$MAXV = 70         # velocidad maxima al lanzar
$BOUNCE = 0.55     # cuanto rebota (0..1) con el tamano normal; pequena rebota mas, grande menos

# ---------- Ventana ----------
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.BackColor = $key
$form.TransparencyKey = $key
$form.Width = $SZ
$form.Height = $SZ
$form.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($form, $true, $null)

$area0 = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Location = New-Object System.Drawing.Point(([int]($area0.Left + $area0.Width / 2) - $M), ($area0.Bottom - $H - $M))

# ---------- Estado ----------
$Pet = @{
    dir = 1; speed = 2; walking = $true; paused = $false
    frame = 0; tick = 0; blink = 0
    vy = 0.0; hop = 0.0; hopV = 0.0
    dragging = $false; down = $false; moved = $false
    downX = 0; downY = 0
    nextDecision = 80
    mode = 'floor'; vdir = -1; stepTop = $null; stuck = $false
    # fisica libre (agarrada o en el aire)
    phi = 0.0; phiV = 0.0          # angulo (rad) y velocidad angular
    gx = 0.0; gy = 0.0             # punto de agarre relativo al centro (sin rotar)
    cx = 0.0; cy = 0.0             # centro de la mascota en pantalla (float)
    vx = 0.0; vyA = 0.0            # velocidad en el aire
    lastCX = 0; lastCY = 0; mvx = 0.0; mvy = 0.0; tvx = 0.0; tvy = 0.0
}
$rand = New-Object System.Random

# ---------- Traer Claude al frente ----------
function Focus-Claude {
    $p = Get-Process -Name claude -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    if ($p) {
        $h = $p.MainWindowHandle
        if ([Win]::IsIconic($h)) { [Win]::ShowWindow($h, 9) | Out-Null }   # SW_RESTORE
        else { [Win]::ShowWindow($h, 5) | Out-Null }                       # SW_SHOW
        # truco ALT para que Windows permita cambiar el foco
        [Win]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [Win]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [Win]::BringWindowToTop($h) | Out-Null
        [Win]::SetForegroundWindow($h) | Out-Null
    } else {
        $app = Get-StartApps -Name 'Claude' -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -eq 'Claude' } | Select-Object -First 1
        if ($app) { Start-Process "shell:AppsFolder\$($app.AppID)" }
    }
}

# Angulo (grados) con el que se dibuja segun el modo
function Get-ModeAngle {
    switch ($Pet.mode) { 'wallR' { return 270 } 'wallL' { return 90 } 'ceiling' { return 180 } }
    return 0
}

# Coloca la ventana para que el centro de la mascota este en (cx, cy)
function Set-Center([double]$cx, [double]$cy) {
    $form.Location = New-Object System.Drawing.Point(([int]$cx - [int]($SZ / 2)), ([int]$cy - [int]($SZ / 2)))
}

# ---------- Dibujo ----------
$form.Add_Paint({
    param($sender, $e)
    $g = $e.Graphics
    $bOrange = New-Object System.Drawing.SolidBrush $orange
    $bBlack  = New-Object System.Drawing.SolidBrush $black
    $free = $Pet.dragging -or $Pet.mode -eq 'air'
    if ($free) { $angle = $Pet.phi * 180.0 / [Math]::PI } else { $angle = Get-ModeAngle }
    if ($angle -ne 0) {
        $g.TranslateTransform($SZ / 2, $SZ / 2); $g.RotateTransform([single]$angle); $g.TranslateTransform(-$SZ / 2, -$SZ / 2)
    }
    if ($free) { $hop = 0 } else { $hop = [int]$Pet.hop }
    $offY = $M + $H - ($body.Count + 2) * $PX - $hop
    $offX = $M
    $rows = @($body)
    if ($free) { if (($Pet.tick -band 2) -eq 0) { $legs = $legsA } else { $legs = $legsB } }   # patalea
    elseif (-not $Pet.walking -or $Pet.paused) { $legs = $legsA }
    elseif ($Pet.frame -eq 0) { $legs = $legsA } else { $legs = $legsB }
    $rows += $legs
    $rows += $legs
    for ($y = 0; $y -lt $rows.Count; $y++) {
        $row = $rows[$y]
        for ($x = 0; $x -lt $row.Length; $x++) {
            $c = $row[$x]
            if ($c -eq '.') { continue }
            $brush = $bOrange
            if ($c -eq 'E' -and $Pet.blink -le 0) { $brush = $bBlack }
            $g.FillRectangle($brush, $offX + $x * $PX, $offY + $y * $PX, $PX, $PX)
        }
    }
    $bOrange.Dispose(); $bBlack.Dispose()
})

# ---------- Raton: agarrar / girar / lanzar / click ----------
$form.Add_MouseDown({
    param($sender, $e)
    if ($e.Button -ne 'Left') { return }
    $Pet.down = $true; $Pet.moved = $false
    $p = [System.Windows.Forms.Cursor]::Position
    $Pet.downX = $p.X; $Pet.downY = $p.Y
})
$form.Add_MouseMove({
    param($sender, $e)
    if (-not $Pet.down) { return }
    $p = [System.Windows.Forms.Cursor]::Position
    if (-not $Pet.moved -and ([Math]::Abs($p.X - $Pet.downX) + [Math]::Abs($p.Y - $Pet.downY)) -gt 4) {
        $Pet.moved = $true; $Pet.dragging = $true
        # angulo actual y centro actual
        if ($Pet.mode -ne 'air') { $Pet.phi = (Get-ModeAngle) * [Math]::PI / 180.0; $Pet.phiV = 0.0 }
        $cx = $form.Left + $SZ / 2; $cy = $form.Top + $SZ / 2
        # punto de agarre en coordenadas de la mascota (deshaciendo la rotacion)
        $dx = $Pet.downX - $cx; $dy = $Pet.downY - $cy
        $co = [Math]::Cos(-$Pet.phi); $si = [Math]::Sin(-$Pet.phi)
        $Pet.gx = $dx * $co - $dy * $si; $Pet.gy = $dx * $si + $dy * $co
        # si la coges por el centro, que cuelgue un poco igualmente
        if ([Math]::Sqrt($Pet.gx * $Pet.gx + $Pet.gy * $Pet.gy) -lt 14) { $Pet.gx = 0.0; $Pet.gy = -14.0 }
        $Pet.lastCX = $p.X; $Pet.lastCY = $p.Y; $Pet.mvx = 0.0; $Pet.mvy = 0.0; $Pet.tvx = 0.0; $Pet.tvy = 0.0
        $Pet.mode = 'air'
    }
})
# ---------- Rueda del raton: crecer / encoger ----------
$MINPX = 2; $MAXPX = 16
function Set-Scale([int]$newPX) {
    $newPX = [Math]::Max($MINPX, [Math]::Min($MAXPX, $newPX))
    if ($newPX -eq $script:PX) { return }
    $ratio = $newPX / $script:PX
    $cx = $form.Left + $script:SZ / 2; $cy = $form.Top + $script:SZ / 2
    $script:PX = $newPX
    $script:W = $body[0].Length * $newPX
    $script:H = $script:W
    $script:M = [int][Math]::Ceiling($script:W * 0.21) + 2
    $script:SZ = $script:W + 2 * $script:M
    $Pet.gx *= $ratio; $Pet.gy *= $ratio          # el punto de agarre escala con ella
    $newSize = New-Object System.Drawing.Size($script:SZ, $script:SZ)
    $form.MinimumSize = $newSize; $form.Size = $newSize
    if ($Pet.mode -eq 'floor' -and -not $Pet.dragging) {
        # mantener los pies en el mismo sitio
        $bottom = $cy + ($script:H / $ratio) / 2
        $cy = $bottom - $script:H / 2
    }
    Set-Center $cx $cy
    $Pet.cx = $cx; $Pet.cy = $cy
    $form.Invalidate()
}
$form.Add_MouseWheel({
    param($sender, $e)
    if ($e.Delta -gt 0) { Set-Scale ($PX + 1) } else { Set-Scale ($PX - 1) }
})

$form.Add_MouseUp({
    param($sender, $e)
    if ($e.Button -ne 'Left') { return }
    $wasDrag = $Pet.dragging
    $Pet.down = $false; $Pet.dragging = $false
    if ($wasDrag) {
        # lanzar con la velocidad que llevaba el raton
        $Pet.mode = 'air'; $Pet.stepTop = $null
        $Pet.vx = [Math]::Max(-$MAXV, [Math]::Min($MAXV, $Pet.tvx))
        $Pet.vyA = [Math]::Max(-$MAXV, [Math]::Min($MAXV, $Pet.tvy))
        $Pet.cx = $form.Left + $SZ / 2; $Pet.cy = $form.Top + $SZ / 2
        # si la sueltas despacio cerca de una pared o del techo, se queda pegada
        if ([Math]::Abs($Pet.vx) + [Math]::Abs($Pet.vyA) -lt 10) { [void](Try-Stick ($W * 0.6)) }
    } else {
        $Pet.hopV = 7.0   # saltito de alegria
        Focus-Claude
    }
})

# ---------- Menu click derecho ----------
$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miOpen = $menu.Items.Add('Abrir Claude')
$miOpen.Add_Click({ Focus-Claude })
$miStay = $menu.Items.Add('Quieta')
$miStay.Add_Click({
    $Pet.paused = -not $Pet.paused
    if ($Pet.paused) { $miStay.Text = 'Pasear' } else { $miStay.Text = 'Quieta' }
})
[void]$menu.Items.Add('-')
$miExit = $menu.Items.Add('Salir')
$miExit.Add_Click({ $timer.Stop(); $form.Close() })
$form.ContextMenuStrip = $menu

# ---------- Pantallas ----------
# Area de trabajo de la pantalla que hay en la columna $px (la mas cercana en vertical a $py), o $null
function Get-AreaAt([int]$px, [int]$py) {
    $best = $null; $bestD = [int]::MaxValue
    foreach ($scr in [System.Windows.Forms.Screen]::AllScreens) {
        $a = $scr.WorkingArea
        if ($px -ge $a.Left -and $px -lt $a.Right) {
            if ($py -lt $a.Top) { $d = $a.Top - $py }
            elseif ($py -gt $a.Bottom) { $d = $py - $a.Bottom }
            else { $d = 0 }
            if ($d -lt $bestD) { $bestD = $d; $best = $a }
        }
    }
    return $best
}
function Get-NearestArea([int]$px, [int]$py) {
    $a = Get-AreaAt $px $py
    if ($null -ne $a) { return $a }
    return [System.Windows.Forms.Screen]::FromPoint((New-Object System.Drawing.Point($px, $py))).WorkingArea
}

function Test-InScreen([int]$px, [int]$py) {
    foreach ($scr in [System.Windows.Forms.Screen]::AllScreens) {
        if ($scr.WorkingArea.Contains($px, $py)) { return $true }
    }
    return $false
}

function Next-Frame { if ($Pet.tick % 6 -eq 0) { $Pet.frame = 1 - $Pet.frame } }

# ---------- Fisica: agarrada (pendulo) ----------
function Step-Drag {
    $c = [System.Windows.Forms.Cursor]::Position
    # velocidad y aceleracion del raton
    $vx = $c.X - $Pet.lastCX; $vy = $c.Y - $Pet.lastCY
    $ax = $vx - $Pet.mvx; $ay = $vy - $Pet.mvy
    $Pet.mvx = $vx; $Pet.mvy = $vy; $Pet.lastCX = $c.X; $Pet.lastCY = $c.Y
    $Pet.tvx = 0.5 * $Pet.tvx + 0.5 * $vx; $Pet.tvy = 0.5 * $Pet.tvy + 0.5 * $vy
    $ax = [Math]::Max(-40, [Math]::Min(40, $ax)); $ay = [Math]::Max(-40, [Math]::Min(40, $ay))

    # vector del punto de agarre al centro de masas (rotado)
    $co = [Math]::Cos($Pet.phi); $si = [Math]::Sin($Pet.phi)
    $rx = -($Pet.gx * $co - $Pet.gy * $si); $ry = -($Pet.gx * $si + $Pet.gy * $co)
    # par = r x F, con F = gravedad - aceleracion del punto de agarre
    $acc = ($rx * ($GRAV - $ay) + $ry * $ax) / ($rx * $rx + $ry * $ry + 300.0)
    $Pet.phiV = ($Pet.phiV + $acc) * 0.985
    $Pet.phiV = [Math]::Max(-0.8, [Math]::Min(0.8, $Pet.phiV))
    $Pet.phi += $Pet.phiV

    # recolocar: el punto de agarre se queda bajo el cursor
    $co = [Math]::Cos($Pet.phi); $si = [Math]::Sin($Pet.phi)
    $Pet.cx = $c.X - ($Pet.gx * $co - $Pet.gy * $si)
    $Pet.cy = $c.Y - ($Pet.gx * $si + $Pet.gy * $co)
    Set-Center $Pet.cx $Pet.cy
}

# ---------- Fisica: en el aire ----------
# Si esta a menos de $thresh px de una pared exterior o del techo, se pega ahi.
function Try-Stick([double]$thresh) {
    $half = $W / 2
    $a = Get-NearestArea ([int]$Pet.cx) ([int]$Pet.cy)
    $best = $null; $bestD = $thresh
    $dR = $a.Right - ($Pet.cx + $half)
    if ($dR -lt $bestD -and -not (Test-InScreen ($a.Right + 1) ([int]$Pet.cy))) { $best = 'wallR'; $bestD = $dR }
    $dL = ($Pet.cx - $half) - $a.Left
    if ($dL -lt $bestD -and -not (Test-InScreen ($a.Left - 1) ([int]$Pet.cy))) { $best = 'wallL'; $bestD = $dL }
    $dT = ($Pet.cy - $half) - $a.Top
    if ($dT -lt $bestD) { $best = 'ceiling'; $bestD = $dT }
    if ($null -eq $best) { return $false }

    switch ($best) {
        'wallR'   { $Pet.cx = $a.Right - $half }
        'wallL'   { $Pet.cx = $a.Left + $half }
        'ceiling' { $Pet.cy = $a.Top + $half }
    }
    # que no se salga por arriba ni por abajo
    $Pet.cy = [Math]::Max($a.Top + $half, [Math]::Min($a.Bottom - $half, $Pet.cy))
    $Pet.mode = $best; $Pet.stuck = $true; $Pet.stepTop = $null
    $Pet.phi = 0.0; $Pet.phiV = 0.0; $Pet.vx = 0.0; $Pet.vyA = 0.0
    $Pet.walking = $false; $Pet.nextDecision = 120 + $rand.Next(120)   # se queda quieta un rato
    if ($rand.Next(2) -eq 0) { $Pet.vdir = -1 } else { $Pet.vdir = 1 }
    if ($rand.Next(2) -eq 0) { $Pet.dir = -1 } else { $Pet.dir = 1 }
    Set-Center $Pet.cx $Pet.cy
    return $true
}

function Step-Air {
    # rebote y rozamiento segun el tamano: pequena = saltarina, grande = pesada
    $sizeK = [Math]::Sqrt(6.0 / $PX)
    $bnc = [Math]::Max(0.15, [Math]::Min(0.92, $BOUNCE * $sizeK))
    $fric = [Math]::Max(0.6, [Math]::Min(0.95, 1.0 - 0.2 / $sizeK))
    $Pet.vyA += $GRAV
    $Pet.vx *= 0.995
    $Pet.cx += $Pet.vx; $Pet.cy += $Pet.vyA
    $Pet.phi += $Pet.phiV; $Pet.phiV *= 0.99
    $half = $W / 2
    $a = Get-NearestArea ([int]$Pet.cx) ([int]$Pet.cy)
    if ([Math]::Abs($Pet.vx) + [Math]::Abs($Pet.vyA) -lt 5 -and (Try-Stick 2)) { return }

    # paredes: rebota solo si al otro lado no hay otra pantalla
    if ($Pet.cx - $half -lt $a.Left -and -not (Test-InScreen ($a.Left - 1) ([int]($Pet.cy + $half - 4)))) {
        $Pet.cx = $a.Left + $half; $Pet.vx = [Math]::Abs($Pet.vx) * $bnc; $Pet.phiV += $Pet.vyA * 0.01
    }
    if ($Pet.cx + $half -gt $a.Right -and -not (Test-InScreen ($a.Right + 1) ([int]($Pet.cy + $half - 4)))) {
        $Pet.cx = $a.Right - $half; $Pet.vx = -[Math]::Abs($Pet.vx) * $bnc; $Pet.phiV -= $Pet.vyA * 0.01
    }
    # techo
    if ($Pet.cy - $half -lt $a.Top) {
        $Pet.cy = $a.Top + $half; $Pet.vyA = [Math]::Abs($Pet.vyA) * $bnc
    }
    # suelo
    if ($Pet.cy + $half -ge $a.Bottom) {
        $Pet.cy = $a.Bottom - $half
        if ([Math]::Abs($Pet.vyA) -lt 4 -and [Math]::Abs($Pet.vx) -lt 3) {
            # aterriza: se pone de pie y sigue paseando
            $Pet.mode = 'floor'; $Pet.phi = 0.0; $Pet.phiV = 0.0; $Pet.vy = 0.0; $Pet.hopV = 5.0
            if ($Pet.vx -gt 0) { $Pet.dir = 1 } elseif ($Pet.vx -lt 0) { $Pet.dir = -1 }
        } else {
            $Pet.phiV += $Pet.vx * 0.008      # al rodar por el suelo gira
            $Pet.vyA = -[Math]::Abs($Pet.vyA) * $bnc
            $Pet.vx *= $fric
            if ([Math]::Abs($Pet.vyA) -lt 2) { $Pet.vyA = 0 }
        }
    }
    Set-Center $Pet.cx $Pet.cy
}

# ---------- Bucle de animacion ----------
# Modos: floor (suelo), wallL / wallR (trepando), ceiling (techo), air (agarrada o volando)
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 30
$timer.Add_Tick({
    $Pet.tick++

    # parpadeo
    if ($Pet.blink -gt 0) { $Pet.blink-- } elseif ($rand.Next(150) -eq 0) { $Pet.blink = 5 }

    # saltito
    if ($Pet.hopV -ne 0 -or $Pet.hop -gt 0) {
        $Pet.hop += $Pet.hopV; $Pet.hopV -= 1.0
        if ($Pet.hop -le 0) { $Pet.hop = 0; $Pet.hopV = 0 }
        if ($Pet.hop -gt 10) { $Pet.hop = 10 }
    }

    if ($Pet.dragging) { Step-Drag; $form.Invalidate(); return }
    if ($Pet.mode -eq 'air') { Step-Air; $form.Invalidate(); return }

    # posicion de la caja logica (sin margen)
    $x = $form.Left + $M; $y = $form.Top + $M
    $cx = $x + [int]($W / 2); $midY = $y + [int]($H / 2)
    $area = Get-NearestArea $cx $midY
    $floor = $area.Bottom - $H

    # decisiones aleatorias
    $Pet.nextDecision--
    if ($Pet.nextDecision -le 0) {
        $r = $rand.Next(10)
        if ($Pet.mode -eq 'floor') {
            if ($r -lt 3) { $Pet.walking = $false }
            else { $Pet.walking = $true; if ($r -lt 5) { $Pet.dir = -$Pet.dir } }
        } else {
            $Pet.walking = $true
            if ($r -lt 2 -and -not $Pet.stuck) { $Pet.mode = 'floor'; $Pet.vy = 0.0 }          # se suelta y cae
            elseif ($r -lt 4) {
                if ($Pet.mode -eq 'ceiling') { $Pet.dir = -$Pet.dir } else { $Pet.vdir = -$Pet.vdir }
            }
        }
        $Pet.speed = 1 + $rand.Next(3)
        $Pet.nextDecision = 60 + $rand.Next(150)
    }
    $moving = $Pet.walking -and -not $Pet.paused

    if ($Pet.mode -eq 'floor') {
        $Pet.stepTop = $null; $Pet.stuck = $false
        if ($y -lt $floor) {
            # cayendo con gravedad
            $Pet.vy += $GRAV
            $y = [Math]::Min($floor, $y + [int]$Pet.vy)
            if ($y -ge $floor) { $Pet.vy = 0; $Pet.hopV = 4.0 }
        } else {
            if ($y -gt $floor) { $Pet.hopV = 6.0 }
            $y = $floor
            if ($moving) {
                $newX = $x + $Pet.dir * $Pet.speed
                if ($Pet.dir -gt 0) { $lead = $newX + $W - 1 } else { $lead = $newX }
                $la = Get-AreaAt $lead $midY
                if ($null -eq $la) {
                    # borde exterior: trepar la pared (o a veces darse la vuelta)
                    if ($rand.Next(10) -lt 9) {
                        if ($Pet.dir -gt 0) { $Pet.mode = 'wallR'; $x = $area.Right - $W }
                        else { $Pet.mode = 'wallL'; $x = $area.Left }
                        $Pet.vdir = -1
                    } else { $Pet.dir = -$Pet.dir }
                } elseif (($la.Bottom - $H) -lt ($floor - 12)) {
                    # la pantalla de al lado tiene el suelo mas alto: trepar el escalon
                    if ($Pet.dir -gt 0) { $Pet.mode = 'wallR'; $x = $area.Right - $W }
                    else { $Pet.mode = 'wallL'; $x = $area.Left }
                    $Pet.vdir = -1
                    $Pet.stepTop = $la.Bottom - $H
                } else { $x = $newX }
                Next-Frame
            }
        }
    }
    elseif ($Pet.mode -eq 'wallL' -or $Pet.mode -eq 'wallR') {
        if ($Pet.mode -eq 'wallR') { $x = $area.Right - $W; $out = 1 } else { $x = $area.Left; $out = -1 }
        if ($moving) {
            $y += $Pet.vdir * $Pet.speed
            if ($Pet.vdir -lt 0) {
                if ($null -ne $Pet.stepTop -and $y -le $Pet.stepTop) {
                    # arriba del escalon: pasar a la otra pantalla
                    $y = $Pet.stepTop; $x += $out * ([int]($W / 2) + 6)
                    $Pet.mode = 'floor'; $Pet.dir = $out; $Pet.hopV = 5.0
                } elseif ($y -le $area.Top) {
                    $y = $area.Top; $Pet.mode = 'ceiling'; $Pet.dir = -$out
                }
            } else {
                if ($y -ge $floor) { $y = $floor; $Pet.mode = 'floor'; $Pet.dir = -$out }
            }
            Next-Frame
        }
    }
    elseif ($Pet.mode -eq 'ceiling') {
        $ca = Get-AreaAt $cx $y
        if ($null -eq $ca) { $ca = $area }
        $y = $ca.Top
        if ($moving) {
            $newX = $x + $Pet.dir * $Pet.speed
            if ($Pet.dir -gt 0) { $lead = $newX + $W - 1 } else { $lead = $newX }
            if ($null -eq (Get-AreaAt $lead $y)) {
                # esquina: bajar por la pared
                if ($Pet.dir -gt 0) { $Pet.mode = 'wallR'; $x = $ca.Right - $W }
                else { $Pet.mode = 'wallL'; $x = $ca.Left }
                $Pet.vdir = 1; $Pet.stepTop = $null
            } else { $x = $newX }
            Next-Frame
        }
    }

    if (($x - $M) -ne $form.Left -or ($y - $M) -ne $form.Top) {
        $form.Location = New-Object System.Drawing.Point(($x - $M), ($y - $M))
    }
    $form.Invalidate()
})

$form.Add_Load({ $form.MinimumSize = New-Object System.Drawing.Size($SZ, $SZ); $form.Size = New-Object System.Drawing.Size($SZ, $SZ) })
$form.Add_Shown({ $form.Size = New-Object System.Drawing.Size($SZ, $SZ); $timer.Start() })
[System.Windows.Forms.Application]::Run($form)
