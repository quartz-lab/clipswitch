using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchPathUtility
    {
        public static string ProjectRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
        }

        public static string AssetPathToAbsolute(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;

            return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
        }

        public static bool IsProjectAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            path = NormalizeAssetPath(path).TrimEnd('/');
            if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            if (Path.IsPathRooted(path))
                return false;
            try
            {
                string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string absolute = Path.GetFullPath(Path.Combine(ProjectRoot, path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                StringComparison comparison = Application.platform == RuntimePlatform.LinuxEditor
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
                return string.Equals(absolute, assetsRoot, comparison) || absolute.StartsWith(assetsRoot + Path.DirectorySeparatorChar, comparison);
            }
            catch { return false; }
        }

        public static void EnsureAssetFolder(string assetFolder)
        {
            assetFolder = NormalizeAssetPath(assetFolder).TrimEnd('/');
            if (!IsProjectAssetPath(assetFolder))
                throw new ArgumentException("Folder must be inside Assets.", "assetFolder");

            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        public static string CreateTimestampedBackupPath(string targetAssetPath)
        {
            string backupRoot = ClipSwitchState.instance.BackupRoot;
            if (!IsProjectAssetPath(backupRoot))
                backupRoot = "Assets/ClipSwitch Backups";

            string datedFolder = NormalizeAssetPath(
                backupRoot.TrimEnd('/') + "/" + DateTime.Now.ToString("yyyy-MM-dd"));
            EnsureAssetFolder(datedFolder);

            string fileName = Path.GetFileNameWithoutExtension(targetAssetPath);
            string extension = Path.GetExtension(targetAssetPath);
            string proposed = string.Format(
                "{0}/{1}_BACKUP_{2}{3}",
                datedFolder,
                fileName,
                DateTime.Now.ToString("HHmmss_fff"),
                extension);

            return AssetDatabase.GenerateUniqueAssetPath(proposed);
        }
    }
}
