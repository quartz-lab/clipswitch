using System;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchDocumentation
    {
        public static bool Open(out string error)
        {
            error = string.Empty;
            try
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(ClipSwitchWindow).Assembly);
                if (package == null || string.IsNullOrEmpty(package.resolvedPath))
                    throw new InvalidOperationException(ClipSwitchLocalization.T("Could not locate the installed ClipSwitch package.", "Не удалось найти установленный пакет ClipSwitch."));
                string path = Path.Combine(package.resolvedPath, "Documentation~", "index.html");
                if (!File.Exists(path))
                    throw new FileNotFoundException(ClipSwitchLocalization.T("The offline documentation file is missing.", "Файл офлайн-документации отсутствует."), path);
                Application.OpenURL(new Uri(path).AbsoluteUri);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
