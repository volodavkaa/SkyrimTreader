using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SkyrimTreader.ProbePlugin <STRPriceProbe.esp|STRRegionalProbe.esp|STRSnapshotBridge.esp>");
    return 2;
}

var output = Path.GetFullPath(args[0]);
var fileName = Path.GetFileName(output);
if (!string.Equals(fileName, "STRPriceProbe.esp", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(fileName, "STRRegionalProbe.esp", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(fileName, "STRSnapshotBridge.esp", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Output file must be named STRPriceProbe.esp, STRRegionalProbe.esp or STRSnapshotBridge.esp.");
    return 2;
}

var regional = string.Equals(fileName, "STRRegionalProbe.esp", StringComparison.OrdinalIgnoreCase);
var snapshotBridge = string.Equals(fileName, "STRSnapshotBridge.esp", StringComparison.OrdinalIgnoreCase);
var questId = snapshotBridge ? "STRSnapshotBridgeQuest" : regional ? "STRRegionalProbeQuest" : "STRPriceProbeQuest";
var scriptName = snapshotBridge ? "STR_SnapshotPriceBridge" : regional ? "STR_RegionalPriceProbe" : "STR_PriceProbe";
var mod = new SkyrimMod(fileName, SkyrimRelease.SkyrimSE);
var quest = mod.Quests.AddNew();
quest.EditorID = questId;
quest.Priority = 0;
quest.VirtualMachineAdapter = new QuestAdapter
{
    Version = 5,
    ObjectFormat = 2,
    ExtraBindDataVersion = 1
};
quest.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = scriptName });

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
mod.BeginWrite.ToPath(output).WithLoadOrder(Array.Empty<ISkyrimModGetter>()).Write();
using (var verify = SkyrimMod.CreateFromBinaryOverlay(output, SkyrimRelease.SkyrimSE))
{
    var writtenQuest = verify.Quests.SingleOrDefault();
    if (writtenQuest?.EditorID != questId ||
        writtenQuest.VirtualMachineAdapter?.Scripts.SingleOrDefault()?.Name != scriptName)
        throw new InvalidDataException("Quest script did not survive plugin export.");
}
Console.WriteLine($"Wrote {output}; quest {quest.EditorID} ({quest.FormKey}).");
return 0;
