using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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
    var copyState = Bind<CopyState>("STR_CopyState");
    var loadState = Bind<LoadState>("STR_LoadState");
    var copyError = Bind<CopyState>("STR_CopyLastError");
    var trackedCount = Bind<TrackedCount>("STR_GetTrackedItemCount");
    var copyTrackedKey = Bind<CopyTrackedKey>("STR_CopyTrackedItemKey");
    var trackedBase = Bind<TrackedValue>("STR_GetTrackedItemBaseValue");
    var trackedKind = Bind<TrackedValue>("STR_GetTrackedItemKind");
    var copyManifest = Bind<CopyState>("STR_CopyCaravanManifest");
    var caravanLoss = Bind<CaravanLoss>("STR_ReportCaravanLoss");
    var caravanArrival = Bind<CaravanArrival>("STR_ReportCaravanArrival");

    using var config = new Utf8(Path.Combine(root, "data/mvp-economy.json"));
    using var catalog = new Utf8(Path.Combine(root, "tests/fixtures/catalog.json"));
    using var none = new Utf8("");
    using var markarth = new Utf8("markarth");
    using var whiterun = new Utf8("whiterun");
    using var ingot = new Utf8("05ACE4:Skyrim.esm");

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
    Check(trade(1, markarth.Pointer, ingot.Pointer, 3) == 1, "trade applied");
    Check(trade(1, markarth.Pointer, ingot.Pointer, 3) == 0, "trade replay ignored");
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
        var stateJson = Marshal.PtrToStringUTF8(buffer, size)!;
        using var doc = JsonDocument.Parse(stateJson);
        var fingerprint = doc.RootElement.GetProperty("catalogFingerprint").GetString()!;
        Check(fingerprint.Length == 64, "catalog fingerprint persisted");
        var saved = doc.RootElement.GetProperty("state");
        Check(saved.GetProperty("lastTradeSequence").GetInt64() == 1, "trade sequence persisted");
        Check(saved.GetProperty("totalPlayerTradeDelta").GetInt64() == 6, "trade quantity persisted");
        Check(advance(6) == 6, "advance to next day");
        Check(loadState(buffer, size) == 5, "restore day 5 state");
        Check(price(markarth.Pointer, ingot.Pointer) == 6, "restored price");
        var manifestSize = copyManifest(IntPtr.Zero, 0);
        Check(manifestSize > 0, "manifest size");
        var manifestBuffer = Marshal.AllocHGlobal(manifestSize);
        try
        {
            Check(copyManifest(manifestBuffer, manifestSize) == manifestSize, "copy caravan manifest");
            using var manifest = JsonDocument.Parse(Marshal.PtrToStringUTF8(manifestBuffer, manifestSize)!);
            var caravans = manifest.RootElement.GetProperty("caravans");
            Check(caravans.GetArrayLength() > 0, "active caravan at day 5");
            var shipmentId = caravans[0].GetProperty("shipmentId").GetInt64();
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
delegate int CaravanLoss(long sequence, long shipmentId, int lostQuantity);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CaravanArrival(long sequence, long shipmentId);

sealed class Utf8 : IDisposable
{
    public IntPtr Pointer { get; }
    public Utf8(string value) => Pointer = Marshal.StringToCoTaskMemUTF8(value);
    public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
}
