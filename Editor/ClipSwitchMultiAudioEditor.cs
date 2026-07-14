using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchMultiAudioEditor : IDisposable
    {
        private const float TrackHeight = 88f;

        private readonly List<ClipSwitchAudioEditorPanel> panels = new List<ClipSwitchAudioEditorPanel>();
        private readonly List<string> selectedGuids = new List<string>();
        private Vector2 tracksScroll;
        private Vector2 settingsScroll;
        private int lastSelectedIndex = -1;

        public Action<List<AudioClip>> ContextMenuRequested { get; set; }

        public int LoadedCount { get { return panels.Count; } }
        public int SelectedCount { get { return selectedGuids.Count; } }
        public bool IsBusy
        {
            get
            {
                for (int i = 0; i < panels.Count; i++) if (panels[i].IsBusy) return true;
                return false;
            }
        }

        public List<AudioClip> LoadedClips
        {
            get
            {
                List<AudioClip> result = new List<AudioClip>();
                for (int i = 0; i < panels.Count; i++) if (panels[i].Clip != null) result.Add(panels[i].Clip);
                return result;
            }
        }

        public List<AudioClip> SelectedClips
        {
            get
            {
                List<AudioClip> result = new List<AudioClip>();
                for (int i = 0; i < panels.Count; i++)
                    if (selectedGuids.Contains(panels[i].Guid) && panels[i].Clip != null) result.Add(panels[i].Clip);
                return result;
            }
        }

        public void LoadClips(IList<AudioClip> clips)
        {
            DisposePanels();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    AudioClip clip = clips[i];
                    if (clip == null) continue;
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip));
                    if (string.IsNullOrEmpty(guid) || !seen.Add(guid)) continue;
                    EditorUtility.DisplayProgressBar("ClipSwitch", string.Format(L("Loading {0} of {1}: {2}", "Загрузка {0} из {1}: {2}"), i + 1, clips.Count, clip.name), clips.Count == 0 ? 1f : (float)i / clips.Count);
                    ClipSwitchAudioEditorPanel panel = new ClipSwitchAudioEditorPanel();
                    panel.SetClip(clip);
                    panels.Add(panel);
                    selectedGuids.Add(guid);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            lastSelectedIndex = panels.Count > 0 ? panels.Count - 1 : -1;
        }

        public void Update()
        {
            for (int i = 0; i < panels.Count; i++) panels[i].Update();
        }

        public void Draw()
        {
            if (panels.Count == 0) return;
            DrawPlaybackToolbar();
            DrawTracks();
            DrawSelectedSettings();
        }

        public bool Matches(IList<string> guids)
        {
            if (guids == null || guids.Count != panels.Count) return false;
            HashSet<string> expected = new HashSet<string>(guids, StringComparer.Ordinal);
            for (int i = 0; i < panels.Count; i++) if (!expected.Contains(panels[i].Guid)) return false;
            return true;
        }

        public void RefreshGuid(string guid)
        {
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i].Guid != guid) continue;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null) panels[i].SetClip(clip);
            }
        }

        public void Dispose()
        {
            DisposePanels();
        }

        private void DrawPlaybackToolbar()
        {
            List<ClipSwitchAudioEditorPanel> selected = SelectedPanels();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                string header = selected.Count == 1
                    ? selected[0].DisplayName
                    : string.Format(L("Loaded: {0}   Selected: {1}", "Загружено: {0}   Выбрано: {1}"), panels.Count, selected.Count);
                EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
                GUI.enabled = selected.Count == 1 && selected[0].IsReady;
                if (GUILayout.Button(Icon("PlayButton", L("Play selected clip.", "Воспроизвести выбранный клип.")), GUILayout.Width(38f), GUILayout.Height(26f))) selected[0].Play();
                GUI.enabled = selected.Count == 1 && ClipSwitchAudioPreview.CurrentClip != null;
                if (GUILayout.Button(Icon("PauseButton", L("Pause or resume selected clip.", "Приостановить или продолжить выбранный клип.")), GUILayout.Width(38f), GUILayout.Height(26f))) ClipSwitchAudioPreview.PauseOrResume();
                GUIStyle stopStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
                if (GUILayout.Button(new GUIContent("■", L("Stop playback.", "Остановить воспроизведение.")), stopStyle, GUILayout.Width(38f), GUILayout.Height(26f))) ClipSwitchAudioPreview.Stop();
                GUI.enabled = true;
            }
        }

        private void DrawTracks()
        {
            float height = Mathf.Clamp(panels.Count * TrackHeight, TrackHeight + 4f, 280f);
            Rect viewport = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
            float width = Mathf.Max(1f, viewport.width - 16f);
            tracksScroll = GUI.BeginScrollView(viewport, tracksScroll, new Rect(0f, 0f, width, panels.Count * TrackHeight), false, true);
            int first = Mathf.Clamp(Mathf.FloorToInt(tracksScroll.y / TrackHeight), 0, panels.Count - 1);
            int last = Mathf.Clamp(Mathf.CeilToInt((tracksScroll.y + viewport.height) / TrackHeight) + 1, first, panels.Count);
            for (int i = first; i < last; i++) DrawTrack(new Rect(0f, i * TrackHeight, width, TrackHeight - 3f), panels[i], i);
            GUI.EndScrollView();
        }

        private void DrawTrack(Rect row, ClipSwitchAudioEditorPanel panel, int index)
        {
            bool selected = selectedGuids.Contains(panel.Guid);
            Event evt = Event.current;
            bool contextEvent = evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1);
            if (contextEvent && row.Contains(evt.mousePosition))
            {
                List<AudioClip> targets = selected && selectedGuids.Count > 1
                    ? SelectedClips
                    : new List<AudioClip> { panel.Clip };
                if (ContextMenuRequested != null) ContextMenuRequested(targets);
                evt.Use();
            }
            Color background = selected
                ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.36f, 0.52f) : new Color(0.60f, 0.81f, 1f))
                : ((index & 1) == 0
                    ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.17f, 0.19f) : new Color(0.92f, 0.93f, 0.95f))
                    : (EditorGUIUtility.isProSkin ? new Color(0.19f, 0.20f, 0.22f) : Color.white));
            EditorGUI.DrawRect(row, background);
            float nameWidth = Mathf.Clamp(row.width * 0.27f, 180f, 260f);
            Rect name = new Rect(row.x + 5f, row.y + 5f, nameWidth - 8f, row.height - 10f);
            Rect wave = new Rect(name.xMax + 5f, row.y + 5f, row.xMax - name.xMax - 10f, row.height - 10f);
            GUIStyle label = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fontStyle = FontStyle.Bold };
            if (GUI.Button(name, new GUIContent(panel.DisplayName, panel.Clip == null ? string.Empty : AssetDatabase.GetAssetPath(panel.Clip)), label))
                Select(index, Event.current.shift, Event.current.control || Event.current.command);
            panel.DrawCompactWaveform(wave, selected && selectedGuids.Count == 1);
        }

        private void DrawSelectedSettings()
        {
            List<ClipSwitchAudioEditorPanel> selected = SelectedPanels();
            if (selected.Count == 0)
            {
                EditorGUILayout.HelpBox(L("Select one or more tracks above to edit them.", "Выберите одну или несколько дорожек выше для редактирования."), MessageType.Info);
                return;
            }

            ClipSwitchAudioEditorPanel primary = selected[0];
            EditorGUILayout.LabelField(selected.Count == 1
                ? L("Settings for selected clip", "Настройки выбранного клипа")
                : string.Format(L("Settings applied to {0} selected clips", "Настройки применяются к выбранным клипам: {0}"), selected.Count), EditorStyles.boldLabel);
            int settingsBefore = primary.SettingsRevision;
            int importBefore = primary.ImportAppliedRevision;
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
            primary.DrawSettingsOnly(false);
            EditorGUILayout.Space(10f);
            DrawApplyActions(selected);
            EditorGUILayout.EndScrollView();

            if (primary.SettingsRevision != settingsBefore)
                for (int i = 1; i < selected.Count; i++) selected[i].CopyProcessingFrom(primary);
            if (primary.ImportAppliedRevision != importBefore)
            {
                for (int i = 1; i < selected.Count; i++)
                {
                    string error;
                    if (!selected[i].CopyImporterSettingsFrom(primary, out error)) EditorUtility.DisplayDialog("ClipSwitch", error, "OK");
                }
            }
        }

        private void DrawApplyActions(List<ClipSwitchAudioEditorPanel> selected)
        {
            bool ready = true;
            for (int i = 0; i < selected.Count; i++) if (!selected[i].IsReady) ready = false;
            EditorGUILayout.HelpBox(L("Overwrite creates a history snapshot for every selected clip.", "Перезапись создаёт снимок истории для каждого выбранного клипа."), MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = ready;
                string overwrite = selected.Count == 1
                    ? L("Overwrite + Backup", "Перезаписать + копия")
                    : string.Format(L("Overwrite {0} Clips + Backups", "Перезаписать клипы ({0}) + копии"), selected.Count);
                if (GUILayout.Button(overwrite, GUILayout.Height(34f))) ApplySelected(selected);
                GUI.enabled = ready && selected.Count == 1;
                if (GUILayout.Button(L("Save As New WAV", "Сохранить как новый WAV"), GUILayout.Height(34f))) selected[0].SaveAsNew();
                GUI.enabled = true;
            }
        }

        private void ApplySelected(List<ClipSwitchAudioEditorPanel> selected)
        {
            if (ClipSwitchState.instance.ConfirmDestructiveActions && !EditorUtility.DisplayDialog(
                    L("Overwrite processed audio?", "Перезаписать обработанное аудио?"),
                    string.Format(L("ClipSwitch will overwrite {0} selected clip(s) and create a history snapshot for each.", "ClipSwitch перезапишет выбранные клипы ({0}) и создаст снимок истории для каждого."), selected.Count),
                    L("Overwrite", "Перезаписать"), L("Cancel", "Отмена"))) return;
            try
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("ClipSwitch", selected[i].DisplayName, (float)i / selected.Count);
                    string error;
                    if (!selected[i].ApplyProcessed(out error)) { EditorUtility.DisplayDialog("ClipSwitch", error, "OK"); break; }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private void Select(int index, bool shift, bool additive)
        {
            string guid = panels[index].Guid;
            if (shift && lastSelectedIndex >= 0)
            {
                if (!additive) selectedGuids.Clear();
                int from = Mathf.Min(lastSelectedIndex, index);
                int to = Mathf.Max(lastSelectedIndex, index);
                for (int i = from; i <= to; i++) if (!selectedGuids.Contains(panels[i].Guid)) selectedGuids.Add(panels[i].Guid);
            }
            else if (additive)
            {
                if (!selectedGuids.Remove(guid)) selectedGuids.Add(guid);
                lastSelectedIndex = index;
            }
            else
            {
                selectedGuids.Clear();
                selectedGuids.Add(guid);
                lastSelectedIndex = index;
            }
            ClipSwitchAudioPreview.Stop();
        }

        private List<ClipSwitchAudioEditorPanel> SelectedPanels()
        {
            List<ClipSwitchAudioEditorPanel> result = new List<ClipSwitchAudioEditorPanel>();
            for (int i = 0; i < panels.Count; i++) if (selectedGuids.Contains(panels[i].Guid)) result.Add(panels[i]);
            return result;
        }

        private void DisposePanels()
        {
            for (int i = 0; i < panels.Count; i++) panels[i].Dispose();
            panels.Clear();
            selectedGuids.Clear();
            ClipSwitchAudioPreview.Stop();
        }

        private static GUIContent Icon(string name, string tooltip)
        {
            GUIContent icon = EditorGUIUtility.IconContent(name);
            icon.tooltip = tooltip;
            return icon;
        }

        private static string L(string english, string russian) { return ClipSwitchLocalization.T(english, russian); }
    }
}
