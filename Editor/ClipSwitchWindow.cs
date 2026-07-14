using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchWindow : EditorWindow
    {
        private enum MainTab { Library, Editor, Settings }

        private const float DetailedRowHeight = 92f;
        private const float CompactRowHeight = 30f;

        [SerializeField] private MainTab tab;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private string folderPath = "Assets";
        [SerializeField] private bool modifiedOnly;
        [SerializeField] private Vector2 libraryScroll;
        [SerializeField] private List<string> librarySelectedGuids = new List<string>();
        [SerializeField] private List<string> loadedEditorGuids = new List<string>();
        [SerializeField] private int lastLibrarySelectedIndex = -1;

        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly List<AudioClip> filtered = new List<AudioClip>();
        private ClipSwitchMultiAudioEditor audioEditor;
        private double nextRepaint;

        [MenuItem("Tools/QuartzLab/ClipSwitch")]
        private static void OpenWindow()
        {
            ClipSwitchWindow window = GetWindow<ClipSwitchWindow>();
            window.UpdateTitle();
            window.minSize = new Vector2(700f, 500f);
            window.Show();
        }

        internal static void OpenAndEdit(AudioClip clip)
        {
            if (clip == null) return;
            ClipSwitchWindow window = GetWindow<ClipSwitchWindow>();
            window.UpdateTitle();
            window.minSize = new Vector2(700f, 500f);
            window.EnsureEditor();
            window.librarySelectedGuids.Clear();
            window.librarySelectedGuids.Add(GuidOf(clip));
            window.lastLibrarySelectedIndex = -1;
            window.OpenLibrarySelectionInEditor();
            window.Show();
        }

        internal static void RefreshOpenWindows(string guid)
        {
            ClipSwitchWindow[] windows = Resources.FindObjectsOfTypeAll<ClipSwitchWindow>();
            for (int i = 0; i < windows.Length; i++) windows[i].OnAssetChanged(guid);
        }

        private void OnEnable()
        {
            wantsLessLayoutEvents = true;
            if (librarySelectedGuids == null) librarySelectedGuids = new List<string>();
            if (loadedEditorGuids == null) loadedEditorGuids = new List<string>();
            EnsureEditor();
            EditorApplication.update += OnEditorUpdate;
            UpdateTitle();
            Refresh();

            // Domain reload restores an already-confirmed editor session. It does not
            // turn a pending Library selection into a heavy editor load.
            if (tab == MainTab.Editor)
            {
                List<AudioClip> loaded = LoadByGuids(loadedEditorGuids);
                if (loaded.Count > 0) audioEditor.LoadClips(loaded);
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (audioEditor != null) audioEditor.Dispose();
            audioEditor = null;
        }

        private void OnEditorUpdate()
        {
            if (audioEditor != null) audioEditor.Update();
            if (EditorApplication.timeSinceStartup < nextRepaint) return;
            nextRepaint = EditorApplication.timeSinceStartup + 1.0 / 60.0;
            if (ClipSwitchAudioPreview.NeedsRepaint || (audioEditor != null && audioEditor.IsBusy)) Repaint();
        }

        private void OnGUI()
        {
            DrawMainTabs();
            switch (tab)
            {
                case MainTab.Library: DrawLibrary(); break;
                case MainTab.Editor: DrawEditorTab(); break;
                case MainTab.Settings: DrawSettings(); break;
            }
        }

        private void DrawMainTabs()
        {
            GUIContent[] tabs =
            {
                TabContent("Project", L("Library", "Библиотека"), L("Select project AudioClips. Loading is deferred until you open the editor.", "Выбрать AudioClip проекта. Загрузка откладывается до открытия редактора.")),
                TabContent("AudioSource Icon", L("Audio Editor", "Аудиоредактор"), L("Edit one or several explicitly loaded clips.", "Редактировать один или несколько явно загруженных клипов.")),
                TabContent("Settings", L("Settings", "Настройки"), L("Configure ClipSwitch.", "Настроить ClipSwitch."))
            };
            MainTab requested = (MainTab)GUILayout.Toolbar((int)tab, tabs, GUILayout.Height(28f));
            if (requested == tab) return;
            if (requested == MainTab.Editor && audioEditor.LoadedCount == 0 && loadedEditorGuids.Count > 0)
            {
                // Restore a previously opened session only when the tab itself is opened.
                // A pending Library selection is never transferred by tab navigation.
                List<AudioClip> restored = LoadByGuids(loadedEditorGuids);
                if (restored.Count > 0) audioEditor.LoadClips(restored);
            }
            tab = requested;
            GUI.FocusControl(null);
        }

        private void DrawLibrary()
        {
            DrawLibraryToolbar();
            DrawLibrarySelectionBar();
            DrawColumnHeader();
            DrawVirtualizedList();
        }

        private void DrawLibraryToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(Icon("Refresh", L("Rescan AudioClip assets.", "Повторно найти AudioClip в проекте.")), EditorStyles.toolbarButton, GUILayout.Width(34f))) Refresh();
                GUI.SetNextControlName("ClipSwitchSearch");
                string newSearch = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(100f));
                GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent(string.Empty, L("Search by clip name or asset path.", "Поиск по имени клипа или пути ассета.")));
                if (newSearch != search) { search = newSearch; ApplyFilter(); }
                if (!string.IsNullOrEmpty(search) && GUILayout.Button(new GUIContent("×", L("Clear search.", "Очистить поиск.")), EditorStyles.toolbarButton, GUILayout.Width(24f))) { search = string.Empty; ApplyFilter(); }

                GUILayout.Label(new GUIContent(L("Folder", "Папка"), L("Only list audio in this Assets folder.", "Показывать аудио только из этой папки Assets.")), GUILayout.Width(48f));
                DefaultAsset currentFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
                DefaultAsset selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(currentFolder, typeof(DefaultAsset), false, GUILayout.Width(145f));
                if (selectedFolder != currentFolder)
                {
                    string path = selectedFolder == null ? "Assets" : AssetDatabase.GetAssetPath(selectedFolder);
                    if (AssetDatabase.IsValidFolder(path)) { folderPath = path; Refresh(); }
                }

                bool only = GUILayout.Toggle(modifiedOnly, new GUIContent(L("Modified", "Изменённые"), L("Show only clips with ClipSwitch history.", "Показывать только клипы с историей ClipSwitch.")), EditorStyles.toolbarButton, GUILayout.Width(88f));
                if (only != modifiedOnly) { modifiedOnly = only; ApplyFilter(); }
                GUILayout.FlexibleSpace();
                GUIContent[] views =
                {
                    new GUIContent(L("Waveforms", "Дорожки"), L("Show detailed waveform rows.", "Показывать подробные строки с дорожками.")),
                    new GUIContent(L("Names", "Названия"), L("Show a compact list of names.", "Показывать компактный список названий."))
                };
                int view = GUILayout.Toolbar((int)ClipSwitchState.instance.LibraryViewMode, views, EditorStyles.toolbarButton, GUILayout.Width(170f));
                if (view != (int)ClipSwitchState.instance.LibraryViewMode)
                {
                    ClipSwitchState.instance.LibraryViewMode = (ClipSwitchListViewMode)view;
                    ClipSwitchState.instance.SaveNow();
                    libraryScroll = Vector2.zero;
                }
                GUI.enabled = ClipSwitchAudioPreview.CurrentClip != null;
                if (GUILayout.Button(Icon("PauseButton", L("Pause or resume preview.", "Приостановить или продолжить звук.")), EditorStyles.toolbarButton, GUILayout.Width(34f))) ClipSwitchAudioPreview.PauseOrResume();
                GUIStyle stopStyle = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
                if (GUILayout.Button(new GUIContent("■", L("Stop preview.", "Остановить звук.")), stopStyle, GUILayout.Width(34f))) ClipSwitchAudioPreview.Stop();
                GUI.enabled = true;
            }
        }

        private void DrawLibrarySelectionBar()
        {
            if (librarySelectedGuids.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(string.Format(L("Selected in Library: {0}", "Выбрано в библиотеке: {0}"), librarySelectedGuids.Count), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(string.Format(L("Open in Editor ({0})", "Открыть в редакторе ({0})"), librarySelectedGuids.Count), L("Load the selected clips into the editor.", "Загрузить выбранные клипы в редактор.")), GUILayout.Height(28f))) OpenLibrarySelectionInEditor();
                if (GUILayout.Button(new GUIContent(L("Clear", "Снять выбор"), L("Clear the pending Library selection.", "Снять отложенное выделение в библиотеке.")), GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    librarySelectedGuids.Clear();
                    lastLibrarySelectedIndex = -1;
                }
            }
        }

        private void DrawColumnHeader()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.14f, 0.15f, 0.17f) : new Color(0.80f, 0.82f, 0.85f));
            if (ClipSwitchState.instance.LibraryViewMode == ClipSwitchListViewMode.Compact)
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height), L("AudioClip name and path", "Название и путь AudioClip"), EditorStyles.boldLabel);
                return;
            }
            Rect name;
            Rect previous;
            Rect current;
            GetColumns(new Rect(rect.x, rect.y, Mathf.Max(1f, rect.width - 16f), rect.height), out name, out previous, out current);
            GUI.Label(new Rect(name.x + 8f, name.y, name.width - 8f, name.height), L("Audio asset", "Аудиоассет"), EditorStyles.boldLabel);
            if (ClipSwitchState.instance.ShowPreviousClipColumn)
                GUI.Label(new Rect(previous.x + 6f, previous.y, previous.width - 6f, previous.height), L("Previous version", "Предыдущая версия"), EditorStyles.boldLabel);
            GUI.Label(new Rect(current.x + 6f, current.y, current.width - 6f, current.height), L("Current clip", "Текущий клип"), EditorStyles.boldLabel);
        }

        private void DrawVirtualizedList()
        {
            Rect viewport = GUILayoutUtility.GetRect(1f, 100000f, 1f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (filtered.Count == 0)
            {
                GUI.Label(viewport, L("No AudioClip assets match the filter.", "Нет AudioClip, соответствующих фильтру."), EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float rowHeight = LibraryRowHeight;
            float viewWidth = Mathf.Max(1f, viewport.width - 16f);
            Rect content = new Rect(0f, 0f, viewWidth, filtered.Count * rowHeight);
            libraryScroll = GUI.BeginScrollView(viewport, libraryScroll, content, false, true);
            int first = Mathf.Clamp(Mathf.FloorToInt(libraryScroll.y / rowHeight), 0, filtered.Count - 1);
            int last = Mathf.Clamp(Mathf.CeilToInt((libraryScroll.y + viewport.height) / rowHeight) + 1, first, filtered.Count);
            for (int i = first; i < last; i++) DrawVirtualRow(new Rect(0f, i * rowHeight, viewWidth, rowHeight - 3f), filtered[i], i);
            GUI.EndScrollView();
        }

        private void DrawVirtualRow(Rect row, AudioClip clip, int index)
        {
            if (clip == null) return;
            string guid = GuidOf(clip);
            bool selected = librarySelectedGuids.Contains(guid);
            // Right-click must be handled before any child button/waveform gets a
            // chance to consume it. This makes the complete list item clickable.
            HandleLibraryContextMenu(row, clip);
            Color background = selected
                ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.35f, 0.50f) : new Color(0.62f, 0.82f, 1f))
                : ((index & 1) == 0
                    ? (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.17f, 0.19f) : new Color(0.92f, 0.93f, 0.95f))
                    : (EditorGUIUtility.isProSkin ? new Color(0.19f, 0.20f, 0.22f) : new Color(0.97f, 0.98f, 0.99f)));
            EditorGUI.DrawRect(row, background);

            string path = AssetDatabase.GetAssetPath(clip);
            if (ClipSwitchState.instance.LibraryViewMode == ClipSwitchListViewMode.Compact)
            {
                string extension = Path.GetExtension(path).ToUpperInvariant();
                string label = clip.name + (string.IsNullOrEmpty(extension) ? string.Empty : "   •   " + extension) + "   •   " + path;
                if (GUI.Button(new Rect(row.x + 6f, row.y + 2f, row.width - 12f, row.height - 4f), new GUIContent(label, path), EditorStyles.label))
                    SelectLibraryClip(index, Event.current.shift, Event.current.control || Event.current.command);
                return;
            }

            Rect name;
            Rect previous;
            Rect current;
            GetColumns(new Rect(row.x, row.y + 5f, row.width, row.height - 10f), out name, out previous, out current);
            Rect drag = new Rect(name.x + 5f, name.y + 4f, 20f, name.height - 8f);
            GUI.Label(drag, Icon("d_Grid.BoxTool", L("Drag onto another waveform to swap.", "Перетащите на другую дорожку для обмена.")));
            HandleDragSource(drag, clip);

            Rect select = new Rect(name.x + 28f, name.y + 2f, name.width - 32f, name.height - 4f);
            GUIStyle selectStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(6, 6, 3, 3)
            };
            string text = "<b>" + clip.name + "</b>\n<size=9>" + path + "</size>";
            if (GUI.Button(select, new GUIContent(text, L("Select this clip. Hold Shift to select a range; loading is deferred.", "Выбрать клип. Shift выбирает диапазон; загрузка будет выполнена позже.")), selectStyle))
                SelectLibraryClip(index, Event.current.shift, Event.current.control || Event.current.command);

            if (ClipSwitchState.instance.ShowPreviousClipColumn)
            {
                AudioClip backup = ClipSwitchOperations.GetLatestBackupClip(clip);
                Texture2D previousTexture = backup == null ? null : ClipSwitchWaveformCache.Get(backup, Mathf.Max(2, (int)previous.width), Mathf.Max(2, (int)previous.height));
                ClipSwitchWaveformGUI.Draw(previous, backup, previousTexture,
                    backup == null ? L("No previous version", "Нет предыдущей версии") : L("Previous: ", "Предыдущая: ") + backup.name, true);
            }

            Texture2D currentTexture = ClipSwitchWaveformCache.Get(clip, Mathf.Max(2, (int)current.width), Mathf.Max(2, (int)current.height));
            ClipSwitchWaveformGUI.Draw(current, clip, currentTexture, clip.name, true);
            HandleClipDrop(current, clip);
        }

        private void SelectLibraryClip(int index, bool shift, bool additive)
        {
            if (index < 0 || index >= filtered.Count) return;
            string guid = GuidOf(filtered[index]);
            if (shift && lastLibrarySelectedIndex >= 0 && lastLibrarySelectedIndex < filtered.Count)
            {
                if (!additive) librarySelectedGuids.Clear();
                int from = Mathf.Min(lastLibrarySelectedIndex, index);
                int to = Mathf.Max(lastLibrarySelectedIndex, index);
                for (int i = from; i <= to; i++)
                {
                    string item = GuidOf(filtered[i]);
                    if (!librarySelectedGuids.Contains(item)) librarySelectedGuids.Add(item);
                }
            }
            else if (additive)
            {
                if (!librarySelectedGuids.Remove(guid)) librarySelectedGuids.Add(guid);
                lastLibrarySelectedIndex = index;
            }
            else
            {
                librarySelectedGuids.Clear();
                librarySelectedGuids.Add(guid);
                lastLibrarySelectedIndex = index;
            }
            Repaint();
        }

        private void HandleLibraryContextMenu(Rect row, AudioClip clicked)
        {
            Event evt = Event.current;
            bool contextEvent = evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1);
            if (!contextEvent || !row.Contains(evt.mousePosition)) return;
            string clickedGuid = GuidOf(clicked);
            List<AudioClip> targets = librarySelectedGuids.Contains(clickedGuid) && librarySelectedGuids.Count > 1
                ? LoadByGuids(librarySelectedGuids)
                : new List<AudioClip> { clicked };
            ShowClipContextMenu(targets, true);
            evt.Use();
        }

        private void ShowClipContextMenu(List<AudioClip> targets, bool includeOpenInEditor)
        {
            if (targets == null || targets.Count == 0) return;
            int count = targets.Count;
            GenericMenu menu = new GenericMenu();
            if (includeOpenInEditor)
            {
                string editorLabel = count == 1 ? L("Open in Editor", "Открыть в редакторе") : string.Format(L("Open in Editor ({0})", "Открыть в редакторе ({0})"), count);
                menu.AddItem(new GUIContent(editorLabel), false, delegate { OpenTargetsInEditor(targets); });
                menu.AddSeparator(string.Empty);
            }
            string replace = count == 1 ? L("Replace With…", "Заменить на…") : string.Format(L("Replace With… ({0})", "Заменить на… ({0})"), count);
            string swap = count == 1 ? L("Swap With…", "Поменять местами с…") : string.Format(L("Swap With… ({0})", "Поменять местами с… ({0})"), count);
            menu.AddItem(new GUIContent(replace), false, delegate { ReplaceTargets(targets); });
            menu.AddItem(new GUIContent(swap), false, delegate { SwapTargets(targets); });
            if (count == 1) menu.AddItem(new GUIContent(L("History", "История")), false, delegate { ClipSwitchHistoryWindow.Open(targets[0]); });
            else menu.AddDisabledItem(new GUIContent(L("History (one clip only)", "История (только один клип)")));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(count == 1 ? L("Find References in Project", "Показать ссылки в проекте") : string.Format(L("Find References in Project ({0})", "Показать ссылки в проекте ({0})"), count)), false, delegate { ClipSwitchReferencesWindow.Open(targets); });
            menu.AddItem(new GUIContent(ClipSwitchExternalEditor.OpenLabel), false, delegate { OpenExternal(targets); });
            menu.ShowAsContext();
        }

        private void DrawEditorTab()
        {
            if (audioEditor.LoadedCount == 0)
            {
                EditorGUILayout.Space(12f);
                EditorGUILayout.HelpBox(L("The editor is empty. Load an audio file, choose a project clip, or send the current Library selection here.", "Редактор пуст. Загрузите аудиофайл, выберите клип проекта или передайте сюда текущее выделение библиотеки."), MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(L("Load Audio File…", "Загрузить аудиофайл…"), L("Import an audio file from disk into the project and load it.", "Импортировать аудиофайл с диска в проект и загрузить его.")), GUILayout.Height(34f)))
                        LoadExternalFileForEditor();
                    if (GUILayout.Button(new GUIContent(L("Choose a Project Clip…", "Выбрать клип проекта…"), L("Open the ClipSwitch clip picker.", "Открыть окно выбора клипа ClipSwitch.")), GUILayout.Height(34f)))
                        ChooseClipForEditor();
                    GUI.enabled = librarySelectedGuids.Count > 0;
                    if (GUILayout.Button(new GUIContent(string.Format(L("Load Library Selection ({0})", "Загрузить выбранное в библиотеке ({0})"), librarySelectedGuids.Count), L("Load the clips selected in Library.", "Загрузить клипы, выбранные в библиотеке.")), GUILayout.Height(34f)))
                        OpenLibrarySelectionInEditor();
                    GUI.enabled = true;
                }
                if (GUILayout.Button(L("Open Library", "Открыть библиотеку"), GUILayout.Height(28f))) tab = MainTab.Library;
                return;
            }

            List<AudioClip> selected = audioEditor.SelectedClips;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button(new GUIContent("← " + L("Library", "Библиотека"), L("Return to clip selection.", "Вернуться к выбору клипов.")), GUILayout.Width(105f), GUILayout.Height(28f))) tab = MainTab.Library;
                GUILayout.FlexibleSpace();
                GUI.enabled = selected.Count > 0;
                if (GUILayout.Button(new GUIContent(selected.Count > 1 ? string.Format(L("Replace ({0})…", "Заменить ({0})…"), selected.Count) : L("Replace With…", "Заменить на…"), L("Replace the selected clip contents while preserving GUIDs.", "Заменить содержимое выбранных клипов с сохранением GUID.")), GUILayout.Height(28f))) ReplaceTargets(selected);
                if (GUILayout.Button(new GUIContent(selected.Count > 1 ? string.Format(L("Swap ({0})…", "Поменять ({0})…"), selected.Count) : L("Swap With…", "Поменять местами с…"), L("Choose the same number of project clips and exchange their sounds.", "Выбрать столько же клипов проекта и обменять их звуки.")), GUILayout.Height(28f))) SwapTargets(selected);
                GUI.enabled = selected.Count == 1;
                if (GUILayout.Button(new GUIContent(L("History", "История"), L("Open complete visual history for the selected clip.", "Открыть полную визуальную историю выбранного клипа.")), GUILayout.Height(28f))) ClipSwitchHistoryWindow.Open(selected[0]);
                GUI.enabled = selected.Count > 0;
                if (GUILayout.Button(new GUIContent(ClipSwitchExternalEditor.OpenLabel, L("Open every selected file in the configured application.", "Открыть все выбранные файлы в настроенном приложении.")), GUILayout.Height(28f))) OpenExternal(selected);
                GUI.enabled = true;
            }
            audioEditor.Draw();
        }

        private void ChooseClipForEditor()
        {
            ClipSwitchClipPickerWindow.Open(1, null, L("Choose AudioClip", "Выбор AudioClip"), delegate(List<AudioClip> chosen)
            {
                if (chosen.Count == 0) return;
                OpenTargetsInEditor(chosen);
            });
        }

        private void LoadExternalFileForEditor()
        {
            string source = EditorUtility.OpenFilePanelWithFilters(L("Choose audio file", "Выберите аудиофайл"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), ClipSwitchOperations.SupportedFilePanelFilters);
            if (string.IsNullOrEmpty(source)) return;
            string extension = Path.GetExtension(source).TrimStart('.');
            string destination = EditorUtility.SaveFilePanelInProject(
                L("Import audio into project", "Импорт аудио в проект"),
                Path.GetFileNameWithoutExtension(source), extension,
                L("Choose where the imported AudioClip will be stored.", "Выберите, где будет сохранён импортированный AudioClip."));
            if (string.IsNullOrEmpty(destination) || !ConfirmEditorReplacement(null)) return;

            string importedPath = string.Empty;
            try
            {
                destination = AssetDatabase.GenerateUniqueAssetPath(destination);
                importedPath = destination;
                string absoluteDestination = ClipSwitchPathUtility.AssetPathToAbsolute(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteDestination));
                File.Copy(source, absoluteDestination, false);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AudioClip imported = AssetDatabase.LoadAssetAtPath<AudioClip>(destination);
                if (imported == null) throw new InvalidOperationException(L("Unity could not import the selected audio file.", "Unity не смогла импортировать выбранный аудиофайл."));
                LoadEditor(new List<AudioClip> { imported });
                tab = MainTab.Editor;
                Repaint();
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(importedPath)) AssetDatabase.DeleteAsset(importedPath);
                ShowError(ex.Message);
            }
        }

        private void OpenLibrarySelectionInEditor()
        {
            if (!TransferSelectionToEditor()) return;
            tab = MainTab.Editor;
            GUI.FocusControl(null);
            Repaint();
        }

        private bool TransferSelectionToEditor()
        {
            List<AudioClip> selected = LoadByGuids(librarySelectedGuids);
            if (selected.Count == 0)
            {
                ShowError(L("The selected clips no longer exist.", "Выбранные клипы больше не существуют."));
                return false;
            }
            return TryLoadEditor(selected);
        }

        private void OpenTargetsInEditor(List<AudioClip> targets)
        {
            if (!TryLoadEditor(targets)) return;
            tab = MainTab.Editor;
            GUI.FocusControl(null);
            Repaint();
        }

        private bool TryLoadEditor(IList<AudioClip> selected)
        {
            if (selected == null || selected.Count == 0) return false;
            List<string> incoming = GuidsOf(selected);
            if (audioEditor.Matches(incoming)) return true;
            if (!ConfirmEditorReplacement(incoming)) return false;
            LoadEditor(selected);
            return true;
        }

        private bool ConfirmEditorReplacement(IList<string> incomingGuids)
        {
            if (audioEditor.LoadedCount == 0) return true;
            if (incomingGuids != null && audioEditor.Matches(incomingGuids)) return true;
            return EditorUtility.DisplayDialog(
                L("Replace the current editor session?", "Заменить текущую сессию редактора?"),
                string.Format(L("The editor already contains {0} clip(s). Loading new clips will discard any unsaved processing settings. Continue?", "В редакторе уже открыты клипы ({0}). Загрузка новых клипов сбросит несохранённые настройки обработки. Продолжить?"), audioEditor.LoadedCount),
                L("Load New Clips", "Загрузить новые"), L("Cancel", "Отмена"));
        }

        private void LoadEditor(IList<AudioClip> selected)
        {
            audioEditor.LoadClips(selected);
            loadedEditorGuids.Clear();
            for (int i = 0; i < selected.Count; i++)
            {
                string guid = GuidOf(selected[i]);
                if (!string.IsNullOrEmpty(guid) && !loadedEditorGuids.Contains(guid)) loadedEditorGuids.Add(guid);
            }
        }

        private void ReplaceTargets(List<AudioClip> targets)
        {
            if (targets == null || targets.Count == 0) return;
            if (targets.Count == 1)
            {
                string source = EditorUtility.OpenFilePanelWithFilters(L("Choose replacement audio", "Выберите аудио для замены"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), ClipSwitchOperations.SupportedFilePanelFilters);
                if (string.IsNullOrEmpty(source)) return;
                if (!Confirm(L("Replace audio asset?", "Заменить аудиоассет?"), L("The old sound will remain in visual history. GUID and references are preserved.", "Старый звук останется в визуальной истории. GUID и ссылки сохранятся."), L("Replace", "Заменить"))) return;
                string error;
                string guid = GuidOf(targets[0]);
                if (!ClipSwitchOperations.ReplaceFromExternal(targets[0], source, out error)) ShowError(error); else OnAssetChanged(guid);
                return;
            }

            ClipSwitchClipPickerWindow.Open(targets.Count, GuidsOf(targets), string.Format(L("Choose {0} replacement clips", "Выберите клипы для замены: {0}"), targets.Count), delegate(List<AudioClip> sources)
            {
                if (sources.Count != targets.Count) return;
                if (!Confirm(L("Replace multiple audio assets?", "Заменить несколько аудиоассетов?"), string.Format(L("Replace {0} clips in list order? Every old sound will be added to History.", "Заменить клипы ({0}) в порядке списка? Каждый старый звук будет добавлен в Историю."), targets.Count), L("Replace All", "Заменить все"))) return;
                List<string> paths = new List<string>();
                for (int i = 0; i < sources.Count; i++) paths.Add(ClipSwitchPathUtility.AssetPathToAbsolute(AssetDatabase.GetAssetPath(sources[i])));
                string error;
                if (!ClipSwitchOperations.BatchReplace(targets, paths, out error)) ShowError(error);
                RefreshAfterOperation(targets);
            });
        }

        private void SwapTargets(List<AudioClip> targets)
        {
            if (targets == null || targets.Count == 0) return;
            ClipSwitchClipPickerWindow.Open(targets.Count, GuidsOf(targets), targets.Count == 1 ? L("Select AudioClip to swap", "Выберите AudioClip для обмена") : string.Format(L("Select {0} clips to swap", "Выберите клипы для обмена: {0}"), targets.Count), delegate(List<AudioClip> sources)
            {
                if (sources.Count != targets.Count) return;
                ExecuteSwaps(targets, sources);
            });
        }

        private void ExecuteSwaps(List<AudioClip> targets, List<AudioClip> sources)
        {
            if (targets == null || sources == null || targets.Count == 0 || targets.Count != sources.Count) return;
            string names = targets.Count == 1 ? targets[0].name + "  ↔  " + sources[0].name : string.Format(L("Swap {0} clip pairs in list order?", "Поменять местами пары клипов ({0}) в порядке списка?"), targets.Count);
            if (!Confirm(L("Swap audio contents?", "Поменять звуки местами?"), names + "\n\n" + L("All GUIDs and references are preserved.", "Все GUID и ссылки сохранятся."), targets.Count == 1 ? L("Swap", "Поменять") : L("Swap All", "Поменять все"))) return;
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("ClipSwitch", targets[i].name + "  ↔  " + sources[i].name, (float)i / targets.Count);
                    string error;
                    if (!ClipSwitchOperations.Swap(targets[i], sources[i], out error)) { ShowError(error); break; }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            RefreshAfterOperation(targets);
            RefreshAfterOperation(sources);
        }

        private void RefreshAfterOperation(IList<AudioClip> affected)
        {
            ClipSwitchWaveformCache.Clear();
            for (int i = 0; i < affected.Count; i++)
            {
                string guid = GuidOf(affected[i]);
                if (!string.IsNullOrEmpty(guid)) audioEditor.RefreshGuid(guid);
            }
            Refresh();
            Repaint();
        }

        private void HandleClipDrop(Rect rect, AudioClip target)
        {
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)) return;
            AudioClip source = null;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++) { source = DragAndDrop.objectReferences[i] as AudioClip; if (source != null && source != target) break; }
            string external = null;
            for (int i = 0; i < DragAndDrop.paths.Length; i++) if (File.Exists(DragAndDrop.paths[i]) && ClipSwitchOperations.IsSupportedAudioPath(DragAndDrop.paths[i])) { external = DragAndDrop.paths[i]; break; }
            if (source == null && external == null) return;
            DragAndDrop.visualMode = source != null ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (source != null) ExecuteSwaps(new List<AudioClip> { target }, new List<AudioClip> { source });
                else if (Confirm(L("Replace dropped audio?", "Заменить перетащенным аудио?"), Path.GetFileName(external), L("Replace", "Заменить")))
                {
                    string guid = GuidOf(target);
                    string error;
                    if (!ClipSwitchOperations.ReplaceFromExternal(target, external, out error)) ShowError(error); else OnAssetChanged(guid);
                }
            }
            evt.Use();
        }

        private void HandleDragSource(Rect rect, AudioClip clip)
        {
            Event evt = Event.current;
            int id = GUIUtility.GetControlID(("ClipSwitchDrag" + clip.GetInstanceID()).GetHashCode(), FocusType.Passive, rect);
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition)) GUIUtility.hotControl = id;
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { clip };
                DragAndDrop.paths = new[] { AssetDatabase.GetAssetPath(clip) };
                DragAndDrop.StartDrag(L("Swap ", "Поменять ") + clip.name);
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
        }

        private void DrawSettings()
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("Interface", "Интерфейс"), EditorStyles.boldLabel);
                string[] languages = { L("Auto (system)", "Авто (система)"), "English", "Русский" };
                int language = EditorGUILayout.Popup(ClipSwitchLocalization.C("Language", "Язык", "Language used by ClipSwitch.", "Язык интерфейса ClipSwitch."), (int)ClipSwitchState.instance.Language, languages);
                if (language != (int)ClipSwitchState.instance.Language)
                {
                    ClipSwitchState.instance.Language = (ClipSwitchLanguage)language;
                    ClipSwitchState.instance.SaveNow();
                    UpdateTitle();
                }

                bool showPrevious = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Show Previous Version Column", "Показывать столбец предыдущей версии", "Display the previous waveform beside the current clip in Library.", "Показывать предыдущую дорожку рядом с текущим клипом в Библиотеке."), ClipSwitchState.instance.ShowPreviousClipColumn);
                if (showPrevious != ClipSwitchState.instance.ShowPreviousClipColumn) { ClipSwitchState.instance.ShowPreviousClipColumn = showPrevious; ClipSwitchState.instance.SaveNow(); }
                bool confirm = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Confirm Destructive Actions", "Подтверждать изменения", "Ask before replace, restore, and swap.", "Спрашивать перед заменой, восстановлением и обменом."), ClipSwitchState.instance.ConfirmDestructiveActions);
                if (confirm != ClipSwitchState.instance.ConfirmDestructiveActions) { ClipSwitchState.instance.ConfirmDestructiveActions = confirm; ClipSwitchState.instance.SaveNow(); }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("External audio application", "Внешнее аудиоприложение"), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    string app = EditorGUILayout.TextField(ClipSwitchLocalization.C("Application", "Приложение", "Executable used by Open In; leave empty for the OS default.", "Приложение для команды открытия; оставьте пустым для приложения ОС."), ClipSwitchState.instance.ExternalEditorPath);
                    if (app != ClipSwitchState.instance.ExternalEditorPath) { ClipSwitchState.instance.ExternalEditorPath = app; ClipSwitchState.instance.SaveNow(); }
                    if (GUILayout.Button(ClipSwitchLocalization.C("Browse…", "Обзор…", "Choose an executable application.", "Выбрать исполняемый файл приложения."), GUILayout.Width(78f))) BrowseExternalApplication();
                }
                EditorGUILayout.LabelField(L("Current command", "Текущая команда"), ClipSwitchExternalEditor.OpenLabel);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("History and cache", "История и кэш"), EditorStyles.boldLabel);
                string backup = EditorGUILayout.TextField(ClipSwitchLocalization.C("Backup Folder", "Папка копий", "Project folder containing history snapshots.", "Папка проекта со снимками истории."), ClipSwitchState.instance.BackupRoot);
                backup = ClipSwitchPathUtility.NormalizeAssetPath(backup).TrimEnd('/');
                if (backup != ClipSwitchState.instance.BackupRoot && ClipSwitchPathUtility.IsProjectAssetPath(backup)) { ClipSwitchState.instance.BackupRoot = backup; ClipSwitchState.instance.SaveNow(); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(ClipSwitchLocalization.C("Show Backup Folder", "Показать папку копий", "Select the backup folder in Project.", "Выбрать папку копий в Project."))) ShowBackupFolder();
                    if (GUILayout.Button(ClipSwitchLocalization.C("Clear Waveform Cache", "Очистить кэш дорожек", "Release cached waveform textures.", "Освободить кэшированные текстуры дорожек."))) { ClipSwitchWaveformCache.Clear(); Repaint(); }
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("Documentation", "Документация"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(L("Open the complete offline ClipSwitch guide in your browser.", "Открыть полное офлайн-руководство ClipSwitch в браузере."), EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(new GUIContent(L("Open Offline Documentation", "Открыть офлайн-документацию"), EditorGUIUtility.IconContent("_Help").image, L("Open the documentation bundled with this package. Internet access is not required.", "Открыть документацию из пакета. Доступ к интернету не требуется.")), GUILayout.Height(30f)))
                {
                    string error;
                    if (!ClipSwitchDocumentation.Open(out error)) ShowError(error);
                }
            }
        }

        private void GetColumns(Rect area, out Rect name, out Rect previous, out Rect current)
        {
            float gap = 5f;
            float nameWidth = Mathf.Clamp(area.width * 0.28f, 180f, 260f);
            name = new Rect(area.x, area.y, nameWidth, area.height);
            float audioX = name.xMax + gap;
            float audioWidth = Mathf.Max(80f, area.xMax - audioX);
            if (ClipSwitchState.instance.ShowPreviousClipColumn)
            {
                float previousWidth = audioWidth * 0.43f;
                previous = new Rect(audioX, area.y, previousWidth - gap * 0.5f, area.height);
                current = new Rect(previous.xMax + gap, area.y, area.xMax - (previous.xMax + gap), area.height);
            }
            else
            {
                previous = Rect.zero;
                current = new Rect(audioX, area.y, audioWidth, area.height);
            }
        }

        private void OnAssetChanged(string guid)
        {
            ClipSwitchWaveformCache.Clear();
            Refresh();
            if (audioEditor != null) audioEditor.RefreshGuid(guid);
            Repaint();
        }

        private void OpenExternal(IList<AudioClip> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                string error;
                if (!ClipSwitchExternalEditor.Open(targets[i], out error) && !string.IsNullOrEmpty(error))
                {
                    ShowError(error);
                    return;
                }
            }
        }

        private void BrowseExternalApplication()
        {
            string current = ClipSwitchState.instance.ExternalEditorPath;
            string directory = string.IsNullOrEmpty(current) ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : Path.GetDirectoryName(current);
            string app = EditorUtility.OpenFilePanel(L("Choose external audio application", "Выберите внешнее аудиоприложение"), directory, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty);
            if (!string.IsNullOrEmpty(app)) { ClipSwitchState.instance.ExternalEditorPath = app; ClipSwitchState.instance.SaveNow(); }
        }

        private void ShowBackupFolder()
        {
            try
            {
                ClipSwitchPathUtility.EnsureAssetFolder(ClipSwitchState.instance.BackupRoot);
                DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ClipSwitchState.instance.BackupRoot);
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        }

        private void Refresh()
        {
            clips.Clear();
            string[] folders = AssetDatabase.IsValidFolder(folderPath) ? new[] { folderPath } : new[] { "Assets" };
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", folders);
            string backupRoot = ClipSwitchPathUtility.NormalizeAssetPath(ClipSwitchState.instance.BackupRoot).TrimEnd('/');
            HashSet<string> live = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(backupRoot) && (path == backupRoot || path.StartsWith(backupRoot + "/", StringComparison.OrdinalIgnoreCase))) continue;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) { clips.Add(clip); live.Add(guids[i]); }
            }
            clips.Sort(delegate(AudioClip a, AudioClip b) { return string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.OrdinalIgnoreCase); });
            librarySelectedGuids.RemoveAll(delegate(string guid) { return !live.Contains(guid); });
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            filtered.Clear();
            string needle = string.IsNullOrWhiteSpace(search) ? string.Empty : search.Trim();
            HashSet<string> historyGuids = null;
            if (modifiedOnly)
            {
                historyGuids = new HashSet<string>(StringComparer.Ordinal);
                List<ClipSwitchHistoryEntry> history = ClipSwitchState.instance.History;
                for (int i = 0; i < history.Count; i++) if (history[i] != null && !string.IsNullOrEmpty(history[i].TargetGuid)) historyGuids.Add(history[i].TargetGuid);
            }
            for (int i = 0; i < clips.Count; i++)
            {
                AudioClip clip = clips[i];
                string path = AssetDatabase.GetAssetPath(clip);
                if (!string.IsNullOrEmpty(needle) && clip.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (modifiedOnly && !historyGuids.Contains(GuidOf(clip))) continue;
                filtered.Add(clip);
            }
            lastLibrarySelectedIndex = -1;
            Repaint();
        }

        private float LibraryRowHeight
        {
            get { return ClipSwitchState.instance.LibraryViewMode == ClipSwitchListViewMode.Compact ? CompactRowHeight : DetailedRowHeight; }
        }

        private void EnsureEditor()
        {
            if (audioEditor == null) audioEditor = new ClipSwitchMultiAudioEditor();
            audioEditor.ContextMenuRequested = delegate(List<AudioClip> targets) { ShowClipContextMenu(targets, false); };
        }
        private void UpdateTitle() { titleContent = new GUIContent(L("ClipSwitch — Audio Replacement", "ClipSwitch — Замена звуков"), EditorGUIUtility.IconContent("AudioSource Icon").image); }
        private static string GuidOf(AudioClip clip) { return clip == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)); }

        private static List<string> GuidsOf(IList<AudioClip> items)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < items.Count; i++) result.Add(GuidOf(items[i]));
            return result;
        }

        private static List<AudioClip> LoadByGuids(IList<string> guids)
        {
            List<AudioClip> result = new List<AudioClip>();
            if (guids == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < guids.Count; i++)
            {
                if (string.IsNullOrEmpty(guids[i]) || !seen.Add(guids[i])) continue;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (clip != null) result.Add(clip);
            }
            return result;
        }

        private bool Confirm(string title, string message, string ok) { return !ClipSwitchState.instance.ConfirmDestructiveActions || EditorUtility.DisplayDialog(title, message, ok, L("Cancel", "Отмена")); }
        private static void ShowError(string error) { EditorUtility.DisplayDialog("ClipSwitch", error, "OK"); }
        private static string L(string english, string russian) { return ClipSwitchLocalization.T(english, russian); }

        private static GUIContent Icon(string name, string tooltip)
        {
            GUIContent icon = EditorGUIUtility.IconContent(name);
            icon.tooltip = tooltip;
            return icon;
        }

        private static GUIContent TabContent(string iconName, string text, string tooltip)
        {
            return new GUIContent(text, EditorGUIUtility.IconContent(iconName).image, tooltip);
        }
    }
}
