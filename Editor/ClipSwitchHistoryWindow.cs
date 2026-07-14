using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchHistoryWindow : EditorWindow
    {
        private const float RowHeight = 118f;

        [SerializeField] private string targetGuid;
        [SerializeField] private Vector2 scroll;

        private readonly List<ClipSwitchHistoryEntry> entries = new List<ClipSwitchHistoryEntry>();

        public static void Open(AudioClip target)
        {
            if (target == null) return;
            ClipSwitchHistoryWindow window = GetWindow<ClipSwitchHistoryWindow>(true);
            window.targetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target));
            window.titleContent = new GUIContent(ClipSwitchLocalization.T("ClipSwitch — History", "ClipSwitch — История"), EditorGUIUtility.IconContent("UndoHistory").image);
            window.minSize = new Vector2(620f, 420f);
            window.RefreshEntries();
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            RefreshEntries();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            ClipSwitchAudioPreview.Stop();
        }

        private void OnEditorUpdate()
        {
            if (ClipSwitchAudioPreview.NeedsRepaint) Repaint();
        }

        private void OnGUI()
        {
            AudioClip target = LoadTarget();
            if (target == null)
            {
                EditorGUILayout.HelpBox(L("The target clip no longer exists.", "Целевой клип больше не существует."), MessageType.Error);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(new GUIContent(target.name, AssetDatabase.GetAssetPath(target)), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(L("Refresh", "Обновить"), L("Reload history snapshots.", "Повторно загрузить снимки истории.")), EditorStyles.toolbarButton, GUILayout.Width(82f)))
                    RefreshEntries();
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(L("This clip has no history snapshots yet.", "У этого клипа пока нет снимков истории."), MessageType.Info);
                return;
            }

            Rect viewport = GUILayoutUtility.GetRect(1f, 100000f, 1f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float viewWidth = Mathf.Max(1f, viewport.width - 16f);
            Rect content = new Rect(0f, 0f, viewWidth, entries.Count * RowHeight);
            scroll = GUI.BeginScrollView(viewport, scroll, content, false, true);
            int first = Mathf.Clamp(Mathf.FloorToInt(scroll.y / RowHeight), 0, entries.Count - 1);
            int last = Mathf.Clamp(Mathf.CeilToInt((scroll.y + viewport.height) / RowHeight) + 1, first, entries.Count);
            for (int i = first; i < last; i++) DrawEntry(new Rect(0f, i * RowHeight, viewWidth, RowHeight - 4f), target, entries[i], i);
            GUI.EndScrollView();
        }

        private void DrawEntry(Rect rect, AudioClip target, ClipSwitchHistoryEntry entry, int index)
        {
            Color background = (index & 1) == 0
                ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.17f, 0.19f) : new Color(0.91f, 0.92f, 0.94f))
                : (EditorGUIUtility.isProSkin ? new Color(0.19f, 0.20f, 0.22f) : new Color(0.96f, 0.97f, 0.98f));
            EditorGUI.DrawRect(rect, background);

            string date = entry.UtcTicks > 0 ? entry.UtcTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
            string operation = ClipSwitchLocalization.Operation(entry.Operation);
            Rect info = new Rect(rect.x + 8f, rect.y + 5f, rect.width - 150f, 18f);
            GUI.Label(info, new GUIContent(date + "   •   " + operation, entry.SourceDescription), EditorStyles.boldLabel);

            AudioClip backup = string.IsNullOrEmpty(entry.BackupPath) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(entry.BackupPath);
            Rect wave = new Rect(rect.x + 8f, rect.y + 27f, rect.width - 126f, 78f);
            Texture2D texture = backup == null ? null : ClipSwitchWaveformCache.Get(backup, Mathf.Max(2, (int)wave.width), Mathf.Max(2, (int)wave.height));
            string label = backup == null
                ? L("Snapshot file is missing", "Файл снимка отсутствует")
                : L("Version: ", "Версия: ") + backup.name;
            ClipSwitchWaveformGUI.Draw(wave, backup, texture, label, true);

            Rect restore = new Rect(rect.xMax - 110f, rect.y + 43f, 100f, 30f);
            GUI.enabled = backup != null && File.Exists(ClipSwitchPathUtility.AssetPathToAbsolute(entry.BackupPath));
            if (GUI.Button(restore, new GUIContent(L("Restore", "Восстановить"), L("Restore this exact audio version. The current state remains in history.", "Восстановить именно эту версию. Текущее состояние останется в истории."))))
                Restore(target, entry);
            GUI.enabled = true;
        }

        private void Restore(AudioClip target, ClipSwitchHistoryEntry entry)
        {
            if (ClipSwitchState.instance.ConfirmDestructiveActions && !EditorUtility.DisplayDialog(
                    L("Restore this version?", "Восстановить эту версию?"),
                    L("The current version will be saved to history before restoration.", "Перед восстановлением текущая версия будет сохранена в истории."),
                    L("Restore", "Восстановить"), L("Cancel", "Отмена")))
                return;

            string error;
            if (!ClipSwitchOperations.RestoreVersion(target, entry, out error))
            {
                EditorUtility.DisplayDialog("ClipSwitch", error, "OK");
                return;
            }
            ClipSwitchWindow.RefreshOpenWindows(targetGuid);
            RefreshEntries();
        }

        private AudioClip LoadTarget()
        {
            return string.IsNullOrEmpty(targetGuid) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(targetGuid));
        }

        private void RefreshEntries()
        {
            entries.Clear();
            if (!string.IsNullOrEmpty(targetGuid)) entries.AddRange(ClipSwitchState.instance.GetHistory(targetGuid));
            Repaint();
        }

        private static string L(string english, string russian) { return ClipSwitchLocalization.T(english, russian); }
    }
}
