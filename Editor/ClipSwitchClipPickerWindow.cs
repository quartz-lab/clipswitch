using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchClipPickerWindow : EditorWindow
    {
        private const float CompactRowHeight = 28f;
        private const float WaveformRowHeight = 82f;

        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly List<AudioClip> filtered = new List<AudioClip>();
        private readonly List<string> selectedGuids = new List<string>();
        private readonly HashSet<string> excludedGuids = new HashSet<string>();
        private Action<List<AudioClip>> onConfirmed;
        private string search = string.Empty;
        private Vector2 scroll;
        private int requiredCount;
        private int lastIndex = -1;

        public static void Open(int count, IEnumerable<string> excluded, string title, Action<List<AudioClip>> callback)
        {
            ClipSwitchClipPickerWindow window = CreateInstance<ClipSwitchClipPickerWindow>();
            window.requiredCount = Mathf.Max(1, count);
            window.onConfirmed = callback;
            if (excluded != null)
                foreach (string guid in excluded) if (!string.IsNullOrEmpty(guid)) window.excludedGuids.Add(guid);
            window.titleContent = new GUIContent(title, EditorGUIUtility.IconContent("AudioSource Icon").image);
            window.minSize = new Vector2(520f, 420f);
            window.Refresh();
            window.ShowUtility();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string next = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
                if (next != search) { search = next; ApplyFilter(); }
                GUIContent[] views =
                {
                    new GUIContent(L("Waveforms", "Дорожки"), L("Show waveforms and allow audio preview.", "Показывать дорожки и разрешить предпрослушивание.")),
                    new GUIContent(L("Names", "Названия"), L("Show a compact list of clip names.", "Показывать компактный список названий."))
                };
                int view = GUILayout.Toolbar((int)ClipSwitchState.instance.PickerViewMode, views, EditorStyles.toolbarButton, GUILayout.Width(170f));
                if (view != (int)ClipSwitchState.instance.PickerViewMode)
                {
                    ClipSwitchState.instance.PickerViewMode = (ClipSwitchListViewMode)view;
                    ClipSwitchState.instance.SaveNow();
                    scroll = Vector2.zero;
                }
                GUI.enabled = ClipSwitchAudioPreview.CurrentClip != null;
                if (GUILayout.Button(Icon("PauseButton", L("Pause or resume preview.", "Приостановить или продолжить звук.")), EditorStyles.toolbarButton, GUILayout.Width(30f))) ClipSwitchAudioPreview.PauseOrResume();
                GUIStyle stopStyle = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                if (GUILayout.Button(new GUIContent("■", L("Stop preview.", "Остановить звук.")), stopStyle, GUILayout.Width(30f))) ClipSwitchAudioPreview.Stop();
                GUI.enabled = true;
                GUILayout.Label(string.Format(L("Selected {0} / {1}", "Выбрано {0} / {1}"), selectedGuids.Count, requiredCount), EditorStyles.miniBoldLabel, GUILayout.Width(120f));
            }

            Rect viewport = GUILayoutUtility.GetRect(1f, 100000f, 1f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (filtered.Count == 0)
                GUI.Label(viewport, L("No AudioClips match the filter.", "Нет AudioClip, соответствующих фильтру."), EditorStyles.centeredGreyMiniLabel);
            else
            {
                float rowHeight = RowHeight;
                float width = Mathf.Max(1f, viewport.width - 16f);
                scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0f, 0f, width, filtered.Count * rowHeight), false, true);
                int first = Mathf.Clamp(Mathf.FloorToInt(scroll.y / rowHeight), 0, filtered.Count - 1);
                int last = Mathf.Clamp(Mathf.CeilToInt((scroll.y + viewport.height) / rowHeight) + 1, first, filtered.Count);
                for (int i = first; i < last; i++) DrawRow(new Rect(0f, i * rowHeight, width, rowHeight - 1f), filtered[i], i);
                GUI.EndScrollView();
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(requiredCount == 1
                    ? L("Choose one AudioClip and confirm.", "Выберите один AudioClip и подтвердите выбор.")
                    : string.Format(L("Choose exactly {0} AudioClips. Order follows the list.", "Выберите ровно {0} AudioClip. Порядок соответствует списку."), requiredCount));
                if (GUILayout.Button(L("Cancel", "Отмена"), GUILayout.Width(90f), GUILayout.Height(26f))) Close();
                GUI.enabled = selectedGuids.Count == requiredCount;
                if (GUILayout.Button(L("Select", "Выбрать"), GUILayout.Width(90f), GUILayout.Height(26f))) Confirm();
                GUI.enabled = true;
            }
        }

        private void DrawRow(Rect rect, AudioClip clip, int index)
        {
            string guid = GuidOf(clip);
            bool selected = selectedGuids.Contains(guid);
            Color background = selected
                ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.38f, 0.56f) : new Color(0.58f, 0.80f, 1f))
                : ((index & 1) == 0
                    ? (EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.20f) : new Color(0.93f, 0.94f, 0.96f))
                    : (EditorGUIUtility.isProSkin ? new Color(0.20f, 0.21f, 0.23f) : Color.white));
            EditorGUI.DrawRect(rect, background);
            string path = AssetDatabase.GetAssetPath(clip);
            if (ClipSwitchState.instance.PickerViewMode == ClipSwitchListViewMode.Compact)
            {
                if (GUI.Button(new Rect(rect.x + 5f, rect.y + 2f, rect.width - 10f, rect.height - 4f), new GUIContent(clip.name + "   •   " + path, path), EditorStyles.label))
                    Toggle(guid, index, Event.current.shift, Event.current.control || Event.current.command);
                return;
            }

            float nameWidth = Mathf.Clamp(rect.width * 0.34f, 180f, 270f);
            Rect nameRect = new Rect(rect.x + 5f, rect.y + 4f, nameWidth - 8f, rect.height - 8f);
            Rect waveformRect = new Rect(nameRect.xMax + 5f, rect.y + 4f, rect.xMax - nameRect.xMax - 10f, rect.height - 8f);
            GUIStyle nameStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fontStyle = FontStyle.Bold };
            string extension = Path.GetExtension(path).ToUpperInvariant();
            if (GUI.Button(nameRect, new GUIContent(clip.name + "\n" + extension, path), nameStyle))
                Toggle(guid, index, Event.current.shift, Event.current.control || Event.current.command);
            Texture2D waveform = ClipSwitchWaveformCache.Get(clip, Mathf.Max(2, (int)waveformRect.width), Mathf.Max(2, (int)waveformRect.height));
            ClipSwitchWaveformGUI.Draw(waveformRect, clip, waveform, clip.name, true);
        }

        private float RowHeight
        {
            get { return ClipSwitchState.instance.PickerViewMode == ClipSwitchListViewMode.Compact ? CompactRowHeight : WaveformRowHeight; }
        }

        private void OnInspectorUpdate()
        {
            if (ClipSwitchAudioPreview.NeedsRepaint) Repaint();
        }

        private void OnDisable()
        {
            AudioClip previewed = ClipSwitchAudioPreview.CurrentClip;
            if (previewed != null && clips.Contains(previewed)) ClipSwitchAudioPreview.Stop();
        }

        private void Toggle(string guid, int index, bool shift, bool additive)
        {
            if (shift && lastIndex >= 0)
            {
                if (!additive) selectedGuids.Clear();
                int from = Mathf.Min(lastIndex, index);
                int to = Mathf.Max(lastIndex, index);
                for (int i = from; i <= to && selectedGuids.Count < requiredCount; i++)
                {
                    string item = GuidOf(filtered[i]);
                    if (!selectedGuids.Contains(item)) selectedGuids.Add(item);
                }
            }
            else if (additive || requiredCount > 1)
            {
                if (!selectedGuids.Remove(guid) && selectedGuids.Count < requiredCount) selectedGuids.Add(guid);
                lastIndex = index;
            }
            else
            {
                selectedGuids.Clear();
                selectedGuids.Add(guid);
                lastIndex = index;
            }
            Repaint();
        }

        private void Confirm()
        {
            if (selectedGuids.Count != requiredCount) return;
            List<AudioClip> result = new List<AudioClip>();
            // Resolve from the complete stable list. A selected clip may be hidden by
            // a search entered after selection and must still be returned.
            for (int i = 0; i < clips.Count; i++)
            {
                string guid = GuidOf(clips[i]);
                if (selectedGuids.Contains(guid)) result.Add(clips[i]);
            }
            Action<List<AudioClip>> callback = onConfirmed;
            onConfirmed = null;
            Close();
            if (callback != null) callback(result);
        }

        private void Refresh()
        {
            clips.Clear();
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
            string backupRoot = ClipSwitchPathUtility.NormalizeAssetPath(ClipSwitchState.instance.BackupRoot).TrimEnd('/');
            for (int i = 0; i < guids.Length; i++)
            {
                if (excludedGuids.Contains(guids[i])) continue;
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(backupRoot) && (path == backupRoot || path.StartsWith(backupRoot + "/", StringComparison.OrdinalIgnoreCase))) continue;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips.Add(clip);
            }
            clips.Sort(delegate(AudioClip a, AudioClip b) { return string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.OrdinalIgnoreCase); });
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            filtered.Clear();
            string needle = string.IsNullOrWhiteSpace(search) ? string.Empty : search.Trim();
            for (int i = 0; i < clips.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(clips[i]);
                if (string.IsNullOrEmpty(needle) || clips[i].name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(clips[i]);
            }
            Repaint();
        }

        private static string GuidOf(AudioClip clip) { return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)); }
        private static GUIContent Icon(string name, string tooltip)
        {
            GUIContent icon = EditorGUIUtility.IconContent(name);
            icon.tooltip = tooltip;
            return icon;
        }
        private static string L(string english, string russian) { return ClipSwitchLocalization.T(english, russian); }
    }
}
