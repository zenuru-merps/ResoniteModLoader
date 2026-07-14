using FrooxEngine;
using HarmonyLib;

namespace ResoniteModLoader;

static class SettingsDataFeedPatcher {
	private static MethodInfo ModListMethod;

	internal static void RunPatch(Harmony harmony) {
		var patchMethod = AccessTools.DeclaredMethod(typeof(SettingsDataFeedPatcher), nameof(InjectModGrid));
		var targetMethod = AccessTools.DeclaredMethod(typeof(SettingsDataFeed), "GenerateItem",
			[typeof(SettingsDataFeed.ActionIdentity), typeof(SettingPropertyAttribute), typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>)]);
		ModListMethod = AccessTools.DeclaredMethod(typeof(ModSettings), nameof(ModSettings.ModList));
		var patched = harmony.Patch(targetMethod, prefix: new HarmonyMethod(patchMethod));
		Logger.DebugInternal("SettingsDataFeedPatcher success! " + patched);
	}

	internal static bool InjectModGrid(ref DataFeedItem __result, SettingsDataFeed.ActionIdentity identity, IReadOnlyList<string> path, IReadOnlyList<string> grouping) {
		if (identity.settingType == typeof(ModSettings) && identity.MemberName == nameof(ModSettings.ModList)) {
			var activeSetting = Settings.GetActiveSetting<ModSettings>();
			__result = activeSetting!.GenerateModGrid(path, grouping);
			return false;
		}

		return true;
	}

	/*
	internal static void RunPatch(Harmony harmony) {
		var transpilerMethod = AccessTools.DeclaredMethod(typeof(SettingsDataFeedPatcher), nameof(EnumerableTranspiler));
		var targetMethod = AccessTools.DeclaredMethod(typeof(SettingsDataFeed), nameof(SettingsDataFeed.Enumerate));
		var stateMachineAttr = targetMethod.GetCustomAttribute<AsyncIteratorStateMachineAttribute>();
		var moveNextMethod = AccessTools.Method(stateMachineAttr.StateMachineType, "MoveNext");

		harmony.Patch(moveNextMethod, transpiler: new HarmonyMethod(transpilerMethod));
	}

	static IEnumerable<CodeInstruction> EnumerableTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original) {
		MethodBody mb = original.GetMethodBody()!;

		LocalVariableInfo groupLocal = mb.LocalVariables
			.Single(local => local.LocalType == typeof(DataFeedGroup));
		int groupLocalIdx = groupLocal.LocalIndex;

		LocalVariableInfo typeLocal = mb.LocalVariables
			.Single(local => local.LocalType == typeof(Type));
		int typeLocalIdx = typeLocal.LocalIndex;

		var tryGetActiveSettingMethod = AccessTools.Method(typeof(Settings), nameof(Settings.TryGetActiveSetting));
		var injectMethod = AccessTools.DeclaredMethod(typeof(SettingsDataFeedPatcher), nameof(InjectedMethod));

		bool foundFirst = false;
		bool foundSecond = false;

		foreach (CodeInstruction instruction in instructions) {
			if (!foundFirst && instruction.Calls(tryGetActiveSettingMethod)) {
				foundFirst = true;
				yield return CodeInstruction.StoreLocal(typeLocalIdx);
				yield return CodeInstruction.LoadLocal(typeLocalIdx);
				yield return instruction;
				continue;
			}

			if (!foundSecond && instruction.IsStloc() && instruction.LocalIndex() == groupLocalIdx) {
				foundSecond = true;
				yield return CodeInstruction.LoadLocal(typeLocalIdx);
				yield return new CodeInstruction(OpCodes.Callvirt, injectMethod);
				yield return instruction;
				continue;
			}

			yield return instruction;
		}

		if (foundFirst && foundSecond) {
			Logger.DebugInternal("SettingsDataFeedPatcher success!");
		}
	}

	static DataFeedGroup InjectedMethod(DataFeedGroup in1, Type in2) {
		return in2 == typeof(ModSettings) ? new DataFeedGrid() : in1;
	}
	*/
}
