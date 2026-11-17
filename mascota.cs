// Mascota virtual de escritorio
// - Pasea por el suelo, trepa por los bordes y anda por el techo, cruzando pantallas
// - Cogela con el raton: cuelga del punto por donde la agarras y se balancea/gira
//   si mueves el raton en circulos
// - Sueltala en movimiento para lanzarla: vuela, gira y rebota en los bordes
// - Click: da un saltito y trae Claude al frente (lo abre si no esta abierto)
// - Rueda del raton encima: crece / encoge
// - Click derecho: menu (Abrir Claude / Quieta-Pasear / Salir)
//
// Se compila con el csc.exe que trae Windows (.NET Framework 4, C# 5): mascota.vbs lo hace solo.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Win {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}

enum Mode { Floor, WallL, WallR, Ceiling, Air }

class PetForm : Form {
    // ---------- Sprite (cada caracter = 1 pixel) ----------
    static readonly string[] Body = {
        "..XXXXXXXXXX..",
        "..XXXXXXXXXX..",
        "..XXEXXXXEXX..",
        "XXXXEXXXXEXXXX",
        "XXXXXXXXXXXXXX",
        "..XXXXXXXXXX..",
        "..XXXXXXXXXX.."
    };
    const string LegsA = "..X.X....X.X..";
    const string LegsB = ".X.X......X.X.";

    int PX = 6;      // tamano de cada "pixel" en pantalla
    int W, H;        // caja logica de la mascota (cuadrada)
    int M = 20;      // margen para que quepa al girar
    int SZ;          // tamano real de la ventana

    static readonly Color Orange = Color.FromArgb(217, 119, 87);
    static readonly Color Black  = Color.FromArgb(20, 20, 20);
    static readonly Color Key    = Color.Magenta;

    // ---------- Fisica ----------
    const double GRAV = 1.2;     // gravedad (px / tick^2)
    const double MAXV = 70;      // velocidad maxima al lanzar
    const double BOUNCE = 0.55;  // cuanto rebota (0..1) con el tamano normal
    const int MINPX = 2, MAXPX = 16;

    // ---------- Estado ----------
    int dir = 1, speed = 2; bool walking = true, paused = false;
    int frame = 0, tick = 0, blink = 0;
    double vy = 0, hop = 0, hopV = 0;
    bool dragging, down, moved;
    int downX, downY;
    int nextDecision = 80;
    Mode mode = Mode.Floor; int vdir = -1; int? stepTop = null; bool stuck = false;
    // fisica libre (agarrada o en el aire)
    double phi, phiV;      // angulo (rad) y velocidad angular
    double gx, gy;         // punto de agarre relativo al centro (sin rotar)
    double cx, cy;         // centro de la mascota en pantalla
    double vx, vyA;        // velocidad en el aire
    int lastCX, lastCY; double mvx, mvy, tvx, tvy;

    readonly Random rand = new Random();
    readonly Timer timer = new Timer();
    readonly SolidBrush bOrange = new SolidBrush(Orange);
    readonly SolidBrush bBlack = new SolidBrush(Black);
    readonly ToolStripMenuItem miStay;

    // Caché de las areas de trabajo (se refresca al cambiar las pantallas y cada pocos segundos)
    Rectangle[] areas;
    // Lo ultimo que se dibujo: solo se repinta si cambia
    float lastAngle = float.NaN; int lastHop = -1; string lastLegs = null; bool lastEyes; int lastPX = -1;

    public PetForm() {
        W = Body[0].Length * PX; H = W; SZ = W + 2 * M;

        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Key;
        TransparencyKey = Key;
        Width = SZ; Height = SZ;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        RefreshAreas();
        Rectangle a0 = Screen.PrimaryScreen.WorkingArea;
        Location = new Point((int)Math.Round(a0.Left + a0.Width / 2.0) - M, a0.Bottom - H - M);

        // ---------- Menu click derecho ----------
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Abrir Claude", null, delegate { FocusClaude(); });
        miStay = new ToolStripMenuItem("Quieta");
        miStay.Click += delegate {
            paused = !paused;
            miStay.Text = paused ? "Pasear" : "Quieta";
        };
        menu.Items.Add(miStay);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, delegate { timer.Stop(); Close(); });
        ContextMenuStrip = menu;

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        timer.Interval = 30;
        timer.Tick += OnTick;
        Load += delegate { MinimumSize = new Size(SZ, SZ); Size = new Size(SZ, SZ); };
        Shown += delegate { Size = new Size(SZ, SZ); timer.Start(); };
    }

    void OnDisplayChanged(object s, EventArgs e) { RefreshAreas(); }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            timer.Dispose(); bOrange.Dispose(); bBlack.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- Traer Claude al frente ----------
    void FocusClaude() {
        IntPtr h = IntPtr.Zero;
        foreach (Process p in Process.GetProcessesByName("claude")) {
            try { if (h == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero) h = p.MainWindowHandle; }
            catch { }
            p.Dispose();
        }
        if (h != IntPtr.Zero) {
            if (Win.IsIconic(h)) Win.ShowWindow(h, 9);   // SW_RESTORE
            else Win.ShowWindow(h, 5);                  // SW_SHOW
            // truco ALT para que Windows permita cambiar el foco
            Win.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Win.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Win.BringWindowToTop(h);
            Win.SetForegroundWindow(h);
        } else {
            // No esta abierto: buscarlo en el menu Inicio (solo pasa al hacer click, asi que no cuesta nada en reposo)
            try {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -WindowStyle Hidden -Command \"$a = Get-StartApps -Name 'Claude' | " +
                    "Where-Object { $_.Name -eq 'Claude' } | Select-Object -First 1; " +
                    "if ($a) { Start-Process ('shell:AppsFolder\\' + $a.AppID) }\"");
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                Process.Start(psi).Dispose();
            } catch { }
        }
    }

    // Angulo (grados) con el que se dibuja segun el modo
    float ModeAngle() {
        switch (mode) {
            case Mode.WallR: return 270;
            case Mode.WallL: return 90;
            case Mode.Ceiling: return 180;
        }
        return 0;
    }

    // Coloca la ventana para que el centro de la mascota este en (cx, cy)
    void SetCenter(double x, double y) {
        int half = (int)Math.Round(SZ / 2.0);
        Location = new Point((int)Math.Round(x) - half, (int)Math.Round(y) - half);
    }

    // ---------- Dibujo ----------
    bool Free { get { return dragging || mode == Mode.Air; } }
    float CurAngle() { return Free ? (float)(phi * 180.0 / Math.PI) : ModeAngle(); }
    int CurHop() { return Free ? 0 : (int)Math.Round(hop); }
    string CurLegs() {
        if (Free) return (tick & 2) == 0 ? LegsA : LegsB;   // patalea
        if (!walking || paused) return LegsA;
        return frame == 0 ? LegsA : LegsB;
    }

    // Repinta solo si ha cambiado algo visible (moverse la ventana no necesita repintar)
    void InvalidateIfChanged() {
        float angle = CurAngle(); int hp = CurHop(); string legs = CurLegs(); bool eyes = blink <= 0;
        if (angle != lastAngle || hp != lastHop || !ReferenceEquals(legs, lastLegs) || eyes != lastEyes || PX != lastPX)
            Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        Graphics g = e.Graphics;
        float angle = CurAngle(); int hp = CurHop(); string legs = CurLegs(); bool eyes = blink <= 0;
        lastAngle = angle; lastHop = hp; lastLegs = legs; lastEyes = eyes; lastPX = PX;

        if (angle != 0) {
            g.TranslateTransform(SZ / 2f, SZ / 2f); g.RotateTransform(angle); g.TranslateTransform(-SZ / 2f, -SZ / 2f);
        }
        int offY = M + H - (Body.Length + 2) * PX - hp;
        int offX = M;
        int rows = Body.Length + 2;
        for (int y = 0; y < rows; y++) {
            string row = y < Body.Length ? Body[y] : legs;
            for (int x = 0; x < row.Length; x++) {
                char c = row[x];
                if (c == '.') continue;
                Brush brush = (c == 'E' && eyes) ? bBlack : bOrange;
                g.FillRectangle(brush, offX + x * PX, offY + y * PX, PX, PX);
            }
        }
    }

    // ---------- Raton: agarrar / girar / lanzar / click ----------
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        down = true; moved = false;
        Point p = Cursor.Position;
        downX = p.X; downY = p.Y;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!down) return;
        Point p = Cursor.Position;
        if (!moved && (Math.Abs(p.X - downX) + Math.Abs(p.Y - downY)) > 4) {
            moved = true; dragging = true;
            // angulo actual y centro actual
            if (mode != Mode.Air) { phi = ModeAngle() * Math.PI / 180.0; phiV = 0.0; }
            double ccx = Left + SZ / 2.0, ccy = Top + SZ / 2.0;
            // punto de agarre en coordenadas de la mascota (deshaciendo la rotacion)
            double dx = downX - ccx, dy = downY - ccy;
            double co = Math.Cos(-phi), si = Math.Sin(-phi);
            gx = dx * co - dy * si; gy = dx * si + dy * co;
            // si la coges por el centro, que cuelgue un poco igualmente
            if (Math.Sqrt(gx * gx + gy * gy) < 14) { gx = 0.0; gy = -14.0; }
            lastCX = p.X; lastCY = p.Y; mvx = 0; mvy = 0; tvx = 0; tvy = 0;
            mode = Mode.Air;
        }
    }

    // ---------- Rueda del raton: crecer / encoger ----------
    void SetScale(int newPX) {
        newPX = Math.Max(MINPX, Math.Min(MAXPX, newPX));
        if (newPX == PX) return;
        double ratio = (double)newPX / PX;
        double ccx = Left + SZ / 2.0, ccy = Top + SZ / 2.0;
        PX = newPX;
        W = Body[0].Length * newPX;
        H = W;
        M = (int)Math.Ceiling(W * 0.21) + 2;
        SZ = W + 2 * M;
        gx *= ratio; gy *= ratio;          // el punto de agarre escala con ella
        Size newSize = new Size(SZ, SZ);
        MinimumSize = newSize; Size = newSize;
        if (mode == Mode.Floor && !dragging) {
            // mantener los pies en el mismo sitio
            double bottom = ccy + (H / ratio) / 2;
            ccy = bottom - H / 2.0;
        }
        SetCenter(ccx, ccy);
        cx = ccx; cy = ccy;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        SetScale(e.Delta > 0 ? PX + 1 : PX - 1);
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool wasDrag = dragging;
        down = false; dragging = false;
        if (wasDrag) {
            // lanzar con la velocidad que llevaba el raton
            mode = Mode.Air; stepTop = null;
            vx = Math.Max(-MAXV, Math.Min(MAXV, tvx));
            vyA = Math.Max(-MAXV, Math.Min(MAXV, tvy));
            cx = Left + SZ / 2.0; cy = Top + SZ / 2.0;
            // si la sueltas despacio cerca de una pared o del techo, se queda pegada
            if (Math.Abs(vx) + Math.Abs(vyA) < 10) TryStick(W * 0.6);
        } else {
            hopV = 7.0;   // saltito de alegria
            FocusClaude();
        }
    }

    // ---------- Pantallas ----------
    void RefreshAreas() {
        Screen[] all = Screen.AllScreens;
        Rectangle[] a = new Rectangle[all.Length];
        for (int i = 0; i < all.Length; i++) a[i] = all[i].WorkingArea;
        areas = a;
    }

    // Area de trabajo de la pantalla que hay en la columna px (la mas cercana en vertical a py)
    bool TryAreaAt(int px, int py, out Rectangle best) {
        best = Rectangle.Empty; int bestD = int.MaxValue; bool found = false;
        foreach (Rectangle a in areas) {
            if (px >= a.Left && px < a.Right) {
                int d;
                if (py < a.Top) d = a.Top - py;
                else if (py > a.Bottom) d = py - a.Bottom;
                else d = 0;
                if (d < bestD) { bestD = d; best = a; found = true; }
            }
        }
        return found;
    }

    Rectangle NearestArea(int px, int py) {
        Rectangle a;
        if (TryAreaAt(px, py, out a)) return a;
        return Screen.FromPoint(new Point(px, py)).WorkingArea;
    }

    bool InScreen(int px, int py) {
        foreach (Rectangle a in areas) if (a.Contains(px, py)) return true;
        return false;
    }

    void NextFrame() { if (tick % 6 == 0) frame = 1 - frame; }

    static int R(double v) { return (int)Math.Round(v); }

    // ---------- Fisica: agarrada (pendulo) ----------
    void StepDrag() {
        Point c = Cursor.Position;
        // velocidad y aceleracion del raton
        double mx = c.X - lastCX, my = c.Y - lastCY;
        double ax = mx - mvx, ay = my - mvy;
        mvx = mx; mvy = my; lastCX = c.X; lastCY = c.Y;
        tvx = 0.5 * tvx + 0.5 * mx; tvy = 0.5 * tvy + 0.5 * my;
        ax = Math.Max(-40, Math.Min(40, ax)); ay = Math.Max(-40, Math.Min(40, ay));

        // vector del punto de agarre al centro de masas (rotado)
        double co = Math.Cos(phi), si = Math.Sin(phi);
        double rx = -(gx * co - gy * si), ry = -(gx * si + gy * co);
        // par = r x F, con F = gravedad - aceleracion del punto de agarre
        double acc = (rx * (GRAV - ay) + ry * ax) / (rx * rx + ry * ry + 300.0);
        phiV = (phiV + acc) * 0.985;
        phiV = Math.Max(-0.8, Math.Min(0.8, phiV));
        phi += phiV;

        // recolocar: el punto de agarre se queda bajo el cursor
        co = Math.Cos(phi); si = Math.Sin(phi);
        cx = c.X - (gx * co - gy * si);
        cy = c.Y - (gx * si + gy * co);
        SetCenter(cx, cy);
    }

    // ---------- Fisica: en el aire ----------
    // Si esta a menos de thresh px de una pared exterior o del techo, se pega ahi.
    bool TryStick(double thresh) {
        double half = W / 2.0;
        Rectangle a = NearestArea(R(cx), R(cy));
        Mode? best = null; double bestD = thresh;
        double dR = a.Right - (cx + half);
        if (dR < bestD && !InScreen(a.Right + 1, R(cy))) { best = Mode.WallR; bestD = dR; }
        double dL = (cx - half) - a.Left;
        if (dL < bestD && !InScreen(a.Left - 1, R(cy))) { best = Mode.WallL; bestD = dL; }
        double dT = (cy - half) - a.Top;
        if (dT < bestD) { best = Mode.Ceiling; bestD = dT; }
        if (best == null) return false;

        switch (best.Value) {
            case Mode.WallR: cx = a.Right - half; break;
            case Mode.WallL: cx = a.Left + half; break;
            case Mode.Ceiling: cy = a.Top + half; break;
        }
        // que no se salga por arriba ni por abajo
        cy = Math.Max(a.Top + half, Math.Min(a.Bottom - half, cy));
        mode = best.Value; stuck = true; stepTop = null;
        phi = 0; phiV = 0; vx = 0; vyA = 0;
        walking = false; nextDecision = 120 + rand.Next(120);   // se queda quieta un rato
        vdir = rand.Next(2) == 0 ? -1 : 1;
        dir = rand.Next(2) == 0 ? -1 : 1;
        SetCenter(cx, cy);
        return true;
    }

    void StepAir() {
        // rebote y rozamiento segun el tamano: pequena = saltarina, grande = pesada
        double sizeK = Math.Sqrt(6.0 / PX);
        double bnc = Math.Max(0.15, Math.Min(0.92, BOUNCE * sizeK));
        double fric = Math.Max(0.6, Math.Min(0.95, 1.0 - 0.2 / sizeK));
        vyA += GRAV;
        vx *= 0.995;
        cx += vx; cy += vyA;
        phi += phiV; phiV *= 0.99;
        double half = W / 2.0;
        Rectangle a = NearestArea(R(cx), R(cy));
        if (Math.Abs(vx) + Math.Abs(vyA) < 5 && TryStick(2)) return;

        // paredes: rebota solo si al otro lado no hay otra pantalla
        if (cx - half < a.Left && !InScreen(a.Left - 1, R(cy + half - 4))) {
            cx = a.Left + half; vx = Math.Abs(vx) * bnc; phiV += vyA * 0.01;
        }
        if (cx + half > a.Right && !InScreen(a.Right + 1, R(cy + half - 4))) {
            cx = a.Right - half; vx = -Math.Abs(vx) * bnc; phiV -= vyA * 0.01;
        }
        // techo
        if (cy - half < a.Top) {
            cy = a.Top + half; vyA = Math.Abs(vyA) * bnc;
        }
        // suelo
        if (cy + half >= a.Bottom) {
            cy = a.Bottom - half;
            if (Math.Abs(vyA) < 4 && Math.Abs(vx) < 3) {
                // aterriza: se pone de pie y sigue paseando
                mode = Mode.Floor; phi = 0; phiV = 0; vy = 0; hopV = 5.0;
                if (vx > 0) dir = 1; else if (vx < 0) dir = -1;
            } else {
                phiV += vx * 0.008;      // al rodar por el suelo gira
                vyA = -Math.Abs(vyA) * bnc;
                vx *= fric;
                if (Math.Abs(vyA) < 2) vyA = 0;
            }
        }
        SetCenter(cx, cy);
    }

    // ---------- Bucle de animacion ----------
    void OnTick(object sender, EventArgs e) {
        tick++;
        if (tick % 100 == 0) RefreshAreas();   // por si cambia la barra de tareas

        // parpadeo
        if (blink > 0) blink--; else if (rand.Next(150) == 0) blink = 5;

        // saltito
        if (hopV != 0 || hop > 0) {
            hop += hopV; hopV -= 1.0;
            if (hop <= 0) { hop = 0; hopV = 0; }
            if (hop > 10) hop = 10;
        }

        if (dragging) { StepDrag(); InvalidateIfChanged(); return; }
        if (mode == Mode.Air) { StepAir(); InvalidateIfChanged(); return; }

        // posicion de la caja logica (sin margen)
        int x = Left + M, y = Top + M;
        int ccx = x + W / 2, midY = y + H / 2;
        Rectangle area = NearestArea(ccx, midY);
        int floor = area.Bottom - H;

        // decisiones aleatorias
        nextDecision--;
        if (nextDecision <= 0) {
            int r = rand.Next(10);
            if (mode == Mode.Floor) {
                if (r < 3) walking = false;
                else { walking = true; if (r < 5) dir = -dir; }
            } else {
                walking = true;
                if (r < 2 && !stuck) { mode = Mode.Floor; vy = 0; }          // se suelta y cae
                else if (r < 4) {
                    if (mode == Mode.Ceiling) dir = -dir; else vdir = -vdir;
                }
            }
            speed = 1 + rand.Next(3);
            nextDecision = 60 + rand.Next(150);
        }
        bool moving = walking && !paused;

        if (mode == Mode.Floor) {
            stepTop = null; stuck = false;
            if (y < floor) {
                // cayendo con gravedad
                vy += GRAV;
                y = Math.Min(floor, y + R(vy));
                if (y >= floor) { vy = 0; hopV = 4.0; }
            } else {
                if (y > floor) hopV = 6.0;
                y = floor;
                if (moving) {
                    int newX = x + dir * speed;
                    int lead = dir > 0 ? newX + W - 1 : newX;
                    Rectangle la;
                    if (!TryAreaAt(lead, midY, out la)) {
                        // borde exterior: trepar la pared (o a veces darse la vuelta)
                        if (rand.Next(10) < 9) {
                            if (dir > 0) { mode = Mode.WallR; x = area.Right - W; }
                            else { mode = Mode.WallL; x = area.Left; }
                            vdir = -1;
                        } else dir = -dir;
                    } else if ((la.Bottom - H) < (floor - 12)) {
                        // la pantalla de al lado tiene el suelo mas alto: trepar el escalon
                        if (dir > 0) { mode = Mode.WallR; x = area.Right - W; }
                        else { mode = Mode.WallL; x = area.Left; }
                        vdir = -1;
                        stepTop = la.Bottom - H;
                    } else x = newX;
                    NextFrame();
                }
            }
        } else if (mode == Mode.WallL || mode == Mode.WallR) {
            int outward;
            if (mode == Mode.WallR) { x = area.Right - W; outward = 1; } else { x = area.Left; outward = -1; }
            if (moving) {
                y += vdir * speed;
                if (vdir < 0) {
                    if (stepTop.HasValue && y <= stepTop.Value) {
                        // arriba del escalon: pasar a la otra pantalla
                        y = stepTop.Value; x += outward * (W / 2 + 6);
                        mode = Mode.Floor; dir = outward; hopV = 5.0;
                    } else if (y <= area.Top) {
                        y = area.Top; mode = Mode.Ceiling; dir = -outward;
                    }
                } else {
                    if (y >= floor) { y = floor; mode = Mode.Floor; dir = -outward; }
                }
                NextFrame();
            }
        } else if (mode == Mode.Ceiling) {
            Rectangle ca;
            if (!TryAreaAt(ccx, y, out ca)) ca = area;
            y = ca.Top;
            if (moving) {
                int newX = x + dir * speed;
                int lead = dir > 0 ? newX + W - 1 : newX;
                Rectangle tmp;
                if (!TryAreaAt(lead, y, out tmp)) {
                    // esquina: bajar por la pared
                    if (dir > 0) { mode = Mode.WallR; x = ca.Right - W; }
                    else { mode = Mode.WallL; x = ca.Left; }
                    vdir = 1; stepTop = null;
                } else x = newX;
                NextFrame();
            }
        }

        if ((x - M) != Left || (y - M) != Top) Location = new Point(x - M, y - M);
        InvalidateIfChanged();
    }

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.Run(new PetForm());
    }
}
