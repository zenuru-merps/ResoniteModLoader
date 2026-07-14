using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using ResoniteModLoader.Utility;

namespace ResoniteModLoader;

/// <summary>
/// TODO: Settings UI for loaded mods.
/// </summary>
[AutoRegisterSetting]
[SettingCategory("ResoniteModLoader")]
public sealed class ModSettings : SettingComponent<ModSettings> {
	/// <inheritdoc/>
	public override bool UserspaceOnly => true;
	private const string ModListName = nameof(ModList);
	internal static readonly Dictionary<string, ResoniteModBase> modAssemblyMapping = new();
	internal static readonly Dictionary<string, ModConfiguration?> modConfigMapping = new();
	private DataFeedItemMapper? templateMapper;

	protected override void OnStart() {
		base.OnStart();
		foreach (var mod in ModLoader.Mods()) {
			string modKey = Path.GetFileNameWithoutExtension(mod.ModAssembly!.File);
			modAssemblyMapping[modKey] = mod;
			if (mod.GetConfiguration() is { } config) {
				modConfigMapping[modKey] = config;
			}
		}
	}

	/*
	public override bool IsSubcategoryPublic(string subcategory) {
		Logger.DebugInternal("ModSettings.IsSubcategoryPublic " + subcategory);
		return subcategory == ModListName || modAssemblyMapping.ContainsKey(subcategory);
	}

	public override async IAsyncEnumerable<DataFeedItem> GetSubcategoryItems(string subcategory) {
		Logger.DebugInternal("ModSettings.GetSubcategoryItems " + subcategory);
		if (!IsSubcategoryPublic(subcategory)) {
			yield return FeedBuilder.Label("ModInfo." + subcategory, $"Invalid mod {subcategory}");
			yield break;
		}

		if (subcategory == ModListName) {
			yield return GenerateModGrid();
			yield return GenerateModConfigPanel(subcategory);
		}

		yield return GenerateModInfoPanel(subcategory, false);
	}
	*/

	private bool FindTemplateMapper(IField field) {
		var slot = field.FindNearestParent<Slot>();
		var view = slot.GetComponentInParents<RootCategoryView>(rcv => slot.IsChildOf(rcv.ItemsManager.ContainerRoot));
		var found = view?.ItemsManager.TemplateMapper.Target;
		if (found == null) {
			return false;
		}

		Logger.DebugInternal("Found TemplateMapper: " + found);
		templateMapper = found;
		return true;

	}

	public DataFeedItem GenerateModGrid(IReadOnlyList<string> path = null!, IReadOnlyList<string> grouping = null!) {
		if (modAssemblyMapping.Count == 0) {
			return FeedBuilder.Label("NoMods", "Settings.ModSettings.NoMods".AsLocaleKey());
		}

		List<DataFeedItem> modLinks = new();

		if (modConfigMapping.Count == 0) {
			modLinks.Add(
				FeedBuilder.Label("NoModsWithConfig", "Settings.ModSettings.NoModsWithConfig".AsLocaleKey())
				.WithVisible(field => field.DriveInverted(ShowAll))
			);
		}

		foreach (var (modKey, mod) in modAssemblyMapping) {
			// var link = FeedBuilder.Category(modKey, mod.Name, groupingParameters: [ModListName]);
			// link.SetOverrideSubpath("ModSettings." + modKey);
			var link = FeedBuilder.ValueAction<string>(modKey, mod.Name, action => action.Target = OpenModConfig, modKey, groupingParameters: [ModListName])
				.WithDescription($"Settings.ModSettings.ModList.{modKey}".AsLocaleKey());
			if (mod.GetConfiguration() is { } config) {
				if (config.ConfigurationItemDefinitions.All(key => key.InternalAccessOnly)) {
					link.InitVisible(field =>
						field.SetupBoolConditionDriver(MultiBoolConditionDriver.ConditionMode.Any,
							ShowAll, ShowInternal));
				}
			}
			else {
				link.InitVisible(field => field.DriveFrom(ShowAll));
			}
			modLinks.Add(link);
		}

		var grid = FeedBuilder.Grid(ModListName, "Settings.ModSettings.ModList".AsLocaleKey(), path, grouping, subitems: modLinks);
		grid.InitEnabled(field => FindTemplateMapper(field));
		return grid;
	}

	[SettingSubcategoryEnumerator]
	public async IAsyncEnumerable<DataFeedItem> ModList() {
		yield return GenerateModGrid();
	}

	[SettingProperty]
	public readonly Sync<bool> ShowInternal;

	[SettingProperty]
	public readonly Sync<bool> ShowAll;

	[SettingProperty]
	public readonly Sync<bool> SumJankAssBullshit;

	[SyncMethod(typeof(Action<Uri>), [])]
	internal static void OpenURI(Uri uri) {
		Slot slot = Userspace.UserspaceWorld.AddSlot("Hyperlink");
		slot.PositionInFrontOfUser(float3.Backward);
		slot.AttachComponent<HyperlinkOpenDialog>().Setup(uri, null!);
	}

	[SyncMethod(typeof(Action<string>), [])]
	internal void OpenModConfig(string modKey) {
		if (!modAssemblyMapping.TryGetValue(modKey, out var mod)) {
			return;
		}
		var dash = Userspace.Current.World.GetRadiantDash();
		dash.Open = true;
		var rect = dash.Slot.OpenModalOverlay(new float2(SumJankAssBullshit.Value ? 0.75f : 0.6f, 0.85f), SumJankAssBullshit.Value ? "Settings.ModSettings.ModList".AsLocaleKey() : mod.Name);
		var root = rect.Slot;

		if (SumJankAssBullshit.Value && templateMapper != null) {
			var template = templateMapper.Slot.Duplicate(root, false);
			template.AttachComponent<DynamicVariableSpace>().SpaceName.Value = "Settings";
			var df = template.AttachComponent<ModConfigurationDataFeed>();
			df.IncludeInternalConfigItems.SyncWithSetting(typeof(ModSettings), nameof(ShowInternal));
			var rcv = template.GetComponent<RootCategoryView>();
			rcv.Path.Clear();
			rcv.Path.Add(modKey);
			rcv.Feed.Target = df;
		}
		else {
			var ui = new UIBuilder(rect);

			ui.ScrollArea();
			ui.VerticalLayout(forceExpandHeight: false);
			ui.FitContent(SizeFit.Disabled, SizeFit.MinSize);

			var df = root.AttachComponent<ModConfigurationDataFeed>();
			var view = root.AttachComponent<SingleFeedView>();

			if (templateMapper is null) {
				var dfim = root.AttachComponent<DataFeedItemMapper>();
				dfim.SetupTemplate();
				view.ItemsManager.TemplateMapper.Target = dfim;
			}
			else {
				view.ItemsManager.TemplateMapper.Target = templateMapper;
			}

			df.IncludeInternalConfigItems.SyncWithSetting(typeof(ModSettings), nameof(ShowInternal));
			view.ItemsManager.ContainerRoot.Target = ui.Root;
			view.Path.Add(modKey);
			view.Feed.Target = df;
		}
	}

	[SyncMethod(typeof(Action))]
	internal static void Dummy() {}
}
