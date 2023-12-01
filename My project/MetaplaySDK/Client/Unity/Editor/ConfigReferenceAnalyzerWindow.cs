// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using static System.FormattableString;

namespace Metaplay.Client.Unity
{
    public class ConfigReferenceAnalyzerWindow : EditorWindow
    {
        public const string DefaultFullGameConfigArchivePath = "Backend/Server/GameConfig/StaticGameConfig.mpa";

        [MenuItem("Metaplay/Config Reference Analyzer")]
        public static void Open()
        {
            ConfigReferenceAnalyzerWindow window = EditorWindow.GetWindow<ConfigReferenceAnalyzerWindow>();
            window.titleContent = new GUIContent("Config Reference Analyzer");
            window.minSize = new Vector2(200.0f, 200.0f);
            window.Show();
        }

        [NonSerialized] bool _isInitialized;

        [NonSerialized] Vector2 _mainUIScroll;
        [NonSerialized] bool _helpExpanded;
        [NonSerialized] string _fullConfigArchivePathInput;
        [NonSerialized] string _currentFullConfigArchivePath;

        [NonSerialized] TaskHelper<CommonResources> _resourcesTask;
        CommonResources _resources => _resourcesTask.ResultOrDefault;

        [NonSerialized] TaskHelper<ItemsReachabilityState> _itemsReachabilityTask;
        ItemsReachabilityState _itemsReachability => _itemsReachabilityTask?.ResultOrDefault;
        [NonSerialized] TaskProgress _itemsReachabilityTaskProgress;

        enum PatchDropdownSorting
        {
            MostPropagatingFirst = 0,
            AccordingToConfig = 1,
        }

        static readonly string[] _patchDropdownSortingStrings = new string[]
        {
            "Most-propagating first",
            "As in experiments config",
        };

        [NonSerialized] PatchDropdownSorting _patchDropdownSorting = default;
        [NonSerialized] ExperimentVariantPair? _selectedPatchId;
        [NonSerialized] TaskHelper<PatchState> _patchStateTask;
        PatchState _patchState => _patchStateTask?.ResultOrDefault;

        [NonSerialized] OrderedSet<ConfigItemId> _selectedExplicitItems = new OrderedSet<ConfigItemId>();
        [NonSerialized] TaskHelper<ExplicitItemsState> _explicitItemsStateTask;
        ExplicitItemsState _explicitItemsState => _explicitItemsStateTask?.ResultOrDefault;

        [NonSerialized] TaskHelper<AnalysisState> _analysisTask;
        AnalysisState _analysisState => _analysisTask?.ResultOrDefault;

        abstract class TaskHelper
        {
            public DateTime StartTime;
            public abstract Task BaseTask { get; }
            public CancellationTokenSource CancellationTokenSource;
            public bool IsOngoing;
            public string Error;
        }

        class TaskHelper<TResult> : TaskHelper
        {
            public override Task BaseTask => Task;
            public Task<TResult> Task;

            public TResult ResultOrDefault => !IsOngoing && Task.Status == TaskStatus.RanToCompletion ? Task.Result : default;
        }

        class TaskProgress
        {
            public int Current;
            public int Total;

            public override string ToString() => Invariant($"{Current}/{Total}");
        }

        class CommonResources
        {
            public DynamicTraversal.Resources TraversalResources;
            public FullGameConfigImportResources FullConfigImportResources;
            public int NumBaselineItems;
            public OrderedDictionary<ExperimentVariantPair, PatchStats> PatchStats;

            public CommonResources(DynamicTraversal.Resources traversalResources, FullGameConfigImportResources fullConfigImportResources, int numBaselineItems, OrderedDictionary<ExperimentVariantPair, PatchStats> patchStats)
            {
                TraversalResources = traversalResources;
                FullConfigImportResources = fullConfigImportResources;
                NumBaselineItems = numBaselineItems;
                PatchStats = patchStats;
            }
        }

        class PatchStats
        {
            public int NumItems;
            public int NumDirectlyPatchedItems;
            public int NumIndirectlyPatchedItems;

            public PatchStats(int numItems, int numDirectlyPatchedItems, int numIndirectlyPatchedItems)
            {
                NumItems = numItems;
                NumDirectlyPatchedItems = numDirectlyPatchedItems;
                NumIndirectlyPatchedItems = numIndirectlyPatchedItems;
            }
        }

        class ItemsReachabilityState
        {
            public OrderedDictionary<Type, TypeItemsInfo> ItemsByType = new OrderedDictionary<Type, TypeItemsInfo>();

            public bool UIExpanded;

            public class TypeItemsInfo
            {
                public List<(object Key, ItemInfo Info)> Items = new List<(object, ItemInfo)>();
                public int MaxReachability;

                public bool UIExpanded;
                public Vector2 UIScroll;
                public string SearchText = "";
            }

            public class ItemInfo
            {
                public int Reachability;

                public ItemInfo(int reachability)
                {
                    Reachability = reachability;
                }
            }
        }

        class PatchState
        {
            public MetaRefAnalysisUtil.AnalysisContext AnalysisContext;
            public ItemSetState RootItems;
            public ItemSetState IndirectlyPatchedItems;

            public PatchState(MetaRefAnalysisUtil.AnalysisContext analysisContext, ItemSetState rootItems, ItemSetState indirectlyPatchedItems)
            {
                AnalysisContext = analysisContext;
                RootItems = rootItems;
                IndirectlyPatchedItems = indirectlyPatchedItems;
            }
        }

        class ExplicitItemsState
        {
            public MetaRefAnalysisUtil.AnalysisContext AnalysisContext;
            public ItemSetState TransitivelyReferringItems;

            public ExplicitItemsState(MetaRefAnalysisUtil.AnalysisContext analysisContext, ItemSetState transitivelyReferringItems)
            {
                AnalysisContext = analysisContext;
                TransitivelyReferringItems = transitivelyReferringItems;
            }
        }

        class AnalysisState
        {
            public MetaRefAnalysisUtil.AnalysisResult MetaRefAnalysisResult;
            public ItemSetState IndirectlyReachableItems;

            public AnalysisState(MetaRefAnalysisUtil.AnalysisResult metaRefAnalysisResult, ItemSetState indirectlyReachableItems)
            {
                MetaRefAnalysisResult = metaRefAnalysisResult;
                IndirectlyReachableItems = indirectlyReachableItems;
            }
        }

        class ItemSetState
        {
            public OrderedDictionary<Type, ItemTypeState> ItemsByType = new OrderedDictionary<Type, ItemTypeState>();
            public int ItemCount => ItemsByType.Values.Sum(t => t.Keys.Count);

            public bool UIExpanded;
        }

        class ItemTypeState
        {
            public OrderedSet<object> Keys = new OrderedSet<object>();

            public bool UIExpanded;
            public Vector2 UIScroll;
        }

        TaskHelper<TResult> StartTask<TResult>(Func<CancellationToken, Task<TResult>> createTask)
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            return new TaskHelper<TResult>
            {
                Task = createTask(cts.Token),
                CancellationTokenSource = cts,
                StartTime = DateTime.UtcNow,
                IsOngoing = true,
            };
        }

        void TryCancelTask(TaskHelper taskHelper)
        {
            if (taskHelper?.IsOngoing == true)
            {
                taskHelper.CancellationTokenSource.Cancel();
            }
        }

        bool UpdateTaskTracking(TaskHelper taskHelper)
        {
            bool completedSuccessfullyJustNow = false;

            if (taskHelper != null && taskHelper.IsOngoing)
            {
                if (taskHelper.BaseTask.IsCompleted)
                {
                    taskHelper.IsOngoing = false;

                    if (taskHelper.BaseTask.Status == TaskStatus.RanToCompletion)
                        completedSuccessfullyJustNow = true;
                    else if (taskHelper.BaseTask.IsCanceled)
                        taskHelper.Error = "Canceled";
                    else
                        taskHelper.Error = taskHelper.BaseTask.Exception.InnerException.ToString();
                }

                Repaint();
            }

            return completedSuccessfullyJustNow;
        }

        void OnEnable()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _fullConfigArchivePathInput = DefaultFullGameConfigArchivePath;
            StartLoadCommonResources();
        }

        void StartLoadCommonResources()
        {
            _currentFullConfigArchivePath = _fullConfigArchivePathInput;

            TryCancelTask(_itemsReachabilityTask);
            _itemsReachabilityTask = null;
            _itemsReachabilityTaskProgress = null;

            TryCancelTask(_patchStateTask);
            _patchStateTask = null;
            _patchDropdownSorting = default;
            _selectedPatchId = null;

            TryCancelTask(_explicitItemsStateTask);
            _explicitItemsStateTask = null;
            _selectedExplicitItems = new OrderedSet<ConfigItemId>();

            TryCancelTask(_analysisTask);
            _analysisTask = null;

            TryCancelTask(_resourcesTask);
            _resourcesTask = StartTask(ct => MetaTask.Run(() => LoadCommonResourcesAsync(ct), ct));
        }

        async Task<CommonResources> LoadCommonResourcesAsync(CancellationToken ct)
        {
            DynamicTraversal.Resources traversalResources = DynamicTraversal.Resources.Create(MetaSerializerTypeRegistry.AllTypes);

            ConfigArchive fullConfigArchive = await ConfigArchive.FromFileAsync(_currentFullConfigArchivePath);
            FullGameConfigImportResources fullConfigImportResources = FullGameConfigImportResources.CreateWithAllConfiguredPatches(fullConfigArchive, GameConfigRuntimeStorageMode.Deduplicating, ct: ct);

            int numBaselineItems;
            {
                IGameConfig baselineSharedConfig = GameConfigFactory.Instance.ImportGameConfig(GameConfigImportParams.Specialization(fullConfigImportResources.Shared, new OrderedSet<ExperimentVariantPair>()));
                numBaselineItems = MetaRefAnalysisUtil.CollectAllItems(baselineSharedConfig).Count;
            }
            ct.ThrowIfCancellationRequested();

            OrderedDictionary<ExperimentVariantPair, PatchStats> patchStats = new OrderedDictionary<ExperimentVariantPair, PatchStats>();
            foreach ((ExperimentVariantPair patchId, GameConfigPatchEnvelope patch) in fullConfigImportResources.Shared.Patches)
            {
                if (patch.IsEmpty)
                    continue;

                IGameConfig patchedSharedConfig = GameConfigFactory.Instance.ImportGameConfig(GameConfigImportParams.Specialization(fullConfigImportResources.Shared, new OrderedSet<ExperimentVariantPair> { patchId }));

                patchStats.Add(patchId, new PatchStats(
                    numItems: MetaRefAnalysisUtil.CollectAllItems(patchedSharedConfig).Count,
                    numDirectlyPatchedItems: MetaRefAnalysisUtil.CollectDirectlyPatchedItems(patchedSharedConfig).Count,
                    numIndirectlyPatchedItems: MetaRefAnalysisUtil.CollectIndirectlyPatchedItems(patchedSharedConfig).Count));

                ct.ThrowIfCancellationRequested();
            }

            return new CommonResources(
                traversalResources,
                fullConfigImportResources,
                numBaselineItems: numBaselineItems,
                patchStats);
        }

        void OnInspectorUpdate()
        {
            if (UpdateTaskTracking(_resourcesTask))
                StartResolveItemsReachability();

            UpdateTaskTracking(_itemsReachabilityTask);

            if (UpdateTaskTracking(_patchStateTask))
                StartReferenceAnalysis(_patchState.AnalysisContext);

            if (UpdateTaskTracking(_explicitItemsStateTask))
                StartReferenceAnalysis(_explicitItemsState.AnalysisContext);

            UpdateTaskTracking(_analysisTask);
        }

        void OnGUI()
        {
            DateTime currentTime = DateTime.UtcNow;

            string TimeSinceStr(DateTime startTime)
            {
                return $"{(currentTime - startTime).TotalSeconds:0.0}s";
            }

            _mainUIScroll = GUILayout.BeginScrollView(_mainUIScroll);

            if (_helpExpanded = EditorGUILayout.Foldout(_helpExpanded, "Help, what's this?", toggleOnLabelClick: true))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.Label(
                    text:
                        "This tool helps you analyze config reference (MetaRef) propagation in experiment variants." +
                        "\n\n" +
                        "Background:\n" +
                        "On the game server, the specialized configs which are associated with experiment variants " +
                        "normally share most of their in-memory contents with the baseline config, so that only the config items " +
                        "which are modified by the variant will get duplicated in memory. " +
                        "However, this duplication is propagated by MetaRefs: if non-modified item X refers " +
                        "to modified item Y (either directly or transitively), then X will need to be duplicated in memory as well, " +
                        "because otherwise it would refer to the wrong instance of Y. Therefore, the amount of config item duplication " +
                        "involved in a variant config is generally greater than just the amount of modified items." +
                        "\n\n" +
                        "If there are specific MetaRefs that cause a significant amount of duplication propagation, then " +
                        "it may be worth the hassle to eliminate those MetaRefs, by means of refactoring them into plain " +
                        "config ids and performing the config lookup explicitly in the game code." +
                        "\n\n" +
                        "This tool is intended to help you find out which MetaRefs are involved in duplication propagation for a given variant, " +
                        "and see how much duplication would be reduced if some MetaRefs were eliminated." +
                        "\n\n" +
                        "You can also select individual config items and see which items transitively refer to them; " +
                        "that is, which items would be duplicated if the selected items were modified by a variant.",
                    style:
                        new GUIStyle(GUI.skin.label) { wordWrap = true });
                GUILayout.EndHorizontal();
            }

            DrawHorizontalLine();

            GUILayout.BeginHorizontal();

            GUILayout.Label("Path to full config archive: ", EditorStyles.boldLabel);

            _fullConfigArchivePathInput = GUILayout.TextField(_fullConfigArchivePathInput, GUILayout.Width(300));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Reload config", GUILayout.Width(100)))
                StartLoadCommonResources();

            DrawHorizontalLine();

            if (_resourcesTask.IsOngoing)
                GUILayout.Label(Invariant($"Loading {_currentFullConfigArchivePath} ... ({TimeSinceStr(_resourcesTask.StartTime)})"));
            else if (_resourcesTask.Error != null)
                GUILayout.Label($"Failed initial load: {_resourcesTask.Error}");
            else
            {
                GUILayout.Label($"Loaded {_currentFullConfigArchivePath} .");
                GUILayout.Label(Invariant($"Total {_resources.NumBaselineItems} items in baseline shared game config."));

                DrawHorizontalLine();

                string PatchTitle(ExperimentVariantPair patchId)
                {
                    PatchStats patchStats = _resources.PatchStats[patchId];

                    int direct = patchStats.NumDirectlyPatchedItems;
                    int indirect = patchStats.NumIndirectlyPatchedItems;
                    int total = patchStats.NumItems;

                    return Invariant($"{patchId.ExperimentId}.{patchId.VariantId} (modifies {direct} directly + {indirect} indirectly out of {total} items)");
                }

                string dropdownTitle = _selectedPatchId.HasValue
                                       ? PatchTitle(_selectedPatchId.Value)
                                       : "<select>";

                GUILayout.BeginHorizontal();
                GUILayout.Label("Select variant: ", EditorStyles.boldLabel);
                if (EditorGUILayout.DropdownButton(new GUIContent(dropdownTitle), FocusType.Keyboard))
                {
                    IEnumerable<KeyValuePair<ExperimentVariantPair, PatchStats>> patchStatsEnumerable;

                    if (_patchDropdownSorting == PatchDropdownSorting.MostPropagatingFirst)
                    {
                        // Sort patches "most-propagating first", except keep variants of the same experiment
                        // grouped together. I.e. sort variants based on the most-propagation variant in the experiment.

                        OrderedDictionary<PlayerExperimentId, int> experimentMaxNumIndirect =
                            _resources.PatchStats
                            .GroupBy(kv => kv.Key.ExperimentId)
                            .ToOrderedDictionary(
                                g => g.Key,
                                g => g.Max(kv => kv.Value.NumIndirectlyPatchedItems));

                        patchStatsEnumerable = _resources.PatchStats.OrderByDescending(kv => experimentMaxNumIndirect[kv.Key.ExperimentId]);
                    }
                    else
                        patchStatsEnumerable = _resources.PatchStats;

                    GenericMenu menu = new GenericMenu();
                    ExperimentVariantPair? previousPatchId = null;
                    foreach ((ExperimentVariantPair patchId, PatchStats patchStats) in patchStatsEnumerable)
                    {
                        if (previousPatchId.HasValue && patchId.ExperimentId != previousPatchId.Value.ExperimentId)
                            menu.AddSeparator("");

                        int direct = patchStats.NumDirectlyPatchedItems;
                        int indirect = patchStats.NumIndirectlyPatchedItems;
                        int total = patchStats.NumItems;

                        menu.AddItem(
                            new GUIContent(PatchTitle(patchId)),
                            patchId == _selectedPatchId,
                            () => SelectPatch(patchId));

                        previousPatchId = patchId;
                    }
                    menu.ShowAsContext();
                }
                GUILayout.Space(5);
                GUILayout.Label("(Note: fully empty variants are omitted.)");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Above dropdown sorting: ");
                _patchDropdownSorting = (PatchDropdownSorting)GUILayout.SelectionGrid((int)_patchDropdownSorting, _patchDropdownSortingStrings, xCount: 1, EditorStyles.radioButton);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Label("- or -");

                GUILayout.BeginHorizontal();
                GUILayout.Label("Select individual items: ", EditorStyles.boldLabel);
                if (_itemsReachabilityTask?.IsOngoing == true)
                    GUILayout.Label("(loading...)");
                else if (_itemsReachabilityTask?.Error != null)
                    GUILayout.Label("(error)");
                else if (_selectedExplicitItems.Count > 0)
                {
                    GUILayout.BeginVertical();
                    List<ConfigItemId> itemsToUnselect = null;
                    foreach (ConfigItemId itemId in _selectedExplicitItems)
                    {
                        if (GUILayout.Button(Invariant($"{itemId.ItemType.Name}: {itemId.Key} (click to unselect)")))
                            (itemsToUnselect ??= new List<ConfigItemId>()).Add(itemId);
                    }
                    GUILayout.EndVertical();

                    if (itemsToUnselect != null)
                    {
                        foreach (ConfigItemId itemId in itemsToUnselect)
                            UnselectExplicitItem(itemId);
                    }
                }
                else if (_itemsReachability != null)
                    GUILayout.Label("<select using foldout below>");

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (_itemsReachabilityTask?.IsOngoing == true)
                    GUILayout.Label($"Resolving single-item reachabilities... progress {_itemsReachabilityTaskProgress} ({TimeSinceStr(_itemsReachabilityTask.StartTime)})");
                else if (_itemsReachabilityTask?.Error != null)
                    GUILayout.Label($"Failed to resolve single-item reachabilities: {_itemsReachabilityTask.Error}");
                else if (_itemsReachability != null)
                {
                    GUIContent singleItemReachabilitiesTitle = new GUIContent(
                        text: "Single-item reachabilities",
                        tooltip: "The \"reachability\" of an item is the number of items that transitively refer to it.");

                    if (_itemsReachability.UIExpanded = EditorGUILayout.Foldout(_itemsReachability.UIExpanded, singleItemReachabilitiesTitle, toggleOnLabelClick: true))
                    {
                        foreach ((Type itemType, ItemsReachabilityState.TypeItemsInfo typeItems) in _itemsReachability.ItemsByType)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(20);
                            typeItems.UIExpanded = EditorGUILayout.Foldout(typeItems.UIExpanded, Invariant($"{itemType.Name}: max reachability {typeItems.MaxReachability}"), toggleOnLabelClick: true);
                            GUILayout.EndHorizontal();
                            if (typeItems.UIExpanded)
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Space(40);
                                GUILayout.BeginVertical();
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("Search: ");
                                typeItems.SearchText = GUILayout.TextField(typeItems.SearchText, GUILayout.Width(200));
                                object matchingKey = null;
                                List<string> itemKeyStrs = new List<string>();
                                foreach ((object key, ItemsReachabilityState.ItemInfo itemInfo) in typeItems.Items)
                                {
                                    string keyStr = Util.ObjectToStringInvariant(key);

                                    if (keyStr == typeItems.SearchText)
                                    {
                                        matchingKey = key;
                                        itemKeyStrs.Insert(0, Invariant($"{keyStr}: reachability {itemInfo.Reachability}"));
                                    }
                                    else if (keyStr.IndexOf(typeItems.SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (matchingKey == null)
                                            matchingKey = key;

                                        itemKeyStrs.Add(Invariant($"{keyStr}: reachability {itemInfo.Reachability}"));
                                    }
                                }
                                if (matchingKey != null)
                                {
                                    if (GUILayout.Button(Invariant($"Select topmost ({matchingKey})")))
                                        SelectExplicitItem(new ConfigItemId(itemType, matchingKey));
                                }
                                GUILayout.FlexibleSpace();
                                GUILayout.EndHorizontal();
                                typeItems.UIScroll = GUILayout.BeginScrollView(typeItems.UIScroll, GUILayout.Height(200));
                                GUILayout.TextArea(string.Join("\n", itemKeyStrs));
                                GUILayout.EndScrollView();
                                GUILayout.EndVertical();
                                GUILayout.EndHorizontal();
                            }
                        }
                    }
                }

                DrawHorizontalLine();

                if (!_selectedPatchId.HasValue && _selectedExplicitItems.Count == 0)
                    GUILayout.Label("Select an experiment variant to see patch propagation caused by MetaRefs, or individual items to see which items refer to them transitively.");

                if (_patchStateTask?.IsOngoing == true)
                    GUILayout.Label(Invariant($"Loading {_selectedPatchId.Value.ExperimentId}/{_selectedPatchId.Value.VariantId} ... ({TimeSinceStr(_patchStateTask.StartTime)})"));
                else if (_patchStateTask?.Error != null)
                    GUILayout.Label($"Failed to load {_selectedPatchId.Value.ExperimentId}/{_selectedPatchId.Value.VariantId}: {_patchStateTask.Error}");
                else if (_patchState != null)
                {
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                "Direct modifications by this variant (hover here for info)",
                            tooltip:
                                "These are the keys of the items this variant is configured to modify."),
                        EditorStyles.boldLabel);
                    DrawItemSetGUI(_patchState.RootItems, count => Invariant($"{count} items directly modified"));

                    GUILayout.Space(10);
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                $"Indirect duplication by this variant (hover here for info)",
                            tooltip:
                                "The variant does not modify these items, but they must still be duplicated " +
                                "(instead of being shared with the baseline config) " +
                                "because they refer via MetaRefs (directly or indirectly) to items which are modified."),
                        EditorStyles.boldLabel);
                    DrawItemSetGUI(_patchState.IndirectlyPatchedItems, count => Invariant($"{count} items indirectly duplicated"));

                    GUILayout.Space(10);
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                "Indirect duplication by this variant, with the below MetaRef selection (hover here for info)",
                            tooltip:
                                $"This is like the above section, except this ignores the " +
                                $"MetaRefs which have been disabled in the below checkboxes."),
                        EditorStyles.boldLabel);

                    if (_analysisTask?.IsOngoing == true)
                        GUILayout.Label(Invariant($"Analyzing... ({TimeSinceStr(_analysisTask.StartTime)})"));
                    else if (_analysisTask?.Error != null)
                        GUILayout.Label($"Failed analysis: {_analysisTask.Error}");
                    else if (_analysisState != null)
                        DrawItemSetGUI(_analysisState.IndirectlyReachableItems, count => Invariant($"{count} items indirectly duplicated"));

                    GUILayout.Space(10);
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                "Encountered MetaRefs (hover here for info)",
                            tooltip:
                                "These are the MetaRefs that may be causing extra item duplication when this variant is used. " +
                                "The higher the \"Influence\" number, the more items are duplicated because of that MetaRef. " +
                                "\n\nYou can try unchecking some MetaRefs to pretend they didn't exist, in order to see " +
                                "how that would reduce the amount of duplication. " +
                                "The updated duplication information will be shown above." +
                                "\n\nYou can use the results to decide which MetaRefs are worth refactoring away (and using just " +
                                "a plain config id in its place)."),
                            EditorStyles.boldLabel);
                    DrawMetaRefControlsGUI(_patchState.AnalysisContext);
                }

                if (_explicitItemsStateTask?.IsOngoing == true)
                    GUILayout.Label(Invariant($"Loading the selected {_selectedExplicitItems.Count} items ... ({TimeSinceStr(_explicitItemsStateTask.StartTime)})"));
                else if (_explicitItemsStateTask?.Error != null)
                    GUILayout.Label($"Failed to load the selected {_selectedExplicitItems.Count} items: {_explicitItemsStateTask.Error}");
                else if (_explicitItemsState != null)
                {
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                "Items referring to the selected items (hover here for info)",
                            tooltip:
                                "These are the keys of the items which transitively refer to the selected items via MetaRefs."),
                        EditorStyles.boldLabel);
                    DrawItemSetGUI(_explicitItemsState.TransitivelyReferringItems, count => Invariant($"{count} transitively referring items"));

                    GUILayout.Space(10);
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                "Items referring to the selected items, with the below MetaRef selection (hover here for info)",
                            tooltip:
                                $"This is like the above section, except this ignores the " +
                                $"MetaRefs which have been disabled in the below checkboxes."),
                        EditorStyles.boldLabel);

                    if (_analysisTask?.IsOngoing == true)
                        GUILayout.Label(Invariant($"Analyzing... ({TimeSinceStr(_analysisTask.StartTime)})"));
                    else if (_analysisTask?.Error != null)
                        GUILayout.Label($"Failed analysis: {_analysisTask.Error}");
                    else if (_analysisState != null)
                        DrawItemSetGUI(_analysisState.IndirectlyReachableItems, count => Invariant($"{count} transitively referring items"));

                    GUILayout.Space(10);
                    GUILayout.Label(
                        new GUIContent(
                            text:
                                "Encountered MetaRefs (hover here for info)",
                            tooltip:
                                "These are the MetaRefs that were encountered while resolving the referring items. " +
                                "The higher the \"Influence\" number, the more items refer via that MetaRef. " +
                                "\n\nYou can try unchecking some MetaRefs to pretend they didn't exist, in order to see " +
                                "how that would reduce the amount of referring items. " +
                                "The updated referring items will be shown above."),
                            EditorStyles.boldLabel);
                    DrawMetaRefControlsGUI(_explicitItemsState.AnalysisContext);
                }
            }

            GUILayout.EndScrollView();
        }

        void DrawItemSetGUI(ItemSetState ctx, Func<int, string> getTitle)
        {
            string title = getTitle(ctx.ItemCount);
            if (ctx.UIExpanded = EditorGUILayout.Foldout(ctx.UIExpanded, title, toggleOnLabelClick: true))
            {
                foreach ((Type itemType, ItemTypeState itemTypeState) in ctx.ItemsByType)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    itemTypeState.UIExpanded = EditorGUILayout.Foldout(itemTypeState.UIExpanded, Invariant($"{itemType.Name}: {itemTypeState.Keys.Count} items"), toggleOnLabelClick: true);
                    GUILayout.EndHorizontal();
                    if (itemTypeState.UIExpanded)
                    {
                        StringBuilder itemKeysStr = new StringBuilder();
                        foreach (object itemKey in itemTypeState.Keys)
                            itemKeysStr.AppendLine(Util.ObjectToStringInvariant(itemKey));

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(40);
                        itemTypeState.UIScroll = GUILayout.BeginScrollView(itemTypeState.UIScroll, GUILayout.Height(200));
                        GUILayout.TextArea(itemKeysStr.ToString());
                        GUILayout.EndScrollView();
                        GUILayout.EndHorizontal();
                    }
                }
            }
        }

        void DrawHorizontalLine()
        {
            Color oldBackgroundColor = GUI.backgroundColor;

            GUI.backgroundColor = Color.gray;

            GUILayout.Box(
                GUIContent.none,
                new GUIStyle
                {
                    normal = new GUIStyleState
                    {
                        background = EditorGUIUtility.whiteTexture,
                    },
                    margin = new RectOffset(
                        left: 5,
                        right: 5,
                        top: 10,
                        bottom: 10),
                },
                GUILayout.Height(1));

            GUI.backgroundColor = oldBackgroundColor;
        }

        void DrawMetaRefControlsGUI(MetaRefAnalysisUtil.AnalysisContext analysisContext)
        {
            if (analysisContext.RelevantTypeLevelReferences.Count == 0)
                GUILayout.Label("No MetaRefs encountered");
            else
            {
                bool anyReferenceDisablementChanged = false;

                foreach (MetaRefAnalysisUtil.ItemTypeReference reference in analysisContext.RelevantTypeLevelReferences)
                {
                    GUILayout.BeginHorizontal();

                    bool wasEnabled = !analysisContext.DisabledTypeLevelReferences.Contains(reference);
                    bool isEnabled = EditorGUILayout.ToggleLeft($"{reference.From.Name} -> {reference.To.Name}, path {reference.From.Name}{reference.Path}", wasEnabled);

                    if (wasEnabled != isEnabled)
                    {
                        if (isEnabled)
                            analysisContext.DisabledTypeLevelReferences.Remove(reference);
                        else
                            analysisContext.DisabledTypeLevelReferences.Add(reference);

                        anyReferenceDisablementChanged = true;
                    }

                    string influence;
                    if (_analysisTask?.IsOngoing == true)
                        influence = "(analyzing...)";
                    else if (_analysisState != null)
                    {
                        string influenceNumber = _analysisState.MetaRefAnalysisResult.TypeLevelReferenceInfluences.GetValueOrDefault(reference, 0).ToString(CultureInfo.InvariantCulture);
                        if (wasEnabled)
                            influence = influenceNumber;
                        else
                            influence = $"{influenceNumber} (disabled)";
                    }
                    else
                        influence = "(error)";

                    GUILayout.Label($"Influence: {influence}", GUILayout.Width(200));

                    GUILayout.EndHorizontal();
                }

                if (anyReferenceDisablementChanged)
                    StartReferenceAnalysis(analysisContext);
            }
        }

        void SelectPatch(ExperimentVariantPair patchId)
        {
            _selectedExplicitItems.Clear();
            TryCancelTask(_explicitItemsStateTask);
            _explicitItemsStateTask = null;

            TryCancelTask(_analysisTask);
            _analysisTask = null;

            _selectedPatchId = patchId;
            TryCancelTask(_patchStateTask);
            _patchStateTask = StartTask(ct => MetaTask.Run(() => LoadPatch(patchId, ct), ct));

            Repaint();
        }

        PatchState LoadPatch(ExperimentVariantPair patchId, CancellationToken ct)
        {
            MetaRefAnalysisUtil.AnalysisContext analysisContext = MetaRefAnalysisUtil.CreateInitialAnalysisContextForPatch(
                _resources.TraversalResources,
                _resources.FullConfigImportResources.Shared,
                patchId);

            ItemSetState rootItemsCtx = CreateItemSetState(analysisContext.RootItems);
            ItemSetState indirectlyPatchedItemsCtx = CreateItemSetState(MetaRefAnalysisUtil.CollectIndirectlyPatchedItems(analysisContext.GameConfig));

            return new PatchState(
                analysisContext,
                rootItemsCtx,
                indirectlyPatchedItemsCtx);
        }

        void SelectExplicitItem(ConfigItemId itemId)
        {
            if (_selectedExplicitItems.Contains(itemId))
                return;

            _selectedPatchId = null;
            TryCancelTask(_patchStateTask);
            _patchStateTask = null;

            _selectedExplicitItems.Add(itemId);
            OnSelectedExplicitItemsSetUpdated();
        }

        void UnselectExplicitItem(ConfigItemId itemId)
        {
            _selectedExplicitItems.Remove(itemId);
            OnSelectedExplicitItemsSetUpdated();
        }

        void OnSelectedExplicitItemsSetUpdated()
        {
            TryCancelTask(_analysisTask);
            _analysisTask = null;

            TryCancelTask(_explicitItemsStateTask);

            if (_selectedExplicitItems.Count > 0)
            {
                OrderedSet<ConfigItemId> itemsCopySet = new OrderedSet<ConfigItemId>(_selectedExplicitItems);
                _explicitItemsStateTask = StartTask(ct => MetaTask.Run(() => LoadExplicitItems(itemsCopySet, ct), ct));
            }
            else
                _explicitItemsStateTask = null;

            Repaint();
        }

        ExplicitItemsState LoadExplicitItems(OrderedSet<ConfigItemId> itemIds, CancellationToken ct)
        {
            MetaRefAnalysisUtil.AnalysisContext analysisContext = MetaRefAnalysisUtil.CreateInitialAnalysisContext(
                _resources.TraversalResources,
                _resources.FullConfigImportResources.Shared.DeduplicationBaseline,
                itemIds);

            MetaRefAnalysisUtil.AnalysisResult initialAnalysisResult = MetaRefAnalysisUtil.Analyze(analysisContext);

            IEnumerable<ConfigItemId> transitivelyReferringItems =
                initialAnalysisResult.ReachableItems
                .Where(id => !itemIds.Contains(id));

            ItemSetState transitivelyReferringItemsCtx = CreateItemSetState(transitivelyReferringItems);

            return new ExplicitItemsState(
                analysisContext,
                transitivelyReferringItemsCtx);
        }

        void StartReferenceAnalysis(MetaRefAnalysisUtil.AnalysisContext analysisContext)
        {
            TryCancelTask(_analysisTask);
            _analysisTask = StartTask(ct => MetaTask.Run(() => Analyze(analysisContext, ct), ct));
            Repaint();
        }

        AnalysisState Analyze(MetaRefAnalysisUtil.AnalysisContext analysisContext, CancellationToken ct)
        {
            MetaRefAnalysisUtil.AnalysisResult metaRefAnalysisResult = MetaRefAnalysisUtil.Analyze(analysisContext);

            IEnumerable<ConfigItemId> indirectlyReachableItems =
                metaRefAnalysisResult.ReachableItems
                .Where(id => !analysisContext.RootItems.Contains(id));

            ItemSetState indirectlyReachableItemsCtx = CreateItemSetState(indirectlyReachableItems);

            return new AnalysisState(
                metaRefAnalysisResult,
                indirectlyReachableItemsCtx);
        }

        void StartResolveItemsReachability()
        {
            _itemsReachabilityTaskProgress = new TaskProgress();
            _itemsReachabilityTaskProgress.Total = _resources.NumBaselineItems;
            TryCancelTask(_itemsReachabilityTask);
            _itemsReachabilityTask = StartTask(ct => MetaTask.Run(() => ResolveItemsReachability(_resources, _itemsReachabilityTaskProgress, ct), ct));
            Repaint();
        }

        ItemsReachabilityState ResolveItemsReachability(CommonResources resources, TaskProgress progress, CancellationToken ct)
        {
            IGameConfig config = resources.FullConfigImportResources.Shared.DeduplicationBaseline;

            OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> reverseReferences = MetaRefAnalysisUtil.CollectItemReverseReferences(resources.TraversalResources, config);

            ItemsReachabilityState result = new ItemsReachabilityState();

            foreach ((ConfigItemId itemId, int itemIndex) in MetaRefAnalysisUtil.CollectAllItems(config).ZipWithIndex())
            {
                int reachability = Util.ComputeReachableNodes(
                    new OrderedSet<ConfigItemId> { itemId },
                    tryGetNodeNeighbors: (ConfigItemId id) => reverseReferences.GetValueOrDefault(id)?.Select(kv => kv.Item1)).Count - 1; // -1 to not count itemId itself

                if (!result.ItemsByType.TryGetValue(itemId.ItemType, out ItemsReachabilityState.TypeItemsInfo typeItems))
                {
                    typeItems = new ItemsReachabilityState.TypeItemsInfo();
                    result.ItemsByType.Add(itemId.ItemType, typeItems);
                }

                typeItems.Items.Add((itemId.Key, new ItemsReachabilityState.ItemInfo(reachability)));
                typeItems.MaxReachability = System.Math.Max(typeItems.MaxReachability, reachability);

                progress.Current = itemIndex+1;

                if (ct.IsCancellationRequested)
                    throw new TaskCanceledException();
            }

            foreach (ItemsReachabilityState.TypeItemsInfo itemsInfo in result.ItemsByType.Values)
                itemsInfo.Items = itemsInfo.Items.OrderByDescending(item => item.Info.Reachability).ToList();

            result.ItemsByType = new OrderedDictionary<Type, ItemsReachabilityState.TypeItemsInfo>(
                result.ItemsByType
                .OrderByDescending(kv => kv.Value.MaxReachability));

            return result;
        }

        static ItemSetState CreateItemSetState(IEnumerable<ConfigItemId> itemIds)
        {
            ItemSetState itemSet = new ItemSetState();
            foreach (ConfigItemId itemId in itemIds)
            {
                if (!itemSet.ItemsByType.TryGetValue(itemId.ItemType, out ItemTypeState itemTypeState))
                {
                    itemTypeState = new ItemTypeState();
                    itemSet.ItemsByType.Add(itemId.ItemType, itemTypeState);
                }

                itemTypeState.Keys.Add(itemId.Key);
            }

            return itemSet;
        }
    }
}
