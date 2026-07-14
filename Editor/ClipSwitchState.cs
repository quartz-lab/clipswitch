using System;
using System.Collections.Generic;
using UnityEditor;

namespace QuartzLab.ClipSwitch
{
    internal enum ClipSwitchLanguage
    {
        Auto,
        English,
        Russian
    }

    internal enum ClipSwitchListViewMode
    {
        Waveforms,
        Compact
    }

    [Serializable]
    internal sealed class ClipSwitchHistoryEntry
    {
        public string TargetGuid;
        public string TargetPath;
        public string BackupPath;
        public string Operation;
        public string SourceDescription;
        public long UtcTicks;
        public string ResultPath;

        public DateTime UtcTime
        {
            get { return new DateTime(UtcTicks, DateTimeKind.Utc); }
        }
    }

    [FilePath("ProjectSettings/QuartzLab.ClipSwitch.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ClipSwitchState : ScriptableSingleton<ClipSwitchState>
    {
        public string BackupRoot = "Assets/ClipSwitch Backups";
        public ClipSwitchLanguage Language = ClipSwitchLanguage.Auto;
        public string ExternalEditorPath = string.Empty;
        public bool ConfirmDestructiveActions = true;
        public bool ShowPreviousClipColumn;
        public ClipSwitchListViewMode LibraryViewMode = ClipSwitchListViewMode.Waveforms;
        public ClipSwitchListViewMode PickerViewMode = ClipSwitchListViewMode.Compact;
        public List<ClipSwitchHistoryEntry> History = new List<ClipSwitchHistoryEntry>();

        public void SaveNow()
        {
            Save(true);
        }

        public ClipSwitchHistoryEntry GetLatest(string targetGuid)
        {
            for (int i = History.Count - 1; i >= 0; i--)
            {
                ClipSwitchHistoryEntry entry = History[i];
                if (entry != null && entry.TargetGuid == targetGuid && !string.IsNullOrEmpty(entry.BackupPath))
                    return entry;
            }

            return null;
        }

        public List<ClipSwitchHistoryEntry> GetHistory(string targetGuid)
        {
            List<ClipSwitchHistoryEntry> result = new List<ClipSwitchHistoryEntry>();
            for (int i = History.Count - 1; i >= 0; i--)
            {
                ClipSwitchHistoryEntry entry = History[i];
                if (entry != null && entry.TargetGuid == targetGuid)
                    result.Add(entry);
            }
            return result;
        }

        public void AddHistory(
            string targetGuid,
            string targetPath,
            string backupPath,
            string operation,
            string sourceDescription,
            string resultPath = null)
        {
            History.Add(new ClipSwitchHistoryEntry
            {
                TargetGuid = targetGuid,
                TargetPath = targetPath,
                BackupPath = backupPath,
                Operation = operation,
                SourceDescription = sourceDescription,
                ResultPath = string.IsNullOrEmpty(resultPath) ? targetPath : resultPath,
                UtcTicks = DateTime.UtcNow.Ticks
            });

            SaveNow();
        }
    }
}
