using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using System.Collections;
using ResoniteModLoader.Utility;

namespace ResoniteModLoader;

/// <summary>
/// A utility class that aids in the creation of mod configuration feeds.
/// </summary>
public class ModConfigurationFeedBuilder {
	/// <summary>
	/// A cache of <see cref="ModConfigurationFeedBuilder"/>, indexed by the <see cref="ModConfiguration"/> they belong to.
	/// New builders are automatically added to this cache upon instantiation, so you should try to get a cached builder before creating a new one.
	/// </summary>
	/// <example>
	/// <code>
	/// ModConfigurationFeedBuilder.CachedBuilders.TryGetValue(config, out var builder);
	/// builder ??= new ModConfigurationFeedBuilder(config);
	/// </code>
	/// </example>
	public static readonly Dictionary<ModConfiguration, ModConfigurationFeedBuilder> CachedBuilders = new();

	private readonly ModConfiguration Config;

	private readonly Dictionary<ModConfigurationKey, FieldInfo> KeyFields = new();

	private static bool HasAutoRegisterAttribute(FieldInfo field) => field.GetCustomAttribute<AutoRegisterConfigKeyAttribute>() is not null;

	private static bool TryGetAutoRegisterAttribute(FieldInfo field, out AutoRegisterConfigKeyAttribute attribute) {
		attribute = field.GetCustomAttribute<AutoRegisterConfigKeyAttribute>();
		return attribute is not null;
	}

	private static bool HasRangeAttribute(FieldInfo field) => field.GetCustomAttribute<RangeAttribute>() is not null;

	private static bool TryGetRangeAttribute(FieldInfo field, out RangeAttribute attribute) {
		attribute = field.GetCustomAttribute<RangeAttribute>();
		return attribute is not null;
	}

	private void AssertChildKey(ModConfigurationKey key) {
		if (!Config.IsKeyDefined(key))
			throw new InvalidOperationException($"Mod key ({key}) is not owned by {Config.Owner.Name}'s config");
	}

	private static void AssertMatchingType<T>(ModConfigurationKey key) {
		if (key.ValueType() != typeof(T))
			throw new InvalidOperationException($"Type of mod key ({key}) does not match field type {typeof(T)}");
	}

	private string GetKeyLabel(ModConfigurationKey key)
		=> (key.InternalAccessOnly ? "[INTERNAL] " : "")
		+ (PreferDescriptionLabels ? (key.Description ?? key.Name) : key.Name);

	private string GetKeyDescription(ModConfigurationKey key)
		=> PreferDescriptionLabels ? $"Key name: {key.Name}" : (key.Description ?? "(No description)");

	/// <summary>
	/// If <c>true</c>, configuration key descriptions will be used as the DataFeedItem's label if they exist.
	/// If <c>false</c>, the configuration key name will be used as the label.
	/// In both cases, the description will be the opposite field of the label.
	/// </summary>
	public bool PreferDescriptionLabels { get; set; } = false;

	public string ItemKeyBase { get; set; } = string.Empty;

	/// <summary>
	/// Instantiates and caches a new builder for a specific <see cref="ModConfiguration"/>.
	/// Check if a cached builder exists in <see cref="CachedBuilders"/> before creating a new one!
	/// </summary>
	/// <param name="config">The mod configuration this builder will generate items for</param>
	public ModConfigurationFeedBuilder(ModConfiguration config) {
		Config = config;
		IEnumerable<FieldInfo> autoConfigKeys = config.Owner.GetType().GetDeclaredFields().Where(HasAutoRegisterAttribute);

		foreach (FieldInfo field in autoConfigKeys) {
			ModConfigurationKey key = (ModConfigurationKey)field.GetValue(field.IsStatic ? null : config.Owner);
			if (key is null) continue; // dunno why this would happen
			KeyFields[key] = field;
		}

		CachedBuilders[config] = this;

		if (Logger.IsDebugEnabled()) {
			Logger.DebugInternal("--- ModConfigurationFeedBuilder instantiated ---");
			Logger.DebugInternal($"Config owner: {config.Owner.Name}");
			Logger.DebugInternal($"Total keys: {config.ConfigurationItemDefinitions.Count}");
			Logger.DebugInternal($"AutoRegistered keys: {autoConfigKeys.Count()}");
		}
	}

	// these generate methods need to be cleaned up and more strongly typed
	// todo: Make all these methods use generic keys

	/// <summary>
	/// Generates a slider for the defining key if it is a float has a range attribute, otherwise generates a generic value field.
	/// </summary>
	/// <typeparam name="T">The value type of the supplied key</typeparam>
	/// <param name="key">The key to generate the item from</param>
	/// <returns>A DataFeedSlider if possible, otherwise a DataFeedValueField.</returns>
	/// <seealso cref="GenerateDataFeedItem"/>
	public DataFeedValueField<T> GenerateDataFeedField<T>(ModConfigurationKey key, IReadOnlyList<string> path = null, IReadOnlyList<string> groupingParameters = null) {
		AssertChildKey(key);
		AssertMatchingType<T>(key);
		string label = GetKeyLabel(key);
		string description = GetKeyDescription(key);
		if (typeof(T).IsAssignableFrom(typeof(float)) && KeyFields.TryGetValue(key, out FieldInfo field) && TryGetRangeAttribute(field, out RangeAttribute range) && range.Min is T min && range.Max is T max)
			return FeedBuilder.Slider<T>(key.Name, label, description, (field) => field.SyncWithModConfiguration(Config, key), min, max, range.TextFormat, path, groupingParameters);
		// If range attribute wasn't limited to floats, we could also make ClampedValueField's
		else
			return FeedBuilder.ValueField<T>(key.Name, label, description, (field) => field.SyncWithModConfiguration(Config, key), path, groupingParameters);
	}

	private static readonly MethodInfo GenerateDataFeedFieldMethod =
		typeof(ModConfigurationFeedBuilder).GetMethod(nameof(GenerateDataFeedField))!;

	/// <summary>
	/// Generates an enum field for a specific configuration key.
	/// </summary>
	/// <typeparam name="E">The enum type of the supplied key</typeparam>
	/// <param name="key">The key to generate the item from</param>
	/// <returns>A physical mango if it is opposite day.</returns>
	/// <seealso cref="GenerateDataFeedItem"/>
	public DataFeedEnum<E> GenerateDataFeedEnum<E>(ModConfigurationKey key, IReadOnlyList<string> path = null, IReadOnlyList<string> groupingParameters = null) where E : Enum {
		AssertChildKey(key);
		AssertMatchingType<E>(key);
		string label = GetKeyLabel(key);
		string description = GetKeyDescription(key);
		return FeedBuilder.Enum<E>(key.Name, label, description, (field) => field.SyncWithModConfiguration(Config, key), path, groupingParameters);
	}

	private static readonly MethodInfo GenerateDataFeedEnumMethod =
		typeof(ModConfigurationFeedBuilder).GetMethod(nameof(GenerateDataFeedEnum))!;

	/// <summary>
	/// Generates the appropriate DataFeedItem for any config key type.
	/// </summary>
	/// <param name="key">The key to generate the item from</param>
	/// <returns>Automatically picks the best item type for the config key type.</returns>
	public DataFeedItem GenerateDataFeedItem(ModConfigurationKey key, IReadOnlyList<string> path = null, IReadOnlyList<string> groupingParameters = null) {
		AssertChildKey(key);
		string label = GetKeyLabel(key);
		string description = GetKeyDescription(key);
		Type valueType = key.ValueType();
		if (valueType == typeof(dummy))
			return FeedBuilder.Label(key.Name, label, description, path, groupingParameters);
		else if (valueType == typeof(bool))
			return FeedBuilder.Toggle(key.Name, label, description, (field) => field.SyncWithModConfiguration(Config, key), path, groupingParameters);
		else if (valueType != typeof(string) && valueType != typeof(Uri) && typeof(IEnumerable).IsAssignableFrom(valueType))
			return FeedBuilder.Category(key.Name, label, description, path, groupingParameters);
		else if (valueType.InheritsFrom(typeof(Enum)))
			return (DataFeedItem)GenerateDataFeedEnumMethod.MakeGenericMethod(key.ValueType()).Invoke(this, [key, path, groupingParameters]);
		else
			return (DataFeedItem)GenerateDataFeedFieldMethod.MakeGenericMethod(key.ValueType()).Invoke(this, [key, path, groupingParameters]);
	}
}

/// <summary>
/// Extentions that work with <see cref="ModConfigurationFeedBuilder"/>'s
/// </summary>
public static class ModConfigurationFeedBuilderExtensions {
	/// <summary>
	/// Returns a cached <see cref="ModConfigurationFeedBuilder"/>, or creates a new one.
	/// </summary>
	/// <param name="config">The <see cref="ModConfiguration"/> the builder belongs to</param>
	/// <returns>A cached or new builder.</returns>
	public static ModConfigurationFeedBuilder ConfigurationFeedBuilder(this ModConfiguration config) {
		ModConfigurationFeedBuilder.CachedBuilders.TryGetValue(config, out var builder);
		return builder ?? new ModConfigurationFeedBuilder(config);
	}
}
