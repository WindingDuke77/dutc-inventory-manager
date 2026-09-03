// ========================================
//         DUTC INVENTORY  v2.0.3
// ========================================
// Sorting + even container balancing, auto container assignment, bottle filling
// (works!), special loadout containers, autocrafting via Custom Data with sprite
// status screen, refinery feeding + ore balancing + queue priority, ice balancing,
// uranium balancing, turret ammo loading (subgrids + docked ships), assembler
// feeding + cleanup, sprite LCDs with auto-scroll, multi-instance election
// (station beats ship), Nanobot Build & Repair fully hands-off.
//
// BACKWARD COMPATIBLE WITH ISY'S INVENTORY MANAGER:
//   - Same container keywords: Ores/Ingots/Components/Tools/Ammo/Bottles
//   - Same block keywords: Special, Locked, Hidden, !manual
//   - Same connector keywords: [No Sorting] and [No IIM]
//   - IIM LCD tags work: IIM-main, IIM-inventory, IIM-warnings, IIM-actions
//     (also reads IIM-style "@0 IIM-inventory" sections in Custom Data)
//   - IIM special container lines work: Component/SteelPlate=100, 100M, 100L, All
//   - IIM autocrafting LCD text format works: "SteelPlate 123 = 5000"
//
// QUICK START
// 1. Paste into a Programmable Block. Runs by itself, no argument needed.
// 2. Name cargo containers: Ores / Ingots / Components / Tools / Ammo / Bottles
//    (or let the script auto-assign them)
// 3. LCD tags (add to any LCD or cockpit name):
//      DUTC-main  (or IIM-main)       = status overview
//      DUTC-inv   (or IIM-inventory)  = item lists, filters in Custom Data
//      DUTC-warn  (or IIM-warnings)   = warnings
//      DUTC-act   (or IIM-actions)    = action log
//      Autocrafting                   = stock screen, edit wanted amounts in its CUSTOM DATA
//    Cockpit screens: DUTC-main:1 = second screen, :2 = third, etc.
// 4. Arguments (optional): pause / resume / reset
// 5. Multiple grids each running this script can dock safely: they elect ONE
//    active manager automatically (station beats ship, then lowest id wins).
//    The other instance goes standby but keeps its screens and machine refills.
//
// ---------------- CONFIG ----------------

string ORE_KEY = "Ores";
string INGOT_KEY = "Ingots";
string COMP_KEY = "Components";
string TOOL_KEY = "Tools";
string AMMO_KEY = "Ammo";
string BOTTLE_KEY = "Bottles";
string FOOD_KEY = "Food";           // food, ingredients and seeds get their own container type
// items that always count as Food no matter what internal type their mod gives them
string[] foodItems = { "Algae", "Grain" };

string SPECIAL_KEY = "Special";
string HIDDEN_KEY = "Hidden";
string MANUAL_KEY = "!manual";
string[] lockedKeywords = { "Locked", "Control Station", "Control Seat", "Safe Zone" };
string NO_SORT_CONNECTOR = "[No Sorting]";
string NO_IIM_CONNECTOR = "[No IIM]";

bool sortItems = true;              // master switch for sorting
bool includeDockedGrids = true;     // pull items from docked ships (protect one with [No Sorting] / [No IIM])
bool autoAssignContainers = true;   // auto-tag a cargo container when a type is missing/full
// blocks that must NEVER be used as storage (not auto-assigned, keyword tags ignored)
// but still get emptied by sorting. Matches block name or subtype, e.g. "Cargo Terminal"
string[] storageBlacklist = { };
// if non-empty, ONLY blocks matching one of these can be auto-assigned as type containers
string[] storageWhitelist = { };
bool balanceTypeContainers = true;  // split items evenly across same-type containers (9 tools / 3 boxes = 3 each)
bool showFillLevel = true;          // show (xx%) in type container names

bool fillBottles = true;            // route bottles through a tank with gas so they get refilled
bool refillStoredBottles = true;    // ALSO refill bottles already sitting in the Bottles container
int bottleRefillEvery = 30;         // every N script cycles (~2s per cycle); only containers with NEW bottles get cycled
// only these subtypes are cycled through tanks - modded bottle-TYPE items
// (powerbanks, shields, paint guns...) sort normally and never touch tanks
string[] tankableBottles = { "HydrogenBottle", "OxygenBottle" };

float UI_SCALE = 1.0f;              // sprite screen size multiplier (bigger text = fewer rows per screen)

bool enableAutocrafting = true;
string CRAFT_KEY = "Autocrafting";  // LCD name keyword for the autocrafting screen(s)
double craftMargin = 0.05;          // 5% - craft when below wanted*(1-margin)

bool feedRefineries = true;         // keep refineries loaded with ore
bool balanceRefineries = true;      // equalize each ore across all refineries
bool sortRefineryQueue = true;      // put priority ore at the front

bool fillTurrets = true;            // keep turrets + fixed guns loaded (subgrids included)
double magsPerTurret = 20;          // magazines of each accepted ammo type per weapon
// refining order, top = first:
List<string> orePriority = new List<string>{ "Stone","Iron","Nickel","Cobalt","Silicon","Uranium","Silver","Gold","Platinum","Magnesium","Scrap" };

bool iceBalancing = true;           // spread ice across O2/H2 generators
double iceBottleReserve = 240;      // liters kept free in each generator for bottles

bool uraniumBalancing = true;       // keep a fixed uranium amount in each reactor
double uraniumLarge = 100;          // per large-grid reactor
double uraniumSmall = 25;           // per small-grid reactor

bool assemblerCleanup = true;       // empty idle assemblers back into cargo
bool feedAssemblers = true;         // pre-stock assembler inputs with the ingots their queue needs
double feedQueueDepth = 100;        // stock ingots for this many queued crafts ahead

bool excludeWelders = true;         // don't drain welders (Build & Repair / Nanobot systems stock themselves)
bool excludeGrinders = false;       // don't drain grinders
bool excludeDrills = false;         // don't drain drills

string[] MAIN_TAGS = { "DUTC-main", "IIM-main" };
string[] INV_TAGS  = { "DUTC-inv", "IIM-inventory" };
string[] WARN_TAGS = { "DUTC-warn", "IIM-warnings" };
string[] ACT_TAGS  = { "DUTC-act", "IIM-actions" };

int maxMovesPerTick = 25;           // sorting transfers per run (performance)
int scanChunk = 250;                // blocks processed per tick while scanning (big-base safety)

// ==== SCRIPT BODY (do not edit below) ====
const int ORE=0, INGOT=1, COMP=2, TOOL=3, AMMO=4, BOTTLE=5, FOOD=6, NCAT=7;
string[] catKeys;
List<IMyTerminalBlock>[] cats = new List<IMyTerminalBlock>[NCAT];
List<IMyTerminalBlock> allInv = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> specials = new List<IMyTerminalBlock>();
List<IMyCargoContainer> untagged = new List<IMyCargoContainer>();
List<IMyRefinery> refineries = new List<IMyRefinery>();
List<IMyAssembler> assemblers = new List<IMyAssembler>();
List<IMyAssembler> distinctAsm = new List<IMyAssembler>();
List<IMyGasGenerator> gens = new List<IMyGasGenerator>();
List<IMyReactor> reactors = new List<IMyReactor>();
List<IMyGasTank> tanks = new List<IMyGasTank>();
List<IMyUserControllableGun> guns = new List<IMyUserControllableGun>();
List<IMyTextPanel> craftLCDs = new List<IMyTextPanel>();
List<IMyTerminalBlock> mainHolders = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> invHolders = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> warnHolders = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> actHolders = new List<IMyTerminalBlock>();
Dictionary<MyItemType,double> stock = new Dictionary<MyItemType,double>();
Dictionary<string,MyItemType> byName = new Dictionary<string,MyItemType>();
Dictionary<MyDefinitionId,double> queued = new Dictionary<MyDefinitionId,double>();
Dictionary<string,MyDefinitionId> bpCache = new Dictionary<string,MyDefinitionId>();
HashSet<string> noBp = new HashSet<string>();
List<string> warnings = new List<string>();
HashSet<string> warnSet = new HashSet<string>();
List<string> actions = new List<string>();
Dictionary<int,int> scroll = new Dictionary<int,int>();
Dictionary<long,double> bottleMem = new Dictionary<long,double>();
List<IMyTerminalBlock> scanBlocks = new List<IMyTerminalBlock>();
List<IMyCubeGrid> noSortGrids = new List<IMyCubeGrid>();
int scanPos = 0;
Dictionary<long,bool> convCache = new Dictionary<long,bool>();
Dictionary<long,bool> reachCache = new Dictionary<long,bool>();
int reachBudget = 0;
int step = 0;
int sortIdx = 0;
int gunIdx = 0;
int moved = 0;
int tick = 0;
int cycles = 0;
bool paused = false;
bool standby = false;
string masterName = "";
string DUTC_MARKER = "[DUTC-INV-ACTIVE]";
string lastError = "";
string[] stepNames = { "Scan","Count","Assign","Sort","Balance","Special","Craft","Refineries","Ice","Uranium","Turrets","Cleanup","Screens" };
public Program()
{
    catKeys = new[]{ ORE_KEY, INGOT_KEY, COMP_KEY, TOOL_KEY, AMMO_KEY, BOTTLE_KEY, FOOD_KEY };
    for (int i = 0; i < NCAT; i++) cats[i] = new List<IMyTerminalBlock>();
    InitRecipes();
    if (!string.IsNullOrEmpty(Storage))
    {
        var parts = Storage.Split('|');
        if (parts.Length >= 2)
        {
            foreach (var e in parts[0].Split(';'))
            {
                int gt = e.IndexOf('>');
                if (gt <= 0) continue;
                MyDefinitionId id;
                if (MyDefinitionId.TryParse(e.Substring(gt + 1), out id)) bpCache[e.Substring(0, gt)] = id;
            }
            foreach (var nm in parts[1].Split(';')) if (nm.Length > 0) noBp.Add(nm);
        }
    }
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}
public void Save()
{
    var sb = new StringBuilder();
    foreach (var kv in bpCache) sb.Append(kv.Key).Append('>').Append(kv.Value.ToString()).Append(';');
    sb.Append('|');
    foreach (var nm in noBp) sb.Append(nm).Append(';');
    Storage = sb.ToString();
}
public void Main(string arg)
{
    arg = arg.Trim().ToLower();
    if (arg == "pause") paused = true;
    if (arg == "resume" || arg == "run") paused = false;
    if (arg == "reset") { bpCache.Clear(); noBp.Clear(); probePos.Clear(); convCache.Clear(); reachCache.Clear(); barCache.Clear(); Storage = ""; lastError = ""; }
    tick++;
    Echo("DUTC INVENTORY " + "|/-\\"[tick % 4]);
    Echo("Step: " + stepNames[step]);
    Echo("Inventories: " + allInv.Count + "  Specials: " + specials.Count);
    Echo("Warnings: " + warnings.Count);
    if (standby) Echo("STANDBY - sorting handled by:\n" + masterName);
    if (lastError != "") Echo("Last error: " + lastError);
    if (paused) { Echo("PAUSED - run with 'resume'"); return; }
    try
    {
        bool done = DoStep(step);
        if (done) step = (step + 1) % 13;
    }
    catch (Exception e)
    {
        lastError = stepNames[step] + ": " + e.Message;
        Warn("Script error in step " + stepNames[step]);
        step = (step + 1) % 13;
    }
}
bool DoStep(int s)
{
    if (standby && (s == 2 || s == 3 || s == 5 || s == 6)) return true;
    switch (s)
    {
        case 0: return Scan();
        case 1: return Count();
        case 2: return Assign();
        case 3: return Sort();
        case 4: return Balance();
        case 5: return SpecialFill();
        case 6: return Craft();
        case 7: return Refine();
        case 8: return Ice();
        case 9: return Uranium();
        case 10: return Turrets();
        case 11: return Cleanup();
        default: return Screens();
    }
}
bool Scan()
{
    if (scanPos == 0)
    {
        cycles++;
        warnings.Clear(); warnSet.Clear();
        if (cycles % 150 == 0) { convCache.Clear(); reachCache.Clear(); }
        if (!Me.CustomData.Contains(DUTC_MARKER)) Me.CustomData = DUTC_MARKER + "\n" + Me.CustomData;
        standby = false; masterName = "";
        var pbs = new List<IMyProgrammableBlock>();
        GridTerminalSystem.GetBlocksOfType(pbs, p => p != Me && p.IsWorking && p.CustomData.Contains(DUTC_MARKER));
        foreach (var p in pbs)
        {
            bool meStatic = Me.CubeGrid.IsStatic;
            bool otherStatic = p.CubeGrid.IsStatic;
            bool otherWins;
            if (otherStatic != meStatic) otherWins = otherStatic;
            else otherWins = p.EntityId < Me.EntityId;
            if (otherWins)
            {
                standby = true;
                masterName = p.CustomName + " on '" + p.CubeGrid.CustomName + "'";
                break;
            }
        }
        for (int i = 0; i < NCAT; i++) cats[i].Clear();
        allInv.Clear(); specials.Clear(); untagged.Clear();
        refineries.Clear(); assemblers.Clear(); gens.Clear(); reactors.Clear(); tanks.Clear(); guns.Clear();
        noSortGrids.Clear();
        var conns = new List<IMyShipConnector>();
        GridTerminalSystem.GetBlocksOfType(conns);
        foreach (var c in conns)
        {
            if (c.Status != MyShipConnectorStatus.Connected) continue;
            if (!c.CustomName.Contains(NO_SORT_CONNECTOR) && !c.CustomName.Contains(NO_IIM_CONNECTOR)) continue;
            var o = c.OtherConnector; if (o == null) continue;
            if (c.CubeGrid.IsSameConstructAs(Me.CubeGrid)) noSortGrids.Add(o.CubeGrid);
            else noSortGrids.Add(c.CubeGrid);
        }
        GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(scanBlocks, b => b.HasInventory);
    }
    int processed = 0;
    for (; scanPos < scanBlocks.Count; scanPos++)
    {
        if (processed >= scanChunk || Runtime.CurrentInstructionCount > 30000) return false;
        var b = scanBlocks[scanPos];
        processed++;
        string n = b.CustomName;
        bool same = b.IsSameConstructAs(Me);
        if (!same)
        {
            if (!includeDockedGrids) continue;
            bool skip = false;
            foreach (var g in noSortGrids) if (b.CubeGrid.IsSameConstructAs(g)) { skip = true; break; }
            if (skip) continue;
        }
        bool locked = false;
        foreach (var k in lockedKeywords) if (n.Contains(k)) { locked = true; break; }
        if (locked) continue;
        string def = b.BlockDefinition.TypeIdString;
        if (def.Contains("Parachute") || def.Contains("VendingMachine") || def.Contains("StoreBlock")) continue;
        bool manual = n.Contains(MANUAL_KEY);
        if (n.Contains(SPECIAL_KEY)) { specials.Add(b); EnsureSpecialTemplate(b); continue; }
        allInv.Add(b);
        bool tagged = false;
        bool storageBlock = !(b is IMyProductionBlock) && !(b is IMyReactor) && !(b is IMyGasGenerator) && !(b is IMyGasTank);
        bool storBlack = MatchList(b, storageBlacklist);
        if (same && storageBlock && !storBlack)
            for (int c = 0; c < NCAT; c++)
                if (n.Contains(catKeys[c])) { cats[c].Add(b); tagged = true; }
        if (b is IMyRefinery) { if (!manual && same && b.IsWorking) refineries.Add((IMyRefinery)b); }
        else if (b is IMyAssembler) { if (!manual && same && b.IsWorking) assemblers.Add((IMyAssembler)b); }
        else if (b is IMyGasGenerator) { if (!manual && same && b.IsFunctional && ((IMyFunctionalBlock)b).Enabled) gens.Add((IMyGasGenerator)b); }
        else if (b is IMyReactor) { if (!manual && same && b.IsFunctional && ((IMyFunctionalBlock)b).Enabled) reactors.Add((IMyReactor)b); }
        else if (b is IMyGasTank) { if (!manual && same && b.IsWorking) tanks.Add((IMyGasTank)b); }
        else if (b is IMyUserControllableGun) { if (!manual && b.IsFunctional) guns.Add((IMyUserControllableGun)b); }
        else if (b is IMyCargoContainer && same && !tagged && !storBlack && !n.Contains(HIDDEN_KEY)
                 && (storageWhitelist.Length == 0 || MatchList(b, storageWhitelist))) untagged.Add((IMyCargoContainer)b);
    }
    scanPos = 0;
    distinctAsm.Clear();
    foreach (var a in assemblers)
    {
        bool dup = false;
        foreach (var d in distinctAsm) if (d.BlockDefinition.ToString() == a.BlockDefinition.ToString()) { dup = true; break; }
        if (!dup) distinctAsm.Add(a);
    }
    for (int c = 0; c < NCAT; c++) cats[c].Sort((a, z) => a.CustomName.CompareTo(z.CustomName));
    untagged.Sort((a, z) => ((double)z.GetInventory(0).MaxVolume).CompareTo((double)a.GetInventory(0).MaxVolume));
    craftLCDs.Clear();
    GridTerminalSystem.GetBlocksOfType(craftLCDs, p => p.IsSameConstructAs(Me) && p.CustomName.Contains(CRAFT_KEY));
    craftLCDs.Sort((a, z) => a.CustomName.CompareTo(z.CustomName));
    mainHolders.Clear(); invHolders.Clear(); warnHolders.Clear(); actHolders.Clear();
    var scr = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(scr, x => x.IsSameConstructAs(Me));
    foreach (var x in scr)
    {
        if (HasTag(x, MAIN_TAGS)) mainHolders.Add(x);
        if (HasTag(x, INV_TAGS)) invHolders.Add(x);
        if (HasTag(x, WARN_TAGS)) warnHolders.Add(x);
        if (HasTag(x, ACT_TAGS)) actHolders.Add(x);
    }
    return true;
}
bool HasTag(IMyTerminalBlock b, string[] tags)
{
    foreach (var t in tags)
        if (b.CustomName.Contains(t) || b.CustomData.Contains(t)) return true;
    return false;
}
bool ContainsAny(string s, string[] tags)
{
    foreach (var t in tags) if (s.Contains(t)) return true;
    return false;
}
bool Count()
{
    stock.Clear(); byName.Clear();
    var items = new List<MyInventoryItem>();
    for (int pass = 0; pass < 2; pass++)
    {
        var list = pass == 0 ? allInv : specials;
        foreach (var b in list)
        {
            if (b.CustomName.Contains(HIDDEN_KEY)) continue;
            for (int q = 0; q < b.InventoryCount; q++)
            {
                items.Clear(); b.GetInventory(q).GetItems(items);
                foreach (var it in items)
                {
                    double a = (double)it.Amount;
                    if (stock.ContainsKey(it.Type)) stock[it.Type] += a; else stock[it.Type] = a;
                }
            }
        }
    }
    foreach (var t in stock.Keys)
    {
        byName[t.SubtypeId] = t;
        byName[t.TypeId.Replace("MyObjectBuilder_", "") + "/" + t.SubtypeId] = t;
    }
    return true;
}
bool Assign()
{
    if (autoAssignContainers)
    {
        reachBudget = 3;
        for (int c = 0; c < NCAT; c++)
        {
            bool need = cats[c].Count == 0;
            if (!need)
            {
                need = true;
                foreach (var w in cats[c]) if (HasSpace(w.GetInventory(0))) { need = false; break; }
            }
            if (!need) continue;
            IMyCargoContainer box = null;
            foreach (var u in untagged) { if (Reachable(u)) { box = u; break; } }
            if (box == null) { Warn("No conveyor-connected cargo container free to assign for '" + catKeys[c] + "'!"); continue; }
            untagged.Remove(box);
            box.CustomName = box.CustomName + " " + catKeys[c];
            cats[c].Add(box);
            Act("Assigned '" + box.CustomName + "' for " + catKeys[c]);
        }
    }
    if (showFillLevel)
    {
        for (int c = 0; c < NCAT; c++)
            foreach (var w in cats[c])
            {
                var inv = w.GetInventory(0);
                string nm = System.Text.RegularExpressions.Regex.Replace(w.CustomName, @"\s*\(\d+\.?\d*%\)", "");
                string want = nm + " (" + Pct((double)inv.CurrentVolume, (double)inv.MaxVolume) + ")";
                if (w.CustomName != want) w.CustomName = want;
            }
    }
    return true;
}
bool Sort()
{
    if (!sortItems) return true;
    moved = 0;
    var items = new List<MyInventoryItem>();
    for (; sortIdx < allInv.Count; sortIdx++)
    {
        if (moved >= maxMovesPerTick || Runtime.CurrentInstructionCount > 35000) return false;
        var b = allInv[sortIdx];
        if (b is IMyReactor) continue;
        if (b is IMyUserControllableGun) continue;
        if (b is IMyShipWelder && (excludeWelders || IsBaR(b))) continue;
        if (excludeGrinders && b is IMyShipGrinder) continue;
        if (excludeDrills && b is IMyShipDrill) continue;
        if (b.CustomName.Contains(HIDDEN_KEY)) continue;
        bool foreignGrid = !b.IsSameConstructAs(Me);
        var inv = b.GetInventory(SrcInvIndex(b));
        if (inv.ItemCount == 0) continue;
        items.Clear(); inv.GetItems(items);
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            int c = Cat(it.Type);
            if (c < 0) continue;
            if (b is IMyGasGenerator && IsIce(it.Type)) continue;
            if (IsCat(b, c)) continue;
            if (foreignGrid && b.CustomName.Contains(catKeys[c])) continue; // respect docked ships' own type containers
            IMyInventory dst = null; string dstName = ""; long dstId = 0;
            if (c == BOTTLE && fillBottles && !(b is IMyGasTank) && Tankable(it.Type))
            {
                var tk = FindTank(it.Type);
                if (tk != null) { dst = tk.GetInventory(0); dstName = tk.CustomName; dstId = tk.EntityId; }
            }
            if (dst == null)
            {
                var w = DestFor(c, b);
                if (w == null) { Warn("All '" + catKeys[c] + "' containers are full!"); continue; }
                dst = w.GetInventory(0); dstName = w.CustomName; dstId = w.EntityId;
            }
            if (!CanMove(inv, b.EntityId, dst, dstId, it.Type))
            {
                Warn("No conveyor path: '" + b.CustomName + "' -> '" + dstName + "'");
                continue;
            }
            if (inv.TransferItemTo(dst, i, null, true))
            {
                moved++;
                Act(it.Type.SubtypeId + ": " + StripPct(b.CustomName) + " -> " + StripPct(dstName));
            }
            if (moved >= maxMovesPerTick) break;
        }
    }
    sortIdx = 0;
    return true;
}
bool Balance()
{
    if (!balanceTypeContainers) return true;
    var items = new List<MyInventoryItem>();
    for (int c = 0; c < NCAT; c++)
    {
        if (cats[c].Count < 2) continue;
        int n = cats[c].Count;
        var totals = new Dictionary<MyItemType, double>();
        foreach (var w in cats[c])
        {
            items.Clear(); w.GetInventory(0).GetItems(items);
            foreach (var it in items)
            {
                if (Cat(it.Type) != c) continue;
                double a; totals.TryGetValue(it.Type, out a);
                totals[it.Type] = a + (double)it.Amount;
            }
        }
        foreach (var kv in totals)
        {
            var t = kv.Key;
            double avg = kv.Value / n;
            double tol = Math.Max(1, avg * 0.05);
            foreach (var rich in cats[c])
            {
                var rInv = rich.GetInventory(0);
                double cur = (double)rInv.GetItemAmount(t);
                if (cur <= avg + tol) continue;
                foreach (var poor in cats[c])
                {
                    if (poor == rich) continue;
                    var pInv = poor.GetInventory(0);
                    double pc = (double)pInv.GetItemAmount(t);
                    if (pc >= avg - tol) continue;
                    if (!HasSpace(pInv)) continue;
                    if (Runtime.CurrentInstructionCount > 28000) return true;
                    double move = Math.Floor(Math.Min(cur - avg, avg - pc) + 0.001);
                    if (move < 1) continue;
                    cur -= PushFrom(rInv, t, move, pInv);
                    if (cur <= avg + tol) break;
                }
            }
            if (Runtime.CurrentInstructionCount > 30000) return true;
        }
    }
    return true;
}
bool SpecialFill()
{
    foreach (var b in specials)
    {
        var inv = b.GetInventory(0);
        foreach (var raw in b.CustomData.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#") || l.StartsWith("@") || l.StartsWith("-")) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string name = l.Substring(0, eq).Trim();
            MyItemType t;
            if (!byName.TryGetValue(name, out t)) continue;
            string val = l.Substring(eq + 1).Trim().ToLower();
            double want = 0; string mode = "n";
            if (val.Contains("all")) { want = 1000000000; mode = "a"; }
            else
            {
                if (!double.TryParse(System.Text.RegularExpressions.Regex.Match(val, @"\d+\.?\d*").Value, out want)) continue;
                if (val.Contains("m")) mode = "m";
                else if (val.Contains("l")) mode = "l";
            }
            double cur = (double)inv.GetItemAmount(t);
            if (mode != "l" && cur < want - 0.01)
            {
                if (!HasSpace(inv)) continue;
                double got = PullTo(inv, t, want - cur, b);
                if (mode != "a" && got < want - cur - 0.01)
                    Warn("'" + StripPct(b.CustomName) + "' missing " + Math.Round(want - cur - got) + " " + name);
            }
            else if ((mode == "n" || mode == "l") && cur > want + 0.01)
            {
                int c = Cat(t);
                if (c < 0) continue;
                var w = DestFor(c, b);
                if (w != null) PushFrom(inv, t, cur - want, w.GetInventory(0));
            }
        }
    }
    return true;
}
string CRAFT_MARKER = "# DUTC Autocrafting";
Dictionary<MyDefinitionId,double> ourQueued = new Dictionary<MyDefinitionId,double>();
Dictionary<string,string> knownBp = new Dictionary<string,string>
{
    { "AQD_Comp_Concrete", "AQD_BP_StoneIngot_To_Concrete" }
};
Dictionary<string,int> probePos = new Dictionary<string,int>();
string[] vanillaCraft = { "SteelPlate","InteriorPlate","Construction","MetalGrid","SmallTube","LargeTube","Motor","Display","BulletproofGlass","Computer","Reactor","Thrust","GravityGenerator","Medical","RadioCommunication","Detector","Explosives","Girder","SolarCell","PowerCell","Superconductor","Canvas" };
int probeBudget = 0;
class CRow
{
    public string name = "";
    public double cur;
    public double want;
    public int state;   // 0 = normal, -1 = NoBP, -2 = ignored, -3 = still checking
    public double inQ;
}
List<CRow> craftRows = new List<CRow>();
bool Craft()
{
    if (!enableAutocrafting || craftLCDs.Count == 0) return true;
    if (assemblers.Count == 0) { Warn("Autocrafting: no usable assemblers!"); return true; }
    probeBudget = 1;
    queued.Clear();
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        q.Clear();
        try { a.GetQueue(q); } catch { continue; }
        foreach (var pi in q)
        {
            double amt; queued.TryGetValue(pi.BlueprintId, out amt);
            queued[pi.BlueprintId] = amt + (double)pi.Amount;
        }
    }
    var okeys = new List<MyDefinitionId>(ourQueued.Keys);
    foreach (var k in okeys)
    {
        double actual; queued.TryGetValue(k, out actual);
        if (ourQueued[k] > actual) ourQueued[k] = actual;
    }
    var master = craftLCDs[0];
    if (!master.CustomData.Contains(CRAFT_MARKER)) SetupCraftCD();
    var entries = new List<string[]>();
    var seen = new HashSet<string>();
    foreach (var p in craftLCDs)
    {
        foreach (var raw in p.CustomData.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string name = l.Substring(0, eq).Trim();
            string val = l.Substring(eq + 1).Trim();
            string bpOv = "";
            var mo = System.Text.RegularExpressions.Regex.Match(val, @"(?i)bp:([\w/]+)");
            if (mo.Success) { bpOv = mo.Groups[1].Value; val = val.Replace(mo.Value, ""); }
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
        if (seen.Contains(t.SubtypeId)) continue;
        MyDefinitionId bp0;
        if (BpState(t.SubtypeId, out bp0, false) != 1) continue;
        seen.Add(t.SubtypeId);
        entries.Add(new[] { t.SubtypeId, "0", "", "" });
        newLines.Append(t.SubtypeId + "=0\n");
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
        double cur = byName.TryGetValue(name, out t) ? CountOf(t) : 0;
        if (mods.Contains("I")) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -2 }); continue; }
        if (e[3] != "" && !bpCache.ContainsKey(name))
        {
            MyDefinitionId ov;
            if (TestBp(e[3], out ov)) { bpCache[name] = ov; noBp.Remove(name); }
        }
        MyDefinitionId bp;
        int st = BpState(name, out bp, want > 0);
        if (st == 0 && want <= 0) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = 0 }); continue; }
        if (st == 0) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -3 }); continue; }
        if (st == -1) { craftRows.Add(new CRow { name = name, cur = cur, want = want, state = -1 }); continue; }
        double inQ; queued.TryGetValue(bp, out inQ);
        if (want > 0 && cur < want * (1 - craftMargin))
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
        craftRows.Add(new CRow { name = name, cur = cur, want = want, state = 0, inQ = inQ });
    }
    DrawCraftScreens();
    return true;
}
void SetupCraftCD()
{
    var master = craftLCDs[0];
    var sbcd = new StringBuilder();
    sbcd.Append(CRAFT_MARKER + "\n");
    sbcd.Append("# Edit the number = wanted stock. The screen only displays status.\n");
    sbcd.Append("# Modifiers after the number:  P = craft first (priority),  I = ignore\n");
    sbcd.Append("# Modded item stuck on NoBP? Force its blueprint: Name=100 BP:BlueprintSubtype\n");
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
            string mods = System.Text.RegularExpressions.Regex.Replace(wantTok, @"[\d\.]", "").ToUpper().Replace("A", "").Replace("D", "").Replace("H", "");
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
        start += DrawCraftPanel(craftLCDs[p], p, start, last);
    }
}
int DrawCraftPanel(IMyTextSurface s, int index, int start, bool last)
{
    var f = Begin(s);
    Vector2 size = s.SurfaceSize, off = (s.TextureSize - size) * 0.5f;
    float W = size.X, H = size.Y, sc = Math.Min(W, H) / 512f * UI_SCALE;
    string hd = "DUTC AUTOCRAFTING" + (craftLCDs.Count > 1 ? " " + (index + 1) + "/" + craftLCDs.Count : "");
    f.Add(Txt(hd, off + new Vector2(W * 0.5f, 10f * sc), 1.05f * sc, UI_DIM));
    int okC = 0, craftC = 0, lowC = 0, nobpC = 0, chkC = 0;
    foreach (var r in craftRows)
    {
        if (r.state == -1) nobpC++;
        else if (r.state == -3) chkC++;
        else if (r.state == 0 && r.want > 0)
        {
            if (r.cur >= r.want * (1 - craftMargin)) okC++;
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
        else if (r.state == -1) { col = UI_BAD; valTxt = Num(r.cur) + "  NoBP"; }
        else
        {
            frac = r.want > 0 ? Math.Min(1, r.cur / r.want) : -1;
            col = r.want <= 0 ? UI_DIM : r.cur >= r.want * (1 - craftMargin) ? UI_GOOD : r.inQ > 0 ? UI_WARNC : UI_BAD;
            valTxt = Num(r.cur) + " / " + Num(r.want) + (r.inQ > 0 ? " +" + Math.Round(r.inQ) : "");
        }
        f.Add(Txt(TruncS(r.name, maxChars), off + new Vector2(W * 0.05f, y), 0.6f * sc, UI_TEXT, TextAlignment.LEFT));
        f.Add(Txt(valTxt, off + new Vector2(W * 0.70f, y), 0.55f * sc, col, TextAlignment.RIGHT));
        if (frac >= 0) DrawBar(f, off + new Vector2(W * 0.845f, y + 8f * sc), W * 0.25f, 10f * sc, frac, col);
        y += rowPx; drawn++;
    }
    if (total == 0) f.Add(Txt("No craftable items known yet", off + new Vector2(W * 0.5f, H * 0.45f), 0.7f * sc, UI_DIM));
    f.Dispose();
    return last ? Math.Max(0, total - start) : maxRows;
}
void QueueBp(MyDefinitionId bp, double need, bool prio)
{
    var usable = new List<IMyAssembler>();
    foreach (var a in assemblers)
    {
        try
        {
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
double RemoveBp(MyDefinitionId bp)
{
    double allow; ourQueued.TryGetValue(bp, out allow);
    if (allow <= 0) return 0;
    double removed = 0;
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        if (allow <= 0) break;
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
int BpState(string name, out MyDefinitionId bp, bool deep)
{
    bp = new MyDefinitionId();
    MyDefinitionId cached;
    if (bpCache.TryGetValue(name, out cached)) { bp = cached; return 1; }
    if (noBp.Contains(name)) return -1;
    if (assemblers.Count == 0) return 0;
    string mapped;
    if (knownBp.TryGetValue(name, out mapped) && TestBp(mapped, out bp)) { bpCache[name] = bp; return 1; }
    string core = name;
    string typePrefix = "";
    int slash = name.IndexOf('/');
    if (slash > 0) { typePrefix = name.Substring(0, slash); core = name.Substring(slash + 1); }
    string baseN = core.Replace("Item", "");
    int lastUnder = baseN.LastIndexOf('_');
    string trimmed = lastUnder > 0 ? baseN.Substring(0, lastUnder) : baseN;
    var candidates = new HashSet<string>();
    if (typePrefix == "SeedItem") { candidates.Add("Seeds_" + core); candidates.Add("Seed_" + core); candidates.Add("Spores_" + core); }
    if (typePrefix != "") candidates.Add(typePrefix + "_" + core);
    candidates.Add(core); candidates.Add(baseN); candidates.Add(trimmed);
    candidates.Add(core + "Component"); candidates.Add(baseN + "Component");
    candidates.Add(core + "Magazine"); candidates.Add(baseN + "Magazine");
    candidates.Add(core + "_ApexSurvivalAdditions");
    foreach (var s in candidates)
        if (TestBp(s, out bp)) { bpCache[name] = bp; return 1; }
    if (!deep) return 0;
    if (probeBudget <= 0) return 0;
    probeBudget--;
    var candList = new List<string>(candidates);
    int pos; probePos.TryGetValue(name, out pos);
    int idx = 0;
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
bool TestBp(string sub, out MyDefinitionId id)
{
    id = new MyDefinitionId();
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
bool Refine()
{
    if (refineries.Count == 0) return true;
    foreach (var r in refineries)
    {
        var inv = r.GetInventory(0);
        if (feedRefineries && (double)inv.CurrentVolume < (double)inv.MaxVolume * 0.4)
        {
            double freeL = ((double)inv.MaxVolume - (double)inv.CurrentVolume) * 1000.0;
            foreach (var ore in orePriority)
            {
                var t = MyItemType.MakeOre(ore);
                if (CountOf(t) <= 0) continue;
                if (!inv.CanItemsBeAdded(1, t)) continue;
                if (PullTo(inv, t, freeL / 0.37, r) > 0) break;
            }
        }
        if (sortRefineryQueue)
        {
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            if (items.Count > 1)
            {
                int best = int.MaxValue, bi = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    int pr = orePriority.IndexOf(items[i].Type.SubtypeId);
                    if (pr < 0) pr = 999;
                    if (pr < best) { best = pr; bi = i; }
                }
                if (bi > 0) inv.TransferItemTo(inv, bi, 0, true);
            }
        }
    }
    if (balanceRefineries && refineries.Count > 1)
    {
        foreach (var ore in orePriority)
        {
            var t = MyItemType.MakeOre(ore);
            double tot = 0;
            foreach (var rf in refineries) tot += (double)rf.GetInventory(0).GetItemAmount(t);
            if (tot < 200) continue;
            double avg = tot / refineries.Count;
            foreach (var rich in refineries)
            {
                double cur = (double)rich.GetInventory(0).GetItemAmount(t);
                if (cur <= avg + 100) continue;
                foreach (var poor in refineries)
                {
                    if (poor == rich) continue;
                    double pc = (double)poor.GetInventory(0).GetItemAmount(t);
                    if (pc >= avg - 100) continue;
                    double move = Math.Min(cur - avg, avg - pc);
                    cur -= PushFrom(rich.GetInventory(0), t, move, poor.GetInventory(0));
                    if (cur <= avg + 100) break;
                }
            }
            if (Runtime.CurrentInstructionCount > 30000) break;
        }
    }
    return true;
}
bool Turrets()
{
    if (!fillTurrets || guns.Count == 0) { gunIdx = 0; return true; }
    var ammoTypes = new List<MyItemType>();
    foreach (var kv in stock)
        if (kv.Key.TypeId.EndsWith("_AmmoMagazine")) ammoTypes.Add(kv.Key);
    if (ammoTypes.Count == 0) { gunIdx = 0; return true; }
    for (; gunIdx < guns.Count; gunIdx++)
    {
        if (Runtime.CurrentInstructionCount > 25000) return false;
        var g = guns[gunIdx];
        if (g.Closed) continue;
        var inv = g.GetInventory(0);
        foreach (var t in ammoTypes)
        {
            if (Runtime.CurrentInstructionCount > 30000) return false;
            if (!inv.CanItemsBeAdded(1, t)) continue;
            double cur = (double)inv.GetItemAmount(t);
            if (cur >= magsPerTurret - 0.01) continue;
            PullTo(inv, t, magsPerTurret - cur, g);
        }
    }
    gunIdx = 0;
    return true;
}
bool Ice()
{
    if (!iceBalancing || gens.Count == 0) return true;
    var iceTypes = new List<MyItemType>();
    foreach (var kv in stock) if (IsIce(kv.Key)) iceTypes.Add(kv.Key);
    if (iceTypes.Count == 0) return true;
    foreach (var g in gens) g.UseConveyorSystem = false;
    foreach (var ice in iceTypes)
    {
        double share = CountOf(ice) / gens.Count;
        foreach (var g in gens)
        {
            var inv = g.GetInventory(0);
            double cur = (double)inv.GetItemAmount(ice);
            double freeL = ((double)inv.MaxVolume - (double)inv.CurrentVolume) * 1000.0;
            double maxHold = cur + Math.Max(0, freeL - iceBottleReserve) / 0.37;
            double target = Math.Min(share, maxHold);
            if (cur > target + 100)
            {
                var w = DestFor(ORE, g);
                if (w != null) PushFrom(inv, ice, cur - target, w.GetInventory(0));
            }
            else if (cur < target - 100)
            {
                PullTo(inv, ice, target - cur, g);
            }
            if (Runtime.CurrentInstructionCount > 30000) return true;
        }
    }
    return true;
}
bool Uranium()
{
    if (!uraniumBalancing || reactors.Count == 0) return true;
    var ur = MyItemType.MakeIngot("Uranium");
    foreach (var r in reactors)
    {
        r.UseConveyorSystem = false;
        var inv = r.GetInventory(0);
        double target = r.CubeGrid.GridSize > 0.6f ? uraniumLarge : uraniumSmall;
        double cur = (double)inv.GetItemAmount(ur);
        if (cur > target + 1)
        {
            var w = DestFor(INGOT, r);
            if (w != null) PushFrom(inv, ur, cur - target, w.GetInventory(0));
        }
        else if (cur < target - 1)
        {
            PullTo(inv, ur, target - cur, r);
        }
    }
    return true;
}
string[] ingotNames = { "Iron","Nickel","Cobalt","Silicon","Silver","Gold","Platinum","Magnesium","Stone" };
Dictionary<string,double[]> recipes = new Dictionary<string,double[]>();
void InitRecipes()
{
    recipes["SteelPlate"] = new double[]{21,0,0,0,0,0,0,0,0};
    recipes["InteriorPlate"] = new double[]{3.5,0,0,0,0,0,0,0,0};
    recipes["ConstructionComponent"] = new double[]{8,0,0,0,0,0,0,0,0};
    recipes["MetalGrid"] = new double[]{12,5,3,0,0,0,0,0,0};
    recipes["SmallTube"] = new double[]{5,0,0,0,0,0,0,0,0};
    recipes["LargeTube"] = new double[]{30,0,0,0,0,0,0,0,0};
    recipes["MotorComponent"] = new double[]{20,5,0,0,0,0,0,0,0};
    recipes["ComputerComponent"] = new double[]{0.5,0,0,0.2,0,0,0,0,0};
    recipes["Display"] = new double[]{1,0,0,5,0,0,0,0,0};
    recipes["BulletproofGlass"] = new double[]{0,0,0,15,0,0,0,0,0};
    recipes["GirderComponent"] = new double[]{6,0,0,0,0,0,0,0,0};
    recipes["PowerCell"] = new double[]{10,2,0,1,0,0,0,0,0};
    recipes["SolarCell"] = new double[]{0,3,0,6,0,0,0,0,0};
    recipes["RadioCommunicationComponent"] = new double[]{8,0,0,1,0,0,0,0,0};
    recipes["DetectorComponent"] = new double[]{5,15,0,0,0,0,0,0,0};
    recipes["MedicalComponent"] = new double[]{60,70,0,0,20,0,0,0,0};
    recipes["ReactorComponent"] = new double[]{15,0,0,0,5,0,0,0,20};
    recipes["ThrustComponent"] = new double[]{30,0,10,0,0,1,0.4,0,0};
    recipes["GravityGeneratorComponent"] = new double[]{600,0,220,0,5,10,0,0,0};
    recipes["Superconductor"] = new double[]{10,0,0,0,0,2,0,0,0};
    recipes["ExplosivesComponent"] = new double[]{0,0,0,0.5,0,0,0,2,0};
    recipes["NATO_25x184mmMagazine"] = new double[]{40,2,0,0,0,0,0,3,0};
}
void FeedAssemblers()
{
    if (!feedAssemblers) return;
    var q = new List<MyProductionItem>();
    foreach (var a in assemblers)
    {
        if (a.Mode == MyAssemblerMode.Disassembly || a.IsQueueEmpty) continue;
        a.UseConveyorSystem = true;
        var inv = a.GetInventory(0);
        if (!HasSpace(inv)) continue;
        if (Runtime.CurrentInstructionCount > 28000) return;
        var need = new Dictionary<string,double>();
        q.Clear();
        try { a.GetQueue(q); } catch { continue; }
        double depth = feedQueueDepth;
        foreach (var pi in q)
        {
            if (depth <= 0) break;
            double amt = Math.Min((double)pi.Amount, depth);
            depth -= amt;
            double[] costs;
            if (!recipes.TryGetValue(pi.BlueprintId.SubtypeName, out costs)) continue;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i] <= 0) continue;
                double v; need.TryGetValue(ingotNames[i], out v);
                need[ingotNames[i]] = v + costs[i] * amt;
            }
        }
        foreach (var kv in need)
        {
            if (Runtime.CurrentInstructionCount > 28000) return;
            var t = MyItemType.MakeIngot(kv.Key);
            double have = (double)inv.GetItemAmount(t);
            if (have >= kv.Value - 0.5) continue;
            PullTo(inv, t, kv.Value - have, a);
        }
    }
}
bool Cleanup()
{
    FeedAssemblers();
    if (assemblerCleanup)
    {
        foreach (var a in assemblers)
        {
            if (!a.IsQueueEmpty) continue;
            var inv = a.GetInventory(0);
            if (inv.ItemCount == 0) continue;
            var w = DestFor(INGOT, a);
            if (w == null) continue;
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);
            for (int i = items.Count - 1; i >= 0; i--) inv.TransferItemTo(w.GetInventory(0), i, null, true);
            Act("Cleaned up " + StripPct(a.CustomName));
        }
    }
    if (fillBottles && refillStoredBottles && cycles % bottleRefillEvery == 0) BottleTopUp();
    return true;
}
void BottleTopUp()
{
    foreach (var w in cats[BOTTLE])
    {
        var inv = w.GetInventory(0);
        var items = new List<MyInventoryItem>();
        inv.GetItems(items);
        double count = 0;
        foreach (var it in items) if (Cat(it.Type) == BOTTLE && Tankable(it.Type)) count += (double)it.Amount;
        double last; bottleMem.TryGetValue(w.EntityId, out last);
        if (count <= last + 0.01) { bottleMem[w.EntityId] = count; continue; }
        bottleMem[w.EntityId] = count;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (Cat(items[i].Type) != BOTTLE) continue;
            if (!Tankable(items[i].Type)) continue;
            var tk = FindTank(items[i].Type);
            if (tk == null) continue;
            if (!CanMove(inv, w.EntityId, tk.GetInventory(0), tk.EntityId, items[i].Type)) continue;
            inv.TransferItemTo(tk.GetInventory(0), i, null, true);
        }
    }
}
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
int SrcInvIndex(IMyTerminalBlock b) { return b is IMyProductionBlock ? 1 : 0; }
int Cat(MyItemType t)
{
    foreach (var s in foodItems) if (t.SubtypeId == s) return FOOD;
    string ty = t.TypeId;
    if (ty.EndsWith("_Ore")) return ORE;
    if (ty.EndsWith("_Ingot")) return INGOT;
    if (ty.EndsWith("_Component")) return COMP;
    if (ty.EndsWith("_AmmoMagazine")) return AMMO;
    if (ty.EndsWith("_OxygenContainerObject") || ty.EndsWith("_GasContainerObject")) return BOTTLE;
    if (ty.EndsWith("_ConsumableItem") || ty.EndsWith("_Ingredient") || ty.EndsWith("_IngredientItem") || ty.EndsWith("_SeedItem") || ty.EndsWith("_Seed") || ty.EndsWith("_Food") || ty.EndsWith("_FoodItem")) return FOOD;
    if (ty.EndsWith("_PhysicalGunObject") || ty.EndsWith("_PhysicalObject") || ty.EndsWith("_Datapad")) return TOOL;
    return -1;
}
bool IsCat(IMyTerminalBlock b, int c) { return cats[c].Contains(b); }
bool IsIce(MyItemType t) { return t.TypeId.EndsWith("_Ore") && t.SubtypeId.Contains("Ice"); }
bool MatchList(IMyTerminalBlock b, string[] list)
{
    foreach (var s in list)
    {
        if (s == null || s == "") continue;
        if (b.CustomName.Contains(s) || b.BlockDefinition.SubtypeId.Contains(s)) return true;
    }
    return false;
}
bool HasSpace(IMyInventory inv) { return (double)inv.CurrentVolume < (double)inv.MaxVolume * 0.98; }
bool CanMove(IMyInventory src, long srcId, IMyInventory dst, long dstId, MyItemType t)
{
    long key = srcId * 397 ^ dstId;
    bool ok;
    if (convCache.TryGetValue(key, out ok)) return ok;
    try { ok = src.CanTransferItemTo(dst, t); } catch { ok = false; }
    convCache[key] = ok;
    return ok;
}
bool Reachable(IMyTerminalBlock b)
{
    bool v;
    if (reachCache.TryGetValue(b.EntityId, out v)) return v;
    if (reachBudget <= 0) return false;
    reachBudget--;
    IMyInventory refInv = null;
    for (int c = 0; c < NCAT; c++)
    {
        if (cats[c].Count > 0 && cats[c][0] != b) { refInv = cats[c][0].GetInventory(0); break; }
    }
    if (refInv == null) refInv = Me.GetInventory(0);
    try { v = b.GetInventory(0).IsConnectedTo(refInv); }
    catch { v = true; }
    reachCache[b.EntityId] = v;
    return v;
}
IMyTerminalBlock DestFor(int c, IMyTerminalBlock exclude)
{
    foreach (var w in cats[c])
    {
        if (w == exclude) continue;
        if (HasSpace(w.GetInventory(0))) return w;
    }
    return null;
}
bool Tankable(MyItemType t)
{
    foreach (var s in tankableBottles) if (t.SubtypeId == s) return true;
    return false;
}
IMyGasTank FindTank(MyItemType t)
{
    bool hyd = t.TypeId.Contains("GasContainerObject");
    foreach (var tk in tanks)
    {
        if (tk.BlockDefinition.SubtypeId.Contains("Hydrogen") != hyd) continue;
        if (tk.FilledRatio < 0.01) continue;
        var inv = tk.GetInventory(0);
        if ((double)inv.MaxVolume - (double)inv.CurrentVolume < 0.13) continue;
        tk.AutoRefillBottles = true;
        return tk;
    }
    return null;
}
double PullTo(IMyInventory dst, MyItemType t, double amount, IMyTerminalBlock exclude)
{
    double before = (double)dst.GetItemAmount(t);
    double got = 0;
    long dstId = dst.Owner != null ? dst.Owner.EntityId : 0;
    int c = Cat(t);
    var items = new List<MyInventoryItem>();
    for (int pass = 0; pass < 2; pass++)
    {
        var list = (pass == 0 && c >= 0) ? cats[c] : allInv;
        foreach (var b in list)
        {
            if (Runtime.CurrentInstructionCount > 42000) return got;
            if (b == exclude) continue;
            if (pass == 1 && c >= 0 && IsCat(b, c)) continue;
            if (b is IMyReactor) continue;
            if (b is IMyGasGenerator && IsIce(t)) continue;
            if (b is IMyUserControllableGun) continue;
            if (b is IMyShipWelder && (excludeWelders || IsBaR(b))) continue;
            if (excludeGrinders && b is IMyShipGrinder) continue;
            if (excludeDrills && b is IMyShipDrill) continue;
            if (b.CustomName.Contains(HIDDEN_KEY)) continue;
            if (c >= 0 && !b.IsSameConstructAs(Me) && b.CustomName.Contains(catKeys[c])) continue;
            var inv = b.GetInventory(SrcInvIndex(b));
            if ((double)inv.GetItemAmount(t) <= 0) continue;
            if (!CanMove(inv, b.EntityId, dst, dstId, t)) continue;
            items.Clear(); inv.GetItems(items);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!items[i].Type.Equals(t)) continue;
                double take = Math.Min(amount - got, (double)items[i].Amount);
                if (take <= 0) break;
                inv.TransferItemTo(dst, i, null, true, (VRage.MyFixedPoint)take);
                got = (double)dst.GetItemAmount(t) - before;
                if (got >= amount - 0.01) return got;
                if (((double)dst.MaxVolume - (double)dst.CurrentVolume) * 1000.0 < 5) return got;
            }
        }
        if (c < 0) break;
    }
    return got;
}
double PushFrom(IMyInventory src, MyItemType t, double amount, IMyInventory dst)
{
    double before = (double)dst.GetItemAmount(t);
    double done = 0;
    var items = new List<MyInventoryItem>();
    src.GetItems(items);
    for (int i = items.Count - 1; i >= 0; i--)
    {
        if (!items[i].Type.Equals(t)) continue;
        double take = Math.Min(amount - done, (double)items[i].Amount);
        if (take <= 0) break;
        src.TransferItemTo(dst, i, null, true, (VRage.MyFixedPoint)take);
        done = (double)dst.GetItemAmount(t) - before;
        if (done >= amount - 0.01) break;
    }
    return done;
}
double CountOf(MyItemType t) { double v; stock.TryGetValue(t, out v); return v; }
Dictionary<long,bool> barCache = new Dictionary<long,bool>();
bool IsBaR(IMyTerminalBlock b)
{
    bool v;
    if (barCache.TryGetValue(b.EntityId, out v)) return v;
    v = false;
    try { b.GetValueBool("BuildAndRepair.ScriptControlled"); v = true; } catch { }
    barCache[b.EntityId] = v;
    return v;
}
void EnsureSpecialTemplate(IMyTerminalBlock b)
{
    if (b.CustomData.Contains("DUTC Special") || b.CustomData.Contains("Special Container modes")) return;
    b.CustomData = "# DUTC Special Container\n# One line per item: Name=Amount\n# Modes (IIM compatible):\n#   Name=100   keep 100, remove excess\n#   Name=100M  keep at least 100, ignore excess\n#   Name=100L  never fill, only remove above 100\n#   Name=All   store everything it can get\n# Ambiguous names: prefix the type, e.g. Ore/Stone=100\nSteelPlate=0\n" + b.CustomData;
}
void Warn(string m) { if (warnSet.Add(m)) warnings.Add(m); }
void Act(string m)
{
    actions.Insert(0, DateTime.Now.ToString("HH:mm:ss") + " " + m);
    if (actions.Count > 25) actions.RemoveAt(25);
}
string StripPct(string n)
{
    return System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(\d+\.?\d*%\)", "");
}
string Bar(int w, double f)
{
    int fi = (int)Math.Round(Math.Max(0, Math.Min(1, f)) * w);
    return "[" + new string('I', fi) + new string('.', w - fi) + "]";
}
string Pct(double cur, double max)
{
    if (max <= 0) return "0%";
    return Math.Round(cur / max * 100, 1) + "%";
}
string Num(double v)
{
    if (v >= 1000000) return Math.Round(v / 1000000, 1) + "M";
    if (v >= 10000) return Math.Round(v / 1000, 1) + "k";
    return Math.Round(v).ToString();
}
