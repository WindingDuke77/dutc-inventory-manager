// ---------- MODULE: LCD screens (PowerDeck-style sprite UI) ----------
// All screens are drawn with sprites: dark background, colored bars, auto-scroll.
// The Autocrafting screen is drawn in 04_craft.cs (input lives in its Custom Data).

Color UI_BG = new Color(8, 10, 16);
Color UI_FRAME = new Color(95, 105, 125);
Color UI_TEXT = new Color(210, 220, 235);
Color UI_DIM = new Color(110, 120, 140);
Color UI_GOOD = new Color(80, 220, 120);
Color UI_WARNC = new Color(255, 190, 60);
Color UI_BAD = new Color(255, 80, 70);
Color[] catColors = {
    new Color(200, 140, 80),   // Ores
    new Color(170, 190, 210),  // Ingots
    new Color(100, 160, 255),  // Components
    new Color(255, 190, 60),   // Tools
    new Color(255, 110, 90),   // Ammo
    new Color(80, 180, 255),   // Bottles
    new Color(150, 210, 110)   // Food
};

class UiRow
{
    public int kind;          // 0 = centered header, 1 = item row with bar, 2 = plain text
    public string name = "";
    public string val = "";
    public double frac = -1;  // <0 = no bar
    public Color col;
}

bool Screens()
{
    foreach (var b in mainHolders) { var s = SurfaceFor(b, MAIN_TAGS); if (s != null) DrawMain(s); }
    foreach (var b in warnHolders) { var s = SurfaceFor(b, WARN_TAGS); if (s != null) DrawList(s, "WARNINGS", WarnRows(), true); }
    foreach (var b in actHolders) { var s = SurfaceFor(b, ACT_TAGS); if (s != null) DrawList(s, "ACTIONS", ActRows(), true); }
    foreach (var b in invHolders) WriteInv(b);
    return true;
}

void DrawMain(IMyTextSurface s)
{
    var f = Begin(s);
    Vector2 size = s.SurfaceSize, off = (s.TextureSize - size) * 0.5f;
    float W = size.X, H = size.Y, sc = Math.Min(W, H) / 512f * UI_SCALE;
    // all vertical spacing in PIXELS so tall/vertical LCDs don't stretch rows apart (thanks VFox32)
    f.Add(Txt("DUTC INVENTORY", off + new Vector2(W * 0.5f, 10f * sc), 1.15f * sc, UI_DIM));
    f.Add(Txt(("" + "|/-\\"[tick % 4]), off + new Vector2(W * 0.95f, 10f * sc), 1.0f * sc, UI_DIM, TextAlignment.RIGHT));
    float y = 64f * sc;
    for (int c = 0; c < NCAT; c++)
    {
        double cur = 0, max = 0;
        foreach (var w in cats[c])
        {
            var inv = w.GetInventory(0);
            cur += (double)inv.CurrentVolume; max += (double)inv.MaxVolume;
        }
        double fr = max > 0 ? cur / max : 0;
        Color col = fr > 0.9 ? UI_BAD : catColors[c];
        f.Add(Txt(catKeys[c] + " x" + cats[c].Count, off + new Vector2(W * 0.05f, y), 0.7f * sc, UI_TEXT, TextAlignment.LEFT));
        DrawBar(f, off + new Vector2(W * 0.60f, y + 10f * sc), W * 0.34f, 13f * sc, fr, col);
        f.Add(Txt(Pct(cur, max), off + new Vector2(W * 0.96f, y + 1f * sc), 0.6f * sc, col, TextAlignment.RIGHT));
        y += 31f * sc;
    }
    f.Add(Box(off + new Vector2(W * 0.5f, y + 2f * sc), new Vector2(W * 0.9f, 2f * sc), UI_FRAME));
    y += 14f * sc;
    f.Add(Txt("Refineries " + refineries.Count, off + new Vector2(W * 0.05f, y), 0.62f * sc, UI_DIM, TextAlignment.LEFT));
    f.Add(Txt("Assemblers " + assemblers.Count, off + new Vector2(W * 0.95f, y), 0.62f * sc, UI_DIM, TextAlignment.RIGHT));
    y += 24f * sc;
    f.Add(Txt("O2/H2 Gens " + gens.Count, off + new Vector2(W * 0.05f, y), 0.62f * sc, UI_DIM, TextAlignment.LEFT));
    f.Add(Txt("Reactors " + reactors.Count, off + new Vector2(W * 0.95f, y), 0.62f * sc, UI_DIM, TextAlignment.RIGHT));
    y += 24f * sc;
    f.Add(Txt("Tanks " + tanks.Count + "   Specials " + specials.Count, off + new Vector2(W * 0.05f, y), 0.62f * sc, UI_DIM, TextAlignment.LEFT));
    f.Add(Txt("Turrets " + guns.Count, off + new Vector2(W * 0.95f, y), 0.62f * sc, UI_DIM, TextAlignment.RIGHT));
    y += 28f * sc;
    double qTot = 0; foreach (var kv in queued) qTot += kv.Value;
    f.Add(Txt("QUEUE " + Math.Round(qTot), off + new Vector2(W * 0.05f, y), 0.7f * sc, qTot > 0 ? UI_WARNC : UI_DIM, TextAlignment.LEFT));
    f.Add(Txt("WARNINGS " + warnings.Count, off + new Vector2(W * 0.95f, y), 0.7f * sc, warnings.Count > 0 ? UI_BAD : UI_GOOD, TextAlignment.RIGHT));
    y += 27f * sc;
    f.Add(Box(off + new Vector2(W * 0.5f, y), new Vector2(W * 0.9f, 2f * sc), UI_FRAME));
    y += 10f * sc;
    int maxChars = (int)(W * 0.9f / (10.5f * sc));
    foreach (var a in actions)
    {
        if (y > H - 24f * sc) break;
        f.Add(Txt(TruncS(a, maxChars), off + new Vector2(W * 0.05f, y), 0.52f * sc, UI_DIM, TextAlignment.LEFT));
        y += 21f * sc;
    }
    if (actions.Count == 0 && y <= H - 24f * sc)
        f.Add(Txt("- no actions yet -", off + new Vector2(W * 0.05f, y), 0.52f * sc, UI_DIM, TextAlignment.LEFT));
    f.Dispose();
}

// Generic scrolling list screen. Scrolls automatically when content overflows.
// Vertical spacing in PIXELS so tall/vertical LCDs don't stretch rows apart (thanks VFox32)
void DrawList(IMyTextSurface s, string title, List<UiRow> rows, bool scrollOn)
{
    var f = Begin(s);
    Vector2 size = s.SurfaceSize, off = (s.TextureSize - size) * 0.5f;
    float W = size.X, H = size.Y, sc = Math.Min(W, H) / 512f * UI_SCALE;
    f.Add(Txt("DUTC " + title, off + new Vector2(W * 0.5f, 10f * sc), 1.05f * sc, UI_DIM));
    f.Add(Box(off + new Vector2(W * 0.5f, 46f * sc), new Vector2(W * 0.9f, 2f * sc), UI_FRAME));
    float rowPx = 26f * sc;
    float yStart = 59f * sc;
    int maxRows = Math.Max(1, (int)((H - 18f * sc - yStart) / rowPx));
    int total = rows.Count;
    int offR = 0;
    if (scrollOn && total > maxRows)
    {
        int key = s.GetHashCode();
        scroll.TryGetValue(key, out offR);
        int maxOff = total - maxRows;
        if (offR > maxOff || offR < 0) offR = 0;
        scroll[key] = offR + 2 > maxOff ? (offR >= maxOff ? 0 : maxOff) : offR + 2;
    }
    int maxChars = (int)(W * 0.55f / (10.5f * sc));
    int maxCharsWide = (int)(W * 0.9f / (10.5f * sc));
    float y = yStart;
    for (int i = offR; i < total && i < offR + maxRows; i++)
    {
        var r = rows[i];
        if (r.kind == 0)
        {
            f.Add(Txt(r.name, off + new Vector2(W * 0.5f, y), 0.72f * sc, r.col));
        }
        else if (r.kind == 1)
        {
            f.Add(Txt(TruncS(r.name, maxChars), off + new Vector2(W * 0.05f, y), 0.6f * sc, UI_TEXT, TextAlignment.LEFT));
            f.Add(Txt(r.val, off + new Vector2(W * 0.66f, y), 0.6f * sc, r.col, TextAlignment.RIGHT));
            if (r.frac >= 0)
                DrawBar(f, off + new Vector2(W * 0.825f, y + 8f * sc), W * 0.25f, 10f * sc, r.frac, r.col);
        }
        else
        {
            f.Add(Txt(TruncS(r.name, maxCharsWide), off + new Vector2(W * 0.05f, y), 0.55f * sc, r.col, TextAlignment.LEFT));
        }
        y += rowPx;
    }
    if (total > maxRows)
    {
        string ind = (offR > 0 ? "^" + offR + " " : "") + (total - offR - maxRows > 0 ? "v" + (total - offR - maxRows) : "");
        if (ind != "") f.Add(Txt(ind, off + new Vector2(W * 0.05f, 10f * sc), 0.55f * sc, UI_DIM, TextAlignment.LEFT));
    }
    if (total == 0) f.Add(Txt("- nothing to show -", off + new Vector2(W * 0.5f, H * 0.45f), 0.8f * sc, UI_DIM));
    f.Dispose();
}

List<UiRow> WarnRows()
{
    var r = new List<UiRow>();
    for (int i = 0; i < warnings.Count; i++)
        r.Add(new UiRow { kind = 2, name = (i + 1) + ". " + warnings[i].Replace("\n", " "), col = UI_TEXT });
    if (warnings.Count == 0) r.Add(new UiRow { kind = 2, name = "No problems detected", col = UI_GOOD });
    return r;
}

List<UiRow> ActRows()
{
    var r = new List<UiRow>();
    foreach (var a in actions) r.Add(new UiRow { kind = 2, name = a, col = UI_DIM });
    if (actions.Count == 0) r.Add(new UiRow { kind = 2, name = "Nothing moved yet", col = UI_DIM });
    return r;
}

// Inventory screens: plain custom data filters AND IIM-style "@N IIM-inventory" sections
void WriteInv(IMyTerminalBlock b)
{
    var prov = b as IMyTextSurfaceProvider;
    var secIdx = new List<int>();
    var secLines = new List<List<string>>();
    int cur = -1;
    foreach (var raw in b.CustomData.Split('\n'))
    {
        var l = raw.Trim();
        if (l.StartsWith("@"))
        {
            cur = -1;
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^@(\d+)");
            if (m.Success && ContainsAny(l, INV_TAGS))
            {
                int ix; int.TryParse(m.Groups[1].Value, out ix);
                secIdx.Add(ix); secLines.Add(new List<string>()); cur = secIdx.Count - 1;
            }
            continue;
        }
        if (cur >= 0) secLines[cur].Add(raw);
    }
    if (secIdx.Count == 0)
    {
        var s = SurfaceFor(b, INV_TAGS);
        if (s == null) return;
        if (b.CustomData.Trim().Length == 0)
            b.CustomData = "# DUTC-inv screen\n# One filter per line (type, item name or regex):\n#   Ore   Ingot   Component   AmmoMagazine\n#   SteelPlate   Iron   NATO\n# Options after the filter:\n#   <number> = bar max   noBar   hideEmpty   hideType   noHeading   noScroll\nComponent\n";
        RenderInv(s, b.CustomData.Split('\n'));
        return;
    }
    for (int i = 0; i < secIdx.Count; i++)
    {
        IMyTextSurface s = null;
        if (prov != null && secIdx[i] < prov.SurfaceCount) s = prov.GetSurface(secIdx[i]);
        else s = b as IMyTextSurface;
        if (s == null) continue;
        RenderInv(s, secLines[i].ToArray());
    }
}

void RenderInv(IMyTextSurface s, string[] lines)
{
    bool noScroll = false;
    var rows = new List<UiRow>();
    foreach (var raw in lines)
    {
        var l = raw.Trim();
        if (l.Length == 0 || l.StartsWith("#")) continue;
        string low = l.ToLower();
        if (low.Contains("noscroll")) noScroll = true;
        if (low.StartsWith("echoc")) { rows.Add(new UiRow { kind = 0, name = l.Substring(5).Trim(), col = UI_DIM }); continue; }
        if (low.StartsWith("echor")) { rows.Add(new UiRow { kind = 2, name = l.Substring(5).Trim(), col = UI_TEXT }); continue; }
        if (low.StartsWith("echo")) { rows.Add(new UiRow { kind = 2, name = l.Substring(4).Trim(), col = UI_TEXT }); continue; }
        if (low.Contains("=") && !low.Contains(" ")) continue;
        var tok = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string filt = tok[0].ToLower();
        double barMax = 0; bool noBar = false, hideEmpty = false, hideType = false, noHeading = false;
        for (int i = 1; i < tok.Length; i++)
        {
            string o = tok[i].ToLower();
            if (o == "nobar") noBar = true;
            else if (o == "hideempty") hideEmpty = true;
            else if (o == "hidetype") hideType = true;
            else if (o == "noheading") noHeading = true;
            else if (o == "noscroll") noScroll = true;
            else if (o == "singleline") { }
            else double.TryParse(o, out barMax);
        }
        var matches = new List<KeyValuePair<MyItemType, double>>();
        foreach (var kv in stock) if (MatchItem(kv.Key, filt)) matches.Add(kv);
        matches.Sort((a, z) => z.Value.CompareTo(a.Value));
        if (!noHeading) rows.Add(new UiRow { kind = 0, name = tok[0].ToUpper(), col = UI_DIM });
        if (matches.Count == 0) { rows.Add(new UiRow { kind = 2, name = "- nothing found -", col = UI_DIM }); continue; }
        double relMax = barMax;
        if (relMax <= 0) foreach (var kv in matches) if (kv.Value > relMax) relMax = kv.Value;
        foreach (var kv in matches)
        {
            if (hideEmpty && kv.Value < 1) continue;
            string nm = kv.Key.SubtypeId;
            if (kv.Key.ToString().EndsWith("Ingot/Stone")) nm = "Gravel";
            if (!hideType) nm += " (" + kv.Key.TypeId.Replace("MyObjectBuilder_", "").Substring(0, 2) + ")";
            int cc = Cat(kv.Key);
            rows.Add(new UiRow
            {
                kind = 1,
                name = nm,
                val = Num(kv.Value),
                frac = noBar ? -1 : (relMax > 0 ? kv.Value / relMax : 0),
                col = cc >= 0 ? catColors[cc] : UI_TEXT
            });
        }
    }
    if (rows.Count == 0)
    {
        rows.Add(new UiRow { kind = 0, name = "NO FILTERS SET", col = UI_WARNC });
        rows.Add(new UiRow { kind = 2, name = "Open this LCD's Custom Data (K)", col = UI_TEXT });
        rows.Add(new UiRow { kind = 2, name = "and add one filter per line:", col = UI_TEXT });
        rows.Add(new UiRow { kind = 2, name = "  Component", col = UI_DIM });
        rows.Add(new UiRow { kind = 2, name = "  Ingot", col = UI_DIM });
        rows.Add(new UiRow { kind = 2, name = "  Ore hideEmpty", col = UI_DIM });
        rows.Add(new UiRow { kind = 2, name = "  SteelPlate 5000", col = UI_DIM });
        rows.Add(new UiRow { kind = 2, name = "Or clear Custom Data completely", col = UI_TEXT });
        rows.Add(new UiRow { kind = 2, name = "for an automatic template.", col = UI_TEXT });
    }
    DrawList(s, "ITEM STOCK", rows, !noScroll);
}

bool MatchItem(MyItemType t, string filt)
{
    string s = (t.TypeId + "/" + t.SubtypeId).ToLower();
    try { return System.Text.RegularExpressions.Regex.IsMatch(s, filt); }
    catch { return s.Contains(filt); }
}

IMyTextSurface SurfaceFor(IMyTerminalBlock b, string[] tags)
{
    int idx = 0; bool found = false;
    foreach (var tag in tags)
    {
        int p = b.CustomName.IndexOf(tag + ":");
        if (p >= 0)
        {
            int.TryParse(System.Text.RegularExpressions.Regex.Match(b.CustomName.Substring(p + tag.Length + 1), @"^\d+").Value, out idx);
            found = true; break;
        }
        var m = System.Text.RegularExpressions.Regex.Match(b.CustomData, @"@(\d+)[^\n]*" + tag);
        if (m.Success) { int.TryParse(m.Groups[1].Value, out idx); found = true; break; }
        if (b.CustomName.Contains(tag)) { found = true; break; }
    }
    if (!found) return null;
    var prov = b as IMyTextSurfaceProvider;
    if (prov != null && idx < prov.SurfaceCount) return prov.GetSurface(idx);
    return b as IMyTextSurface;
}

// ----- sprite helpers (PowerDeck style) -----

MySpriteDrawFrame Begin(IMyTextSurface s)
{
    s.ContentType = ContentType.SCRIPT;
    s.Script = "";
    var f = s.DrawFrame();
    f.Add(Box((s.TextureSize - s.SurfaceSize) * 0.5f + s.SurfaceSize * 0.5f, s.SurfaceSize, UI_BG));
    return f;
}

MySprite Box(Vector2 pos, Vector2 size, Color c)
{
    return new MySprite() { Type = SpriteType.TEXTURE, Data = "SquareSimple",
        Position = pos, Size = size, Color = c, Alignment = TextAlignment.CENTER };
}

MySprite Txt(string text, Vector2 pos, float scale, Color c, TextAlignment al = TextAlignment.CENTER)
{
    return new MySprite() { Type = SpriteType.TEXT, Data = text, Position = pos,
        RotationOrScale = scale, Color = c, Alignment = al, FontId = "White" };
}

void DrawBar(MySpriteDrawFrame f, Vector2 center, float w, float h, double frac, Color col)
{
    float t = 2f;
    f.Add(Box(center, new Vector2(w + t * 2, h + t * 2), UI_FRAME));
    f.Add(Box(center, new Vector2(w, h), UI_BG));
    float fw = (float)(w * Math.Max(0, Math.Min(1, frac)));
    if (fw > 1) f.Add(Box(center + new Vector2(-w * 0.5f + fw * 0.5f, 0), new Vector2(fw, h), col));
}

string TruncS(string t, int max)
{
    return max > 2 && t.Length > max ? t.Substring(0, max - 2) + ".." : t;
}
