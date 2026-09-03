// ---------- MODULE: item sorting + special containers ----------

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

// Even split: every item spread equally across all containers of its type.
// 9 tools in 3 Tools containers -> 3 each.
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

// IIM-compatible modes: item=100 (normal), item=100M (minimum), item=100L (limiter), item=All
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
