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
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
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
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

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

// Recordatorio personalizado, leido de recordatorios.txt (una linea = un aviso).
//   [dias] [fecha] (HH:MM | cada N [HH:MM-HH:MM]) | mensaje [| animo]
class Reminder {
    public string Key, Text;
    public Mood M = Mood.Normal;
    public int Every;                 // minutos entre avisos (0 = a hora fija)
    public int At = -1;               // minuto del dia del aviso a hora fija
    public int From = -1, To = -1;    // franja horaria para los "cada N" (minuto del dia)
    public int Days = 127;            // bit 0 = lunes ... bit 6 = domingo
    public int Year, Month, Day;      // fecha concreta (0 = cualquiera)
    public int Next, LastDay;         // proximo aviso (TickCount) y ultimo dia avisado

    static int ParseHM(string s) {
        int c = s.IndexOf(':'), h, m;
        if (c < 1 || !int.TryParse(s.Substring(0, c), out h) || !int.TryParse(s.Substring(c + 1), out m)) return -1;
        if (h < 0 || h > 23 || m < 0 || m > 59 || s.Length - c != 3) return -1;
        return h * 60 + m;
    }

    const string DayLetters = "lmxjvsd";

    // "L-V", "L,X,V", "S-D", "LXV"... -> mascara de bits; 0 si no es un dia
    static int ParseDays(string s) {
        if (s == "diario" || s == "todos") return 127;
        if (s == "laborables") return 31;
        if (s == "finde") return 96;
        int mask = 0;
        for (int i = 0; i < s.Length; i++) {
            char ch = s[i];
            if (ch == ',') continue;
            int d = DayLetters.IndexOf(ch);
            if (d < 0) return 0;
            if (i + 2 < s.Length && s[i + 1] == '-') {
                int e = DayLetters.IndexOf(s[i + 2]);
                if (e < 0) return 0;
                for (int k = d; ; k = (k + 1) % 7) { mask |= 1 << k; if (k == e) break; }
                i += 2;
            } else mask |= 1 << d;
        }
        return mask;
    }

    bool ParseDate(string s) {
        int y = 0, mo, d;
        string[] p;
        if (s.IndexOf('-') > 0) {             // 2026-12-24
            p = s.Split('-');
            if (p.Length != 3 || p[0].Length != 4 || !int.TryParse(p[0], out y) || !int.TryParse(p[1], out mo) || !int.TryParse(p[2], out d)) return false;
        } else if (s.IndexOf('/') > 0) {      // 24/12 o 24/12/2026
            p = s.Split('/');
            if (p.Length < 2 || p.Length > 3 || !int.TryParse(p[0], out d) || !int.TryParse(p[1], out mo)) return false;
            if (p.Length == 3 && !int.TryParse(p[2], out y)) return false;
        } else return false;
        if (mo < 1 || mo > 12 || d < 1 || d > 31) return false;
        Year = y; Month = mo; Day = d;
        return true;
    }

    // Devuelve null si la linea esta vacia o es un comentario; lanza FormatException si no se entiende.
    public static Reminder Parse(string line) {
        string t = line.Trim();
        if (t.Length == 0 || t[0] == '#') return null;
        string[] parts = t.Split('|');
        if (parts.Length < 2 || parts[1].Trim().Length == 0) throw new FormatException("falta el mensaje (separa con |)");
        Reminder r = new Reminder();
        r.Text = parts[1].Trim();
        if (parts.Length > 2) {
            string a = parts[2].Trim().ToLowerInvariant();
            if (a == "feliz") r.M = Mood.Feliz;
            else if (a == "aburrida" || a == "triste") r.M = Mood.Aburrida;
            else if (a == "mareada") r.M = Mood.Mareada;
            else if (a != "" && a != "normal") throw new FormatException("ánimo desconocido: " + parts[2].Trim());
        }
        string[] tok = parts[0].Trim().ToLowerInvariant().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        bool daysSet = false;
        for (int i = 0; i < tok.Length; i++) {
            string s = tok[i];
            if (s == "cada") {
                if (i + 1 >= tok.Length) throw new FormatException("falta el número de minutos tras 'cada'");
                string n = tok[++i]; int mult = 1;
                if (n.EndsWith("min")) n = n.Substring(0, n.Length - 3);
                else if (n.EndsWith("m")) n = n.Substring(0, n.Length - 1);
                else if (n.EndsWith("h")) { n = n.Substring(0, n.Length - 1); mult = 60; }
                int v;
                if (!int.TryParse(n, out v) || v <= 0) throw new FormatException("minutos no válidos: " + tok[i]);
                r.Every = v * mult;
                if (i + 1 < tok.Length && (tok[i + 1] == "min" || tok[i + 1] == "minutos")) i++;
                else if (i + 1 < tok.Length && (tok[i + 1] == "h" || tok[i + 1] == "horas" || tok[i + 1] == "hora")) { r.Every = v * 60; i++; }
                continue;
            }
            int dash = s.IndexOf('-');
            if (s.IndexOf(':') > 0 && dash > 0) {   // franja 09:00-18:00
                r.From = ParseHM(s.Substring(0, dash)); r.To = ParseHM(s.Substring(dash + 1));
                if (r.From < 0 || r.To < 0) throw new FormatException("franja no válida: " + s);
                continue;
            }
            if (s.IndexOf(':') > 0) {
                r.At = ParseHM(s);
                if (r.At < 0) throw new FormatException("hora no válida: " + s);
                continue;
            }
            if (char.IsDigit(s[0])) {
                if (!r.ParseDate(s)) throw new FormatException("fecha no válida: " + s);
                continue;
            }
            int dm = ParseDays(s);
            if (dm == 0) throw new FormatException("no entiendo '" + s + "'");
            r.Days = daysSet ? r.Days | dm : dm; daysSet = true;
        }
        if (r.Every > 0 && r.At >= 0) throw new FormatException("usa una hora fija o 'cada N', no las dos");
        if (r.Every == 0 && r.At < 0) throw new FormatException("falta la hora (HH:MM) o 'cada N'");
        if (r.From >= 0 && r.Every == 0) throw new FormatException("la franja HH:MM-HH:MM solo vale con 'cada N'");
        r.Key = string.Join(" ", tok) + "|" + r.Text + "|" + r.M;
        return r;
    }

    public bool DateOk(DateTime dt) {
        int dow = ((int)dt.DayOfWeek + 6) % 7;     // lunes = 0
        if ((Days & (1 << dow)) == 0) return false;
        if (Month != 0 && (dt.Month != Month || dt.Day != Day)) return false;
        if (Year != 0 && dt.Year != Year) return false;
        return true;
    }

    public bool InWindow(int min) {
        if (From < 0) return true;
        return From <= To ? min >= From && min < To : min >= From || min < To;   // la franja puede cruzar la medianoche
    }
}

// Ventana de otra app: caja visible y si se puede andar por su borde superior
class WinInfo { public IntPtr H; public Rectangle R; public bool Usable; }

enum Acc { Ninguno, Navidad, Bruja, Fiesta, Gafas }
enum Anim { None, Code, Yawn, Wave, Look }   // Look: solo Pokemon (mira a un lado y al otro)

// Particula (polvo = 0, estrellita = 1) en coordenadas de pantalla
class Particle { public double X, Y, VX, VY; public int Life, Max, Kind, Size; }

// Ventana overlay ligera y click-through donde se dibujan las particulas (la de la mascota no cambia, asi el click solo cuenta sobre el cuerpo)
class Fx : Form {
    public List<Particle> Items;
    readonly SolidBrush bDust = new SolidBrush(Color.FromArgb(205, 198, 185));
    readonly SolidBrush bStar = new SolidBrush(Color.FromArgb(250, 215, 70));

    public Fx() {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
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

    protected override void OnPaint(PaintEventArgs e) {
        if (Items == null) return;
        Graphics g = e.Graphics;
        for (int i = 0; i < Items.Count; i++) {
            Particle p = Items[i];
            int x = (int)Math.Round(p.X) - Left, y = (int)Math.Round(p.Y) - Top;
            if (p.Kind == 0) {
                int s = Math.Max(1, p.Size * p.Life / p.Max);
                g.FillRectangle(bDust, x - s / 2, y - s / 2, s, s);
            } else {
                int c = p.Size, h = c / 2;
                g.FillRectangle(bStar, x - h, y - h, c, c);
                g.FillRectangle(bStar, x - h - c, y - h, c, c);
                g.FillRectangle(bStar, x - h + c, y - h, c, c);
                g.FillRectangle(bStar, x - h, y - h - c, c, c);
                g.FillRectangle(bStar, x - h, y - h + c, c, c);
            }
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) { bDust.Dispose(); bStar.Dispose(); }
        base.Dispose(disposing);
    }
}

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
    int M = 31;      // margen para que quepa al girar y el portatil de "programar" a un lado (la parte transparente no capta clicks)
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

    // ---------- Aspecto: Claudio (por defecto) o cualquier Pokemon (sprite 2D Gen 5 de PokeAPI/sprites) ----------
    const int COLS = 14;                  // ancho logico de la mascota, en "pixeles"
    string skin = "claudio";              // "claudio" | "og" (Claudio original, ojos de pixel) | "pokemon"
    int pokeId = 693; string pokeName = "Clawitzer";
    bool useSprite;                       // true = se dibuja el sprite en vez de Claudio
    bool shiny = true;                    // variante: shiny o normal
    Bitmap petImg;                        // sprite original, recortado al contenido
    Bitmap spr, sprFlip; int sprPX = -1;  // sprite ya escalado al tamano actual (normal y volteado)
    Rectangle sprHead;                    // cabeza del sprite (coordenadas de spr); vacia = no lleva gorro
    bool lastFlip;
    readonly ToolStripMenuItem miSkin, miSkinClaudio, miSkinOg, miSkinPick, miNormal, miShiny, miShow;
    // Pokemon usados recientemente (id + nombre), el mas reciente primero; se guardan en config.txt
    readonly List<KeyValuePair<int, string>> recents = new List<KeyValuePair<int, string>>();
    const int MAX_RECENTS = 3;
    NotifyIcon tray;                      // icono junto al reloj: menu rapido y mostrar/ocultar
    bool petHidden;
    static readonly string SpriteDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MascotaClaude", "sprites");
    const string SpriteUrl = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/";
    bool rainbow = false;        // modo arcoiris: el color va rotando
    double hue = 0;               // tono actual del arcoiris (0..360)
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MascotaClaude");
    static readonly string ConfigPath = Path.Combine(DataDir, "config.txt");
    // recordatorios.txt va en la carpeta de la mascota (junto a mascota.vbs y mascota.exe)
    static readonly string RemPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordatorios.txt");
    static readonly string OldRemPath = Path.Combine(DataDir, "recordatorios.txt");   // ubicacion de la version anterior

    // ---------- Animo, bocadillos, sueno y mirada ----------
    // Frases de los bocadillos: edita estas listas a tu gusto.
    static readonly string[] FrasesAzar = {
        "¿Qué tal vas?", "Aquí sigo, vigilando.", "¿Has bebido agua?", "Estírate un poquito.",
        "Me gusta este rincón.", "¿Hacemos algo?", "Qué buen día para pasear.", "Tic tac, tic tac...",
        "Hoy te veo con ganas.", "¿Un descanso?", "Eres lo mejor :3", "Cuídate mucho, ¿vale?",
        "Te quiero un poquito ^^", "Aquí estoy para lo que quieras.", "¿Me das un mimito?", "Hoy lo estás haciendo genial.",
        "<3", "Qué ganas de verte sonreír ^^", "Estoy orgullosa de ti :3", "Pase lo que pase, aquí me tienes."
    };
    static readonly string[] FrasesLanzada = { "¡Wiiii!", "¡Aaaah!", "¡Voy volando!", "¡Cuidado!", "¡Otra vez!", "¡Aaah, mi pancita!", "¡Wiii, otra vez, otra vez!" };
    static readonly string[] FrasesDespertar = { "¡Buenas!", "Mmm... ya estoy aquí.", "¿Ya has vuelto?", "Qué bien he dormido.", "Hola otra vez.", "*bostezo* Buenos días ^^", "¡Has vuelto! Qué alegría :3" };
    static readonly string[] FrasesCaricia = { "Qué a gusto...", "Me gusta eso.", "Jiji, cosquillas.", "Sigue, sigue.", "Soy feliz.", "Mimitos... uwu", "Me derrito :3", "Eres un amor <3", "Más, más ^^" };
    static readonly string[] FrasesMareo = { "Uf, qué mareo...", "Todo da vueltas...", "Necesito sentarme.", "Ay, mi cabeza.", "Me hago un ovillito...", "Estoy viendo estrellitas..." };
    static readonly string[] FrasesAburrida = { "*suspiro*", "Qué aburrimiento...", "¿Hay alguien ahí?", "Hmpf...", "Juega conmigo, anda...", "Te echo de menos :(" };
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
    const int POMO_WORK_MIN = 25, POMO_BREAK_MIN = 5;
    readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
    readonly List<WinInfo> wins = new List<WinInfo>();
    readonly Win.EnumProc enumProc;
    bool winsOn = true, onWin = false, jumping = false;
    IntPtr perchHwnd; Rectangle perchRect;            // ventana sobre la que anda
    ToolStripMenuItem miWins, miRem, miPomo;
    bool pomoOn, pomoWork;                            // el pomodoro no se guarda (empieza parado)
    int pomoEnd;
    List<Reminder> rems = new List<Reminder>();       // recordatorios de recordatorios.txt
    DateTime remStamp = DateTime.MinValue;            // fecha de modificacion del archivo ya cargado
    string remCreateErr;                              // error al crear el archivo al arrancar (se muestra al poder)
    bool remLoaded;                                   // ya se cargo una vez (las siguientes recargas se anuncian)
    bool oldWater, oldStretch, oldGym;                // opciones de versiones anteriores (para crear el archivo)
    bool bubbleImportant;
    bool lowWarned; PowerLineStatus lastLine = PowerLineStatus.Unknown;
    uint shellMsg; int lastFlashMs;

    // ---------- Animaciones, particulas y accesorios ----------
    const int MAXP = 20;                              // maximo de particulas a la vez
    static readonly string[] AccKeys = { "auto", "ninguno", "navidad", "bruja", "fiesta", "gafas" };
    static readonly string[] AccNames = { "Automático", "Ninguno", "Gorro de Navidad", "Sombrero de bruja", "Gorro de fiesta", "Gafas de sol" };
    Anim anim = Anim.None; int animT, animLen; bool pendingSleep;
    int lastNearMs;
    readonly List<Particle> parts = new List<Particle>();
    Fx fx;
    int accMode = -1;                                 // -1 = automatico (por fecha); si no, el valor de Acc
    Acc accNow = Acc.Ninguno;
    readonly ToolStripMenuItem[] miAccItems = new ToolStripMenuItem[6];
    ToolStripMenuItem miAcc, miDo, miDoCode;
    readonly SolidBrush bRed = new SolidBrush(Color.FromArgb(205, 40, 50));
    readonly SolidBrush bWhite = new SolidBrush(Color.FromArgb(245, 245, 245));
    readonly SolidBrush bPurple = new SolidBrush(Color.FromArgb(75, 45, 115));
    readonly SolidBrush bGold = new SolidBrush(Color.FromArgb(245, 200, 60));
    // portatil (de perfil, gris claro) de la animacion "programar"; codeSide = lado hacia el que mira (1 der., -1 izq.)
    readonly SolidBrush bLapBase = new SolidBrush(Color.FromArgb(196, 198, 205));
    readonly SolidBrush bLapScr = new SolidBrush(Color.FromArgb(232, 233, 240));
    int codeSide = 1;
    readonly SolidBrush[] bCode = {
        new SolidBrush(Color.FromArgb(140, 235, 170)), new SolidBrush(Color.FromArgb(120, 195, 255)),
        new SolidBrush(Color.FromArgb(255, 175, 205)), new SolidBrush(Color.FromArgb(255, 230, 120)) };
    readonly SolidBrush bPink =new SolidBrush(Color.FromArgb(230, 70, 150));
    readonly SolidBrush bBlue = new SolidBrush(Color.FromArgb(70, 150, 230));
    Acc lastAcc = Acc.Ninguno; int lastAnimK = -1;

    // Caché de las areas de trabajo (se refresca al cambiar las pantallas y cada pocos segundos)
    Rectangle[] areas;
    // Lo ultimo que se dibujo: solo se repinta si cambia
    float lastAngle = float.NaN; int lastHop = -1; string lastLegs = null; int lastEyes; int lastPX = -1;
    int lastSq = int.MinValue, lastShv; bool lastSurp;
    int sqT, shiverT, nextIdleAnim = 6000;          // squash al aterrizar, sacudida, cuenta atras de animaciones idle
    double scX = 1, scY = 1, anX, anY;              // escala del sprite al pintar (squash & stretch) y su ancla (los pies)
    Mood lastMood = Mood.Normal; int lastLX, lastLY, lastAnim = -1; bool lastLying;

    public PetForm() {
        W = COLS * PX; H = W; SZ = W + 2 * M;

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
        miShow = new ToolStripMenuItem("Mostrar mascota");
        miShow.Checked = true;
        miShow.Click += delegate { SetPetVisible(petHidden); };
        menu.Items.Add(miShow);

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
        // Con un Pokemon, "Color" ofrece sus dos variantes en vez de la paleta
        miNormal = new ToolStripMenuItem("Normal");
        miNormal.Click += delegate { SetShiny(false); };
        miShiny = new ToolStripMenuItem("Shiny");
        miShiny.Click += delegate { SetShiny(true); };
        miColor.DropDownItems.Add(miNormal);
        miColor.DropDownItems.Add(miShiny);
        menu.Items.Add(miColor);

        miSkin = new ToolStripMenuItem("Aspecto");
        miSkinClaudio = new ToolStripMenuItem("Claudio");
        miSkinClaudio.Click += delegate { SetSkin("claudio"); };
        miSkinOg = new ToolStripMenuItem("Claudio OG");
        miSkinOg.Click += delegate { SetSkin("og"); };
        miSkinPick = new ToolStripMenuItem("Elegir Pokémon...");
        miSkinPick.Click += delegate { PickPokemon(); };
        miSkin.DropDownItems.Add(miSkinClaudio);
        miSkin.DropDownItems.Add(miSkinOg);
        miSkin.DropDownItems.Add(new ToolStripSeparator());
        miSkin.DropDownItems.Add(miSkinPick);   // los recientes se insertan entre Claudio y el separador (RebuildSkinMenu)
        menu.Items.Add(miSkin);

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

        miDo = new ToolStripMenuItem("Hacer...");
        miDo.DropDownItems.Add("Saludar", null, delegate { DoAnim(Anim.Wave, 80); });
        miDo.DropDownItems.Add("Bostezar", null, delegate { DoAnim(Anim.Yawn, 70); });
        miDoCode = (ToolStripMenuItem)miDo.DropDownItems.Add("Programar", null, delegate { DoAnim(Anim.Code, 360); });
        menu.Items.Add(miDo);

        miAcc = new ToolStripMenuItem("Accesorios");
        for (int i = 0; i < miAccItems.Length; i++) {
            int idx = i;
            miAccItems[i] = new ToolStripMenuItem(AccNames[i]);
            miAccItems[i].Click += delegate {
                accMode = idx - 1;           // 0 -> automatico (-1); 1.. -> Acc.Ninguno, Navidad...
                UpdateAcc(); UpdateAccChecks(); Invalidate(); SaveConfig();
            };
            miAcc.DropDownItems.Add(miAccItems[i]);
        }
        menu.Items.Add(miAcc);

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
        miRem.DropDownItems.Add(miPomo);
        miRem.DropDownItems.Add(new ToolStripSeparator());
        miRem.DropDownItems.Add("Editar recordatorios.txt...", null, delegate {
            if (!EnsureRemFile()) return;
            try { Process.Start("notepad.exe", "\"" + RemPath + "\""); } catch { }
        });
        miRem.DropDownItems.Add("Abrir carpeta", null, delegate {
            if (!EnsureRemFile()) return;
            try { Process.Start("explorer.exe", "/select,\"" + RemPath + "\""); } catch { }
        });
        menu.Items.Add(miRem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, delegate { timer.Stop(); SetTeams(false); Close(); });
        ContextMenuStrip = menu;

        // Icono junto al reloj: mismo menu; click izquierdo = mostrar / ocultar la mascota
        tray = new NotifyIcon();
        tray.Text = "Mascota Claude";
        tray.ContextMenuStrip = menu;
        tray.MouseClick += delegate(object s, MouseEventArgs ev) { if (ev.Button == MouseButtons.Left) SetPetVisible(petHidden); };
        tray.Visible = true;

        LoadConfig();
        enumProc = OnEnumWindow;
        UpdateAcc(); UpdateAccChecks();
        lastNearMs = Environment.TickCount;
        miBubbles.Checked = bubblesOn;
        miWins.Checked = winsOn;
        EnsureRemFile();   // se carga en el primer tick (ya con la ventana visible para poder avisar de errores)
        lastActMs = Environment.TickCount; lastRealInput = (uint)Environment.TickCount;
        nextChatter = 6000 + rand.Next(9840);       // ~3-8 min (30 ms por tick)
        SetBodyColor(baseColor);
        UpdateColorChecks();
        ApplySkin();

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
            tray.Visible = false; tray.Dispose();
            if (petImg != null) petImg.Dispose();
            if (spr != null) spr.Dispose();
            if (sprFlip != null) sprFlip.Dispose();
            bubble.Dispose();
            if (fx != null) fx.Dispose();
            bRed.Dispose(); bWhite.Dispose(); bPurple.Dispose(); bGold.Dispose(); bPink.Dispose(); bBlue.Dispose();
            bLapBase.Dispose(); bLapScr.Dispose();
            foreach (SolidBrush cb in bCode) cb.Dispose();
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
        miSkin.DropDown.Renderer = r;
        miRem.DropDown.Renderer = r;
        miAcc.DropDown.Renderer = r;
        miDo.DropDown.Renderer = r;
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
                else if (k == "aspecto") skin = v == "claudio" || v == "og" ? v : "pokemon";   // "clawitzer" = valor antiguo
                else if (k == "pokemon") { int pid; if (int.TryParse(v, out pid) && pid > 0) pokeId = pid; }
                else if (k == "nombre" && v.Length > 0) pokeName = v;
                else if (k == "recientes") {
                    recents.Clear();
                    foreach (string part in v.Split(',')) {
                        int c = part.IndexOf(':'); int rid;
                        if (c <= 0 || !int.TryParse(part.Substring(0, c), out rid) || rid <= 0) continue;
                        string rn = part.Substring(c + 1).Trim();
                        if (rn.Length > 0 && recents.Count < MAX_RECENTS && !recents.Exists(r => r.Key == rid))
                            recents.Add(new KeyValuePair<int, string>(rid, rn));
                    }
                }
                else if (k == "variante") shiny = v != "normal";
                else if (k == "bocadillos") bubblesOn = v != "0";
                else if (k == "ventanas") winsOn = v != "0";
                else if (k == "agua") oldWater = v == "1";        // antiguas opciones del menu: solo se usan
                else if (k == "estirar") oldStretch = v == "1";   // al crear recordatorios.txt por primera vez
                else if (k == "gimnasio") oldGym = v == "1";
                else if (k == "accesorio") { int ai = Array.IndexOf(AccKeys, v); if (ai >= 0) accMode = ai - 1; }
            }
        } catch { }
    }

    void SaveConfig() {
        try {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ConfigPath,
                "color=" + (baseColor.ToArgb() & 0xFFFFFF).ToString("X6") + "\r\n" +
                "arcoiris=" + (rainbow ? "1" : "0") + "\r\n" +
                "aspecto=" + skin + "\r\n" +
                "pokemon=" + pokeId + "\r\n" +
                "nombre=" + pokeName + "\r\n" +
                "recientes=" + string.Join(",", recents.ConvertAll(r => r.Key + ":" + r.Value).ToArray()) + "\r\n" +
                "variante=" + (shiny ? "shiny" : "normal") + "\r\n" +
                "bocadillos=" + (bubblesOn ? "1" : "0") + "\r\n" +
                "ventanas=" + (winsOn ? "1" : "0") + "\r\n" +
                "accesorio=" + AccKeys[accMode + 1] + "\r\n");
        } catch { }
    }

    // ---------- Aspecto ----------
    void SetSkin(string s) { skin = s; ApplySkin(); SaveConfig(); }
    void SetShiny(bool s) { shiny = s; ApplySkin(); SaveConfig(); }

    void ApplySkin() {
        if (skin == "pokemon") {
            string err;
            Bitmap img = GetPokeSprite(pokeId, shiny, out err);
            if (img != null) { if (petImg != null) petImg.Dispose(); petImg = img; sprPX = -1; }
        }
        useSprite = skin == "pokemon" && petImg != null;
        if (!useSprite && skin == "pokemon") skin = "claudio";
        if (useSprite || recents.Count == 0) AddRecent(pokeId, pokeName);   // Pokemon por defecto si aun no hay ninguno
        RebuildSkinMenu();
        // Claudio OG no tiene animaciones ni accesorios; los Pokemon si (sin programar ni gafas)
        miDo.Visible = skin != "og";        // los Pokemon tambien saludan y bostezan...
        miDoCode.Visible = !useSprite;      // ...pero no programan
        miAcc.Visible = skin != "og";       // Claudio OG sigue sin accesorios
        miAccItems[5].Visible = !useSprite; // "Gafas de sol": los Pokemon no
        UpdateTrayIcon();
        // "Color": paleta para Claudio, Normal/Shiny para los Pokemon
        foreach (ToolStripItem it in miColor.DropDownItems)
            it.Visible = (it == miNormal || it == miShiny) ? useSprite : !useSprite;
        miNormal.Checked = !shiny; miShiny.Checked = shiny;
        BuildSprites();
        Invalidate();
    }

    void AddRecent(int id, string name) {
        recents.RemoveAll(r => r.Key == id);
        recents.Insert(0, new KeyValuePair<int, string>(id, name));
        if (recents.Count > MAX_RECENTS) recents.RemoveRange(MAX_RECENTS, recents.Count - MAX_RECENTS);
        PruneSprites();
    }

    // La cache de sprites (%LOCALAPPDATA%\MascotaClaude\sprites) solo guarda los Pokemon recientes:
    // los demas se borran (si vuelves a elegir uno, se descarga otra vez)
    void PruneSprites() {
        foreach (string sub in new string[] { "normal", "shiny" }) {
            try {
                string dir = Path.Combine(SpriteDir, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "*.png")) {
                    int id;
                    if (int.TryParse(Path.GetFileNameWithoutExtension(f), out id) && !recents.Exists(r => r.Key == id))
                        try { File.Delete(f); } catch { }
                }
            } catch { }
        }
    }

    // Menu "Aspecto": Claudio, Claudio OG, Pokemon recientes, separador, "Elegir Pokemon..."
    void RebuildSkinMenu() {
        ToolStripItemCollection items = miSkin.DropDownItems;
        while (items.Count > 2 && !(items[2] is ToolStripSeparator)) items.RemoveAt(2);
        miSkinClaudio.Checked = skin == "claudio";
        miSkinOg.Checked = skin == "og";
        for (int i = 0; i < recents.Count; i++) {
            int id = recents[i].Key; string name = recents[i].Value;
            ToolStripMenuItem mi = new ToolStripMenuItem(name);
            mi.Checked = useSprite && id == pokeId;
            mi.Click += delegate { SetPokemon(id, name, shiny); };
            items.Insert(2 + i, mi);
        }
    }

    // ---------- Pokemon: sprites de PokeAPI/sprites (Gen 5, 2D), con cache en %LOCALAPPDATA%\MascotaClaude\sprites ----------
    static byte[] Download(string url, int timeoutMs) {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.UserAgent = "MascotaClaude"; rq.Timeout = timeoutMs; rq.ReadWriteTimeout = timeoutMs;
        using (WebResponse rs = rq.GetResponse())
        using (Stream st = rs.GetResponseStream())
        using (MemoryStream ms = new MemoryStream()) {
            byte[] buf = new byte[16384]; int n;
            while ((n = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
            return ms.ToArray();
        }
    }

    // Sprite del Pokemon (cache -> descarga). null + err si no se pudo.
    static Bitmap GetPokeSprite(int id, bool shiny, out string err) {
        err = null;
        try {
            string dir = Path.Combine(SpriteDir, shiny ? "shiny" : "normal");
            string f = Path.Combine(dir, id + ".png");
            if (!File.Exists(f)) {
                byte[] data = Download(SpriteUrl + (shiny ? "shiny/" : "") + id + ".png", 6000);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(f, data);
            }
            using (FileStream fs = File.OpenRead(f)) {
                Bitmap b = LoadSprite(fs);
                if (b == null) { err = "El sprite está vacío."; File.Delete(f); }
                return b;
            }
        } catch (Exception ex) { err = ex.Message; return null; }
    }

    class PokeItem {
        public int Id; public string Name;
        public override string ToString() { return "#" + Id.ToString("000") + "  " + Name; }
    }

    // Lista de Pokemon (id + nombre) de la PokeAPI, guardada en cache la primera vez
    static List<PokeItem> LoadDex() {
        string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MascotaClaude", "pokedex.txt");
        List<PokeItem> list = new List<PokeItem>();
        try {
            string[] lines;
            if (File.Exists(f)) lines = File.ReadAllLines(f);
            else {
                string json = System.Text.Encoding.UTF8.GetString(Download("https://pokeapi.co/api/v2/pokemon?limit=1025", 10000));
                List<string> l = new List<string>();
                foreach (Match m in Regex.Matches(json, "\"name\":\"([^\"]+)\",\"url\":\"[^\"]*/pokemon/(\\d+)/\""))
                    l.Add(m.Groups[2].Value + "|" + m.Groups[1].Value);
                if (l.Count == 0) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllLines(f, l.ToArray());
                lines = l.ToArray();
            }
            foreach (string line in lines) {
                int bar = line.IndexOf('|'); int id;
                if (bar < 0 || !int.TryParse(line.Substring(0, bar), out id)) continue;
                string n = line.Substring(bar + 1);
                list.Add(new PokeItem { Id = id, Name = char.ToUpper(n[0]) + n.Substring(1) });
            }
            return list.Count > 0 ? list : null;
        } catch { return null; }
    }

    void PickPokemon() {
        List<PokeItem> dex;
        Cursor old = Cursor.Current; Cursor.Current = Cursors.WaitCursor;
        try { dex = LoadDex(); } finally { Cursor.Current = old; }
        if (dex == null) {
            MessageBox.Show("No he podido descargar la lista de Pokémon (¿sin conexión?).", "Mascota", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using (Form f = new Form())
        using (TextBox tb = new TextBox())
        using (ListBox lb = new ListBox())
        using (CheckBox cs = new CheckBox())
        using (Button ok = new Button())
        using (Button cancel = new Button()) {
            f.Text = "Elegir Pokémon"; f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen; f.ClientSize = new Size(300, 410);
            f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false; f.TopMost = true;
            tb.SetBounds(10, 10, 280, 24);
            lb.SetBounds(10, 42, 280, 290);
            cs.SetBounds(10, 342, 280, 24); cs.Text = "Shiny"; cs.Checked = shiny;
            ok.SetBounds(130, 374, 75, 28); ok.Text = "Aplicar"; ok.DialogResult = DialogResult.OK;
            cancel.SetBounds(215, 374, 75, 28); cancel.Text = "Cancelar"; cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Controls.AddRange(new Control[] { tb, lb, cs, ok, cancel });
            Action fill = delegate {
                string q = tb.Text.Trim().ToLowerInvariant().TrimStart('#');
                lb.BeginUpdate(); lb.Items.Clear();
                foreach (PokeItem it in dex)
                    if (q.Length == 0 || it.Name.ToLowerInvariant().Contains(q) || it.Id.ToString().StartsWith(q) || it.Id.ToString("000").StartsWith(q))
                        lb.Items.Add(it);
                lb.EndUpdate();
                if (lb.Items.Count > 0) lb.SelectedIndex = 0;
            };
            tb.TextChanged += delegate { fill(); };
            lb.DoubleClick += delegate { if (lb.SelectedItem != null) ok.PerformClick(); };
            fill();
            for (int i = 0; i < lb.Items.Count; i++) if (((PokeItem)lb.Items[i]).Id == pokeId) { lb.SelectedIndex = i; break; }
            f.Shown += delegate { tb.Focus(); };
            if (f.ShowDialog() != DialogResult.OK || lb.SelectedItem == null) return;
            PokeItem sel = (PokeItem)lb.SelectedItem;
            SetPokemon(sel.Id, sel.Name, cs.Checked);
        }
    }

    // Descarga (si hace falta) y aplica; si no hay sprite se queda como estaba
    void SetPokemon(int id, string name, bool sh) {
        string err; Bitmap img;
        Cursor old = Cursor.Current; Cursor.Current = Cursors.WaitCursor;
        try { img = GetPokeSprite(id, sh, out err); } finally { Cursor.Current = old; }
        if (img == null) {
            MessageBox.Show("No he podido cargar el sprite de " + name + (sh ? " (shiny)" : "") + ".\r\n" + err, "Mascota", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        img.Dispose();   // ApplySkin lo vuelve a leer de la cache
        pokeId = id; pokeName = name; shiny = sh; skin = "pokemon";
        ApplySkin(); SaveConfig();
    }

    // ---------- Icono junto al reloj y mostrar / ocultar ----------
    void SetPetVisible(bool v) {
        petHidden = !v;
        miShow.Checked = v;
        if (v) { Show(); TopMost = true; }
        else { HideBubble(); Hide(); }
    }

    void UpdateTrayIcon() {
        if (tray == null) return;
        try {
            using (Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(b)) {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                if (useSprite && petImg != null) {
                    double k = Math.Min(30.0 / petImg.Width, 30.0 / petImg.Height);
                    if (k >= 1) k = Math.Floor(k); else g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    int w = (int)Math.Round(petImg.Width * k), h = (int)Math.Round(petImg.Height * k);
                    g.DrawImage(petImg, new Rectangle((32 - w) / 2, (32 - h) / 2, w, h), 0, 0, petImg.Width, petImg.Height, GraphicsUnit.Pixel);
                } else {
                    using (SolidBrush br = new SolidBrush(Orange))
                        for (int y = 0; y < Body.Length + 2; y++) {
                            string row = y < Body.Length ? Body[y] : LegsA;
                            for (int x = 0; x < row.Length; x++) if (row[x] != '.') g.FillRectangle(br, 2 + x * 2, 7 + y * 2, 2, 2);
                        }
                }
                IntPtr h2 = b.GetHicon();
                using (Icon tmp = Icon.FromHandle(h2)) tray.Icon = (Icon)tmp.Clone();
                Win.DestroyIcon(h2);
            }
        } catch { }
    }

    // Lee un PNG, lo recorta al contenido y deja la transparencia "dura"
    // (la ventana usa un color clave, asi que no puede haber pixeles semitransparentes).
    static Bitmap LoadSprite(Stream s) {
        try {
            {
                if (s == null) return null;
                using (Bitmap src = new Bitmap(s)) {
                    int x0 = src.Width, y0 = src.Height, x1 = -1, y1 = -1;
                    for (int y = 0; y < src.Height; y++)
                        for (int x = 0; x < src.Width; x++)
                            if (src.GetPixel(x, y).A >= 128) {
                                if (x < x0) x0 = x; if (x > x1) x1 = x;
                                if (y < y0) y0 = y; if (y > y1) y1 = y;
                            }
                    if (x1 < 0) return null;
                    Bitmap crop = new Bitmap(x1 - x0 + 1, y1 - y0 + 1, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(crop))
                        g.DrawImage(src, new Rectangle(0, 0, crop.Width, crop.Height), x0, y0, crop.Width, crop.Height, GraphicsUnit.Pixel);
                    HardAlpha(crop);
                    return crop;
                }
            }
        } catch { return null; }
    }

    static void HardAlpha(Bitmap b) {
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++) {
                Color c = b.GetPixel(x, y);
                if (c.A == 0 || c.A == 255) continue;
                b.SetPixel(x, y, c.A >= 128 ? Color.FromArgb(255, c) : Color.FromArgb(0, 0, 0, 0));
            }
    }

    // Escala el sprite al tamano actual: con enteros (nitido) cuando cabe, sin pasarse nunca de la caja logica
    void BuildSprites() {
        if (!useSprite || sprPX == PX) return;
        double k = COLS * (double)PX / Math.Max(petImg.Width, petImg.Height);
        if (k >= 1) k = Math.Floor(k);
        int w = Math.Max(1, (int)Math.Round(petImg.Width * k)), h = Math.Max(1, (int)Math.Round(petImg.Height * k));
        Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b)) {
            g.InterpolationMode = k >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(petImg, new Rectangle(0, 0, w, h), 0, 0, petImg.Width, petImg.Height, GraphicsUnit.Pixel);
        }
        if (k < 1) HardAlpha(b);
        Bitmap f = (Bitmap)b.Clone();
        f.RotateFlip(RotateFlipType.RotateNoneFlipX);
        if (spr != null) spr.Dispose();
        if (sprFlip != null) sprFlip.Dispose();
        spr = b; sprFlip = f; sprPX = PX;
        sprHead = HeadBox(spr);
        if (!HatFits(spr, sprHead)) sprHead = Rectangle.Empty;   // orejas o cuernos arriba: sin gorro
    }

    // Los sprites de Pokemon miran a la izquierda: se voltea cuando camina hacia la derecha
    // (segun el modo, "derecha" del sprite apunta a un sitio distinto de la pantalla).
    bool Flip() {
        bool f = BaseFlip();
        return (useSprite && anim == Anim.Look && LookFlipped(animT, animLen)) ? !f : f;   // mirar alrededor
    }
    bool BaseFlip() {
        if (Free) return dir > 0;
        switch (mode) {
            case Mode.WallR: return vdir < 0;
            case Mode.WallL: return vdir > 0;
            case Mode.Ceiling: return dir < 0;
        }
        return dir > 0;
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
    float CurAngle() {
        if (Free) return (float)(phi * 180.0 / Math.PI);
        if (!useSprite) return ModeAngle();
        // Pokemon: se contonea al andar y se balancea al saludar
        bool moving = WaddleMoving(walking, paused, anim, mood == Mood.Dormida, vy);
        return ModeAngle() + WaddleAngle(moving, mode == Mode.Floor, tick) + (anim == Anim.Wave ? WaveTilt(animT) : 0);
    }

    // ---------- Vida de los Pokemon (funciones puras, ver tests\MascotaTests.cs) ----------
    // "Programar" no tiene sentido sin portatil: un Pokemon mira alrededor
    internal static Anim SpriteAnim(Anim a) { return a == Anim.Code ? Anim.Look : a; }
    // Contoneo al andar por el suelo: se inclina a un lado y al otro con cada paso
    // Andar a saltitos de lado a lado: un saltito cada WALK_HOP ticks, inclinandose a un lado y al otro
    const int WALK_HOP = 12;   // ~0,36 s por saltito
    internal static float WaddleAngle(bool moving, bool onFloor, int t) {
        if (!moving || !onFloor) return 0;
        return (t / WALK_HOP) % 2 == 0 ? -5f : 5f;
    }
    // Altura del saltito (arco) en el tick t; px = altura maxima
    internal static int WalkHopPx(int t, int px) {
        return (int)Math.Round(px * Math.Sin(Math.PI * (t % WALK_HOP) / WALK_HOP));
    }
    // Solo se contonea si de verdad esta dando pasos (mismas condiciones que avanzan "frame")
    internal static bool WaddleMoving(bool walking, bool paused, Anim anim, bool asleep, double vy) {
        return walking && !paused && anim == Anim.None && !asleep && vy == 0;
    }
    // Cuanto bajar el gorro para que su punta no se salga por arriba de la ventana
    internal static int HatDrop(double hatTop) { return hatTop < 0 ? (int)Math.Ceiling(-hatTop) : 0; }
    // Angulo que decide si el bocadillo sigue al cuerpo o a la caja: la inclinacion de los Pokemon no cuenta
    internal static float BubbleAngle(bool free, float freeAngle, float modeAngle) { return free ? freeAngle : modeAngle; }
    // Balanceo al saludar (grados enteros para no repintar cada tick)
    internal static float WaveTilt(int animT) { return (float)Math.Round(6 * Math.Sin(animT * 0.35)); }
    // Bostezo: se estira hacia arriba y al final suelta el aire achatandose un poco
    internal static double YawnSquash(int animT, int animLen) {
        int end = animLen - 20;
        if (animT >= animLen) return 0;
        if (animT < 10) return -0.15 * animT / 10.0;
        if (animT < end) return -0.15;
        return 0.10 * (animLen - animT) / 20.0;
    }
    // Mirar alrededor: el tercio central mira hacia el otro lado
    internal static bool LookFlipped(int animT, int animLen) { return animT >= animLen / 3 && animT < 2 * animLen / 3; }
    // Saludo de Pokemon: dos saltitos
    internal static bool WaveHopAt(int animT) { return animT == 2 || animT == 26; }
    // Tumbada durmiendo en el suelo (sin patas, el cuerpo apoyado)
    bool Lying() { return mood == Mood.Dormida && mode == Mode.Floor && !dragging && vy == 0; }
    // Rebotito al andar (1 px o poco mas, en el paso con patas B)
    int Bob() { return (walking && !paused && anim == Anim.None && mood != Mood.Dormida && mode == Mode.Floor && !Free && frame == 1) ? Math.Max(1, PX / 5) : 0; }
    int CurHop() {
        if (Free || Lying()) return 0;
        // Pokemon: anda a saltitos en vez del rebotito de Claudio
        if (useSprite && mode == Mode.Floor && WaddleMoving(walking, paused, anim, mood == Mood.Dormida, vy))
            return (int)Math.Round(hop) + WalkHopPx(tick, PX);
        return (int)Math.Round(hop) + Bob();
    }

    // Parpadeo en 3 fases: 0 = abiertos, 1 = entornados, 2 = cerrados
    int BlinkPhase() {
        if (blink <= 0) return 0;
        return (blink >= 7 || blink <= 2) ? 1 : 2;
    }

    // Cara de sorpresa mientras la agarras o vuela lanzada
    bool Surprised() { return (dragging || (mode == Mode.Air && !jumping)) && mood != Mood.Mareada && mood != Mood.Dormida; }

    // Se sacude al soltarla en el suelo: desplazamiento horizontal alterno
    int ShiverOff() { return shiverT > 0 ? (((shiverT / 2) % 2 == 0) ? 1 : -1) * Math.Max(2, PX / 2) : 0; }

    // Squash & stretch: > 0 achatada, < 0 estirada. Incluye la respiracion idle.
    double SqS() {
        if (Free) return 0;
        double s = 0.2 * sqT / 6.0;
        if (hopV > 1 && hop > 0) s -= 0.10;
        if (useSprite && anim == Anim.Yawn) s += YawnSquash(animT, animLen);
        if (!useSprite && anim == Anim.Code && animLen > 0) {   // se agacha hacia el portatil mientras teclea
            int u = Math.Min(animT, animLen - animT);
            s += 0.22 * Math.Min(1.0, Math.Max(0.0, (u - 14) / 14.0));
        }
        s += mood == Mood.Dormida ? 0.03 * Math.Sin(tick * 0.045) : 0.022 * Math.Sin(tick * 0.07);
        return Math.Max(-0.2, Math.Min(0.25, s));
    }
    int SqKey() { return (int)Math.Round(SqS() * H); }   // solo repinta si cambia al menos 1 px

    // Cara segun el animo y la animacion en curso
    Mood FaceMood() {
        if (anim == Anim.Wave && (mood == Mood.Normal || mood == Mood.Aburrida)) return Mood.Feliz;
        if (anim == Anim.Yawn && (mood == Mood.Normal || mood == Mood.Aburrida)) return Mood.Dormida;
        return mood;
    }
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
        if (!walking || paused || anim == Anim.Code) return LegsA;
        return frame == 0 ? LegsA : LegsB;
    }

    // Repinta solo si ha cambiado algo visible (moverse la ventana no necesita repintar)
    void InvalidateIfChanged() {
        float angle = CurAngle(); int hp = CurHop(); string legs = CurLegs(); int eyes = BlinkPhase();
        if (angle != lastAngle || hp != lastHop || !ReferenceEquals(legs, lastLegs) || eyes != lastEyes || PX != lastPX
            || mood != lastMood || lookX != lastLX || lookY != lastLY || AnimPhase() != lastAnim || Lying() != lastLying
            || AnimKey() != lastAnimK || accNow != lastAcc || SqKey() != lastSq || ShiverOff() != lastShv || Surprised() != lastSurp
            || (useSprite && Flip() != lastFlip))
            Invalidate();
    }

    // Fotograma de la animacion actual (0 = ninguna); cambia solo cuando hay que repintar
    int AnimKey() {
        switch (anim) {
            case Anim.Wave: return 10 + (animT / 4) % 2;
            case Anim.Code: return 40 + animT / 3;      // teclas y lineas de codigo cambian cada 3 ticks
            case Anim.Yawn: return 30 + YawnStage();
            case Anim.Look: return 50 + (LookFlipped(animT, animLen) ? 1 : 0);
        }
        return 0;
    }

    int YawnStage() { return (animT < 10 || animT >= animLen - 12) ? 0 : 1; }   // boca pequena / grande

    // Pixeles del sprite escalados (squash & stretch) con bordes redondeados a entero: sin huecos entre celdas
    void Block(Graphics g, Brush b, int offX, int offY, int x0, int y0, int w, int h) {
        int xa = (int)Math.Round(anX + (offX + x0 * PX - anX) * scX), xb = (int)Math.Round(anX + (offX + (x0 + w) * PX - anX) * scX);
        int ya = (int)Math.Round(anY + (offY + y0 * PX - anY) * scY), yb = (int)Math.Round(anY + (offY + (y0 + h) * PX - anY) * scY);
        g.FillRectangle(b, xa, ya, xb - xa, yb - ya);
    }

    void Row(Graphics g, int offX, int offY, int x0, int x1, int y, Brush b) {
        Block(g, b, offX, offY, x0, y, x1 - x0 + 1, 1);
    }

    // Celda (k, r) del portatil: k = columnas hacia fuera desde el costado del cuerpo, r = filas sobre el suelo (pixeles de pantalla, sin escalar)
    void LCell(Graphics g, Brush b, int ex, int k, int r) {
        int x = codeSide > 0 ? ex + k * PX : ex - (k + 1) * PX;
        g.FillRectangle(b, x, (int)Math.Round(anY) - (r + 1) * PX, PX, PX);
    }

    // Programar: busca en el costado, saca un portatil cerrado, lo abre (3 pasos), teclea agachada; al terminar lo cierra y lo guarda.
    // u = ticks hasta el extremo mas cercano de la animacion, asi el final es la secuencia inversa del principio.
    void DrawLaptop(Graphics g) {
        int u = Math.Min(animT, animLen - animT);
        int gap = Math.Max(1, PX / 3);
        int ex = (int)Math.Round(anX + codeSide * 7 * PX * scX) + codeSide * gap;
        if (u < 12) {   // palpando el costado, buscando el portatil
            LCell(g, bBody, ex, 0, (animT / 3) % 2 == 0 ? 4 : 3);
            return;
        }
        int stage = u < 18 ? 0 : (u < 22 ? 1 : (u < 26 ? 2 : 3));   // 0 cerrado, 1-2 abriendo, 3 abierto
        for (int k = 0; k <= 3; k++) LCell(g, bLapBase, ex, k, 0);   // base fina
        if (stage >= 1) LCell(g, bLapScr, ex, 3, 1);                  // pantalla inclinada hacia atras
        if (stage >= 2) LCell(g, bLapScr, ex, 4, 2);
        if (stage >= 3) {
            LCell(g, bLapScr, ex, 4, 3);
            LCell(g, bCode[(animT / 4) % 4], ex, 3, 2);               // brillo de la pantalla
            LCell(g, bCode[(animT / 4 + 1) % 4], ex, 3, 3);
            LCell(g, bBody, ex, 0, 1);                                // bracito sobre la base...
            LCell(g, bBody, ex, (animT / 3) % 2 == 0 ? 1 : 2, 1);     // ...con la manita tecleando
        }
    }

    // Programar (portatil), bostezar (boca) y saludar (bracito a la derecha)
    void DrawAnim(Graphics g, int offX, int offY) {
        if (anim == Anim.None || (mood != Mood.Normal && mood != Mood.Aburrida)) return;
        switch (anim) {
            case Anim.Wave:   // bracito que cambia de inclinacion
                Dot(g, offX, offY, 14, 3, bBody); Dot(g, offX, offY, 14, 2, bBody);
                if ((animT / 4) % 2 == 0) { Dot(g, offX, offY, 13, 1, bBody); Dot(g, offX, offY, 13, 0, bBody); }
                else { Dot(g, offX, offY, 15, 1, bBody); Dot(g, offX, offY, 15, 0, bBody); }
                break;
            case Anim.Code:
                DrawLaptop(g);   // (en pixeles de pantalla; usa el ancla y la escala del sprite)
                break;
            case Anim.Yawn:
                if (YawnStage() == 0) Row(g, offX, offY, 6, 7, 5, bBlack);
                else { Row(g, offX, offY, 5, 8, 4, bBlack); Row(g, offX, offY, 5, 8, 5, bBlack); Row(g, offX, offY, 6, 7, 6, bBlack); }
                break;
        }
    }

    // ---------- Accesorios por fecha ----------
    internal static Acc AutoAcc(DateTime d) {
        int m = d.Month, day = d.Day;
        if ((m == 12 && day == 31) || (m == 1 && day == 1)) return Acc.Fiesta;   // prioridad
        if (m == 12 || (m == 1 && day <= 6)) return Acc.Navidad;
        if (m == 10 && day >= 25) return Acc.Bruja;
        if (m == 7 || m == 8) return Acc.Gafas;
        return Acc.Ninguno;
    }

    void UpdateAcc() { accNow = accMode < 0 ? AutoAcc(DateTime.Now) : (Acc)accMode; }

    void UpdateAccChecks() {
        for (int i = 0; i < miAccItems.Length; i++) miAccItems[i].Checked = (i == accMode + 1);
    }

    // Gorros como datos: {x0, x1, fila, color} en columnas 0-13 del cuerpo de Claudio (fila -1 = justo encima de la cabeza).
    // Colores: 0 blanco, 1 rojo, 2 morado, 3 dorado, 4 rosa, 5 azul. Los comparten Claudio y los Pokemon.
    static readonly int[][] HatNavidad = {
        new[] { 2, 11, -1, 0 }, new[] { 3, 10, -2, 1 }, new[] { 4, 9, -3, 1 },
        new[] { 5, 9, -4, 1 }, new[] { 7, 9, -5, 1 }, new[] { 10, 11, -5, 0 } };
    static readonly int[][] HatBruja = {
        new[] { 0, 13, -1, 2 }, new[] { 4, 9, -2, 2 }, new[] { 6, 7, -2, 3 }, new[] { 5, 8, -3, 2 },
        new[] { 6, 8, -4, 2 }, new[] { 7, 8, -5, 2 }, new[] { 8, 9, -6, 2 } };
    static readonly int[][] HatFiesta = {
        new[] { 4, 9, -1, 4 }, new[] { 5, 8, -2, 5 }, new[] { 6, 7, -3, 4 }, new[] { 6, 7, -4, 3 } };

    internal static int[][] HatShape(Acc a) {
        switch (a) {
            case Acc.Navidad: return HatNavidad;
            case Acc.Bruja: return HatBruja;
            case Acc.Fiesta: return HatFiesta;
        }
        return null;   // Ninguno y Gafas no son gorros
    }

    Brush HatBrush(int c) {
        switch (c) {
            case 0: return bWhite;
            case 1: return bRed;
            case 2: return bPurple;
            case 3: return bGold;
            case 4: return bPink;
        }
        return bBlue;
    }

    // Un Pokemon no lleva gafas: no se sabe donde tiene los ojos
    internal static Acc SpriteAcc(Acc a) { return a == Acc.Gafas ? Acc.Ninguno : a; }

    // "Cabeza" del sprite: franja superior (al menos 1/6 del alto) y su ancho opaco
    internal static Rectangle HeadBox(Bitmap b) {
        int top = -1;
        for (int y = 0; y < b.Height && top < 0; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A >= 128) { top = y; break; }
        if (top < 0) return Rectangle.Empty;
        int band = Math.Min(Math.Max(2, b.Height / 6), b.Height - top);
        int x0 = b.Width, x1 = -1;
        for (int y = top; y < top + band; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A >= 128) { if (x < x0) x0 = x; if (x > x1) x1 = x; }
        return new Rectangle(x0, top, x1 - x0 + 1, band);
    }

    // Tamano de celda del gorro segun el ancho de la cabeza (entre PX/2 y PX)
    internal static int HatCell(int headWidthPx, int px) {
        return Math.Max(Math.Max(2, px / 2), Math.Min(px, (int)Math.Round(headWidthPx / 10.0)));
    }

    internal static int HatCol(int col, bool flip) { return flip ? 13 - col : col; }

    // Si lo alto del sprite son orejas, cuernos o pinzas, no se pone gorro. Se detecta como "salientes finos":
    // desde lo alto se tarda mas de 1/8 del alto en llegar a una fila maciza (un tramo opaco >= 30% del ancho).
    // Calibrado con Pikachu, Eevee, Umbreon y Clawitzer (sin gorro) y Jigglypuff, Snorlax, Bulbasaur (con gorro).
    internal static bool HatFits(Bitmap b, Rectangle head) {
        if (head.IsEmpty) return false;
        int minRun = (int)Math.Ceiling(0.3 * b.Width);
        for (int y = head.Y; y < b.Height; y++) {
            int run = 0, best = 0;
            for (int x = 0; x < b.Width; x++) {
                if (b.GetPixel(x, y).A >= 128) { run++; if (run > best) best = run; }
                else run = 0;
            }
            if (best >= minRun) return y - head.Y <= b.Height / 8;
        }
        return false;
    }

    // Gorro sobre un Pokemon: centrado en su cabeza, con el ala encajada una celda y espejado si mira al otro lado.
    // dst = rectangulo donde se ha dibujado el sprite (ya con squash y saltito), asi el gorro lo sigue.
    void DrawHat(Graphics g, Rectangle dst, bool flip) {
        int[][] shape = HatShape(SpriteAcc(accNow));
        if (shape == null || sprHead.IsEmpty) return;
        int cell = HatCell(sprHead.Width, PX);
        double cw = cell * scX, ch = cell * scY;
        int hx = flip ? spr.Width - sprHead.Right : sprHead.X;
        double cx = dst.X + (hx + sprHead.Width / 2.0) * scX;
        double topY = dst.Y + sprHead.Y * scY + ch;
        int minRow = 0;
        foreach (int[] r in shape) if (r[2] < minRow) minRow = r[2];
        topY += HatDrop(topY + minRow * ch);   // Pokemon altos (o en pleno salto): se encaja mas en vez de cortarse
        foreach (int[] r in shape) {
            int a = HatCol(r[0], flip), b = HatCol(r[1], flip);
            if (a > b) { int t = a; a = b; b = t; }
            int xa = (int)Math.Round(cx + (a - 7) * cw), xb = (int)Math.Round(cx + (b - 6) * cw);
            int ya = (int)Math.Round(topY + r[2] * ch), yb = (int)Math.Round(topY + (r[2] + 1) * ch);
            g.FillRectangle(HatBrush(r[3]), xa, ya, xb - xa, yb - ya);
        }
    }

    // Se dibujan en coordenadas del sprite (fila 0 = tope de la cabeza), asi giran y escalan con ella
    void DrawAcc(Graphics g, int offX, int offY) {
        int[][] hat = HatShape(accNow);
        if (hat != null)
            foreach (int[] r in hat) Row(g, offX, offY, r[0], r[1], r[2], HatBrush(r[3]));
        switch (accNow) {
            case Acc.Gafas:
                Row(g, offX, offY, 2, 5, 2, bBlack); Row(g, offX, offY, 2, 5, 3, bBlack); Row(g, offX, offY, 2, 5, 4, bBlack);
                Row(g, offX, offY, 8, 11, 2, bBlack); Row(g, offX, offY, 8, 11, 3, bBlack); Row(g, offX, offY, 8, 11, 4, bBlack);
                Row(g, offX, offY, 6, 7, 2, bBlack);
                Dot(g, offX, offY, 2, 2, bWhite); Dot(g, offX, offY, 8, 2, bWhite);   // brillo
                break;
        }
    }

    void Dot(Graphics g, int offX, int offY, int sx, int sy, Brush b) {
        Block(g, b, offX, offY, sx, sy, 1, 1);
    }

    // La boca solo aparece en acciones: "o" al agarrarla/lanzarla y sonrisa abierta al saludar (el bostezo la dibuja en DrawAnim)
    void DrawMouth(Graphics g, int offX, int offY) {
        if (Surprised()) { Row(g, offX, offY, 6, 7, 4, bBlack); Row(g, offX, offY, 6, 7, 5, bBlack); return; }
        if (anim == Anim.Wave && FaceMood() == Mood.Feliz) { Row(g, offX, offY, 5, 8, 4, bBlack); Row(g, offX, offY, 6, 7, 5, bBlack); }
    }

    // Ojos segun el animo. Columnas de los ojos: 4 y 9; filas 2-3 (coordenadas del sprite).
    // Ojos grandes (2x2) con brillo. Bloques de ojo: columnas 3-4 y 9-10, filas 2-3. bl = fase del parpadeo.
    void DrawEyes(Graphics g, int offX, int offY, int bl) {
        int[] cols = { 3, 9 };
        Mood em = FaceMood();
        bool surp = Surprised();
        foreach (int ex in cols) {
            if (surp) {              // sorpresa: ojos altos (2x3) con brillo
                Block(g, bBlack, offX, offY, ex + lookX, 1 + lookY, 2, 4);
                Dot(g, offX, offY, ex + lookX, 1 + lookY, bWhite);
                continue;
            }
            switch (em) {
                case Mood.Dormida:       // cerrados: rayita de 4
                    Row(g, offX, offY, ex - 1, ex + 2, 4, bBlack);
                    break;
                case Mood.Feliz:         // ^ ^
                    Row(g, offX, offY, ex, ex + 1, 3, bBlack); Dot(g, offX, offY, ex - 1, 4, bBlack); Dot(g, offX, offY, ex + 2, 4, bBlack);
                    break;
                case Mood.Mareada: {     // espiral: anillo con un hueco que gira
                    int gap = (tick / 3) % 8;
                    int[] rx = { -1, 0, 1, 1, 1, 0, -1, -1 };
                    int[] ry = { -1, -1, -1, 0, 1, 1, 1, 0 };
                    for (int i = 0; i < 8; i++)
                        if (i != gap) Dot(g, offX, offY, ex + 1 + rx[i], 4 + ry[i], bBlack);
                    break;
                }
                case Mood.Aburrida:      // entornados: solo la fila de abajo
                    if (bl == 2) Row(g, offX, offY, ex - 1, ex + 2, 4, bBlack);
                    else Row(g, offX, offY, ex + lookX, ex + lookX + 1, 4, bBlack);
                    break;
                default:
                    if (bl == 0) {
                        Block(g, bBlack, offX, offY, ex + lookX, 2 + lookY, 2, 3);
                        Dot(g, offX, offY, ex + lookX, 2 + lookY, bWhite);   // brillo
                    } else if (bl == 1) Row(g, offX, offY, ex + lookX, ex + lookX + 1, 4, bBlack);
                    else Row(g, offX, offY, ex - 1, ex + 2, 4, bBlack);
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
        float angle = CurAngle(); int hp = CurHop(); string legs = CurLegs(); int eyes = BlinkPhase();
        bool lying = Lying();
        double sq = SqS(); int shv = ShiverOff();
        lastAngle = angle; lastHop = hp; lastLegs = legs; lastEyes = eyes; lastPX = PX;
        lastMood = mood; lastLX = lookX; lastLY = lookY; lastAnim = AnimPhase(); lastLying = lying;
        lastAnimK = AnimKey(); lastAcc = accNow;
        lastSq = (int)Math.Round(sq * H); lastShv = shv; lastSurp = Surprised();

        if (angle != 0) {
            g.TranslateTransform(SZ / 2f, SZ / 2f); g.RotateTransform(angle); g.TranslateTransform(-SZ / 2f, -SZ / 2f);
        }
        int offY = M + H - (Body.Length + 2) * PX - hp + (lying ? 2 * PX : 0);
        int offX = M + shv;
        int rows = lying ? Body.Length : Body.Length + 2;   // tumbada: sin patas
        // squash & stretch anclado en los pies
        scY = 1 - sq; scX = 1 + 0.55 * sq; anX = offX + 7 * PX; anY = offY + rows * PX;
        if (useSprite) {
            // Pokemon: mismo anclaje en los pies y mismo squash & stretch; sin ojos/boca de Claudio, con su gorro si le cabe
            bool flip = Flip(); lastFlip = flip;
            BuildSprites();
            Bitmap img = flip ? sprFlip : spr;
            int bob = 0;   // el "paso" ya lo dan los saltitos (CurHop)
            int dw = (int)Math.Round(img.Width * scX), dh = (int)Math.Round(img.Height * scY);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            Rectangle dst = new Rectangle((int)Math.Round(anX - dw / 2.0), (int)Math.Round(anY) - dh - bob, dw, dh);
            g.DrawImage(img, dst);
            DrawHat(g, dst, flip);
            DrawDecor(g, offX, offY, lying);
            return;
        }
        for (int y = 0; y < rows; y++) {
            string row = y < Body.Length ? Body[y] : legs;
            for (int x = 0; x < row.Length; x++) {
                if (row[x] == '.') continue;
                Block(g, bBody, offX, offY, x, y, 1, 1);
            }
        }
        if (skin == "og") {
            // Claudio original: los ojos son los pixeles 'E' del cuerpo, en negro salvo al parpadear
            if (eyes == 0)
                for (int y = 0; y < Body.Length; y++)
                    for (int x = 0; x < Body[y].Length; x++)
                        if (Body[y][x] == 'E') Block(g, bBlack, offX, offY, x, y, 1, 1);
            DrawDecor(g, offX, offY, lying);
            return;
        }
        DrawEyes(g, offX, offY, eyes);
        DrawMouth(g, offX, offY);
        DrawAnim(g, offX, offY);
        DrawAcc(g, offX, offY);
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
            jumping = false; onWin = false; anim = Anim.None; pendingSleep = false;
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
        W = COLS * newPX;
        H = W;
        M = (int)Math.Ceiling(W * 0.34) + 2;
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
            // saluda con el bracito (en vez del saltito); si no puede (aire, pared...) da el saltito
            StartAnim(Anim.Wave, 80);
            if (anim != Anim.Wave) hopV = 7.0;
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
            bool jl = jumping;
            if ((Math.Abs(vyA) < 4 && Math.Abs(vx) < 3) || (jumping && li >= 0)) {
                // aterriza: se pone de pie y sigue paseando
                mode = Mode.Floor; phi = 0; phiV = 0; vy = 0; hopV = 5.0;
                if (li >= 0) SetPerch(li);
                jumping = false;
                if (jl) SpawnDust(cx, fl, 4); else shiverT = 20;   // al soltarla en el suelo, se sacude
                if (vx > 0) dir = 1; else if (vx < 0) dir = -1;
                vx = 0; vyA = 0;
            } else {
                if (Math.Abs(vyA) > 6) SpawnDust(cx, fl, 6);
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
        if (pomoOn && now - pomoEnd >= 0) {
            if (pomoWork) { pomoWork = false; pomoEnd = now + POMO_BREAK_MIN * 60000; Alert("¡Descanso de " + POMO_BREAK_MIN + " min! Levántate un poco.", Mood.Feliz, 300); }
            else { pomoWork = true; pomoEnd = now + POMO_WORK_MIN * 60000; Alert("Se acabó el descanso: ¡otros " + POMO_WORK_MIN + " min de trabajo!", Mood.Normal, 0); }
            return;
        }
        DateTime dt = DateTime.Now;
        int min = dt.Hour * 60 + dt.Minute;
        int dayKey = dt.Year * 1000 + dt.DayOfYear;
        foreach (Reminder r in rems) {
            if (r.Every > 0) {
                if (now - r.Next < 0) continue;
                r.Next = now + r.Every * 60000;
                if (!r.DateOk(dt) || !r.InWindow(min)) continue;   // fuera de dia/franja: se salta este aviso
            } else {
                // a hora fija: si estaba dormida o con otro aviso, aun sale en los 10 min siguientes
                int late = (min - r.At + 1440) % 1440;
                if (late >= 10 || r.LastDay == dayKey || !r.DateOk(min < r.At ? dt.AddDays(-1) : dt)) continue;
                r.LastDay = dayKey;
            }
            Alert(r.Text, r.M, r.M == Mood.Normal ? 0 : 300);
            return;
        }
    }

    // ---------- Recordatorios personalizados (recordatorios.txt) ----------
    const string RemHelp =
        "# Recordatorios de la mascota\r\n" +
        "# ----------------------------\r\n" +
        "# Una línea por aviso:   CUÁNDO | MENSAJE | ÁNIMO (opcional)\r\n" +
        "# Guarda el archivo y la mascota lo recarga sola en unos segundos.\r\n" +
        "# Las líneas que empiezan por # se ignoran (quita la # para activar un ejemplo).\r\n" +
        "#\r\n" +
        "# CUÁNDO puede ser:\r\n" +
        "#   15:30                  todos los días a las 15:30\r\n" +
        "#   cada 45                cada 45 minutos (también vale: cada 45 min, cada 2h)\r\n" +
        "#   cada 60 09:00-18:00    cada hora, solo dentro de esa franja\r\n" +
        "#   y delante, si quieres, los días y/o una fecha:\r\n" +
        "#     L M X J V S D        días sueltos o rangos: L-V, S-D, L,X,V\r\n" +
        "#     laborables / finde   atajos para L-V y S-D\r\n" +
        "#     24/12  o  2026-12-24 una fecha concreta (sin año = todos los años)\r\n" +
        "#\r\n" +
        "# ÁNIMO: normal (por defecto), feliz (da un saltito), aburrida o mareada.\r\n" +
        "#\r\n" +
        "# Los avisos salen aunque silencies los bocadillos. Si está dormida, los de hora fija\r\n" +
        "# esperan hasta 10 minutos a que despierte; los de 'cada N' se saltan esa vez.\r\n" +
        "# (El Pomodoro sigue en el menú Recordatorios.)\r\n" +
        "\r\n";

    // Crea recordatorios.txt si no existe. Si no puede, lo dice en un bocadillo y devuelve false.
    bool EnsureRemFile() {
        try {
            if (File.Exists(RemPath)) return true;
            if (File.Exists(OldRemPath)) { File.Copy(OldRemPath, RemPath); return true; }   // trae el de la version anterior
            string c = RemHelp +
                "L-V 15:00 | ¡Hora de salir! | feliz\r\n" +
                (oldGym ? "" : "#") + "L-V 15:30 | ¡Hora de ir al gimnasio! | feliz\r\n" +
                (oldWater ? "" : "#") + "cada 60 | Toca beber agua.\r\n" +
                (oldStretch ? "" : "#") + "cada 50 | Pausa activa: levántate y estírate un poco.\r\n" +
                "\r\n" +
                "# Más ejemplos:\r\n" +
                "#laborables 13:45 | ¡A comer! | feliz\r\n" +
                "#cada 20 09:00-19:00 | Regla 20-20-20: mira algo lejano 20 segundos.\r\n" +
                "#V 17:00 | ¡Viernes! Apunta lo pendiente para el lunes.\r\n" +
                "#24/12 18:00 | ¡Feliz Nochebuena! | feliz\r\n";
            File.WriteAllText(RemPath, c, new System.Text.UTF8Encoding(true));   // con BOM: el Bloc de notas respeta las tildes
            return true;
        } catch (Exception e) {
            remCreateErr = "No he podido crear " + RemPath + ": " + e.Message;
            if (Visible) Alert(remCreateErr, Mood.Mareada, 120);
            return false;
        }
    }

    // Lee el texto en UTF-8; si no lo es (guardado como ANSI), con la codificacion del sistema
    static string[] ReadLinesAnyEncoding(string path) {
        byte[] b = File.ReadAllBytes(path);
        string txt;
        try { txt = new System.Text.UTF8Encoding(false, true).GetString(b); }
        catch (System.Text.DecoderFallbackException) { txt = System.Text.Encoding.Default.GetString(b); }
        if (txt.Length > 0 && txt[0] == '﻿') txt = txt.Substring(1);
        return txt.Replace("\r\n", "\n").Split('\n');
    }

    // Recarga si el archivo ha cambiado. announce = decir que se han actualizado.
    void LoadReminders(bool announce) {
        DateTime stamp;
        string[] lines;
        try {
            if (!File.Exists(RemPath)) {
                if (rems.Count > 0) rems = new List<Reminder>();
                remStamp = DateTime.MinValue;
                if (remCreateErr != null) { Alert(remCreateErr, Mood.Mareada, 120); remCreateErr = null; }   // fallo al arrancar
                return;
            }
            stamp = File.GetLastWriteTimeUtc(RemPath);
            if (stamp == remStamp) return;
            lines = ReadLinesAnyEncoding(RemPath);
        } catch { return; }   // p. ej. el editor lo tiene bloqueado mientras guarda: se reintenta luego
        remStamp = stamp; remLoaded = true;
        // conservar el estado (proximo aviso, ya avisado hoy) de las lineas que no han cambiado
        Dictionary<string, Reminder> old = new Dictionary<string, Reminder>();
        foreach (Reminder r in rems) old[r.Key] = r;
        List<Reminder> list = new List<Reminder>();
        string err = null;
        int now = Environment.TickCount;
        for (int i = 0; i < lines.Length; i++) {
            Reminder r;
            try { r = Reminder.Parse(lines[i]); }
            catch (FormatException e) { if (err == null) err = "recordatorios.txt, línea " + (i + 1) + ": " + e.Message; continue; }
            if (r == null) continue;
            Reminder prev;
            if (old.TryGetValue(r.Key, out prev)) { r.Next = prev.Next; r.LastDay = prev.LastDay; old.Remove(r.Key); }
            else r.Next = now + r.Every * 60000;
            list.Add(r);
        }
        rems = list;
        if (err != null) Alert("No entiendo " + err, Mood.Mareada, 120);
        else if (announce) Alert("Recordatorios actualizados (" + list.Count + " activos).", Mood.Feliz, 120);
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
        if (BubbleAngle(Free, CurAngle(), ModeAngle()) != 0) r = new Rectangle(Left + M, Top + M, W, H);
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
        bool want = idle >= SLEEP_MS || (night && idle >= NIGHT_IDLE_MS);
        if (mood != Mood.Dormida) {
            if (!want || idle < 3000) pendingSleep = false;
            else if (!dragging && !pendingSleep) {
                // antes de dormirse, bosteza (al acabar la animacion se tumba)
                StartAnim(Anim.Yawn, 70);
                if (anim == Anim.Yawn) pendingSleep = true; else StartSleep();
            }
        } else if (idle < 3000) Wake();
    }

    // Empieza una animacion solo si esta de pie en el suelo/ventana y sin sobresaltos
    void StartAnim(Anim a, int len) {
        if (mode != Mode.Floor || dragging || mood == Mood.Dormida || mood == Mood.Mareada || vy != 0) return;
        if (useSprite) { a = SpriteAnim(a); if (a == Anim.Look) len = 90; }
        anim = a; animT = 0; animLen = len;
        if (a == Anim.Code) { codeSide = dir >= 0 ? 1 : -1; lookX = codeSide; lookY = 1; }
    }




    // Desde el menu "Hacer...": si esta dormida, solo la despierta (que ya saluda)
    void DoAnim(Anim a, int len) {
        if (mood == Mood.Dormida) { Wake(); return; }
        StartAnim(a, len);
    }

    void SpawnDust(double x, double y, int n) {
        for (int i = 0; i < n && parts.Count < MAXP; i++) {
            int sgn = i % 2 == 0 ? -1 : 1;
            Particle p = new Particle();
            p.X = x + sgn * W * 0.25; p.Y = y - 2;
            p.VX = sgn * (0.6 + rand.NextDouble() * 2.4); p.VY = -rand.NextDouble() * 1.2;
            p.Life = p.Max = 14 + rand.Next(8); p.Kind = 0; p.Size = Math.Max(3, PX * 2 / 3);
            parts.Add(p);
        }
    }

    void SpawnStar() {
        Particle p = new Particle();
        p.X = Left + SZ / 2.0 + (rand.NextDouble() - 0.5) * W * 0.8;
        p.Y = Top + M + H - 9 * PX + PX;
        p.VX = (rand.NextDouble() - 0.5) * 0.8; p.VY = -0.5;
        p.Life = p.Max = 28; p.Kind = 1; p.Size = Math.Max(2, PX / 2);
        parts.Add(p);
    }

    // Particulas: solo hay ventana overlay mientras queda alguna viva
    void UpdateParticles() {
        if (mood == Mood.Mareada && tick % 5 == 0 && parts.Count < MAXP) SpawnStar();
        for (int i = parts.Count - 1; i >= 0; i--) {
            Particle p = parts[i];
            p.X += p.VX; p.Y += p.VY;
            if (p.Kind == 0) { p.VX *= 0.93; p.VY *= 0.95; } else p.VX *= 0.98;
            if (--p.Life <= 0) parts.RemoveAt(i);
        }
        if (parts.Count == 0) { if (fx != null && fx.Visible) fx.Hide(); return; }
        if (fx == null) { fx = new Fx(); fx.Items = parts; }
        int e = SZ / 2;
        Rectangle b = new Rectangle(Left - e, Top - e, SZ + 2 * e, SZ + 2 * e);
        if (fx.Bounds != b) fx.Bounds = b;
        if (!fx.Visible) fx.Show();
        fx.Invalidate();
    }

    void StartSleep() {
        anim = Anim.None; pendingSleep = false;
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
        StartAnim(Anim.Wave, 70);
    }

    // Pupilas: 1 pixel de sprite hacia el cursor (en coordenadas del sprite, deshaciendo la rotacion)
    void UpdateLook() {
        if (mood != Mood.Normal && mood != Mood.Aburrida) { lookX = 0; lookY = 0; return; }
        if (anim == Anim.Code) { lookX = codeSide; lookY = 1; return; }   // mira al portatil
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

        // animaciones (programar / bostezar / saludar)
        if (anim != Anim.None) {
            if (dragging || mode != Mode.Floor || mood == Mood.Dormida || mood == Mood.Mareada) { anim = Anim.None; pendingSleep = false; }
            else if (++animT >= animLen) {
                anim = Anim.None;
                if (pendingSleep) { pendingSleep = false; StartSleep(); }
            } else if (useSprite && anim == Anim.Wave && WaveHopAt(animT) && hop <= 0) {
                hopV = 4;   // saludo de Pokemon: saltito
            }
        } else if (mode == Mode.Floor && !dragging && vy == 0 && chaseTicks <= 0 && (mood == Mood.Normal || mood == Mood.Aburrida)) {
            // bostezo o "programar" espontaneo cada ~3-5 min (para un momento lo que estuviera haciendo);
            // aburrida bosteza mucho mas a menudo
            if (mood == Mood.Aburrida && tick % 33 == 17 && rand.Next(10) == 0) StartAnim(Anim.Yawn, 70);
            else if (--nextIdleAnim <= 0) {
                if (rand.Next(2) == 0) StartAnim(Anim.Yawn, 70); else StartAnim(Anim.Code, 300 + rand.Next(150));   // programar: ~9-14 s
                nextIdleAnim = anim != Anim.None ? 5940 + rand.Next(3960) : 66;   // si no pudo, reintenta en ~2 s
            }
        }
        if (sqT > 0) sqT--;
        if (shiverT > 0) shiverT--;
        // saltitos de alegria ocasionales
        if (mood == Mood.Feliz && tick % 33 == 21 && mode == Mode.Floor && vy == 0 && hop == 0 && !dragging && rand.Next(3) == 0) hopV = 6.0;
        // saluda si el cursor se acerca tras un rato sin verlo
        if (tick % 11 == 3) {
            Point cp = Cursor.Position;
            double ddx = cp.X - (Left + SZ / 2.0), ddy = cp.Y - (Top + SZ / 2.0);
            if (ddx * ddx + ddy * ddy < 9.0 * W * W) {
                if (now - lastNearMs > 120000 && anim == Anim.None && (mood == Mood.Normal || mood == Mood.Aburrida)) StartAnim(Anim.Wave, 70);
                lastNearMs = now;
            }
        }
        if (tick % 1000 == 3) UpdateAcc();
        UpdateParticles();

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
        if (tick % 100 == 37) LoadReminders(remLoaded);   // ~3 s: recarga recordatorios.txt si se ha editado
        if (tick % 165 == 50) CheckPower();

        if (--nextChatter <= 0) {
            nextChatter = 6000 + rand.Next(9840);   // ~3-8 min
            if (mood != Mood.Dormida && !dragging && mode != Mode.Air && bubbleTicks <= 0) Say(FrasesAzar);
        }
        if (bubbleTicks > 0 && --bubbleTicks == 0) HideBubble();
    }

    // ---------- Bucle de animacion ----------
    void OnTick(object sender, EventArgs e) {
        if (petHidden) return;   // oculta: congelada (Teams en verde sigue, tiene su propio temporizador)
        tick++;
        if (tick % 100 == 0) RefreshAreas();   // por si cambia la barra de tareas
        Think();

        // arcoiris: cambia de tono cada 3 ticks (~11 veces/s, una vuelta completa cada ~11 s)
        if (rainbow && !useSprite && tick % 3 == 0) { hue = (hue + 4) % 360; SetBodyColor(FromHue(hue)); }

        // parpadeo
        if (blink > 0) blink--; else if (rand.Next(150) == 0) blink = 8;

        // saltito
        if (hopV != 0 || hop > 0) {
            hop += hopV; hopV -= 1.0;
            if (hop <= 0) { hop = 0; hopV = 0; sqT = 6; }   // al caer se achata un momento
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
        bool moving = walking && !paused && mood != Mood.Dormida && anim == Anim.None;
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
                if (y >= floor) { if (vy > 5) SpawnDust(ccx, y + H, 5); vy = 0; hopV = 4.0; }
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
