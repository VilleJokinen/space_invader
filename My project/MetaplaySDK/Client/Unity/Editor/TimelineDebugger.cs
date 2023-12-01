// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Unity;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class TimelineDebugger : EditorWindow
{
    class Tick { }

    // Styles
    GUIStyle                        _listItemStyle;
    GUIStyle                        _selectedListItemStyle;

    // Current state
    object                          _selectedEntry      = null;
    Vector2                         _listScrollPos      = Vector2.zero;
    Vector2                         _detailsScrollPos   = Vector2.zero;
    bool                            _showFilters        = false;

    [NonSerialized] OrderedDictionary<Type, bool> _filters;

    // Selected entry info (fetched on selecting new entry)
    string                         _selectedName;
    string                         _operationStr;
    string                         _differenceStr;
    string                         _beforeStateStr;
    string                         _afterStateStr;

    [MenuItem("Metaplay/Timeline Debugger")]
    public static void ShowWindow()
    {
        TimelineDebugger window = (TimelineDebugger)EditorWindow.GetWindow(typeof(TimelineDebugger));
        window.titleContent = new GUIContent("Timeline");
        window.minSize = new Vector2(300.0f, 200.0f);
        window.Show();
    }

    void OnEnable()
    {
        _listItemStyle = CreateListItemStyle(false);
        _selectedListItemStyle = CreateListItemStyle(true);
    }

    void OnDisable()
    {
        // Make sure history tracking is disabled
        TimelineHistory history = MetaplaySDK.TimelineHistory as TimelineHistory;
        if (history != null)
            history.SetEnabled(false);
    }

    void Update()
    {
        Repaint();
    }

    public void OnGUI()
    {
        // Fetch history (if exists)
        TimelineHistory history = MetaplaySDK.TimelineHistory as TimelineHistory;
        if (history == null)
        {
            GUILayout.Label("No timeline found", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("No timeline history found in MetaplaySDK.TimelineHistory.", MessageType.Info);
            return;
        }

        // Ensure that history is enabled (when GUI visible)
        if (!history.IsEnabled)
            history.SetEnabled(true);

        if (_filters == null)
            _filters = new OrderedDictionary<Type, bool>();

        List<TimelineEntry> entries = history.Entries;

        // Layout
        int windowWidth = (int)position.width;
        int listWidth = 160;
        int detailsWidth = windowWidth - listWidth - 10;

        EditorGUILayout.BeginHorizontal();
        {
            // List of ticks & actions
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(listWidth));
            {
                foreach (TimelineEntry entry in entries)
                {
                    _filters.AddIfAbsent(entry.ModelType, true);
                    if (entry.Action != null)
                        _filters.AddIfAbsent(entry.Action.GetType(), true);
                    else
                        _filters.AddIfAbsent(typeof(Tick), true);
                }

                _showFilters = EditorGUILayout.Foldout(_showFilters, "Filtering");
                if (_showFilters)
                {
                    foreach (ref var kv in _filters)
                        kv.Value = GUILayout.Toggle(kv.Value, kv.Key.Name);
                }

                GUILayout.Label("Ticks & Actions", EditorStyles.boldLabel);

                _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
                {
                    // Render entries in reverse order (latest is at top)
                    for (int ndx = entries.Count - 1; ndx >= 0; ndx--)
                    {
                        TimelineEntry entry = entries[ndx];
                        bool isSelected = (entry == _selectedEntry);

                        if (!_filters[entry.ModelType])
                            continue;

                        if (entry.Action != null && !_filters[entry.Action.GetType()])
                            continue;

                        if (entry.Action == null && !_filters[typeof(Tick)])
                            continue;

                        if (GUILayout.Button(entry.Name, isSelected ? _selectedListItemStyle : _listItemStyle))
                        {
                            _selectedEntry = entry;
                            _selectedName = entry.Name;
                            history.ExportEntry(ndx, out _operationStr, out _differenceStr, out _beforeStateStr, out _afterStateStr);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();

            // Details of selected tick / action
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(detailsWidth));
            {
                if (_selectedEntry == null)
                {
                    GUILayout.Label("No entry selected", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox($"No timeline entry selected, choose one from the panel on the left to see its details.", MessageType.Info);
                }
                else
                {
                    GUILayout.Label(_selectedName, EditorStyles.boldLabel);

                    _detailsScrollPos = EditorGUILayout.BeginScrollView(_detailsScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
                    {
                        float lineHeight = GUI.skin.textArea.lineHeight + 0.2f;

                        if (_operationStr != null)
                        {
                            GUILayout.Label("Operation:", EditorStyles.boldLabel);
                            EditorGUILayout.SelectableLabel(_operationStr, GUI.skin.textArea, GUILayout.Height(4.0f + LineCount(_operationStr) * lineHeight));
                        }

                        GUILayout.Label("State difference:", EditorStyles.boldLabel);
                        EditorGUILayout.SelectableLabel(_differenceStr, GUI.skin.textArea, GUILayout.Height(4.0f + LineCount(_differenceStr) * lineHeight));

                        GUILayout.Label("State before:", EditorStyles.boldLabel);
                        EditorGUILayout.SelectableLabel(_beforeStateStr, GUI.skin.textArea, GUILayout.Height(4.0f + LineCount(_beforeStateStr) * lineHeight));

                        GUILayout.Label("State after:", EditorStyles.boldLabel);
                        EditorGUILayout.SelectableLabel(_afterStateStr, GUI.skin.textArea, GUILayout.Height(4.0f + LineCount(_afterStateStr) * lineHeight));
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();
    }

    static int LineCount(string str)
    {
        return Regex.Matches(str, "\n").Count + 1;
    }

    GUIStyle CreateListItemStyle(bool isSelected)
    {
        // create listbox style and temp textures
        GUIStyle style = new GUIStyle();
        style.name = "listbox";
        Color textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f, 1.0f) : Color.black;
        if (isSelected)
            textColor.b += 1.0f;
        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.active.textColor = textColor;
        style.focused.textColor = textColor;
        style.onNormal.textColor = textColor;
        style.onHover.textColor = textColor;
        style.onActive.textColor = textColor;
        style.onFocused.textColor = textColor;
        style.margin = new RectOffset(0, 0, 2, 2);
        style.padding = new RectOffset(4, 4, 2, 2);

        Texture2D hoverTex = new Texture2D(1, 1);
        hoverTex.hideFlags = HideFlags.HideAndDontSave;
        // tempObjects.Add(hoverTex); // store texture in a serialized list so it can be destroyed later in the event of a recompile to prevent leaked textures
        hoverTex.SetPixel(0, 0, EditorGUIUtility.isProSkin ? new Color(0.35f, 0.35f, 0.35f, 1.0f) : new Color(0.92f, 0.92f, 0.92f, 1.0f));
        hoverTex.Apply();
        style.hover.background = hoverTex;

        Texture2D highlightTex = new Texture2D(1, 1);
        highlightTex.hideFlags = HideFlags.HideAndDontSave;
        // tempObjects.Add(highlightTex);
        highlightTex.SetPixel(0, 0, EditorGUIUtility.isProSkin ? new Color(0x0a / 255f, 0x19 / 255f, 0x36 / 255f, 1.0f) : new Color(0.92f, 0.92f, 0.92f, 1.0f));
        highlightTex.Apply();
        // style.onNormal.background = highlightTex;
        // style.onFocused.background = highlightTex;
        if (isSelected)
            style.normal.background = highlightTex;
        return style;
    }
}
