// Adivina el Pokémon: minijuego de dos jugadores (dos Claudios en red).
// - Cada uno juega con el Pokémon que tiene su mascota (se bloquea el aspecto mientras dura la partida).
// - Cada jugador adivina el del otro rellenando campos (tipos, habilidad, generación...) y, en cualquier
//   momento, arriesgando el nombre. Cada campo sale en verde si acierta y en rojo si falla.
// - Conexión directa por TCP: uno crea la partida y recibe un código (su IP codificada), el otro lo teclea.
// - Los datos de cada Pokémon salen de PokeAPI y solo los conoce el cliente de su dueño: el rival
//   pregunta y el dueño responde "sí / no" (se fían uno del otro; no es a prueba de tramposos).
//
// Se compila junto con mascota.cs (mascota.vbs lo hace solo): C# 5, .NET Framework 4.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

enum GState { Lobby, Hosting, Joining, Syncing, Playing, Ended }

// Una opción de un desplegable: Key es lo que viaja por la red, Text lo que se ve.
class GOpt {
    public string Key, Text;
    public override string ToString() { return Text; }
}

class GField {
    public string Key, Label;
    public bool Multi;       // varios valores pueden ser ciertos (habilidades)
    public bool Editable;    // se puede escribir (con autocompletado) en vez de solo elegir
    public List<GOpt> Opts = new List<GOpt>();
}

// Datos secretos de MI Pokémon: para cada campo, los valores que son ciertos.
class PokeData {
    public int Id; public string Name; public bool Shiny;
    public Dictionary<string, List<string>> Truth = new Dictionary<string, List<string>>();
    public Bitmap Sprite;
}

// ---------- Datos de PokeAPI (con cache en %LOCALAPPDATA%\MascotaClaude) ----------
static class GameDex {
    const string Api = "https://pokeapi.co/api/v2/";
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MascotaClaude");
    static readonly object Lk = new object();

    public static List<GOpt> Species, Abilities;
    public static readonly List<GField> Fields = new List<GField>();
    public static readonly GField NameField = new GField { Key = "name", Label = "Nombre", Editable = true };

    static readonly string[] TypeKeys = { "normal", "fire", "water", "electric", "grass", "ice", "fighting", "poison", "ground",
                                          "flying", "psychic", "bug", "rock", "ghost", "dragon", "dark", "steel", "fairy" };
    static readonly string[] TypeEs = { "Normal", "Fuego", "Agua", "Eléctrico", "Planta", "Hielo", "Lucha", "Veneno", "Tierra",
                                        "Volador", "Psíquico", "Bicho", "Roca", "Fantasma", "Dragón", "Siniestro", "Acero", "Hada" };
    static readonly string[] GenEs = { "I · Kanto", "II · Johto", "III · Hoenn", "IV · Sinnoh", "V · Teselia", "VI · Kalos",
                                       "VII · Alola", "VIII · Galar", "IX · Paldea" };
    static readonly string[] EvKeys = { "0", "1", "2", "3" };
    static readonly string[] EvEs = { "Sin evolución", "Básico (evoluciona)", "Fase 1 (1.ª evolución)", "Fase 2 (2.ª evolución)" };
    static readonly string[] CatKeys = { "normal", "baby", "legendary", "mythical" };
    static readonly string[] CatEs = { "Normal", "Bebé", "Legendario", "Singular (mítico)" };
    static readonly string[] ColKeys = { "black", "blue", "brown", "gray", "green", "pink", "purple", "red", "white", "yellow" };
    static readonly string[] ColEs = { "Negro", "Azul", "Marrón", "Gris", "Verde", "Rosa", "Morado", "Rojo", "Blanco", "Amarillo" };
    static readonly string[] HeightEs = { "Menos de 0,5 m", "0,5 – 0,9 m", "1 – 1,4 m", "1,5 – 1,9 m", "2 – 2,9 m", "3 m o más" };
    static readonly string[] WeightEs = { "Menos de 5 kg", "5 – 19,9 kg", "20 – 49,9 kg", "50 – 99,9 kg", "100 – 299,9 kg", "300 kg o más" };

    static GameDex() {
        Fields.Add(Make("t1", "1.er tipo", false, false, TypeKeys, TypeEs));
        GField t2 = Make("t2", "2.º tipo", false, false, TypeKeys, TypeEs);
        t2.Opts.Insert(0, new GOpt { Key = "none", Text = "Ninguno (sin 2.º tipo)" });
        Fields.Add(t2);
        Fields.Add(new GField { Key = "ab", Label = "Habilidad", Multi = true, Editable = true });
        Fields.Add(Make("gen", "Generación", false, false, Seq(1, 9), GenEs));
        Fields.Add(Make("ev", "Fase evolutiva", false, false, EvKeys, EvEs));
        Fields.Add(Make("cat", "Categoría", false, false, CatKeys, CatEs));
        Fields.Add(Make("col", "Color", false, false, ColKeys, ColEs));
        Fields.Add(Make("alt", "Altura", false, false, Seq(0, 5), HeightEs));
        Fields.Add(Make("pes", "Peso", false, false, Seq(0, 5), WeightEs));
        string[] ini = new string[26], iniT = new string[26];
        for (int i = 0; i < 26; i++) { ini[i] = ((char)('A' + i)).ToString(); iniT[i] = ini[i]; }
        Fields.Add(Make("ini", "Inicial", false, false, ini, iniT));
    }

    static string[] Seq(int from, int to) {
        string[] r = new string[to - from + 1];
        for (int i = 0; i < r.Length; i++) r[i] = (from + i).ToString();
        return r;
    }

    static GField Make(string key, string label, bool multi, bool editable, string[] keys, string[] texts) {
        GField f = new GField { Key = key, Label = label, Multi = multi, Editable = editable };
        for (int i = 0; i < keys.Length; i++) f.Opts.Add(new GOpt { Key = keys[i], Text = texts[i] });
        return f;
    }

    public static GField Field(string key) {
        if (key == "name") return NameField;
        foreach (GField f in Fields) if (f.Key == key) return f;
        return null;
    }

    public static GOpt FindKey(List<GOpt> opts, string key) {
        foreach (GOpt o in opts) if (o.Key == key) return o;
        return null;
    }

    // Quita mayúsculas, acentos, espacios y signos: "Mr. Mime" == "mr mime" == "mrmime"
    public static string Norm(string s) {
        StringBuilder b = new StringBuilder();
        foreach (char c in (s ?? "").Normalize(NormalizationForm.FormD)) {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c == '♀') b.Append('f');
            else if (c == '♂') b.Append('m');
            else if (char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
        }
        return b.ToString();
    }

    public static GOpt FindText(List<GOpt> opts, string text) {
        string n = Norm(text);
        if (n.Length == 0) return null;
        foreach (GOpt o in opts) if (Norm(o.Text) == n) return o;
        return null;
    }

    public static string Pretty(string slug) {
        switch (slug) {
            case "nidoran-f": return "Nidoran ♀";
            case "nidoran-m": return "Nidoran ♂";
            case "farfetchd": return "Farfetch'd";
            case "sirfetchd": return "Sirfetch'd";
            case "mr-mime": return "Mr. Mime";
            case "mime-jr": return "Mime Jr.";
            case "mr-rime": return "Mr. Rime";
            case "type-null": return "Type: Null";
            case "flabebe": return "Flabébé";
            case "ho-oh": return "Ho-Oh";
            case "porygon-z": return "Porygon-Z";
            case "jangmo-o": return "Jangmo-o";
            case "hakamo-o": return "Hakamo-o";
            case "kommo-o": return "Kommo-o";
        }
        string[] w = slug.Split('-');
        for (int i = 0; i < w.Length; i++) if (w[i].Length > 0) w[i] = char.ToUpper(w[i][0]) + w[i].Substring(1);
        return string.Join(" ", w);
    }

    // ---- JSON (JavaScriptSerializer devuelve Dictionary<string,object> y object[]) ----
    static object Json(string url) {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        return js.DeserializeObject(Encoding.UTF8.GetString(PetForm.Download(url, 12000)));
    }
    static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
    static object G(object d, string k) { object v; Dictionary<string, object> dd = D(d); return dd != null && dd.TryGetValue(k, out v) ? v : null; }
    static string S(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture); }
    static List<object> L(object o) {
        List<object> r = new List<object>();
        System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
        if (e != null && !(o is string)) foreach (object x in e) r.Add(x);
        return r;
    }

    // ---- Listas (especies y habilidades) ----
    public static void EnsureLists() {
        lock (Lk) {
            if (Species == null) Species = LoadSpecies();
            if (Abilities == null) Abilities = LoadAbilities();
            Field("ab").Opts = Abilities;
            NameField.Opts = Species;
        }
    }

    static List<GOpt> LoadSpecies() {
        string f = Path.Combine(Dir, "especies.txt");
        string[] lines = null;
        if (File.Exists(f)) lines = File.ReadAllLines(f);
        if (lines == null || lines.Length < 100) {
            List<string> l = new List<string>();
            foreach (object r in L(G(Json(Api + "pokemon-species?limit=1025"), "results"))) {
                string url = S(G(r, "url")).TrimEnd('/');
                string id = url.Substring(url.LastIndexOf('/') + 1);
                l.Add(id + "|" + S(G(r, "name")));
            }
            if (l.Count < 100) throw new Exception("La lista de Pokémon está vacía.");
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(f, l.ToArray());
            lines = l.ToArray();
        }
        List<GOpt> res = new List<GOpt>();
        foreach (string line in lines) {
            int bar = line.IndexOf('|'); int id;
            if (bar < 0 || !int.TryParse(line.Substring(0, bar), out id)) continue;
            res.Add(new GOpt { Key = id.ToString(), Text = Pretty(line.Substring(bar + 1)) });
        }
        res.Sort(delegate(GOpt a, GOpt b) { return string.Compare(a.Text, b.Text, StringComparison.CurrentCultureIgnoreCase); });
        return res;
    }

    static List<GOpt> LoadAbilities() {
        string f = Path.Combine(Dir, "habilidades.txt");
        string[] lines = null;
        if (File.Exists(f)) lines = File.ReadAllLines(f);
        if (lines == null || lines.Length < 50) {
            List<string> l = new List<string>();
            foreach (object r in L(G(Json(Api + "ability?limit=1000"), "results"))) l.Add(S(G(r, "name")));
            if (l.Count < 50) throw new Exception("La lista de habilidades está vacía.");
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(f, l.ToArray());
            lines = l.ToArray();
        }
        List<GOpt> res = new List<GOpt>();
        foreach (string line in lines) if (line.Trim().Length > 0) res.Add(new GOpt { Key = line.Trim(), Text = Pretty(line.Trim()) });
        res.Sort(delegate(GOpt a, GOpt b) { return string.Compare(a.Text, b.Text, StringComparison.CurrentCultureIgnoreCase); });
        return res;
    }

    public static string SpeciesName(int id) {
        GOpt o = Species == null ? null : FindKey(Species, id.ToString());
        return o == null ? "#" + id : o.Text;
    }

    // ---- Datos de mi Pokémon ----
    static readonly string[] TruthKeys = { "t1", "t2", "ab", "gen", "ev", "cat", "col", "alt", "pes", "ini" };

    public static PokeData LoadMe(int id, bool shiny) {
        PokeData pd = new PokeData { Id = id, Shiny = shiny };
        string f = Path.Combine(Dir, "partida", id + ".txt");
        if (!ReadCache(f, pd)) {
            Fetch(id, pd);
            try { Directory.CreateDirectory(Path.GetDirectoryName(f)); WriteCache(f, pd); } catch { }
        }
        string err;
        pd.Sprite = PetForm.GetPokeSprite(id, shiny, out err);   // puede ser null: el juego funciona igual
        return pd;
    }

    static bool ReadCache(string f, PokeData pd) {
        try {
            if (!File.Exists(f)) return false;
            Dictionary<string, string> kv = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(f)) {
                int eq = line.IndexOf('=');
                if (eq > 0) kv[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            if (!kv.ContainsKey("v") || kv["v"] != "1" || !kv.ContainsKey("name")) return false;
            pd.Name = kv["name"];
            foreach (string k in TruthKeys) {
                if (!kv.ContainsKey(k) || kv[k].Length == 0) return false;
                pd.Truth[k] = new List<string>(kv[k].Split(','));
            }
            return true;
        } catch { pd.Truth.Clear(); return false; }
    }

    static void WriteCache(string f, PokeData pd) {
        List<string> l = new List<string>();
        l.Add("v=1"); l.Add("name=" + pd.Name);
        foreach (string k in TruthKeys) l.Add(k + "=" + string.Join(",", pd.Truth[k].ToArray()));
        File.WriteAllLines(f, l.ToArray());
    }

    static void Fetch(int id, PokeData pd) {
        object p = Json(Api + "pokemon/" + id);
        object s = Json(Api + "pokemon-species/" + id);
        string slug = S(G(s, "name"));
        if (slug.Length == 0) throw new Exception("PokeAPI no tiene datos de #" + id + ".");
        pd.Name = Pretty(slug);

        string t1 = null, t2 = null;
        foreach (object t in L(G(p, "types"))) {
            string n = S(G(G(t, "type"), "name"));
            if (S(G(t, "slot")) == "1") t1 = n; else if (S(G(t, "slot")) == "2") t2 = n;
        }
        if (t1 == null) throw new Exception("PokeAPI no devolvió los tipos.");
        List<string> abs = new List<string>();
        foreach (object a in L(G(p, "abilities"))) {
            string n = S(G(G(a, "ability"), "name"));
            if (n.Length > 0 && !abs.Contains(n)) abs.Add(n);
        }
        if (abs.Count == 0) throw new Exception("PokeAPI no devolvió las habilidades.");

        string genName = S(G(G(s, "generation"), "name"));   // "generation-vi"
        int gen = Array.IndexOf(new string[] { "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix" }, genName.Substring(genName.LastIndexOf('-') + 1)) + 1;
        if (gen <= 0) throw new Exception("Generación desconocida: " + genName);

        string cat = S(G(s, "is_mythical")) == "True" ? "mythical" : S(G(s, "is_legendary")) == "True" ? "legendary" : S(G(s, "is_baby")) == "True" ? "baby" : "normal";

        // fase evolutiva: posición de la especie en su cadena
        int depth = 0; bool evolves = false;
        string chainUrl = S(G(G(s, "evolution_chain"), "url"));
        if (chainUrl.Length > 0) {
            object chain = G(Json(chainUrl), "chain");
            Walk(chain, slug, 0, ref depth, ref evolves);
        }
        string ev = depth == 0 ? (evolves ? "1" : "0") : (depth == 1 ? "2" : "3");

        int h = Convert.ToInt32(S(G(p, "height"))), w = Convert.ToInt32(S(G(p, "weight")));
        int hb = h < 5 ? 0 : h < 10 ? 1 : h < 15 ? 2 : h < 20 ? 3 : h < 30 ? 4 : 5;
        int wb = w < 50 ? 0 : w < 200 ? 1 : w < 500 ? 2 : w < 1000 ? 3 : w < 3000 ? 4 : 5;

        pd.Truth["t1"] = new List<string> { t1 };
        pd.Truth["t2"] = new List<string> { t2 ?? "none" };
        pd.Truth["ab"] = abs;
        pd.Truth["gen"] = new List<string> { gen.ToString() };
        pd.Truth["ev"] = new List<string> { ev };
        pd.Truth["cat"] = new List<string> { cat };
        pd.Truth["col"] = new List<string> { S(G(G(s, "color"), "name")) };
        pd.Truth["alt"] = new List<string> { hb.ToString() };
        pd.Truth["pes"] = new List<string> { wb.ToString() };
        pd.Truth["ini"] = new List<string> { char.ToUpper(slug[0]).ToString() };
    }

    static bool Walk(object node, string slug, int depth, ref int outDepth, ref bool evolves) {
        List<object> next = L(G(node, "evolves_to"));
        if (S(G(G(node, "species"), "name")) == slug) { outDepth = depth; evolves = next.Count > 0; return true; }
        foreach (object n in next) if (Walk(n, slug, depth + 1, ref outDepth, ref evolves)) return true;
        return false;
    }
}

// ---------- Conexión: líneas de texto UTF-8 sobre TCP ----------
class Link {
    public const int Port = 47831;
    public Action<string> OnLine;    // se llama desde el hilo de lectura
    public Action OnClosed;          // idem; una sola vez
    readonly TcpClient cli; readonly NetworkStream ns;
    readonly object wl = new object();
    bool closed;

    public Link(TcpClient c) {
        cli = c; cli.NoDelay = true;
        try { cli.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
        ns = cli.GetStream();
    }

    public void Start() {
        Thread t = new Thread(ReadLoop);
        t.IsBackground = true;
        t.Start();
    }

    void ReadLoop() {
        try {
            byte[] buf = new byte[1024];
            MemoryStream cur = new MemoryStream();
            int n;
            while ((n = ns.Read(buf, 0, buf.Length)) > 0) {
                for (int i = 0; i < n; i++) {
                    if (buf[i] == (byte)'\n') {
                        string line = Encoding.UTF8.GetString(cur.ToArray()).TrimEnd('\r');
                        cur.SetLength(0);
                        if (OnLine != null) OnLine(line);
                    } else {
                        cur.WriteByte(buf[i]);
                        if (cur.Length > 2048) return;   // un mensaje nuestro nunca es tan largo
                    }
                }
            }
        } catch { }
        finally {
            Close();
            Action a = OnClosed;
            if (a != null) a();
        }
    }

    public void Send(string line) {
        lock (wl) {
            if (closed) return;
            try { byte[] b = Encoding.UTF8.GetBytes(line + "\n"); ns.Write(b, 0, b.Length); }
            catch { Close(); }
        }
    }

    public void Close() {
        closed = true;
        try { cli.Close(); } catch { }
    }
}

class GRow {
    public GField F; public Label Lab; public ComboBox Cb; public Button Btn; public FlowLayoutPanel Chips;
    public bool Solved;
}

class PendingGuess { public GRow Row; public string Value, Text; }

class Addr {
    public IPAddress Ip; public string Desc;
    public override string ToString() { return PokeGame.CodeOf(Ip) + "     " + Ip + "  ·  " + Desc; }
}

// ---------- La ventana del juego ----------
class PokeGame : Form {
    const bool TURNOS = true;       // true: un intento por turno (campo o nombre). false: los dos a la vez
    const int MAX_VIDAS = 3;        // fallos de nombre permitidos

    static readonly Color Green = Color.FromArgb(170, 225, 170), Red = Color.FromArgb(240, 170, 170);
    static readonly Color DarkGreen = Color.FromArgb(30, 130, 50), DarkRed = Color.FromArgb(185, 45, 45), Grey = Color.FromArgb(110, 110, 110);

    readonly int myId; readonly bool myShiny;
    readonly Action<bool> onResult;               // true = ganaste, false = perdiste
    public event EventHandler StateChanged;
    GState state = GState.Lobby;
    public bool Locked { get { return state != GState.Ended; } }   // mientras sea true, el aspecto de la mascota no se puede cambiar

    readonly Random rand = new Random();
    int session;                                  // se incrementa al cancelar: los hilos antiguos se ignoran
    Link link; TcpListener listener; bool isHost;
    PokeData me; bool dataReady, helloOk, sentReady, peerReady;
    bool myTurn; int vidas = MAX_VIDAS;
    PendingGuess pend;
    readonly List<GRow> rows = new List<GRow>();
    GRow nameRow;
    // resultado
    bool resWon; string resReason;
    int rivalId; string rivalName; bool rivalShiny;

    // lobby
    Panel lobby, game;
    ComboBox cbAddr; Button btnHost, btnJoin, btnCopy, btnCancel; TextBox txtCode;
    Label lblCode, lblLobbyMsg;
    GroupBox grpHost, grpJoin;
    // partida
    PictureBox picMine, picRival; Label lblMine, lblRivalQ, lblRivalName, lblStatus, lblVidas, lblLast;
    Panel pnlFields; TableLayoutPanel tblFields, tblName; Button btnAbandon;

    public PokeGame(int pokeId, bool shiny, Action<bool> result) {
        myId = pokeId; myShiny = shiny; onResult = result;
        Text = "Adivina el Pokémon";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;   // ventana pequeña y discreta
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 8f);
        BuildLobby();
        BuildGame();
        ShowLobby();
        FormClosing += OnFormClosing;
        FormClosed += delegate { Cleanup(); state = GState.Ended; RaiseState(); };
    }

    void RaiseState() { if (StateChanged != null) StateChanged(this, EventArgs.Empty); }
    void SetState(GState s) { state = s; RaiseState(); }

    // Ejecuta en el hilo de la interfaz (lo llaman los hilos de red)
    void UI(Action a) {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    // ================= Lobby =================
    void BuildLobby() {
        lobby = new Panel { Dock = DockStyle.Fill };
        Controls.Add(lobby);

        Label intro = new Label { Left = 8, Top = 6, Width = 324, Height = 28,
            Text = "Adivina el Pokémon de tu colega. Tu aspecto queda bloqueado mientras juegas." };
        lobby.Controls.Add(intro);

        grpHost = new GroupBox { Left = 8, Top = 36, Width = 324, Height = 84, Text = "Crear partida" };
        cbAddr = new ComboBox { Left = 8, Top = 16, Width = 306, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (Addr a in LocalAddrs()) cbAddr.Items.Add(a);
        if (cbAddr.Items.Count > 0) cbAddr.SelectedIndex = 0;
        btnHost = new Button { Left = 8, Top = 46, Width = 84, Height = 24, Text = "Crear" };
        btnHost.Click += delegate { StartHost(); };
        lblCode = new Label { Left = 100, Top = 44, Width = 130, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 13f, FontStyle.Bold), ForeColor = DarkGreen };
        btnCopy = new Button { Left = 238, Top = 46, Width = 76, Height = 24, Text = "Copiar", Visible = false };
        btnCopy.Click += delegate { try { Clipboard.SetText(lblCode.Text); } catch { } };
        grpHost.Controls.AddRange(new Control[] { cbAddr, btnHost, lblCode, btnCopy });
        lobby.Controls.Add(grpHost);

        grpJoin = new GroupBox { Left = 8, Top = 124, Width = 324, Height = 52, Text = "Unirme" };
        Label lj = new Label { Left = 8, Top = 22, Width = 44, Text = "Código:" };
        txtCode = new TextBox { Left = 54, Top = 18, Width = 130, Font = new Font("Consolas", 10f) };
        txtCode.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartJoin(); } };
        btnJoin = new Button { Left = 192, Top = 17, Width = 80, Height = 24, Text = "Unirme" };
        btnJoin.Click += delegate { StartJoin(); };
        grpJoin.Controls.AddRange(new Control[] { lj, txtCode, btnJoin });
        lobby.Controls.Add(grpJoin);

        lblLobbyMsg = new Label { Left = 8, Top = 182, Width = 324, Height = 40, ForeColor = Grey,
            Text = "Misma red (oficina o VPN). Al crear, Windows puede pedir permiso al firewall: acéptalo." };
        btnCancel = new Button { Left = 8, Top = 226, Width = 70, Height = 22, Text = "Cancelar", Visible = false };
        btnCancel.Click += delegate { Reset("Cancelado."); };
        lobby.Controls.AddRange(new Control[] { lblLobbyMsg, btnCancel });
    }

    void ShowLobby() {
        game.Visible = false; lobby.Visible = true;
        ClientSize = new Size(340, 254);
    }

    void LobbyBusy(bool busy) {
        cbAddr.Enabled = btnHost.Enabled = !busy;
        txtCode.Enabled = btnJoin.Enabled = !busy;
        btnCancel.Visible = busy;
    }

    void LobbyMsg(string t, bool error) { lblLobbyMsg.Text = t; lblLobbyMsg.ForeColor = error ? DarkRed : Grey; }

    public static string CodeOf(IPAddress ip) {
        byte[] b = ip.GetAddressBytes();
        return string.Format("{0:X2}{1:X2}-{2:X2}{3:X2}", b[0], b[1], b[2], b[3]);
    }

    static bool ParseCode(string s, out IPAddress ip, out int port) {
        ip = null; port = Link.Port; s = (s ?? "").Trim();
        if (s.Length == 0) return false;
        if (s.IndexOf('.') >= 0) {
            int c = s.LastIndexOf(':');
            if (c > 0) { if (!int.TryParse(s.Substring(c + 1), out port) || port < 1 || port > 65535) return false; s = s.Substring(0, c); }
            return IPAddress.TryParse(s, out ip) && ip.AddressFamily == AddressFamily.InterNetwork;
        }
        StringBuilder h = new StringBuilder();
        foreach (char ch in s) { if (Uri.IsHexDigit(ch)) h.Append(ch); else if (ch != '-' && ch != ' ') return false; }
        if (h.Length != 8) return false;
        byte[] b = new byte[4];
        for (int i = 0; i < 4; i++) b[i] = Convert.ToByte(h.ToString(i * 2, 2), 16);
        ip = new IPAddress(b);
        return true;
    }

    static List<Addr> LocalAddrs() {
        List<Addr> good = new List<Addr>(), rest = new List<Addr>();
        try {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties p = ni.GetIPProperties();
                bool gw = false;
                foreach (GatewayIPAddressInformation g in p.GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gw = true;
                string d = ni.Name + " " + ni.Description;
                bool virt = d.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 || d.IndexOf("vethernet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            d.IndexOf("vmware", StringComparison.OrdinalIgnoreCase) >= 0 || d.IndexOf("wsl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            d.IndexOf("hyper-v", StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (UnicastIPAddressInformation u in p.UnicastAddresses) {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] b = u.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;
                    Addr a = new Addr { Ip = u.Address, Desc = ni.Name };
                    if (gw && !virt) good.Add(a); else rest.Add(a);
                }
            }
        } catch { }
        good.AddRange(rest);
        good.Add(new Addr { Ip = IPAddress.Loopback, Desc = "solo este equipo (pruebas)" });
        return good;
    }

    // ---- Crear / unirse ----
    void StartHost() {
        if (state != GState.Lobby) return;
        Addr a = cbAddr.SelectedItem as Addr;
        if (a == null) { LobbyMsg("No hay ninguna dirección de red disponible.", true); return; }
        TcpListener l;
        try { l = new TcpListener(IPAddress.Any, Link.Port); l.Start(1); }
        catch (Exception ex) { LobbyMsg("No he podido abrir el puerto " + Link.Port + " (¿ya hay otra partida abierta?): " + ex.Message, true); return; }
        listener = l; isHost = true; int ses = ++session;
        SetState(GState.Hosting);
        LobbyBusy(true);
        lblCode.Text = CodeOf(a.Ip); btnCopy.Visible = true;
        LobbyMsg("Pásale este código a tu colega (Teams, etc.). Esperando a que se una...", false);
        StartLoadData(ses);
        Thread t = new Thread(delegate() {
            try {
                TcpClient c = l.AcceptTcpClient();
                try { l.Stop(); } catch { }
                UI(delegate { if (ses == session) OnPeer(c); else c.Close(); });
            } catch { }
        });
        t.IsBackground = true; t.Start();
    }

    void StartJoin() {
        if (state != GState.Lobby) return;
        IPAddress ip; int port;
        if (!ParseCode(txtCode.Text, out ip, out port)) { LobbyMsg("Ese código no vale: son 8 caracteres (por ejemplo C0A8-0105) o una IP.", true); return; }
        isHost = false; int ses = ++session;
        SetState(GState.Joining);
        LobbyBusy(true); lblCode.Text = ""; btnCopy.Visible = false;
        LobbyMsg("Conectando con " + ip + "...", false);
        StartLoadData(ses);
        Thread t = new Thread(delegate() {
            TcpClient c = new TcpClient();
            try {
                IAsyncResult ar = c.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(6000)) { c.Close(); throw new Exception("no responde (¿firewall o red distinta?)"); }
                c.EndConnect(ar);
                UI(delegate { if (ses == session) OnPeer(c); else c.Close(); });
            } catch (Exception ex) {
                try { c.Close(); } catch { }
                string m = ex.Message;
                UI(delegate { if (ses == session) Reset("No he podido conectar con tu colega: " + m); });
            }
        });
        t.IsBackground = true; t.Start();
    }

    // Descarga las listas y los datos de mi Pokémon mientras se conecta el otro
    void StartLoadData(int ses) {
        dataReady = false; sentReady = false; peerReady = false; helloOk = false; me = null;
        Thread t = new Thread(delegate() {
            PokeData d = null; string err = null;
            try { GameDex.EnsureLists(); d = GameDex.LoadMe(myId, myShiny); }
            catch (Exception ex) { err = ex.Message; }
            UI(delegate {
                if (ses != session) return;
                if (d == null) { Reset("No he podido obtener los datos de tu Pokémon (¿sin conexión a PokeAPI?): " + err); return; }
                me = d; dataReady = true;
                TrySendReady(); TryStart();
            });
        });
        t.IsBackground = true; t.Start();
    }

    void OnPeer(TcpClient c) {
        if (state != GState.Hosting && state != GState.Joining) { c.Close(); return; }
        if (listener != null) { try { listener.Stop(); } catch { } listener = null; }
        Link l = new Link(c);
        link = l;
        l.OnLine = delegate(string s) { UI(delegate { HandleLine(l, s); }); };
        l.OnClosed = delegate { UI(delegate { OnLinkClosed(l); }); };
        SetState(GState.Syncing);
        LobbyMsg("¡Conectados! Preparando la partida...", false);
        l.Start();
        l.Send("HI|1");
    }

    void TrySendReady() {
        if (link != null && helloOk && dataReady && !sentReady) { sentReady = true; link.Send("READY"); }
    }

    void TryStart() {
        if (!isHost || state != GState.Syncing || !dataReady || !peerReady) return;
        bool guestFirst = rand.Next(2) == 0;
        link.Send("START|" + (guestFirst ? "1" : "0"));
        BeginPlay(!guestFirst);
    }

    // Vuelve al lobby (cancelar o fallo antes de empezar)
    void Reset(string msg) {
        session++;
        Cleanup();
        dataReady = helloOk = sentReady = peerReady = false; me = null; pend = null;
        lblCode.Text = ""; btnCopy.Visible = false;
        LobbyBusy(false);
        LobbyMsg(msg, msg != "Cancelado.");
        state = GState.Lobby;   // sigue bloqueado el aspecto hasta cerrar la ventana
        ShowLobby();
    }

    void Cleanup() {
        if (listener != null) { try { listener.Stop(); } catch { } listener = null; }
        if (link != null) { link.OnLine = null; link.OnClosed = null; link.Close(); link = null; }
    }

    // ================= Protocolo =================
    void HandleLine(Link from, string line) {
        if (from != link || state == GState.Lobby) return;
        string[] p = line.Split('|');
        switch (p[0]) {
            case "HI":
                if (p.Length < 2 || p[1] != "1") { Reset("Tu colega tiene otra versión del juego."); return; }
                helloOk = true; TrySendReady(); break;
            case "READY": peerReady = true; TryStart(); break;
            case "START":
                if (!isHost && state == GState.Syncing && dataReady) BeginPlay(p.Length > 1 && p[1] == "1");
                break;
            case "Q": if (state == GState.Playing && p.Length >= 3) OnQuestion(p[1], p[2]); break;
            case "A": if (state == GState.Playing && p.Length >= 4) OnAnswer(p[1], p[2], p[3] == "1"); break;
            case "END": OnEnd(p); break;
            case "REVEAL": if (state == GState.Ended) OnReveal(p, 1); break;
        }
    }

    void OnLinkClosed(Link l) {
        if (l != link) return;
        link = null;
        if (state == GState.Playing) Finish(false, "conn");
        else if (state == GState.Syncing) Reset("Tu colega se ha desconectado.");
    }

    // El rival pregunta por mi Pokémon
    void OnQuestion(string field, string value) {
        bool ok; string text;
        if (field == "name") {
            int id;
            if (!int.TryParse(value, out id) || GameDex.FindKey(GameDex.Species, value) == null) return;
            ok = id == me.Id; text = "el nombre = " + GameDex.SpeciesName(id);
        } else {
            GField f = GameDex.Field(field); GOpt o = f == null ? null : GameDex.FindKey(f.Opts, value);
            if (o == null) return;
            List<string> truth;
            ok = me.Truth.TryGetValue(field, out truth) && truth.Contains(value);
            text = f.Label + " = " + o.Text;
        }
        lblLast.Text = "Tu colega: " + text + (ok ? "  →  acertó" : "  →  falló");
        if (field == "name" && ok) { SendEnd("guessed"); Finish(false, "guessed"); return; }
        link.Send("A|" + field + "|" + value + "|" + (ok ? "1" : "0"));
        myTurn = true;
        RefreshUI();
    }

    // Respuesta a mi pregunta
    void OnAnswer(string field, string value, bool ok) {
        if (pend == null || pend.Row.F.Key != field || pend.Value != value) return;
        PendingGuess g = pend; pend = null;
        AddChip(g.Row, g.Text, ok);
        RemoveOpt(g.Row, value);
        if (field == "name") {
            if (!ok) vidas--;
            UpdateVidas();
            if (vidas <= 0) { SendEnd("nolives"); Finish(false, "nolives"); return; }
        } else if (ok && !g.Row.F.Multi) g.Row.Solved = true;
        myTurn = false;
        RefreshUI();
    }

    // El otro ha perdido (me han adivinado, se ha quedado sin intentos o ha abandonado): gano yo
    void OnEnd(string[] p) {
        if (state != GState.Playing || p.Length < 2) return;
        string reason = p[1];
        if (reason != "guessed" && reason != "nolives" && reason != "abandon") return;
        OnReveal(p, 2);
        if (reason == "guessed" && pend != null && pend.Row == nameRow) { AddChip(nameRow, pend.Text, true); pend = null; }
        Finish(true, reason);
        if (link != null) link.Send("REVEAL|" + me.Id + "|" + me.Name + "|" + (me.Shiny ? "1" : "0"));
    }

    void SendEnd(string reason) {
        if (link != null) link.Send("END|" + reason + "|" + me.Id + "|" + me.Name + "|" + (me.Shiny ? "1" : "0"));
    }

    // p[i] = id, p[i+1] = nombre, p[i+2] = shiny
    void OnReveal(string[] p, int i) {
        int id;
        if (p.Length < i + 3 || !int.TryParse(p[i], out id) || id < 1 || id > 1025) return;
        rivalId = id;
        string n = p[i + 1].Trim(); if (n.Length > 40) n = n.Substring(0, 40);
        rivalName = n.Length > 0 ? n : GameDex.SpeciesName(id);
        rivalShiny = p[i + 2] == "1";
        if (state == GState.Ended) ShowResult();
        int rid = id; bool rs = rivalShiny;
        ThreadPool.QueueUserWorkItem(delegate {
            string err; Bitmap b = null;
            try { b = PetForm.GetPokeSprite(rid, rs, out err); } catch { }
            UI(delegate { if (b != null && rid == rivalId) { picRival.Image = b; picRival.Visible = true; lblRivalQ.Visible = false; } });
        });
    }

    // ================= Partida =================
    void BuildGame() {
        game = new Panel { Dock = DockStyle.Fill, Visible = false };
        Controls.Add(game);

        picMine = new PictureBox { Left = 6, Top = 4, Width = 48, Height = 48, SizeMode = PictureBoxSizeMode.Zoom };
        lblMine = new Label { Left = 0, Top = 54, Width = 60, Height = 12, TextAlign = ContentAlignment.TopCenter, AutoEllipsis = true, Font = new Font("Segoe UI", 7f) };
        lblRivalQ = new Label { Left = 318, Top = 4, Width = 48, Height = 48, Text = "?", TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold), BackColor = Color.FromArgb(235, 235, 235), ForeColor = Grey };
        picRival = new PictureBox { Left = 318, Top = 4, Width = 48, Height = 48, SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        lblRivalName = new Label { Left = 312, Top = 54, Width = 60, Height = 12, TextAlign = ContentAlignment.TopCenter, AutoEllipsis = true, Font = new Font("Segoe UI", 7f), Text = "???" };
        lblStatus = new Label { Left = 60, Top = 2, Width = 252, Height = 32, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
        lblVidas = new Label { Left = 60, Top = 34, Width = 252, Height = 14, TextAlign = ContentAlignment.MiddleCenter };
        lblLast = new Label { Left = 60, Top = 49, Width = 252, Height = 26, TextAlign = ContentAlignment.TopCenter, ForeColor = Grey, Font = new Font("Segoe UI", 7.5f) };

        pnlFields = new Panel { Left = 4, Top = 78, Width = 364, Height = 296, AutoScroll = true };
        tblFields = NewTable(); pnlFields.Controls.Add(tblFields);

        tblName = NewTable(); tblName.Left = 4; tblName.Top = 378;
        btnAbandon = new Button { Left = 6, Top = 434, Width = 74, Height = 20, Text = "Abandonar" };
        btnAbandon.Click += delegate { Abandon(); };
        game.Controls.AddRange(new Control[] { picMine, lblMine, lblRivalQ, picRival, lblRivalName, lblStatus, lblVidas, lblLast, pnlFields, tblName, btnAbandon });
    }

    static TableLayoutPanel NewTable() {
        TableLayoutPanel t = new TableLayoutPanel { Left = 0, Top = 0, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        return t;
    }

    GRow MakeRow(TableLayoutPanel t, GField f) {
        GRow r = new GRow { F = f };
        r.Lab = new Label { Text = f.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 5, 1, 2) };
        r.Cb = new ComboBox { Width = 118, Anchor = AnchorStyles.Left, Margin = new Padding(1, 2, 1, 2), DropDownStyle = f.Editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, MaxDropDownItems = 12, DropDownWidth = 190 };
        foreach (GOpt o in f.Opts) r.Cb.Items.Add(o);
        if (f.Editable) { r.Cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend; r.Cb.AutoCompleteSource = AutoCompleteSource.ListItems; }
        r.Cb.SelectedIndex = -1;
        r.Btn = new Button { Text = f.Key == "name" ? "¡Ya!" : "Ir", Width = 34, Height = 21, Margin = new Padding(1, 2, 1, 2), Padding = new Padding(0) };
        r.Chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(118, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 1, 0, 1) };
        GRow rr = r;
        r.Btn.Click += delegate { Guess(rr); };
        r.Cb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && rr.Btn.Enabled) { e.SuppressKeyPress = true; Guess(rr); } };
        int row = t.RowCount; t.RowCount = row + 1; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(r.Lab, 0, row); t.Controls.Add(r.Cb, 1, row); t.Controls.Add(r.Btn, 2, row); t.Controls.Add(r.Chips, 3, row);
        return r;
    }

    void BeginPlay(bool iStart) {
        myTurn = iStart; vidas = MAX_VIDAS; pend = null;
        rows.Clear(); tblFields.Controls.Clear(); tblFields.RowStyles.Clear(); tblFields.RowCount = 0;
        tblName.Controls.Clear(); tblName.RowStyles.Clear(); tblName.RowCount = 0;
        foreach (GField f in GameDex.Fields) rows.Add(MakeRow(tblFields, f));
        nameRow = MakeRow(tblName, GameDex.NameField);
        nameRow.Lab.Font = new Font(Font, FontStyle.Bold);
        picMine.Image = me.Sprite;
        lblMine.Text = me.Name;
        lblRivalName.Text = "???";
        lblLast.Text = "";
        btnAbandon.Text = "Abandonar";
        lobby.Visible = false; game.Visible = true;
        ClientSize = new Size(372, 458);
        UpdateVidas();
        SetState(GState.Playing);
        RefreshUI();
        Activate();
    }

    void UpdateVidas() {
        StringBuilder b = new StringBuilder("Intentos de nombre: ");
        for (int i = 0; i < MAX_VIDAS; i++) b.Append(i < vidas ? "♥ " : "♡ ");
        lblVidas.Text = b.ToString();
    }

    bool CanAct() { return state == GState.Playing && pend == null && (!TURNOS || myTurn); }

    void RefreshUI() {
        if (state != GState.Playing) return;
        bool can = CanAct();
        foreach (GRow r in rows) { bool en = can && !r.Solved; r.Cb.Enabled = en; r.Btn.Enabled = en; }
        nameRow.Cb.Enabled = can; nameRow.Btn.Enabled = can;
        if (pend != null) { lblStatus.Text = "Esperando respuesta..."; lblStatus.ForeColor = Grey; }
        else if (TURNOS && !myTurn) { lblStatus.Text = "Turno de tu colega..."; lblStatus.ForeColor = Grey; }
        else { lblStatus.Text = "¡Tu turno!\nPrueba un campo o el nombre."; lblStatus.ForeColor = DarkGreen; }
    }

    void Guess(GRow r) {
        if (!CanAct()) return;
        GOpt o = r.F.Editable ? GameDex.FindText(r.F.Opts, r.Cb.Text) : r.Cb.SelectedItem as GOpt;
        if (o == null) { lblLast.Text = r.F.Editable ? "Escribe un valor que exista." : "Elige una opción."; return; }
        pend = new PendingGuess { Row = r, Value = o.Key, Text = o.Text };
        link.Send("Q|" + r.F.Key + "|" + o.Key);
        RefreshUI();
    }

    void AddChip(GRow r, string text, bool ok) {
        Label c = new Label { AutoSize = true, Text = (ok ? "✔ " : "✘ ") + text, BackColor = ok ? Green : Red, ForeColor = Color.Black,
            Margin = new Padding(0, 1, 3, 1), Padding = new Padding(2, 1, 2, 1), Font = new Font("Segoe UI", 7.5f) };
        r.Chips.Controls.Add(c);
        if (ok && !r.F.Multi) { r.Lab.ForeColor = DarkGreen; r.Lab.Font = new Font(r.Lab.Font, FontStyle.Bold); }
    }

    void RemoveOpt(GRow r, string key) {
        for (int i = 0; i < r.Cb.Items.Count; i++) {
            GOpt o = (GOpt)r.Cb.Items[i];
            if (o.Key == key) { r.Cb.Items.RemoveAt(i); break; }
        }
        r.Cb.SelectedIndex = -1; r.Cb.Text = "";
    }

    void Abandon() {
        if (state == GState.Playing) {
            if (MessageBox.Show(this, "¿Abandonar la partida? Perderás.", "Adivina el Pokémon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SendEnd("abandon"); Finish(false, "abandon");
        } else Close();
    }

    void OnFormClosing(object s, FormClosingEventArgs e) {
        if (state == GState.Playing) {
            if (MessageBox.Show(this, "Si cierras la ventana abandonas la partida y pierdes. ¿Seguro?", "Adivina el Pokémon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
            SendEnd("abandon");
        }
    }

    // Fin de la partida: se desbloquea el aspecto de la mascota
    void Finish(bool won, string reason) {
        if (state == GState.Ended) return;
        resWon = won; resReason = reason; pend = null;
        foreach (GRow r in rows) { r.Cb.Enabled = false; r.Btn.Enabled = false; }
        nameRow.Cb.Enabled = false; nameRow.Btn.Enabled = false;
        btnAbandon.Text = "Cerrar";
        SetState(GState.Ended);
        ShowResult();
        if (reason != "conn" && onResult != null) onResult(won);
    }

    void ShowResult() {
        string rival = rivalName ?? "...";
        string t; Color c = resWon ? DarkGreen : DarkRed;
        if (resReason == "conn") { t = "Se perdió la conexión.\nPartida anulada."; c = Grey; }
        else if (resWon)
            t = "¡HAS GANADO!\n" + (resReason == "nolives" ? "Sin intentos. " : resReason == "abandon" ? "Abandonó. " : "") + "Era " + rival;
        else
            t = (resReason == "abandon" ? "Has abandonado" : "Has perdido") + "\n" +
                (resReason == "guessed" ? "Te adivinó. " : resReason == "nolives" ? "Sin intentos. " : "") + "El suyo era " + rival;
        lblStatus.Text = t; lblStatus.ForeColor = c;
        lblRivalName.Text = rivalName != null ? rivalName : "???";
    }
}
