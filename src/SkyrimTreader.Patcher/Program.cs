using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

return await SynthesisPipeline.Instance
    .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
    .SetTypicalOpen(GameRelease.SkyrimSE, "STRCatalogPatch.esp")
    .Run(args);

static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
{
    var rulesPath = Path.Combine(AppContext.BaseDirectory, "item-rules.json");
    var rules = JsonSerializer.Deserialize<Dictionary<string, CommodityRule>>(
        File.ReadAllText(rulesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Item rules are empty.");

    var foodList = state.PatchMod.FormLists.AddNew();
    foodList.EditorID = "STR_FoodItems";
    var ironList = state.PatchMod.FormLists.AddNew();
    ironList.EditorID = "STR_IronItems";
    var counts = new Dictionary<string, int> { ["food"] = 0, ["iron"] = 0 };
    foreach (var item in state.LoadOrder.PriorityOrder.WinningOverrides<IItemGetter>())
    {
        if (!rules.TryGetValue(item.FormKey.ToString(), out var rule)) continue;
        if (rule.Units <= 0) throw new InvalidDataException($"Invalid units for {item.FormKey}.");
        var list = rule.Commodity switch
        {
            "food" => foodList,
            "iron" => ironList,
            _ => throw new InvalidDataException($"Unknown commodity {rule.Commodity}.")
        };
        list.Items.Add(item.ToLink<IItemGetter>());
        counts[rule.Commodity]++;
    }
    Console.WriteLine($"STR catalogue: food={counts["food"]}, iron={counts["iron"]}.");
}

internal sealed record CommodityRule(string Commodity, int Units);
