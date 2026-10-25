namespace SkyrimTreader.Catalog;

internal static class Mo2Profile
{
    public static List<PluginSource> Resolve(string dataFolder, string profileFolder)
    {
        var loadOrderPath = Path.Combine(profileFolder, "loadorder.txt");
        var pluginsPath = Path.Combine(profileFolder, "plugins.txt");
        var modListPath = Path.Combine(profileFolder, "modlist.txt");
        var modsFolder = Path.GetFullPath(Path.Combine(profileFolder, "..", "..", "mods"));

        var loadOrder = ReadEntries(loadOrderPath).ToList();
        if (loadOrder.Count == 0 || loadOrder.Count != loadOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidDataException("MO2 loadorder.txt is empty or contains duplicates.");

        var pluginSettings = ReadEntries(pluginsPath).ToList();
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitlyEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in pluginSettings)
        {
            var enabled = setting.StartsWith('*');
            var name = enabled ? setting[1..] : setting;
            ValidatePluginName(name);
            if (!disabled.Add(name)) throw new InvalidDataException($"Duplicate MO2 plugin setting: {name}");
            if (enabled) explicitlyEnabled.Add(name);
        }
        if (explicitlyEnabled.Any(x => !loadOrder.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("An enabled MO2 plugin is missing from loadorder.txt.");
        disabled.ExceptWith(explicitlyEnabled);

        var enabledMods = ReadEntries(modListPath)
            .Where(x => x.StartsWith('+'))
            .Select(x => x[1..])
            .ToList();
        foreach (var mod in enabledMods)
            if (mod != Path.GetFileName(mod) || mod is "." or "..")
                throw new InvalidDataException($"Invalid MO2 mod name: {mod}");
        var sources = new List<PluginSource>();
        foreach (var name in loadOrder.Where(x => !disabled.Contains(x)))
        {
            ValidatePluginName(name);
            // MO2 writes modlist.txt in reverse priority order: the first enabled provider wins.
            var provider = enabledMods
                .Select(mod => Path.Combine(modsFolder, mod, name))
                .Where(File.Exists)
                .FirstOrDefault();
            var path = provider ?? Path.Combine(dataFolder, name);
            if (!File.Exists(path)) throw new FileNotFoundException($"Active MO2 plugin not found: {name}", path);
            sources.Add(new PluginSource(name, path));
        }
        return sources;
    }

    private static IEnumerable<string> ReadEntries(string path) => File.ReadLines(path)
        .Select(x => x.Trim().TrimStart('\uFEFF'))
        .Where(x => x.Length > 0 && !x.StartsWith('#'));

    private static void ValidatePluginName(string name)
    {
        if (name != Path.GetFileName(name) ||
            !new[] { ".esm", ".esp", ".esl" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Invalid plugin name: {name}");
    }
}

internal sealed record PluginSource(string Name, string Path);
