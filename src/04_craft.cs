// ---------- MODULE: autocrafting (Custom Data driven, sprite UI) ----------
// Wanted amounts live in the Autocrafting LCD's CUSTOM DATA (prefilled automatically).
// The screen itself is a sprite status display and auto-scrolls.
// Manual blueprint override for stubborn/modded items:  Name=100 BP:ExactBlueprintSubtype

string CRAFT_MARKER = "# DUTC Autocrafting";
// amounts THIS script queued, per blueprint - we only ever remove our own queue
// entries, so other scripts (e.g. Build & Repair) keep theirs untouched
Dictionary<MyDefinitionId,double> ourQueued = new Dictionary<MyDefinitionId,double>();
// community-confirmed blueprint names no pattern can guess (thanks aantono)
Dictionary<string,string> knownBp = new Dictionary<string,string>
{
    { "AQD_Comp_Concrete", "AQD_BP_StoneIngot_To_Concrete" },
    { "DeuteriumContainmentUnit", "IceToDeuterium" },             // Star Trek Mod Pack (thanks Jasper + aantono)
    { "AntideuteriumContainmentUnit", "IceToAntideuterium" },
    { "ATLASAmmoMagazine", "40mmATLASBP" }                        // Aryx Weapon Enterprises (thanks The Burger Buster)
};
// blueprints seen queued by hand in any assembler, newest first - the script
// adopts them for matching items, Isy style (thanks The Burger Buster)
List<MyDefinitionId> learnedBps = new List<MyDefinitionId>();
HashSet<MyDefinitionId> learnedSet = new HashSet<MyDefinitionId>();
// per-name sweep progress: interrupted scans RESUME instead of restarting, so
// uncraftable items (Fruit...) always reach a final NoBP verdict (thanks Oxnard)
Dictionary<string,int> probePos = new Dictionary<string,int>();
string[] vanillaCraft = { "SteelPlate","InteriorPlate","Construction","MetalGrid","SmallTube","LargeTube","Motor","Display","BulletproofGlass","Computer","Reactor","Thrust","GravityGenerator","Medical","RadioCommunication","Detector","Explosives","Girder","SolarCell","PowerCell","Superconductor","Canvas" };
// prefilled onto the list even when none are in stock yet, so ammo, tools and
// bottles show up without having to craft one first (thanks Oxnard)
string[] vanillaEnsure = { "NATO_25x184mm","Missile200mm","AutocannonClip","MediumCalibreAmmo","LargeCalibreAmmo","SmallRailgunAmmo","LargeRailgunAmmo","AutomaticRifleGun_Mag_20rd","PreciseAutomaticRifleGun_Mag_5rd","RapidFireAutomaticRifleGun_Mag_50rd","UltimateAutomaticRifleGun_Mag_30rd","SemiAutoPistolMagazine","FullAutoPistolMagazine","ElitePistolMagazine","FlareClip","AngleGrinderItem","AngleGrinder2Item","AngleGrinder3Item","AngleGrinder4Item","WelderItem","Welder2Item","Welder3Item","Welder4Item","HandDrillItem","HandDrill2Item","HandDrill3Item","HandDrill4Item","OxygenBottle","HydrogenBottle" };
string lastAsmSig = "";
int probeBudget = 0;

class CRow
{
    public string name = "";
    public double cur;
    public double want;
    public int state;   // 0 = normal, -1 = NoBP, -2 = ignored, -3 = still checking, -4 = bad BP: override
    public double inQ;
    public double disQ;
}
List<CRow> craftRows = new List<CRow>();

bool Craft()
{
    if (!enableAutocrafting || craftLCDs.Count == 0) return true;
    // the master list is the panel CARRYING THE MARKER, not whichever sorts first.
    // The 2.0.6 panel reordering could crown a second master, leaving two competing
    // lists - edits on one were silently shadowed by stale duplicates on the other
    // (thanks The Burger Buster). Stray markers get retired on sight.
    IMyTextPanel master = null;
    foreach (var p in craftLCDs) if (p.CustomData.Contains(CRAFT_MARKER)) { master = p; break; }
    if (master == null) { master = craftLCDs[0]; SetupCraftCD(master); }
    foreach (var p in craftLCDs)
        if (p != master && p.CustomData.Contains(CRAFT_MARKER))
            p.CustomData = p.CustomData.Replace(CRAFT_MARKER, "# list lives on the marker panel now");
    // no assembler is no reason for a BLANK screen - draw it and say why (thanks Enig)
    if (assemblers.Count == 0) { Warn("Autocrafting: no usable assemblers!"); craftRows.Clear(); DrawCraftScreens(); return true; }
    probeBudget = 1;
    // quick-probe misses are re-checked occasionally (a new assembler type may
    // have arrived), not every cycle - modded junk items stay cheap (thanks EBALL360)
    if (cycles % 60 == 0) quickMiss.Clear();
    string asmSig = distinctAsm.Count + "/" + assemblers.Count;
    if (asmSig != lastAsmSig) { lastAsmSig = asmSig; quickMiss.Clear(); }
    // assemblers we flipped to disassembly go back to assembly once they finish
    if (ourDisAsm.Count > 0)
    {
        var doneD = new List<long>();
        foreach (var a in assemblers)
            if (ourDisAsm.Contains(a.EntityId) && a.Mode == MyAssemblerMode.Disassembly && a.IsQueueEmpty)
            { a.Mode = MyAssemblerMode.Assembly; doneD.Add(a.EntityId); }
        foreach (var id in doneD) ourDisAsm.Remove(id);
    }
    queued.Clear();
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        if (a.Mode == MyAssemblerMode.Disassembly) continue;
        q.Clear();
        try { a.GetQueue(q); } catch { continue; }
        foreach (var pi in q)
        {
            double amt; queued.TryGetValue(pi.BlueprintId, out amt);
            queued[pi.BlueprintId] = amt + (double)pi.Amount;
            if (learnedSet.Add(pi.BlueprintId))
            {
                learnedBps.Insert(0, pi.BlueprintId);
                if (learnedBps.Count > 100) { learnedSet.Remove(learnedBps[100]); learnedBps.RemoveAt(100); }
            }
        }
    }
    // crafts completed since last pass: shrink our tracker to what is actually queued
    var okeys = new List<MyDefinitionId>(ourQueued.Keys);
    foreach (var k in okeys)
    {
        double actual; queued.TryGetValue(k, out actual);
        if (ourQueued[k] > actual) ourQueued[k] = actual;
    }
    var entries = new List<string[]>();
    var seen = new HashSet<string>();
    // master parses FIRST: its values always win over leftovers on other panels
    var panels = new List<IMyTextPanel>();
    panels.Add(master);
    foreach (var p in craftLCDs) if (p != master) panels.Add(p);
    foreach (var p in panels)
    {
        foreach (var raw in p.CustomData.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string name = l.Substring(0, eq).Trim();
            // pasted full paths work too: "MyObjectBuilder_SeedItem/Grain" counts the
            // SEEDS, not the crop - without this the processor loops forever (thanks Froman Joe)
            if (name.StartsWith("MyObjectBuilder_")) name = name.Substring(16);
            if (name.Equals("UIScale", StringComparison.OrdinalIgnoreCase)) continue;
            string val = l.Substring(eq + 1).Trim();
            string bpOv = "";
            // BP: captures to the END of the line - modded blueprint subtypes can
            // contain spaces, so put BP: last (thanks The Burger Buster)
            var mo = System.Text.RegularExpressions.Regex.Match(val, @"(?i)bp:\s*(.+)$");
            if (mo.Success)
            {
                bpOv = mo.Groups[1].Value.Trim(); val = val.Replace(mo.Value, "");
                // IIM lists print blueprints as full paths - keep only the subtype
                int sl = bpOv.LastIndexOf('/');
                if (sl >= 0) bpOv = bpOv.Substring(sl + 1);
            }
            double want;
            double.TryParse(System.Text.RegularExpressions.Regex.Match(val, @"\d+").Value, out want);
            string mods = System.Text.RegularExpressions.Regex.Replace(val, @"[\d\.\s]", "").ToUpper();
            if (name == "" || seen.Contains(name)) continue;
            seen.Add(name);
            entries.Add(new[] { name, want.ToString(), mods, bpOv });
        }
    }

    var newLines = new StringBuilder();
    foreach (var kv in stock)
    {
        var t = kv.Key;
        if (!t.TypeId.EndsWith("_Component") && !t.TypeId.EndsWith("_AmmoMagazine") && Cat(t) != FOOD && !t.TypeId.EndsWith("_PhysicalGunObject") && !t.TypeId.EndsWith("_PhysicalObject")) continue;
        // seed items list under their qualified name, so "SeedItem/Grain" counts
        // seeds and plain "Grain" keeps counting the crop (thanks Froman Joe)
        string anm = t.SubtypeId;
        string shortTy = t.TypeId.Replace("MyObjectBuilder_", "");
        if (shortTy == "SeedItem" || shortTy == "Seed" || shortTy == "Seeds") anm = shortTy + "/" + t.SubtypeId;
        if (seen.Contains(anm)) continue;
        if (quickMiss.Contains(anm)) continue;
        MyDefinitionId bp0;
        int st0 = BpState(anm, out bp0, false);
        if (st0 != 1) { if (st0 == 0) quickMiss.Add(anm); continue; }
        seen.Add(anm);
        entries.Add(new[] { anm, "0", "", "" });
        newLines.Append(anm + "=0\n");
    }
    foreach (var v in vanillaEnsure)
    {
        if (seen.Contains(v) || quickMiss.Contains(v)) continue;
        MyDefinitionId bp1;
        int st1 = BpState(v, out bp1, false);
        if (st1 != 1) { if (st1 == 0) quickMiss.Add(v); continue; }
        seen.Add(v);
        entries.Add(new[] { v, "0", "", "" });
        newLines.Append(v + "=0\n");
    }
    if (newLines.Length > 0)
        master.CustomData = master.CustomData.TrimEnd('\n') + "\n" + newLines.ToString().TrimEnd('\n');

    entries.Sort((a, z) => a[0].CompareTo(z[0]));

    craftRows.Clear();
    foreach (var e in entries)
    {
        string name = e[0]; string mods = e[2];
        double want; double.TryParse(e[1], out want);
        MyItemType t;
        double cur = byName.TryGetValue(name, out t) ? CountOfCraft(t) : 0;
        if (mods.Contains("I")) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -2 }); continue; }
        bool ovFail = false;
        if (e[3] != "" && !bpCache.ContainsKey(name))
        {
            MyDefinitionId ov;
            if (TestBp(e[3], out ov)) { bpCache[name] = ov; noBp.Remove(name); }
            else ovFail = true;
        }
        MyDefinitionId bp;
        // deep blueprint searching only for items someone actually WANTS - inventory
        // clutter (raw meats, carcasses...) never triggers the sweep (thanks Oxnard).
        // A D row needs its blueprint even at want=0 (melt everything).
        bool hasD = mods.Contains("D");
        int st = BpState(name, out bp, want > 0 || hasD);
        // a BP: override that didn't resolve shows as BP? - the override name is
        // wrong or no present assembler accepts it (thanks The Burger Buster)
        if (st != 1 && ovFail) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -4 }); continue; }
        if (st == 0 && want <= 0 && !hasD) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = 0 }); continue; }
        if (st == 0) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -3 }); continue; }
        if (st == -1) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -1 }); continue; }
        double inQ; queued.TryGetValue(bp, out inQ);
        // craft to the EXACT quota - the old 5% margin stopped short and sat green
        // at 4892/5000 (thanks Oxnard + The Burger Buster)
        if (want > 0 && cur + inQ < want - 0.01)
        {
            double need = want - cur - inQ;
            if (need >= 1)
            {
                QueueBp(bp, need, mods.Contains("P"));
                queued[bp] = inQ + need;
                inQ += need;
                double om; ourQueued.TryGetValue(bp, out om);
                ourQueued[bp] = om + need;
                Act("Queued " + Math.Round(need) + " " + name);
            }
        }
        else if (cur >= want && inQ > 0)
        {
            double removed = RemoveBp(bp);
            inQ = Math.Max(0, inQ - removed);
            queued[bp] = inQ;
        }
        // D modifier: melt the surplus back down (thanks PriorityZer0 + aantono).
        // Works at want=0 too: Item=0D means keep none, melt everything (thanks The Burger Buster)
        double disQ = 0;
        if (allowDisassembly && hasD)
        {
            disQ = DisQueueOf(bp);
            if (cur > want * (1 + craftMargin))
            {
                double excess = cur - want - disQ;
                if (excess >= 1) disQ += QueueDis(bp, Math.Min(excess, 500), name);
            }
            else if (disQ > 0 && cur <= want) { CancelDis(bp); disQ = 0; }
        }
        craftRows.Add(new CRow { name = name, cur = cur, want = want, state = 0, inQ = inQ, disQ = disQ });
    }
    if (balanceAssemblers) RebalanceAsm();
    DrawCraftScreens();
    return true;
}

// fast assemblers run dry while slow ones still hold a long queue - an idle
// assembler takes half the tail off the busiest one. Only amounts THIS script
// queued are moved; Build Planner / Nanobot entries stay put (thanks jokerace45)
void RebalanceAsm()
{
    var q = new List<MyProductionItem>();
    foreach (var idle in assemblers)
    {
        if (!idle.IsQueueEmpty || idle.CooperativeMode) continue;
        if (idle.Mode == MyAssemblerMode.Disassembly || ourDisAsm.Contains(idle.EntityId)) continue;
        if (Runtime.CurrentInstructionCount > 30000) return;
        IMyAssembler rich = null; int richIdx = -1; double richAmt = 0; MyDefinitionId richBp = new MyDefinitionId();
        foreach (var a in assemblers)
        {
            if (a == idle || a.Mode == MyAssemblerMode.Disassembly || a.IsQueueEmpty) continue;
            q.Clear();
            try { a.GetQueue(q); } catch { continue; }
            if (q.Count == 0) continue;
            // only the LAST queue entry is considered - that is where this script appends
            int i = q.Count - 1;
            double own; ourQueued.TryGetValue(q[i].BlueprintId, out own);
            double amt = Math.Min((double)q[i].Amount, own);
            if (amt < 2) continue;
            bool can; try { can = idle.CanUseBlueprint(q[i].BlueprintId); } catch { can = false; }
            if (!can) continue;
            if (amt > richAmt) { rich = a; richIdx = i; richAmt = amt; richBp = q[i].BlueprintId; }
        }
        if (rich == null) continue;
        double move = Math.Floor(richAmt / 2);
        if (move < 1) continue;
        try
        {
            rich.RemoveQueueItem(richIdx, (VRage.MyFixedPoint)move);
            idle.AddQueueItem(richBp, (VRage.MyFixedPoint)move);
            Act("Rebalanced " + Math.Round(move) + " " + richBp.SubtypeName);
        }
        catch { }
    }
}

// First-time setup: header, migrate old text-format entries (v1.1 / IIM), prefill vanilla craftables.
void SetupCraftCD(IMyTextPanel master)
{
    var sbcd = new StringBuilder();
    sbcd.Append(CRAFT_MARKER + "\n");
    sbcd.Append("# Edit the number = wanted stock. The screen only displays status.\n");
    sbcd.Append("# Modifiers after the number:  P = craft first (priority),  I = ignore\n");
    sbcd.Append("#   D = disassemble the excess above the wanted amount (e.g. SteelPlate=1000D)\n");
    sbcd.Append("# Modded item stuck on NoBP? Force its blueprint: Name=100 BP:BlueprintSubtype\n");
    sbcd.Append("# Blueprint name unknown? Queue the item once by hand and hover it in the assembler's production queue.\n");
    sbcd.Append("# Exact ITEM name unknown? Run the PB with argument:  items <part of the name>\n");
    sbcd.Append("# BP: not resolving? Run the PB with argument:  bp <the blueprint name>\n");
    sbcd.Append("# New craftable items get added here automatically.\n");
    var have = new HashSet<string>();
    foreach (var p in craftLCDs)
    {
        foreach (var raw in p.GetText().Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("=") || l.StartsWith("#") || l.StartsWith("DUTC") || l.StartsWith("Item")) continue;
            var tok = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 2) continue;
            string wantTok = null;
            for (int i = tok.Length - 1; i >= 1; i--)
            {
                if (tok[i].Contains("[") || tok[i].Contains("]")) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(tok[i], @"\d")) { wantTok = tok[i]; break; }
            }
            if (wantTok == null) continue;
            double w;
            double.TryParse(System.Text.RegularExpressions.Regex.Match(wantTok, @"\d+").Value, out w);
            string mods = System.Text.RegularExpressions.Regex.Replace(wantTok, @"[\d\.]", "").ToUpper().Replace("A", "").Replace("H", "");
            if (have.Contains(tok[0])) continue;
            have.Add(tok[0]);
            sbcd.Append(tok[0] + "=" + Math.Round(w) + mods + "\n");
        }
    }
    foreach (var v in vanillaCraft)
    {
        if (have.Contains(v)) continue;
        MyDefinitionId bp;
        if (BpState(v, out bp, false) != 1) continue;
        have.Add(v);
        sbcd.Append(v + "=0\n");
    }
    master.CustomData = sbcd.ToString().TrimEnd('\n') + (master.CustomData.Trim().Length > 0 ? "\n" + master.CustomData : "");
    Act("Autocrafting list written to Custom Data of " + StripPct(master.CustomName));
}

void DrawCraftScreens()
{
    int start = 0;
    for (int p = 0; p < craftLCDs.Count; p++)
    {
        bool last = p == craftLCDs.Count - 1;
        start += DrawCraftPanel(craftLCDs[p], p, start, last, UiScaleFor(craftLCDs[p]));
    }
}

int DrawCraftPanel(IMyTextSurface s, int index, int start, bool last, float us)
{
    var f = Begin(s);
    Vector2 size = s.SurfaceSize, off = (s.TextureSize - size) * 0.5f;
    float W = size.X, H = size.Y, sc = Math.Min(W, H) / 512f * us;
    string hd = "DUTC AUTOCRAFTING" + (craftLCDs.Count > 1 ? " " + (index + 1) + "/" + craftLCDs.Count : "");
    f.Add(Txt(hd, off + new Vector2(W * 0.5f, 10f * sc), 1.05f * sc, UI_DIM));
    int okC = 0, craftC = 0, lowC = 0, nobpC = 0, chkC = 0;
    foreach (var r in craftRows)
    {
        if (r.state == -1) nobpC++;
        else if (r.state == -3) chkC++;
        else if (r.state == 0 && r.want > 0)
        {
            if (r.cur >= r.want - 0.5) okC++;
            else if (r.inQ > 0) craftC++;
            else lowC++;
        }
    }
    string counts = okC + " ok   " + craftC + " crafting   " + lowC + " low   " + nobpC + " noBP";
    if (chkC > 0) counts += "   " + chkC + " checking";
    counts += "   |   edit Custom Data";
    f.Add(Txt(counts, off + new Vector2(W * 0.5f, 38f * sc), 0.55f * sc, UI_DIM));
    f.Add(Box(off + new Vector2(W * 0.5f, 64f * sc), new Vector2(W * 0.9f, 2f * sc), UI_FRAME));
    float rowPx = 26f * sc;
    float yStart = 77f * sc;
    int maxRows = Math.Max(1, (int)((H - 18f * sc - yStart) / rowPx));
    int total = craftRows.Count;
    int offR = start;
    if (last)
    {
        int remaining = total - start;
        if (remaining > maxRows)
        {
            int key = s.GetHashCode();
            int so; scroll.TryGetValue(key, out so);
            int maxOff = remaining - maxRows;
            if (so > maxOff || so < 0) so = 0;
            scroll[key] = so + 2 > maxOff ? (so >= maxOff ? 0 : maxOff) : so + 2;
            offR = start + so;
            string ind = (so > 0 ? "^" + so + " " : "") + (maxOff - so > 0 ? "v" + (maxOff - so) : "");
            if (ind != "") f.Add(Txt(ind, off + new Vector2(W * 0.05f, 10f * sc), 0.55f * sc, UI_DIM, TextAlignment.LEFT));
        }
    }
    int maxChars = (int)(W * 0.42f / (10.5f * sc));
    float y = yStart;
    int drawn = 0;
    for (int i = offR; i < total && drawn < maxRows; i++)
    {
        var r = craftRows[i];
        Color col; string valTxt; double frac = -1;
        if (r.state == -2) { col = UI_DIM; valTxt = "ignored"; }
        else if (r.state == -3) { col = UI_DIM; valTxt = Num(r.cur) + "  checking.."; }
        else if (r.state == -4) { col = UI_BAD; valTxt = Num(r.cur) + "  BP?"; }
        else if (r.state == -1) { col = UI_BAD; valTxt = Num(r.cur) + "  NoBP"; }
        else
        {
            frac = r.want > 0 ? Math.Min(1, r.cur / r.want) : -1;
            // green means the FULL quota, not "within 5%" (thanks Oxnard + The Burger Buster)
            col = r.want <= 0 ? UI_DIM : r.cur >= r.want - 0.5 ? (r.disQ > 0 ? UI_WARNC : UI_GOOD) : r.inQ > 0 ? UI_WARNC : UI_BAD;
            valTxt = Num(r.cur) + " / " + Num(r.want) + (r.inQ > 0 ? " +" + Math.Round(r.inQ) : "") + (r.disQ > 0 ? " -" + Math.Round(r.disQ) : "");
        }
        f.Add(Txt(TruncS(r.name, maxChars), off + new Vector2(W * 0.05f, y), 0.6f * sc, UI_TEXT, TextAlignment.LEFT));
        f.Add(Txt(valTxt, off + new Vector2(W * 0.70f, y), 0.55f * sc, col, TextAlignment.RIGHT));
        if (frac >= 0) DrawBar(f, off + new Vector2(W * 0.845f, y + 8f * sc), W * 0.25f, 10f * sc, frac, col);
        y += rowPx; drawn++;
    }
    if (total == 0) f.Add(Txt(assemblers.Count == 0 ? "No usable assembler on this grid" : "No craftable items known yet", off + new Vector2(W * 0.5f, H * 0.45f), 0.7f * sc, assemblers.Count == 0 ? UI_WARNC : UI_DIM));
    f.Dispose();
    return last ? Math.Max(0, total - start) : maxRows;
}

// total amount of bp sitting in disassembly-mode queues
double DisQueueOf(MyDefinitionId bp)
{
    double tot = 0;
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        if (a.Mode != MyAssemblerMode.Disassembly) continue;
        q.Clear();
        try { a.GetQueue(q); } catch { continue; }
        foreach (var pi in q) if (pi.BlueprintId == bp) tot += (double)pi.Amount;
    }
    return tot;
}

// queue a disassembly job: reuse an assembler already in disassembly mode, else
// borrow ONE idle assembler (flipped back to assembly the moment it finishes)
double QueueDis(MyDefinitionId bp, double amount, string name)
{
    IMyAssembler pick = null;
    foreach (var a in assemblers)
    {
        if (a.Mode != MyAssemblerMode.Disassembly) continue;
        bool can; try { can = a.CanUseBlueprint(bp); } catch { can = false; }
        if (can) { pick = a; break; }
    }
    if (pick == null && ourDisAsm.Count == 0)
    {
        foreach (var a in assemblers)
        {
            if (!a.IsQueueEmpty || a.CooperativeMode) continue;
            bool can; try { can = a.CanUseBlueprint(bp); } catch { can = false; }
            if (!can) continue;
            a.Mode = MyAssemblerMode.Disassembly;
            a.UseConveyorSystem = true;
            ourDisAsm.Add(a.EntityId);
            pick = a; break;
        }
    }
    if (pick == null) return 0;
    double amt = Math.Floor(amount);
    try { pick.AddQueueItem(bp, (VRage.MyFixedPoint)amt); }
    catch { return 0; }
    Act("Disassembling " + Math.Round(amt) + " " + name);
    return amt;
}

// only cancels disassembly jobs on assemblers WE flipped - a manual disassembly
// run set up by the player is never touched
void CancelDis(MyDefinitionId bp)
{
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        if (a.Mode != MyAssemblerMode.Disassembly || !ourDisAsm.Contains(a.EntityId)) continue;
        q.Clear();
        try { a.GetQueue(q); } catch { continue; }
        for (int i = q.Count - 1; i >= 0; i--)
            if (q[i].BlueprintId == bp) { try { a.RemoveQueueItem(i, q[i].Amount); } catch { } }
    }
}

void QueueBp(MyDefinitionId bp, double need, bool prio)
{
    var usable = new List<IMyAssembler>();
    foreach (var a in assemblers)
    {
        try
        {
            if (ourDisAsm.Contains(a.EntityId)) continue;
            if (!a.CanUseBlueprint(bp)) continue;
            if (a.Mode == MyAssemblerMode.Disassembly)
            {
                if (a.IsQueueEmpty) a.Mode = MyAssemblerMode.Assembly; else continue;
            }
            usable.Add(a);
        }
        catch { }
    }
    if (usable.Count == 0) { Warn("No assembler can craft " + bp.SubtypeName); return; }
    double chunk = Math.Ceiling(need / usable.Count);
    foreach (var a in usable)
    {
        double amt = Math.Min(chunk, need);
        if (amt < 1) break;
        try
        {
            if (prio) a.InsertQueueItem(0, bp, (VRage.MyFixedPoint)amt);
            else a.AddQueueItem(bp, (VRage.MyFixedPoint)amt);
        }
        catch (Exception ex)
        {
            Warn("Assembler '" + a.CustomName + "' failed to queue " + bp.SubtypeName + ": " + ex.Message);
        }
        need -= amt;
    }
}

// removes at most the amount THIS script queued; foreign queue entries survive
double RemoveBp(MyDefinitionId bp)
{
    double allow; ourQueued.TryGetValue(bp, out allow);
    if (allow <= 0) return 0;
    double removed = 0;
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        if (allow <= 0) break;
        if (a.Mode == MyAssemblerMode.Disassembly) continue;
        q.Clear();
        try { a.GetQueue(q); } catch { continue; }
        for (int i = q.Count - 1; i >= 0 && allow > 0; i--)
        {
            if (q[i].BlueprintId != bp) continue;
            double take = Math.Min(allow, (double)q[i].Amount);
            try { a.RemoveQueueItem(i, (VRage.MyFixedPoint)take); } catch { continue; }
            allow -= take; removed += take;
        }
    }
    ourQueued[bp] = 0;
    return removed;
}

// Blueprint resolver. Returns 1 = found (cached), -1 = definitely none, 0 = still probing.
// Heavy prefix sweeps are limited to one item per cycle so the PB never overruns.
int BpState(string name, out MyDefinitionId bp, bool deep)
{
    bp = new MyDefinitionId();
    MyDefinitionId cached;
    if (bpCache.TryGetValue(name, out cached)) { bp = cached; return 1; }
    if (noBp.Contains(name)) return -1;
    if (assemblers.Count == 0) return 0;
    string mapped;
    if (knownBp.TryGetValue(name, out mapped) && TestBp(mapped, out bp)) { bpCache[name] = bp; return 1; }
    // "Type/Sub" names target a specific item type: SeedItem/Grain=10 extracts seed
    // packs while plain Grain=100 grows the crop (thanks aantono)
    string core = name;
    string typePrefix = "";
    int slash = name.IndexOf('/');
    if (slash > 0) { typePrefix = name.Substring(0, slash); core = name.Substring(slash + 1); }
    bool isSeed = typePrefix == "SeedItem" || typePrefix == "Seed" || typePrefix == "Seeds";
    string baseN = core.Replace("Item", "");
    int lastUnder = baseN.LastIndexOf('_');
    string trimmed = lastUnder > 0 ? baseN.Substring(0, lastUnder) : baseN;
    var candidates = new HashSet<string>();
    if (isSeed)
    {
        candidates.Add("Seeds_" + core); candidates.Add("Seed_" + core); candidates.Add("Spores_" + core);
        candidates.Add("Seeds_" + baseN); candidates.Add("Spores_" + baseN);
        candidates.Add(core + "_Seed"); candidates.Add(core + "_Seeds");
    }
    if (typePrefix != "") candidates.Add(typePrefix + "_" + core);
    candidates.Add(core); candidates.Add(baseN); candidates.Add(trimmed);
    candidates.Add(core + "Component"); candidates.Add(baseN + "Component");
    candidates.Add(core + "Magazine"); candidates.Add(baseN + "Magazine");
    candidates.Add(core + "Item"); candidates.Add(baseN + "Item");
    candidates.Add(core + "_ApexSurvivalAdditions"); candidates.Add(baseN + "_ApexSurvivalAdditions");
    foreach (var s in candidates)
        if (TestBp(s, out bp)) { bpCache[name] = bp; return 1; }
    // blueprints LEARNED from hand-queued crafts: adopt one whose core name matches
    // this item's - queue a stubborn modded item once and the script picks its
    // blueprint up by itself, Isy style (thanks The Burger Buster)
    string coreN = BpCore(baseN).ToUpper();
    if (coreN.Length >= 5)
    {
        foreach (var lb in learnedBps)
        {
            string lc = BpCore(lb.SubtypeName).ToUpper();
            if (lc.Length < 4) continue;
            if (lc != coreN && !lc.Contains(coreN) && !coreN.Contains(lc)) continue;
            MyDefinitionId t2;
            if (TestBp(lb.SubtypeName, out t2)) { bpCache[name] = t2; return 1; }
        }
    }
    if (!deep) return 0;
    if (probeBudget <= 0 || Runtime.CurrentInstructionCount > 28000) return 0;
    probeBudget--;
    // deep sweep, RESUMABLE: pos remembers how far we got last cycle
    var candList = new List<string>(candidates);
    int pos; probePos.TryGetValue(name, out pos);
    int idx = 0;
    // 2-digit prefix mods (ASA aeroponics crops etc.): 01Crop / 01_Crop / Position01_Crop (thanks aantono)
    for (int ci = 0; ci < candList.Count; ci++)
    {
        for (int i = 1; i <= 35; i++)
        {
            string p2 = i.ToString("00");
            for (int v = 0; v < 3; v++)
            {
                if (idx++ < pos) continue;
                string sub = v == 0 ? p2 + candList[ci] : v == 1 ? p2 + "_" + candList[ci] : "Position" + p2 + "_" + candList[ci];
                if (TestBp(sub, out bp)) { probePos.Remove(name); bpCache[name] = bp; return 1; }
                if (Runtime.CurrentInstructionCount > 20000) { probePos[name] = idx; return 0; }
            }
        }
    }
    // modded consumables (Apex food etc.) use Position numbers well above 200 (thanks aantono)
    for (int n = 1; n <= 400; n = n < 30 ? n + 1 : n + 5)
    {
        string pre = "Position" + n.ToString("0000") + "_";
        for (int ci = 0; ci < candList.Count; ci++)
        {
            if (idx++ < pos) continue;
            if (TestBp(pre + candList[ci], out bp)) { probePos.Remove(name); bpCache[name] = bp; return 1; }
            if (Runtime.CurrentInstructionCount > 22000) { probePos[name] = idx; return 0; }
        }
    }
    probePos.Remove(name);
    noBp.Add(name);
    return -1;
}

// strip the decorations mods put around the shared core of an item and its
// blueprint: "ATLASAmmoMagazine" and "40mmATLASBP" both reduce to "ATLAS"
string BpCore(string s)
{
    s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)^(Position\d+_|\d+mm)", "");
    for (int i = 0; i < 2; i++)
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)(AmmoMagazine|MagDef|Magazine|Component|Ammo|Item|Mag|Def|BP)$", "");
    return s;
}

// PB argument "bps": blueprints seen queued in your assemblers, newest first -
// queue a stubborn item once by hand and copy its exact blueprint name from here
void BpsCmd()
{
    var sb = new StringBuilder();
    sb.Append("Blueprints seen in assembler queues:\n");
    int n = 0;
    foreach (var b in learnedBps)
    {
        sb.Append(b.SubtypeName + "\n");
        if (++n >= 20) { sb.Append("...more truncated"); break; }
    }
    if (n == 0) sb.Append("none yet - queue something by hand first");
    report = sb.ToString();
}

// PB argument "bp <name>": probe a blueprint name directly and show which
// assembler types the script can see - takes the guesswork out of BP? rows
void BpProbeCmd(string nm)
{
    var sb = new StringBuilder();
    sb.Append("BP probe '" + nm + "'\nAssembler types (" + distinctAsm.Count + "):\n");
    foreach (var d in distinctAsm)
        sb.Append("  " + (d.BlockDefinition.SubtypeName == "" ? d.BlockDefinition.TypeIdString : d.BlockDefinition.SubtypeName) + "\n");
    MyDefinitionId bp;
    if (bpCache.TryGetValue(nm, out bp)) sb.Append("cached: " + bp.SubtypeName);
    else if (TestBp(nm, out bp)) sb.Append("direct hit: " + bp.SubtypeName);
    else
    {
        noBp.Remove(nm); quickMiss.Remove(nm); probePos.Remove(nm);
        MyDefinitionId b2;
        if (BpState(nm, out b2, false) == 1) sb.Append("found by pattern: " + b2.SubtypeName);
        else sb.Append("no direct match. Caches for this name are cleared -\nset a wanted amount and the deep sweep retries it.\nNo assembler type listed above that should craft it?\nCheck it is on THIS grid, powered and not !manual.");
    }
    report = sb.ToString();
}

// PB argument "items <text>": list stock item ids matching the text, so the
// exact SubtypeId for the autocrafting list is one command away
void ItemsCmd(string f)
{
    var sb = new StringBuilder();
    sb.Append("Items matching '" + f + "':\n");
    int n = 0;
    foreach (var kv in stock)
    {
        string full = kv.Key.TypeId.Replace("MyObjectBuilder_", "") + "/" + kv.Key.SubtypeId;
        if (full.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
        sb.Append(full + "  x" + Math.Round(kv.Value) + "\n");
        if (++n >= 20) { sb.Append("...more truncated"); break; }
    }
    if (n == 0) sb.Append("nothing in stock matches");
    report = sb.ToString();
}

bool TestBp(string sub, out MyDefinitionId id)
{
    id = new MyDefinitionId();
    // retry once: MyDefinitionId.TryParse hits a non-thread-safe VRage string hash
    // dictionary and can throw when engine threads insert at the same time (thanks aantono)
    for (int attempt = 0; attempt < 2; attempt++)
    {
        try
        {
            MyDefinitionId t;
            if (!MyDefinitionId.TryParse("MyObjectBuilder_BlueprintDefinition/" + sub, out t)) return false;
            foreach (var a in distinctAsm)
                if (a.CanUseBlueprint(t)) { id = t; return true; }
            return false;
        }
        catch { }
    }
    return false;
}
