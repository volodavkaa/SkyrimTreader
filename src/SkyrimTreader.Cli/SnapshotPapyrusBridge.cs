using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkyrimTreader.Simulation;

internal static class SnapshotPapyrusBridge
{
    private static readonly Regex FormKeyPattern = new("^([0-9A-Fa-f]{6}):([A-Za-z0-9_.-]+)$", RegexOptions.Compiled);
    private static readonly string[] RegionIds = ["markarth", "whiterun", "riften"];

    public static int Export(string[] arguments)
    {
        if (arguments.Length != 5)
        {
            Console.Error.WriteLine("Usage: SkyrimTreader.Cli export-papyrus-bridge <config.json> <catalog.json> <snapshot.json> <STR_SnapshotPriceBridge.psc>");
            return 2;
        }
        try
        {
            var config = EconomyJson.LoadConfig(arguments[1]);
            var catalog = ItemCatalog.Load(arguments[2], config);
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(arguments[2])));
            var snapshot = PriceSnapshot.Load(arguments[3]);
            snapshot.Validate(config, catalog, fingerprint);
            if (config.Regions.Count != RegionIds.Length || RegionIds.Any(region => !config.Regions.ContainsKey(region)))
                throw new InvalidDataException("Papyrus bridge currently supports exactly Markarth, Riften and Whiterun.");

            var items = snapshot.Regions["markarth"].Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (items.Count is < 1 or > 64 || RegionIds.Any(region => !items.SequenceEqual(
                    snapshot.Regions[region].Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Papyrus bridge requires 1..64 identical item keys across the three regions.");
            var script = Build(snapshot, items);
            var output = Path.GetFullPath(arguments[4]);
            if (!string.Equals(Path.GetFileName(output), "STR_SnapshotPriceBridge.psc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output script must be named STR_SnapshotPriceBridge.psc.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, script, new UTF8Encoding(false));
            Console.WriteLine($"Exported day {snapshot.Day}: {items.Count} forms for 3 regions to {output}.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or OverflowException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string Build(PriceSnapshot snapshot, List<string> items)
    {
        var script = new StringBuilder();
        void Line(string value = "") => script.AppendLine(value);
        Line("Scriptname STR_SnapshotPriceBridge extends Quest");
        Line("; Generated from a verified C# price snapshot. Changes global form values while active.");
        Line("Form[] Items");
        Line("Int[] OriginalValues");
        Line("Int[] AppliedValues");
        Line("Bool[] ItemEnabled");
        Line("Int[] MarkarthValues");
        Line("Int[] WhiterunValues");
        Line("Int[] RiftenValues");
        Line("Location MarkarthCity");
        Line("Location WhiterunCity");
        Line("Location RiftenCity");
        Line("Bool Active = False");
        Line("Bool BarterOpen = False");
        Line("Int LastRegion = -1");
        Line();
        Line("Event OnInit()");
        Line("    If Active");
        Line("        Debug.Trace(\"STR bridge: duplicate initialization ignored\")");
        Line("        Return");
        Line("    EndIf");
        Line("    MarkarthCity = Game.GetFormFromFile(0x00018A59, \"Skyrim.esm\") as Location");
        Line("    WhiterunCity = Game.GetFormFromFile(0x00018A56, \"Skyrim.esm\") as Location");
        Line("    RiftenCity = Game.GetFormFromFile(0x00018A58, \"Skyrim.esm\") as Location");
        Line("    If MarkarthCity == None || WhiterunCity == None || RiftenCity == None");
        Line("        Debug.Trace(\"STR bridge: missing city location\")");
        Line("        Stop()");
        Line("        Return");
        Line("    EndIf");
        foreach (var name in new[] { "Items", "OriginalValues", "AppliedValues", "ItemEnabled", "MarkarthValues", "WhiterunValues", "RiftenValues" })
            Line($"    {name} = new {(name == "Items" ? "Form" : name == "ItemEnabled" ? "Bool" : "Int")}[{items.Count}]");
        for (var i = 0; i < items.Count; i++)
        {
            var key = items[i];
            var match = FormKeyPattern.Match(key);
            if (!match.Success) throw new InvalidDataException($"Unsupported FormKey in snapshot: {key}.");
            var plugin = match.Groups[2].Value;
            if (!snapshot.LoadOrder.Contains(plugin, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Plugin {plugin} is absent from snapshot load order.");
            int Value(string region) => checked((int)snapshot.Regions[region][key].Value);
            var original = checked((int)snapshot.Regions["markarth"][key].BaseValue);
            Line($"    Items[{i}] = Game.GetFormFromFile(0x00{match.Groups[1].Value.ToUpperInvariant()}, \"{plugin}\")");
            Line($"    OriginalValues[{i}] = {original.ToString(CultureInfo.InvariantCulture)}");
            Line($"    MarkarthValues[{i}] = {Value("markarth").ToString(CultureInfo.InvariantCulture)}");
            Line($"    WhiterunValues[{i}] = {Value("whiterun").ToString(CultureInfo.InvariantCulture)}");
            Line($"    RiftenValues[{i}] = {Value("riften").ToString(CultureInfo.InvariantCulture)}");
        }
        Line("    Int i = 0");
        Line("    Int enabledCount = 0");
        Line("    Int initialRegion = 0");
        Line("    Actor player = Game.GetPlayer()");
        Line("    If player.IsInLocation(MarkarthCity)");
        Line("        initialRegion = 1");
        Line("    ElseIf player.IsInLocation(WhiterunCity)");
        Line("        initialRegion = 2");
        Line("    ElseIf player.IsInLocation(RiftenCity)");
        Line("        initialRegion = 3");
        Line("    EndIf");
        Line("    Debug.Trace(\"STR bridge: init region=\" + initialRegion)");
        Line("    While i < Items.Length");
        Line("        Int liveValue = -1");
        Line("        If Items[i] != None");
        Line("            liveValue = Items[i].GetGoldValue()");
        Line("        EndIf");
        Line("        Bool alreadyApplied = (initialRegion == 1 && liveValue == MarkarthValues[i]) || (initialRegion == 2 && liveValue == WhiterunValues[i]) || (initialRegion == 3 && liveValue == RiftenValues[i])");
        Line("        If liveValue == OriginalValues[i] || alreadyApplied");
        Line("            ItemEnabled[i] = True");
        Line("            AppliedValues[i] = liveValue");
        Line("            If alreadyApplied && liveValue != OriginalValues[i]");
        Line("                Debug.Trace(\"STR bridge: resumed item \" + i + \" value=\" + liveValue)");
        Line("            EndIf");
        Line("            enabledCount = enabledCount + 1");
        Line("        Else");
        Line("            Debug.Trace(\"STR bridge: skipped item \" + i + \" live=\" + liveValue + \" expected=\" + OriginalValues[i])");
        Line("            Debug.Notification(\"STR skipped item \" + i + \" (\" + liveValue + \"/\" + OriginalValues[i] + \")\")");
        Line("        EndIf");
        Line("        i = i + 1");
        Line("    EndWhile");
        Line("    If enabledCount == 0");
        Line("        Debug.Notification(\"STR prices paused: no matching items\")");
        Line("        Stop()");
        Line("        Return");
        Line("    EndIf");
        Line("    Active = True");
        Line("    RegisterForMenu(\"BarterMenu\")");
        Line("    Debug.Notification(\"STR snapshot prices active\")");
        Line("    ApplyCurrentRegion()");
        Line("    RegisterForSingleUpdate(1.0)");
        Line("EndEvent");
        Line();
        Line("Event OnUpdate()");
        Line("    If !Active");
        Line("        Return");
        Line("    EndIf");
        Line("    If !BarterOpen");
        Line("        ApplyCurrentRegion()");
        Line("    EndIf");
        Line("    If Active");
        Line("        RegisterForSingleUpdate(1.0)");
        Line("    EndIf");
        Line("EndEvent");
        Line();
        Line("Event OnMenuOpen(String menuName)");
        Line("    If menuName == \"BarterMenu\"");
        Line("        BarterOpen = True");
        Line("    EndIf");
        Line("EndEvent");
        Line();
        Line("Event OnMenuClose(String menuName)");
        Line("    If menuName == \"BarterMenu\"");
        Line("        BarterOpen = False");
        Line("        If Active");
        Line("            ApplyCurrentRegion()");
        Line("        EndIf");
        Line("    EndIf");
        Line("EndEvent");
        Line();
        Line("Function ApplyCurrentRegion()");
        Line("    Int region = 0");
        Line("    Actor player = Game.GetPlayer()");
        Line("    If player.IsInLocation(MarkarthCity)");
        Line("        region = 1");
        Line("    ElseIf player.IsInLocation(WhiterunCity)");
        Line("        region = 2");
        Line("    ElseIf player.IsInLocation(RiftenCity)");
        Line("        region = 3");
        Line("    EndIf");
        Line("    If region != LastRegion");
        Line("        Debug.Trace(\"STR bridge: region=\" + region + \" cell=\" + player.GetParentCell())");
        Line("        LastRegion = region");
        Line("    EndIf");
        Line("    Int i = 0");
        Line("    Bool anyEnabled = False");
        Line("    While i < Items.Length");
        Line("      If ItemEnabled[i]");
        Line("        Int desired = OriginalValues[i]");
        Line("        If region == 1");
        Line("            desired = MarkarthValues[i]");
        Line("        ElseIf region == 2");
        Line("            desired = WhiterunValues[i]");
        Line("        ElseIf region == 3");
        Line("            desired = RiftenValues[i]");
        Line("        EndIf");
        Line("        Int current = Items[i].GetGoldValue()");
        Line("        If current != AppliedValues[i] && current != OriginalValues[i]");
        Line("            Debug.Trace(\"STR bridge: external value change for item \" + i)");
        Line("            ItemEnabled[i] = False");
        Line("        Else");
        Line("            If current != desired");
        Line("                Items[i].SetGoldValue(desired)");
        Line("                Debug.Trace(\"STR bridge: item \" + i + \" value \" + current + \" -> \" + desired)");
        Line("            EndIf");
        Line("            AppliedValues[i] = desired");
        Line("            anyEnabled = True");
        Line("        EndIf");
        Line("      EndIf");
        Line("        i = i + 1");
        Line("    EndWhile");
        Line("    If !anyEnabled");
        Line("        Active = False");
        Line("        Stop()");
        Line("    EndIf");
        Line("EndFunction");
        return script.ToString();
    }
}
