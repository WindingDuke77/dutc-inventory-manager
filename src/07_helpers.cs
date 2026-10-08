// ---------- MODULE: shared helpers ----------

int SrcInvIndex(IMyTerminalBlock b) { return b is IMyProductionBlock ? 1 : 0; }

// classification is cached per item type - the food word scan only ever runs once per type
Dictionary<MyItemType,int> catCache = new Dictionary<MyItemType,int>();
int Cat(MyItemType t)
{
    int c;
    if (catCache.TryGetValue(t, out c)) return c;
    c = CatCalc(t);
    catCache[t] = c;
    return c;
}

int CatCalc(MyItemType t)
{
    foreach (var s in foodItems) if (t.SubtypeId == s) return FOOD;
    string ty = t.TypeId;
    if (ty.EndsWith("_Ore")) return ORE;
    if (ty.EndsWith("_Ingot")) return INGOT;
    if (ty.EndsWith("_Component")) return COMP;
    if (ty.EndsWith("_AmmoMagazine")) return AMMO;
    if (ty.EndsWith("_OxygenContainerObject") || ty.EndsWith("_GasContainerObject")) return BOTTLE;
    // broad type match: catches modded builder types like FoodItem, EdibleItem,
    // DrinkItem, SeedPack... not just the exact vanilla suffixes
    if (ty.Contains("Consumable") || ty.Contains("Ingredient") || ty.Contains("Seed") || ty.Contains("Food") || ty.Contains("Edible") || ty.Contains("Drink")) return FOOD;
    // vegetables and other produce that mods ship under a generic item type:
    // match by name, AFTER every real category had its chance (thanks Lord Byte)
    foreach (var w in foodWords) if (t.SubtypeId.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return FOOD;
    if (ty.EndsWith("_PhysicalGunObject") || ty.EndsWith("_PhysicalObject") || ty.EndsWith("_Datapad")) return TOOL;
    return -1;
}

bool IsCat(IMyTerminalBlock b, int c) { return cats[c].Contains(b); }

// panel order for multi-screen autocrafting: the number after the keyword,
// 0 when unnumbered, so "Autocrafting" leads and "Autocrafting 2" follows
int CraftOrd(string n)
{
    int p = n.IndexOf(CRAFT_KEY);
    if (p < 0) return int.MaxValue;
    var m = System.Text.RegularExpressions.Regex.Match(n.Substring(p + CRAFT_KEY.Length), @"\d+");
    return m.Success ? int.Parse(m.Value) : 0;
}

// any ice-type ore, modded included: FilteredIce, AlienLakeIce... (thanks aantono)
bool IsIce(MyItemType t) { return t.TypeId.EndsWith("_Ore") && t.SubtypeId.IndexOf("Ice", StringComparison.OrdinalIgnoreCase) >= 0; }

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

// conveyor path checks are ENGINE pathfinding - expensive on huge grids and not
// counted by the instruction limit. Results are cached and refreshed every ~5 min.
bool CanMove(IMyInventory src, long srcId, IMyInventory dst, long dstId, MyItemType t)
{
    long key = srcId * 397 ^ dstId;
    bool ok;
    if (convCache.TryGetValue(key, out ok)) return ok;
    try { ok = src.CanTransferItemTo(dst, t); } catch { ok = false; }
    convCache[key] = ok;
    return ok;
}

// can this block's inventory actually reach the conveyor network?
// cached + budgeted: at most a few live probes per cycle, rest deferred
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
// what autocraft quotas see (docked ships / turrets / specials filtered per config)
double CountOfCraft(MyItemType t) { double v; craftStock.TryGetValue(t, out v); return v; }

// Nanobot Build & Repair blocks manage their own inventory - never touch them
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
