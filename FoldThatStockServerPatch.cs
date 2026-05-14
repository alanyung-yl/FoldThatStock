using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using TemplateItem = SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem;

namespace FoldThatStock.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.foldthatstock.server";
    public override string Name { get; init; } = "FoldThatStock";
    public override string Author { get; init; } = "alanyung-yl";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("0.3.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

public sealed class FoldThatStockServerConfig
{
    public bool Enabled { get; set; } = true;
    public List<WeaponFoldPatch> WeaponPatches { get; set; } = new();
    public List<StockTemplatePatch> StockPatches { get; set; } = new();
}

public sealed class WeaponFoldPatch
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string WeaponTemplateId { get; set; } = "";
    public bool? Foldable { get; set; } = true;
    public string? FoldedSlot { get; set; } = "mod_stock";
    public List<StockTemplatePatch> StockPatches { get; set; } = new();
}

public sealed class StockTemplatePatch
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string StockTemplateId { get; set; } = "";
    public int? SizeReduceRight { get; set; } = 1;
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class FoldThatStockServerPatch(
    ISptLogger<FoldThatStockServerPatch> logger,
    DatabaseService databaseService
) : IOnLoad
{
    private const string LogPrefix = "FoldThatStock:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public Task OnLoad()
    {
        var config = LoadOrCreateConfig();
        if (!config.Enabled)
        {
            logger.Info($"{LogPrefix} Server template patch is disabled in config.");
            return Task.CompletedTask;
        }

        var items = databaseService.GetItems();
        var patchedWeapons = 0;
        var patchedStocks = 0;

        foreach (var weaponPatch in config.WeaponPatches.Where(patch => patch != null && patch.Enabled))
        {
            if (ApplyWeaponPatch(items, weaponPatch))
            {
                patchedWeapons++;
            }
        }

        foreach (var stockPatch in GetEnabledStockPatches(config))
        {
            if (ApplyStockPatch(items, stockPatch))
            {
                patchedStocks++;
            }
        }

        logger.Success($"{LogPrefix} Applied {patchedWeapons} weapon patch(es) and {patchedStocks} stock patch(es).");
        return Task.CompletedTask;
    }

    private bool ApplyWeaponPatch(Dictionary<MongoId, TemplateItem> items, WeaponFoldPatch patch)
    {
        if (!TryGetTemplate(items, patch.WeaponTemplateId, patch.Name, out var template))
        {
            return false;
        }

        if (template.Properties == null)
        {
            logger.Warning($"{LogPrefix} Weapon `{GetPatchLabel(patch.Name, patch.WeaponTemplateId)}` has no properties object.");
            return false;
        }

        var changed = false;
        if (patch.Foldable.HasValue)
        {
            changed |= TrySetTemplateProperty(template.Properties, "Foldable", patch.Foldable.Value, patch.Name, patch.WeaponTemplateId);
        }

        if (patch.FoldedSlot != null)
        {
            changed |= TrySetTemplateProperty(template.Properties, "FoldedSlot", patch.FoldedSlot, patch.Name, patch.WeaponTemplateId);
        }

        if (changed)
        {
            logger.Info(
                $"{LogPrefix} Patched weapon `{GetPatchLabel(patch.Name, patch.WeaponTemplateId)}` " +
                $"Foldable={patch.Foldable?.ToString() ?? "<unchanged>"} FoldedSlot={patch.FoldedSlot ?? "<unchanged>"}."
            );
        }

        return changed;
    }

    private bool ApplyStockPatch(Dictionary<MongoId, TemplateItem> items, StockTemplatePatch patch)
    {
        if (!TryGetTemplate(items, patch.StockTemplateId, patch.Name, out var template))
        {
            return false;
        }

        if (template.Properties == null)
        {
            logger.Warning($"{LogPrefix} Stock `{GetPatchLabel(patch.Name, patch.StockTemplateId)}` has no properties object.");
            return false;
        }

        var changed = false;
        if (patch.SizeReduceRight.HasValue)
        {
            changed |= TrySetTemplateProperty(template.Properties, "SizeReduceRight", patch.SizeReduceRight.Value, patch.Name, patch.StockTemplateId);
        }

        if (changed)
        {
            logger.Info(
                $"{LogPrefix} Patched stock `{GetPatchLabel(patch.Name, patch.StockTemplateId)}` " +
                $"SizeReduceRight={patch.SizeReduceRight?.ToString(CultureInfo.InvariantCulture) ?? "<unchanged>"}."
            );
        }

        return changed;
    }

    private bool TryGetTemplate(Dictionary<MongoId, TemplateItem> items, string templateId, string label, out TemplateItem template)
    {
        template = null!;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            logger.Warning($"{LogPrefix} Empty template id in config entry `{label}`.");
            return false;
        }

        if (!TryParseMongoId(templateId.Trim(), out var parsedId))
        {
            logger.Warning($"{LogPrefix} Invalid template id `{templateId}` in config entry `{label}`.");
            return false;
        }

        if (!items.TryGetValue(parsedId, out template!))
        {
            logger.Warning($"{LogPrefix} Template `{GetPatchLabel(label, templateId)}` was not found in the item database.");
            return false;
        }

        return true;
    }

    private bool TrySetTemplateProperty(object properties, string propertyName, object value, string label, string templateId)
    {
        var property = properties
            .GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

        if (property == null || !property.CanWrite)
        {
            logger.Warning($"{LogPrefix} Property `{propertyName}` was not found or is not writable on `{GetPatchLabel(label, templateId)}`.");
            return false;
        }

        try
        {
            property.SetValue(properties, ConvertValue(value, property.PropertyType));
            return true;
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"{LogPrefix} Failed setting `{propertyName}` on `{GetPatchLabel(label, templateId)}` to `{value}`: {exception.Message}"
            );
            return false;
        }
    }

    private FoldThatStockServerConfig LoadOrCreateConfig()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            var defaultConfig = CreateDefaultConfig();
            SaveConfig(configPath, defaultConfig);
            logger.Info($"{LogPrefix} Created default config at `{configPath}`.");
            return defaultConfig;
        }

        try
        {
            var configContent = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<FoldThatStockServerConfig>(configContent, JsonOptions) ?? CreateDefaultConfig();
            NormalizeConfig(config);
            return config;
        }
        catch (Exception exception)
        {
            logger.Error($"{LogPrefix} Failed reading config at `{configPath}`. Using defaults. Error: {exception.Message}");
            return CreateDefaultConfig();
        }
    }

    private static FoldThatStockServerConfig CreateDefaultConfig()
    {
        return new FoldThatStockServerConfig
        {
            Enabled = true,
            StockPatches = new List<StockTemplatePatch>
            {
                new()
                {
                    Name = "SIG Sauer Thin Side-Folding Stock",
                    StockTemplateId = "5fbcc437d724d907e2077d5c",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SIG folding knuckle",
                    StockTemplateId = "58ac1bf086f77420ed183f9f",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "PMM ULSS stock",
                    StockTemplateId = "5c5db6f82e2216003a0fe914",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SIG telescoping stock",
                    StockTemplateId = "5fbcc429900b1d5091531dd7",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SIG stock locking hinge assembly",
                    StockTemplateId = "6529348224cbe3c74a05e5c4",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "UTG SFS AK adapter",
                    StockTemplateId = "5649b2314bdc2d79388b4576",
                    SizeReduceRight = 1,
                },
            },
            WeaponPatches = new List<WeaponFoldPatch>
            {
                new()
                {
                    Name = "SIG MCX .300 Blackout with supported folding stock",
                    WeaponTemplateId = "5fbcc1d9016cce60e8341ab3",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "SIG MPX 9x19 with supported folding stock",
                    WeaponTemplateId = "58948c8e86f77409493f7266",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "AK-74N 5.45x39 with ME4 buffer tube adapter",
                    WeaponTemplateId = "5644bd2b4bdc2d3b4c8b4572",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "AK-74 5.45x39 with ME4 buffer tube adapter",
                    WeaponTemplateId = "5bf3e03b0db834001d2c4a9c",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "AKM 7.62x39 with ME4 buffer tube adapter",
                    WeaponTemplateId = "59d6088586f774275f37482f",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "AKMN 7.62x39 with ME4 buffer tube adapter",
                    WeaponTemplateId = "5a0ec13bfcdbcb00165aa685",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "VPO-136 Vepr-KM 7.62x39 with ME4 buffer tube adapter",
                    WeaponTemplateId = "59e6152586f77473dc057aa1",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "VPO-209 .366 TKM with ME4 buffer tube adapter",
                    WeaponTemplateId = "59e6687d86f77411d949b251",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "RD-704 7.62x39 with ME4 buffer tube adapter",
                    WeaponTemplateId = "628a60ae6b1d481ff772e9c8",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
            },
        };
    }

    private static void NormalizeConfig(FoldThatStockServerConfig config)
    {
        config.WeaponPatches ??= new List<WeaponFoldPatch>();
        config.StockPatches ??= new List<StockTemplatePatch>();
        foreach (var weaponPatch in config.WeaponPatches.Where(patch => patch != null))
        {
            weaponPatch.StockPatches ??= new List<StockTemplatePatch>();
        }
    }

    private static IEnumerable<StockTemplatePatch> GetEnabledStockPatches(FoldThatStockServerConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stockPatch in config.StockPatches.Where(patch => patch != null && patch.Enabled))
        {
            if (TryMarkStockPatchSeen(seen, stockPatch))
            {
                yield return stockPatch;
            }
        }

        foreach (var stockPatch in config.WeaponPatches
            .Where(weaponPatch => weaponPatch != null && weaponPatch.Enabled)
            .SelectMany(weaponPatch => weaponPatch.StockPatches ?? new List<StockTemplatePatch>())
            .Where(patch => patch != null && patch.Enabled))
        {
            if (TryMarkStockPatchSeen(seen, stockPatch))
            {
                yield return stockPatch;
            }
        }
    }

    private static bool TryMarkStockPatchSeen(HashSet<string> seen, StockTemplatePatch patch)
    {
        var key = !string.IsNullOrWhiteSpace(patch.StockTemplateId)
            ? patch.StockTemplateId.Trim()
            : patch.Name.Trim();

        return key.Length == 0 || seen.Add(key);
    }

    private static void SaveConfig(string path, FoldThatStockServerConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static object? ConvertValue(object value, Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType == typeof(string))
        {
            return value.ToString();
        }

        if (targetType == typeof(MongoId) && value is string mongoIdText)
        {
            return (MongoId)mongoIdText;
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value.ToString() ?? string.Empty, true);
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static bool TryParseMongoId(string value, out MongoId id)
    {
        try
        {
            id = value;
            return true;
        }
        catch
        {
            id = MongoId.Empty();
            return false;
        }
    }

    private static string GetPatchLabel(string label, string templateId)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return templateId;
        }

        return $"{label} ({templateId})";
    }

    private static string GetConfigPath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var modDirectory = Path.GetDirectoryName(assemblyPath);

        if (string.IsNullOrWhiteSpace(modDirectory))
        {
            return Path.Combine(AppContext.BaseDirectory, "config.json");
        }

        return Path.Combine(modDirectory, "config.json");
    }
}
