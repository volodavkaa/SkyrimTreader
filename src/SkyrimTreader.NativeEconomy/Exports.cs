// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkyrimTreader.Simulation;

namespace SkyrimTreader.NativeEconomy;

// All entry points use C-compatible integers and UTF-8 byte buffers. No managed
// exception is allowed to cross the SKSE/native boundary.
public static unsafe class Exports
{
    private static readonly object Sync = new();
    private static readonly NativeJsonContext JsonContext = new(new JsonSerializerOptions(EconomyJson.Options));
    private static EconomyConfig? _config;
    private static ItemCatalog? _catalog;
    private static CatalogItem[] _trackedItems = [];
    private static EconomyEngine? _engine;
    private static string _catalogFingerprint = "";
    private static string _compatiblePreviousCatalogFingerprint = "";
    private static RuntimeSettings _settings = new();
    private static PhysicalCaravanRoutes _physicalRoutes = new();
    private static int? _worldDayOrigin;
    private static string _lastError = "";

    [UnmanagedCallersOnly(EntryPoint = "STR_Initialize")]
    public static int Initialize(byte* configPath, byte* catalogPath, byte* statePath)
    {
        lock (Sync)
        {
            try
            {
                var config = EconomyJson.LoadConfig(ReadRequired(configPath));
                var catalogFile = ReadRequired(catalogPath);
                var catalog = ItemCatalog.Load(catalogFile, config);
                var settingsFile = Path.Combine(Path.GetDirectoryName(catalogFile)!, "STRSettings.json");
                var settings = File.Exists(settingsFile)
                    ? JsonSerializer.Deserialize(File.ReadAllText(settingsFile), JsonContext.RuntimeSettings)
                        ?? throw new InvalidDataException("Runtime settings are empty.")
                    : new RuntimeSettings();
                settings.Validate();
                var routeFile = Path.Combine(Path.GetDirectoryName(catalogFile)!, "STRVisibleRoutes.json");
                var physicalRoutes = File.Exists(routeFile)
                    ? PhysicalCaravanRoutes.Load(routeFile, config, catalog)
                    : new PhysicalCaravanRoutes();
                var catalogFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(catalogFile)));
                var previousCatalogFile = Path.Combine(Path.GetDirectoryName(catalogFile)!, "STRPreviousCatalog.json");
                var previousCatalogFingerprint = "";
                if (File.Exists(previousCatalogFile))
                {
                    var previous = ItemCatalog.Load(previousCatalogFile, config);
                    if (!CanMigrateCatalog(previous, catalog))
                        throw new InvalidDataException("Previous catalog changes a previously tracked item.");
                    previousCatalogFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(previousCatalogFile)));
                }
                var stateFile = ReadOptional(statePath);
                var envelope = stateFile.Length > 0 && File.Exists(stateFile)
                    ? ReadEnvelope(File.ReadAllBytes(stateFile), catalogFingerprint, previousCatalogFingerprint)
                    : null;
                var engine = new EconomyEngine(config, envelope?.State, seed: 42);
                _config = config;
                _catalog = catalog;
                _trackedItems = catalog.Records.Where(item => item.Commodity is not null)
                    .OrderBy(item => item.FormKey, StringComparer.OrdinalIgnoreCase).ToArray();
                _engine = engine;
                _catalogFingerprint = catalogFingerprint;
                _settings = settings;
                _physicalRoutes = physicalRoutes;
                _compatiblePreviousCatalogFingerprint = previousCatalogFingerprint;
                _worldDayOrigin = envelope?.WorldDayOrigin;
                _lastError = "";
                return engine.State.Day;
            }
            catch (Exception ex)
            {
                _config = null;
                _catalog = null;
                _trackedItems = [];
                _engine = null;
                _catalogFingerprint = "";
                _compatiblePreviousCatalogFingerprint = "";
                _settings = new RuntimeSettings();
                _physicalRoutes = new PhysicalCaravanRoutes();
                _worldDayOrigin = null;
                return Fail(ex);
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_AdvanceToDay")]
    public static int AdvanceToDay(int day)
    {
        lock (Sync)
        {
            try
            {
                var engine = RequireEngine();
                if (day < engine.State.Day || day - engine.State.Day > 10000)
                    throw new ArgumentOutOfRangeException(nameof(day));
                engine.AdvanceDays(day - engine.State.Day);
                _lastError = "";
                return engine.State.Day;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_SyncWorldDay")]
    public static int SyncWorldDay(int absoluteDay)
    {
        lock (Sync)
        {
            try
            {
                if (absoluteDay < 0) throw new ArgumentOutOfRangeException(nameof(absoluteDay));
                var engine = RequireEngine();
                var origin = _worldDayOrigin ?? checked(absoluteDay - engine.State.Day);
                var target = checked(absoluteDay - origin);
                // Console commands and some load transitions can move the game clock
                // backwards. Never rewind an already processed economy day.
                if (target < engine.State.Day)
                {
                    _lastError = "";
                    return engine.State.Day;
                }
                if (target - engine.State.Day > 10000)
                    throw new ArgumentOutOfRangeException(nameof(absoluteDay));
                engine.AdvanceDays(target - engine.State.Day);
                _worldDayOrigin = origin;
                _lastError = "";
                return engine.State.Day;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_GetPrice")]
    public static int GetPrice(byte* region, byte* formKey)
    {
        lock (Sync)
        {
            try
            {
                var engine = RequireEngine();
                var regionId = ReadRequired(region);
                var itemId = ReadRequired(formKey);
                if (!engine.State.Regions.ContainsKey(regionId))
                    throw new ArgumentException("Unknown region.", nameof(region));
                var item = _catalog!.Find(itemId) ?? throw new ArgumentException("Unknown form key.", nameof(formKey));
                var simulated = engine.RegionalItemValue(regionId, item);
                var value = Math.Clamp(Math.Round(item.BaseValue +
                    (simulated - (double)item.BaseValue) * _settings.PriceIntensity), 1, int.MaxValue);
                _lastError = "";
                return checked((int)value);
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_GetTrackedItemCount")]
    public static int GetTrackedItemCount()
    {
        lock (Sync)
        {
            try
            {
                RequireEngine();
                _lastError = "";
                return _trackedItems.Length;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_CopyTrackedItemKey")]
    public static int CopyTrackedItemKey(int index, byte* destination, int capacity)
    {
        lock (Sync)
        {
            try { return CopyUtf8(TrackedItem(index).FormKey, destination, capacity); }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_GetTrackedItemBaseValue")]
    public static int GetTrackedItemBaseValue(int index)
    {
        lock (Sync)
        {
            try
            {
                var value = checked((int)TrackedItem(index).BaseValue);
                _lastError = "";
                return value;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_GetTrackedItemKind")]
    public static int GetTrackedItemKind(int index)
    {
        lock (Sync)
        {
            try
            {
                var kind = TrackedItem(index).Type switch
                {
                    "MISC" => 1,
                    "ALCH" => 2,
                    "WEAP" => 3,
                    "ARMO" => 4,
                    _ => throw new InvalidDataException("Unsupported tracked item type.")
                };
                _lastError = "";
                return kind;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ApplyTrade")]
    public static int ApplyTrade(long sequence, byte* region, byte* formKey, int netSoldToMerchant)
    {
        lock (Sync)
        {
            try
            {
                var engine = RequireEngine();
                var regionId = ReadRequired(region);
                var itemId = ReadRequired(formKey);
                if (!engine.State.Regions.ContainsKey(regionId))
                    throw new ArgumentException("Unknown region.", nameof(region));
                var item = _catalog!.Find(itemId) ?? throw new ArgumentException("Unknown form key.", nameof(formKey));
                var applied = engine.ApplyTradeItem(sequence, regionId, item, netSoldToMerchant);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    // A game-side container event is already ordered when it reaches this call.
    // Allocate and apply its sequence under the same lock so save/load and event
    // callbacks cannot create gaps or apply a transaction twice.
    [UnmanagedCallersOnly(EntryPoint = "STR_ApplyTradeAuto")]
    public static long ApplyTradeAuto(byte* region, byte* formKey, int netSoldToMerchant)
    {
        lock (Sync)
        {
            try
            {
                var engine = RequireEngine();
                var regionId = ReadRequired(region);
                var itemId = ReadRequired(formKey);
                if (!engine.State.Regions.ContainsKey(regionId))
                    throw new ArgumentException("Unknown region.", nameof(region));
                var item = _catalog!.Find(itemId) ?? throw new ArgumentException("Unknown form key.", nameof(formKey));
                var sequence = checked(engine.State.LastTradeSequence + 1);
                engine.ApplyTradeItem(sequence, regionId, item, netSoldToMerchant);
                _lastError = "";
                return sequence;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ReportCaravanLoss")]
    public static int ReportCaravanLoss(long sequence, long shipmentId, int lostQuantity)
    {
        lock (Sync)
        {
            try
            {
                var applied = RequireEngine().ReportCaravanLoss(sequence, shipmentId, lostQuantity);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ReportCaravanArrival")]
    public static int ReportCaravanArrival(long sequence, long shipmentId)
    {
        lock (Sync)
        {
            try
            {
                var applied = RequireEngine().ReportCaravanArrival(sequence, shipmentId);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_CopyCaravanManifest")]
    public static int CopyCaravanManifest(byte* destination, int capacity)
    {
        lock (Sync)
        {
            try
            {
                var manifest = CaravanManifest.Create(RequireEngine());
                return CopyUtf8(JsonSerializer.Serialize(manifest, EconomyJson.Context.CaravanManifest), destination, capacity);
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_CopyStatus")]
    public static int CopyStatus(byte* destination, int capacity)
    {
        lock (Sync)
        {
            try
            {
                var status = EconomyStatus.Create(_config ?? throw new InvalidOperationException("Initialize first."),
                    RequireEngine());
                status.PriceIntensity = _settings.PriceIntensity;
                status.VisibleCaravansEnabled = _settings.VisibleCaravansEnabled;
                return CopyUtf8(JsonSerializer.Serialize(status, EconomyJson.Context.EconomyStatus), destination, capacity);
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_CopyVisibleCaravanPlan")]
    public static int CopyVisibleCaravanPlan(byte* region, byte* destination, int capacity)
    {
        lock (Sync)
        {
            try
            {
                var plan = RequireEngine().PlanVisibleCaravan(ReadRequired(region), _settings.VisibleCaravansEnabled);
                return CopyUtf8(plan is null ? "null" : JsonSerializer.Serialize(plan, EconomyJson.Context.VisibleCaravanPlan),
                    destination, capacity);
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_CopyPhysicalCaravanPlan")]
    public static int CopyPhysicalCaravanPlan(byte* region, byte* destination, int capacity)
    {
        lock (Sync)
        {
            try
            {
                var plan = _physicalRoutes.Plan(RequireEngine(), ReadRequired(region),
                    _settings.VisibleCaravansEnabled);
                return CopyUtf8(plan is null ? "null" :
                    JsonSerializer.Serialize(plan, EconomyJson.Context.PhysicalCaravanPlan), destination, capacity);
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ConfirmPhysicalCaravanSpawn")]
    public static int ConfirmPhysicalCaravanSpawn(long shipmentId, byte* region, uint leaderFormId)
    {
        lock (Sync)
        {
            try
            {
                if (!_settings.VisibleCaravansEnabled)
                    throw new InvalidOperationException("Visible caravans are disabled.");
                var applied = RequireEngine().ConfirmVisibleCaravanSpawn(shipmentId, ReadRequired(region),
                    leaderFormId, _physicalRoutes.EnabledRouteIds(), _physicalRoutes.SupportedCommodities());
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ConfirmPhysicalCaravanGroup")]
    public static int ConfirmPhysicalCaravanGroup(long shipmentId, byte* region, uint leaderFormId,
        uint packFormId, uint guardFormId)
    {
        lock (Sync)
        {
            try
            {
                if (!_settings.VisibleCaravansEnabled)
                    throw new InvalidOperationException("Visible caravans are disabled.");
                var applied = RequireEngine().ConfirmPhysicalCaravanGroup(shipmentId, ReadRequired(region),
                    leaderFormId, packFormId, guardFormId, _physicalRoutes.EnabledRouteIds(),
                    _physicalRoutes.SupportedCommodities());
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_UpdateVisibleWaypoint")]
    public static int UpdateVisibleWaypoint(long shipmentId, uint leaderFormId, int waypointIndex, int anchorCount)
    {
        lock (Sync)
        {
            try
            {
                var applied = RequireEngine().UpdateVisibleWaypoint(shipmentId, leaderFormId,
                    waypointIndex, anchorCount);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_GetPhysicalCaravanGroup")]
    public static int GetPhysicalCaravanGroup(long* shipmentId, uint* leaderFormId,
        uint* packFormId, uint* guardFormId)
    {
        lock (Sync)
        {
            try
            {
                if (shipmentId is null || leaderFormId is null || packFormId is null || guardFormId is null)
                    throw new ArgumentNullException(nameof(shipmentId));
                var binding = RequireEngine().State.VisibleCaravan;
                if (binding is null || binding.PackFormId == 0 || binding.GuardFormId == 0) return 0;
                *shipmentId = binding.ShipmentId;
                *leaderFormId = binding.LeaderFormId;
                *packFormId = binding.PackFormId;
                *guardFormId = binding.GuardFormId;
                _lastError = "";
                return 1;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_RebindPhysicalCaravanGroup")]
    public static int RebindPhysicalCaravanGroup(long shipmentId, uint oldLeaderFormId,
        uint leaderFormId, uint packFormId, uint guardFormId)
    {
        lock (Sync)
        {
            try
            {
                var applied = RequireEngine().RebindPhysicalCaravanGroup(shipmentId,
                    oldLeaderFormId, leaderFormId, packFormId, guardFormId);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ConfirmVisibleCaravanSpawn")]
    public static int ConfirmVisibleCaravanSpawn(long shipmentId, byte* region, uint leaderFormId)
    {
        lock (Sync)
        {
            try
            {
                if (!_settings.VisibleCaravansEnabled)
                    throw new InvalidOperationException("Visible caravans are disabled.");
                var applied = RequireEngine().ConfirmVisibleCaravanSpawn(shipmentId, ReadRequired(region), leaderFormId);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_ReleaseVisibleCaravan")]
    public static int ReleaseVisibleCaravan(long shipmentId, uint leaderFormId)
    {
        lock (Sync)
        {
            try
            {
                var applied = RequireEngine().ReleaseVisibleCaravan(shipmentId, leaderFormId);
                _lastError = "";
                return applied ? 1 : 0;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    // A zero-capacity call returns the required UTF-8 byte count. The caller owns
    // the buffer and should persist it in the matching SKSE co-save record.
    [UnmanagedCallersOnly(EntryPoint = "STR_CopyState")]
    public static int CopyState(byte* destination, int capacity)
    {
        lock (Sync)
        {
            try
            {
                if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
                var envelope = new NativeStateEnvelope
                {
                    CatalogFingerprint = _catalogFingerprint,
                    WorldDayOrigin = _worldDayOrigin,
                    State = RequireEngine().State
                };
                return CopyUtf8(JsonSerializer.Serialize(envelope, JsonContext.NativeStateEnvelope), destination, capacity);
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_LoadState")]
    public static int LoadState(byte* stateJson, int length)
    {
        lock (Sync)
        {
            try
            {
                if (stateJson == null || length <= 0 || length > 16 * 1024 * 1024)
                    throw new ArgumentOutOfRangeException(nameof(length));
                if (_config is null || _catalog is null) throw new InvalidOperationException("Initialize first.");
                var envelope = ReadEnvelope(new ReadOnlySpan<byte>(stateJson, length), _catalogFingerprint,
                    _compatiblePreviousCatalogFingerprint);
                _engine = new EconomyEngine(_config, envelope.State);
                _worldDayOrigin = envelope.WorldDayOrigin;
                _lastError = "";
                return _engine.State.Day;
            }
            catch (Exception ex) { return Fail(ex); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "STR_CopyLastError")]
    public static int CopyLastError(byte* destination, int capacity)
    {
        lock (Sync)
        {
            var bytes = Encoding.UTF8.GetBytes(_lastError);
            if (destination != null && capacity > 0)
            {
                var count = Math.Min(bytes.Length, capacity - 1);
                bytes.AsSpan(0, count).CopyTo(new Span<byte>(destination, capacity));
                destination[count] = 0;
            }
            return bytes.Length;
        }
    }

    private static EconomyEngine RequireEngine() => _engine ?? throw new InvalidOperationException("Economy is not initialized.");
    private static CatalogItem TrackedItem(int index)
    {
        RequireEngine();
        if (index < 0 || index >= _trackedItems.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return _trackedItems[index];
    }
    private static int CopyUtf8(string json, byte* destination, int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        var bytes = Encoding.UTF8.GetBytes(json);
        if (destination != null && capacity >= bytes.Length)
            bytes.CopyTo(new Span<byte>(destination, capacity));
        _lastError = "";
        return bytes.Length;
    }
    private static bool CanMigrateCatalog(ItemCatalog previous, ItemCatalog current)
    {
        foreach (var oldItem in previous.Records.Where(item => item.Commodity is not null))
        {
            var newItem = current.Find(oldItem.FormKey);
            if (newItem is null ||
                !string.Equals(oldItem.Commodity, newItem.Commodity, StringComparison.OrdinalIgnoreCase) ||
                oldItem.Units != newItem.Units || oldItem.BaseValue != newItem.BaseValue ||
                !string.Equals(oldItem.Type, newItem.Type, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(oldItem.WinningPlugin, newItem.WinningPlugin, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
    private static NativeStateEnvelope ReadEnvelope(ReadOnlySpan<byte> bytes, string catalogFingerprint,
        string compatiblePreviousFingerprint)
    {
        var envelope = JsonSerializer.Deserialize(bytes, JsonContext.NativeStateEnvelope)
            ?? throw new InvalidDataException("State is empty.");
        if (envelope.SchemaVersion != 1 ||
            (!string.Equals(envelope.CatalogFingerprint, catalogFingerprint, StringComparison.OrdinalIgnoreCase) &&
             (compatiblePreviousFingerprint.Length == 0 ||
              !string.Equals(envelope.CatalogFingerprint, compatiblePreviousFingerprint, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("State catalog differs from current catalog.");
        if (envelope.State is null) throw new InvalidDataException("State body is empty.");
        return envelope;
    }
    private static string ReadRequired(byte* value)
    {
        var result = ReadOptional(value);
        if (result.Length == 0) throw new ArgumentException("Required UTF-8 string is empty.");
        return result;
    }

    private static string ReadOptional(byte* value) => value == null ? "" : Marshal.PtrToStringUTF8((nint)value) ?? "";
    private static int Fail(Exception ex)
    {
        _lastError = $"{ex.GetType().Name}: {ex.Message}";
        return -1;
    }
}
