using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchWaveformCache
    {
        private const int MaxItems = 96;

        private sealed class CacheItem
        {
            public string Key;
            public Texture2D Texture;
            public long LastUse;
        }

        private static readonly Dictionary<string, CacheItem> Cache = new Dictionary<string, CacheItem>();
        private static long useCounter;

        static ClipSwitchWaveformCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        public static Texture2D Get(AudioClip clip, int width, int height)
        {
            if (clip == null || width < 2 || height < 2)
                return null;

            string path = AssetDatabase.GetAssetPath(clip);
            string key = path + "|" + clip.GetInstanceID() + "|" + width + "x" + height + "|" + EditorGUIUtility.isProSkin;
            CacheItem item;
            if (Cache.TryGetValue(path, out item) && item != null && item.Key == key && item.Texture != null)
            {
                item.LastUse = ++useCounter;
                return item.Texture;
            }

            Destroy(item);
            Texture2D texture = BuildFromImporter(clip, width, height);
            Cache[path] = new CacheItem { Key = key, Texture = texture, LastUse = ++useCounter };
            TrimCache();
            return texture;
        }

        public static Texture2D Get(ClipSwitchWavData data, string cacheKey, int width, int height)
        {
            if (data == null || data.Samples == null || width < 2 || height < 2)
                return null;

            string path = "memory:" + cacheKey;
            string key = path + "|" + data.FrameCount + "|" + data.Channels + "|" + width + "x" + height + "|" + EditorGUIUtility.isProSkin;
            CacheItem item;
            if (Cache.TryGetValue(path, out item) && item != null && item.Key == key && item.Texture != null)
            {
                item.LastUse = ++useCounter;
                return item.Texture;
            }

            Destroy(item);
            Texture2D texture = BuildFromSamples(data.Samples, data.FrameCount, data.Channels, width, height);
            Cache[path] = new CacheItem { Key = key, Texture = texture, LastUse = ++useCounter };
            TrimCache();
            return texture;
        }

        public static void Invalidate(string key)
        {
            CacheItem item;
            if (Cache.TryGetValue(key, out item))
            {
                Destroy(item);
                Cache.Remove(key);
            }
            string memoryKey = "memory:" + key;
            if (Cache.TryGetValue(memoryKey, out item))
            {
                Destroy(item);
                Cache.Remove(memoryKey);
            }
        }

        public static void Clear()
        {
            foreach (CacheItem item in Cache.Values)
                Destroy(item);
            Cache.Clear();
        }

        private static Texture2D BuildFromImporter(AudioClip clip, int width, int height)
        {
            float[] minMax = ClipSwitchAudioPreview.GetMinMaxData(clip);
            int channels = Mathf.Max(1, clip.channels);
            int columns = minMax == null ? 0 : minMax.Length / (2 * channels);
            return BuildTexture(width, height, channels, delegate(int channel, int x, out float min, out float max)
            {
                min = 0f;
                max = 0f;
                if (columns <= 0)
                    return;
                int column = Mathf.Clamp(Mathf.RoundToInt((float)x / Mathf.Max(1, width - 1) * (columns - 1)), 0, columns - 1);
                int offset = (column * channels + channel) * 2;
                if (offset + 1 < minMax.Length)
                {
                    max = minMax[offset];
                    min = minMax[offset + 1];
                }
            });
        }

        private static Texture2D BuildFromSamples(float[] samples, int frames, int channels, int width, int height)
        {
            return BuildTexture(width, height, channels, delegate(int channel, int x, out float min, out float max)
            {
                int start = (int)Math.Floor((double)x / width * frames);
                int end = Mathf.Max(start + 1, (int)Math.Ceiling((double)(x + 1) / width * frames));
                end = Mathf.Min(end, frames);
                min = 1f;
                max = -1f;
                for (int frame = start; frame < end; frame++)
                {
                    float value = samples[frame * channels + channel];
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
                if (min > max) min = max = 0f;
            });
        }

        private delegate void MinMaxProvider(int channel, int x, out float min, out float max);

        private static Texture2D BuildTexture(int width, int height, int channels, MinMaxProvider provider)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.name = "ClipSwitch Waveform";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[width * height];
            Color wave = EditorGUIUtility.isProSkin ? new Color(0.25f, 0.78f, 1f, 0.96f) : new Color(0.04f, 0.38f, 0.72f, 0.96f);
            Color center = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.14f) : new Color(0f, 0f, 0f, 0.14f);
            int laneHeight = Mathf.Max(1, height / Mathf.Max(1, channels));

            for (int channel = 0; channel < channels; channel++)
            {
                int laneBottom = channel * laneHeight;
                int laneTop = channel == channels - 1 ? height : Mathf.Min(height, laneBottom + laneHeight);
                int centerY = Mathf.Clamp((laneBottom + laneTop) / 2, 0, height - 1);
                for (int x = 0; x < width; x++) pixels[centerY * width + x] = center;

                for (int x = 0; x < width; x++)
                {
                    float min;
                    float max;
                    provider(channel, x, out min, out max);
                    float amplitude = Mathf.Max(1f, laneTop - laneBottom - 2f) * 0.48f;
                    int yMin = Mathf.Clamp(Mathf.RoundToInt(centerY + min * amplitude), laneBottom, laneTop - 1);
                    int yMax = Mathf.Clamp(Mathf.RoundToInt(centerY + max * amplitude), laneBottom, laneTop - 1);
                    if (yMin > yMax) { int swap = yMin; yMin = yMax; yMax = swap; }
                    for (int y = yMin; y <= yMax; y++) pixels[y * width + x] = wave;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void TrimCache()
        {
            while (Cache.Count > MaxItems)
            {
                string oldestKey = null;
                long oldestUse = long.MaxValue;
                foreach (KeyValuePair<string, CacheItem> pair in Cache)
                {
                    if (pair.Value != null && pair.Value.LastUse < oldestUse)
                    {
                        oldestUse = pair.Value.LastUse;
                        oldestKey = pair.Key;
                    }
                }
                if (oldestKey == null) break;
                Destroy(Cache[oldestKey]);
                Cache.Remove(oldestKey);
            }
        }

        private static void Destroy(CacheItem item)
        {
            if (item != null && item.Texture != null)
                UnityEngine.Object.DestroyImmediate(item.Texture);
        }
    }
}
