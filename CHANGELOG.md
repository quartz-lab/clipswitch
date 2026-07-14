# Changelog

## 1.0.0 — 2026-07-13

- Added smooth 60 FPS playhead interpolation and continuous waveform scrubbing, including sub-0.1-second clips.
- Added channel-separated Unity-style waveforms with compact name, duration, sample rate, channel layout, and codec overlays.
- Added replacement and swapping across every audio format supported by Unity 2022.3 while preserving asset GUIDs.
- Replaced the WAV-only editor with realtime, asynchronous PCM processing for any decodable `AudioClip`.
- Added Unity import settings for common and platform-specific options, plus original/imported size and ratio.
- Added complete, unlimited per-clip visual history with waveform preview and restoration to any snapshot.
- Added deferred single/range/multi-selection in Library: clicking never switches tabs or allocates editor PCM, while Shift and Ctrl/Cmd support batch queues.
- Added multi-track editor selection with shared realtime processing/import settings and single-track Play, Pause, and Stop controls.
- Added a right-click Library context menu that does not alter selection, including count-aware batch Replace/Swap, single-clip visual History, and multi-file external opening.
- Replaced Unity's close-sensitive object picker with a virtualized ClipSwitch picker whose callback only runs after explicit confirmation.
- Added dynamic `Open in <application>` labels and true OS-default application launching when no executable is configured.
- Added file extensions to editor track/playback names, enlarged Stop icons, and removed duplicate advanced mono conversion.
- Added reliable drag-and-drop swapping.
- Made Library and Audio Editor context menus respond over the entire row, including labels, empty space, and waveforms.
- Simplified multi-selection action suffixes to plain item counts such as `(5)`.
- Added persistent compact-name and waveform modes to Library and the clip picker, including picker preview, pause, and stop.
- Changed editor transfer semantics: tab navigation never transfers Library selection, empty editor loads need no confirmation, and only replacing a loaded session prompts.
- Added **Open in Editor** and **Find References in Project** context actions; the references window separates loaded Hierarchy components from referencing project assets.
- Added bundled offline HTML documentation and a Settings button that opens it without network access.
- Added Library, Audio Editor, and Settings tabs.
- Added complete English/Russian UI localization and tooltips for controls.
- Added configurable external audio application support.
- Added built-in Unity Editor icons and bounded waveform texture caching with deterministic cleanup.
- Added a virtualized library that draws and caches only visible rows, eliminating large-project list stalls.
- Added click-and-drag scrubbing that keeps the playhead under the pointer and starts playback only on release.
- Fixed temporary-file cleanup, cross-format rollback, corrupted UI text, and several preview/cache lifetime leaks.
