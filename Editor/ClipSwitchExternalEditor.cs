using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchExternalEditor
    {
        public static string ApplicationDisplayName
        {
            get
            {
                string application = ClipSwitchState.instance.ExternalEditorPath;
                if (string.IsNullOrWhiteSpace(application))
                    return ClipSwitchLocalization.T("default application", "приложении по умолчанию");
                application = application.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string name = Path.GetFileNameWithoutExtension(application);
                return string.IsNullOrEmpty(name) ? application : name;
            }
        }

        public static string OpenLabel
        {
            get { return ClipSwitchLocalization.T("Open in ", "Открыть в ") + ApplicationDisplayName; }
        }

        public static bool Open(AudioClip clip, out string error)
        {
            error = string.Empty;
            if (clip == null)
            {
                error = ClipSwitchLocalization.T("No audio clip is selected.", "Аудиоклип не выбран.");
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
            string absolutePath = ClipSwitchPathUtility.AssetPathToAbsolute(assetPath);
            if (!File.Exists(absolutePath))
            {
                error = ClipSwitchLocalization.T("The audio file does not exist on disk.", "Аудиофайл отсутствует на диске.");
                return false;
            }

            string application = ClipSwitchState.instance.ExternalEditorPath;
            try
            {
                if (string.IsNullOrWhiteSpace(application))
                {
                    // AssetDatabase.OpenAsset may only focus Unity's AudioClip inspector.
                    // Shell execution is what actually launches the OS file association.
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = absolutePath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(absolutePath) ?? ClipSwitchPathUtility.ProjectRoot
                    });
                    return true;
                }

                application = Environment.ExpandEnvironmentVariables(application.Trim().Trim('"'));
                if (!File.Exists(application) && !Directory.Exists(application))
                {
                    error = ClipSwitchLocalization.T("The configured external application was not found:\n", "Выбранное внешнее приложение не найдено:\n") + application;
                    return false;
                }

                bool macBundle = Application.platform == RuntimePlatform.OSXEditor && Directory.Exists(application);
                Process.Start(new ProcessStartInfo
                {
                    FileName = macBundle ? "/usr/bin/open" : application,
                    Arguments = macBundle
                        ? "-a \"" + application.Replace("\"", "\\\"") + "\" \"" + absolutePath.Replace("\"", "\\\"") + "\""
                        : "\"" + absolutePath.Replace("\"", "\\\"") + "\"",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(absolutePath) ?? ClipSwitchPathUtility.ProjectRoot
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ClipSwitchLocalization.T("Could not open the external application: ", "Не удалось открыть внешнее приложение: ") + ex.Message;
                return false;
            }
        }
    }
}
