# ClipSwitch

ClipSwitch is a production-oriented audio replacement workbench for Unity Editor 2022.3+. Open it from **Tools → QuartzLab → ClipSwitch**.

## Highlights

- Replace or swap audio while preserving the target `.meta`, GUID, references, and import identity.
- Work across WAV, MP3, OGG, FLAC, AIFF, XM, MOD, IT, and S3M files supported by Unity 2022.3. When formats differ, ClipSwitch moves the target asset to the correct extension through `AssetDatabase`, so its GUID stays intact.
- Edit any `AudioClip` Unity can decode. Realtime preview updates after trim, silence removal, gain, normalization, DC removal, reverse, fades, or pitch/speed changes. Mono conversion remains in Unity Import Settings, where Unity owns it.
- Inspect and edit Unity's common audio import settings and Default/Standalone/Android/iOS/WebGL overrides.
- Restore any version from complete per-clip history. Current state is snapshotted before every restoration.
- Select one clip with a click or a range with Shift without decoding audio in the Library. Loading begins only after the explicit **Open in Editor** command; switching tabs never transfers the Library selection.
- Right-click without changing selection to Replace, Swap, inspect History, or open one/many files in the configured application. Batch actions state the exact number of source clips required.
- Switch both Library and clip picker between persistent compact-name and waveform views. Picker waveforms support preview scrubbing, pause, and stop.
- Find exact references in loaded-scene components and referencing project assets from any clip context menu.
- Browse complete history in a separate visual window where every restorable version has its own waveform and preview.
- Use separated mono/stereo/multichannel waveforms with embedded codec, rate, duration, layout, and name.
- Switch the entire interface between English, Russian, or automatic system language.

## Typical workflow

1. In **Library**, click one clip, Shift-click a range, or Ctrl/Cmd-click individual clips. This only queues GUIDs and keeps a large library responsive.
2. Use **Open in Editor** in the selection bar or context menu to decode and prepare the queued clips. No prompt is shown for an empty editor; replacing an existing editor session warns about unsaved processing settings.
3. Select one or several editor tracks. Realtime processing and Unity import settings apply to that selection; Play, Pause, and Stop are available when exactly one track is selected.
4. Use **Replace With…**, **Swap With…**, **History**, or the dynamic **Open in &lt;application&gt;** command from the editor action bar or the Library context menu. History is intentionally single-clip only.
5. **Overwrite + Backup** keeps GUIDs and references. A processed non-WAV asset is safely converted to `.wav` because Unity has no public compressed-audio encoder.
6. Open the separate visual **History** window to listen to and restore any earlier state, including the original extension and asset path.

## Safety model

- Every destructive operation creates an immutable full-file snapshot under `Assets/ClipSwitch Backups` before touching the target.
- Cross-format moves use `AssetDatabase.MoveAsset`, preserving the `.meta` file and GUID.
- Writes go through a temporary file and rollback to the snapshot on failure.
- Processing previews live in memory and are destroyed when replaced or when the window closes. Library selection never allocates those previews.
- Swap selection uses a ClipSwitch picker with explicit **Select** and **Cancel** actions; closing it cannot trigger a pending swap.
- Context menus are captured at the full row level before child controls, so right-click works over labels, empty row space, and waveforms without changing selection.
- Reading compressed clips temporarily uses `Decompress On Load`; original importer settings are restored in a `finally` block.

## Русский

ClipSwitch — готовое рабочее окно для быстрой замены звуков в существующих Unity-проектах. В настройках выберите **Русский** или **Авто**. Переведены вкладки, заголовки, поля, действия, диалоги и подсказки. Обычный клик выбирает один клип, Shift — диапазон, а Ctrl/Cmd — отдельные клипы. Библиотека при этом не декодирует звук: передача выполняется только командой **Открыть в редакторе**, а обычное переключение вкладок ничего не загружает. Пустой редактор заполняется без подтверждения; предупреждение появляется только при замене существующей сессии. ПКМ работает по всей площади строки и не меняет выделение. Для группы доступны пакетная замена, обмен, поиск ссылок и открытие файлов; визуальная История остаётся действием для одного клипа. Библиотека и окно выбора имеют сохраняемые компактный и waveform-режимы. Полное руководство доступно офлайн из вкладки Настройки.

## Installation

Install `package.json` through Unity Package Manager (**+ → Add package from disk…**) or copy the package into the project's `Packages` folder.
