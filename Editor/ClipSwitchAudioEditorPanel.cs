using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchAudioEditorPanel : IDisposable
    {
        private static readonly string[] Platforms = { "Default", "Standalone", "Android", "iOS", "WebGL" };

        private AudioClip clip;
        private string clipGuid;
        private ClipSwitchWavData sourceData;
        private ClipSwitchWavData processedData;
        private AudioClip previewClip;
        private string loadError;
        private string processingError;
        private Vector2 scroll;
        private int platformIndex;
        private bool unityFoldout = true;
        private bool advancedFoldout = true;
        private bool importSettingsDirty;

        private double trimStart;
        private double trimEnd;
        private float gainDb;
        private bool normalize;
        private float normalizeTargetDb = -0.5f;
        private bool reverse;
        private bool removeDcOffset;
        private float fadeIn;
        private float fadeOut;
        private float pitchSemitones;
        private float silenceThresholdDb = -45f;

        private Task<ClipSwitchWavData> processTask;
        private int requestedRevision;
        private int runningRevision;
        private double processAfter;
        private int importAppliedRevision;

        public AudioClip Clip { get { return clip; } }
        public bool IsBusy { get { return processTask != null; } }
        public bool IsReady { get { return processedData != null && processTask == null && string.IsNullOrEmpty(loadError) && string.IsNullOrEmpty(processingError); } }
        public int SettingsRevision { get { return requestedRevision; } }
        public int ImportAppliedRevision { get { return importAppliedRevision; } }
        public string Guid { get { return clipGuid; } }
        public string DisplayName
        {
            get
            {
                if (clip == null) return string.Empty;
                string extension = Path.GetExtension(AssetDatabase.GetAssetPath(clip)).ToUpperInvariant();
                return clip.name + (string.IsNullOrEmpty(extension) ? string.Empty : "   •   " + extension);
            }
        }

        public void SetClip(AudioClip value)
        {
            requestedRevision++;
            DisposePreview();
            clip = value;
            clipGuid = value == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
            sourceData = null;
            processedData = null;
            loadError = string.Empty;
            processingError = string.Empty;
            if (value == null) return;

            try
            {
                sourceData = ClipSwitchAudioDataUtility.ReadClip(value);
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(clipGuid));
                ResetAdvancedSettings();
                RequestProcessing(true);
            }
            catch (Exception ex) { loadError = ex.Message; }
        }

        public void Update()
        {
            if (processTask != null && processTask.IsCompleted)
            {
                Task<ClipSwitchWavData> completed = processTask;
                int revision = runningRevision;
                processTask = null;
                try
                {
                    ClipSwitchWavData result = completed.Result;
                    if (revision == requestedRevision)
                    {
                        processedData = result;
                        processingError = string.Empty;
                        RebuildPreviewClip();
                    }
                }
                catch (Exception ex)
                {
                    Exception actual = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                    processingError = actual.Message == "Trim range is empty."
                        ? L("Trim range is empty.", "Диапазон обрезки пуст.")
                        : actual.Message;
                }
            }

            if (processTask == null && sourceData != null && runningRevision != requestedRevision &&
                EditorApplication.timeSinceStartup >= processAfter)
            {
                ClipSwitchWavProcessSettings settings = CreateSettings();
                ClipSwitchWavData source = sourceData;
                runningRevision = requestedRevision;
                processTask = Task.Run(delegate { return ClipSwitchWavProcessor.Process(source, settings); });
            }
        }

        public void Draw()
        {
            if (clip == null)
            {
                EditorGUILayout.HelpBox(L("Select one clip in the Library and choose Edit.", "Выберите один клип в Библиотеке и нажмите «Редактировать»."), MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();
            if (!string.IsNullOrEmpty(loadError))
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawWaveform();
            EditorGUILayout.Space(8f);
            DrawUnitySettings();
            EditorGUILayout.Space(8f);
            DrawAdvancedSettings();
            EditorGUILayout.Space(12f);
            DrawSaveActions();
            EditorGUILayout.EndScrollView();
        }

        public void DrawSettingsOnly(bool showSaveActions)
        {
            if (clip == null) return;
            if (!string.IsNullOrEmpty(loadError))
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Error);
                return;
            }
            DrawUnitySettings();
            EditorGUILayout.Space(8f);
            DrawAdvancedSettings();
            if (showSaveActions)
            {
                EditorGUILayout.Space(12f);
                DrawSaveActions();
            }
        }

        public void DrawCompactWaveform(Rect rect, bool allowScrub)
        {
            Texture2D texture = processedData == null ? null : ClipSwitchWaveformCache.Get(processedData, "editor-" + clipGuid + "-" + runningRevision, Mathf.Max(2, (int)rect.width), Mathf.Max(2, (int)rect.height));
            ClipSwitchWaveformGUI.Draw(rect, previewClip, texture, DisplayName, allowScrub);
            if (IsBusy) GUI.Label(new Rect(rect.xMax - 110f, rect.yMax - 18f, 104f, 16f), L("Processing…", "Обработка…"), EditorStyles.whiteMiniLabel);
        }

        public void Play()
        {
            if (previewClip != null) ClipSwitchAudioPreview.Play(previewClip, 0f);
        }

        public void CopyProcessingFrom(ClipSwitchAudioEditorPanel source)
        {
            if (source == null || source == this || sourceData == null) return;
            trimStart = Math.Max(0.0, Math.Min(source.trimStart, sourceData.Duration));
            trimEnd = Math.Max(trimStart, Math.Min(source.trimEnd, sourceData.Duration));
            gainDb = source.gainDb;
            normalize = source.normalize;
            normalizeTargetDb = source.normalizeTargetDb;
            reverse = source.reverse;
            removeDcOffset = source.removeDcOffset;
            fadeIn = Mathf.Min(source.fadeIn, (float)sourceData.Duration);
            fadeOut = Mathf.Min(source.fadeOut, (float)sourceData.Duration);
            pitchSemitones = source.pitchSemitones;
            silenceThresholdDb = source.silenceThresholdDb;
            RequestProcessing(false);
        }

        public bool CopyImporterSettingsFrom(ClipSwitchAudioEditorPanel source, out string error)
        {
            error = string.Empty;
            if (source == null || source == this) return true;
            try
            {
                AudioImporter from = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(source.clipGuid)) as AudioImporter;
                AudioImporter to = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(clipGuid)) as AudioImporter;
                if (from == null || to == null) throw new InvalidOperationException(L("Audio importer is unavailable.", "Аудиоимпортер недоступен."));
                to.forceToMono = from.forceToMono;
                SetNormalize(to, GetNormalize(from));
                to.loadInBackground = from.loadInBackground;
                to.ambisonic = from.ambisonic;
                to.defaultSampleSettings = from.defaultSampleSettings;
                for (int i = 1; i < Platforms.Length; i++)
                {
                    if (from.ContainsSampleSettingsOverride(Platforms[i])) to.SetOverrideSampleSettings(Platforms[i], from.GetOverrideSampleSettings(Platforms[i]));
                    else to.ClearSampleSettingOverride(Platforms[i]);
                }
                to.SaveAndReimport();
                ReloadSourceAfterImport();
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool ApplyProcessed(out string error)
        {
            error = string.Empty;
            if (!IsReady) { error = L("Processed audio is not ready.", "Обработанный звук ещё не готов."); return false; }
            string temporary = Path.Combine(Path.GetTempPath(), "ClipSwitchProcessed_" + System.Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                ClipSwitchWavCodec.Write(temporary, processedData);
                if (!ClipSwitchOperations.ReplaceFromExternal(clip, temporary, "Process", out error)) return false;
                SetClip(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(clipGuid)));
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }

        public void Dispose()
        {
            requestedRevision++;
            DisposePreview();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(new GUIContent(DisplayName, AssetDatabase.GetAssetPath(clip)), EditorStyles.boldLabel);
                GUI.enabled = previewClip != null;
                if (GUILayout.Button(Icon("PlayButton", "Play", "Воспроизвести", "Play the processed realtime preview.", "Воспроизвести обработанный realtime-preview."), GUILayout.Width(36f), GUILayout.Height(24f)))
                    ClipSwitchAudioPreview.Play(previewClip, 0f);
                GUI.enabled = ClipSwitchAudioPreview.CurrentClip != null;
                if (GUILayout.Button(Icon("PauseButton", "Pause", "Пауза", "Pause or resume audio preview.", "Приостановить или продолжить предпрослушивание."), GUILayout.Width(36f), GUILayout.Height(24f)))
                    ClipSwitchAudioPreview.PauseOrResume();
                GUIStyle stopStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                if (GUILayout.Button(new GUIContent("■", L("Stop audio preview.", "Остановить предпрослушивание.")), stopStyle, GUILayout.Width(36f), GUILayout.Height(24f)))
                    ClipSwitchAudioPreview.Stop();
                GUI.enabled = true;
            }
        }

        private void DrawWaveform()
        {
            Rect rect = GUILayoutUtility.GetRect(10f, 118f, GUILayout.ExpandWidth(true));
            Texture2D texture = processedData == null ? null : ClipSwitchWaveformCache.Get(processedData, "editor-" + clipGuid + "-" + runningRevision, Mathf.Max(2, (int)rect.width), Mathf.Max(2, (int)rect.height));
            ClipSwitchWaveformGUI.Draw(rect, previewClip, texture, DisplayName, true);
            if (IsBusy)
                GUI.Label(new Rect(rect.xMax - 110f, rect.yMax - 19f, 104f, 16f), L("Processing…", "Обработка…"), EditorStyles.whiteMiniLabel);
            if (!string.IsNullOrEmpty(processingError)) EditorGUILayout.HelpBox(processingError, MessageType.Error);
        }

        private void DrawUnitySettings()
        {
            unityFoldout = EditorGUILayout.Foldout(unityFoldout, ClipSwitchLocalization.C("Unity Import Settings", "Настройки импорта Unity", "The same core settings as the AudioClip Inspector.", "Основные параметры из стандартного инспектора AudioClip."), true);
            if (!unityFoldout) return;
            string path = AssetDatabase.GUIDToAssetPath(clipGuid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Undo.RecordObject(importer, "ClipSwitch audio import settings");
                EditorGUI.BeginChangeCheck();
                importer.forceToMono = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Force To Mono", "Преобразовать в Mono", "Downmix all channels to one channel during import.", "Свести все каналы в один при импорте."), importer.forceToMono);
                using (new EditorGUI.DisabledScope(!importer.forceToMono))
                {
                    bool oldNormalize = GetNormalize(importer);
                    bool newNormalize = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Normalize", "Нормализация", "Normalize after mono downmix to avoid a volume drop.", "Нормализовать после сведения в mono, чтобы избежать падения громкости."), oldNormalize);
                    if (newNormalize != oldNormalize) SetNormalize(importer, newNormalize);
                }
                importer.loadInBackground = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Load In Background", "Загрузка в фоне", "Load audio without blocking the main thread.", "Загружать аудио без блокировки главного потока."), importer.loadInBackground);
                importer.ambisonic = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Ambisonic", "Амбисонический", "Treat the source as ambisonic audio.", "Считать источник амбисоническим аудио."), importer.ambisonic);
                if (EditorGUI.EndChangeCheck()) importSettingsDirty = true;

                string[] names = ClipSwitchLocalization.IsRussian
                    ? new[] { "По умолчанию", "ПК", "Android", "iOS", "WebGL" }
                    : Platforms;
                GUIContent[] platformLabels = new GUIContent[names.Length];
                for (int i = 0; i < names.Length; i++)
                    platformLabels[i] = new GUIContent(names[i], L("Edit import settings for this platform.", "Изменить настройки импорта для этой платформы."));
                platformIndex = GUILayout.Toolbar(platformIndex, platformLabels);
                string platform = Platforms[platformIndex];
                bool isDefault = platformIndex == 0;
                bool overridden = isDefault || importer.ContainsSampleSettingsOverride(platform);
                if (!isDefault)
                {
                    bool newOverride = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Override for " + platform, "Переопределить для " + platform, "Use platform-specific audio import settings.", "Использовать отдельные настройки импорта для платформы."), overridden);
                    if (newOverride != overridden)
                    {
                        if (newOverride) importer.SetOverrideSampleSettings(platform, importer.GetOverrideSampleSettings(platform));
                        else importer.ClearSampleSettingOverride(platform);
                        overridden = newOverride;
                        importSettingsDirty = true;
                    }
                }

                using (new EditorGUI.DisabledScope(!overridden))
                {
                    AudioImporterSampleSettings settings = isDefault ? importer.defaultSampleSettings : importer.GetOverrideSampleSettings(platform);
                    EditorGUI.BeginChangeCheck();
                    settings.loadType = DrawLoadType(settings.loadType);
                    settings.preloadAudioData = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Preload Audio Data", "Предзагрузка аудиоданных", "Load sample data when the clip asset loads.", "Загружать сэмплы вместе с ассетом клипа."), settings.preloadAudioData);
                    settings.compressionFormat = (AudioCompressionFormat)EditorGUILayout.EnumPopup(ClipSwitchLocalization.C("Compression Format", "Формат сжатия", "Runtime encoding used for this platform.", "Кодирование, используемое на этой платформе."), settings.compressionFormat);
                    using (new EditorGUI.DisabledScope(settings.compressionFormat == AudioCompressionFormat.PCM || settings.compressionFormat == AudioCompressionFormat.ADPCM))
                        settings.quality = EditorGUILayout.Slider(ClipSwitchLocalization.C("Quality", "Качество", "Compression quality from smallest to highest fidelity.", "Качество сжатия: от меньшего размера к лучшей точности."), settings.quality, 0.01f, 1f);
                    settings.sampleRateSetting = DrawSampleRate(settings.sampleRateSetting);
                    if (settings.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate)
                        settings.sampleRateOverride = (uint)Mathf.Max(1, EditorGUILayout.IntField(ClipSwitchLocalization.C("Sample Rate", "Частота", "Target sample rate in Hz.", "Целевая частота в Гц."), (int)settings.sampleRateOverride));

                    if (EditorGUI.EndChangeCheck())
                    {
                        if (isDefault) importer.defaultSampleSettings = settings;
                        else if (overridden) importer.SetOverrideSampleSettings(platform, settings);
                        importSettingsDirty = true;
                    }
                }
                DrawSizeInfo(clip);
                GUI.enabled = importSettingsDirty;
                if (GUILayout.Button(ClipSwitchLocalization.C("Apply Unity Import Settings", "Применить настройки импорта Unity", "Save import settings and reimport this AudioClip.", "Сохранить настройки и повторно импортировать AudioClip."), GUILayout.Height(26f)))
                    ApplyImporter(importer);
                GUI.enabled = true;
            }
        }

        private void DrawAdvancedSettings()
        {
            advancedFoldout = EditorGUILayout.Foldout(advancedFoldout, ClipSwitchLocalization.C("ClipSwitch Advanced Processing", "Расширенная обработка ClipSwitch", "Non-destructive realtime controls until you save.", "Неразрушающие realtime-настройки до сохранения."), true);
            if (!advancedFoldout || sourceData == null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField(L("Trim and silence", "Обрезка и тишина"), EditorStyles.boldLabel);
                trimStart = EditorGUILayout.DoubleField(ClipSwitchLocalization.C("Start (seconds)", "Начало (секунды)", "Time kept as the beginning of the result.", "Время, с которого начинается результат."), trimStart);
                trimEnd = EditorGUILayout.DoubleField(ClipSwitchLocalization.C("End (seconds)", "Конец (секунды)", "Time kept as the end of the result.", "Время, на котором заканчивается результат."), trimEnd);
                trimStart = Math.Max(0.0, Math.Min(trimStart, sourceData.Duration));
                trimEnd = Math.Max(trimStart, Math.Min(trimEnd, sourceData.Duration));
                float min = (float)trimStart;
                float max = (float)trimEnd;
                EditorGUILayout.MinMaxSlider(ClipSwitchLocalization.C("Trim Range", "Диапазон обрезки", "Drag both handles to choose the retained range.", "Перетаскивайте оба маркера, чтобы выбрать сохраняемый диапазон."), ref min, ref max, 0f, (float)sourceData.Duration);
                trimStart = min;
                trimEnd = max;
                silenceThresholdDb = EditorGUILayout.Slider(ClipSwitchLocalization.C("Silence Threshold", "Порог тишины", "Samples below this level are treated as silence.", "Сэмплы ниже этого уровня считаются тишиной."), silenceThresholdDb, -80f, -12f);
                if (GUILayout.Button(ClipSwitchLocalization.C("Remove Leading / Trailing Silence", "Убрать тишину в начале / конце", "Detect and select only the audible range.", "Найти и выбрать только слышимый диапазон."))) DetectSilence();

                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField(L("Sound", "Звук"), EditorStyles.boldLabel);
                pitchSemitones = EditorGUILayout.Slider(ClipSwitchLocalization.C("Pitch / Speed", "Питч / скорость", "Shift pitch in semitones by smooth cubic resampling; duration changes too.", "Изменить высоту в полутонах плавным кубическим ресэмплингом; длительность тоже изменится."), pitchSemitones, -24f, 24f);
                gainDb = EditorGUILayout.Slider(ClipSwitchLocalization.C("Gain (dB)", "Усиление (дБ)", "Apply gain before normalization.", "Изменить громкость перед нормализацией."), gainDb, -48f, 24f);
                normalize = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Peak Normalize", "Пиковая нормализация", "Scale the result peak to the target level.", "Масштабировать пик результата к целевому уровню."), normalize);
                using (new EditorGUI.DisabledScope(!normalize))
                    normalizeTargetDb = EditorGUILayout.Slider(ClipSwitchLocalization.C("Normalize Target (dB)", "Цель нормализации (дБ)", "Maximum peak level after normalization.", "Максимальный пик после нормализации."), normalizeTargetDb, -12f, 0f);
                removeDcOffset = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Remove DC Offset", "Убрать DC-смещение", "Center each channel around zero.", "Центрировать каждый канал относительно нуля."), removeDcOffset);
                reverse = EditorGUILayout.Toggle(ClipSwitchLocalization.C("Reverse", "Реверс", "Reverse audio frames while preserving channel order.", "Развернуть аудиокадры, сохранив порядок каналов."), reverse);
                fadeIn = EditorGUILayout.Slider(ClipSwitchLocalization.C("Fade In (seconds)", "Плавное появление (секунды)", "Linear fade from silence at the beginning.", "Линейное нарастание от тишины в начале."), fadeIn, 0f, Mathf.Min(10f, (float)sourceData.Duration));
                fadeOut = EditorGUILayout.Slider(ClipSwitchLocalization.C("Fade Out (seconds)", "Плавное затухание (секунды)", "Linear fade to silence at the end.", "Линейное затухание к тишине в конце."), fadeOut, 0f, Mathf.Min(10f, (float)sourceData.Duration));

                if (EditorGUI.EndChangeCheck()) RequestProcessing(false);
                if (GUILayout.Button(ClipSwitchLocalization.C("Reset Processing", "Сбросить обработку", "Return all advanced controls to their original values.", "Вернуть все расширенные параметры к исходным значениям.")))
                {
                    ResetAdvancedSettings();
                    RequestProcessing(true);
                }
            }
        }

        private void DrawSaveActions()
        {
            EditorGUILayout.HelpBox(L("Saving as WAV allows lossless processing of every audio format Unity can decode. Overwrite preserves the GUID and all references; if needed the asset extension changes safely to .wav.",
                "Сохранение в WAV позволяет без потерь обрабатывать любой формат, который декодирует Unity. Перезапись сохраняет GUID и все ссылки; при необходимости расширение ассета безопасно меняется на .wav."), MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = processedData != null && !IsBusy;
                if (GUILayout.Button(ClipSwitchLocalization.C("Overwrite + Backup", "Перезаписать + копия", "Save the preview over the selected asset with a history snapshot.", "Сохранить preview поверх ассета со снимком в истории."), GUILayout.Height(34f))) ApplyOverwrite();
                if (GUILayout.Button(ClipSwitchLocalization.C("Save As New WAV", "Сохранить как новый WAV", "Create a separate processed asset.", "Создать отдельный обработанный ассет."), GUILayout.Height(34f))) SaveAsNew();
                GUI.enabled = true;
            }
        }

        private void ApplyImporter(AudioImporter importer)
        {
            try
            {
                importer.SaveAndReimport();
                importSettingsDirty = false;
                ReloadSourceAfterImport();
                importAppliedRevision++;
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("ClipSwitch", ex.Message, "OK"); }
        }

        private void ApplyOverwrite()
        {
            if (ClipSwitchState.instance.ConfirmDestructiveActions && !EditorUtility.DisplayDialog(L("Overwrite audio asset?", "Перезаписать аудиоассет?"), L("A full history snapshot will be created before the processed WAV is written.", "Перед записью обработанного WAV будет создан полный снимок истории."), L("Overwrite", "Перезаписать"), L("Cancel", "Отмена"))) return;
            string temporary = Path.Combine(Path.GetTempPath(), "ClipSwitchProcessed_" + System.Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                ClipSwitchWavCodec.Write(temporary, processedData);
                string error;
                if (!ClipSwitchOperations.ReplaceFromExternal(clip, temporary, "Process", out error)) throw new InvalidOperationException(error);
                SetClip(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(clipGuid)));
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("ClipSwitch", L("Processing failed:\n", "Ошибка обработки:\n") + ex.Message, "OK"); }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }

        public void SaveAsNew()
        {
            string original = AssetDatabase.GUIDToAssetPath(clipGuid);
            string path = EditorUtility.SaveFilePanelInProject(L("Save processed WAV", "Сохранить обработанный WAV"), clip.name + "_edited", "wav", L("Choose a location inside Assets.", "Выберите расположение внутри Assets."), Path.GetDirectoryName(original));
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                ClipSwitchWavCodec.Write(ClipSwitchPathUtility.AssetPathToAbsolute(path), processedData);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("ClipSwitch", ex.Message, "OK"); }
        }

        private void DetectSilence()
        {
            double start;
            double end;
            ClipSwitchWavProcessor.DetectNonSilentRange(sourceData, silenceThresholdDb, out start, out end);
            trimStart = start;
            trimEnd = end;
            RequestProcessing(false);
        }

        private void RequestProcessing(bool immediate)
        {
            requestedRevision++;
            processAfter = EditorApplication.timeSinceStartup + (immediate ? 0.0 : 0.08);
            processingError = string.Empty;
        }

        private void RebuildPreviewClip()
        {
            bool wasPlaying = ClipSwitchAudioPreview.CurrentClip == previewClip;
            float position = wasPlaying ? ClipSwitchAudioPreview.GetNormalizedPosition(previewClip) : 0f;
            DisposePreview();
            previewClip = ClipSwitchAudioDataUtility.CreatePreviewClip(processedData, "ClipSwitch Preview");
            if (wasPlaying) ClipSwitchAudioPreview.Play(previewClip, position);
        }

        private void DisposePreview()
        {
            if (previewClip == null) return;
            if (ClipSwitchAudioPreview.CurrentClip == previewClip) ClipSwitchAudioPreview.Stop();
            UnityEngine.Object.DestroyImmediate(previewClip);
            previewClip = null;
        }

        private void ResetAdvancedSettings()
        {
            trimStart = 0.0;
            trimEnd = sourceData == null ? 0.0 : sourceData.Duration;
            gainDb = 0f;
            normalize = false;
            normalizeTargetDb = -0.5f;
            reverse = false;
            removeDcOffset = false;
            fadeIn = 0f;
            fadeOut = 0f;
            pitchSemitones = 0f;
        }

        private ClipSwitchWavProcessSettings CreateSettings()
        {
            return new ClipSwitchWavProcessSettings { TrimStartSeconds = trimStart, TrimEndSeconds = trimEnd, GainDb = gainDb, Normalize = normalize, NormalizeTargetDb = normalizeTargetDb, Reverse = reverse, RemoveDcOffset = removeDcOffset, ConvertToMono = false, FadeInSeconds = fadeIn, FadeOutSeconds = fadeOut, PitchSemitones = pitchSemitones };
        }

        private void ReloadSourceAfterImport()
        {
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(clipGuid));
            ClipSwitchWaveformCache.Invalidate(AssetDatabase.GetAssetPath(clip));
            sourceData = ClipSwitchAudioDataUtility.ReadClip(clip);
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(clipGuid));
            trimStart = Math.Max(0.0, Math.Min(trimStart, sourceData.Duration));
            trimEnd = Math.Max(trimStart, Math.Min(trimEnd, sourceData.Duration));
            RequestProcessing(true);
        }

        private static void DrawSizeInfo(AudioClip target)
        {
            long original = ClipSwitchAudioImporterUtility.GetSourceSize(target);
            long imported = ClipSwitchAudioImporterUtility.GetImportedSize(target);
            string ratio = original > 0 && imported >= 0 ? ((double)imported / original).ToString("0.00") + "x" : "—";
            EditorGUILayout.LabelField(ClipSwitchLocalization.C("Original Size", "Исходный размер", "Size of the source file on disk.", "Размер исходного файла на диске."), new GUIContent(ClipSwitchAudioImporterUtility.FormatBytes(original)));
            EditorGUILayout.LabelField(ClipSwitchLocalization.C("Imported Size", "Импортированный размер", "Estimated runtime data size after import.", "Оценка размера данных после импорта."), new GUIContent(ClipSwitchAudioImporterUtility.FormatBytes(imported)));
            EditorGUILayout.LabelField(ClipSwitchLocalization.C("Ratio", "Соотношение", "Imported size divided by original size.", "Импортированный размер, делённый на исходный."), new GUIContent(ratio));
        }

        private static AudioClipLoadType DrawLoadType(AudioClipLoadType value)
        {
            if (!ClipSwitchLocalization.IsRussian)
                return (AudioClipLoadType)EditorGUILayout.EnumPopup(ClipSwitchLocalization.C("Load Type", "Тип загрузки", "How imported audio is kept and loaded at runtime.", "Как импортированное аудио хранится и загружается во время работы."), value);
            string[] names = { "Распаковать при загрузке", "Сжато в памяти", "Потоково" };
            int[] values = { (int)AudioClipLoadType.DecompressOnLoad, (int)AudioClipLoadType.CompressedInMemory, (int)AudioClipLoadType.Streaming };
            Rect rect = EditorGUILayout.GetControlRect();
            Rect fieldRect = EditorGUI.PrefixLabel(rect, ClipSwitchLocalization.C("Load Type", "Тип загрузки", "How imported audio is kept and loaded at runtime.", "Как импортированное аудио хранится и загружается во время работы."));
            return (AudioClipLoadType)EditorGUI.IntPopup(fieldRect, (int)value, names, values);
        }

        private static AudioSampleRateSetting DrawSampleRate(AudioSampleRateSetting value)
        {
            if (!ClipSwitchLocalization.IsRussian)
                return (AudioSampleRateSetting)EditorGUILayout.EnumPopup(ClipSwitchLocalization.C("Sample Rate Setting", "Частота дискретизации", "Preserve, optimize, or explicitly override the sample rate.", "Сохранить, оптимизировать или явно задать частоту дискретизации."), value);
            string[] names = { "Сохранить", "Оптимизировать", "Переопределить" };
            int[] values = { (int)AudioSampleRateSetting.PreserveSampleRate, (int)AudioSampleRateSetting.OptimizeSampleRate, (int)AudioSampleRateSetting.OverrideSampleRate };
            Rect rect = EditorGUILayout.GetControlRect();
            Rect fieldRect = EditorGUI.PrefixLabel(rect, ClipSwitchLocalization.C("Sample Rate Setting", "Частота дискретизации", "Preserve, optimize, or explicitly override the sample rate.", "Сохранить, оптимизировать или явно задать частоту дискретизации."));
            return (AudioSampleRateSetting)EditorGUI.IntPopup(fieldRect, (int)value, names, values);
        }

        private static bool GetNormalize(AudioImporter importer)
        {
            SerializedProperty property = FindProperty(importer, "m_Normalize", "normalize");
            return property == null || property.boolValue;
        }

        private static void SetNormalize(AudioImporter importer, bool value)
        {
            SerializedObject serialized = new SerializedObject(importer);
            SerializedProperty property = serialized.FindProperty("m_Normalize") ?? serialized.FindProperty("normalize");
            if (property == null) return;
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty FindProperty(AudioImporter importer, params string[] names)
        {
            SerializedObject serialized = new SerializedObject(importer);
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty property = serialized.FindProperty(names[i]);
                if (property != null) return property;
            }
            return null;
        }

        private static GUIContent Icon(string iconName, string english, string russian, string englishTooltip, string russianTooltip)
        {
            GUIContent icon = EditorGUIUtility.IconContent(iconName);
            icon.tooltip = L(englishTooltip, russianTooltip);
            if (icon.image == null) icon.text = L(english, russian);
            return icon;
        }

        private static string L(string english, string russian) { return ClipSwitchLocalization.T(english, russian); }
    }
}
