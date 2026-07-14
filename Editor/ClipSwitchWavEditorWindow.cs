using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    // Compatibility bridge for projects that called the old internal WAV editor through reflection.
    internal static class ClipSwitchWavEditorWindow
    {
        public static void Open(AudioClip target)
        {
            ClipSwitchWindow.OpenAndEdit(target);
        }
    }
}
