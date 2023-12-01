// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using System;
using UnityEditor;
using UnityEngine;

namespace Metaplay.Unity
{
    public class EnvironmentConfigEditor : EditorWindow
    {
        DefaultEnvironmentConfigProvider                      _provider;
        [SerializeField] EnvironmentConfig[]                  _environmentConfigs;
        [SerializeField] string[]                             _environmentIds;
        [SerializeField] int                                  _selectedEnvironmentIndex;
        [SerializeField] EnvironmentConfigEditorConfigWrapper _configsWrapper;

        [SerializeField] string _builtEnvironmentsJson;
        [SerializeField] bool   _showBuiltConfigs = false;

        int     _controlLabelWidth = 150;
        Vector2 _scrollPosition    = Vector2.zero;
        Editor  _configEditor;

        float  _saveEditsDelay = 0.5f; // Amount of time in seconds to wait before saving edits to provider
        double _nextSaveTime;
        bool   _editorDirty;

        bool _initialized = false;

        public EnvironmentConfigEditor() { }

        void OnEnable()
        {
            // Ensure Metaplay Core is initialized
            MetaplayCore.Initialize();

            LoadAll();
        }

        void OnDisable()
        {
            SaveConfigsToProvider();

            DestroyImmediate(_configsWrapper);
        }

        [MenuItem("Metaplay/Environment Configs")]
        public static void ShowWindow()
        {
            var wnd = EditorWindow.GetWindow<EnvironmentConfigEditor>();
            wnd.titleContent = new GUIContent("Metaplay Environment Config Editor");
            wnd.minSize      = new Vector2(200, 200);
            wnd.Show();
        }

        void LoadAll()
        {
            try
            {
                _provider = DefaultEnvironmentConfigProvider.Instance;
            }
            catch (InvalidOperationException)
            {
                _provider = null;
                return;
            }

            if (!_provider.Initialized)
            {
                return;
            }

            _environmentIds     = _provider.GetAllEnvironmentIds();
            _environmentConfigs = _provider.GetAllEnvironmentConfigs();

            if (_configsWrapper != null)
            {
                _configsWrapper.Environments = _environmentConfigs;
            }

            _selectedEnvironmentIndex = Array.IndexOf(_environmentIds, _provider.GetActiveEnvironmentId());

            _initialized = true;
        }

        void OnInspectorUpdate()
        {
            if (_provider != null && !_initialized)
            {
                LoadAll();
            }
        }

        void OnGUI()
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Metaplay Environment Config Editor", EditorStyles.largeLabel);

            if (_provider == null)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("Error! DefaultEnvironmentConfigProvider or a provider deriving from it is not the active environment config provider integration.", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!_provider.Initialized)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("Waiting for DefaultEnvironmentConfigProvider to Initialize...", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (GUILayout.Button("Refresh", GUILayout.MaxWidth(100)))
            {
                // Reload configs from file into provider
                _provider.ReloadConfigs();
                // Reload all values from provider
                LoadAll();
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Selected environment dropdown
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Active Environment: ", GUILayout.MinWidth(_controlLabelWidth));
            _selectedEnvironmentIndex = EditorGUILayout.Popup(_selectedEnvironmentIndex, _environmentIds);
            if (EditorGUI.EndChangeCheck())
            {
                try
                {
                    // Save to provider
                    _provider.SetActiveEnvironmentId(_environmentIds[_selectedEnvironmentIndex]);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Metaplay environment config editor failed to save changes: " + ex);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(1);

            // Preview built configs button
            if (_showBuiltConfigs)
            {
                if (GUILayout.Button("Hide preview"))
                {
                    _builtEnvironmentsJson = "";
                    _showBuiltConfigs      = false;
                }
            }
            else
            {
                if (GUILayout.Button("Preview Built Environment Configs"))
                {
                    _builtEnvironmentsJson = _provider.GetEditorDefinedBuildEnvironmentsPreview();
                    _showBuiltConfigs      = true;
                }
            }

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // Simple horizontal line

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Built configs JSON preview
            if (_showBuiltConfigs)
            {
                EditorGUILayout.LabelField(_builtEnvironmentsJson, EditorStyles.textArea);
            }

            // Editing the config specs
            if (_configsWrapper == null || _configEditor == null)
            {
                _configsWrapper              = ScriptableObject.CreateInstance<EnvironmentConfigEditorConfigWrapper>();
                _configsWrapper.hideFlags    = HideFlags.DontSaveInEditor;
                _configsWrapper.Environments = _environmentConfigs;
                _configEditor                = Editor.CreateEditor(_configsWrapper);
            }

            EditorGUI.BeginChangeCheck();
            _configEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                // Reset save delay timer
                _editorDirty  = true;
                _nextSaveTime = EditorApplication.timeSinceStartup + (double)_saveEditsDelay;

                // Populate added configs
                for (int i = 0; i < _configsWrapper.Environments.Length; i++)
                {
                    Type configType = IntegrationRegistry.GetSingleIntegrationType<EnvironmentConfig>();
                    if (_configsWrapper.Environments[i] == null)
                    {
                        _configsWrapper.Environments[i] = Activator.CreateInstance(configType) as EnvironmentConfig;
                    }
                }

                //_configEditor.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        void Update()
        {
            // Save edits after delay
            if (_editorDirty && EditorApplication.timeSinceStartup > _nextSaveTime)
            {
                SaveConfigsToProvider();
                _editorDirty = false;

                LoadAll();
            }
        }

        void SaveConfigsToProvider()
        {
            if (_configsWrapper != null && _configsWrapper.Environments != null)
            {
                // Set edited configs to provider
                _environmentConfigs = _configsWrapper.Environments;
                _provider.SetAllEnvironmentConfigs(_environmentConfigs);
            }
        }
    }
}
