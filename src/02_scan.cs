// ---------- MODULE: block scanning + item counting + container assignment ----------

// big bases: block processing is chunked across ticks so one scan can never
// stall the game (thanks Oxnard for the 1044-inventory report)
bool Scan()
{
    if (scanPos == 0)
    {
        cycles++;
        warnings.Clear(); warnSet.Clear();
        reachBudget = 5;
        // conveyor path results are cached; refresh them every ~5 minutes
        if (cycles % 150 == 0) { convCache.Clear(); reachCache.Clear(); }

        // multi-instance election: mark this PB, then check for other running instances.
        // Station beats ship; same class -> lowest EntityId wins. Loser goes standby.
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
        // machines can't be type containers - a "Food Processor" contains the word
        // Food but must never be treated as the Food storage (thanks Oxnard)
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
    // one assembler per block type - blueprint discovery only needs to probe each type once
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
    // "Autocrafting 1", "Autocrafting 2"... panels follow the number AFTER the
    // keyword, whatever the rest of the block name looks like (thanks Lord Byte)
    craftLCDs.Sort((a, z) => { int na = CraftOrd(a.CustomName), nz = CraftOrd(z.CustomName); return na != nz ? na.CompareTo(nz) : a.CustomName.CompareTo(z.CustomName); });
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

// item counting is chunked like Scan: a 1000+ inventory base can never push one
// tick past the instruction limit (thanks EBALL360). Partial counts accumulate
// in stockTmp and only replace the live stock once the pass is complete.
bool Count()
{
    if (countPass == 0 && countPos == 0) stockTmp.Clear();
    var items = new List<MyInventoryItem>();
    while (countPass < 2)
    {
        var list = countPass == 0 ? allInv : specials;
        for (; countPos < list.Count; countPos++)
        {
            if (Runtime.CurrentInstructionCount > 32000) return false;
            var b = list[countPos];
            if (b.CustomName.Contains(HIDDEN_KEY)) continue;
            for (int q = 0; q < b.InventoryCount; q++)
            {
                items.Clear(); b.GetInventory(q).GetItems(items);
                foreach (var it in items)
                {
                    double a = (double)it.Amount;
                    if (stockTmp.ContainsKey(it.Type)) stockTmp[it.Type] += a; else stockTmp[it.Type] = a;
                }
            }
        }
        countPos = 0; countPass++;
    }
    countPass = 0;
    stock.Clear();
    foreach (var kv in stockTmp) stock[kv.Key] = kv.Value;
    byName.Clear();
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
            // decorative lockers/cabinets have inventories but no conveyor ports -
            // only assign containers that are actually reachable through conveyors
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
