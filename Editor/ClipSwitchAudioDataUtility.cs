using System;
using UnityEditor;
using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchAudioDataUtility
    {
        public static ClipSwitchWavData ReadClip(AudioClip source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            string path = AssetDatabase.GetAssetPath(source);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return ReadLoadedClip(source);

            AudioImporterSampleSettings originalSettings = importer.defaultSampleSettings;
            bool originalLoadInBackground = importer.loadInBackground;
            bool mustReimport = originalSettings.loadType != AudioClipLoadType.DecompressOnLoad ||
                                !originalSettings.preloadAudioData || originalLoadInBackground;

            try
            {
                if (mustReimport)
                {
                    AudioImporterSampleSettings readable = originalSettings;
                    readable.loadType = AudioClipLoadType.DecompressOnLoad;
                    readable.preloadAudioData = true;
                    importer.defaultSampleSettings = readable;
                    importer.loadInBackground = false;
                    importer.SaveAndReimport();
                    source = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }

                return ReadLoadedClip(source);
            }
            finally
            {
                if (mustReimport)
                {
                    importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer != null)
                    {
                        importer.defaultSampleSettings = originalSettings;
                        importer.loadInBackground = originalLoadInBackground;
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        public static AudioClip CreatePreviewClip(ClipSwitchWavData data, string name)
        {
            if (data == null || data.Samples == null || data.FrameCount <= 0)
                throw new ArgumentException(ClipSwitchLocalization.T("Audio data is empty.", "Аудиоданные пусты."), "data");

            AudioClip result = AudioClip.Create(name, data.FrameCount, data.Channels, data.SampleRate, false);
            result.hideFlags = HideFlags.HideAndDontSave;
            if (!result.SetData(data.Samples, 0))
            {
                UnityEngine.Object.DestroyImmediate(result);
                throw new InvalidOperationException(ClipSwitchLocalization.T("Unity could not create the realtime preview clip.", "Unity не смог создать клип для realtime-предпросмотра."));
            }
            result.LoadAudioData();
            return result;
        }

        private static ClipSwitchWavData ReadLoadedClip(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
                throw new InvalidOperationException(ClipSwitchLocalization.T("The audio clip contains no readable samples.", "Аудиоклип не содержит доступных для чтения сэмплов."));

            clip.LoadAudioData();
            float[] samples = new float[checked(clip.samples * clip.channels)];
            if (!clip.GetData(samples, 0))
                throw new InvalidOperationException(ClipSwitchLocalization.T("Unity could not decode this audio clip. Check that its importer supports Decompress On Load.", "Unity не смог декодировать аудиоклип. Проверьте поддержку режима «Распаковать при загрузке»."));

            return new ClipSwitchWavData
            {
                SampleRate = clip.frequency,
                Channels = clip.channels,
                BitsPerSample = 32,
                IsFloat = true,
                Samples = samples
            };
        }
    }
}
