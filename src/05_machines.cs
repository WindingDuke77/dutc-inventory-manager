// ---------- MODULE: refineries, ice, uranium, cleanup, bottle top-up ----------

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
    // equalize each ore across all refineries (rich -> poor, direct transfer)
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

// keep every turret and fixed gun loaded - subgrids (rotors/pistons) included,
// as long as a conveyor path exists
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
    // balance EVERY ice-type ore, modded ones included (thanks aantono)
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

// ingots per craft, order: Fe, Ni, Co, Si, Ag, Au, Pt, Mg, Stone(gravel)
// vanilla values - blueprint ingredients are not readable from PB scripts.
// Unknown/modded recipes: the assembler's own conveyor pull still handles them.
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

// push the ingots each queued assembler needs straight into its input
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
        // only re-run bottles through a tank when NEW bottles arrived since last time -
        // stops the endless container<->tank ping-pong (thanks Oxnard)
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
