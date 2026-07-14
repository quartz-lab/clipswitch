using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchAudioImporterUtility
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type AudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

        public static long GetSourceSize(AudioClip clip)
        {
            if (clip == null) return -1;
            try
            {
                string absolute = ClipSwitchPathUtility.AssetPathToAbsolute(AssetDatabase.GetAssetPath(clip));
                return File.Exists(absolute) ? new FileInfo(absolute).Length : -1;
            }
            catch { return -1; }
        }

        public static long GetImportedSize(AudioClip clip)
        {
            long value = InvokeSize(new[] { "GetImportedSize", "GetImportedFileSize", "GetSoundSize" }, clip);
            if (value > 0 || clip == null) return value;
            try { return checked((long)clip.samples * clip.channels * sizeof(float)); }
            catch { return -1; }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "—";
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1) { value /= 1024.0; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.##") + " " + units[unit];
        }

        private static long InvokeSize(string[] names, AudioClip clip)
        {
            if (AudioUtil == null || clip == null) return -1;
            AudioImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            MethodInfo[] methods = AudioUtil.GetMethods(Flags);
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name != names[n] || method.GetParameters().Length != 1) continue;
                    Type parameter = method.GetParameters()[0].ParameterType;
                    object argument = parameter.IsInstanceOfType(clip) ? (object)clip :
                                      parameter.IsInstanceOfType(importer) ? importer : null;
                    if (argument == null) continue;
                    try
                    {
                        object value = method.Invoke(null, new[] { argument });
                        if (value is int) return (int)value;
                        if (value is long) return (long)value;
                        if (value is uint) return (uint)value;
                        if (value is ulong) return (long)Math.Min(long.MaxValue, (ulong)value);
                    }
                    catch { }
                }
            }
            return -1;
        }
    }
}
