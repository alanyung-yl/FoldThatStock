using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.Visual;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace FoldThatStock
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class FoldThatStockPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.foldthatstock";
        public const string PluginName = "FoldThatStock";
        public const string PluginVersion = "2.1.0";
        private const string UziAdapterStockSlotId = "mod_stock_000";
        private const string Sa58TemplateId = "5b0bbe4e5acfc40dc528a72d";

        private static readonly Vector3 SigMpxMcxRetractedPosition = new Vector3(0f, 0.0102f, 0.092f);
        private static readonly Vector3 Mp5A3RetractedPosition = new Vector3(0.0001f, 0.0222f, -0.0678f);
        private static readonly HashSet<string> UziAnimatedAkTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5644bd2b4bdc2d3b4c8b4572", // AK-74N
            "5bf3e03b0db834001d2c4a9c", // AK-74
            "59d6088586f774275f37482f", // AKM
            "5a0ec13bfcdbcb00165aa685", // AKMN
            "59e6152586f77473dc057aa1", // VPO-136
            "59e6687d86f77411d949b251", // VPO-209
            "674d6121c09f69dfb201a888", // Aklys Defense Velociraptor
            "628a60ae6b1d481ff772e9c8"  // RD-704
        };
        private static readonly HashSet<string> UasSksStockDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "uas_sks_stock",
            "uas_shared_folding_stock"
        };
        private static readonly HashSet<string> StockRoutedAnimatedWeaponTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5fbcc1d9016cce60e8341ab3", // MCX .300 Blackout
            "58948c8e86f77409493f7266", // MPX 9x19
            "65290f395ae2ae97b80fdf2d", // MCX-SPEAR 6.8x51
            "5926bb2186f7744b1c6c6e60", // MP5 Navy 3 9x19
            "5bfea6e90db834001b7347f3"  // M700 7.62x51
        };
        private static readonly HashSet<string> NativeFoldOperationWeaponTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5fb64bc92b1b027b1f50bcf2", // KRISS Vector Gen.2 .45 ACP
            "5fc3f2d5900b1d5091531e57", // KRISS Vector Gen.2 9x19
            "6680304edadb7aa61d00cef0", // UZI PRO pistol 9x19
            "668e71a8dadf42204c032ce1"  // UZI PRO SMG 9x19
        };
        private static readonly HashSet<string> UziProWeaponTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "6680304edadb7aa61d00cef0", // UZI PRO pistol 9x19
            "668e71a8dadf42204c032ce1"  // UZI PRO SMG 9x19
        };
        private static readonly HashSet<string> UziStockAdapterTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "668672b8c99550c6fd0f0b29", // UZI PRO A3 Tactical Rear Stock Adapter
            "669cf78806768ff39504fc1c"  // UZI PRO CSM stock adapter
        };
        private static readonly HashSet<string> UziAdapterLeftFoldingStockDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sig_thin_folding_stock",
            "sig_folding_knuckle",
            "mpx_pmm_ulss_stock",
            "sig_telescoping_stock",
            "sig_stock_locking_hinge_assembly"
        };
        private static readonly HashSet<string> UziAdapterCollapsingStockDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sig_mpx_mcx_early_type_stock",
            "sig_mpx_brace"
        };
        private static readonly HashSet<string> CollapseStockDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sig_mpx_mcx_early_type_stock",
            "sig_mpx_brace",
            "mp5_a3_old_model_stock"
        };

        internal static FoldThatStockPlugin Instance { get; private set; }

        internal static readonly Quaternion DefaultFoldedRotation = new Quaternion(0f, 0.7071068f, 0.7071068f, 0f);
        internal static readonly Quaternion DefaultRightFoldedRotation = new Quaternion(0f, -0.7071068f, -0.7071068f, 0f);
        private static readonly Quaternion Sa58BrsFoldedRotation = new Quaternion(-0.02192926f, -0.83048016f, 0.03819934f, 0.55530408f);
        private static readonly Quaternion PositiveZFoldedRotation = Quaternion.Euler(0f, 0f, 180f);
        private static readonly Quaternion FoldingStockPreviewRotationCorrection = Quaternion.Euler(0f, -90f, 0f);

        internal sealed class VisualStockDefinition
        {
            public string Id;
            public string DisplayName;
            public string TemplateId;
            public string WeaponTemplateId = string.Empty;
            public string ContainerPathContains = string.Empty;
            public string StockPathContains;
            public string VisualTargetPathContains = string.Empty;
            public string[] TargetBoneNamePatterns;
            public bool KeepUnfoldedRotation;
            public bool HasFoldedRotation;
            public Quaternion FoldedRotation;
            public bool HasRetractedPosition;
            public Vector3 RetractedPosition;
            public bool CorrectReversedPreview;
            public float PreviewYawAdjustmentDegrees;
            public string BundleFileName;
            public string BundleSourcePathContains;
            public string BundleOverridePath;
        }

        // Describes one reusable game-animation donor. Resolved clips are cached so each
        // family is loaded at most once; fallback bundles are released immediately.
        private sealed class DonorAnimationProfile
        {
            public string Id;
            public string DisplayName;
            public string BundleFolder;
            public string FoldClipName;
            public string UnfoldClipName;
            public string[] AnimationAssetSuffixes;
            public bool ManipulatesRightArm;
            public AnimationClip FoldClip;
            public AnimationClip UnfoldClip;
            public bool AttemptedLoad;
        }

        private sealed class SharedServerConfig
        {
            [JsonProperty("UziAdapterFoldSuppression")]
            public UziAdapterFoldSuppressionSettings UziAdapterFoldSuppression { get; set; }
        }

        private sealed class UziAdapterFoldSuppressionSettings
        {
            [JsonProperty("SuppressLeftFoldingStocks")]
            public bool? SuppressLeftFoldingStocks { get; set; }

            [JsonProperty("SuppressCollapsingStocks")]
            public bool? SuppressCollapsingStocks { get; set; }
        }

        internal static readonly VisualStockDefinition[] BuiltInVisualStockDefinitions =
        {
            new VisualStockDefinition
            {
                Id = "sig_thin_folding_stock",
                DisplayName = "SIG Sauer Thin Side-Folding Stock",
                TemplateId = "5fbcc437d724d907e2077d5c",
                StockPathContains = "stock_all_sig_thin_folding_stock",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_all_sig_thin_folding_stock.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_thin_folding_stock.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_thin_folding_stock.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_folding_knuckle",
                DisplayName = "SIG Sauer Folding Knuckle Stock Adapter",
                TemplateId = "58ac1bf086f77420ed183f9f",
                StockPathContains = "stock_all_sig_folding_knuckle",
                TargetBoneNamePatterns = new[] { "stk_rt" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_all_sig_folding_knuckle.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_folding_knuckle.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_folding_knuckle.bundle")
            },
            new VisualStockDefinition
            {
                Id = "mpx_pmm_ulss_stock",
                DisplayName = "MPX/MCX PMM ULSS stock",
                TemplateId = "5c5db6f82e2216003a0fe914",
                StockPathContains = "stock_mpx_pmm_ulss",
                TargetBoneNamePatterns = new[] { "mod_stock" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_mpx_pmm_ulss.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_mpx_pmm_ulss.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_mpx_pmm_ulss.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_telescoping_stock",
                DisplayName = "SIG Sauer Telescoping/Folding Stock",
                TemplateId = "5fbcc429900b1d5091531dd7",
                StockPathContains = "stock_all_sig_telescoping_stock",
                TargetBoneNamePatterns = new[] { "mod_stock" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_all_sig_telescoping_stock.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_telescoping_stock.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_telescoping_stock.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_mpx_mcx_early_type_stock",
                DisplayName = "SIG Sauer Collapsing/Telescoping Stock",
                TemplateId = "5894a13e86f7742405482982",
                StockPathContains = "stock_all_sig_mpx_mcx_early_type",
                TargetBoneNamePatterns = new[] { "mod_stock_000" },
                KeepUnfoldedRotation = true,
                HasRetractedPosition = true,
                RetractedPosition = SigMpxMcxRetractedPosition
            },
            new VisualStockDefinition
            {
                Id = "sig_mpx_brace",
                DisplayName = "SB Tactical MPX Pistol Stabilizing Brace",
                TemplateId = "6761496fe2cf1419500357e9",
                StockPathContains = "stock_all_sig_mpx_brace",
                TargetBoneNamePatterns = new[] { "mod_stock_001" },
                KeepUnfoldedRotation = true,
                HasRetractedPosition = true,
                RetractedPosition = SigMpxMcxRetractedPosition
            },
            new VisualStockDefinition
            {
                Id = "mp5_a3_old_model_stock",
                DisplayName = "HK MP5 A3 old model stock",
                TemplateId = "5926d40686f7740f152b6b7e",
                StockPathContains = "stock_mp5_hk_a3_std",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                KeepUnfoldedRotation = true,
                HasRetractedPosition = true,
                RetractedPosition = Mp5A3RetractedPosition,
                BundleFileName = "stock_mp5_hk_a3_std.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_mp5_hk_a3_std.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_mp5_hk_a3_std.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_stock_locking_hinge_assembly",
                DisplayName = "SIG Sauer Locking Stock Hinge Assembly",
                TemplateId = "6529348224cbe3c74a05e5c4",
                StockPathContains = "stock_all_sig_stock_locking_hinge_assembly",
                TargetBoneNamePatterns = new[] { "mod_stock_001" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_all_sig_stock_locking_hinge_assembly.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_stock_locking_hinge_assembly.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_stock_locking_hinge_assembly.bundle")
            },
            new VisualStockDefinition
            {
                Id = "uzi_pro_a3_modular_folding_brace",
                DisplayName = "UZI PRO A3 Tactical Modular Folding Brace",
                TemplateId = "6686717ffb75ee4a5e02eb19",
                StockPathContains = "stock_uzi_pro_a3_tactical_modular_folding_brace",
                TargetBoneNamePatterns = new[] { "mod_stock_axis_002" },
                HasFoldedRotation = true,
                FoldedRotation = PositiveZFoldedRotation
            },
            new VisualStockDefinition
            {
                Id = "uzi_pro_stabilizing_brace",
                DisplayName = "UZI PRO Stabilizing Brace",
                TemplateId = "668032ba74b8f2050c0b917d",
                StockPathContains = "stock_uzi_pro_sb_tactical_stabilizing_brace",
                TargetBoneNamePatterns = new[] { "mod_stock_axis_000" }
            },
            new VisualStockDefinition
            {
                Id = "uzi_pro_sbr_buttstock",
                DisplayName = "UZI PRO SBR buttstock",
                TemplateId = "66867310f3734a938b077f79",
                StockPathContains = "stock_uzi_pro_iwi_pro_buttstock",
                TargetBoneNamePatterns = new[] { "mod_stock_axis_001" }
            },
            new VisualStockDefinition
            {
                Id = "ak_utg_sfs_adapter",
                DisplayName = "AKM/AK-74 ME4 buffer tube adapter",
                TemplateId = "5649b2314bdc2d79388b4576",
                StockPathContains = "stock_ak_utg_sfs_adapter",
                TargetBoneNamePatterns = new[] { "mod_stock_001" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation,
                CorrectReversedPreview = true,
                BundleFileName = "stock_ak_utg_sfs_adapter.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_ak_utg_sfs_adapter.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_ak_utg_sfs_adapter.bundle")
            },
            new VisualStockDefinition
            {
                Id = "ak_magpul_zhukov_s",
                DisplayName = "AKM/AK-74 Magpul Zhukov-S stock",
                TemplateId = "5b0e794b5acfc47a877359b2",
                StockPathContains = "stock_ak_magpul_zhukov_s",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation,
                CorrectReversedPreview = true,
                BundleFileName = "stock_ak_magpul_zhukov_s.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_ak_magpul_zhukov_s.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_ak_magpul_zhukov_s.bundle")
            },
            new VisualStockDefinition
            {
                Id = "uas_ak_stock",
                DisplayName = "FAB Defense UAS AK stock",
                TemplateId = "5b04473a5acfc40018632f70",
                ContainerPathContains = "stock_ak_fab_defense_uas_ak_p",
                StockPathContains = "stock_ak_fab_defense_uas_ak_p",
                TargetBoneNamePatterns = new[] { "mod_stock_folding_axis" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation,
                BundleFileName = "stock_ak_fab_defense_uas_ak_p.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_ak_fab_defense_uas_ak_p.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_ak_fab_defense_uas_ak_p.bundle")
            },
            new VisualStockDefinition
            {
                Id = "uas_sks_stock",
                DisplayName = "FAB Defense UAS SKS stock",
                TemplateId = "5d0236dad7ad1a0940739d29",
                ContainerPathContains = "stock_sks_fab_defence_uas_sks",
                StockPathContains = "stock_sks_fab_defence_uas_sks",
                TargetBoneNamePatterns = new[] { "mod_stock_folding_axis" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation
            },
            new VisualStockDefinition
            {
                Id = "uas_shared_folding_stock",
                DisplayName = "FAB Defense UAS folding stock",
                TemplateId = "653ed132896b99b40a0292e6",
                StockPathContains = "stock_fab_fab_defence_uas_folding_stock",
                TargetBoneNamePatterns = new[] { "mod_stock_folding_axis" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation
            },
            new VisualStockDefinition
            {
                Id = "m700_ai_at_aics_chassis",
                DisplayName = "M700 AI AT AICS polymer chassis",
                TemplateId = "5d25d0ac8abbc3054f3e61f7",
                StockPathContains = "stock_m700_ai_at_aics_chasiss",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_m700_ai_at_aics_chasiss.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_m700_ai_at_aics_chasiss.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_m700_ai_at_aics_chasiss.bundle")
            },
            new VisualStockDefinition
            {
                Id = "m700_magpul_pro_700_folding_stock",
                DisplayName = "M700 Magpul Pro 700 folding stock",
                TemplateId = "5cdeac42d7f00c000d36ba73",
                StockPathContains = "stock_m700_magpul_pro_700_folding_stock",
                VisualTargetPathContains = "stock_m700_magpul_pro_700_chasiss",
                TargetBoneNamePatterns = new[] { "mod_stock" },
                CorrectReversedPreview = true,
                PreviewYawAdjustmentDegrees = 110f
            },
            new VisualStockDefinition
            {
                Id = "sa58_brs_stock",
                DisplayName = "SA-58 BRS stock",
                TemplateId = "5b7d64555acfc4001876c8e2",
                StockPathContains = "stock_sa58_ds_arms_para_brs",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                HasFoldedRotation = true,
                FoldedRotation = Sa58BrsFoldedRotation,
                CorrectReversedPreview = true,
                BundleFileName = "stock_sa58_ds_arms_para_brs.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_sa58_ds_arms_para_brs.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_sa58_ds_arms_para_brs.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sa58_folding_stock",
                DisplayName = "SA58 folding stock",
                TemplateId = "5b7d63cf5acfc4001876c8df",
                StockPathContains = "stock_sa58_ds_arms_para_folding_stock",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation,
                CorrectReversedPreview = true,
                BundleFileName = "stock_sa58_ds_arms_para_folding_stock.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_sa58_ds_arms_para_folding_stock.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_sa58_ds_arms_para_folding_stock.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sa58_spr_stock",
                DisplayName = "SA58 SPR stock",
                TemplateId = "5b7d63de5acfc400170e2f8d",
                StockPathContains = "stock_sa58_ds_arms_para_spr_stock",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                HasFoldedRotation = true,
                FoldedRotation = DefaultRightFoldedRotation,
                CorrectReversedPreview = true,
                BundleFileName = "stock_sa58_ds_arms_para_spr_stock.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_sa58_ds_arms_para_spr_stock.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_sa58_ds_arms_para_spr_stock.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sa58_folding_buffer_tube_adapter",
                DisplayName = "SA58 buffer tube adapter",
                TemplateId = "5b099bf25acfc4001637e683",
                StockPathContains = "stock_sa58_ds_arms_para_folding_buffer_tube_adapter",
                TargetBoneNamePatterns = new[] { "mod_stock_axis" },
                HasFoldedRotation = true,
                FoldedRotation = PositiveZFoldedRotation
            },
            new VisualStockDefinition
            {
                Id = "vector_kriss_non_folding_adapter",
                DisplayName = "KRISS Vector non-folding stock adapter",
                TemplateId = "5fb655b748c711690e3a8d5a",
                StockPathContains = "stock_vector_kriss_non_folding_adapter",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                CorrectReversedPreview = true,
                BundleFileName = "stock_vector_kriss_non_folding_adapter.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_vector_kriss_non_folding_adapter.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_vector_kriss_non_folding_adapter.bundle")
            }
        };

        private readonly HashSet<string> _loggedMissingBundlePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedRedirects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedVisualBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedAnimationFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedPreviewRepairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<object> _animatedFoldOperations = new HashSet<object>();
        private readonly HashSet<FoldableComponent> _animatedFoldables = new HashSet<FoldableComponent>();

        private readonly DonorAnimationProfile _umpDonor = new DonorAnimationProfile
        {
            Id = "ump-right-fold",
            DisplayName = "UMP right-fold",
            BundleFolder = "ump",
            FoldClipName = "ump_stock_fold",
            UnfoldClipName = "ump_stock_unfold",
            AnimationAssetSuffixes = new[]
            {
                "assets/content/weapons/ump/weapon_hk_ump_1143x23_animation.fbx",
                "assets/content/weapons/ump/weapon_hk_ump_1143x23_animation_1.fbx"
            },
            ManipulatesRightArm = true
        };
        private readonly DonorAnimationProfile _uziProDonor = new DonorAnimationProfile
        {
            Id = "uzi-pro-right-fold",
            DisplayName = "UZI PRO SMG right-fold",
            BundleFolder = "uzi_pro",
            FoldClipName = "uzip_smg_stock_fold",
            UnfoldClipName = "uzip_smg_stock_unfold",
            AnimationAssetSuffixes = new[]
            {
                "assets/content/weapons/uzi_pro/weapon_iwi_uzi_pro_smg_9x19_animation.fbx"
            },
            ManipulatesRightArm = true
        };
        private readonly DonorAnimationProfile _mp5Donor = new DonorAnimationProfile
        {
            Id = "mp5-collapse",
            DisplayName = "MP5 collapsing-stock",
            BundleFolder = "mp5",
            FoldClipName = "mp5_stock_collapse",
            UnfoldClipName = "mp5_stock_uncollapse",
            AnimationAssetSuffixes = new[]
            {
                "assets/content/weapons/mp5/weapon_hk_mp5_navy3_9x19_animation_0.fbx",
                "assets/content/weapons/mp5/weapon_hk_mp5_navy3_9x19_animation_1.fbx"
            },
            ManipulatesRightArm = false
        };
        private readonly DonorAnimationProfile _aks74uDonor = new DonorAnimationProfile
        {
            Id = "aks74u-left-fold",
            DisplayName = "AKS-74U left-fold",
            BundleFolder = "aks74u",
            FoldClipName = "aks74u_stock_fold_left",
            UnfoldClipName = "aks74u_stock_unfold_left",
            AnimationAssetSuffixes = new[]
            {
                "assets/content/weapons/aks74u/weapon_izhmash_aks74u_545x39_animation.fbx",
                "assets/content/weapons/aks74u/weapon_izhmash_aks74u_545x39_animation_0.fbx"
            },
            ManipulatesRightArm = false
        };
        private Harmony _harmony;
        private bool _isShuttingDown;
        private bool _suppressUziAdapterLeftFoldingStocks = true;
        private bool _suppressUziAdapterCollapsingStocks = true;

        private void Awake()
        {
            Instance = this;
            LoadSharedServerConfig();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(FoldThatStockPlugin).Assembly);
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void LoadSharedServerConfig()
        {
            string gameRootPath = Directory.GetParent(BepInEx.Paths.BepInExRootPath)?.FullName
                ?? BepInEx.Paths.BepInExRootPath;
            string configPath = Path.Combine(
                gameRootPath,
                "SPT_Runtime",
                "user",
                "mods",
                "FoldThatStock",
                "config.json");

            try
            {
                if (!File.Exists(configPath))
                {
                    Logger.LogWarning(
                        $"Shared server config was not found at {configPath}. "
                        + "UZI adapter fold suppression will use its safe defaults.");
                    return;
                }

                SharedServerConfig config = JsonConvert.DeserializeObject<SharedServerConfig>(File.ReadAllText(configPath));
                UziAdapterFoldSuppressionSettings settings = config?.UziAdapterFoldSuppression;
                _suppressUziAdapterLeftFoldingStocks = settings?.SuppressLeftFoldingStocks ?? true;
                _suppressUziAdapterCollapsingStocks = settings?.SuppressCollapsingStocks ?? true;
                Logger.LogInfo(
                    "Loaded UZI adapter fold suppression from the shared server config: "
                    + $"left-folding={_suppressUziAdapterLeftFoldingStocks}, "
                    + $"collapsing={_suppressUziAdapterCollapsingStocks}.");
            }
            catch (Exception exception)
            {
                _suppressUziAdapterLeftFoldingStocks = true;
                _suppressUziAdapterCollapsingStocks = true;
                Logger.LogWarning(
                    $"Shared server config could not be read from {configPath}: {exception.Message}. "
                    + "UZI adapter fold suppression will use its safe defaults.");
            }
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            FoldThatStockArmAnimationOverlay[] overlays = FindObjectsOfType<FoldThatStockArmAnimationOverlay>();
            for (int i = 0; i < overlays.Length; i++)
            {
                overlays[i]?.Cancel(false);
            }

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            ClearDonorProfile(_umpDonor);
            ClearDonorProfile(_uziProDonor);
            ClearDonorProfile(_mp5Donor);
            ClearDonorProfile(_aks74uDonor);
            _animatedFoldOperations.Clear();
            _animatedFoldables.Clear();

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private static void ClearDonorProfile(DonorAnimationProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.FoldClip = null;
            profile.UnfoldClip = null;
        }

        internal void TryAttachVisualController(Item item, GameObject itemView)
        {
            if (item == null
                || itemView == null
                || (UsesNativeFoldOperation(item) && !RequiresNativeVisualCorrection(item))
                || !ContainsSupportedVisualTarget(itemView.transform))
            {
                return;
            }

            FoldThatStockVisualController controller = itemView.GetComponent<FoldThatStockVisualController>();
            bool added = false;
            if (controller == null)
            {
                controller = itemView.AddComponent<FoldThatStockVisualController>();
                added = true;
            }

            if (controller.Bind(item))
            {
                return;
            }

            if (added)
            {
                Destroy(controller);
            }
        }

        internal void LogVisualBinding(string key, string message)
        {
            if (_loggedVisualBindings.Add(key))
            {
                Logger.LogInfo(message);
            }
        }

        // Repairs only the standalone inventory/inspection prefab. It does not touch the
        // stock transform used by the in-raid folding controller or donor animation.
        internal void RepairReversedStockPreview(Item item, GameObject itemView)
        {
            if (_isShuttingDown || item == null || itemView == null)
            {
                return;
            }

            VisualStockDefinition definition;
            if (!TryMatchBuiltInDefinition(item, out definition)
                || definition == null
                || !definition.CorrectReversedPreview)
            {
                return;
            }

            if (itemView.GetComponent<FoldThatStockPreviewPivotRepair>() != null)
            {
                return;
            }

            PreviewPivot previewPivot = itemView.GetComponent<PreviewPivot>();
            bool addedPreviewPivot = previewPivot == null;
            if (addedPreviewPivot)
            {
                previewPivot = itemView.AddComponent<PreviewPivot>();
                previewPivot.AutoAdjustPivot();
            }

            if (previewPivot.Icon == null)
            {
                previewPivot.Icon = new PreviewPivot.IconSettings();
            }

            Quaternion originalRotation = previewPivot.Icon.rotation;
            if (Mathf.Approximately(
                originalRotation.x * originalRotation.x
                    + originalRotation.y * originalRotation.y
                    + originalRotation.z * originalRotation.z
                    + originalRotation.w * originalRotation.w,
                0f))
            {
                originalRotation = Quaternion.identity;
            }

            Quaternion previewCorrection = Quaternion.Euler(
                0f,
                definition.PreviewYawAdjustmentDegrees,
                0f) * FoldingStockPreviewRotationCorrection;
            previewPivot.Icon.rotation = previewCorrection * originalRotation;
            itemView.AddComponent<FoldThatStockPreviewPivotRepair>();

            if (_loggedPreviewRepairs.Add(definition.Id))
            {
                Logger.LogInfo(
                    $"Corrected reversed preview for {definition.DisplayName}"
                    + (addedPreviewPivot ? " and generated its missing PreviewPivot center." : "."));
            }
        }

        internal async Task<GameObject> RepairReversedStockPreviewAsync(Item item, Task<GameObject> itemViewTask)
        {
            GameObject itemView = await itemViewTask;
            RepairReversedStockPreview(item, itemView);
            return itemView;
        }

        internal void RedirectBundlePath(ref string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            VisualStockDefinition definition;
            if (!TryGetBuiltInBundleDefinition(path, out definition))
            {
                return;
            }

            string overridePath = ResolveOverrideBundlePath(definition);
            if (string.IsNullOrWhiteSpace(overridePath) || !File.Exists(overridePath))
            {
                string missingKey = definition.Id + "|" + overridePath;
                if (_loggedMissingBundlePaths.Add(missingKey))
                {
                    Logger.LogWarning($"Bundle override for {definition.DisplayName} was not found: {overridePath ?? "<null>"}");
                }

                return;
            }

            if (PathsEqual(path, overridePath))
            {
                return;
            }

            string redirectKey = definition.Id + "|" + path;
            if (_loggedRedirects.Add(redirectKey))
            {
                Logger.LogInfo($"Redirecting {definition.DisplayName} bundle to {overridePath}");
            }

            path = overridePath;
        }

        // Entry point from the fold-operation patch. Supported weapons with a working native
        // fold operation keep it; donor-routed weapons use the overlay; remaining custom stocks
        // retain the original instant-completion fallback.
        internal void CompleteFoldOperationIfSupported(object operationState, FoldOperation foldOperation)
        {
            if (operationState == null || !IsSupportedFoldOperation(foldOperation))
            {
                return;
            }

            if (UsesNativeFoldOperation(foldOperation))
            {
                if (RequiresNativeVisualCorrection(foldOperation?.Foldable?.Item))
                {
                    BeginNativeVisualAnimation(foldOperation.Foldable);
                }

                return;
            }

            if (TryStartHybridDonorAnimation(operationState, foldOperation))
            {
                return;
            }

            InvokeFoldOperationCompletion(operationState);
        }

        private static bool UsesNativeFoldOperation(FoldOperation foldOperation)
        {
            return UsesNativeFoldOperation(foldOperation?.Foldable?.Item);
        }

        private static bool UsesNativeFoldOperation(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                Item rootItem = item.GetRootItem() ?? item;
                return NativeFoldOperationWeaponTemplateIds.Contains(GetTemplateId(rootItem));
            }
            catch
            {
                return NativeFoldOperationWeaponTemplateIds.Contains(GetTemplateId(item));
            }
        }

        private static bool RequiresNativeVisualCorrection(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                Item rootItem = item.GetRootItem() ?? item;
                VisualStockDefinition stockDefinition;
                return UziProWeaponTemplateIds.Contains(GetTemplateId(rootItem))
                    && TryFindItemTreeVisualDefinition(rootItem, out stockDefinition)
                    && string.Equals(
                        stockDefinition.Id,
                        "uzi_pro_a3_modular_folding_brace",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void BeginNativeVisualAnimation(FoldableComponent foldable)
        {
            if (foldable == null)
            {
                return;
            }

            FoldThatStockVisualController[] controllers = FindObjectsOfType<FoldThatStockVisualController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i]?.BeginNativeAnimation(foldable);
            }
        }

        internal bool IsFoldOperationCurrent(object operationState, FoldOperation foldOperation)
        {
            if (operationState == null || foldOperation == null)
            {
                return false;
            }

            try
            {
                FieldInfo foldOperationField = GetInstanceFields(operationState.GetType())
                    .SingleOrDefault(field => field.FieldType == typeof(FoldOperation));
                return foldOperationField != null && ReferenceEquals(foldOperationField.GetValue(operationState), foldOperation);
            }
            catch
            {
                return false;
            }
        }

        // Releases overlay ownership and guarantees that a failed/cancelled animation cannot
        // leave either the logical operation or custom stock visual in a held state.
        internal void FinishDonorAnimation(object operationState, FoldOperation foldOperation, bool completeOperation)
        {
            if (operationState != null)
            {
                _animatedFoldOperations.Remove(operationState);
            }

            if (foldOperation?.Foldable != null)
            {
                _animatedFoldables.Remove(foldOperation.Foldable);
            }

            ReleaseVisualStock(foldOperation?.Foldable, true, FoldThatStockVisualController.DefaultTransitionSeconds);
            if (completeOperation && !_isShuttingDown && IsFoldOperationCurrent(operationState, foldOperation))
            {
                InvokeFoldOperationCompletion(operationState);
            }
        }

        // EFT must enter its true folded/unfolded idle state before the final 0.30-second
        // fade. Otherwise the overlay fades toward the old idle pose and snaps afterward.
        internal bool CompleteDonorOperationForHandoff(object operationState, FoldOperation foldOperation)
        {
            if (_isShuttingDown || !IsFoldOperationCurrent(operationState, foldOperation))
            {
                return false;
            }

            return InvokeFoldOperationCompletion(operationState);
        }

        internal void LogAnimationFailureOnce(string key, string message)
        {
            if (_loggedAnimationFailures.Add(key))
            {
                Logger.LogWarning(message);
            }
        }

        // Suppress EFT's native stock-animation callback only while our overlay owns this
        // operation. Native behavior remains available for operations outside that set.
        internal bool ShouldAllowFoldAnimationEvent(object operationState)
        {
            if (operationState == null)
            {
                return true;
            }

            if (_animatedFoldOperations.Contains(operationState))
            {
                return false;
            }

            FieldInfo[] foldOperationFields = operationState.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => field.FieldType == typeof(FoldOperation))
                .ToArray();

            if (foldOperationFields.Length != 1)
            {
                return true;
            }

            try
            {
                return foldOperationFields[0].GetValue(operationState) != null;
            }
            catch
            {
                return true;
            }
        }

        // Selects a donor from both the weapon root and installed stock, then attaches one
        // short-lived sampler to the active first-person animator. The proven AK/UMP path is
        // preserved, RD-704 uses the Vector gesture, the A3-on-SIG and UAS-SKS routes use
        // the UZI PRO gesture, and other SIG/MP5 weapons use MP5 or AKS-74U gestures.
        private bool TryStartHybridDonorAnimation(object operationState, FoldOperation foldOperation)
        {
            DonorAnimationProfile profile;
            VisualStockDefinition stockDefinition;
            if (!TryResolveDonorProfile(foldOperation, out profile, out stockDefinition))
            {
                return false;
            }

            AnimationClip clip = ResolveDonorClip(profile, foldOperation.NewValue);
            if (clip == null)
            {
                LogAnimationFailureOnce(
                    profile.Id + "-donor-clip",
                    $"Folding animation was skipped because the {profile.DisplayName} donor clip could not be loaded. The instant fold fallback will be used.");
                return false;
            }

            Animator animator = ResolveUnityAnimator(operationState);
            if (animator == null)
            {
                LogAnimationFailureOnce(
                    profile.Id + "-animator",
                    "Folding animation was skipped because the first-person Unity Animator could not be resolved. The instant fold fallback will be used.");
                return false;
            }

            FoldThatStockArmAnimationOverlay overlay = animator.GetComponent<FoldThatStockArmAnimationOverlay>();
            if (overlay != null && overlay.IsRunning)
            {
                LogAnimationFailureOnce(
                    "donor-overlay-busy",
                    "A folding animation was already active when another fold operation started. The instant fold fallback will be used for the new operation.");
                return false;
            }

            if (overlay == null)
            {
                overlay = animator.gameObject.AddComponent<FoldThatStockArmAnimationOverlay>();
            }

            if (!overlay.Begin(
                this,
                operationState,
                foldOperation,
                animator,
                ResolvePlayerTransform(operationState),
                clip,
                profile.ManipulatesRightArm,
                GetDonorContactSeconds(clip, foldOperation.NewValue),
                GetStockTransitionSeconds(clip, foldOperation.NewValue),
                profile.DisplayName))
            {
                Destroy(overlay);
                LogAnimationFailureOnce(
                    profile.Id + "-arm-rig",
                    "Folding animation was skipped because the compatible first-person arm bones were not found. The instant fold fallback will be used.");
                return false;
            }

            _animatedFoldOperations.Add(operationState);
            _animatedFoldables.Add(foldOperation.Foldable);
            HoldVisualStock(foldOperation.Foldable, foldOperation.NewValue);
            Logger.LogInfo(
                $"Playing {profile.DisplayName} hybrid clip {clip.name} through Animator {animator.name} "
                + $"for {stockDefinition?.DisplayName ?? "the selected stock"} (length {clip.length:0.000}s).");
            return true;
        }

        // The EFT fold operation returns to idle at the beginning of our final pose fade.
        // Keep the same weapon input-locked until the overlay itself has fully finished so a
        // second FoldStock command cannot silently toggle the logical state during that fade.
        internal bool IsFoldInputLocked(Item item)
        {
            if (_isShuttingDown || item == null || _animatedFoldables.Count == 0)
            {
                return false;
            }

            try
            {
                Item rootItem = item.GetRootItem() ?? item;
                foreach (FoldableComponent foldable in _animatedFoldables)
                {
                    Item animatedItem = foldable?.Item;
                    if (animatedItem != null && ReferenceEquals(animatedItem.GetRootItem() ?? animatedItem, rootItem))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // A weapon can disappear during controller teardown; allow EFT to handle the
                // input normally in that exceptional state instead of leaving a global lock.
            }

            return false;
        }

        private bool ShouldSuppressUziAdapterFolding(FoldableComponent foldable)
        {
            if (foldable?.Item == null
                || foldable.Folded)
            {
                return false;
            }

            try
            {
                Item rootItem = foldable.Item.GetRootItem() ?? foldable.Item;
                if (!UziProWeaponTemplateIds.Contains(GetTemplateId(rootItem)))
                {
                    return false;
                }

                foreach (Item item in rootItem.GetAllItems())
                {
                    if (!UziStockAdapterTemplateIds.Contains(GetTemplateId(item)))
                    {
                        continue;
                    }

                    CompoundItem adapter = item as CompoundItem;
                    Slot stockSlot = adapter?.Slots?.FirstOrDefault(slot => string.Equals(
                        slot.ID,
                        UziAdapterStockSlotId,
                        StringComparison.OrdinalIgnoreCase));
                    Item installedStock = stockSlot?.ContainedItem;
                    if (installedStock == null)
                    {
                        return true;
                    }

                    if (IsUziAdapterStockCategorySuppressed(installedStock))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // An item can be detached while UI eligibility is being refreshed. Preserve
                // EFT's normal behavior when the transient adapter slot cannot be inspected.
            }

            return false;
        }

        private bool IsUziAdapterStockCategorySuppressed(Item installedStock)
        {
            VisualStockDefinition definition;
            if (TryMatchBuiltInDefinition(installedStock, out definition)
                && IsUziAdapterStockCategorySuppressed(definition))
            {
                return true;
            }

            foreach (Item child in installedStock.GetAllItems())
            {
                if (TryMatchBuiltInDefinition(child, out definition)
                    && IsUziAdapterStockCategorySuppressed(definition))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsUziAdapterStockCategorySuppressed(VisualStockDefinition definition)
        {
            return definition != null
                && ((_suppressUziAdapterLeftFoldingStocks
                        && UziAdapterLeftFoldingStockDefinitionIds.Contains(definition.Id))
                    || (_suppressUziAdapterCollapsingStocks
                        && UziAdapterCollapsingStockDefinitionIds.Contains(definition.Id)));
        }

        // Keep the successful UMP synchronization ratios, scaled to each donor's native clip
        // length. This preserves the UMP timing and prevents shorter AKS-74U clips from lagging.
        private static float GetDonorContactSeconds(AnimationClip clip, bool targetFolded)
        {
            const float foldContactRatio = 0.2647057f;
            const float unfoldContactRatio = 0.3676471f;
            return clip.length * (targetFolded ? foldContactRatio : unfoldContactRatio);
        }

        private static float GetStockTransitionSeconds(AnimationClip clip, bool targetFolded)
        {
            const float foldTransitionRatio = 0.3235292f;
            const float unfoldTransitionRatio = 0.2352941f;
            return clip.length * (targetFolded ? foldTransitionRatio : unfoldTransitionRatio);
        }

        private AnimationClip ResolveDonorClip(DonorAnimationProfile profile, bool targetFolded)
        {
            if (!profile.AttemptedLoad)
            {
                profile.AttemptedLoad = true;
                LoadDonorClips(profile);
            }

            return targetFolded ? profile.FoldClip : profile.UnfoldClip;
        }

        // Prefer clips Unity has already loaded. Loading the donor's client bundle ourselves
        // is the fallback when that weapon has not otherwise appeared during the session. The
        // fallback bundle is released after extracting the clips so EFT remains its sole owner.
        private void LoadDonorClips(DonorAnimationProfile profile)
        {
            try
            {
                AssignDonorClips(profile, Resources.FindObjectsOfTypeAll<AnimationClip>());
                if (profile.FoldClip != null && profile.UnfoldClip != null)
                {
                    Logger.LogInfo($"Using {profile.DisplayName} clips already loaded by the game.");
                    return;
                }

                foreach (AssetBundle loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (TryLoadDonorClipsFromBundle(profile, loadedBundle))
                    {
                        Logger.LogInfo($"Loaded {profile.DisplayName} clips from an existing game asset bundle.");
                        return;
                    }
                }

                string bundlePath = Path.Combine(
                    Application.streamingAssetsPath,
                    "Windows",
                    "assets",
                    "content",
                    "weapons",
                    profile.BundleFolder,
                    "client_assets.bundle");
                if (!File.Exists(bundlePath))
                {
                    LogAnimationFailureOnce(profile.Id + "-client-bundle-missing", $"Donor client asset bundle was not found: {bundlePath}");
                    return;
                }

                AssetBundle donorBundle = AssetBundle.LoadFromFile(bundlePath);
                if (donorBundle == null)
                {
                    LogAnimationFailureOnce(profile.Id + "-client-bundle-load", $"Unity could not load the donor client asset bundle: {bundlePath}");
                    return;
                }

                try
                {
                    if (TryLoadDonorClipsFromBundle(profile, donorBundle))
                    {
                        Logger.LogInfo($"Loaded {profile.DisplayName} clips from {bundlePath} and released the donor bundle.");
                    }
                }
                finally
                {
                    donorBundle.Unload(false);
                }
            }
            catch (Exception exception)
            {
                LogAnimationFailureOnce(profile.Id + "-client-bundle-exception", $"Donor clip loading failed: {exception.Message}");
            }
        }

        private bool TryLoadDonorClipsFromBundle(DonorAnimationProfile profile, AssetBundle bundle)
        {
            if (bundle == null)
            {
                return false;
            }

            try
            {
                string[] animationAssetNames = bundle.GetAllAssetNames()
                    .Where(name => profile.AnimationAssetSuffixes.Any(suffix =>
                        NormalizePathForMatch(name).EndsWith(suffix, StringComparison.Ordinal)))
                    .ToArray();
                if (animationAssetNames.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < animationAssetNames.Length; i++)
                {
                    AssignDonorClips(profile, bundle.LoadAssetWithSubAssets<AnimationClip>(animationAssetNames[i]));
                    if (profile.FoldClip != null && profile.UnfoldClip != null)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void AssignDonorClips(DonorAnimationProfile profile, IEnumerable<AnimationClip> clips)
        {
            if (clips == null)
            {
                return;
            }

            foreach (AnimationClip clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                if (profile.FoldClip == null && string.Equals(clip.name, profile.FoldClipName, StringComparison.OrdinalIgnoreCase))
                {
                    profile.FoldClip = clip;
                }
                else if (profile.UnfoldClip == null && string.Equals(clip.name, profile.UnfoldClipName, StringComparison.OrdinalIgnoreCase))
                {
                    profile.UnfoldClip = clip;
                }
            }
        }

        // EFT operation internals are obfuscated, so resolve the weapon animator by stable
        // runtime field/property types instead of version-specific member names.
        private static Animator ResolveUnityAnimator(object operationState)
        {
            if (operationState == null)
            {
                return null;
            }

            try
            {
                object firearmController = GetInstanceFields(operationState.GetType())
                    .Where(field => string.Equals(field.FieldType.Name, "FirearmController", StringComparison.Ordinal))
                    .Select(field => field.GetValue(operationState))
                    .FirstOrDefault(value => value != null);
                object weaponPrefab = firearmController == null
                    ? null
                    : GetInstanceFields(firearmController.GetType())
                        .Where(field => string.Equals(field.FieldType.Name, "WeaponPrefab", StringComparison.Ordinal))
                        .Select(field => field.GetValue(firearmController))
                        .FirstOrDefault(value => value != null);
                PropertyInfo weaponAnimatorProperty = weaponPrefab?.GetType().GetProperty(
                    "Animator",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Animator weaponAnimator = UnwrapUnityAnimator(weaponAnimatorProperty?.GetValue(weaponPrefab, null));
                if (weaponAnimator != null)
                {
                    return weaponAnimator;
                }

                object firearmsAnimator = GetInstanceFields(operationState.GetType())
                    .Where(field => string.Equals(field.FieldType.Name, "FirearmsAnimator", StringComparison.Ordinal))
                    .Select(field => field.GetValue(operationState))
                    .FirstOrDefault(value => value != null);
                if (firearmsAnimator == null)
                {
                    return null;
                }

                PropertyInfo wrapperProperty = firearmsAnimator.GetType().GetProperty(
                    "Animator",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return UnwrapUnityAnimator(wrapperProperty?.GetValue(firearmsAnimator, null));
            }
            catch
            {
                return null;
            }
        }

        // The player root is used to find the rendered arm bones and FinalIK components,
        // which are separate from the lightweight transforms under the weapon Animator.
        private static Transform ResolvePlayerTransform(object operationState)
        {
            if (operationState == null)
            {
                return null;
            }

            try
            {
                Component player = GetInstanceFields(operationState.GetType())
                    .Where(field => string.Equals(field.FieldType.FullName, "EFT.Player", StringComparison.Ordinal))
                    .Select(field => field.GetValue(operationState) as Component)
                    .FirstOrDefault(value => value != null);
                return player?.transform;
            }
            catch
            {
                return null;
            }
        }

        private static Animator UnwrapUnityAnimator(object wrapper)
        {
            Animator directAnimator = wrapper as Animator;
            if (directAnimator != null)
            {
                return directAnimator;
            }

            PropertyInfo animatorProperty = wrapper?.GetType().GetProperty(
                "Animator",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Animator animator = animatorProperty?.GetValue(wrapper, null) as Animator;
            if (animator != null)
            {
                return animator;
            }

            FieldInfo animatorField = wrapper?.GetType().GetField(
                    "Animator_0",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return animatorField?.GetValue(wrapper) as Animator;
        }

        // Each visible item view owns its own stock controller. Holding the matching view
        // prevents its quaternion rotation from completing before the hand makes contact.
        private void HoldVisualStock(FoldableComponent foldable, bool targetFolded)
        {
            if (foldable == null)
            {
                return;
            }

            FoldThatStockVisualController[] controllers = FindObjectsOfType<FoldThatStockVisualController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i]?.HoldForAnimation(foldable, targetFolded);
            }
        }

        // Starts (or snaps) the custom stock pivot toward the logical folded state.
        internal void ReleaseVisualStock(FoldableComponent foldable, bool animate, float transitionSeconds)
        {
            if (foldable == null)
            {
                return;
            }

            FoldThatStockVisualController[] controllers = FindObjectsOfType<FoldThatStockVisualController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i]?.ReleaseAnimationHold(foldable, animate, transitionSeconds);
            }
        }

        // Retarget only configured weapon roots. AK-family roots keep the working UMP profile;
        // SIG/MP5/M700 roots route by the supported stock found in the item tree.
        private bool TryResolveDonorProfile(
            FoldOperation foldOperation,
            out DonorAnimationProfile profile,
            out VisualStockDefinition stockDefinition)
        {
            profile = null;
            stockDefinition = null;
            if (foldOperation?.Foldable?.Item == null)
            {
                return false;
            }

            try
            {
                Item rootItem = foldOperation.Foldable.Item.GetRootItem() ?? foldOperation.Foldable.Item;
                string rootTemplateId = GetTemplateId(rootItem);
                if (!TryFindItemTreeVisualDefinition(rootItem, out stockDefinition))
                {
                    return false;
                }

                if (string.Equals(stockDefinition.Id, "uas_ak_stock", StringComparison.OrdinalIgnoreCase))
                {
                    profile = _uziProDonor;
                    return true;
                }

                if (UasSksStockDefinitionIds.Contains(stockDefinition.Id))
                {
                    profile = _uziProDonor;
                    return true;
                }

                if (UziAnimatedAkTemplateIds.Contains(rootTemplateId))
                {
                    profile = _uziProDonor;
                    return true;
                }

                if (string.Equals(rootTemplateId, Sa58TemplateId, StringComparison.OrdinalIgnoreCase))
                {
                    profile = _umpDonor;
                    return true;
                }

                if (!StockRoutedAnimatedWeaponTemplateIds.Contains(rootTemplateId))
                {
                    return false;
                }

                if (string.Equals(
                    stockDefinition.Id,
                    "uzi_pro_a3_modular_folding_brace",
                    StringComparison.OrdinalIgnoreCase))
                {
                    profile = _uziProDonor;
                    return true;
                }

                profile = CollapseStockDefinitionIds.Contains(stockDefinition.Id)
                    ? _mp5Donor
                    : _aks74uDonor;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Replaces the animation-event callback that would normally end EFT's fold operation.
        // method_5 is the SPT 4.1.3 name; SwitchToIdle and the signature scan are fallbacks.
        private bool InvokeFoldOperationCompletion(object operationState)
        {
            if (operationState == null)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            MethodInfo completeMethod = operationState.GetType().GetMethod("method_5", flags, null, Type.EmptyTypes, null)
                ?? operationState.GetType().GetMethod("SwitchToIdle", flags, null, Type.EmptyTypes, null);
            if (completeMethod == null)
            {
                MethodInfo[] candidates = operationState.GetType()
                    .GetMethods(flags)
                    .Where(method => method.ReturnType == typeof(void)
                        && method.GetParameters().Length == 0
                        && !method.IsVirtual
                        && !method.IsSpecialName)
                    .ToArray();
                if (candidates.Length == 1)
                {
                    completeMethod = candidates[0];
                }
            }

            if (completeMethod == null)
            {
                Logger.LogWarning("Fold operation fallback skipped: completion method was not found.");
                return false;
            }

            try
            {
                completeMethod.Invoke(operationState, null);
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Logger.LogWarning($"Fold operation fallback failed: {exception.InnerException?.Message ?? exception.Message}");
                return false;
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Fold operation fallback failed: {exception.Message}");
                return false;
            }
        }

        private static IEnumerable<FieldInfo> GetInstanceFields(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    yield return field;
                }
            }
        }

        internal static FoldableComponent ResolveFoldableForVisualItem(Item item)
        {
            if (item == null)
            {
                return null;
            }

            FoldableComponent ownFoldable = item.GetItemComponent<FoldableComponent>();
            if (ownFoldable != null)
            {
                return ownFoldable;
            }

            try
            {
                Item rootItem = item.GetRootItem();
                if (rootItem == null)
                {
                    return null;
                }

                return rootItem.GetItemComponent<FoldableComponent>();
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryFindVisualDefinition(Transform transform, out VisualStockDefinition definition)
        {
            definition = null;
            if (transform == null)
            {
                return false;
            }

            string path = GetTransformPath(transform);
            foreach (VisualStockDefinition item in BuiltInVisualStockDefinitions)
            {
                if (item == null || !NameMatchesAnyPattern(transform.name, item.TargetBoneNamePatterns))
                {
                    continue;
                }

                string targetPathContains = string.IsNullOrWhiteSpace(item.VisualTargetPathContains)
                    ? item.StockPathContains
                    : item.VisualTargetPathContains;
                if (!PathContains(path, item.ContainerPathContains) || !PathContains(path, targetPathContains))
                {
                    continue;
                }

                definition = item;
                return true;
            }

            return false;
        }

        internal static bool ItemTreeContainsBuiltInDefinition(Item item)
        {
            VisualStockDefinition ignored;
            return TryFindItemTreeVisualDefinition(item, out ignored);
        }

        private static bool TryFindItemTreeVisualDefinition(Item item, out VisualStockDefinition definition)
        {
            definition = null;
            if (item == null)
            {
                return false;
            }

            try
            {
                Item rootItem = item.GetRootItem() ?? item;
                foreach (Item child in rootItem.GetAllItems())
                {
                    if (TryMatchBuiltInDefinition(child, out definition))
                    {
                        return true;
                    }
                }

                return TryMatchBuiltInDefinition(rootItem, out definition);
            }
            catch
            {
                return TryMatchBuiltInDefinition(item, out definition);
            }
        }

        internal static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static bool ContainsSupportedVisualTarget(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                VisualStockDefinition ignored;
                if (TryFindVisualDefinition(transforms[i], out ignored))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSupportedFoldOperation(FoldOperation foldOperation)
        {
            if (foldOperation == null || foldOperation.Foldable == null)
            {
                return false;
            }

            Item foldableItem = foldOperation.Foldable.Item;
            if (ItemTreeContainsBuiltInDefinition(foldableItem))
            {
                return true;
            }

            foreach (VisualStockDefinition definition in BuiltInVisualStockDefinitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.WeaponTemplateId))
                {
                    continue;
                }

                string templateId = GetTemplateId(foldableItem);
                if (!string.IsNullOrWhiteSpace(templateId)
                    && templateId.IndexOf(definition.WeaponTemplateId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ItemMatchesBuiltInDefinition(Item item)
        {
            VisualStockDefinition ignored;
            return TryMatchBuiltInDefinition(item, out ignored);
        }

        private static bool TryMatchBuiltInDefinition(Item item, out VisualStockDefinition matchedDefinition)
        {
            matchedDefinition = null;
            if (item == null)
            {
                return false;
            }

            string haystack = GetItemMatchText(item);
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            foreach (VisualStockDefinition definition in BuiltInVisualStockDefinitions)
            {
                if (definition == null)
                {
                    continue;
                }

                string templateId = GetTemplateId(item);
                bool exactTemplateMatch = !string.IsNullOrWhiteSpace(definition.TemplateId)
                    && string.Equals(templateId, definition.TemplateId, StringComparison.OrdinalIgnoreCase);
                bool pathMatch = !string.IsNullOrWhiteSpace(definition.StockPathContains)
                    && haystack.IndexOf(
                        NormalizePathForMatch(definition.StockPathContains),
                        StringComparison.OrdinalIgnoreCase) >= 0;
                if (exactTemplateMatch || pathMatch)
                {
                    matchedDefinition = definition;
                    return true;
                }
            }

            return false;
        }

        private static string GetItemMatchText(Item item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            if (item.Template != null)
            {
                parts.Add(item.Template._name);
                parts.Add(item.Template._id.ToString());
            }

            parts.Add(GetTemplateId(item));

            ResourceKey prefab = item.Prefab;
            if (prefab != null)
            {
                parts.Add(prefab.path);
                parts.Add(prefab.rcid);
                parts.Add(prefab.FileName);
                parts.Add(prefab.ToAssetName());
            }

            return NormalizePathForMatch(string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray()));
        }

        private static string GetTemplateId(Item item)
        {
            if (item == null)
            {
                return null;
            }

            try
            {
                return item.StringTemplateId;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetBuiltInBundleDefinition(string path, out VisualStockDefinition definition)
        {
            definition = null;
            foreach (VisualStockDefinition item in BuiltInVisualStockDefinitions)
            {
                if (item == null)
                {
                    continue;
                }

                if (BundlePathMatches(path, item.BundleSourcePathContains, item.BundleFileName)
                    && !string.IsNullOrWhiteSpace(item.BundleOverridePath))
                {
                    definition = item;
                    return true;
                }
            }

            return false;
        }

        private static bool BundlePathMatches(string path, string sourcePathContains, string fileName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalizedPath = NormalizePathForMatch(path);
            string normalizedSourceContains = NormalizePathForMatch(sourcePathContains ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(normalizedSourceContains) && normalizedPath.Contains(normalizedSourceContains))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string pathFileName = Path.GetFileName(path) ?? string.Empty;
            return string.Equals(pathFileName, fileName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveOverrideBundlePath(VisualStockDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.BundleOverridePath))
            {
                return null;
            }

            string configuredPath = definition.BundleOverridePath.Trim().Trim('"');
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            string pluginRootCandidate = Path.Combine(BepInEx.Paths.PluginPath, configuredPath);
            if (File.Exists(pluginRootCandidate))
            {
                return pluginRootCandidate;
            }

            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                string assemblySiblingCandidate = Path.Combine(assemblyDirectory, definition.BundleFileName ?? Path.GetFileName(configuredPath));
                if (File.Exists(assemblySiblingCandidate))
                {
                    return assemblySiblingCandidate;
                }
            }

            return pluginRootCandidate;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool PathContains(string path, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(path)
                && NormalizePathForMatch(path).IndexOf(NormalizePathForMatch(value), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizePathForMatch(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim().ToLowerInvariant();
        }

        private static bool NameMatchesAnyPattern(string name, string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(name) || patterns == null || patterns.Length == 0)
            {
                return false;
            }

            foreach (string pattern in patterns)
            {
                if (PatternMatches(name, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PatternMatches(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            pattern = pattern.Trim();
            if (pattern == "*")
            {
                return true;
            }

            bool startsWithWildcard = pattern.StartsWith("*", StringComparison.Ordinal);
            bool endsWithWildcard = pattern.EndsWith("*", StringComparison.Ordinal);
            string innerPattern = pattern.Trim('*');

            if (innerPattern.Length == 0)
            {
                return true;
            }

            if (startsWithWildcard && endsWithWildcard)
            {
                return value.IndexOf(innerPattern, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (startsWithWildcard)
            {
                return value.EndsWith(innerPattern, StringComparison.OrdinalIgnoreCase);
            }

            if (endsWithWildcard)
            {
                return value.StartsWith(innerPattern, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(value, innerPattern, StringComparison.OrdinalIgnoreCase);
        }

        [HarmonyPatch]
        private static class ContainerViewInsertItemPatch
        {
            private static MethodBase TargetMethod()
            {
                Type slotViewType = typeof(Item).Assembly.GetType("ContainerCollectionView+SlotView", false);
                MethodInfo insertItemMethod = slotViewType?.GetMethod(
                    "InsertItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    new[] { typeof(Item), typeof(GameObject) },
                    null);

                if (insertItemMethod == null || insertItemMethod.ReturnType != typeof(void))
                {
                    throw new MissingMethodException("FoldThatStock could not resolve ContainerCollectionView.SlotView.InsertItem(Item, GameObject).");
                }

                return insertItemMethod;
            }

            private static void Postfix(Item item, GameObject itemView)
            {
                Instance?.TryAttachVisualController(item, itemView);
            }
        }

        // Weapon/item inspection uses the synchronous clean-prefab path.
        [HarmonyPatch]
        private static class CleanLootPrefabPreviewPatch
        {
            private static MethodBase TargetMethod()
            {
                return ResolveCleanLootPrefabMethod(async: false);
            }

            private static void Postfix(Item __0, ref GameObject __result)
            {
                Instance?.RepairReversedStockPreview(__0, __result);
            }
        }

        // Inventory icon generation uses the asynchronous clean-prefab path and reads
        // PreviewPivot immediately after awaiting it, so return a task that repairs first.
        [HarmonyPatch]
        private static class CleanLootPrefabAsyncPreviewPatch
        {
            private static MethodBase TargetMethod()
            {
                return ResolveCleanLootPrefabMethod(async: true);
            }

            private static void Postfix(Item __0, ref Task<GameObject> __result)
            {
                FoldThatStockPlugin plugin = Instance;
                if (plugin == null || __result == null)
                {
                    return;
                }

                VisualStockDefinition definition;
                if (!TryMatchBuiltInDefinition(__0, out definition)
                    || definition == null
                    || !definition.CorrectReversedPreview)
                {
                    return;
                }

                __result = plugin.RepairReversedStockPreviewAsync(__0, __result);
            }
        }

        // EFT has exposed this service as both EFT.ObjectsFactory and PoolManagerClass
        // across recent builds. Resolve by method contract so either runtime name works.
        private static MethodBase ResolveCleanLootPrefabMethod(bool async)
        {
            Type assemblyType = typeof(Item);
            Type factoryType = assemblyType.Assembly.GetType("EFT.ObjectsFactory", false)
                ?? assemblyType.Assembly.GetType("PoolManagerClass", false);
            if (factoryType == null)
            {
                throw new MissingMethodException("FoldThatStock could not resolve EFT's clean-loot prefab factory type.");
            }

            Type expectedReturnType = async ? typeof(Task<GameObject>) : typeof(GameObject);
            int expectedParameterCount = async ? 2 : 3;
            MethodInfo method = AccessTools.GetDeclaredMethods(factoryType).SingleOrDefault(candidate =>
            {
                if (candidate.Name != (async ? "CreateCleanLootPrefabAsync" : "CreateCleanLootPrefab")
                    || candidate.ReturnType != expectedReturnType)
                {
                    return false;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == expectedParameterCount
                    && parameters[0].ParameterType == typeof(Item);
            });

            if (method == null)
            {
                throw new MissingMethodException(
                    $"FoldThatStock could not resolve {factoryType.FullName}."
                    + (async ? "CreateCleanLootPrefabAsync(Item, IPlayer)." : "CreateCleanLootPrefab(Item, ECameraType, IPlayer)."));
            }

            return method;
        }

        [HarmonyPatch]
        private static class AssetBundleLoadFromFilePatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodInfo[] methods =
                {
                    AccessTools.Method(typeof(AssetBundle), "LoadFromFile", new[] { typeof(string) }),
                    AccessTools.Method(typeof(AssetBundle), "LoadFromFile", new[] { typeof(string), typeof(uint) }),
                    AccessTools.Method(typeof(AssetBundle), "LoadFromFile", new[] { typeof(string), typeof(uint), typeof(ulong) }),
                    AccessTools.Method(typeof(AssetBundle), "LoadFromFileAsync", new[] { typeof(string) }),
                    AccessTools.Method(typeof(AssetBundle), "LoadFromFileAsync", new[] { typeof(string), typeof(uint) }),
                    AccessTools.Method(typeof(AssetBundle), "LoadFromFileAsync", new[] { typeof(string), typeof(uint), typeof(ulong) })
                };

                return methods.Where(method => method != null).Cast<MethodBase>();
            }

            private static void Prefix(ref string path)
            {
                Instance?.RedirectBundlePath(ref path);
            }
        }

        // Make EFT's normal fold eligibility host-aware without changing attachment filters or
        // the global BlocksFolding value on any stock template. Returning false only while the
        // component is unfolded suppresses folding but always leaves unfolding available.
        [HarmonyPatch(typeof(FoldableComponent), nameof(FoldableComponent.CanBeFolded), MethodType.Getter)]
        private static class UziAdapterFoldSuppressionPatch
        {
            private static void Postfix(FoldableComponent __instance, ref bool __result)
            {
                if (__result && Instance?.ShouldSuppressUziAdapterFolding(__instance) == true)
                {
                    __result = false;
                }
            }
        }

        // Ignore only the in-raid fold hotkey while this weapon's donor overlay is active.
        // In particular, this covers the 0.30-second final handoff after EFT has already
        // returned to idle and would otherwise accept a second, unanimated fold operation.
        [HarmonyPatch(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.TranslateCommand))]
        private static class FoldStockInputGuardPatch
        {
            private static readonly FieldInfo ControllerField = typeof(FirearmHandsInputTranslator)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(field => field.FieldType == typeof(IFirearmHandsController));

            private static bool Prefix(
                FirearmHandsInputTranslator __instance,
                EFT.InputSystem.ECommand command,
                ref EFT.InputSystem.InputNode.ETranslateResult __result)
            {
                if (command != EFT.InputSystem.ECommand.FoldStock || Instance == null || ControllerField == null)
                {
                    return true;
                }

                try
                {
                    IFirearmHandsController controller = ControllerField.GetValue(__instance) as IFirearmHandsController;
                    if (controller == null || !Instance.IsFoldInputLocked(controller.Item))
                    {
                        return true;
                    }

                    __result = EFT.InputSystem.InputNode.ETranslateResult.Ignore;
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }

        // Intercepts the operation after EFT has created it, then preserves native completion,
        // starts the selected donor overlay, or uses the instant fallback as appropriate.
        [HarmonyPatch]
        private static class FoldOperationStartPatch
        {
            private static MethodBase TargetMethod()
            {
                return ResolveFoldStockOperationMethod(IsFoldStockStartMethod, "Start(FoldOperation, Callback)");
            }

            private static void Postfix(object __instance, object[] __args)
            {
                FoldOperation foldOperation = __args != null && __args.Length > 0
                    ? __args[0] as FoldOperation
                    : null;

                Instance?.CompleteFoldOperationIfSupported(__instance, foldOperation);
            }
        }

        // Prevents the native animation event from racing the donor overlay's lifecycle.
        [HarmonyPatch]
        private static class FoldOperationOnFoldPatch
        {
            private static MethodBase TargetMethod()
            {
                return ResolveFoldStockOperationMethod(IsFoldStockOnFoldMethod, "OnFold(bool)");
            }

            private static bool Prefix(object __instance)
            {
                return Instance == null || Instance.ShouldAllowFoldAnimationEvent(__instance);
            }
        }

        private static MethodInfo ResolveFoldStockOperationMethod(Func<MethodInfo, bool> predicate, string description)
        {
            Type[] operationTypes = typeof(Player.FirearmController)
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(type => AccessTools.GetDeclaredMethods(type).Any(IsFoldStockStartMethod))
                .ToArray();

            if (operationTypes.Length != 1)
            {
                throw new MissingMethodException(
                    $"FoldThatStock expected one firearm fold operation type, but found {operationTypes.Length}.");
            }

            MethodInfo[] methods = AccessTools.GetDeclaredMethods(operationTypes[0])
                .Where(predicate)
                .ToArray();

            if (methods.Length != 1)
            {
                throw new MissingMethodException(
                    $"FoldThatStock expected one {description} method on {operationTypes[0].FullName}, but found {methods.Length}.");
            }

            return methods[0];
        }

        private static bool IsFoldStockStartMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "Start"
                && method.ReturnType == typeof(void)
                && parameters.Length == 2
                && parameters[0].ParameterType == typeof(FoldOperation)
                && parameters[1].ParameterType.FullName == "Comfort.Common.Callback";
        }

        private static bool IsFoldStockOnFoldMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "OnFold"
                && method.ReturnType == typeof(void)
                && parameters.Length == 1
                && parameters[0].ParameterType == typeof(bool);
        }
    }

    // Instance marker that keeps the preview correction idempotent when the same prefab
    // passes through both preview construction and another clean-prefab consumer.
    internal sealed class FoldThatStockPreviewPivotRepair : MonoBehaviour
    {
    }

    /// <summary>
    /// Owns the custom pivot state for one rendered stock view. Inventory views snap to the
    /// logical state; the first-person donor overlay explicitly requests timed transitions.
    /// </summary>
    public sealed class FoldThatStockVisualController : MonoBehaviour, IDress
    {
        internal const float DefaultTransitionSeconds = 0.12f;
        private const float NativeAnimationTimeoutSeconds = 3f;

        private sealed class TargetState
        {
            public Transform Transform;
            public FoldThatStockPlugin.VisualStockDefinition Definition;
            public bool HasPositionState;
            public Vector3 UnfoldedPosition;
            public Quaternion UnfoldedRotation;
            public Quaternion FoldedRotation;
            public bool HasVisualFolded;
            public bool VisualFolded;
            public bool TweenActive;
            public Vector3 TweenPositionFrom;
            public Vector3 TweenPositionTo;
            public Quaternion TweenFrom;
            public Quaternion TweenTo;
            public float TweenStartedAt;
            public float TweenDuration;
        }

        private readonly List<TargetState> _targets = new List<TargetState>();
        private FoldableComponent _foldable;
        private Action _unbind;
        private bool _animationHold;
        private bool _heldFolded;
        private bool _nativeAnimationInProgress;
        private float _nativeAnimationTimeoutAt;

        public void Init(Item item, bool isAnimated)
        {
            Bind(item);
        }

        public void Deinit()
        {
            if (_unbind != null)
            {
                _unbind();
                _unbind = null;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                TargetState target = _targets[i];
                if (target != null && target.Transform != null)
                {
                    if (target.HasPositionState)
                    {
                        target.Transform.localPosition = target.UnfoldedPosition;
                    }

                    target.Transform.localRotation = target.UnfoldedRotation;
                }
            }

            _targets.Clear();
            _foldable = null;
            _animationHold = false;
            _nativeAnimationInProgress = false;
            _nativeAnimationTimeoutAt = 0f;
        }

        public bool Bind(Item item)
        {
            Deinit();

            _foldable = FoldThatStockPlugin.ResolveFoldableForVisualItem(item);
            if (_foldable == null)
            {
                return false;
            }

            RegisterTargets();
            if (_targets.Count == 0)
            {
                _foldable = null;
                return false;
            }

            _unbind = _foldable.OnChanged.Subscribe(new Action(OnFoldChanged));
            ApplyStateForFolded(_foldable.Folded, false, DefaultTransitionSeconds);

            FoldThatStockPlugin.Instance?.LogVisualBinding(
                item?.Id ?? GetInstanceID().ToString(CultureInfo.InvariantCulture),
                $"Bound FoldThatStock visual controller to {item} with {_targets.Count} target(s).");

            return true;
        }

        private void OnDestroy()
        {
            Deinit();
        }

        private void LateUpdate()
        {
            if (_foldable == null || _targets.Count == 0)
            {
                return;
            }

            if (_nativeAnimationInProgress)
            {
                if (Time.realtimeSinceStartup < _nativeAnimationTimeoutAt)
                {
                    return;
                }

                _nativeAnimationInProgress = false;
            }

            ApplyStateForFolded(_animationHold ? _heldFolded : _foldable.Folded, false, DefaultTransitionSeconds);
        }

        private void OnFoldChanged()
        {
            _nativeAnimationInProgress = false;

            // Preserve EFT's instant inventory-preview behavior. In-raid animation timing is
            // supplied explicitly through ReleaseAnimationHold rather than this event.
            ApplyStateForFolded(
                _animationHold ? _heldFolded : _foldable.Folded,
                false,
                DefaultTransitionSeconds);
        }

        internal void BeginNativeAnimation(FoldableComponent foldable)
        {
            if (_foldable == null || !ReferenceEquals(_foldable, foldable))
            {
                return;
            }

            // Let EFT's native UZI clip own the A3 pivot during the gesture. When the logical
            // folded state changes, OnFoldChanged applies the configured final Euler rotation.
            _nativeAnimationInProgress = true;
            _nativeAnimationTimeoutAt = Time.realtimeSinceStartup + NativeAnimationTimeoutSeconds;
        }

        internal void HoldForAnimation(FoldableComponent foldable, bool targetFolded)
        {
            if (_foldable == null || !ReferenceEquals(_foldable, foldable))
            {
                return;
            }

            _animationHold = true;
            _heldFolded = !targetFolded;
            ApplyStateForFolded(_heldFolded, false, DefaultTransitionSeconds);
        }

        internal void ReleaseAnimationHold(FoldableComponent foldable, bool animate, float transitionSeconds)
        {
            if (_foldable == null || !ReferenceEquals(_foldable, foldable))
            {
                return;
            }

            _animationHold = false;
            ApplyStateForFolded(_foldable.Folded, animate, transitionSeconds);
        }

        private void RegisterTargets()
        {
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                FoldThatStockPlugin.VisualStockDefinition definition;
                if (!FoldThatStockPlugin.TryFindVisualDefinition(transforms[i], out definition))
                {
                    continue;
                }

                Quaternion unfoldedRotation = transforms[i].localRotation;
                Quaternion foldedRotation = definition.KeepUnfoldedRotation
                    ? unfoldedRotation
                    : definition.HasFoldedRotation
                    ? definition.FoldedRotation
                    : FoldThatStockPlugin.DefaultFoldedRotation;

                _targets.Add(new TargetState
                {
                    Transform = transforms[i],
                    Definition = definition,
                    HasPositionState = definition.HasRetractedPosition,
                    UnfoldedPosition = transforms[i].localPosition,
                    UnfoldedRotation = unfoldedRotation,
                    FoldedRotation = foldedRotation
                });
            }
        }

        private void ApplyStateForFolded(bool folded, bool animateChangedState, float transitionSeconds)
        {
            float now = Time.realtimeSinceStartup;

            for (int i = 0; i < _targets.Count; i++)
            {
                TargetState target = _targets[i];
                if (target == null || target.Transform == null)
                {
                    continue;
                }

                Quaternion targetRotation = folded ? target.FoldedRotation : target.UnfoldedRotation;
                Vector3 targetPosition = GetTargetPosition(target, folded);
                if (!target.HasVisualFolded || target.VisualFolded != folded)
                {
                    target.HasVisualFolded = true;
                    target.VisualFolded = folded;

                    if (animateChangedState)
                    {
                        target.TweenActive = true;
                        target.TweenPositionFrom = target.Transform.localPosition;
                        target.TweenPositionTo = targetPosition;
                        target.TweenFrom = target.Transform.localRotation;
                        target.TweenTo = targetRotation;
                        target.TweenStartedAt = now;
                        target.TweenDuration = Mathf.Max(0.001f, transitionSeconds);
                    }
                    else
                    {
                        target.TweenActive = false;
                    }
                }

                if (target.TweenActive)
                {
                    float t = Mathf.Clamp01((now - target.TweenStartedAt) / target.TweenDuration);
                    if (target.HasPositionState)
                    {
                        target.Transform.localPosition = Vector3.Lerp(target.TweenPositionFrom, target.TweenPositionTo, t);
                    }

                    target.Transform.localRotation = Quaternion.Slerp(target.TweenFrom, target.TweenTo, t);
                    if (t >= 1f)
                    {
                        target.TweenActive = false;
                        if (target.HasPositionState)
                        {
                            target.Transform.localPosition = targetPosition;
                        }

                        target.Transform.localRotation = targetRotation;
                    }
                }
                else
                {
                    if (target.HasPositionState)
                    {
                        target.Transform.localPosition = targetPosition;
                    }

                    target.Transform.localRotation = targetRotation;
                }
            }
        }

        private static Vector3 GetTargetPosition(TargetState target, bool folded)
        {
            if (target == null || !target.HasPositionState || !folded)
            {
                return target != null ? target.UnfoldedPosition : Vector3.zero;
            }

            return target.Definition != null
                ? target.Definition.RetractedPosition
                : target.UnfoldedPosition;
        }
    }
}
