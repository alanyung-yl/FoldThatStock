using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace FoldThatStock
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class FoldThatStockPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.foldthatstock";
        public const string PluginName = "FoldThatStock";
        public const string PluginVersion = "0.3.0";

        internal static FoldThatStockPlugin Instance { get; private set; }

        internal static readonly Quaternion DefaultFoldedRotation = new Quaternion(0f, 0.7071068f, 0.7071068f, 0f);

        internal sealed class VisualStockDefinition
        {
            public string Id;
            public string DisplayName;
            public string WeaponTemplateId = string.Empty;
            public string ContainerPathContains = string.Empty;
            public string StockPathContains;
            public string[] TargetBoneNamePatterns;
            public bool HasFoldedRotation;
            public Quaternion FoldedRotation;
            public string BundleFileName;
            public string BundleSourcePathContains;
            public string BundleOverridePath;
        }

        internal static readonly VisualStockDefinition[] BuiltInVisualStockDefinitions =
        {
            new VisualStockDefinition
            {
                Id = "sig_thin_folding_stock",
                DisplayName = "SIG thin folding stock",
                StockPathContains = "stock_all_sig_thin_folding_stock",
                TargetBoneNamePatterns = new[] { "mod_stock_folding" },
                HasFoldedRotation = true,
                FoldedRotation = new Quaternion(0f, 0.7071068f, 0.7071068f, 0f),
                BundleFileName = "stock_all_sig_thin_folding_stock.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_thin_folding_stock.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_thin_folding_stock.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_folding_knuckle",
                DisplayName = "SIG folding knuckle",
                StockPathContains = "stock_all_sig_folding_knuckle",
                TargetBoneNamePatterns = new[] { "stk_rt" },
                BundleFileName = "stock_all_sig_folding_knuckle.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_folding_knuckle.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_folding_knuckle.bundle")
            },
            new VisualStockDefinition
            {
                Id = "mpx_pmm_ulss_stock",
                DisplayName = "PMM ULSS stock",
                StockPathContains = "stock_mpx_pmm_ulss",
                TargetBoneNamePatterns = new[] { "mod_stock" },
                BundleFileName = "stock_mpx_pmm_ulss.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_mpx_pmm_ulss.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_mpx_pmm_ulss.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_telescoping_stock",
                DisplayName = "SIG telescoping stock",
                StockPathContains = "stock_all_sig_telescoping_stock",
                TargetBoneNamePatterns = new[] { "mod_stock" },
                BundleFileName = "stock_all_sig_telescoping_stock.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_telescoping_stock.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_telescoping_stock.bundle")
            },
            new VisualStockDefinition
            {
                Id = "sig_stock_locking_hinge_assembly",
                DisplayName = "SIG stock locking hinge assembly",
                StockPathContains = "stock_all_sig_stock_locking_hinge_assembly",
                TargetBoneNamePatterns = new[] { "mod_stock_001" },
                BundleFileName = "stock_all_sig_stock_locking_hinge_assembly.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_all_sig_stock_locking_hinge_assembly.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_all_sig_stock_locking_hinge_assembly.bundle")
            },
            new VisualStockDefinition
            {
                Id = "ak_utg_sfs_adapter",
                DisplayName = "UTG SFS AK adapter",
                StockPathContains = "stock_ak_utg_sfs_adapter",
                TargetBoneNamePatterns = new[] { "mod_stock_001" },
                BundleFileName = "stock_ak_utg_sfs_adapter.bundle",
                BundleSourcePathContains = "assets/content/items/mods/stocks/stock_ak_utg_sfs_adapter.bundle",
                BundleOverridePath = Path.Combine("FoldThatStock", "stock_ak_utg_sfs_adapter.bundle")
            }
        };

        private readonly HashSet<string> _loggedMissingBundlePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedRedirects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loggedVisualBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(FoldThatStockPlugin).Assembly);
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        internal void TryAttachVisualController(Item item, GameObject itemView)
        {
            if (item == null || itemView == null || !ContainsSupportedVisualTarget(itemView.transform))
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

        internal void CompleteFoldOperationIfSupported(object operationState, FoldOperationClass foldOperation)
        {
            if (operationState == null || !IsSupportedFoldOperation(foldOperation))
            {
                return;
            }

            MethodInfo completeMethod = AccessTools.Method(operationState.GetType(), "method_5", Type.EmptyTypes);
            if (completeMethod == null)
            {
                Logger.LogWarning("Fold operation fallback skipped: completion method was not found.");
                return;
            }

            try
            {
                completeMethod.Invoke(operationState, null);
            }
            catch (TargetInvocationException exception)
            {
                Logger.LogWarning($"Fold operation fallback failed: {exception.InnerException?.Message ?? exception.Message}");
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Fold operation fallback failed: {exception.Message}");
            }
        }

        internal bool ShouldAllowFoldAnimationEvent(object operationState)
        {
            if (operationState == null)
            {
                return true;
            }

            FieldInfo foldOperationField = AccessTools.Field(operationState.GetType(), "FoldOperationClass");
            if (foldOperationField == null)
            {
                return true;
            }

            try
            {
                return foldOperationField.GetValue(operationState) != null;
            }
            catch
            {
                return true;
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
                Item rootItem = global::GClass3380.GetRootItem(item);
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

                if (!PathContains(path, item.ContainerPathContains) || !PathContains(path, item.StockPathContains))
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
            if (item == null)
            {
                return false;
            }

            try
            {
                Item rootItem = global::GClass3380.GetRootItem(item) ?? item;
                foreach (Item child in global::GClass3380.GetAllItems(rootItem))
                {
                    if (ItemMatchesBuiltInDefinition(child))
                    {
                        return true;
                    }
                }

                return ItemMatchesBuiltInDefinition(rootItem);
            }
            catch
            {
                return ItemMatchesBuiltInDefinition(item);
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

        internal static string FormatQuaternion(Quaternion quaternion)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.00000000}, {1:0.00000000}, {2:0.00000000}, {3:0.00000000})",
                quaternion.x,
                quaternion.y,
                quaternion.z,
                quaternion.w);
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

        private static bool IsSupportedFoldOperation(FoldOperationClass foldOperation)
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
                if (definition == null || string.IsNullOrWhiteSpace(definition.StockPathContains))
                {
                    continue;
                }

                if (haystack.IndexOf(NormalizePathForMatch(definition.StockPathContains), StringComparison.OrdinalIgnoreCase) >= 0)
                {
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

        [HarmonyPatch(typeof(global::GClass768.GClass769), "InsertItem")]
        private static class ContainerViewInsertItemPatch
        {
            private static void Postfix(Item item, GameObject itemView)
            {
                Instance?.TryAttachVisualController(item, itemView);
            }
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

        [HarmonyPatch]
        private static class FoldOperationStartPatch
        {
            private static MethodBase TargetMethod()
            {
                Type operationType = AccessTools.TypeByName("EFT.Player+FirearmController+Class1269");
                if (operationType == null)
                {
                    return null;
                }

                return AccessTools.GetDeclaredMethods(operationType)
                    .FirstOrDefault(method => method.Name == "Start" && method.GetParameters().Length == 2);
            }

            private static void Postfix(object __instance, object[] __args)
            {
                FoldOperationClass foldOperation = __args != null && __args.Length > 0
                    ? __args[0] as FoldOperationClass
                    : null;

                Instance?.CompleteFoldOperationIfSupported(__instance, foldOperation);
            }
        }

        [HarmonyPatch]
        private static class FoldOperationOnFoldPatch
        {
            private static MethodBase TargetMethod()
            {
                Type operationType = AccessTools.TypeByName("EFT.Player+FirearmController+Class1269");
                if (operationType == null)
                {
                    return null;
                }

                return AccessTools.GetDeclaredMethods(operationType)
                    .FirstOrDefault(method => method.Name == "OnFold" && method.GetParameters().Length == 1);
            }

            private static bool Prefix(object __instance)
            {
                return Instance == null || Instance.ShouldAllowFoldAnimationEvent(__instance);
            }
        }
    }

    public sealed class FoldThatStockVisualController : MonoBehaviour, GInterface236
    {
        private const float TransitionSeconds = 0.12f;

        private sealed class TargetState
        {
            public Transform Transform;
            public Quaternion UnfoldedRotation;
            public Quaternion FoldedRotation;
            public bool HasVisualFolded;
            public bool VisualFolded;
            public bool TweenActive;
            public Quaternion TweenFrom;
            public Quaternion TweenTo;
            public float TweenStartedAt;
        }

        private readonly List<TargetState> _targets = new List<TargetState>();
        private FoldableComponent _foldable;
        private Action _unbind;

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
                    target.Transform.localRotation = target.UnfoldedRotation;
                }
            }

            _targets.Clear();
            _foldable = null;
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
            ApplyState(false);

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

            ApplyState(false);
        }

        private void OnFoldChanged()
        {
            ApplyState(true);
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

                Quaternion foldedRotation = definition.HasFoldedRotation
                    ? definition.FoldedRotation
                    : FoldThatStockPlugin.DefaultFoldedRotation;

                _targets.Add(new TargetState
                {
                    Transform = transforms[i],
                    UnfoldedRotation = transforms[i].localRotation,
                    FoldedRotation = foldedRotation
                });
            }
        }

        private void ApplyState(bool animateChangedState)
        {
            bool folded = _foldable != null && _foldable.Folded;
            float now = Time.realtimeSinceStartup;

            for (int i = 0; i < _targets.Count; i++)
            {
                TargetState target = _targets[i];
                if (target == null || target.Transform == null)
                {
                    continue;
                }

                Quaternion targetRotation = folded ? target.FoldedRotation : target.UnfoldedRotation;
                if (!target.HasVisualFolded || target.VisualFolded != folded)
                {
                    target.HasVisualFolded = true;
                    target.VisualFolded = folded;

                    if (animateChangedState)
                    {
                        target.TweenActive = true;
                        target.TweenFrom = target.Transform.localRotation;
                        target.TweenTo = targetRotation;
                        target.TweenStartedAt = now;
                    }
                    else
                    {
                        target.TweenActive = false;
                    }
                }

                if (target.TweenActive)
                {
                    float t = Mathf.Clamp01((now - target.TweenStartedAt) / TransitionSeconds);
                    target.Transform.localRotation = Quaternion.Slerp(target.TweenFrom, target.TweenTo, t);
                    if (t >= 1f)
                    {
                        target.TweenActive = false;
                        target.Transform.localRotation = targetRotation;
                    }
                }
                else
                {
                    target.Transform.localRotation = targetRotation;
                }
            }
        }
    }
}
