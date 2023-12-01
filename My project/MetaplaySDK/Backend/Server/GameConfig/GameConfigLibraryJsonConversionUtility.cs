using Metaplay.Core.Config;
using Metaplay.Core.Json;
using Metaplay.Server.AdminApi;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Metaplay.Server.GameConfig;

/// <summary>
/// Note that this solution is currently quite ugly, this is currently required since patches do not support nested fields in patches; only top level items are patched.
/// Long term, we want to redesign patches to work on nested fields, which should make this a lot easier.
/// </summary>
internal static class GameConfigLibraryJsonConversionUtility
{
    static JsonSerializerSettings Settings { get; }

    static GameConfigLibraryJsonConversionUtility()
    {
        Settings = new JsonSerializerSettings();
        AdminApiJsonSerialization.ApplySettingsWithOverrides(Settings, (ref JsonSerialization.Options x) => { x.ExcludeReadOnlyProperties = true; });
    }

    public static Dictionary<string, ConfigKey> ConvertPartialGameConfigToConfigKeys(PartialGameConfig partialGameConfig, List<string> experimentFilter, ILogger logger)
    {
        JsonSerializationErrorLogger     errorLogger                   = new JsonSerializationErrorLogger();
        JsonSerializer serializer = JsonSerializationErrorAdminApiUtility.CreateSerializerWithJsonErrors(errorLogger, Settings);

        JObject        sharedRoot               = JObject.FromObject(partialGameConfig.SharedLibraries, serializer);
        JObject        serverRoot               = JObject.FromObject(partialGameConfig.ServerLibraries, serializer);

        JsonSerializationErrorAdminApiUtility.WriteErrorsToConsole(errorLogger, logger, "api/gameConfig/{configIdStr}/details");

        JObject playerExperimentsRoot = serverRoot[ServerGameConfigBase.PlayerExperimentsEntryName]?.Value<JObject>();

        Dictionary<string, ConfigKey> config = new Dictionary<string, ConfigKey>();
        foreach (var prop in sharedRoot.Children<JProperty>())
        {
            ConfigKey configKey = CreateConfigKeyForLibraryEntry(prop, playerExperimentsRoot, experimentFilter, false);
            config.Add(prop.Name, configKey);
        }

        foreach (var prop in serverRoot.Children<JProperty>())
        {
            ConfigKey configKey = CreateConfigKeyForLibraryEntry(prop, playerExperimentsRoot, experimentFilter, true);
            config.Add(prop.Name, configKey);
        }

        return config;
    }

    public static Dictionary<string, ConfigKey> DiffPartialGameConfig(string baseLineId, string newRootId, PartialGameConfig baseline, PartialGameConfig newRoot, ILogger logger)
    {
        JsonSerializationErrorLogger     errorLogger         = new JsonSerializationErrorLogger();
        JsonSerializer serializer     = JsonSerializationErrorAdminApiUtility.CreateSerializerWithJsonErrors(errorLogger, Settings);

        JObject        baseSharedRoot = JObject.FromObject(baseline.SharedLibraries, serializer);
        JObject        baseServerRoot = JObject.FromObject(baseline.ServerLibraries, serializer);

        JObject newSharedRoot = JObject.FromObject(newRoot.SharedLibraries, serializer);
        JObject newServerRoot = JObject.FromObject(newRoot.ServerLibraries, serializer);

        JsonSerializationErrorAdminApiUtility.WriteErrorsToConsole(errorLogger, logger, "api/gameConfig/{configIdStr}/diff");

        Dictionary<string, ConfigKey> config = new Dictionary<string, ConfigKey>();
        foreach (ConfigKey configKey in DiffJToken(baseLineId, newRootId, "Shared", baseSharedRoot, newSharedRoot).Children)
            config.Add(configKey.Title, configKey);
        foreach (ConfigKey configKey in DiffJToken(baseLineId, newRootId, "Server", baseServerRoot, newServerRoot).Children)
            config.Add(configKey.Title, configKey);

        return config;
    }

    static ConfigKey DiffJToken(string baseLineId, string newRootId, string propertyKey, JToken baseline, JToken newRoot)
    {
        ConfigKey configKey = null;
        if (baseline is JProperty prop)
            baseline = prop.Value;
        if (newRoot is JProperty newProp)
            newRoot = newProp.Value;

        if (baseline is JValue val)
        {
            JValue                    newValue   = newRoot as JValue;
            Dictionary<string,object> values     = new Dictionary<string, object>(){{baseLineId, val.Value}};
            ChangeType                changeType = ChangeType.None;

            if (newValue == null)
                changeType = ChangeType.Removed;
            if (newValue != null && !val.Equals(newValue))
            {
                values.Add(newRootId, newValue.Value);
                changeType = ChangeType.Modified;
            }

            configKey = new ConfigKey(propertyKey, null, values, val.Value?.GetType().FullName, changeType);
        }
        else if (baseline is JObject obj)
        {
            JContainer newContainer = newRoot as JContainer;
            configKey = new ConfigKey(propertyKey, baseline["$type"]?.Value<string>() ?? baseline.Type.ToString());
            var baselineChildren = obj.Children().ToDictionary(x => x is JProperty childProp ? childProp.Name : "", x => x as JProperty);
            var newChildren      = newContainer?.Children().ToDictionary(x => x is JProperty childProp ? childProp.Name : "", x => x as JProperty);
            foreach ((string stringKey, JProperty jToken) in baselineChildren)
            {
                if (stringKey == "$type")
                {
                    newChildren?.Remove(stringKey);
                    continue;
                }

                JProperty newToken = null;
                newChildren?.TryGetValue(stringKey, out newToken);

                ConfigKey diffJToken = DiffJToken(baseLineId, newRootId, stringKey, jToken, newToken);
                if(diffJToken.Differences)
                    configKey.AddChild(diffJToken);
                newChildren?.Remove(stringKey);
            }

            if (newChildren != null)
            {
                foreach ((string key, JProperty value) in newChildren)
                {
                    ConfigKey childKey = CreateConfigKeyForJToken(
                        key,
                        value.Value,
                        potentialPatches: null,
                        fieldPatches: null,
                        replacementFieldPatches: null,
                        keyOverride: newRootId,
                        forceChildrenChangeType: ChangeType.Added);
                    configKey.AddChild(childKey);
                }
            }

            configKey.UpdateSubtitle();
        }
        else if (baseline is JArray array)
        {
            JContainer newContainer = newRoot as JArray;
            configKey = new ConfigKey(propertyKey, "[]");
            var baselineChildren = array.Select((token,  i) => (token, index: i)).ToDictionary(x => x.index, x => x.token);
            var newChildren      = newContainer?.Select((token,  i) => (token, index: i)).ToDictionary(x => x.index, x => x.token);
            foreach ((int index, JToken jToken) in baselineChildren)
            {
                string stringKey = $"[{index.ToString(CultureInfo.InvariantCulture)}]";
                JToken newToken  = null;
                newChildren?.TryGetValue(index, out newToken);

                ConfigKey diffJToken = DiffJToken(baseLineId, newRootId, stringKey, jToken, newToken);
                if (diffJToken.Differences)
                    configKey.AddChild(diffJToken);

                newChildren?.Remove(index);
            }

            if (newChildren != null)
            {
                foreach ((int key, JToken value) in newChildren)
                {
                    ConfigKey childKey = CreateConfigKeyForJToken(
                        $"[{key.ToString(CultureInfo.InvariantCulture)}]",
                        value,
                        null,
                        null,
                        null,
                        keyOverride: newRootId,
                        forceChildrenChangeType: ChangeType.Added,
                        arrayIndex: key);
                    configKey.AddChild(childKey);
                }
            }

            configKey.UpdateSubtitle(true);
        }

        return configKey;
    }

    static ConfigKey CreateConfigKeyForLibraryEntry(JProperty prop, JObject playerExperimentsRoot, List<string> experiments, bool useServerPatch)
    {
        List<(string variantKey, JObject obj)> potentialPatches = FindMatchingExperiment(prop.Name, playerExperimentsRoot, experiments, useServerPatch);
        ConfigKey                              configKey        = GeneratePatchedReturnValue(prop.Name, prop.Value as JObject, potentialPatches);

        List<(string variantKey, string itemKey, JToken obj)> additionPatch = FindMatchingAdditionPatch(potentialPatches);
        foreach ((string variantKey, string itemKey, JToken jToken) in additionPatch)
        {
            if (jToken is JObject obj)
            {
                ConfigKey key = GeneratePatchedReturnValue(
                    itemKey,
                    obj,
                    potentialPatches: null,
                    keyOverride: variantKey,
                    forceChildrenChangeType: ChangeType.Added);
                configKey.AddChild(key);
            }
            else if (jToken is JValue val)
            {
                ConfigKey key = new ConfigKey(itemKey, null, new Dictionary<string, object>() {{variantKey, val.Value}}, val.Value?.GetType().FullName, changeType: ChangeType.Added);
                configKey.AddChild(key);
            }
        }

        configKey.UpdateSubtitle();
        return configKey;
    }

    static ConfigKey GeneratePatchedReturnValue(
        string key,
        JObject root,
        List<(string variantKey, JObject obj)> potentialPatches,
        List<(string variantKey, JToken obj)> fieldPatches = null,
        string keyOverride = null,
        ChangeType forceChildrenChangeType = ChangeType.None,
        int arrayIndex = -1)
    {
        ConfigKey configKey = new ConfigKey(arrayIndex == -1 ? key : $"[{arrayIndex.ToString(CultureInfo.InvariantCulture)}]", root["$type"]?.Value<string>() ?? root.Type.ToString());
        foreach (JProperty prop in root.Children<JProperty>())
        {
            if (prop.Name == "$type")
                continue;

            List<(string variantKey, JToken obj)> replacementFieldPatches = null;
            if (fieldPatches == null)
                replacementFieldPatches = FindMatchingReplacementPatch(prop.Name, potentialPatches);

            ConfigKey value = CreateConfigKeyForJToken(
                prop.Name,
                prop.Value,
                potentialPatches,
                fieldPatches,
                replacementFieldPatches,
                keyOverride,
                forceChildrenChangeType,
                arrayIndex);

            if (value != null)
                configKey.AddChild(value);
        }


        configKey.UpdateSubtitle();
        return configKey;
    }

    static ConfigKey CreateConfigKeyForJToken(
        string key,
        JToken tokenValue,
        List<(string variantKey, JObject obj)> potentialPatches,
        List<(string variantKey, JToken obj)> fieldPatches,
        List<(string variantKey, JToken obj)> replacementFieldPatches,
        string keyOverride = null,
        ChangeType forceChildrenChangeType = ChangeType.None,
        int arrayIndex = -1)
    {
        ConfigKey value = null;
        if (tokenValue is JObject jObject)
        {
            if (fieldPatches != null)
            {
                replacementFieldPatches = new List<(string variantKey, JToken obj)>();
                foreach ((string variantKey, JToken obj) in fieldPatches)
                {
                    if (obj[key] is JObject nestedObj)
                        replacementFieldPatches.Add((variantKey, nestedObj));
                    if (obj[key] is JArray nestedArr)
                        replacementFieldPatches.Add((variantKey, nestedArr));
                }
            }

            value = GeneratePatchedReturnValue(
                key,
                jObject,
                potentialPatches,
                replacementFieldPatches,
                keyOverride,
                forceChildrenChangeType);
        }
        else if (tokenValue is JValue jValue)
        {
            object                     val        = jValue.Value;
            Dictionary<string, object> dictionary = new Dictionary<string, object> {{keyOverride ?? "Baseline", val}};
            if (fieldPatches != null)
            {
                foreach ((string variantKey, JToken obj) in fieldPatches)
                {
                    JToken target = obj;
                    if (obj is JArray arr)
                    {
                        if (arrayIndex == -1 || arr.Count <= arrayIndex)
                            continue;

                        target = arr[arrayIndex];
                    }

                    if (target[key] is JValue patchValue && (!patchValue.Value?.Equals(val) ?? false))
                        dictionary.Add(variantKey, patchValue.Value);
                }
            }
            else if (replacementFieldPatches != null)
            {
                foreach ((string variantKey, JToken token) in replacementFieldPatches)
                {
                    if (token is JValue patchValue)
                    {
                        if (!patchValue.Value?.Equals(val) ?? false)
                            dictionary.Add(variantKey, patchValue.Value);
                    }
                }
            }

            ChangeType changeType = forceChildrenChangeType;
            if (dictionary.Count > 1 && changeType == ChangeType.None)
                changeType = ChangeType.Modified;

            value = new ConfigKey(key, null, dictionary, val?.GetType().FullName, changeType);
        }
        else if (tokenValue is JArray jArray)
        {
            value = new ConfigKey(key, "[]");
            for (int i = 0; i < jArray.Count; i++)
            {
                JToken token = jArray[i];
                ConfigKey configKey = CreateConfigKeyForJToken(
                    key,
                    token,
                    potentialPatches,
                    fieldPatches,
                    replacementFieldPatches,
                    keyOverride,
                    forceChildrenChangeType,
                    i);
                configKey.UpdateTitle($"[{i.ToString(CultureInfo.InvariantCulture)}]");
                value.AddChild(configKey);
            }

            Dictionary<int, List<(string variantKey, JToken overrideValue)>> additions = new Dictionary<int, List<(string, JToken)>>();
            if (fieldPatches != null)
            {
                foreach ((string variantKey, JToken token) in fieldPatches)
                {
                    if (token is JObject obj)
                    {
                        if (obj[key] is JArray arr)
                        {
                            if (jArray.Count < arr.Count)
                            {
                                for (int i = jArray.Count; i < arr.Count; i++)
                                {
                                    if (!additions.ContainsKey(i))
                                        additions[i] = new List<(string, JToken)>();

                                    additions[i].Add((variantKey, arr[i]));
                                }
                            }
                        }
                    }
                }

                foreach ((int i, List<(string, JToken)> valueTuples) in additions)
                {
                    ConfigKey overrideKey = null;
                    foreach ((string variantKey, JToken overrideValue) in valueTuples)
                    {
                        if (overrideKey == null)
                        {
                            overrideKey = CreateConfigKeyForJToken(
                                $"[{i.ToString(CultureInfo.InvariantCulture)}]",
                                overrideValue,
                                potentialPatches: null,
                                fieldPatches: null,
                                replacementFieldPatches: null,
                                variantKey,
                                forceChildrenChangeType: ChangeType.Added);
                        }
                        else
                            overrideKey.AddValuesToChildrenFromMatchingStructure(variantKey, overrideValue);
                    }

                    value.AddChild(overrideKey);
                }
            }
            value.UpdateSubtitle(true);
        }

        return value;
    }

    static List<(string variantKey, JToken obj)> FindMatchingReplacementPatch(string toMatch, List<(string variantKey, JObject obj)> potentialPatches)
    {
        List<(string variantKey, JToken obj)> matching = new List<(string variantKey, JToken obj)>();
        if (potentialPatches == null)
            return matching;

        foreach ((string variantKey, JObject jObject) in potentialPatches)
        {
            var replaced = jObject["replacedItems"] ?? jObject["replacedMembers"];

            if (replaced == null)
                continue;

            foreach ((string key, JToken value) in replaced.Value<JObject>())
            {
                if (toMatch.Equals(key, StringComparison.InvariantCultureIgnoreCase))
                    matching.Add((variantKey, value.Value<JToken>()));
            }
        }

        return matching;
    }

    static List<(string variantKey, string itemKey, JToken obj)> FindMatchingAdditionPatch(List<(string variantKey, JObject obj)> potentialPatches)
    {
        List<(string variantKey, string itemKey, JToken obj)> matching = new List<(string variantKey, string itemKey, JToken obj)>();
        if (potentialPatches == null)
            return matching;

        foreach ((string variantKey, JObject jObject) in potentialPatches)
        {
            var addition = jObject["appendedItems"] ?? jObject["appendedMembers"];

            if (addition == null)
                continue;

            foreach ((string key, JToken value) in addition.Value<JObject>())
                matching.Add((variantKey, key, value.Value<JToken>()));
        }

        return matching;
    }

    static List<(string variantKey, JObject obj)> FindMatchingExperiment(string toMatch, JObject playerExperimentsRoot, List<string> filter, bool useServerPatch)
    {
        List<(string variantKey, JObject obj)> matching = new List<(string variantKey, JObject obj)>();
        if (playerExperimentsRoot == null)
            return matching;

        foreach ((string key, JToken value) in playerExperimentsRoot)
        {
            if (!filter?.Any(x => x.Equals(key, StringComparison.InvariantCultureIgnoreCase)) ?? false)
                continue;

            if (value is not JObject)
                continue;

            var variantsObj = value["variants"]?.Value<JObject>();

            if (variantsObj == null)
                continue;

            foreach ((string variantId, JToken token) in variantsObj)
            {
                JObject  configPatch = token.Value<JObject>()?["configPatch"]?.Value<JObject>();
                string  patchKey = useServerPatch ? "ServerConfigPatch" : "SharedConfigPatch";

                if(configPatch?.ContainsKey(patchKey) == false)
                    continue;

                JObject patches = configPatch?[patchKey]?["entryPatches"]?.Value<JObject>();
                if (patches == null)
                    continue;

                foreach ((string propertyKey, JToken jToken) in patches)
                {
                    if (propertyKey.Equals(toMatch, StringComparison.InvariantCultureIgnoreCase))
                        matching.Add(($"{key}.{variantId}", jToken.Value<JObject>()));
                }
            }
        }

        return matching;
    }

    public enum ChangeType
    {
        None,
        Added,
        Modified,
        Removed
    }

    public class ConfigKey
    {
        [JsonIgnore] readonly List<ConfigKey> _children = new List<ConfigKey>();
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public                string          Type        { get; private set; }
        public                string          Title       { get; private set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public                string          Subtitle    { get; private set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public                bool            Differences { get; private set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore, TypeNameHandling = TypeNameHandling.None, ItemTypeNameHandling = TypeNameHandling.None)]
        public IReadOnlyList<ConfigKey> Children => _children;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore, TypeNameHandling = TypeNameHandling.None, ItemTypeNameHandling = TypeNameHandling.None)]
        public Dictionary<string, object> Values { get; private init; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ChangeType ChangeType { get; private init; }

        public ConfigKey(
            string title,
            List<ConfigKey> children,
            Dictionary<string, object> values,
            string type = null,
            ChangeType changeType = ChangeType.None)
        {
            _children = children;
            Values    = values;
            Title     = title;
            Type      = type;

            ChangeType = changeType;

            Differences = values?.Count > 1 || changeType != ChangeType.None;

            UpdateSubtitle();
        }

        public ConfigKey(string title, string type)
        {
            Type  = type;
            Title = title;
        }

        public void UpdateSubtitle(bool isArray = false)
        {
            if (Children?.Count > 0)
                if (isArray)
                    Subtitle = $"[{Children.Count.ToString(CultureInfo.InvariantCulture)}]";
                else
                    Subtitle = $"{{{Children.Count.ToString(CultureInfo.InvariantCulture)}}}";
            else
                Subtitle = null;
        }

        public void UpdateTitle(string newTitle)
        {
            Title = newTitle;
        }

        public void AddChild(ConfigKey key)
        {
            _children.Add(key);
            Differences = Differences || key.Differences;
        }

        public void AddValuesToChildrenFromMatchingStructure(string variantKey, JToken structure)
        {
            if (structure is JValue value)
            {
                Values.Add(variantKey, value.Value);
            }
            else if (structure is JProperty prop && prop.Name == Title && prop.Value is JValue propValue)
            {
                Values.Add(variantKey, propValue.Value);
            }
            else if (structure is JObject obj)
            {
                var structureChildren = obj.Children().Where(x => (x as JProperty)?.Name != "$type");
                if (structureChildren.Count() == Children.Count)
                {
                    int i = 0;
                    foreach (JToken child in structureChildren)
                    {
                        Children[i].AddValuesToChildrenFromMatchingStructure(variantKey, child);
                        i++;
                    }
                }
            }
        }
    }
}
