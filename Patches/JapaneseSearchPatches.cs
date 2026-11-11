using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using IPA.Loader;

namespace JapaneseIME.Patches
{
    /// <summary>
    /// Makes the base game song search understand Japanese.
    ///
    /// The target type and methods are looked up by name instead of referencing
    /// the game assemblies directly. BetterSongList is also optional: if its
    /// search patch is loaded it is guarded dynamically, and if it is absent no
    /// BetterSongList assembly is required.
    /// </summary>
    internal static class JapaneseSearchPatcher
    {
        private const string LevelFilterTypeName = "LevelFilter";
        private const string CleanTextMethodName = "CleanText";
        private const string MatchScoreMethodName = "CalculateMatchScore";

        private const string BetterSongListPluginId = "BetterSongList";
        private const string BetterSongListAssemblyName = "BetterSongList";
        private const string BetterSongListSearchPatchType =
            "BetterSongList.HarmonyPatches.ImproveBasegameSearch";

        private static Harmony _harmony;
        private static bool _betterSongListHooked;
        private static bool _assemblyLoadHooked;

        internal static void Install(Harmony harmony)
        {
            _harmony = harmony;

            try
            {
                InstallCore();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME: Japanese song search setup failed: " + ex);
            }
        }

        private static void InstallCore()
        {
            Type levelFilter = FindLevelFilterType();
            if (levelFilter == null)
            {
                Plugin.Log?.Warn(
                    "JapaneseIME: LevelFilter type was not found; Japanese song search is unavailable.");
                return;
            }

            MethodInfo cleanText = FindMethod(levelFilter, CleanTextMethodName, typeof(string));
            if (cleanText == null || cleanText.ReturnType != typeof(string))
            {
                Plugin.Log?.Warn(
                    "JapaneseIME: LevelFilter.CleanText was not found; Japanese song search is unavailable.");
                return;
            }

            Patch(cleanText, nameof(CleanTextPrefix), null, "LevelFilter.CleanText");

            // BetterSongList and other mods patch FilterLevelByText during startup.
            // That can JIT the method (and inline CleanText) before this plugin is
            // loaded, so replace its CleanText calls in the IL as well.
            MethodInfo filterLevelByText = FindFilterLevelByText(levelFilter);
            if (filterLevelByText != null)
            {
                PatchTranspiler(
                    filterLevelByText, nameof(CleanTextTranspiler),
                    "LevelFilter.FilterLevelByText");
            }

            PatchFilterLevelsStateMachines(levelFilter);

            // A one-character match in the middle of a title scores exactly 50 and
            // the game keeps only scores above 50. Nudge that case so partial
            // matches, which are common for Japanese titles, are not dropped.
            MethodInfo matchScore = FindMethodContaining(
                levelFilter, MatchScoreMethodName, typeof(int), typeof(string), typeof(string[]));
            if (matchScore != null)
            {
                Patch(matchScore, null, nameof(MatchScorePostfix), "LevelFilter.CalculateMatchScore");
            }

            InstallBetterSongListGuard();
        }

        internal static void Uninstall()
        {
            if (_assemblyLoadHooked)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                _assemblyLoadHooked = false;
            }
        }

        private static void Patch(MethodInfo target, string prefixName, string postfixName, string label)
        {
            try
            {
                HarmonyMethod prefix = prefixName == null
                    ? null
                    : new HarmonyMethod(GetPatchMethod(prefixName));
                HarmonyMethod postfix = postfixName == null
                    ? null
                    : new HarmonyMethod(GetPatchMethod(postfixName));

                _harmony.Patch(target, prefix: prefix, postfix: postfix);
                Plugin.Log?.Info("JapaneseIME: Japanese search patch applied to " + label + ".");
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(
                    "JapaneseIME: Japanese search patch failed for " + label + ": " + ex);
            }
        }

        private static void PatchTranspiler(MethodInfo target, string transpilerName, string label)
        {
            try
            {
                _harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(GetPatchMethod(transpilerName)));
                Plugin.Log?.Info(
                    "JapaneseIME: Japanese search patch applied to " + label + ".");
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(
                    "JapaneseIME: Japanese search patch failed for " + label + ": " + ex);
            }
        }

        private static MethodInfo GetPatchMethod(string name)
        {
            return typeof(JapaneseSearchPatcher).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic);
        }

        // Replaces LevelFilter.CleanText with JapaneseSearchText.Normalize in the
        // search methods. FilterLevelByText is often JIT-compiled early by
        // BetterSongList, so patching only CleanText would miss an inlined call.
        // The FilterLevelsAsync state machine cleans the query in MoveNext.
        private static IEnumerable<CodeInstruction> CleanTextTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo normalize = typeof(JapaneseSearchText).GetMethod(
                nameof(JapaneseSearchText.Normalize),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (normalize == null)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    yield return instruction;
                }

                yield break;
            }

            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo called = instruction.operand as MethodInfo;
                if (instruction.opcode == OpCodes.Call &&
                    called != null &&
                    called.Name == CleanTextMethodName &&
                    called.DeclaringType != null &&
                    called.DeclaringType.FullName == LevelFilterTypeName)
                {
                    instruction.operand = normalize;
                    replaced++;
                }

                yield return instruction;
            }

            if (replaced == 0)
            {
                Plugin.Log?.Warn(
                    "JapaneseIME: A search method did not contain a CleanText call.");
            }
        }

        // Replaces the game's ASCII-only CleanText with a version that keeps
        // Japanese characters and normalizes kana.
        private static bool CleanTextPrefix(string s, ref string __result)
        {
            try
            {
                __result = JapaneseSearchText.Normalize(s);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME: Japanese search normalization failed: " + ex);
                return true;
            }
        }

        private static void MatchScorePostfix(ref int __result)
        {
            if (__result == 50)
            {
                __result = 51;
            }
        }

        private static void InstallBetterSongListGuard()
        {
            if (TryPatchBetterSongListGuard())
            {
                return;
            }

            // BetterSongList may be loaded after this plugin. Watch for it so
            // the optional compatibility patch is still applied.
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            _assemblyLoadHooked = true;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            string name;
            try
            {
                name = args.LoadedAssembly.GetName().Name;
            }
            catch (Exception)
            {
                return;
            }

            if (name != BetterSongListAssemblyName)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            _assemblyLoadHooked = false;
            TryPatchBetterSongListGuard();
        }

        private static bool TryPatchBetterSongListGuard()
        {
            Assembly assembly = FindBetterSongListAssembly();
            if (assembly == null)
            {
                return false;
            }

            if (_betterSongListHooked)
            {
                return true;
            }

            Type patchType = assembly.GetType(BetterSongListSearchPatchType, false);
            MethodInfo prefix = patchType?.GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prefix == null)
            {
                Plugin.Log?.Warn(
                    "JapaneseIME: BetterSongList search patch was not found; " +
                    "multi-term Japanese search may be limited.");
                return true;
            }

            try
            {
                _harmony.Patch(
                    prefix,
                    prefix: new HarmonyMethod(GetPatchMethod(nameof(BetterSongListPrefixGuard))));
                _betterSongListHooked = true;
                Plugin.Log?.Info(
                    "JapaneseIME: BetterSongList search compatibility patch applied.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(
                    "JapaneseIME: BetterSongList search compatibility patch failed: " + ex);
            }

            return true;
        }

        // BetterSongList joins multiple search terms into one phrase before the
        // game matches them. Japanese queries are often separated by spaces
        // (including the full-width space, which the normalizer turns into a
        // plain space), and a joined phrase then matches no title. Let the game
        // match each Japanese term separately instead.
        private static bool BetterSongListPrefixGuard(ref string[] searchTerms)
        {
            if (searchTerms == null || searchTerms.Length < 2)
            {
                return true;
            }

            foreach (string term in searchTerms)
            {
                if (JapaneseSearchText.ContainsJapanese(term))
                {
                    return false;
                }
            }

            return true;
        }

        private static Assembly FindBetterSongListAssembly()
        {
            try
            {
                PluginMetadata plugin = PluginManager.GetPluginFromId(BetterSongListPluginId);
                if (plugin?.Assembly != null)
                {
                    return plugin.Assembly;
                }
            }
            catch (Exception)
            {
                // Fall back to scanning loaded assemblies below.
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetName().Name == BetterSongListAssemblyName)
                    {
                        return assembly;
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        private static Type FindLevelFilterType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName;
                try
                {
                    assemblyName = assembly.GetName().Name;
                }
                catch (Exception)
                {
                    continue;
                }

                if (assemblyName != "DataModels" && assemblyName != "Main")
                {
                    continue;
                }

                Type type;
                try
                {
                    type = assembly.GetType(LevelFilterTypeName, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (IsLevelFilter(type))
                {
                    return type;
                }
            }

            Type fallback = AccessTools.TypeByName(LevelFilterTypeName);
            return IsLevelFilter(fallback) ? fallback : null;
        }

        private static bool IsLevelFilter(Type type)
        {
            return type != null &&
                   type.FullName == LevelFilterTypeName &&
                   FindMethod(type, CleanTextMethodName, typeof(string)) != null;
        }

        private static MethodInfo FindMethod(Type type, string name, params Type[] parameterTypes)
        {
            try
            {
                return type.GetMethod(
                    name,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    parameterTypes,
                    null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void PatchFilterLevelsStateMachines(Type levelFilter)
        {
            foreach (Type nested in levelFilter.GetNestedTypes(
                         BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.Name.IndexOf("FilterLevelsAsync", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                MethodInfo moveNext = FindStateMachineMoveNext(nested);
                if (moveNext != null)
                {
                    PatchTranspiler(
                        moveNext, nameof(CleanTextTranspiler),
                        "LevelFilter." + nested.Name + ".MoveNext");
                }
            }
        }

        private static MethodInfo FindStateMachineMoveNext(Type stateMachine)
        {
            foreach (MethodInfo method in stateMachine.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name.EndsWith("MoveNext", StringComparison.Ordinal) &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 0)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo FindFilterLevelByText(Type levelFilter)
        {
            foreach (MethodInfo method in levelFilter.GetMethods(
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "FilterLevelByText")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[1].ParameterType == typeof(string[]))
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo FindMethodContaining(
            Type type, string namePart, Type returnType, params Type[] parameterTypes)
        {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name.IndexOf(namePart, StringComparison.Ordinal) < 0 ||
                    method.ReturnType != returnType)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                {
                    continue;
                }

                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType != parameterTypes[i])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return method;
                }
            }

            return null;
        }
    }
}
