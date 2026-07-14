using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchOperations
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".ogg", ".flac", ".aif", ".aiff", ".xm", ".mod", ".it", ".s3m"
        };

        public static bool IsSupportedAudioPath(string path)
        {
            return !string.IsNullOrEmpty(path) && SupportedExtensions.Contains(Path.GetExtension(path));
        }

        public static string[] SupportedFilePanelFilters
        {
            get
            {
                return new[]
                {
                    ClipSwitchLocalization.T("Unity audio files", "Аудиофайлы Unity"), "wav,mp3,ogg,flac,aif,aiff,xm,mod,it,s3m",
                    ClipSwitchLocalization.T("All files", "Все файлы"), "*"
                };
            }
        }

        public static bool ReplaceFromExternal(AudioClip target, string externalPath, out string error)
        {
            return ReplaceFromExternal(target, externalPath, "Replace", out error);
        }

        public static bool ReplaceFromExternal(AudioClip target, string externalPath, string operation, out string error)
        {
            error = string.Empty;
            if (target == null)
            {
                error = L("Target clip is missing.", "Целевой клип отсутствует.");
                return false;
            }
            if (string.IsNullOrEmpty(externalPath) || !File.Exists(externalPath))
            {
                error = L("Selected file does not exist.", "Выбранный файл не существует.");
                return false;
            }
            if (!IsSupportedAudioPath(externalPath))
            {
                error = L("Unity 2022.3 cannot import this audio format. Supported: WAV, MP3, OGG, FLAC, AIFF and tracker modules (XM, MOD, IT, S3M).",
                    "Unity 2022.3 не импортирует этот аудиоформат. Поддерживаются WAV, MP3, OGG, FLAC, AIFF и трекерные модули XM, MOD, IT, S3M.");
                return false;
            }

            string targetPath = AssetDatabase.GetAssetPath(target);
            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            if (!ValidateEditableAudioAsset(targetPath, out error))
                return false;

            string targetAbsolute = ClipSwitchPathUtility.AssetPathToAbsolute(targetPath);
            if (PathsEqual(targetAbsolute, externalPath))
            {
                error = L("The selected source is the same file as the target.", "Источник совпадает с целевым файлом.");
                return false;
            }

            string backupPath;
            if (!CreateBackup(targetPath, out backupPath, out error))
                return false;

            string resultPath = targetPath;
            try
            {
                string sourceExtension = Path.GetExtension(externalPath).ToLowerInvariant();
                string targetExtension = Path.GetExtension(targetPath).ToLowerInvariant();
                if (!string.Equals(sourceExtension, targetExtension, StringComparison.OrdinalIgnoreCase))
                {
                    resultPath = GetAvailablePathWithExtension(targetPath, sourceExtension);
                    string moveError = AssetDatabase.MoveAsset(targetPath, resultPath);
                    if (!string.IsNullOrEmpty(moveError))
                        throw new IOException(moveError);
                }

                CopyFileSafely(externalPath, ClipSwitchPathUtility.AssetPathToAbsolute(resultPath));
                AssetDatabase.ImportAsset(resultPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(resultPath) == null)
                    throw new InvalidDataException(L("Unity could not import the replacement as an AudioClip.", "Unity не смог импортировать замену как AudioClip."));

                ClipSwitchState.instance.AddHistory(targetGuid, targetPath, backupPath, operation, externalPath, resultPath);
                ClipSwitchWaveformCache.Invalidate(targetPath);
                if (resultPath != targetPath) ClipSwitchWaveformCache.Invalidate(resultPath);
                return true;
            }
            catch (Exception ex)
            {
                error = L("Replacement failed: ", "Ошибка замены: ") + ex.Message;
                Rollback(targetPath, resultPath, backupPath);
                return false;
            }
        }

        public static bool RestoreLatest(AudioClip target, out string error)
        {
            if (target == null)
            {
                error = L("Target clip is missing.", "Целевой клип отсутствует.");
                return false;
            }
            string path = AssetDatabase.GetAssetPath(target);
            ClipSwitchHistoryEntry latest = ClipSwitchState.instance.GetLatest(AssetDatabase.AssetPathToGUID(path));
            return RestoreVersion(target, latest, out error);
        }

        public static bool RestoreVersion(AudioClip target, ClipSwitchHistoryEntry version, out string error)
        {
            error = string.Empty;
            if (target == null || version == null)
            {
                error = L("The requested history version is unavailable.", "Запрошенная версия истории недоступна.");
                return false;
            }
            string currentPath = AssetDatabase.GetAssetPath(target);
            string guid = AssetDatabase.AssetPathToGUID(currentPath);
            string backupAbsolute = ClipSwitchPathUtility.AssetPathToAbsolute(version.BackupPath);
            if (!File.Exists(backupAbsolute))
            {
                error = L("History snapshot is missing:\n", "Снимок истории отсутствует:\n") + version.BackupPath;
                return false;
            }
            if (!ValidateEditableAudioAsset(currentPath, out error))
                return false;

            string currentBackup;
            if (!CreateBackup(currentPath, out currentBackup, out error))
                return false;

            string desiredPath = ClipSwitchPathUtility.NormalizeAssetPath(version.TargetPath);
            if (string.IsNullOrEmpty(desiredPath))
                desiredPath = Path.ChangeExtension(currentPath, Path.GetExtension(version.BackupPath)).Replace('\\', '/');
            string resultPath = currentPath;
            try
            {
                if (!string.Equals(currentPath, desiredPath, StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(desiredPath);
                    if (occupied != null)
                        throw new IOException(L("The original asset path is occupied: ", "Исходный путь ассета занят: ") + desiredPath);
                    string moveError = AssetDatabase.MoveAsset(currentPath, desiredPath);
                    if (!string.IsNullOrEmpty(moveError)) throw new IOException(moveError);
                    resultPath = desiredPath;
                }

                CopyFileSafely(backupAbsolute, ClipSwitchPathUtility.AssetPathToAbsolute(resultPath));
                AssetDatabase.ImportAsset(resultPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ClipSwitchState.instance.AddHistory(guid, currentPath, currentBackup, "Restore", version.UtcTime.ToLocalTime().ToString("G"), resultPath);
                ClipSwitchWaveformCache.Invalidate(currentPath);
                ClipSwitchWaveformCache.Invalidate(resultPath);
                return true;
            }
            catch (Exception ex)
            {
                error = L("Restore failed: ", "Ошибка восстановления: ") + ex.Message;
                Rollback(currentPath, resultPath, currentBackup);
                return false;
            }
        }

        public static bool Swap(AudioClip first, AudioClip second, out string error)
        {
            error = string.Empty;
            if (first == null || second == null || first == second)
            {
                error = L("Choose two different clips.", "Выберите два разных клипа.");
                return false;
            }

            string firstPath = AssetDatabase.GetAssetPath(first);
            string secondPath = AssetDatabase.GetAssetPath(second);
            if (!ValidateEditableAudioAsset(firstPath, out error) || !ValidateEditableAudioAsset(secondPath, out error))
                return false;

            string firstGuid = AssetDatabase.AssetPathToGUID(firstPath);
            string secondGuid = AssetDatabase.AssetPathToGUID(secondPath);
            string firstTemp = Path.Combine(Path.GetTempPath(), "ClipSwitch_" + Guid.NewGuid().ToString("N") + Path.GetExtension(firstPath));
            string secondTemp = Path.Combine(Path.GetTempPath(), "ClipSwitch_" + Guid.NewGuid().ToString("N") + Path.GetExtension(secondPath));
            try
            {
                File.Copy(ClipSwitchPathUtility.AssetPathToAbsolute(firstPath), firstTemp, true);
                File.Copy(ClipSwitchPathUtility.AssetPathToAbsolute(secondPath), secondTemp, true);

                if (!ReplaceFromExternal(first, secondTemp, "Swap", out error)) return false;
                second = AssetDatabase.LoadAssetAtPath<AudioClip>(secondPath);
                if (second == null) second = LoadByGuid(secondGuid);
                if (!ReplaceFromExternal(second, firstTemp, "Swap", out error))
                {
                    AudioClip changedFirst = LoadByGuid(firstGuid);
                    ClipSwitchHistoryEntry firstEntry = ClipSwitchState.instance.GetLatest(firstGuid);
                    string rollbackError;
                    RestoreVersion(changedFirst, firstEntry, out rollbackError);
                    return false;
                }
                return true;
            }
            finally
            {
                TryDelete(firstTemp);
                TryDelete(secondTemp);
            }
        }

        public static bool BatchReplace(IList<AudioClip> targets, IList<string> sources, out string error)
        {
            error = string.Empty;
            if (targets == null || sources == null || targets.Count == 0 || targets.Count != sources.Count)
            {
                error = L("Targets and sources must have the same non-zero count.", "Количество целей и источников должно совпадать и быть больше нуля.");
                return false;
            }
            List<string> temporarySources = new List<string>();
            List<string> targetGuids = new List<string>();
            List<ClipSwitchHistoryEntry> completedEntries = new List<ClipSwitchHistoryEntry>();
            try
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (targets[i] == null || string.IsNullOrEmpty(sources[i]) || !File.Exists(sources[i]) || !IsSupportedAudioPath(sources[i]))
                    {
                        error = string.Format(L("Invalid target or source at item {0}.", "Некорректная цель или источник в элементе {0}."), i + 1);
                        return false;
                    }
                    string temporary = Path.Combine(Path.GetTempPath(), "ClipSwitchBatch_" + Guid.NewGuid().ToString("N") + Path.GetExtension(sources[i]));
                    File.Copy(sources[i], temporary, true);
                    temporarySources.Add(temporary);
                    targetGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(targets[i])));
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    AudioClip currentTarget = LoadByGuid(targetGuids[i]);
                    if (!ReplaceFromExternal(currentTarget, temporarySources[i], "Batch replace", out error))
                    {
                        bool rollbackSucceeded = true;
                        for (int completed = completedEntries.Count - 1; completed >= 0; completed--)
                        {
                            string rollbackError;
                            if (!RestoreVersion(LoadByGuid(targetGuids[completed]), completedEntries[completed], out rollbackError))
                                rollbackSucceeded = false;
                        }
                        error = string.Format(L("Item {0} of {1} failed. Completed items were {2}.\n{3}",
                            "Ошибка в элементе {0} из {1}. Выполненные элементы {2}.\n{3}"), i + 1, targets.Count,
                            rollbackSucceeded ? L("rolled back", "откачены") : L("only partially rolled back; use History", "откачены лишь частично; используйте Историю"), error);
                        return false;
                    }

                    ClipSwitchHistoryEntry entry = ClipSwitchState.instance.GetLatest(targetGuids[i]);
                    if (entry != null)
                    {
                        entry.SourceDescription = sources[i];
                        ClipSwitchState.instance.SaveNow();
                        completedEntries.Add(entry);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = L("Could not prepare batch replacement: ", "Не удалось подготовить пакетную замену: ") + ex.Message;
                return false;
            }
            finally
            {
                for (int i = 0; i < temporarySources.Count; i++) TryDelete(temporarySources[i]);
            }
        }

        public static AudioClip GetLatestBackupClip(AudioClip target)
        {
            ClipSwitchHistoryEntry entry = GetLatestEntry(target);
            return entry == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(entry.BackupPath);
        }

        public static ClipSwitchHistoryEntry GetLatestEntry(AudioClip target)
        {
            if (target == null) return null;
            string path = AssetDatabase.GetAssetPath(target);
            return ClipSwitchState.instance.GetLatest(AssetDatabase.AssetPathToGUID(path));
        }

        internal static bool CreateBackup(string targetPath, out string backupPath, out string error)
        {
            backupPath = string.Empty;
            error = string.Empty;
            try
            {
                string absolute = ClipSwitchPathUtility.AssetPathToAbsolute(targetPath);
                if (!File.Exists(absolute)) throw new FileNotFoundException(targetPath);
                backupPath = ClipSwitchPathUtility.CreateTimestampedBackupPath(targetPath);
                string backupAbsolute = ClipSwitchPathUtility.AssetPathToAbsolute(backupPath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupAbsolute) ?? ClipSwitchPathUtility.ProjectRoot);
                File.Copy(absolute, backupAbsolute, false);
                AssetDatabase.ImportAsset(backupPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception ex)
            {
                error = L("Could not create backup: ", "Не удалось создать резервную копию: ") + ex.Message;
                return false;
            }
        }

        private static AudioClip LoadByGuid(string guid)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static string GetAvailablePathWithExtension(string path, string extension)
        {
            string desired = Path.ChangeExtension(path, extension).Replace('\\', '/');
            if (string.Equals(desired, path, StringComparison.OrdinalIgnoreCase)) return path;
            return AssetDatabase.LoadMainAssetAtPath(desired) == null ? desired : AssetDatabase.GenerateUniqueAssetPath(desired);
        }

        private static bool ValidateEditableAudioAsset(string path, out string error)
        {
            error = string.Empty;
            if (!ClipSwitchPathUtility.IsProjectAssetPath(path))
            {
                error = L("ClipSwitch can only modify audio under Assets.", "ClipSwitch изменяет аудио только внутри Assets.");
                return false;
            }
            string absolute = ClipSwitchPathUtility.AssetPathToAbsolute(path);
            if (!File.Exists(absolute))
            {
                error = L("Audio file is missing on disk:\n", "Аудиофайл отсутствует на диске:\n") + path;
                return false;
            }
            try
            {
                if (!AssetDatabase.MakeEditable(path))
                {
                    error = L("Version control did not allow editing:\n", "Система контроля версий запретила редактирование:\n") + path;
                    return false;
                }
            }
            catch
            {
                if (new FileInfo(absolute).IsReadOnly)
                {
                    error = L("The audio file is read-only:\n", "Аудиофайл доступен только для чтения:\n") + path;
                    return false;
                }
            }
            return true;
        }

        private static void CopyFileSafely(string source, string destination)
        {
            string temporary = destination + ".clipswitch.tmp";
            try
            {
                File.Copy(source, temporary, true);
                File.Copy(temporary, destination, true);
            }
            finally { TryDelete(temporary); }
        }

        private static void Rollback(string originalPath, string currentPath, string backupPath)
        {
            try
            {
                if (!string.Equals(originalPath, currentPath, StringComparison.OrdinalIgnoreCase) &&
                    AssetDatabase.LoadMainAssetAtPath(currentPath) != null && AssetDatabase.LoadMainAssetAtPath(originalPath) == null)
                    AssetDatabase.MoveAsset(currentPath, originalPath);
                string backup = ClipSwitchPathUtility.AssetPathToAbsolute(backupPath);
                if (File.Exists(backup))
                {
                    File.Copy(backup, ClipSwitchPathUtility.AssetPathToAbsolute(originalPath), true);
                    AssetDatabase.ImportAsset(originalPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }
            }
            catch { }
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static string L(string english, string russian)
        {
            return ClipSwitchLocalization.T(english, russian);
        }
    }
}
