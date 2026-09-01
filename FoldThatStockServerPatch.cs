using System.Collections;
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
    public override string ModGuid { get; init; } = "com.foldthatstock";
    public override string Name { get; init; } = "FoldThatStock";
    public override string Author { get; init; } = "alanyung-yl";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "GPL-3.0-only";
}

public sealed class FoldThatStockServerConfig
{
    public bool Enabled { get; set; } = true;
    public UziAdapterFoldSuppressionConfig? UziAdapterFoldSuppression { get; set; }
    public List<WeaponFoldPatch> WeaponPatches { get; set; } = new();
    public List<StockTemplatePatch> StockPatches { get; set; } = new();
}

public sealed class UziAdapterFoldSuppressionConfig
{
    public bool SuppressLeftFoldingStocks { get; set; } = true;
    public bool SuppressCollapsingStocks { get; set; } = true;
}

public sealed class WeaponFoldPatch
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string WeaponTemplateId { get; set; } = "";
    public bool? Foldable { get; set; } = true;
    public string? FoldedSlot { get; set; } = "mod_stock";
    public int? SizeReduceRight { get; set; }
    public List<string> AdditionalCompatibleStockTemplateIds { get; set; } = new();
    public List<StockTemplatePatch> StockPatches { get; set; } = new();
}

public sealed class StockTemplatePatch
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string StockTemplateId { get; set; } = "";
    public int? SizeReduceRight { get; set; } = 1;
    public bool? BlocksFolding { get; set; }
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class FoldThatStockServerPatch(
    ISptLogger<FoldThatStockServerPatch> logger,
    DatabaseService databaseService
) : IOnLoad
{
    private const string LogPrefix = "FoldThatStock:";
    private const string DefaultFoldedSlot = "mod_stock";
    private const string Mp5WeaponTemplateId = "5926bb2186f7744b1c6c6e60";
    private const string AxmcWeaponTemplateId = "627e14b21713922ded6f2c15";
    private const string UziProA3BraceTemplateId = "6686717ffb75ee4a5e02eb19";

    private static readonly HashSet<string> WeaponLevelSizeReductionTemplateIds = new(StringComparer.OrdinalIgnoreCase)
    {
        Mp5WeaponTemplateId,
        AxmcWeaponTemplateId,
    };

    private static readonly string[] BuiltInSupportedStockTemplateIds =
    {
        "5fbcc437d724d907e2077d5c",
        "58ac1bf086f77420ed183f9f",
        "5c5db6f82e2216003a0fe914",
        "5fbcc429900b1d5091531dd7",
        "5894a13e86f7742405482982",
        "6761496fe2cf1419500357e9",
        "6529348224cbe3c74a05e5c4",
        "5649b2314bdc2d79388b4576",
        "5b0e794b5acfc47a877359b2",
        "5926d40686f7740f152b6b7e",
        "5d25d0ac8abbc3054f3e61f7",
        "5cdeac22d7f00c000f26168f", // M700 Pro 700 chassis hosting the folding stock
        "5cdeac42d7f00c000d36ba73",
        "5b7d64555acfc4001876c8e2",
        "5b7d63cf5acfc4001876c8df",
        "5b7d63de5acfc400170e2f8d",
        "5b099bf25acfc4001637e683",
        "5fb655b748c711690e3a8d5a",
        "5b04473a5acfc40018632f70",
        "5d0236dad7ad1a0940739d29",
        "653ed132896b99b40a0292e6",
        "6686717ffb75ee4a5e02eb19",
        "668032ba74b8f2050c0b917d",
        "66867310f3734a938b077f79",
        "668672b8c99550c6fd0f0b29",
        "669cf78806768ff39504fc1c",
    };

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
        var patchedFoldBlocks = 0;

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

        patchedFoldBlocks = ApplyFoldCompatibilityPatches(items, config);

        logger.Success(
            $"{LogPrefix} Applied {patchedWeapons} weapon patch(es), {patchedStocks} stock patch(es), " +
            $"and {patchedFoldBlocks} fold compatibility patch(es)."
        );
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

        // MP5's stock slot is receiver-owned and AXMC's stock is integral to the weapon.
        // Both therefore need their folded inventory reduction on the weapon root.
        if (patch.SizeReduceRight.HasValue
            && WeaponLevelSizeReductionTemplateIds.Contains(patch.WeaponTemplateId))
        {
            changed |= TrySetTemplateProperty(template.Properties, "SizeReduceRight", patch.SizeReduceRight.Value, patch.Name, patch.WeaponTemplateId);
        }

        changed |= AddCompatibleStocksToFoldedSlot(template, patch);

        return changed;
    }

    private bool AddCompatibleStocksToFoldedSlot(TemplateItem weaponTemplate, WeaponFoldPatch patch)
    {
        if (weaponTemplate.Properties == null
            || patch.AdditionalCompatibleStockTemplateIds == null
            || patch.AdditionalCompatibleStockTemplateIds.Count == 0)
        {
            return false;
        }

        var foldedSlot = GetConfiguredFoldedSlot(weaponTemplate, patch);
        var slot = weaponTemplate.Properties.Slots?.FirstOrDefault(candidate => string.Equals(
            candidate.Name,
            foldedSlot,
            StringComparison.OrdinalIgnoreCase));
        if (slot == null)
        {
            logger.Warning(
                $"{LogPrefix} Folded slot `{foldedSlot}` was not found on " +
                $"`{GetPatchLabel(patch.Name, patch.WeaponTemplateId)}` while adding stock compatibility."
            );
            return false;
        }

        var filters = slot.Properties?.Filters;
        if (filters == null)
        {
            logger.Warning(
                $"{LogPrefix} Folded slot `{foldedSlot}` has no compatibility filter on " +
                $"`{GetPatchLabel(patch.Name, patch.WeaponTemplateId)}`."
            );
            return false;
        }

        var changed = false;
        foreach (var stockTemplateId in patch.AdditionalCompatibleStockTemplateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseMongoId(stockTemplateId, out var parsedId))
            {
                logger.Warning(
                    $"{LogPrefix} Invalid compatible stock template id `{stockTemplateId}` on " +
                    $"`{GetPatchLabel(patch.Name, patch.WeaponTemplateId)}`."
                );
                continue;
            }

            foreach (var filter in filters)
            {
                if (filter.Filter != null)
                {
                    changed |= filter.Filter.Add(parsedId);
                }
            }
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

        if (patch.BlocksFolding.HasValue)
        {
            changed |= TrySetTemplateProperty(template.Properties, "BlocksFolding", patch.BlocksFolding.Value, patch.Name, patch.StockTemplateId);
        }

        return changed;
    }

    private int ApplyFoldCompatibilityPatches(Dictionary<MongoId, TemplateItem> items, FoldThatStockServerConfig config)
    {
        var supportedStockTemplateIds = GetSupportedStockTemplateIds(config);
        var processedStockTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patchedFoldBlocks = 0;

        foreach (var weaponPatch in config.WeaponPatches.Where(patch => patch != null && patch.Enabled))
        {
            if (!TryGetTemplate(items, weaponPatch.WeaponTemplateId, weaponPatch.Name, out var weaponTemplate))
            {
                continue;
            }

            if (weaponTemplate.Properties == null)
            {
                continue;
            }

            var foldedSlot = GetConfiguredFoldedSlot(weaponTemplate, weaponPatch);
            var acceptedStockTemplateIds = GetFoldedSlotFilterIds(weaponTemplate, foldedSlot)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var stockTemplateId in acceptedStockTemplateIds)
            {
                if (!processedStockTemplateIds.Add(stockTemplateId))
                {
                    continue;
                }

                if (!TryGetTemplateQuiet(items, stockTemplateId, out var stockTemplate) || stockTemplate.Properties == null)
                {
                    continue;
                }

                var shouldBlockFolding = !supportedStockTemplateIds.Contains(stockTemplateId);
                if (TrySetTemplateProperty(stockTemplate.Properties, "BlocksFolding", shouldBlockFolding, string.Empty, stockTemplateId))
                {
                    patchedFoldBlocks++;
                }
            }
        }

        return patchedFoldBlocks;
    }

    private static HashSet<string> GetSupportedStockTemplateIds(FoldThatStockServerConfig config)
    {
        var supportedStockTemplateIds = new HashSet<string>(BuiltInSupportedStockTemplateIds, StringComparer.OrdinalIgnoreCase);

        foreach (var stockPatch in GetEnabledStockPatches(config))
        {
            if (!string.IsNullOrWhiteSpace(stockPatch.StockTemplateId))
            {
                supportedStockTemplateIds.Add(stockPatch.StockTemplateId.Trim());
            }
        }

        return supportedStockTemplateIds;
    }

    private static string GetConfiguredFoldedSlot(TemplateItem weaponTemplate, WeaponFoldPatch patch)
    {
        if (!string.IsNullOrWhiteSpace(patch.FoldedSlot))
        {
            return patch.FoldedSlot.Trim();
        }

        if (weaponTemplate.Properties != null)
        {
            var foldedSlot = GetStringMemberValue(weaponTemplate.Properties, "FoldedSlot");
            if (!string.IsNullOrWhiteSpace(foldedSlot))
            {
                return foldedSlot.Trim();
            }
        }

        return DefaultFoldedSlot;
    }

    private static IEnumerable<string> GetFoldedSlotFilterIds(TemplateItem weaponTemplate, string foldedSlot)
    {
        if (weaponTemplate.Properties == null)
        {
            yield break;
        }

        var slots = GetMemberValue(weaponTemplate.Properties, "Slots");
        foreach (var slot in EnumerateItems(slots))
        {
            var slotName = GetStringMemberValue(slot, "_name", "Name");
            if (!string.Equals(slotName, foldedSlot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var slotProperties = GetMemberValue(slot, "_props", "Props", "Properties") ?? slot;
            var filters = GetMemberValue(slotProperties, "filters", "Filters");
            foreach (var filter in EnumerateItems(filters))
            {
                var filterIds = GetMemberValue(filter, "Filter");
                foreach (var filterId in EnumerateItems(filterIds))
                {
                    var stockTemplateId = filterId?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(stockTemplateId))
                    {
                        yield return stockTemplateId;
                    }
                }
            }
        }
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

    private static bool TryGetTemplateQuiet(Dictionary<MongoId, TemplateItem> items, string templateId, out TemplateItem template)
    {
        template = null!;
        return !string.IsNullOrWhiteSpace(templateId)
            && TryParseMongoId(templateId.Trim(), out var parsedId)
            && items.TryGetValue(parsedId, out template!);
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
            if (NormalizeConfig(config))
            {
                SaveConfig(configPath, config);
                logger.Info($"{LogPrefix} Updated `{configPath}` with newly supported defaults.");
            }

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
            UziAdapterFoldSuppression = new UziAdapterFoldSuppressionConfig
            {
                SuppressLeftFoldingStocks = true,
                SuppressCollapsingStocks = true,
            },
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
                    Name = "SIG Sauer Folding Knuckle Stock Adapter",
                    StockTemplateId = "58ac1bf086f77420ed183f9f",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "MPX/MCX PMM ULSS stock",
                    StockTemplateId = "5c5db6f82e2216003a0fe914",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SIG Sauer Telescoping/Folding Stock",
                    StockTemplateId = "5fbcc429900b1d5091531dd7",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SIG Sauer Collapsing/Telescoping Stock",
                    StockTemplateId = "5894a13e86f7742405482982",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SB Tactical MPX Pistol Stabilizing Brace",
                    StockTemplateId = "6761496fe2cf1419500357e9",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SIG Sauer Locking Stock Hinge Assembly",
                    StockTemplateId = "6529348224cbe3c74a05e5c4",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "UZI PRO A3 Tactical Modular Folding Brace",
                    StockTemplateId = UziProA3BraceTemplateId,
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "UZI PRO Stabilizing Brace",
                    StockTemplateId = "668032ba74b8f2050c0b917d",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "UZI PRO SBR buttstock",
                    StockTemplateId = "66867310f3734a938b077f79",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "UZI PRO A3 Tactical Rear Stock Adapter",
                    StockTemplateId = "668672b8c99550c6fd0f0b29",
                    SizeReduceRight = null,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "UZI PRO CSM stock adapter",
                    StockTemplateId = "669cf78806768ff39504fc1c",
                    SizeReduceRight = null,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "AKM/AK-74 ME4 buffer tube adapter",
                    StockTemplateId = "5649b2314bdc2d79388b4576",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "AKM/AK-74 Magpul Zhukov-S stock",
                    StockTemplateId = "5b0e794b5acfc47a877359b2",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "FAB Defense UAS AK stock",
                    StockTemplateId = "5b04473a5acfc40018632f70",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "FAB Defense UAS SKS stock",
                    StockTemplateId = "5d0236dad7ad1a0940739d29",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "HK MP5 A3 old model stock",
                    StockTemplateId = "5926d40686f7740f152b6b7e",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "M700 AI AT AICS polymer chassis",
                    StockTemplateId = "5d25d0ac8abbc3054f3e61f7",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "M700 Magpul Pro 700 folding stock",
                    StockTemplateId = "5cdeac42d7f00c000d36ba73",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "SA-58 BRS stock",
                    StockTemplateId = "5b7d64555acfc4001876c8e2",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "SA58 folding stock",
                    StockTemplateId = "5b7d63cf5acfc4001876c8df",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "SA58 SPR stock",
                    StockTemplateId = "5b7d63de5acfc400170e2f8d",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "SA58 buffer tube adapter",
                    StockTemplateId = "5b099bf25acfc4001637e683",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
                new()
                {
                    Name = "KRISS Vector non-folding stock adapter",
                    StockTemplateId = "5fb655b748c711690e3a8d5a",
                    SizeReduceRight = 1,
                    BlocksFolding = false,
                },
            },
            WeaponPatches = new List<WeaponFoldPatch>
            {
                new()
                {
                    Name = "SIG MCX .300 Blackout assault rifle",
                    WeaponTemplateId = "5fbcc1d9016cce60e8341ab3",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                    AdditionalCompatibleStockTemplateIds = new List<string> { UziProA3BraceTemplateId },
                },
                new()
                {
                    Name = "SIG MPX 9x19 submachine gun",
                    WeaponTemplateId = "58948c8e86f77409493f7266",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                    AdditionalCompatibleStockTemplateIds = new List<string> { UziProA3BraceTemplateId },
                },
                new()
                {
                    Name = "SIG MCX-SPEAR 6.8x51 assault rifle",
                    WeaponTemplateId = "65290f395ae2ae97b80fdf2d",
                    Foldable = true,
                    FoldedSlot = "mod_stock_000",
                    AdditionalCompatibleStockTemplateIds = new List<string> { UziProA3BraceTemplateId },
                },
                new()
                {
                    Name = "IWI UZI PRO pistol 9x19",
                    WeaponTemplateId = "6680304edadb7aa61d00cef0",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "IWI UZI PRO SMG 9x19 submachine gun",
                    WeaponTemplateId = "668e71a8dadf42204c032ce1",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Kalashnikov AK-74N 5.45x39 assault rifle",
                    WeaponTemplateId = "5644bd2b4bdc2d3b4c8b4572",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Kalashnikov AK-74 5.45x39 assault rifle",
                    WeaponTemplateId = "5bf3e03b0db834001d2c4a9c",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Kalashnikov AKM 7.62x39 assault rifle",
                    WeaponTemplateId = "59d6088586f774275f37482f",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Kalashnikov AKMN 7.62x39 assault rifle",
                    WeaponTemplateId = "5a0ec13bfcdbcb00165aa685",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Molot Arms VPO-136 Vepr-KM 7.62x39 carbine",
                    WeaponTemplateId = "59e6152586f77473dc057aa1",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Molot Arms VPO-209 .366 TKM carbine",
                    WeaponTemplateId = "59e6687d86f77411d949b251",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Rifle Dynamics RD-704 7.62x39 assault rifle",
                    WeaponTemplateId = "628a60ae6b1d481ff772e9c8",
                    Foldable = true,
                    FoldedSlot = "mod_stock_000",
                },
                new()
                {
                    Name = "Aklys Defense Velociraptor .300 Blackout assault rifle",
                    WeaponTemplateId = "674d6121c09f69dfb201a888",
                    Foldable = true,
                    FoldedSlot = "mod_stock_000",
                },
                new()
                {
                    Name = "DS Arms SA-58 7.62x51 assault rifle",
                    WeaponTemplateId = "5b0bbe4e5acfc40dc528a72d",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "HK MP5 Navy 3 9x19 submachine gun",
                    WeaponTemplateId = "5926bb2186f7744b1c6c6e60",
                    Foldable = true,
                    // The MP5 stock slot belongs to its nested receiver, not the weapon root.
                    FoldedSlot = "",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "Accuracy International AXMC .338 LM bolt-action sniper rifle",
                    WeaponTemplateId = AxmcWeaponTemplateId,
                    Foldable = true,
                    // The folding stock is integral to the weapon and has no inventory slot.
                    FoldedSlot = "",
                    SizeReduceRight = 1,
                },
                new()
                {
                    Name = "Remington Model 700 7.62x51 bolt-action sniper rifle",
                    WeaponTemplateId = "5bfea6e90db834001b7347f3",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Simonov SKS 7.62x39 carbine",
                    WeaponTemplateId = "574d967124597745970e7c94",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
                new()
                {
                    Name = "Molot Arms Simonov OP-SKS 7.62x39 carbine",
                    WeaponTemplateId = "587e02ff24597743df3deaeb",
                    Foldable = true,
                    FoldedSlot = "mod_stock",
                },
            },
        };
    }

    // Preserve existing user settings while appending built-ins added by newer releases.
    // Users can keep an entry disabled; only a completely missing template id is migrated.
    private static bool NormalizeConfig(FoldThatStockServerConfig config)
    {
        var changed = false;
        if (config.WeaponPatches == null)
        {
            config.WeaponPatches = new List<WeaponFoldPatch>();
            changed = true;
        }

        if (config.StockPatches == null)
        {
            config.StockPatches = new List<StockTemplatePatch>();
            changed = true;
        }

        if (config.UziAdapterFoldSuppression == null)
        {
            config.UziAdapterFoldSuppression = new UziAdapterFoldSuppressionConfig();
            changed = true;
        }

        foreach (var weaponPatch in config.WeaponPatches.Where(patch => patch != null))
        {
            if (weaponPatch.AdditionalCompatibleStockTemplateIds == null)
            {
                weaponPatch.AdditionalCompatibleStockTemplateIds = new List<string>();
                changed = true;
            }

            if (weaponPatch.StockPatches == null)
            {
                weaponPatch.StockPatches = new List<StockTemplatePatch>();
                changed = true;
            }

            // Remove legacy weapon-level reductions such as the old RD-704 default.
            // Only weapons whose folding stock cannot own the reduction keep this value.
            if (!WeaponLevelSizeReductionTemplateIds.Contains(weaponPatch.WeaponTemplateId)
                && weaponPatch.SizeReduceRight.HasValue)
            {
                weaponPatch.SizeReduceRight = null;
                changed = true;
            }
        }

        var defaults = CreateDefaultConfig();
        foreach (var defaultWeaponPatch in defaults.WeaponPatches)
        {
            var existingWeaponPatch = config.WeaponPatches.FirstOrDefault(existing => existing != null
                && string.Equals(existing.WeaponTemplateId, defaultWeaponPatch.WeaponTemplateId, StringComparison.OrdinalIgnoreCase));
            if (existingWeaponPatch != null)
            {
                foreach (var stockTemplateId in defaultWeaponPatch.AdditionalCompatibleStockTemplateIds)
                {
                    if (existingWeaponPatch.AdditionalCompatibleStockTemplateIds.Any(existing => string.Equals(
                        existing,
                        stockTemplateId,
                        StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    existingWeaponPatch.AdditionalCompatibleStockTemplateIds.Add(stockTemplateId);
                    changed = true;
                }

                if (!existingWeaponPatch.SizeReduceRight.HasValue && defaultWeaponPatch.SizeReduceRight.HasValue)
                {
                    existingWeaponPatch.SizeReduceRight = defaultWeaponPatch.SizeReduceRight;
                    changed = true;
                }

                // Migrate the old MP5 default, which pointed at a receiver-owned slot that
                // FoldableComponent cannot resolve from the weapon root.
                if (string.Equals(defaultWeaponPatch.WeaponTemplateId, "5926bb2186f7744b1c6c6e60", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existingWeaponPatch.FoldedSlot, "mod_stock", StringComparison.OrdinalIgnoreCase))
                {
                    existingWeaponPatch.FoldedSlot = "";
                    changed = true;
                }

                continue;
            }

            config.WeaponPatches.Add(defaultWeaponPatch);
            changed = true;
        }

        foreach (var defaultStockPatch in defaults.StockPatches)
        {
            if (config.StockPatches.Any(existing => existing != null
                && string.Equals(existing.StockTemplateId, defaultStockPatch.StockTemplateId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            config.StockPatches.Add(defaultStockPatch);
            changed = true;
        }

        return changed;
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

    private static object? GetMemberValue(object? source, params string[] memberNames)
    {
        if (source == null)
        {
            return null;
        }

        var type = source.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        foreach (var memberName in memberNames)
        {
            var property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(source);
            }

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(source);
            }
        }

        return null;
    }

    private static string? GetStringMemberValue(object? source, params string[] memberNames)
    {
        return GetMemberValue(source, memberNames)?.ToString();
    }

    private static IEnumerable<object?> EnumerateItems(object? value)
    {
        if (value == null || value is string)
        {
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
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
