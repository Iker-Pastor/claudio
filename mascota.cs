// Mascota virtual de escritorio
// - Pasea por el suelo, trepa por los bordes y anda por el techo, cruzando pantallas
// - Cogela con el raton: cuelga del punto por donde la agarras y se balancea/gira
//   si mueves el raton en circulos
// - Sueltala en movimiento para lanzarla: vuela, gira y rebota en los bordes
// - Click: da un saltito y trae Claude al frente (lo abre si no esta abierto)
// - Rueda del raton encima: crece / encoge
// - Click derecho: menu (Abrir Claude / Color / Quieta-Pasear / Salir)
// - Color: predefinidos o personalizado (se guarda)
// - Animos (normal / feliz / aburrida / mareada / dormida), bocadillos de texto,
//   ojos que siguen al raton y sueno cuando no hay actividad (o de noche)
//
// Se compila con el csc.exe que trae Windows (.NET Framework 4, C# 5): mascota.vbs lo hace solo.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Win {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    // Ventanas de otras apps (subirse a ellas) y aviso de parpadeo en la barra de tareas
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
}

enum Mode { Floor, WallL, WallR, Ceiling, Air }
enum Mood { Normal, Feliz, Aburrida, Mareada, Dormida }

// Ventana de otra app: caja visible y si se puede andar por su borde superior
class WinInfo { public IntPtr H; public Rectangle R; public bool Usable; }

// ---------- Bocadillo de texto: ventana sin borde, topmost, que no roba el foco ni capta clicks ----------
class Bubble : Form {
    readonly Font font = new Font("Segoe UI", 9f);
    static readonly Color Ink = Color.FromArgb(40, 40, 40);
    const int PAD = 7, TAIL = 7;
    string text = "";
    bool tailDown = true;
    int tailX = 20, bw = 40, bh = 24;
    GraphicsPath shape;

    public Bubble() {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Opacity = 0.97;               // ventana "layered": permite el click-through
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams {
        get {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x20;   // NOACTIVATE | TOOLWINDOW | TRANSPARENT (click-through)
            return cp;
        }
    }

    public void SetText(string t) {
        if (t == text) return;
        text = t; Rebuild();
    }

    void Rebuild() {
        Size ts = TextRenderer.MeasureText(text, font, new Size(190, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        bw = ts.Width + 2 + 2 * PAD; bh = ts.Height + 2 * PAD;
        ClientSize = new Size(bw, bh + TAIL);
        int top = tailDown ? 0 : TAIL;
        int r = 8;
        Rectangle rc = new Rectangle(0, top, bw - 1, bh - 1);
        GraphicsPath p = new GraphicsPath(FillMode.Winding);
        p.AddArc(rc.Left, rc.Top, r * 2, r * 2, 180, 90);
        p.AddArc(rc.Right - r * 2, rc.Top, r * 2, r * 2, 270, 90);
        p.AddArc(rc.Right - r * 2, rc.Bottom - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(rc.Left, rc.Bottom - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        int tx = Math.Max(PAD + 5, Math.Min(bw - PAD - 5, tailX));
        if (tailDown) p.AddPolygon(new Point[] { new Point(tx - 5, bh - 1), new Point(tx + 5, bh - 1), new Point(tx, bh - 1 + TAIL) });
        else p.AddPolygon(new Point[] { new Point(tx - 5, top), new Point(tx + 5, top), new Point(tx, 0) });
        Region old = Region;
        Region = new Region(p);
        if (old != null) old.Dispose();
        if (shape != null) shape.Dispose();
        shape = p;
        Invalidate();
    }

    // Se coloca encima de la mascota (o debajo si no cabe arriba). pet = caja visible en pantalla.
    public void Follow(Rectangle pet, Rectangle area) {
        bool down = pet.Top - (bh + TAIL) - 2 >= area.Top;
        int x = pet.Left + pet.Width / 2 - bw / 2;
        x = Math.Max(area.Left, Math.Min(area.Right - bw, x));
        int tx = pet.Left + pet.Width / 2 - x;
        if (down != tailDown || Math.Abs(tx - tailX) > 3) { tailDown = down; tailX = tx; Rebuild(); }
        int y = down ? pet.Top - bh - TAIL - 2 : pet.Bottom + 2;
        if (x != Left || y != Top) Location = new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e) {
        if (shape != null) using (Pen p = new Pen(Color.FromArgb(150, 150, 150))) e.Graphics.DrawPath(p, shape);
        TextRenderer.DrawText(e.Graphics, text, font,
            new Rectangle(PAD, (tailDown ? 0 : TAIL) + PAD, bw - 2 * PAD + 2, bh - 2 * PAD), Ink,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) { font.Dispose(); if (shape != null) shape.Dispose(); }
        base.Dispose(disposing);
    }
}

// ---------- Menu en modo oscuro ----------
class DarkColors : ProfessionalColorTable {
    static readonly Color Bg = Color.FromArgb(43, 43, 43);
    static readonly Color Hover = Color.FromArgb(65, 65, 65);
    static readonly Color Border = Color.FromArgb(80, 80, 80);
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Border; } }
    public override Color MenuItemBorder { get { return Hover; } }
    public override Color MenuItemSelected { get { return Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
    public override Color MenuItemPressedGradientBegin { get { return Hover; } }
    public override Color MenuItemPressedGradientEnd { get { return Hover; } }
    public override Color SeparatorDark { get { return Border; } }
    public override Color SeparatorLight { get { return Border; } }
    public override Color CheckBackground { get { return Color.FromArgb(70, 90, 120); } }
    public override Color CheckSelectedBackground { get { return Color.FromArgb(80, 105, 140); } }
    public override Color CheckPressedBackground { get { return Color.FromArgb(80, 105, 140); } }
}

class DarkRenderer : ToolStripProfessionalRenderer {
    static readonly Color Text = Color.FromArgb(240, 240, 240);
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        e.TextColor = e.Item.Enabled ? Text : Color.FromArgb(130, 130, 130);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
        e.ArrowColor = Text;
        base.OnRenderArrow(e);
    }
    // La marca de verificacion por defecto es negra: en oscuro se dibuja a mano en blanco
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
        Rectangle r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 3, e.ImageRectangle.Height + 3);
        using (SolidBrush b = new SolidBrush(ColorTable.CheckBackground)) e.Graphics.FillRectangle(b, r);
        using (Pen p = new Pen(Color.FromArgb(120, 150, 200))) e.Graphics.DrawRectangle(p, r);
        if (e.Item.Image == null) {
            System.Drawing.Drawing2D.SmoothingMode old = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Text, 1.6f)) {
                int x = e.ImageRectangle.X, y = e.ImageRectangle.Y, w = e.ImageRectangle.Width, h = e.ImageRectangle.Height;
                e.Graphics.DrawLines(p, new PointF[] {
                    new PointF(x + w * 0.2f, y + h * 0.5f), new PointF(x + w * 0.42f, y + h * 0.72f), new PointF(x + w * 0.8f, y + h * 0.28f) });
            }
            e.Graphics.SmoothingMode = old;
        }
    }
}

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

    // ---------- Teams en verde: mientras la mascota esta abierta ----------
    // Solo "toca" el sistema si llevas IDLE_MS sin actividad real.
    const int IDLE_MS = 60000, KEEP_MS = 30000;
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    readonly Timer keepTimer = new Timer();
    ToolStripMenuItem miTeams;
    bool teamsOn;
    SolidBrush bBody = new SolidBrush(Orange);
    readonly SolidBrush bBlack = new SolidBrush(Black);
    readonly ToolStripMenuItem miStay;

    // ---------- Color ----------
    static readonly string[] PresetNames = { "Naranja Claude", "Rojo", "Azul", "Verde", "Morado", "Rosa", "Amarillo", "Blanco" };
    static readonly Color[] PresetColors = {
        Orange, Color.FromArgb(220, 60, 60), Color.FromArgb(70, 130, 220), Color.FromArgb(80, 180, 90),
        Color.FromArgb(140, 90, 210), Color.FromArgb(235, 120, 170), Color.FromArgb(235, 200, 60), Color.FromArgb(240, 240, 240)
    };
    Color baseColor = Orange;     // color elegido
    readonly ToolStripMenuItem[] miPresets = new ToolStripMenuItem[PresetNames.Length];
    readonly ToolStripMenuItem miCustom, miRainbow, miColor;
    readonly ContextMenuStrip menu;
    bool rainbow = false;         // modo arcoiris: el color va rotando
    double hue = 0;               // tono actual del arcoiris (0..360)
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MascotaClaude");
    static readonly string ConfigPath = Path.Combine(DataDir, "config.txt");

    // ---------- Animo, bocadillos, sueno y mirada ----------
    // Frases de los bocadillos: edita estas listas a tu gusto.
    static readonly string[] FrasesAzar = {
        "¿Qué tal vas?", "Aquí sigo, vigilando.", "¿Has bebido agua?", "Estírate un poquito.",
        "Me gusta este rincón.", "¿Hacemos algo?", "Qué buen día para pasear.", "Tic tac, tic tac...",
        "Hoy te veo con ganas.", "¿Un descanso?"
    };
    static readonly string[] FrasesLanzada = { "¡Wiiii!", "¡Aaaah!", "¡Voy volando!", "¡Cuidado!", "¡Otra vez!" };
    static readonly string[] FrasesDespertar = { "¡Buenas!", "Mmm... ya estoy aquí.", "¿Ya has vuelto?", "Qué bien he dormido.", "Hola otra vez." };
    static readonly string[] FrasesCaricia = { "Qué a gusto...", "Me gusta eso.", "Jiji, cosquillas.", "Sigue, sigue.", "Soy feliz." };
    static readonly string[] FrasesMareo = { "Uf, qué mareo...", "Todo da vueltas...", "Necesito sentarme.", "Ay, mi cabeza." };
    static readonly string[] FrasesAburrida = { "*suspiro*", "Qué aburrimiento...", "¿Hay alguien ahí?", "Hmpf..." };
    const int BORED_MS = 600000;                  // sin interactuar con ella -> aburrida (10 min)
    const uint SLEEP_MS = 300000;                 // sin entrada en el sistema -> dormida (5 min)
    const uint NIGHT_IDLE_MS = 60000;             // de noche (01:00-07:00) basta 1 min sin entrada
    Mood mood = Mood.Normal; int moodTicks = 0;
    readonly Bubble bubble = new Bubble();
    bool bubblesOn = true; int bubbleTicks = 0, nextChatter, nextSigh;
    ToolStripMenuItem miBubbles;
    int lastActMs;                                 // ultima interaccion con la mascota
    uint lastRealInput, lastSeenInput, nudgeAt; bool hasNudge;   // ultima entrada real del usuario (sin contar el Nudge de Teams)
    int lookX, lookY;                              // desplazamiento de las pupilas (-1, 0, 1)
    int chaseTicks = 0;                            // persiguiendo el cursor
    int wobX = 0;                                  // tambaleo (mareada)
    int caressN = 0, lastSign = 0, lastMoveMs = 0, lastMX = 0;
    int shakeScore = 0, shakeStamp = 0;
    readonly SolidBrush bHeart = new SolidBrush(Color.FromArgb(240, 90, 130));
    readonly SolidBrush bZ = new SolidBrush(Color.FromArgb(110, 130, 200));
    static readonly string[] HeartGlyph = { ".X.X.", "XXXXX", ".XXX.", "..X.." };
    static readonly string[] ZGlyph = { "XXX", "..X", ".X.", "X..", "XXX" };

    // ---------- Ventanas, avisos y recordatorios ----------
    const int POMO_WORK_MIN = 25, POMO_BREAK_MIN = 5, WATER_MIN = 60, STRETCH_MIN = 50;
    readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
    readonly List<WinInfo> wins = new List<WinInfo>();
    readonly Win.EnumProc enumProc;
    bool winsOn = true, onWin = false, jumping = false;
    IntPtr perchHwnd; Rectangle perchRect;            // ventana sobre la que anda
    ToolStripMenuItem miWins, miRem, miPomo, miWater, miStretch, miGym;
    bool pomoOn, pomoWork, waterOn, stretchOn, gymOn;  // pomodoro no se guarda: solo agua, estirar y gimnasio
    int pomoEnd, waterNext, stretchNext, leaveDay, gymDay;
    bool bubbleImportant;
    bool lowWarned; PowerLineStatus lastLine = PowerLineStatus.Unknown;
    uint shellMsg; int lastFlashMs;

    // Caché de las areas de trabajo (se refresca al cambiar las pantallas y cada pocos segundos)
    Rectangle[] areas;
    // Lo ultimo que se dibujo: solo se repinta si cambia
    float lastAngle = float.NaN; int lastHop = -1; string lastLegs = null; bool lastEyes; int lastPX = -1;
    Mood lastMood = Mood.Normal; int lastLX, lastLY, lastAnim = -1; bool lastLying;

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
        menu = new ContextMenuStrip();
        menu.Items.Add("Abrir Claude", null, delegate { FocusClaude(); });

        miColor = new ToolStripMenuItem("Color");
        for (int i = 0; i < PresetNames.Length; i++) {
            Color c = PresetColors[i];
            miPresets[i] = new ToolStripMenuItem(PresetNames[i], Swatch(c));
            miPresets[i].Click += delegate { SetFixedColor(c); };
            miColor.DropDownItems.Add(miPresets[i]);
        }
        miCustom = new ToolStripMenuItem("Personalizado...");
        miCustom.Click += delegate { PickCustomColor(); };
        miColor.DropDownItems.Add(miCustom);
        miColor.DropDownItems.Add(new ToolStripSeparator());
        miRainbow = new ToolStripMenuItem("Arcoíris", RainbowSwatch());
        miRainbow.Click += delegate { rainbow = true; UpdateColorChecks(); SaveConfig(); };
        miColor.DropDownItems.Add(miRainbow);
        menu.Items.Add(miColor);

        miStay = new ToolStripMenuItem("Quieta");
        miStay.Click += delegate {
            paused = !paused;
            miStay.Text = paused ? "Pasear" : "Quieta";
        };
        menu.Items.Add(miStay);
        miTeams = new ToolStripMenuItem("Teams en verde");
        miTeams.CheckOnClick = true;
        miTeams.Checked = true;
        miTeams.CheckedChanged += delegate { SetTeams(miTeams.Checked); };
        menu.Items.Add(miTeams);
        miBubbles = new ToolStripMenuItem("Bocadillos");
        miBubbles.CheckOnClick = true;
        miBubbles.Checked = true;
        miBubbles.CheckedChanged += delegate {
            bubblesOn = miBubbles.Checked;
            if (!bubblesOn) HideBubble();
            SaveConfig();
        };
        menu.Items.Add(miBubbles);

        miWins = new ToolStripMenuItem("Subir a ventanas");
        miWins.CheckOnClick = true;
        miWins.Checked = true;
        miWins.CheckedChanged += delegate {
            winsOn = miWins.Checked;
            if (winsOn) RefreshWindows(); else { wins.Clear(); onWin = false; }
            SaveConfig();
        };
        menu.Items.Add(miWins);

        miRem = new ToolStripMenuItem("Recordatorios");
        miPomo = new ToolStripMenuItem("Pomodoro (" + POMO_WORK_MIN + "/" + POMO_BREAK_MIN + ")");
        miPomo.CheckOnClick = true;
        miPomo.CheckedChanged += delegate {
            pomoOn = miPomo.Checked;
            if (pomoOn) {
                pomoWork = true; pomoEnd = Environment.TickCount + POMO_WORK_MIN * 60000;
                SayText("Pomodoro en marcha: " + POMO_WORK_MIN + " min de trabajo.", true);
            } else SayText("Pomodoro parado.", true);
        };
        miWater = new ToolStripMenuItem("Beber agua (cada " + WATER_MIN + " min)");
        miWater.CheckOnClick = true;
        miWater.CheckedChanged += delegate {
            waterOn = miWater.Checked;
            if (waterOn) waterNext = Environment.TickCount + WATER_MIN * 60000;
            SaveConfig();
        };
        miStretch = new ToolStripMenuItem("Pausa activa (cada " + STRETCH_MIN + " min)");
        miStretch.CheckOnClick = true;
        miStretch.CheckedChanged += delegate {
            stretchOn = miStretch.Checked;
            if (stretchOn) stretchNext = Environment.TickCount + STRETCH_MIN * 60000;
            SaveConfig();
        };
        miGym = new ToolStripMenuItem("Gimnasio a las 15:30 (L-V)");
        miGym.CheckOnClick = true;
        miGym.CheckedChanged += delegate { gymOn = miGym.Checked; SaveConfig(); };
        miRem.DropDownItems.Add(miPomo);
        miRem.DropDownItems.Add(miWater);
        miRem.DropDownItems.Add(miStretch);
        miRem.DropDownItems.Add(miGym);
        menu.Items.Add(miRem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, delegate { timer.Stop(); SetTeams(false); Close(); });
        ContextMenuStrip = menu;

        LoadConfig();
        enumProc = OnEnumWindow;
        miBubbles.Checked = bubblesOn;
        miWins.Checked = winsOn;
        miWater.Checked = waterOn; miStretch.Checked = stretchOn; miGym.Checked = gymOn;
        int t0 = Environment.TickCount;
        waterNext = t0 + WATER_MIN * 60000; stretchNext = t0 + STRETCH_MIN * 60000;
        lastActMs = Environment.TickCount; lastRealInput = (uint)Environment.TickCount;
        nextChatter = 6000 + rand.Next(9840);       // ~3-8 min (30 ms por tick)
        SetBodyColor(baseColor);
        UpdateColorChecks();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        ApplyMenuTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;   // cambiar claro/oscuro en vivo

        timer.Interval = 30;
        timer.Tick += OnTick;
        Load += delegate { MinimumSize = new Size(SZ, SZ); Size = new Size(SZ, SZ); };
        Shown += delegate { Size = new Size(SZ, SZ); timer.Start(); SetTeams(miTeams.Checked); };

        keepTimer.Interval = KEEP_MS;
        keepTimer.Tick += delegate { Nudge(); };
    }

    void SetTeams(bool on) {
        if (on == teamsOn) return;
        teamsOn = on;
        if (on) {
            // Impide que la pantalla se apague/bloquee y que el equipo se suspenda
            Win.SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            keepTimer.Start();
            Nudge();
        } else {
            Win.SetThreadExecutionState(ES_CONTINUOUS);
            keepTimer.Stop();
        }
    }

    void Nudge() {
        if (!teamsOn || IdleMs() < IDLE_MS) return;
        // F15 no existe en teclados normales: ninguna app reacciona, pero cuenta como actividad
        Win.keybd_event(0x7E, 0, 0, UIntPtr.Zero);
        Win.keybd_event(0x7E, 0, 2, UIntPtr.Zero);
        Win.mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
        Win.mouse_event(0x0001, -1, 0, 0, UIntPtr.Zero);
        nudgeAt = (uint)Environment.TickCount; hasNudge = true;   // para no confundirlo con entrada real
    }

    static uint IdleMs() {
        Win.LASTINPUTINFO info = new Win.LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);
        if (!Win.GetLastInputInfo(ref info)) return 0;
        return (uint)Environment.TickCount - info.dwTime;
    }

    void OnDisplayChanged(object s, EventArgs e) { RefreshAreas(); }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SetTeams(false);
            keepTimer.Dispose();
            timer.Dispose(); bBody.Dispose(); bBlack.Dispose(); bHeart.Dispose(); bZ.Dispose();
            bubble.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- Tema del menu (sigue el modo claro/oscuro de Windows) ----------
    static bool SystemDark() {
        try {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return v is int && (int)v == 0;
        } catch { return false; }
    }

    bool? menuDark = null;   // tema aplicado ahora mismo (null = ninguno todavia)
    void ApplyMenuTheme() {
        bool dark = SystemDark();
        if (menuDark == dark) return;
        menuDark = dark;
        ToolStripRenderer r = dark ? (ToolStripRenderer)new DarkRenderer() : new ToolStripProfessionalRenderer();
        menu.Renderer = r;
        miColor.DropDown.Renderer = r;
        miRem.DropDown.Renderer = r;
    }

    void OnUserPreferenceChanged(object s, UserPreferenceChangedEventArgs e) {
        if (e.Category == UserPreferenceCategory.General) ApplyMenuTheme();
    }

    // ---------- Color ----------
    static Bitmap Swatch(Color c) {
        Bitmap b = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(b)) {
            using (SolidBrush br = new SolidBrush(c)) g.FillRectangle(br, 1, 1, 14, 14);
            g.DrawRectangle(Pens.Gray, 1, 1, 13, 13);
        }
        return b;
    }

    static Bitmap RainbowSwatch() {
        Bitmap b = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(b)) {
            for (int x = 0; x < 14; x++)
                using (SolidBrush br = new SolidBrush(FromHue(x * 360.0 / 14))) g.FillRectangle(br, 1 + x, 1, 1, 14);
            g.DrawRectangle(Pens.Gray, 1, 1, 13, 13);
        }
        return b;
    }

    // Color vivo del tono h (grados). Saturacion < 1, asi que nunca da el magenta transparente.
    static Color FromHue(double h) {
        const double s = 0.65, v = 0.95;
        double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(R((r + m) * 255), R((g + m) * 255), R((b + m) * 255));
    }

    void SetBodyColor(Color c) {
        // el magenta exacto es el color transparente de la ventana: evitarlo
        if (c.ToArgb() == Key.ToArgb()) c = Color.FromArgb(254, 0, 255);
        if (bBody.Color.ToArgb() == c.ToArgb()) return;
        SolidBrush old = bBody;
        bBody = new SolidBrush(c);
        old.Dispose();
        Invalidate();
    }

    void SetFixedColor(Color c) {
        baseColor = c; rainbow = false;
        SetBodyColor(c); UpdateColorChecks(); SaveConfig();
    }

    void PickCustomColor() {
        using (ColorDialog dlg = new ColorDialog()) {
            dlg.FullOpen = true; dlg.Color = baseColor;
            if (dlg.ShowDialog(this) == DialogResult.OK) SetFixedColor(dlg.Color);
        }
    }

    void UpdateColorChecks() {
        bool preset = false;
        for (int i = 0; i < miPresets.Length; i++) {
            bool on = !rainbow && PresetColors[i].ToArgb() == baseColor.ToArgb();
            miPresets[i].Checked = on; preset |= on;
        }
        miCustom.Checked = !rainbow && !preset;
        miRainbow.Checked = rainbow;
    }

    void LoadConfig() {
        try {
            if (!File.Exists(ConfigPath)) return;
            foreach (string line in File.ReadAllLines(ConfigPath)) {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k == "color") baseColor = Color.FromArgb(255, Color.FromArgb(int.Parse(v, NumberStyles.HexNumber)));
                else if (k == "arcoiris") rainbow = v == "1";
                else if (k == "bocadillos") bubblesOn = v != "0";
                else if (k == "ventanas") winsOn = v != "0";
                else if (k == "agua") waterOn = v == "1";
                else if (k == "estirar") stretchOn = v == "1";
                else if (k == "gimnasio") gymOn = v == "1";
            }
        } catch { }
    }

    void SaveConfig() {
        try {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ConfigPath,
                "color=" + (baseColor.ToArgb() & 0xFFFFFF).ToString("X6") + "\r\n" +
                "arcoiris=" + (rainbow ? "1" : "0") + "\r\n" +
                "bocadillos=" + (bubblesOn ? "1" : "0") + "\r\n" +
                "ventanas=" + (winsOn ? "1" : "0") + "\r\n" +
                "agua=" + (waterOn ? "1" : "0") + "\r\n" +
                "estirar=" + (stretchOn ? "1" : "0") + "\r\n" +
                "gimnasio=" + (gymOn ? "1" : "0") + "\r\n");
        } catch { }
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
    // Tumbada durmiendo en el suelo (sin patas, el cuerpo apoyado)
    bool Lying() { return mood == Mood.Dormida && mode == Mode.Floor && !dragging && vy == 0; }
    int CurHop() { return (Free || Lying()) ? 0 : (int)Math.Round(hop); }
    int AnimPhase() {
        switch (mood) {
            case Mood.Feliz: return (tick / 6) % 8;
            case Mood.Mareada: return (tick / 3) % 8;
            case Mood.Dormida: return (tick / 15) % 4;
        }
        return 0;
    }
    string CurLegs() {
        if (Free) return (tick & 2) == 0 ? LegsA : LegsB;   // patalea
        if (!walking || paused) return LegsA;
        return frame == 0 ? LegsA : LegsB;
    }

    // Repinta solo si ha cambiado algo visible (moverse la ventana no necesita repintar)
    void InvalidateIfChanged() {
        float angle = CurAngle(); int hp = CurHop(); string legs = CurLegs(); bool eyes = blink <= 0;
        if (angle != lastAngle || hp != lastHop || !ReferenceEquals(legs, lastLegs) || eyes != lastEyes || PX != lastPX
            || mood != lastMood || lookX != lastLX || lookY != lastLY || AnimPhase() != lastAnim || Lying() != lastLying)
            Invalidate();
    }

    void Dot(Graphics g, int offX, int offY, int sx, int sy, Brush b) {
        g.FillRectangle(b, offX + sx * PX, offY + sy * PX, PX, PX);
    }

    // Ojos segun el animo. Columnas de los ojos: 4 y 9; filas 2-3 (coordenadas del sprite).
    void DrawEyes(Graphics g, int offX, int offY, bool open) {
        int[] cols = { 4, 9 };
        foreach (int ex in cols) {
            switch (mood) {
                case Mood.Dormida:       // cerrados: una rayita
                    Dot(g, offX, offY, ex - 1, 3, bBlack); Dot(g, offX, offY, ex, 3, bBlack); Dot(g, offX, offY, ex + 1, 3, bBlack);
                    break;
                case Mood.Feliz:         // ^ ^
                    Dot(g, offX, offY, ex, 2, bBlack); Dot(g, offX, offY, ex - 1, 3, bBlack); Dot(g, offX, offY, ex + 1, 3, bBlack);
                    break;
                case Mood.Mareada: {     // espiral: anillo con un hueco que gira
                    int gap = (tick / 3) % 8;
                    int[] rx = { -1, 0, 1, 1, 1, 0, -1, -1 };
                    int[] ry = { -1, -1, -1, 0, 1, 1, 1, 0 };
                    for (int i = 0; i < 8; i++)
                        if (i != gap) Dot(g, offX, offY, ex + rx[i], 3 + ry[i], bBlack);
                    break;
                }
                case Mood.Aburrida:      // medio cerrados: solo la mitad de abajo
                    if (open) Dot(g, offX, offY, ex + lookX, 3, bBlack);
                    break;
                default:
                    if (open) { Dot(g, offX, offY, ex + lookX, 2 + lookY, bBlack); Dot(g, offX, offY, ex + lookX, 3 + lookY, bBlack); }
                    break;
            }
        }
    }

    void DrawGlyph(Graphics g, string[] glyph, int x, int y, int cell, Brush b) {
        for (int gy2 = 0; gy2 < glyph.Length; gy2++)
            for (int gx2 = 0; gx2 < glyph[gy2].Length; gx2++)
                if (glyph[gy2][gx2] == 'X') g.FillRectangle(b, x + gx2 * cell, y + gy2 * cell, cell, cell);
    }

    // Corazoncitos (feliz) o Zzz (dormida) por encima de la cabeza
    void DrawDecor(Graphics g, int offX, int offY, bool lying) {
        if (mood == Mood.Feliz) {
            int cell = Math.Max(1, PX / 2), p = (tick / 6) % 8;
            for (int k = 0; k < 2; k++) {
                int ph = (p + k * 4) % 8;
                int hx = offX + (k == 0 ? 1 : 8) * PX;
                int hy = Math.Max(0, offY - cell * 3 - ph * (PX / 2 + 1));
                DrawGlyph(g, HeartGlyph, hx, hy, cell, bHeart);
            }
        } else if (mood == Mood.Dormida && lying) {
            int n = (tick / 15) % 4;
            for (int i = 0; i < n; i++) {
                int cell = Math.Max(1, PX * (i + 2) / 6);
                int zx = offX + 10 * PX + i * PX;
                int zy = Math.Max(0, offY - PX / 2 - i * PX - 5 * cell);
                DrawGlyph(g, ZGlyph, zx, zy, cell, bZ);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        Graphics g = e.Graphics;
        float angle = CurAngle(); int hp = CurHop(); string legs = CurLegs(); bool eyes = blink <= 0;
        bool lying = Lying();
        lastAngle = angle; lastHop = hp; lastLegs = legs; lastEyes = eyes; lastPX = PX;
        lastMood = mood; lastLX = lookX; lastLY = lookY; lastAnim = AnimPhase(); lastLying = lying;

        if (angle != 0) {
            g.TranslateTransform(SZ / 2f, SZ / 2f); g.RotateTransform(angle); g.TranslateTransform(-SZ / 2f, -SZ / 2f);
        }
        int offY = M + H - (Body.Length + 2) * PX - hp + (lying ? 2 * PX : 0);
        int offX = M;
        int rows = lying ? Body.Length : Body.Length + 2;   // tumbada: sin patas
        for (int y = 0; y < rows; y++) {
            string row = y < Body.Length ? Body[y] : legs;
            for (int x = 0; x < row.Length; x++) {
                if (row[x] == '.') continue;
                g.FillRectangle(bBody, offX + x * PX, offY + y * PX, PX, PX);
            }
        }
        DrawEyes(g, offX, offY, eyes);
        DrawDecor(g, offX, offY, lying);
    }

    // ---------- Raton: agarrar / girar / lanzar / click ----------
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        down = true; moved = false; Touch();
        Point p = Cursor.Position;
        downX = p.X; downY = p.Y;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!down) { Caress(e.X); return; }
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
            jumping = false; onWin = false;
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
        Touch();
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
            if (Math.Abs(vx) + Math.Abs(vyA) >= 10) {
                AddShake(2);
                if (mood != Mood.Mareada && rand.Next(2) == 0) Say(FrasesLanzada);
            }
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

        // suelo: el de la pantalla o el borde superior de una ventana por la que acaba de caer
        double fl = a.Bottom; int li = -1;
        if (winsOn && vyA > 0) {
            li = FindLanding(R(cx), R(cy - vyA + half), R(cy + half));
            if (li >= 0) fl = wins[li].R.Top;
        }

        // paredes: rebota solo si al otro lado no hay otra pantalla
        if (cx - half < a.Left && !InScreen(a.Left - 1, R(cy + half - 4))) {
            if (Math.Abs(vx) > 10) AddShake(1);
            cx = a.Left + half; vx = Math.Abs(vx) * bnc; phiV += vyA * 0.01;
        }
        if (cx + half > a.Right && !InScreen(a.Right + 1, R(cy + half - 4))) {
            if (Math.Abs(vx) > 10) AddShake(1);
            cx = a.Right - half; vx = -Math.Abs(vx) * bnc; phiV -= vyA * 0.01;
        }
        // techo
        if (cy - half < a.Top) {
            if (Math.Abs(vyA) > 10) AddShake(1);
            cy = a.Top + half; vyA = Math.Abs(vyA) * bnc;
        }
        // suelo
        if (cy + half >= fl) {
            cy = fl - half;
            if (li < 0) jumping = false;
            if ((Math.Abs(vyA) < 4 && Math.Abs(vx) < 3) || (jumping && li >= 0)) {
                // aterriza: se pone de pie y sigue paseando
                mode = Mode.Floor; phi = 0; phiV = 0; vy = 0; hopV = 5.0;
                if (li >= 0) SetPerch(li);
                jumping = false;
                if (vx > 0) dir = 1; else if (vx < 0) dir = -1;
                vx = 0; vyA = 0;
            } else {
                if (Math.Abs(vyA) > 10) AddShake(1);
                phiV += vx * 0.008;      // al rodar por el suelo gira
                vyA = -Math.Abs(vyA) * bnc;
                vx *= fric;
                if (Math.Abs(vyA) < 2) vyA = 0;
            }
        }
        SetCenter(cx, cy);
    }

    // ---------- Animo, bocadillos y sueno ----------
    void SetMood(Mood m, int ticks) { mood = m; moodTicks = ticks; }

    // Interaccion directa con la mascota (resetea el aburrimiento)
    void Touch() {
        lastActMs = Environment.TickCount;
        if (mood == Mood.Aburrida) SetMood(Mood.Normal, 0);
    }

    // "Acariciar": mover el raton por encima, sin pulsar, de un lado a otro varias veces
    void Caress(int mx) {
        if (mood == Mood.Dormida || mood == Mood.Mareada || dragging) return;
        int now = Environment.TickCount;
        if (now - lastMoveMs > 700) { caressN = 0; lastSign = 0; }
        int dx = mx - lastMX;
        if (Math.Abs(dx) < 3) return;
        lastMX = mx; lastMoveMs = now; Touch();
        int s = dx > 0 ? 1 : -1;
        if (lastSign != 0 && s != lastSign) caressN++;
        lastSign = s;
        if (caressN >= 4) {
            caressN = 0; lastSign = 0;
            if (mood != Mood.Feliz) { hopV = 6.0; Say(FrasesCaricia); }
            SetMood(Mood.Feliz, 230);   // ~7 s
        }
    }

    // Lanzamientos y rebotes fuertes en poco tiempo la marean
    void AddShake(int n) {
        int now = Environment.TickCount;
        if (now - shakeStamp > 8000) shakeScore = 0;
        shakeStamp = now; shakeScore += n;
        if (shakeScore >= 10 && mood != Mood.Dormida) {
            shakeScore = 0; chaseTicks = 0;
            SetMood(Mood.Mareada, 170);   // ~5 s
            Say(FrasesMareo);
        }
    }

    void Say(string[] pool) { SayText(pool[rand.Next(pool.Length)], false); }

    // important = recordatorios y avisos: salen aunque los bocadillos esten silenciados, duran mas
    // y no los pisan los bocadillos normales.
    void SayText(string t, bool important) {
        if (!important) {
            if (!bubblesOn || mood == Mood.Dormida) return;
            if (bubbleImportant && bubbleTicks > 0) return;
        }
        bubbleImportant = important;
        bubble.SetText(t);
        bubbleTicks = important ? 330 + t.Length * 6 : 100 + t.Length * 4;
        UpdateBubble();
        if (!bubble.Visible) bubble.Show();
    }

    void HideBubble() {
        bubbleTicks = 0; bubbleImportant = false;
        if (bubble.Visible) bubble.Hide();
    }

    // ---------- Avisos y recordatorios ----------
    bool CanAlert() { return mood != Mood.Dormida; }

    // Bocadillo importante + (opcional) cambio de animo. No hace nada si esta dormida.
    bool Alert(string text, Mood m, int moodT) {
        if (!CanAlert()) return false;
        if (m != Mood.Normal) SetMood(m, moodT);
        if (m == Mood.Feliz) hopV = 6.0;
        SayText(text, true);
        return true;
    }

    void CheckPower() {
        try {
            PowerStatus ps = SystemInformation.PowerStatus;
            if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0) return;
            PowerLineStatus line = ps.PowerLineStatus;
            float pct = ps.BatteryLifePercent;
            if (line == PowerLineStatus.Online) {
                lowWarned = false;
                if (lastLine == PowerLineStatus.Offline) {
                    if (!Alert("¡Cargador conectado! Gracias.", Mood.Feliz, 230)) return;   // reintenta al despertar
                }
            } else if (line == PowerLineStatus.Offline && pct <= 1f) {
                if (pct >= 0.20f) lowWarned = false;
                else if (!lowWarned) {
                    if (Alert("Batería baja (" + (int)Math.Round(pct * 100) + "%). ¡Enchúfame al cargador!", Mood.Aburrida, 300)) lowWarned = true;
                }
            }
            lastLine = line;
        } catch { }
    }

    void Reminders() {
        if (!CanAlert() || (bubbleImportant && bubbleTicks > 0)) return;
        int now = Environment.TickCount;
        DateTime dt = DateTime.Now;
        bool weekday = dt.DayOfWeek != DayOfWeek.Saturday && dt.DayOfWeek != DayOfWeek.Sunday;
        int dayKey = dt.Year * 1000 + dt.DayOfYear;
        if (weekday && dt.Hour == 15 && dt.Minute < 10 && leaveDay != dayKey) {
            leaveDay = dayKey; Alert("¡Hora de salir!", Mood.Feliz, 300); return;
        }
        if (gymOn && weekday && dt.Hour == 15 && dt.Minute >= 30 && dt.Minute < 40 && gymDay != dayKey) {
            gymDay = dayKey; Alert("¡Hora de ir al gimnasio!", Mood.Feliz, 300); return;
        }
        if (pomoOn && now - pomoEnd >= 0) {
            if (pomoWork) { pomoWork = false; pomoEnd = now + POMO_BREAK_MIN * 60000; Alert("¡Descanso de " + POMO_BREAK_MIN + " min! Levántate un poco.", Mood.Feliz, 300); }
            else { pomoWork = true; pomoEnd = now + POMO_WORK_MIN * 60000; Alert("Se acabó el descanso: ¡otros " + POMO_WORK_MIN + " min de trabajo!", Mood.Normal, 0); }
            return;
        }
        if (waterOn && now - waterNext >= 0) {
            waterNext = now + WATER_MIN * 60000; Alert("Toca beber agua.", Mood.Normal, 0); return;
        }
        if (stretchOn && now - stretchNext >= 0) {
            stretchNext = now + STRETCH_MIN * 60000; Alert("Pausa activa: levántate y estírate un poco.", Mood.Normal, 0); return;
        }
    }

    // ---------- Subirse a ventanas ----------
    bool WinState(IntPtr h, out Rectangle r) {
        r = Rectangle.Empty;
        if (!Win.IsWindow(h) || !Win.IsWindowVisible(h) || Win.IsIconic(h)) return false;
        if (Win.GetWindowTextLength(h) == 0) return false;
        if ((Win.GetWindowLong(h, -20) & (0x80 | 0x20)) != 0) return false;     // toolwindow / transparente a clicks
        int cloaked;
        if (Win.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;   // DWMWA_CLOAKED
        uint pid; Win.GetWindowThreadProcessId(h, out pid);
        if (pid == ownPid) return false;
        Win.RECT rc;
        if (Win.DwmGetWindowAttribute(h, 9, out rc, 16) != 0 && !Win.GetWindowRect(h, out rc)) return false;   // 9 = EXTENDED_FRAME_BOUNDS
        r = Rectangle.FromLTRB(rc.L, rc.T, rc.R, rc.B);
        return true;
    }

    // Se puede andar por su borde superior: no maximizada, con sitio encima y a una altura razonable
    bool WinUsable(IntPtr h, Rectangle r) {
        if (r.Width < 120 || r.Height < 60 || Win.IsZoomed(h)) return false;
        int mx = r.Left + r.Width / 2;
        if (!InScreen(mx, r.Top)) return false;
        Rectangle a = NearestArea(mx, r.Top);
        return r.Top - H >= a.Top && r.Top <= a.Bottom - H - 4;
    }

    bool OnEnumWindow(IntPtr h, IntPtr l) {
        Rectangle r;
        if (WinState(h, out r)) { WinInfo w = new WinInfo(); w.H = h; w.R = r; w.Usable = WinUsable(h, r); wins.Add(w); }
        return true;
    }

    // Lista de ventanas en orden Z (de arriba a abajo). Se llama ~1 vez por segundo.
    void RefreshWindows() {
        wins.Clear();
        Win.EnumWindows(enumProc, IntPtr.Zero);
        if (onWin) {
            int idx = -1;
            for (int i = 0; i < wins.Count; i++) if (wins[i].H == perchHwnd) { idx = i; break; }
            if (idx < 0 || Covered(idx, Left + SZ / 2)) onWin = false;   // desaparecio o la tapa otra ventana: cae
        }
    }

    // Esta tapado el borde superior de la ventana idx en la columna x por una ventana mas alta?
    bool Covered(int idx, int x) {
        int top = wins[idx].R.Top;
        for (int j = 0; j < idx; j++) if (wins[j].R.Contains(x, top)) return true;
        return false;
    }

    bool PerchValid(out Rectangle r) {
        return WinState(perchHwnd, out r) && WinUsable(perchHwnd, r);
    }

    void SetPerch(int idx) {
        perchHwnd = wins[idx].H;
        Rectangle fr;
        perchRect = WinState(perchHwnd, out fr) ? fr : wins[idx].R;
        onWin = true; jumping = false;
    }

    // Ventana cuyo borde superior cruza los pies (prevFeet -> newFeet) en la columna ccx, o -1
    int FindLanding(int ccx, int prevFeet, int newFeet) {
        for (int i = 0; i < wins.Count; i++) {
            WinInfo w = wins[i];
            if (!w.Usable) continue;
            int top = w.R.Top;
            if (prevFeet <= top && newFeet >= top && ccx >= w.R.Left && ccx < w.R.Right && !Covered(i, ccx)) return i;
        }
        return -1;
    }

    int JumpTargetX(Rectangle r, int mx) {
        return mx < r.Left ? r.Left + W / 2 + 25 : r.Right - W / 2 - 25;
    }

    // Salto parabolico hacia una ventana cercana. Devuelve false si no hay ninguna.
    bool TryJump(int x, int y) {
        if (!winsOn || wins.Count == 0) return false;
        int feet = y + H, mx = x + W / 2;
        List<int> cand = new List<int>();
        for (int i = 0; i < wins.Count; i++) {
            WinInfo w = wins[i];
            if (!w.Usable || (onWin && w.H == perchHwnd) || w.R.Width < W + 60) continue;
            int gap = mx < w.R.Left ? w.R.Left - mx : (mx >= w.R.Right ? mx - w.R.Right : 0);
            int rise = feet - w.R.Top;
            if (gap < 30 || gap > 320 || rise > 260 || rise < -400) continue;
            if (Covered(i, JumpTargetX(w.R, mx))) continue;
            cand.Add(i);
        }
        if (cand.Count == 0) return false;
        Rectangle t = wins[cand[rand.Next(cand.Count)]].R;
        double tx = JumpTargetX(t, mx);
        double rs = feet - t.Top;
        double apex = Math.Max(rs, 0) + 30;
        double vy0 = -Math.Sqrt(2 * GRAV * apex);
        double T = -vy0 / GRAV + Math.Sqrt(2 * (apex - rs) / GRAV);
        double vxx = Math.Max(-30, Math.Min(30, (tx - mx) / T / 0.97));
        cx = mx; cy = y + H / 2.0; vx = vxx; vyA = vy0;
        phi = 0; phiV = 0; mode = Mode.Air; stepTop = null; onWin = false; jumping = true;
        dir = vxx > 0 ? 1 : -1; hopV = 0;
        return true;
    }

    // ---------- Claude parpadea en la barra de tareas (HSHELL_FLASH) ----------
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        try { shellMsg = Win.RegisterWindowMessage("SHELLHOOK"); Win.RegisterShellHookWindow(Handle); } catch { }
    }

    protected override void OnHandleDestroyed(EventArgs e) {
        try { Win.DeregisterShellHookWindow(Handle); } catch { }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m) {
        if (shellMsg != 0 && (uint)m.Msg == shellMsg && (m.WParam.ToInt64() & 0xFFFF) == 0x8006) OnFlash(m.LParam);   // HSHELL_FLASH
        base.WndProc(ref m);
    }

    void OnFlash(IntPtr h) {
        try {
            int now = Environment.TickCount;
            if (now - lastFlashMs < 15000 && lastFlashMs != 0) return;
            uint pid; Win.GetWindowThreadProcessId(h, out pid);
            if (pid == 0 || pid == ownPid) return;
            using (Process p = Process.GetProcessById((int)pid)) {
                if (!string.Equals(p.ProcessName, "claude", StringComparison.OrdinalIgnoreCase)) return;
            }
            if (Alert("¡Claude te ha respondido!", Mood.Feliz, 300)) lastFlashMs = now;
        } catch { }
    }

    void UpdateBubble() {
        if (bubbleTicks <= 0) return;
        Rectangle r;
        if (CurAngle() != 0) r = new Rectangle(Left + M, Top + M, W, H);
        else {
            bool ly = Lying();
            int top = Top + M + H - (Body.Length + 2) * PX - CurHop() + (ly ? 2 * PX : 0);
            r = new Rectangle(Left + M, top, W, (ly ? Body.Length : Body.Length + 2) * PX);
        }
        bubble.Follow(r, NearestArea(r.Left + r.Width / 2, r.Top + r.Height / 2));
    }

    // Ultima entrada REAL del usuario: ignora la que genera el Nudge de "Teams en verde"
    void PollInput() {
        Win.LASTINPUTINFO info = new Win.LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);
        if (!Win.GetLastInputInfo(ref info)) return;
        uint t = info.dwTime;
        if (t == lastSeenInput) return;
        lastSeenInput = t;
        bool ours = hasNudge && Math.Abs(unchecked((int)(t - nudgeAt))) <= 300;
        if (!ours) lastRealInput = t;
    }

    void UpdateSleep() {
        PollInput();
        uint idle = (uint)Environment.TickCount - lastRealInput;
        int h = DateTime.Now.Hour;
        bool night = h >= 1 && h < 7;
        if (mood != Mood.Dormida) {
            if (!dragging && (idle >= SLEEP_MS || (night && idle >= NIGHT_IDLE_MS))) StartSleep();
        } else if (idle < 3000) Wake();
    }

    void StartSleep() {
        SetMood(Mood.Dormida, 0);
        chaseTicks = 0; walking = false; hopV = 0;
        HideBubble();
        if (mode != Mode.Floor && mode != Mode.Air) {   // pared o techo: se deja caer
            mode = Mode.Floor; vy = 0; stuck = false; stepTop = null;
        }
    }

    void Wake() {
        SetMood(Mood.Normal, 0);
        lastActMs = Environment.TickCount;
        hopV = 6.0;
        Say(FrasesDespertar);
    }

    // Pupilas: 1 pixel de sprite hacia el cursor (en coordenadas del sprite, deshaciendo la rotacion)
    void UpdateLook() {
        if (mood != Mood.Normal && mood != Mood.Aburrida) { lookX = 0; lookY = 0; return; }
        Point c = Cursor.Position;
        double dx = c.X - (Left + SZ / 2.0), dy = c.Y - (Top + SZ / 2.0);
        double a = -CurAngle() * Math.PI / 180.0;
        double rx = dx * Math.Cos(a) - dy * Math.Sin(a), ry = dx * Math.Sin(a) + dy * Math.Cos(a);
        double thr = PX * 2;
        lookX = rx > thr ? 1 : (rx < -thr ? -1 : 0);
        lookY = ry > thr ? 1 : (ry < -thr ? -1 : 0);
    }

    // Pixeles por tick al pasear segun el animo
    int MoodStep() {
        switch (mood) {
            case Mood.Feliz: return speed + 1;
            case Mood.Aburrida: return (tick & 1) == 0 ? speed : 0;      // la mitad de rapido
            case Mood.Mareada: return tick % 3 == 0 ? speed : 0;
        }
        return speed;
    }

    // Todo lo "mental": se llama una vez por tick
    void Think() {
        if (tick % 7 == 0) UpdateSleep();
        if (tick % 3 == 0) UpdateLook();
        int now = Environment.TickCount;

        if (moodTicks > 0 && --moodTicks == 0) SetMood(Mood.Normal, 0);

        if (mood == Mood.Normal && tick % 33 == 0 && now - lastActMs > BORED_MS) {
            SetMood(Mood.Aburrida, 0);
            nextSigh = 1500 + rand.Next(1500);
            Say(FrasesAburrida);
        }
        if (mood == Mood.Aburrida && --nextSigh <= 0) {
            nextSigh = 1500 + rand.Next(1500);   // ~50-100 s
            if (bubbleTicks <= 0) Say(FrasesAburrida);
        }

        if (winsOn && tick % 33 == 5 && (onWin || mood != Mood.Dormida)) RefreshWindows();
        if (tick % 33 == 11) Reminders();
        if (tick % 165 == 50) CheckPower();

        if (--nextChatter <= 0) {
            nextChatter = 6000 + rand.Next(9840);   // ~3-8 min
            if (mood != Mood.Dormida && !dragging && mode != Mode.Air && bubbleTicks <= 0) Say(FrasesAzar);
        }
        if (bubbleTicks > 0 && --bubbleTicks == 0) HideBubble();
    }

    // ---------- Bucle de animacion ----------
    void OnTick(object sender, EventArgs e) {
        tick++;
        if (tick % 100 == 0) RefreshAreas();   // por si cambia la barra de tareas
        Think();

        // arcoiris: cambia de tono cada 3 ticks (~11 veces/s, una vuelta completa cada ~11 s)
        if (rainbow && tick % 3 == 0) { hue = (hue + 4) % 360; SetBodyColor(FromHue(hue)); }

        // parpadeo
        if (blink > 0) blink--; else if (rand.Next(150) == 0) blink = 5;

        // saltito
        if (hopV != 0 || hop > 0) {
            hop += hopV; hopV -= 1.0;
            if (hop <= 0) { hop = 0; hopV = 0; }
            if (hop > 10) hop = 10;
        }

        if (dragging) { wobX = 0; StepDrag(); InvalidateIfChanged(); UpdateBubble(); return; }
        if (mode == Mode.Air) { wobX = 0; onWin = false; StepAir(); InvalidateIfChanged(); UpdateBubble(); return; }

        // dormida: no se queda pegada a paredes/techo
        if (mood == Mood.Dormida && mode != Mode.Floor) { mode = Mode.Floor; vy = 0; stuck = false; stepTop = null; }

        // posicion de la caja logica (sin margen); wobX es el tambaleo que se aplico en el tick anterior
        int x = Left + M - wobX, y = Top + M;
        int ccx = x + W / 2, midY = y + H / 2;
        Rectangle area = NearestArea(ccx, midY);
        int floor = area.Bottom - H;

        // sobre una ventana: la acompaña si se mueve; si desaparece, se minimiza o la tapan, cae
        if (mode != Mode.Floor) onWin = false;
        if (onWin) {
            Rectangle nr;
            if (!winsOn || !PerchValid(out nr) || ccx < nr.Left || ccx >= nr.Right) onWin = false;
            else { int pdx = nr.Left - perchRect.Left; x += pdx; ccx += pdx; perchRect = nr; floor = nr.Top - H; }
        }

        // a veces (raro) persigue unos segundos al cursor si esta cerca, en su mismo suelo
        if (chaseTicks <= 0 && tick % 33 == 0 && mode == Mode.Floor && y >= floor && !paused
            && (mood == Mood.Normal || mood == Mood.Feliz)) {
            Point cp = Cursor.Position;
            int adx = Math.Abs(cp.X - ccx);
            if (adx > W && adx < W * 8 && cp.X >= area.Left && cp.X < area.Right
                && cp.Y > area.Bottom - 3 * H && cp.Y <= area.Bottom && rand.Next(90) == 0)
                chaseTicks = 100 + rand.Next(100);
        }
        if (chaseTicks > 0) {
            if (mode != Mode.Floor || mood == Mood.Dormida || mood == Mood.Mareada || paused) chaseTicks = 0;
            else {
                chaseTicks--;
                int dxc = Cursor.Position.X - ccx;
                if (Math.Abs(dxc) < W / 2 + 4) { chaseTicks = 0; walking = false; }
                else { walking = true; dir = dxc > 0 ? 1 : -1; if (speed < 2) speed = 2; }
            }
        }

        // decisiones aleatorias
        if (chaseTicks <= 0) nextDecision--;
        if (nextDecision <= 0 && chaseTicks <= 0) {
            int r = rand.Next(10);
            if (mode == Mode.Floor) {
                if (r < 3) walking = false;
                else { walking = true; if (r < 5) dir = -dir; }
                // a veces salta a una ventana cercana
                if (winsOn && !paused && y >= floor && (mood == Mood.Normal || mood == Mood.Feliz) && rand.Next(4) == 0 && TryJump(x, y)) {
                    nextDecision = 60 + rand.Next(150);
                    InvalidateIfChanged(); UpdateBubble();
                    return;
                }
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
        bool moving = walking && !paused && mood != Mood.Dormida;
        int st = MoodStep();

        if (mode == Mode.Floor) {
            stepTop = null; stuck = false;
            if (y < floor) {
                // cayendo con gravedad
                vy += GRAV;
                int ny = Math.Min(floor, y + R(vy));
                if (!onWin && winsOn) {
                    // puede aterrizar sobre una ventana
                    int li = FindLanding(ccx, y + H, ny + H);
                    if (li >= 0) { SetPerch(li); floor = perchRect.Top - H; ny = floor; }
                }
                y = ny;
                if (y >= floor) { vy = 0; hopV = 4.0; }
            } else {
                if (y > floor) hopV = 6.0;
                y = floor;
                if (moving && onWin) {
                    // andando por el borde de una ventana: al llegar al final se da la vuelta o se tira
                    int newX = x + dir * st;
                    int cn = newX + W / 2;
                    if (cn < perchRect.Left || cn >= perchRect.Right) {
                        if (rand.Next(3) == 0) { onWin = false; x = newX; }
                        else dir = -dir;
                    } else x = newX;
                    NextFrame();
                } else if (moving) {
                    int newX = x + dir * st;
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
                y += vdir * st;
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
                int newX = x + dir * st;
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

        // tambaleo de la mareada (solo en el suelo)
        wobX = (mood == Mood.Mareada && mode == Mode.Floor) ? R(Math.Sin(tick * 0.4) * PX * 0.8) : 0;
        if ((x - M + wobX) != Left || (y - M) != Top) Location = new Point(x - M + wobX, y - M);
        InvalidateIfChanged();
        UpdateBubble();
    }

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.Run(new PetForm());
    }
}
