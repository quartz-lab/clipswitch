using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchWaveformGUI
    {
        public static void Draw(Rect rect, AudioClip clip, Texture2D waveform, string displayName, bool allowScrub)
        {
            Color background = EditorGUIUtility.isProSkin ? new Color(0.065f, 0.075f, 0.09f) : new Color(0.79f, 0.82f, 0.86f);
            EditorGUI.DrawRect(rect, background);
            if (waveform != null) GUI.DrawTexture(rect, waveform, ScaleMode.StretchToFill, true);
            if (clip == null)
            {
                GUIStyle empty = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(rect, new GUIContent(displayName, displayName), empty);
                return;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            string format = string.IsNullOrEmpty(path) ? "PCM" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            string channels = clip.channels == 1 ? ClipSwitchLocalization.T("Mono", "Моно") :
                              clip.channels == 2 ? ClipSwitchLocalization.T("Stereo", "Стерео") :
                              clip.channels + ClipSwitchLocalization.T(" ch", " кан.");
            string info = string.Format("{0}   {1:0.###}s   {2:0.#} kHz   {3}   {4}", displayName, clip.length, clip.frequency / 1000f, channels, format);
            GUIStyle overlay = new GUIStyle(EditorStyles.whiteMiniLabel) { clipping = TextClipping.Clip };
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 18f), new Color(0f, 0f, 0f, 0.44f));
            GUI.Label(new Rect(rect.x + 5f, rect.y + 1f, rect.width - 10f, 16f), new GUIContent(info, info), overlay);

            if (allowScrub) HandleScrub(rect, clip);
            if (ClipSwitchAudioPreview.CurrentClip == clip)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, ClipSwitchAudioPreview.GetNormalizedPosition(clip));
                EditorGUI.DrawRect(new Rect(Mathf.Clamp(x - 1f, rect.xMin, rect.xMax - 2f), rect.y, 2f, rect.height), new Color(1f, 0.42f, 0.08f, 1f));
            }
        }

        private static void HandleScrub(Rect rect, AudioClip clip)
        {
            Event evt = Event.current;
            int id = GUIUtility.GetControlID("ClipSwitchWaveform".GetHashCode(), FocusType.Passive, rect);
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = id;
                ClipSwitchAudioPreview.BeginScrub(clip, Position(rect, evt.mousePosition.x));
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                ClipSwitchAudioPreview.UpdateScrub(clip, Position(rect, evt.mousePosition.x));
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                ClipSwitchAudioPreview.EndScrub(clip, Position(rect, evt.mousePosition.x));
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        private static float Position(Rect rect, float x)
        {
            return Mathf.InverseLerp(rect.xMin, rect.xMax, Mathf.Clamp(x, rect.xMin, rect.xMax));
        }
    }
}
