using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 1) throw new ArgumentException("Pass the published Native AOT DLL path.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var library = NativeLibrary.Load(Path.GetFullPath(args[0]));
// Native AOT libraries remain loaded until process exit.
    T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    var initialize = Bind<Initialize>("STR_Initialize");
    var advance = Bind<Advance>("STR_AdvanceToDay");
    var syncWorldDay = Bind<Advance>("STR_SyncWorldDay");
    var price = Bind<Price>("STR_GetPrice");
    var trade = Bind<Trade>("STR_ApplyTrade");
    var tradeAuto = Bind<TradeAuto>("STR_ApplyTradeAuto");
    var copyState = Bind<CopyState>("STR_CopyState");
    var loadState = Bind<LoadState>("STR_LoadState");
    var copyError = Bind<CopyState>("STR_CopyLastError");
    var trackedCount = Bind<TrackedCount>("STR_GetTrackedItemCount");
    var copyTrackedKey = Bind<CopyTrackedKey>("STR_CopyTrackedItemKey");
    var trackedBase = Bind<TrackedValue>("STR_GetTrackedItemBaseValue");
    var trackedKind = Bind<TrackedValue>("STR_GetTrackedItemKind");
    var copyManifest = Bind<CopyState>("STR_CopyCaravanManifest");
    var copyStatus = Bind<CopyState>("STR_CopyStatus");
    var copyVisiblePlan = Bind<CopyPlan>("STR_CopyVisibleCaravanPlan");
    var copyPhysicalPlan = Bind<CopyPlan>("STR_CopyPhysicalCaravanPlan");
    var confirmVisibleSpawn = Bind<ConfirmVisibleSpawn>("STR_ConfirmVisibleCaravanSpawn");
    var confirmPhysicalSpawn = Bind<ConfirmVisibleSpawn>("STR_ConfirmPhysicalCaravanSpawn");
    var confirmPhysicalGroup = Bind<ConfirmPhysicalGroup>("STR_ConfirmPhysicalCaravanGroup");
    var updateVisibleWaypoint = Bind<UpdateVisibleWaypoint>("STR_UpdateVisibleWaypoint");
    var getPhysicalGroup = Bind<GetPhysicalGroup>("STR_GetPhysicalCaravanGroup");
    var rebindPhysicalGroup = Bind<RebindPhysicalGroup>("STR_RebindPhysicalCaravanGroup");
    var releaseVisible = Bind<ReleaseVisible>("STR_ReleaseVisibleCaravan");
    var caravanLoss = Bind<CaravanLoss>("STR_ReportCaravanLoss");
    var caravanArrival = Bind<CaravanArrival>("STR_ReportCaravanArrival");

    using var config = new Utf8(Path.Combine(root, "data/mvp-economy.json"));
    using var catalog = new Utf8(Path.Combine(root, "tests/fixtures/catalog.json"));
    using var none = new Utf8("");
    using var markarth = new Utf8("markarth");
    using var whiterun = new Utf8("whiterun");
    using var ingot = new Utf8("05ACE4:Skyrim.esm");
    byte[] savedState = [];

    Check(initialize(config.Pointer, catalog.Pointer, none.Pointer) == 0, "initialize day 0");
    Check(trackedCount() > 0, "tracked items available");
    var ironIndex = -1;
    for (var index = 0; index < trackedCount(); index++)
    {
        var keySize = copyTrackedKey(index, IntPtr.Zero, 0);
        Check(keySize > 0 && keySize < 256, "tracked item key size");
        var keyBuffer = Marshal.AllocHGlobal(keySize);
        try
        {
            Check(copyTrackedKey(index, keyBuffer, keySize) == keySize, "tracked item key copied");
            if (Marshal.PtrToStringUTF8(keyBuffer, keySize) == "05ACE4:Skyrim.esm") ironIndex = index;
        }
        finally { Marshal.FreeHGlobal(keyBuffer); }
    }
    Check(ironIndex >= 0 && trackedBase(ironIndex) == 7 && trackedKind(ironIndex) == 1,
        "tracked iron ingot metadata");
    Check(trackedBase(trackedCount()) == -1, "invalid tracked index rejected");
    Check(price(markarth.Pointer, ingot.Pointer) == 7, "initial price");
    Check(advance(5) == 5, "advance five days");
    Check(price(markarth.Pointer, ingot.Pointer) == 6, "Markarth day 5 price");
    Check(price(whiterun.Pointer, ingot.Pointer) == 8, "Whiterun day 5 price");
    var physicalSize = copyPhysicalPlan(whiterun.Pointer, IntPtr.Zero, 0);
    Check(physicalSize == 4, "no physical route without verified markers");
    Check(confirmPhysicalSpawn(1, whiterun.Pointer, 0xFF012345) == -1,
        "physical binding rejected without verified road");
    var planSize = copyVisiblePlan(whiterun.Pointer, IntPtr.Zero, 0);
    Check(planSize > 4, "visible caravan plan available");
    var planBuffer = Marshal.AllocHGlobal(planSize);
    long visibleShipmentId;
    const uint visibleLeader = 0xFF012345;
    try
    {
        Check(copyVisiblePlan(whiterun.Pointer, planBuffer, planSize) == planSize, "visible plan copied");
        using var plan = JsonDocument.Parse(Marshal.PtrToStringUTF8(planBuffer, planSize)!);
        visibleShipmentId = plan.RootElement.GetProperty("shipmentId").GetInt64();
        Check(plan.RootElement.GetProperty("action").GetString() == "Spawn", "visible plan requests spawn");
    }
    finally { Marshal.FreeHGlobal(planBuffer); }
    Check(confirmVisibleSpawn(visibleShipmentId, whiterun.Pointer, visibleLeader) == 1, "visible spawn bound");
    Check(confirmVisibleSpawn(visibleShipmentId, whiterun.Pointer, visibleLeader) == 0, "visible spawn replay ignored");
    var statusSize = copyStatus(IntPtr.Zero, 0);
    Check(statusSize > 0, "status size");
    var statusBuffer = Marshal.AllocHGlobal(statusSize);
    try
    {
        Check(copyStatus(statusBuffer, statusSize) == statusSize, "status copied");
        using var status = JsonDocument.Parse(Marshal.PtrToStringUTF8(statusBuffer, statusSize)!);
        Check(status.RootElement.GetProperty("day").GetInt32() == 5, "status day");
        Check(status.RootElement.GetProperty("regions").GetProperty("markarth").GetProperty("iron")
            .GetProperty("regionalBaseValue").GetInt32() > 0, "status price");
        Check(status.RootElement.GetProperty("caravans").GetArrayLength() > 0, "status caravans");
    }
    finally { Marshal.FreeHGlobal(statusBuffer); }
    Check(trade(1, markarth.Pointer, ingot.Pointer, 3) == 1, "trade applied");
    Check(trade(1, markarth.Pointer, ingot.Pointer, 3) == 0, "trade replay ignored");
    using (var invalidRegion = new Utf8("unknown"))
        Check(tradeAuto(invalidRegion.Pointer, ingot.Pointer, 1) == -1, "invalid auto trade rejected");
    Check(tradeAuto(markarth.Pointer, ingot.Pointer, 1) == 2, "automatic trade sequence follows explicit trade");
    Check(price(markarth.Pointer, ingot.Pointer) == 6, "price frozen until next day");

    var size = copyState(IntPtr.Zero, 0);
    if (size < 0)
    {
        var detail = Marshal.AllocHGlobal(2048);
        try
        {
            copyError(detail, 2048);
            throw new InvalidOperationException("STR_CopyState: " + Marshal.PtrToStringUTF8(detail));
        }
        finally { Marshal.FreeHGlobal(detail); }
    }
    Check(size > 0 && size < 16 * 1024 * 1024, "state byte count");
    var buffer = Marshal.AllocHGlobal(size);
    try
    {
        Check(copyState(buffer, size) == size, "copy state");
        savedState = new byte[size];
        Marshal.Copy(buffer, savedState, 0, size);
        var stateJson = Marshal.PtrToStringUTF8(buffer, size)!;
        using var doc = JsonDocument.Parse(stateJson);
        var fingerprint = doc.RootElement.GetProperty("catalogFingerprint").GetString()!;
        Check(fingerprint.Length == 64, "catalog fingerprint persisted");
        var saved = doc.RootElement.GetProperty("state");
        Check(saved.GetProperty("lastTradeSequence").GetInt64() == 2, "trade sequence persisted");
        Check(saved.GetProperty("totalPlayerTradeDelta").GetInt64() == 8, "trade quantity persisted");
        Check(advance(6) == 6, "advance to next day");
        Check(loadState(buffer, size) == 5, "restore day 5 state");
        Check(price(markarth.Pointer, ingot.Pointer) == 6, "restored price");
        Check(releaseVisible(visibleShipmentId, visibleLeader) == 1, "restored visible caravan released");
        Check(releaseVisible(visibleShipmentId, visibleLeader) == 0, "visible release replay ignored");
        var manifestSize = copyManifest(IntPtr.Zero, 0);
        Check(manifestSize > 0, "manifest size");
        var manifestBuffer = Marshal.AllocHGlobal(manifestSize);
        try
        {
            Check(copyManifest(manifestBuffer, manifestSize) == manifestSize, "copy caravan manifest");
            using var manifest = JsonDocument.Parse(Marshal.PtrToStringUTF8(manifestBuffer, manifestSize)!);
            var caravans = manifest.RootElement.GetProperty("caravans");
            Check(caravans.GetArrayLength() > 0, "active caravan at day 5");
            var shipmentId = caravans.EnumerateArray()
                .First(x => x.GetProperty("status").GetString() == "InTransit")
                .GetProperty("shipmentId").GetInt64();
            Check(caravanLoss(1, shipmentId, 1) == 1, "caravan loses cargo");
            Check(caravanLoss(1, shipmentId, 1) == 0, "caravan loss replay ignored");
            Check(caravanArrival(2, shipmentId) == 1, "caravan arrives");
            Check(caravanArrival(2, shipmentId) == 0, "caravan arrival replay ignored");
            var eventStateSize = copyState(IntPtr.Zero, 0);
            var eventState = Marshal.AllocHGlobal(eventStateSize);
            try
            {
                Check(copyState(eventState, eventStateSize) == eventStateSize, "copy caravan event state");
                using var stateDoc = JsonDocument.Parse(Marshal.PtrToStringUTF8(eventState, eventStateSize)!);
                Check(stateDoc.RootElement.GetProperty("state").GetProperty("lastCaravanEventSequence").GetInt64() == 2,
                    "caravan sequence persisted");
            }
            finally { Marshal.FreeHGlobal(eventState); }
        }
        finally { Marshal.FreeHGlobal(manifestBuffer); }
        var wrongFingerprint = (fingerprint[0] == '0' ? "1" : "0") + fingerprint[1..];
        using var staleState = new Utf8(stateJson.Replace(fingerprint, wrongFingerprint, StringComparison.Ordinal));
        Check(loadState(staleState.Pointer, Encoding.UTF8.GetByteCount(stateJson)) == -1, "reject changed catalog");
        Check(price(markarth.Pointer, ingot.Pointer) == 6, "failed load preserves live economy");
    }
    finally { Marshal.FreeHGlobal(buffer); }

    Check(syncWorldDay(200) == 5, "first world day establishes origin");
    var clockStateSize = copyState(IntPtr.Zero, 0);
    var clockState = Marshal.AllocHGlobal(clockStateSize);
    try
    {
        Check(copyState(clockState, clockStateSize) == clockStateSize, "world day origin saved");
        Check(syncWorldDay(201) == 6, "world day advances economy once");
        Check(syncWorldDay(201) == 6, "repeated world day is idempotent");
        Check(loadState(clockState, clockStateSize) == 5, "restore world day origin");
        Check(syncWorldDay(201) == 6, "restored origin advances on following day");
        Check(syncWorldDay(200) == 6, "backward world day leaves economy unchanged");
    }
    finally { Marshal.FreeHGlobal(clockState); }
    Check(advance(4) == -1, "reject backward day");
    var errorSize = copyError(IntPtr.Zero, 0);
    Check(errorSize > 0, "error available to native caller");
    var errorBuffer = Marshal.AllocHGlobal(errorSize + 1);
    try
    {
        copyError(errorBuffer, errorSize + 1);
        Check(Marshal.PtrToStringUTF8(errorBuffer)!.Contains("ArgumentOutOfRangeException", StringComparison.Ordinal), "error text");
    }
    finally { Marshal.FreeHGlobal(errorBuffer); }

    var physicalFolder = Path.Combine(root, "build/native-physical-checks");
    Directory.CreateDirectory(physicalFolder);
    var physicalCatalogPath = Path.Combine(physicalFolder, "STRCatalog.json");
    File.Copy(Path.Combine(root, "tests/fixtures/catalog.json"), physicalCatalogPath, overwrite: true);
    File.WriteAllText(Path.Combine(physicalFolder, "STRVisibleRoutes.json"),
        """
        {"schemaVersion":1,"routes":[{"routeId":"whiterun-riften","enabled":true,
        "navmeshVerified":true,"worldspace":"00003C:Skyrim.esm",
        "anchors":["000100:Test.esp","000101:Test.esp"],
        "spawnMarker":"000102:Test.esp","retireMarker":"000103:Test.esp",
        "minPlayerDistance":1000,"maxPlayerDistance":5000,"stuckTimeoutSeconds":30}],
        "cargoItems":[{"formKey":"05ACE4:Skyrim.esm","commodity":"iron","units":2}]}
        """);
    using (var physicalCatalog = new Utf8(physicalCatalogPath))
    {
        Check(initialize(config.Pointer, physicalCatalog.Pointer, none.Pointer) == 0,
            "verified physical route initialized");
        Check(advance(5) == 5, "physical route advances to shipment day");
        var sizePhysical = copyPhysicalPlan(whiterun.Pointer, IntPtr.Zero, 0);
        var bufferPhysical = Marshal.AllocHGlobal(sizePhysical);
        long physicalShipmentId;
        try
        {
            Check(copyPhysicalPlan(whiterun.Pointer, bufferPhysical, sizePhysical) == sizePhysical,
                "physical plan copied");
            using var doc = JsonDocument.Parse(Marshal.PtrToStringUTF8(bufferPhysical, sizePhysical)!);
            physicalShipmentId = doc.RootElement.GetProperty("shipment").GetProperty("shipmentId").GetInt64();
            Check(doc.RootElement.GetProperty("shipment").GetProperty("action").GetString() == "Spawn",
                "verified road permits spawn");
            Check(doc.RootElement.GetProperty("cargo").EnumerateArray().Sum(x =>
                x.GetProperty("count").GetInt32() * x.GetProperty("unitsEach").GetInt32()) +
                doc.RootElement.GetProperty("unrepresentedUnits").GetInt32() ==
                doc.RootElement.GetProperty("shipment").GetProperty("remainingQuantity").GetInt32(),
                "physical cargo conserves shipment units");
        }
        finally { Marshal.FreeHGlobal(bufferPhysical); }
        Check(confirmPhysicalGroup(physicalShipmentId, whiterun.Pointer,
            0xFF000111, 0xFF000112, 0xFF000113) == 1, "physical trio bound");
        Check(updateVisibleWaypoint(physicalShipmentId, 0xFF000111, 1, 3) == 1,
            "physical waypoint advanced");
        Check(getPhysicalGroup(out var boundShipment, out var boundLeader, out var boundPack,
            out var boundGuard) == 1 && boundShipment == physicalShipmentId &&
            boundLeader == 0xFF000111 && boundPack == 0xFF000112 && boundGuard == 0xFF000113,
            "physical actor references returned");
        var physicalStateSize = copyState(IntPtr.Zero, 0);
        var physicalState = Marshal.AllocHGlobal(physicalStateSize);
        try
        {
            Check(copyState(physicalState, physicalStateSize) == physicalStateSize,
                "physical group state copied");
            Check(initialize(config.Pointer, physicalCatalog.Pointer, none.Pointer) == 0,
                "physical group engine reset");
            Check(loadState(physicalState, physicalStateSize) == 5,
                "physical group restored");
            Check(rebindPhysicalGroup(physicalShipmentId, 0xFF000111,
                0xFF100111, 0xFF100112, 0xFF100113) == 1,
                "physical actor FormIDs remapped");
            Check(getPhysicalGroup(out _, out boundLeader, out boundPack, out boundGuard) == 1 &&
                boundLeader == 0xFF100111 && boundPack == 0xFF100112 && boundGuard == 0xFF100113,
                "resolved physical actor references retained");
        }
        finally { Marshal.FreeHGlobal(physicalState); }
    }

    // An additive catalog may reuse the economy state only when the shipped
    // previous catalog exactly matches the co-save and every old mapping is intact.
    var migrationFolder = Path.Combine(root, "build/native-migration-checks");
    Directory.CreateDirectory(migrationFolder);
    var migrationSettingsPath = Path.Combine(migrationFolder, "STRSettings.json");
    if (File.Exists(migrationSettingsPath)) File.Delete(migrationSettingsPath);
    var expandedCatalogPath = Path.Combine(migrationFolder, "STRCatalog.json");
    var previousCatalogPath = Path.Combine(migrationFolder, "STRPreviousCatalog.json");
    var expandedCatalog = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/fixtures/catalog.json")))!;
    expandedCatalog["records"]![1]!["commodity"] = "food";
    expandedCatalog["records"]![1]!["units"] = 1;
    File.WriteAllText(expandedCatalogPath, expandedCatalog.ToJsonString());
    File.Copy(Path.Combine(root, "tests/fixtures/catalog.json"), previousCatalogPath, overwrite: true);
    using var expandedPath = new Utf8(expandedCatalogPath);
    Check(initialize(config.Pointer, expandedPath.Pointer, none.Pointer) == 0, "compatible catalog initialized");
    var savedBuffer = Marshal.AllocHGlobal(savedState.Length);
    try
    {
        Marshal.Copy(savedState, 0, savedBuffer, savedState.Length);
        Check(loadState(savedBuffer, savedState.Length) == 5, "additive catalog restores old state");
        Check(price(markarth.Pointer, ingot.Pointer) == 6, "migrated regional price");
        var migratedSize = copyState(IntPtr.Zero, 0);
        var migratedBuffer = Marshal.AllocHGlobal(migratedSize);
        try
        {
            Check(copyState(migratedBuffer, migratedSize) == migratedSize, "migrated state copied");
            using var migrated = JsonDocument.Parse(Marshal.PtrToStringUTF8(migratedBuffer, migratedSize)!);
            Check(migrated.RootElement.GetProperty("catalogFingerprint").GetString() !=
                JsonDocument.Parse(savedState).RootElement.GetProperty("catalogFingerprint").GetString(),
                "next co-save uses expanded catalog fingerprint");
        }
        finally { Marshal.FreeHGlobal(migratedBuffer); }
        expandedCatalog["records"]![0]!["baseValue"] = 8;
        File.WriteAllText(previousCatalogPath, expandedCatalog.ToJsonString());
        Check(initialize(config.Pointer, expandedPath.Pointer, none.Pointer) == -1,
            "changed previously tracked item blocks migration");
        File.Delete(previousCatalogPath);
        Check(initialize(config.Pointer, expandedPath.Pointer, none.Pointer) == 0,
            "expanded catalog without sidecar initializes");
        Check(loadState(savedBuffer, savedState.Length) == -1,
            "old co-save rejected without compatible previous catalog");
        File.Copy(Path.Combine(root, "tests/fixtures/catalog.json"), previousCatalogPath, overwrite: true);
        File.WriteAllText(migrationSettingsPath,
            "{\"schemaVersion\":1,\"priceIntensity\":0,\"visibleCaravansEnabled\":false}");
        Check(initialize(config.Pointer, expandedPath.Pointer, none.Pointer) == 0, "runtime settings initialized");
        Check(loadState(savedBuffer, savedState.Length) == 5, "settings do not invalidate saved economy");
        Check(price(markarth.Pointer, ingot.Pointer) == 7, "zero price intensity preserves base value");
        var disabledPlanSize = copyVisiblePlan(whiterun.Pointer, IntPtr.Zero, 0);
        var disabledPlan = Marshal.AllocHGlobal(disabledPlanSize);
        try
        {
            Check(copyVisiblePlan(whiterun.Pointer, disabledPlan, disabledPlanSize) == disabledPlanSize,
                "disabled visible plan copied");
            using var disabled = JsonDocument.Parse(Marshal.PtrToStringUTF8(disabledPlan, disabledPlanSize)!);
            Check(disabled.RootElement.GetProperty("action").GetString() == "Retire",
                "disabled setting retires an already bound caravan");
        }
        finally { Marshal.FreeHGlobal(disabledPlan); }
        Check(confirmVisibleSpawn(visibleShipmentId, whiterun.Pointer, visibleLeader) == -1,
            "disabled setting rejects new spawn confirmation");
        Check(releaseVisible(visibleShipmentId, visibleLeader) == 1,
            "disabled setting permits retirement");
    }
    finally { Marshal.FreeHGlobal(savedBuffer); }

    Console.WriteLine("PASS Native AOT exports: prices, world day, trade, caravans, state round-trip, errors");

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"FAIL {description}");
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Initialize(IntPtr config, IntPtr catalog, IntPtr state);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Advance(int day);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Price(IntPtr region, IntPtr formKey);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Trade(long sequence, IntPtr region, IntPtr formKey, int count);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate long TradeAuto(IntPtr region, IntPtr formKey, int count);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CopyState(IntPtr destination, int capacity);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int LoadState(IntPtr source, int length);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int TrackedCount();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int TrackedValue(int index);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CopyTrackedKey(int index, IntPtr destination, int capacity);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CopyPlan(IntPtr region, IntPtr destination, int capacity);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int ConfirmVisibleSpawn(long shipmentId, IntPtr region, uint leaderFormId);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int ConfirmPhysicalGroup(long shipmentId, IntPtr region, uint leaderFormId, uint packFormId, uint guardFormId);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int UpdateVisibleWaypoint(long shipmentId, uint leaderFormId, int waypointIndex, int anchorCount);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int GetPhysicalGroup(out long shipmentId, out uint leaderFormId, out uint packFormId, out uint guardFormId);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int RebindPhysicalGroup(long shipmentId, uint oldLeaderFormId, uint leaderFormId, uint packFormId, uint guardFormId);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int ReleaseVisible(long shipmentId, uint leaderFormId);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CaravanLoss(long sequence, long shipmentId, int lostQuantity);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CaravanArrival(long sequence, long shipmentId);

sealed class Utf8 : IDisposable
{
    public IntPtr Pointer { get; }
    public Utf8(string value) => Pointer = Marshal.StringToCoTaskMemUTF8(value);
    public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
}
