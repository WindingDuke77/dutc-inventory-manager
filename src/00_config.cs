// ========================================
//         DUTC INVENTORY  v2.0.4
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
