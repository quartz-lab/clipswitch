using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchReferencesWindow : EditorWindow
    {
        [Serializable]
        private sealed class Result
        {
            public string TargetGuid;
            public string Label;
            public string Detail;
            public UnityEngine.Object Context;
            public bool Hierarchy;
        }

        [SerializeField] private List<string> targetGuids = new List<string>();
        [SerializeField] private int tab;
        [SerializeField] private Vector2 scroll;
        private readonly List<Result> results = new List<Result>();
        private bool scanning;

        public static void Open(IList<AudioClip> clips)
        {
            if (clips == null || clips.Count == 0) return;
            ClipSwitchReferencesWindow window = GetWindow<ClipSwitchReferencesWindow>();
            window.targetGuids.Clear();
            for (int i = 0; i < clips.Count; i++)
            {
                string path = clips[i] == null ? string.Empty : AssetDatabase.GetAssetPath(clips[i]);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid) && !window.targetGuids.Contains(guid)) window.targetGuids.Add(guid);
            }
            window.titleContent = new GUIContent(ClipSwitchLocalization.T("Audio References", "Ссылки на аудио"), EditorGUIUtility.IconContent("Search Icon").image);
            window.minSize = new Vector2(620f, 380f);
            window.Show();
            window.Focus();
            EditorApplication.delayCall += window.Scan;
        }

        private void OnGUI()
        {
            int hierarchyCount = 0;
            int projectCount = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Hierarchy) hierarchyCount++; else projectCount++;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUIContent[] tabs =
                {
                    new GUIContent(string.Format(L("Hierarchy ({0})", "Иерархия ({0})"), hierarchyCount), L("References on components in currently loaded scenes.", "Ссылки в компонентах загруженных сцен.")),
                    new GUIContent(string.Format(L("Project Assets ({0})", "Ассеты проекта ({0})"), projectCount), L("Scenes, prefabs, ScriptableObjects and other assets depending on the clip.", "Сцены, префабы, ScriptableObject и другие ассеты, зависящие от клипа."))
                };
                tab = GUILayout.Toolbar(tab, tabs, EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                GUI.enabled = !scanning;
                if (GUILayout.Button(new GUIContent(L("Rescan", "Обновить"), L("Search the loaded Hierarchy and all project assets again.", "Повторно найти ссылки в Иерархии и ассетах проекта.")), EditorStyles.toolbarButton, GUILayout.Width(82f))) Scan();
                GUI.enabled = true;
            }

            if (scanning)
            {
                EditorGUILayout.HelpBox(L("Searching for serialized AudioClip references…", "Поиск сериализованных ссылок на AudioClip…"), MessageType.Info);
                return;
            }

            List<Result> visible = new List<Result>();
            bool hierarchy = tab == 0;
            for (int i = 0; i < results.Count; i++) if (results[i].Hierarchy == hierarchy) visible.Add(results[i]);
            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox(hierarchy
                    ? L("No references were found in currently loaded scenes.", "В загруженных сценах ссылки не найдены.")
                    : L("No referencing project assets were found.", "Ассеты проекта со ссылками не найдены."), MessageType.Info);
                return;
            }

            const float rowHeight = 50f;
            Rect viewport = GUILayoutUtility.GetRect(1f, 100000f, 1f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float width = Mathf.Max(1f, viewport.width - 16f);
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0f, 0f, width, visible.Count * rowHeight), false, true);
            int first = Mathf.Clamp(Mathf.FloorToInt(scroll.y / rowHeight), 0, visible.Count - 1);
            int last = Mathf.Clamp(Mathf.CeilToInt((scroll.y + viewport.height) / rowHeight) + 1, first, visible.Count);
            for (int i = first; i < last; i++) DrawResult(new Rect(0f, i * rowHeight, width, rowHeight - 2f), visible[i], i);
            GUI.EndScrollView();
        }

        private void DrawResult(Rect rect, Result result, int index)
        {
            Color background = (index & 1) == 0
                ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.17f, 0.19f) : new Color(0.93f, 0.94f, 0.96f))
                : (EditorGUIUtility.isProSkin ? new Color(0.19f, 0.20f, 0.22f) : Color.white);
            EditorGUI.DrawRect(rect, background);
            Texture icon = result.Context == null ? EditorGUIUtility.IconContent("console.infoicon").image : AssetPreview.GetMiniThumbnail(result.Context);
            if (icon == null) icon = EditorGUIUtility.IconContent("console.infoicon").image;
            GUI.DrawTexture(new Rect(rect.x + 7f, rect.y + 8f, 32f, 32f), icon, ScaleMode.ScaleToFit, true);
            GUIStyle style = new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            string text = "<b>" + result.Label + "</b>\n<size=9>" + result.Detail + "</size>";
            if (GUI.Button(new Rect(rect.x + 44f, rect.y + 2f, rect.width - 50f, rect.height - 4f), new GUIContent(text, result.Detail), style)) SelectResult(result);
        }

        private static void SelectResult(Result result)
        {
            if (result.Context == null) return;
            Component component = result.Context as Component;
            if (component != null)
            {
                Selection.activeGameObject = component.gameObject;
                EditorGUIUtility.PingObject(component.gameObject);
            }
            else
            {
                Selection.activeObject = result.Context;
                EditorGUIUtility.PingObject(result.Context);
            }
        }

        private void Scan()
        {
            if (this == null || scanning) return;
            scanning = true;
            results.Clear();
            Repaint();
            try
            {
                Dictionary<string, AudioClip> targets = ResolveTargets();
                ScanHierarchy(targets);
                ScanProject(targets);
                results.Sort(delegate(Result a, Result b)
                {
                    int group = b.Hierarchy.CompareTo(a.Hierarchy);
                    return group != 0 ? group : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("ClipSwitch", L("Reference search failed: ", "Ошибка поиска ссылок: ") + ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                scanning = false;
                Repaint();
            }
        }

        private Dictionary<string, AudioClip> ResolveTargets()
        {
            Dictionary<string, AudioClip> targets = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
            for (int i = 0; i < targetGuids.Count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(targetGuids[i]);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) targets[targetGuids[i]] = clip;
            }
            return targets;
        }

        private void ScanHierarchy(Dictionary<string, AudioClip> targets)
        {
            Component[] components = Resources.FindObjectsOfTypeAll<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || EditorUtility.IsPersistent(component) || !component.gameObject.scene.IsValid()) continue;
                try
                {
                    SerializedObject serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        AudioClip referenced = property.objectReferenceValue as AudioClip;
                        if (referenced == null) continue;
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(referenced));
                        if (!targets.ContainsKey(guid)) continue;
                        results.Add(new Result
                        {
                            TargetGuid = guid,
                            Hierarchy = true,
                            Context = component,
                            Label = component.gameObject.scene.name + " / " + HierarchyPath(component.transform),
                            Detail = component.GetType().Name + "." + property.propertyPath + "  →  " + referenced.name
                        });
                    }
                }
                catch { /* Broken or editor-only components must not abort the search. */ }
            }
        }

        private void ScanProject(Dictionary<string, AudioClip> targets)
        {
            HashSet<string> targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AudioClip> pair in targets) targetPaths.Add(AssetDatabase.GetAssetPath(pair.Value));
            string[] paths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsValidFolder(path) || targetPaths.Contains(path)) continue;
                if ((i & 31) == 0 && EditorUtility.DisplayCancelableProgressBar("ClipSwitch", L("Searching project references…", "Поиск ссылок в проекте…") + "\n" + path, paths.Length == 0 ? 1f : (float)i / paths.Length)) break;
                string[] dependencies;
                try { dependencies = AssetDatabase.GetDependencies(path, false); }
                catch { continue; }
                for (int d = 0; d < dependencies.Length; d++)
                {
                    if (!targetPaths.Contains(dependencies[d])) continue;
                    AudioClip target = AssetDatabase.LoadAssetAtPath<AudioClip>(dependencies[d]);
                    if (target == null) continue;
                    string guid = AssetDatabase.AssetPathToGUID(dependencies[d]);
                    results.Add(new Result
                    {
                        TargetGuid = guid,
                        Hierarchy = false,
                        Context = AssetDatabase.LoadMainAssetAtPath(path),
                        Label = path,
                        Detail = L("References ", "Ссылается на ") + target.name
                    });
                }
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string L(string english, string russian) { return ClipSwitchLocalization.T(english, russian); }
    }
}
