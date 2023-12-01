// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using UnityEditor;
using UnityEngine;

namespace Metaplay.Unity
{
    public class EnvironmentConfigEditorConfigWrapper : ScriptableObject
    {
        [SerializeReference] public EnvironmentConfig[] Environments;
    }

    [CustomEditor(typeof(EnvironmentConfigEditorConfigWrapper))]
    public class EnvironmentConfigEditorConfigWrapperEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            // Hide the "Script:" field
            DrawPropertiesExcluding(serializedObject, new string[] {"m_Script"});
            serializedObject.ApplyModifiedProperties();
        }
    }
}
