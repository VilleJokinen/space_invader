// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;

namespace Metaplay.Client.Unity
{
    /// <summary>
    /// Class to get a list of all available non-null Models. This class can be inherited
    /// and <see cref="GetModels"/> overridden to implement custom way to get Models
    /// in case the default getter does not discover all Models.
    /// </summary>
    public class ModelInspectorSourceProvider : IMetaIntegrationSingleton<ModelInspectorSourceProvider>
    {
        public virtual List<IModel> GetModels()
        {
            // Gets Models for each ClientSlot registered in ClientStore
            if (ClientSlot.AllValues == null)
                return new List<IModel>();

            List<IModel> models = new List<IModel>();

            foreach (ClientSlot clientSlot in ClientSlot.AllValues)
            {
                IEntityClientContext clientContext = MetaplaySDK.SessionContext?.ClientStore?.TryGetEntityClientContext(clientSlot);
                IModel clientModel = clientContext?.Model;
                if (clientModel != null)
                {
                    models.Add(clientModel);
                }
            }

            return models;
        }
    }

    public class ModelInspectorWindow : EditorWindow
    {
#region ModelInspector

        public const int MaximumTreeDepth       = 32;
        public const int ValuePreviewChildCount = 5;

        IModel               _model;
        ValueProxyBase       _rootProxy;
        Vector2              _scrollPos   = Vector2.zero;
        bool                 _initialized = false;
        List<IModel>         _models;
        List<ValueProxyBase> _footerBreadcrumbsList = new List<ValueProxyBase>();
        ModelInspectorArgs   _modelInspectorArgs;

        // UI implementation specific fields (IMGUI)
        [NonSerialized]  bool                   _treeViewInitialized;
        [NonSerialized]  TreeViewState          _treeViewState;
        [SerializeField] MultiColumnHeaderState _multiColumnHeaderState;
        SearchField                             _searchField;
        ModelInspectorTreeView<ValueProxyBase>  _treeView;
        GUIStyle                                _footerBoxStyle = null;

        float TreeViewWidth => position.width - 6;
        float TreeViewHeight => position.height - 112;

        public class ModelInspectorArgs
        {
            public bool         ShowReadOnlyProperties   = false;
            public bool         ShowBaseClassMembers     = false;
            public bool         ErrorNoModelFound        = false;
            public bool         ErrorFailedToRenderModel = false;
            public bool         PauseModelUpdate         = false;
            public string       SearchFilter             = "";
            public HashSet<int> VisibleProxies           = new HashSet<int>();
        }

        [MenuItem("Metaplay / Model Inspector")]
        public static void OpenModelInspector()
        {
            ModelInspectorWindow window = GetWindow<ModelInspectorWindow>();
            window.titleContent = new GUIContent("Model Inspector");
            window.minSize = new Vector2(200.0f, 200.0f);
            window.Show();
        }

        /// <summary>
        /// Updates the list of possible Models to inspect.
        /// </summary>
        void UpdateModelList()
        {
            _models = IntegrationRegistry.Get<ModelInspectorSourceProvider>().GetModels();
        }

        void OnModelDropdownChoice(object model)
        {
            UpdateModel((IModel)model);
        }

        void UpdateModel(IModel model)
        {
            // Already the same Model, do nothing
            if (_model == model && _initialized)
            {
                return;
            }

            _model = model;

            // Clear old data
            _rootProxy = null;

            // Handle null Model
            if (_model == null)
            {
                _modelInspectorArgs.ErrorNoModelFound = true;
                return;
            }
            _modelInspectorArgs.ErrorNoModelFound = false;

            // Initialize
            try
            {
                // Create root proxy
                _rootProxy = ValueProxyBase.CreateProxy(depth: 0, typeof(IModel), null);
                // Build proxy tree
                _rootProxy.Update(_model.GetType().Name, _model, recursive: true, isRoot: true, forceRefreshTexts: true,
                    appendTypeToName: false, args: _modelInspectorArgs);

                _initialized = true;
                _modelInspectorArgs.ErrorFailedToRenderModel = false;

                // Init IMGUI tree view
                _treeViewInitialized = false;
                InitIMGUITreeView();

                // Rebuild UI side tree
                _treeView.Reload();
            }
            catch (Exception ex)
            {
                _rootProxy = null;
                _model     = null;

                Debug.LogWarning($"Failed to render model: {ex}");
                _modelInspectorArgs.ErrorFailedToRenderModel = true;
            }
        }

        public void OnEnable()
        {
            // Set window title
            titleContent        = new GUIContent("Model Inspector");
            _model              = null;
            _initialized        = false;
            _modelInspectorArgs = new ModelInspectorArgs();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _model              = null;
                _initialized        = false;
                _modelInspectorArgs = new ModelInspectorArgs();
                _footerBoxStyle     = null;
            }
        }

        void OnInspectorUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (!_modelInspectorArgs.PauseModelUpdate && !_modelInspectorArgs.ErrorFailedToRenderModel)
            {
                try
                {
                    // Ensure that we're visualizing the correct Model, or if no Model chosen default to PlayerModel
                    if (_model == null)
                        UpdateModel(MetaplaySDK.SessionContext?.PlayerContext?.Model);
                    else
                        UpdateModel(_model);

                    // Refresh the Model list to check that the current Model is still included.
                    // The Model reference can become out-of-date, for example in the case of a terminated and restarted session.
                    // Note: Automatically switches to inspect the first matching type Model found in the updated Model list.
                    // In the case of multiple Models of the same type, this might update to an incorrect Model.
                    UpdateModelList();
                    if (_models.Count > 0) // Don't change selected Model while no Models are available
                    {
                        if (_model != null && !_models.Contains(_model))
                        {
                            // Current Model is out of date, so update to the first matching type Model found in the updated Model list
                            bool modelChanged = false;
                            Type currentModelType = _model.GetType();
                            foreach (IModel model in _models)
                            {
                                if (model.GetType() == currentModelType)
                                {
                                    if (_models.Select(m => m.GetType() == currentModelType).Count() > 1)
                                        Debug.LogWarning($"Metaplay Model Inspector: Selected Model ({currentModelType}) has been automatically changed to a matching type Model due to an out-of-date Model reference. Multiple Models of the same type have been detected, so check that the selected Model is correct.");
                                    else
                                        Debug.Log($"Metaplay Model Inspector: Selected Model ({currentModelType}) has been automatically changed to a matching type Model due to an out-of-date Model reference.");
                                    UpdateModel(model);
                                    modelChanged = true;
                                    break;
                                }
                            }

                            if (!modelChanged)
                            {
                                // No matching Model found, default to PlayerModel
                                Debug.LogWarning($"Metaplay Model Inspector: Selected Model ({currentModelType}) has been automatically changed to PlayerModel due to an out-of-date Model reference. Defaulted to PlayerModel, because no matching type Models were found.");
                                UpdateModel(MetaplaySDK.SessionContext?.PlayerContext?.Model);
                            }
                        }
                    }

                    // Update all bound values
                    if (_rootProxy != null && _model != null && _initialized)
                    {
                        GetVisibleProxyIds(_modelInspectorArgs.VisibleProxies);
                        _rootProxy.Update(_model.GetType().Name, _model, recursive: true, isRoot: true, forceRefreshTexts: false,
                            appendTypeToName: false, args: _modelInspectorArgs);
                        // Rebuild UI side tree
                        _treeView.Reload();
                        this.Repaint();
                    }
                }
                catch (Exception ex)
                {
                    _rootProxy = null;
                    _model     = null;

                    Debug.LogWarning($"Failed to render Model: {ex}");
                    _modelInspectorArgs.ErrorFailedToRenderModel = true;
                }
            }
        }

        /// <summary>
        /// Updates the proxy tree and forces all proxies to update texts.
        /// </summary>
        void UpdateTreeFull()
        {
            if (!EditorApplication.isPlaying || _rootProxy == null || _model == null || !_initialized)
                return;

            try
            {
                _rootProxy.Update(_model.GetType().Name, _model, true, true, true,
                    false, _modelInspectorArgs);
            }
            catch (Exception ex)
            {
                _rootProxy = null;
                _model     = null;

                Debug.LogWarning($"Failed to render Model: {ex}");
                _modelInspectorArgs.ErrorFailedToRenderModel = true;
            }
        }

#endregion
#region UI

        void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.BeginVertical(GUILayout.MaxWidth(400));
                GUILayout.Label("Welcome to the Metaplay Model Inspector window!", EditorStyles.boldLabel);
                GUILayout.Label(
                    "This window allows you to inspect " +
                    "serializable Metaplay Models such as the PlayerModel, GuildModel and any other Models that implement the IModel interface.",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox("Enter Play Mode to get started.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // Create styles here as some are based on default styles that can only be accessed in OnGUI
            if (_footerBoxStyle == null)
                _footerBoxStyle = CreateFooterBoxStyle();

            try
            {
                // Update GUI elements
                UpdateModelGUI(_rootProxy);
            }
            catch (Exception ex)
            {
                _rootProxy = null;
                _model     = null;

                Debug.LogWarning($"Failed to render Model: {ex}");
                _modelInspectorArgs.ErrorFailedToRenderModel = true;
            }
        }

        void UpdateModelGUI(ValueProxyBase rootProxy)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            // Choose Model dropdown menu
            GUILayout.Label("Selected Model: ", EditorStyles.boldLabel);
            GUILayout.Space(10);
            string dropdownTitle = "Choose Model";
            if (_model != null)
            {
                dropdownTitle = _model.GetType().Name;
            }
            if (EditorGUILayout.DropdownButton(new GUIContent(dropdownTitle), FocusType.Keyboard, GUILayout.MinWidth(110)))
            {
                // Update available Models
                UpdateModelList();
                // Create and show menu to choose Model to inspect
                GenericMenu menu = new GenericMenu();
                foreach (IModel model in _models)
                {
                    menu.AddItem(new GUIContent(model.GetType().Name), _model == model, OnModelDropdownChoice, model);
                }
                menu.ShowAsContext();
            }
            GUILayout.FlexibleSpace();

            // Pause Model updating button
            if (_modelInspectorArgs.PauseModelUpdate)
            {
                if (GUILayout.Button("Resume updating", GUILayout.MaxWidth(110)))
                {
                    _modelInspectorArgs.PauseModelUpdate = false;
                }
            }
            else
            {
                if (GUILayout.Button("Pause updating", GUILayout.MaxWidth(110)))
                {
                    _modelInspectorArgs.PauseModelUpdate = true;
                    // Update the whole tree including texts so the whole tree can be explored while paused without regular updates
                    UpdateTreeFull();
                }
            }
            EditorGUILayout.EndHorizontal();
            //EditorGUILayout.Space(1);

            // Error messages
            if (_modelInspectorArgs.ErrorNoModelFound)
                EditorGUILayout.LabelField("No Model found!");

            if (_modelInspectorArgs.ErrorFailedToRenderModel)
                EditorGUILayout.LabelField("Failed to render Model, see console for detailed error.");

            if (_model != null && rootProxy != null)
            {
                // Filter toggles
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Show read-only properties ", GUILayout.Width(204));
                bool showReadOnly = EditorGUILayout.Toggle(_modelInspectorArgs.ShowReadOnlyProperties);
                if (showReadOnly != _modelInspectorArgs.ShowReadOnlyProperties)
                {
                    _modelInspectorArgs.ShowReadOnlyProperties = showReadOnly;
                    _treeView.Reload();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Show base class members ", GUILayout.Width(204));
                bool showBaseClass = EditorGUILayout.Toggle(_modelInspectorArgs.ShowBaseClassMembers);
                if (showBaseClass != _modelInspectorArgs.ShowBaseClassMembers)
                {
                    _modelInspectorArgs.ShowBaseClassMembers = showBaseClass;
                    _treeView.Reload();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);

                // Search bar
                bool searchWasEmpty = _modelInspectorArgs.SearchFilter == "";
                EditorGUILayout.BeginHorizontal();
                _modelInspectorArgs.SearchFilter = _searchField.OnGUI(_modelInspectorArgs.SearchFilter);
                _treeView.searchString           = _modelInspectorArgs.SearchFilter;
                EditorGUILayout.EndHorizontal();

                // Do a full update of the tree including all texts at the start of a search so that search can compare the texts
                if (searchWasEmpty && _modelInspectorArgs.SearchFilter != "" && !_modelInspectorArgs.PauseModelUpdate)
                {
                    UpdateTreeFull();
                }

                // Tree view
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
                // Calculate tree view rect
                Rect treeViewRect = GUILayoutUtility.GetRect(0f, TreeViewWidth, 0f, TreeViewHeight);
                treeViewRect.xMin = 3f;
                treeViewRect.xMax = 3f + TreeViewWidth;
                // Update IMGUI treeview
                _treeView.OnGUI(treeViewRect);
                EditorGUILayout.EndScrollView();

                // Footer
                ValueProxyBase selectedProxy = _treeView.GetSelectedItem();
                if (selectedProxy != null)
                    GUILayout.Box(ToFooterString(selectedProxy), _footerBoxStyle, GUILayout.ExpandWidth(true));
                else
                    GUILayout.Box($"", _footerBoxStyle, GUILayout.ExpandWidth(true));
            }
            EditorGUILayout.EndVertical();
        }

        public void ClearSearch()
        {
            _modelInspectorArgs.SearchFilter = "";
            _treeView.searchString           = "";
        }

        string ToFooterString(ValueProxyBase proxy)
        {
            StringBuilder sb = new StringBuilder();
            _footerBreadcrumbsList.Clear();
            proxy.AddParentsToListRecursive(_footerBreadcrumbsList);
            _footerBreadcrumbsList.Reverse();
            foreach (ValueProxyBase parent in _footerBreadcrumbsList)
            {
                sb.Append(parent.UpdateNameText());
                sb.Append(" > ");
            }

            sb.Append(proxy.UpdateNameText());
            sb.Append($" | Type: {proxy.ValueType.ToGenericTypeString()}");
            return sb.ToString();
        }

        void GetVisibleProxyIds(HashSet<int> proxies)
        {
            _treeView.GetVisibleItemIds(proxies);
        }

        GUIStyle CreateFooterBoxStyle()
        {
            GUIStyle style = new GUIStyle();
            style.name = "model-inspector-footer-style";
            Color textColor = EditorGUIUtility.isProSkin ? new Color(0.824f, 0.824f, 0.824f, 1f) : new Color(0f, 0f, 0f, 1f);
            style.normal.textColor    = textColor;
            style.hover.textColor     = textColor;
            style.active.textColor    = textColor;
            style.focused.textColor   = textColor;
            style.onNormal.textColor  = textColor;
            style.onHover.textColor   = textColor;
            style.onActive.textColor  = textColor;
            style.onFocused.textColor = textColor;
            style.alignment           = TextAnchor.MiddleLeft;
            style.fontStyle           = FontStyle.Bold;
            style.padding             = new RectOffset(5, 5, 3, 3);
            style.wordWrap            = true;
            return style;
        }

        void InitIMGUITreeView()
        {
            if (!_treeViewInitialized)
            {
                // Check if it already exists (deserialized from window layout file or scriptable object)
                if (_treeViewState == null)
                    _treeViewState = new TreeViewState();

                bool firstInit = _multiColumnHeaderState == null;
                MultiColumnHeaderState headerState = ModelInspectorTreeView<ValueProxyBase>.CreateDefaultMultiColumnHeaderState(TreeViewWidth);
                if (MultiColumnHeaderState.CanOverwriteSerializedFields(_multiColumnHeaderState, headerState))
                    MultiColumnHeaderState.OverwriteSerializedFields(_multiColumnHeaderState, headerState);
                _multiColumnHeaderState = headerState;

                MultiColumnHeader multiColumnHeader = new MultiColumnHeader(headerState);
                if (firstInit)
                    multiColumnHeader.ResizeToFit();

                _treeView = new ModelInspectorTreeView<ValueProxyBase>(_treeViewState, multiColumnHeader, _rootProxy, this);

                _searchField = new SearchField();
                _searchField.downOrUpArrowKeyPressed += _treeView.SetFocusAndEnsureSelectedItem;

                _treeViewInitialized = true;
            }
        }

        public class TreeViewItem<T> : TreeViewItem where T : ValueProxyBase
        {
            public T Data { get; set; }

            public TreeViewItem (int id, int depth, string displayName, T data) : base (id, depth, displayName)
            {
                this.Data = data;
            }
        }

        public class ModelInspectorTreeView<T> : TreeView where T : ValueProxyBase
        {
            const float RowHeight = 20f;
            const float ExtraSpaceBeforeLabel = 0f;

            enum MyColumns
            {
                Name,
                Value,
            }

            readonly GUIStyle             _valuePreviewStyle;
            readonly T                    _treeRoot;
            readonly ModelInspectorWindow _modelInspectorWindow;
            readonly List<TreeViewItem>   _rows = new List<TreeViewItem>(100);

            public ModelInspectorTreeView(TreeViewState state, MultiColumnHeader multiColumnHeader, T treeRoot, ModelInspectorWindow modelInspectorWindow) : base(state, multiColumnHeader)
            {
                this._treeRoot             = treeRoot;
                this._valuePreviewStyle    = CreateValuePreviewStyle();
                this._modelInspectorWindow = modelInspectorWindow;

                // Custom setup
                rowHeight                     = RowHeight;
                columnIndexForTreeFoldouts    = 0;
                showAlternatingRowBackgrounds = true;
                showBorder                    = true;
                customFoldoutYOffset          = 0f;
                extraSpaceBeforeIconAndLabel  = ExtraSpaceBeforeLabel;
                multiColumnHeader.canSort     = false;
                multiColumnHeader.height      = MultiColumnHeader.DefaultGUI.minimumHeight;

                // Resize to fit when columns are resized so that value column always fills available space
                multiColumnHeader.columnSettingsChanged += (int column) => multiColumnHeader.ResizeToFit();

                Reload();
                multiColumnHeader.ResizeToFit();
            }

            public void GetVisibleItems(HashSet<ValueProxyBase> proxies)
            {
                if (proxies == null)
                    proxies = new HashSet<ValueProxyBase>();
                else
                    proxies.Clear();

                GetFirstAndLastVisibleRows(out int first, out int last);
                int rowIndex = 0;
                foreach (TreeViewItem<T> rowItem in GetRows())
                {
                    if (rowIndex > last)
                        break;
                    if (rowIndex >= first)
                        proxies.Add(rowItem.Data);
                    rowIndex++;
                }
            }

            public void GetVisibleItemIds(HashSet<int> proxies)
            {
                if (proxies == null)
                    proxies = new HashSet<int>();
                else
                    proxies.Clear();

                GetFirstAndLastVisibleRows(out int first, out int last);
                int rowIndex = 0;
                foreach (TreeViewItem<T> rowItem in GetRows())
                {
                    if (rowIndex > last)
                        break;
                    if (rowIndex >= first)
                        proxies.Add(rowItem.Data.Id);
                    rowIndex++;
                }
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                TreeViewItem<T> item = (TreeViewItem<T>)args.item;

                for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                {
                    CellGUI(args.GetCellRect(i), item, (MyColumns)args.GetColumn(i), ref args);
                }
            }

            void CellGUI(Rect cellRect, TreeViewItem<T> item, MyColumns column, ref RowGUIArgs args)
            {
                // Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
                CenterRectUsingSingleLineHeight(ref cellRect);

                switch (column)
                {
                    case MyColumns.Name:
                    {
                        args.rowRect = cellRect;
                        CenterNameCellRect(ref args.rowRect);
                        // Refresh name (needed when for example collection count changes)
                        item.displayName = item.Data.NameText;
                        // Draws appropriate label including foldout if needed
                        base.RowGUI(args);
                    }
                    break;

                    case MyColumns.Value:
                    {
                        // Draw content preview value text in separate style for the types that can have content preview value text
                        if (item.Data is ObjectValueProxy || item.Data is CollectionValueProxy || item.Data is FixedPointVectorProxy)
                            EditorGUI.LabelField(cellRect, item.Data.ValueText, _valuePreviewStyle);
                        else
                            DefaultGUI.Label(cellRect, item.Data.ValueText, args.selected, args.focused);
                    }
                    break;
                }
            }

            void CenterNameCellRect(ref Rect rect)
            {
                rect.y      += 2;
                rect.height =  EditorGUIUtility.singleLineHeight;
            }

            protected override TreeViewItem BuildRoot()
            {
                int depthForHiddenRoot = -1;

                TreeViewItem<T> rootItem = new TreeViewItem<T>(_treeRoot.Id, depthForHiddenRoot, _treeRoot.UpdateNameText(), _treeRoot);
                //BuildTreeRecursive(rootItem, 0);
                return rootItem;
            }

            /// <summary>
            /// Builds the complete tree starting with the root
            /// </summary>
            void BuildTreeRecursive(TreeViewItem<T> parent, int depth)
            {
                if (parent.children == null)
                    parent.children = new List<TreeViewItem>(parent.Data.Children.Count);
                else
                    parent.children.Clear();

                foreach (T child in parent.Data.Children)
                {
                    TreeViewItem<T> childItem = new TreeViewItem<T>(child.Id, depth, child.NameText, child);
                    parent.AddChild(childItem);

                    if (child.Children.Count > 0)
                        BuildTreeRecursive(childItem, depth + 1);
                }
            }

            /// <summary>
            /// Builds rows that are currently in the tree view taking into account filtering and expanded state of items.
            /// </summary>
            protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
            {
                _rows.Clear();
                if (!string.IsNullOrEmpty(searchString))
                {
                    Search(_treeRoot, searchString, _rows);
                }
                else
                {
                    if (_treeRoot?.Children.Count > 0)
                        BuildRowsRecursive(_treeRoot, 0, _rows);
                }

                // We still need to setup the child parent information for the rows since this
                // information is used by the TreeView internal logic (navigation, dragging etc)
                SetupParentsAndChildrenFromDepths(root, _rows);

                return _rows;
            }

            void Search(T searchTarget, string search, List<TreeViewItem> result)
            {
                if (string.IsNullOrEmpty(search))
                    throw new ArgumentException("Invalid search: cannot be null or empty", nameof(search));

                const int tempItemDepth = 0; // tree is flattened when showing search results

                // Push children of searchTarget to search stack
                Stack<T> stack = new Stack<T>();
                foreach (ValueProxyBase element in searchTarget.Children)
                {
                    // Filter based on current active toggles
                    if (IsElementFilteredByToggles((T)element, element.Depth))
                        continue;

                    stack.Push((T)element);
                }

                while (stack.Count > 0)
                {
                    // Pop and check if element in stack matches search
                    T current = stack.Pop();
                    if (current.NameText.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) != -1
                        || current.ValueText.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) != -1)
                    {
                        result.Add(new TreeViewItem<T>(current.Id, tempItemDepth, current.NameText, current));
                    }

                    // Add children of popped element to stack
                    if (current.Children != null && current.Children.Count > 0)
                    {
                        foreach (ValueProxyBase element in current.Children)
                        {
                            // Filter based on current active toggles
                            if (IsElementFilteredByToggles((T)element, element.Depth))
                                continue;

                            stack.Push((T)element);
                        }
                    }
                }

                result.Reverse();
            }

            public ValueProxyBase GetSelectedItem()
            {
                return ((TreeViewItem<ValueProxyBase>)FindItem(GetSelection().FirstOrDefault(), rootItem))?.Data;
            }

            protected override void KeyEvent()
            {
                if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    if (!hasSearch)
                        return;

                    TreeViewItem<ValueProxyBase> selectedItem = (TreeViewItem<ValueProxyBase>)FindItem(GetSelection().FirstOrDefault(), rootItem);
                    if (selectedItem != null)
                        JumpToItem(selectedItem.id);
                }

                // Ctrl + C on Windows or linux, or command + C on OSX, to copy selected value to clipboard
#if UNITY_EDITOR_OSX
                if (Event.current.isKey && Event.current.command && Event.current.keyCode == KeyCode.C)
#else
                if (Event.current.isKey && Event.current.control && Event.current.keyCode == KeyCode.C)
#endif
                {
                    TreeViewItem<ValueProxyBase> selectedItem = ((TreeViewItem<ValueProxyBase>)FindItem(GetSelection().FirstOrDefault(), rootItem));
                    EditorGUIUtility.systemCopyBuffer = selectedItem?.Data.ValueText;
                }
            }

            protected override void ContextClickedItem(int id)
            {
                // Right clicking a row shows a 'Copy value' option
                TreeViewItem<ValueProxyBase> clickedItem = ((TreeViewItem<ValueProxyBase>)FindItem(id, rootItem));
                if (clickedItem != null)
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Copy value"), false, () => { EditorGUIUtility.systemCopyBuffer = clickedItem.Data.ValueText; });
                    menu.ShowAsContext();
                }
            }

            protected override void DoubleClickedItem(int id)
            {
                if (!hasSearch)
                    return;

                JumpToItem(id);
            }

            /// <summary>
            /// Escapes search mode and scrolls to the <see cref="TreeViewItem{T}"/> with the given id.
            /// </summary>
            void JumpToItem(int id)
            {
                // Escape from search and expand clicked item and scroll to show clicked item
                _modelInspectorWindow.ClearSearch();

                foreach (int ancestor in GetAncestors(id))
                {
                    SetExpanded(ancestor, true);
                }
                Reload();
                Rect rowRect = GetRowRect(FindRowOfItem(FindItem(id, rootItem)));
                state.scrollPos = new Vector2(state.scrollPos.x, rowRect.y - rowHeight * 3);
            }

            /// <summary>
            /// Builds rows recursively taking into account expanded state of foldouts. Filtering elements based on toggles happens here.
            /// </summary>
            void BuildRowsRecursive(T parent, int depth, IList<TreeViewItem> newRows)
            {
                foreach (T child in parent.Children)
                {
                    // Filter elements hidden by toggles (such as readonly, base class members of root)
                    if (IsElementFilteredByToggles(child, depth + 1))
                        continue;

                    TreeViewItem<T> item = new TreeViewItem<T>(child.Id, depth, child.NameText, child);
                    newRows.Add(item);

                    if (child.Children.Count > 0)
                    {
                        if (IsExpanded(child.Id))
                        {
                            BuildRowsRecursive(child, depth + 1, newRows);
                        }
                        else
                        {
                            item.children = CreateChildListForCollapsedParent();
                        }
                    }
                }
            }

            /// <summary>
            /// Checks if element is filtered by current state of Model Inspector filter toggles, such as 'Show read-only properties' and 'Show base class members'
            /// </summary>
            /// <param name="element">Element to check</param>
            /// <param name="depth">Depth of element</param>
            /// <returns>True, if element is filtered. False otherwise.</returns>
            bool IsElementFilteredByToggles(T element, int depth)
            {
                // Filter for properties without setter / readonly properties
                if (!_modelInspectorWindow._modelInspectorArgs.ShowReadOnlyProperties && !element.HasSetter)
                    return true;

                // Filter for base class members. Filter toggle only applies to depth 0 or members directly under root.
                // Base class members deeper in the tree are shown by default regardless of toggle.
                if (depth == 1) // (parent is ObjectValueProxy)
                {
                    if (!_modelInspectorWindow._modelInspectorArgs.ShowBaseClassMembers && element.Parent is ObjectValueProxy parent && parent.BaseMembers.Contains(element))
                        return true;
                }

                return false;
            }

            protected override IList<int> GetDescendantsThatHaveChildren(int id)
            {
                ValueProxyBase parent = _treeRoot.FindChild(id, true);
                if (parent == null)
                {
                    Debug.LogWarning("Metaplay Model Inspector: Could not find Id: " + id + " under Id: " + _treeRoot.Id + " " + _treeRoot.UpdateNameText());
                    return new List<int>();
                }

                Stack<ValueProxyBase> searchStack = new Stack<ValueProxyBase>();
                searchStack.Push(parent);

                List<int> descendantsThatHaveChildren = new List<int>();
                while (searchStack.Count > 0)
                {
                    ValueProxyBase current = searchStack.Pop();
                    if (current.HasChildren)
                    {
                        descendantsThatHaveChildren.Add(current.Id);
                        foreach (ValueProxyBase child in current.Children)
                        {
                            searchStack.Push(child);
                        }
                    }
                }
                return descendantsThatHaveChildren;
            }

            protected override IList<int> GetAncestors (int id)
            {
                List<int> parents = new List<int>();
                ValueProxyBase targetProxy = _treeRoot.FindChild(id, true);
                if (targetProxy == null)
                    Debug.LogWarning("Metaplay Model Inspector: Could not find Id: " + id + " under Id: " + _treeRoot.Id + " " + _treeRoot.UpdateNameText());
                if (targetProxy != null)
                {
                    while (targetProxy.Parent != null)
                    {
                        parents.Add(targetProxy.Parent.Id);
                        targetProxy = targetProxy.Parent;
                    }
                }
                return parents;
            }

            public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(float treeViewWidth)
            {
                MultiColumnHeaderState.Column[] columns = new[]
                {
                    new MultiColumnHeaderState.Column
                    {
                        headerContent         = new GUIContent("Name"),
                        headerTextAlignment   = TextAlignment.Left,
                        sortedAscending       = true,
                        sortingArrowAlignment = TextAlignment.Left,
                        width                 = 300,
                        minWidth              = 60,
                        autoResize            = false,
                        allowToggleVisibility = false,
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent         = new GUIContent("Value"),
                        headerTextAlignment   = TextAlignment.Left,
                        sortedAscending       = true,
                        sortingArrowAlignment = TextAlignment.Left,
                        width                 = 300,
                        minWidth              = 60,
                        autoResize            = true,
                        allowToggleVisibility = false,
                    },
                };

                Assert.AreEqual(columns.Length, Enum.GetValues(typeof(MyColumns)).Length, "Number of columns should match number of enum values: You probably forgot to update one of them.");

                MultiColumnHeaderState state = new MultiColumnHeaderState(columns);
                return state;
            }

            public static GUIStyle CreateValuePreviewStyle()
            {
                GUIStyle style = new GUIStyle();
                style.name = "value_preview_style";
                Color textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f, 0.65f) : new Color(0f, 0f, 0f, 0.65f);
                style.normal.textColor    = textColor;
                style.hover.textColor     = textColor;
                style.active.textColor    = textColor;
                style.focused.textColor   = textColor;
                style.onNormal.textColor  = textColor;
                style.onHover.textColor   = textColor;
                style.onActive.textColor  = textColor;
                style.onFocused.textColor = textColor;
                style.margin              = new RectOffset(0, 0, 2, 2);
                style.padding             = new RectOffset(4, 4, 2, 2);
                style.fontSize            = DefaultStyles.label.fontSize - 1;
                style.fontStyle           = FontStyle.Italic;
                style.alignment           = TextAnchor.MiddleLeft;

                return style;
            }
        }
    }
#endregion
}
