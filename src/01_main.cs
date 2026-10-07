// ---------- MAIN: state, entry point, step machine ----------

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
int scanPhase = 0;
int countPos = 0;
int countPass = 0;
Dictionary<MyItemType,double> stockTmp = new Dictionary<MyItemType,double>();
HashSet<string> quickMiss = new HashSet<string>();
HashSet<long> ourDisAsm = new HashSet<long>();
int specialIdx = 0;
int scrPos = 0;
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
string report = "";
Dictionary<long,string> beatSeen = new Dictionary<long,string>();
Dictionary<long,int> beatAge = new Dictionary<long,int>();
string lastError = "";
string[] stepNames = { "Scan","Count","Assign","Sort","Balance","Special","Craft","Refineries","Ice","Uranium","Turrets","Cleanup","Screens" };

public Program()
{
    catKeys = new[]{ ORE_KEY, INGOT_KEY, COMP_KEY, TOOL_KEY, AMMO_KEY, BOTTLE_KEY, FOOD_KEY };
    for (int i = 0; i < NCAT; i++) cats[i] = new List<IMyTerminalBlock>();
    InitRecipes();
    // blueprint knowledge survives recompiles and world reloads - uncraftable
    // items (Fruit etc.) are only ever swept once per world (thanks Oxnard)
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
    string raw = arg.Trim();
    arg = raw.ToLower();
    if (arg == "pause") paused = true;
    if (arg == "resume" || arg == "run") paused = false;
    // diagnostics: "items <text>" lists matching stock item ids (case preserved),
    // "bp <name>" probes a blueprint name against the visible assemblers
    if (arg.StartsWith("items ")) ItemsCmd(raw.Substring(6).Trim());
    if (arg.StartsWith("bp ")) BpProbeCmd(raw.Substring(3).Trim());
    if (arg == "bps") BpsCmd();
    if (arg == "reset") { bpCache.Clear(); noBp.Clear(); probePos.Clear(); convCache.Clear(); reachCache.Clear(); barCache.Clear(); quickMiss.Clear(); catCache.Clear(); learnedBps.Clear(); learnedSet.Clear(); Storage = ""; lastError = ""; report = ""; }
    tick++;
    Echo("DUTC INVENTORY " + "|/-\\"[tick % 4]);
    Echo("Step: " + stepNames[step]);
    Echo("Inventories: " + allInv.Count + "  Specials: " + specials.Count);
    Echo("Warnings: " + warnings.Count);
    if (standby) Echo("STANDBY - sorting handled by:\n" + masterName);
    if (lastError != "") Echo("Last error: " + lastError);
    if (report != "") Echo("\n" + report);
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
    // standby (another instance is the active manager): keep scanning, counting,
    // balancing of OWN containers, own-grid machine refills and screens -
    // but no sorting/assigning/crafting/specials
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
