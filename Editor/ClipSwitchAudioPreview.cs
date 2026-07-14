using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchAudioPreview
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Type AudioUtilType;
        private static readonly MethodInfo PlayMethod;
        private static readonly MethodInfo StopMethod;
        private static readonly MethodInfo PauseMethod;
        private static readonly MethodInfo ResumeMethod;
        private static readonly MethodInfo IsPlayingMethod;
        private static readonly MethodInfo GetSamplePositionMethod;
        private static readonly MethodInfo SetSamplePositionMethod;
        private static readonly MethodInfo GetMinMaxDataMethod;

        private static AudioClip currentClip;
        private static bool paused;
        private static double playbackAnchorTime;
        private static int playbackAnchorSample;
        private static int pausedSample;
        private static bool scrubbing;
        private static float scrubPosition;
        private static GameObject generatedPlayerObject;
        private static AudioSource generatedSource;
        private static AudioListener generatedListener;
        private static bool useGeneratedPlayer;

        static ClipSwitchAudioPreview()
        {
            AudioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (AudioUtilType == null)
                return;

            PlayMethod = FindMethod(new[] { "PlayPreviewClip", "PlayClip" }, 3);
            StopMethod = FindMethod(new[] { "StopAllPreviewClips", "StopAllClips" }, 0);
            PauseMethod = FindMethod(new[] { "PausePreviewClip", "PauseClip" }, 0);
            ResumeMethod = FindMethod(new[] { "ResumePreviewClip", "ResumeClip" }, 0);
            IsPlayingMethod = FindMethod(new[] { "IsPreviewClipPlaying", "IsClipPlaying" }, 0);
            GetSamplePositionMethod = FindMethod(new[] { "GetPreviewClipSamplePosition", "GetClipSamplePosition" }, 0);
            SetSamplePositionMethod = FindMethod(new[] { "SetPreviewClipSamplePosition", "SetClipSamplePosition" }, 2);
            GetMinMaxDataMethod = FindMethod(new[] { "GetMinMaxData" }, 1);
            AssemblyReloadEvents.beforeAssemblyReload += DestroyGeneratedPlayer;
            EditorApplication.quitting += DestroyGeneratedPlayer;
        }

        public static AudioClip CurrentClip
        {
            get { return currentClip; }
        }

        public static bool IsAvailable
        {
            get { return AudioUtilType != null && PlayMethod != null && StopMethod != null; }
        }

        public static bool IsPlaying
        {
            get
            {
                if (useGeneratedPlayer)
                    return generatedSource != null && generatedSource.isPlaying;
                if (IsPlayingMethod == null)
                    return false;

                try
                {
                    object value = IsPlayingMethod.Invoke(null, null);
                    return value is bool && (bool)value;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsPaused
        {
            get { return paused; }
        }

        public static bool IsScrubbing
        {
            get { return scrubbing; }
        }

        public static bool NeedsRepaint
        {
            get
            {
                if (currentClip == null)
                    return false;
                if (scrubbing)
                    return true;
                double predictedEnd = playbackAnchorTime +
                                      (double)Mathf.Max(0, currentClip.samples - playbackAnchorSample) /
                                      Mathf.Max(1, currentClip.frequency) + 0.12;
                return paused || IsPlaying || EditorApplication.timeSinceStartup <= predictedEnd;
            }
        }

        public static void Play(AudioClip clip, float normalizedPosition)
        {
            if (clip == null || (PlayMethod == null && EditorUtility.IsPersistent(clip)))
                return;

            int sample = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp01(normalizedPosition) * Mathf.Max(0, clip.samples - 1)),
                0,
                Mathf.Max(0, clip.samples - 1));

            try
            {
                Stop();
                clip.LoadAudioData();
                if (!EditorUtility.IsPersistent(clip))
                {
                    EnsureGeneratedPlayer();
                    generatedSource.clip = clip;
                    generatedSource.timeSamples = sample;
                    generatedSource.Play();
                    useGeneratedPlayer = true;
                }
                else
                {
                    PlayMethod.Invoke(null, new object[] { clip, sample, false });
                    useGeneratedPlayer = false;
                }
                currentClip = clip;
                paused = false;
                playbackAnchorSample = sample;
                pausedSample = sample;
                playbackAnchorTime = EditorApplication.timeSinceStartup;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ClipSwitch audio preview failed: " + ex.Message);
            }
        }

        public static void Seek(AudioClip clip, float normalizedPosition)
        {
            if (clip == null)
                return;

            int sample = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp01(normalizedPosition) * Mathf.Max(0, clip.samples - 1)),
                0,
                Mathf.Max(0, clip.samples - 1));

            try
            {
                if (clip == currentClip && useGeneratedPlayer && generatedSource != null)
                {
                    generatedSource.timeSamples = sample;
                    playbackAnchorSample = sample;
                    pausedSample = sample;
                    playbackAnchorTime = EditorApplication.timeSinceStartup;
                }
                else if (clip == currentClip && (IsPlaying || paused) && SetSamplePositionMethod != null)
                {
                    SetSamplePositionMethod.Invoke(null, new object[] { clip, sample });
                    playbackAnchorSample = sample;
                    pausedSample = sample;
                    playbackAnchorTime = EditorApplication.timeSinceStartup;
                }
                else
                    Play(clip, normalizedPosition);
            }
            catch
            {
                Play(clip, normalizedPosition);
            }
        }

        public static void BeginScrub(AudioClip clip, float normalizedPosition)
        {
            Stop();
            if (clip == null)
                return;
            currentClip = clip;
            scrubbing = true;
            scrubPosition = Mathf.Clamp01(normalizedPosition);
        }

        public static void UpdateScrub(AudioClip clip, float normalizedPosition)
        {
            if (!scrubbing || clip == null || currentClip != clip)
                BeginScrub(clip, normalizedPosition);
            else
                scrubPosition = Mathf.Clamp01(normalizedPosition);
        }

        public static void EndScrub(AudioClip clip, float normalizedPosition)
        {
            float position = Mathf.Clamp01(normalizedPosition);
            scrubbing = false;
            scrubPosition = position;
            Play(clip, position);
        }

        public static void PauseOrResume()
        {
            try
            {
                if (!paused && IsPlaying && (useGeneratedPlayer || PauseMethod != null))
                {
                    pausedSample = GetInterpolatedSample(currentClip);
                    if (useGeneratedPlayer && generatedSource != null) generatedSource.Pause();
                    else PauseMethod.Invoke(null, null);
                    paused = true;
                }
                else if (paused && (useGeneratedPlayer || ResumeMethod != null))
                {
                    if (useGeneratedPlayer && generatedSource != null) generatedSource.UnPause();
                    else ResumeMethod.Invoke(null, null);
                    paused = false;
                    playbackAnchorSample = pausedSample;
                    playbackAnchorTime = EditorApplication.timeSinceStartup;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ClipSwitch pause/resume failed: " + ex.Message);
            }
        }

        public static void Stop()
        {
            if (StopMethod != null)
            {
                try { StopMethod.Invoke(null, null); }
                catch { /* Preview stopping should not interrupt editor work. */ }
            }
            if (generatedSource != null)
            {
                generatedSource.Stop();
                generatedSource.clip = null;
            }

            currentClip = null;
            paused = false;
            scrubbing = false;
            scrubPosition = 0f;
            useGeneratedPlayer = false;
            playbackAnchorSample = 0;
            pausedSample = 0;
        }

        public static float GetNormalizedPosition(AudioClip clip)
        {
            if (clip == null || currentClip != clip || clip.samples <= 0)
                return 0f;
            if (scrubbing)
                return scrubPosition;

            if (paused)
                return Mathf.Clamp01((float)pausedSample / Mathf.Max(1, clip.samples - 1));
            int sample = GetInterpolatedSample(clip);
            return Mathf.Clamp01((float)sample / Mathf.Max(1, clip.samples - 1));
        }

        private static int GetInterpolatedSample(AudioClip clip)
        {
            if (clip == null) return 0;
            int backendSample = GetSamplePosition(clip);
            int predictedSample = playbackAnchorSample + Mathf.RoundToInt((float)(EditorApplication.timeSinceStartup - playbackAnchorTime) * clip.frequency);
            return Mathf.Clamp(Mathf.Max(backendSample, predictedSample), 0, Mathf.Max(0, clip.samples - 1));
        }

        private static int GetSamplePosition(AudioClip clip)
        {
            if (useGeneratedPlayer && generatedSource != null && generatedSource.clip == clip)
                return Mathf.Clamp(generatedSource.timeSamples, 0, Mathf.Max(0, clip.samples - 1));
            if (clip == null || GetSamplePositionMethod == null)
                return 0;
            try
            {
                object value = GetSamplePositionMethod.Invoke(null, null);
                return Mathf.Clamp(value is int ? (int)value : 0, 0, Mathf.Max(0, clip.samples - 1));
            }
            catch { return 0; }
        }

        public static float[] GetMinMaxData(AudioClip clip)
        {
            if (clip == null || GetMinMaxDataMethod == null)
                return null;

            string path = AssetDatabase.GetAssetPath(clip);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return null;

            try
            {
                return GetMinMaxDataMethod.Invoke(null, new object[] { importer }) as float[];
            }
            catch
            {
                return null;
            }
        }

        private static MethodInfo FindMethod(string[] names, int parameterCount)
        {
            if (AudioUtilType == null)
                return null;

            MethodInfo[] methods = AudioUtilType.GetMethods(Flags);
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name == names[n] && method.GetParameters().Length == parameterCount)
                        return method;
                }
            }

            return null;
        }

        private static void EnsureGeneratedPlayer()
        {
            if (generatedSource != null) return;
            generatedPlayerObject = new GameObject("ClipSwitch Realtime Preview");
            generatedPlayerObject.hideFlags = HideFlags.HideAndDontSave;
            generatedSource = generatedPlayerObject.AddComponent<AudioSource>();
            generatedSource.hideFlags = HideFlags.HideAndDontSave;
            generatedSource.playOnAwake = false;
            generatedSource.loop = false;
            generatedSource.spatialBlend = 0f;
            generatedSource.volume = 1f;
            if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
            {
                generatedListener = generatedPlayerObject.AddComponent<AudioListener>();
                generatedListener.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private static void DestroyGeneratedPlayer()
        {
            if (generatedPlayerObject != null)
                UnityEngine.Object.DestroyImmediate(generatedPlayerObject);
            generatedPlayerObject = null;
            generatedSource = null;
            generatedListener = null;
            useGeneratedPlayer = false;
        }
    }
}
