# Architecture

## Overview

The solution uses a single WPF application project at `src/SoundboardMixer.App/` and keeps the design deliberately concrete:

- `ViewModels/`
  UI state and commands for the main window and clip rows.
- `Services/`
  Settings persistence, logging, file picking, audio engine, clip loading, and global hotkeys.
- `Models/`
  Persisted settings and simple data objects shared across the app.

The application is composed manually in `App.xaml.cs` without a dependency injection framework.

## Audio Flow

The audio pipeline is:

1. Enumerate the selected microphone endpoint and output/render endpoint by WASAPI device ID.
2. Start `WasapiCapture` for the selected microphone.
3. Push captured microphone buffers into a `BufferedWaveProvider`.
4. Convert microphone samples into the internal mix format:
   - 48 kHz
   - 32-bit float
   - stereo
5. Load each imported clip with `AudioFileReader`, decode it up front, and normalize it into the same internal mix format.
6. Mix live microphone samples plus all currently active clips inside a single custom `ISampleProvider`.
7. Apply a simple soft limiter/clamp pass before output.
8. Send the full mic+clip mix to the selected mixed output device through `WasapiOut`.
9. Optionally send a second clip-only render stream to a separate speaker/headphone device through another `WasapiOut` instance, controlled by a user-facing enable toggle.

This keeps microphone capture, clip scheduling, mixing, and playback routing in one focused service.

## Main Services

### `AudioEngineService`

- Enumerates capture and render devices.
- Starts/stops WASAPI microphone capture and render output.
- Mixes live microphone audio with active in-memory clips.
- Maintains a second clip-only speaker-monitor output when configured.
- Uses a lower-latency shared-mode capture/render profile than the original scaffold.
- Applies microphone volume, soundboard volume, mute state, and a soft limiter.
- Raises engine status updates for the UI.

### `ClipLoaderService`

- Loads `.wav` and `.mp3` files from their original paths.
- Decodes audio through NAudio.
- Normalizes clip data into the internal 48 kHz float stereo format.
- Returns an in-memory clip ready for low-latency playback.

### `SettingsService`

- Reads and writes `%AppData%\SoundboardMixer\settings.json` using `System.Text.Json`.
- Persists:
  - microphone device ID
  - output device ID
  - clip list
  - clip display names
  - hotkey text
  - microphone volume
  - soundboard volume
  - mute state
  - basic window placement

### `GlobalHotkeyService`

- Registers optional clip hotkeys with `RegisterHotKey`.
- Tracks registration conflicts and reports them back to the UI.
- Raises clip trigger events when a registered hotkey is pressed.

### `LogService`

- Publishes info, warning, and error entries for the in-app log pane.

## UI Layout

The WPF window is organized into three functional areas:

- Top:
  microphone selector, mixed-output selector, speaker-monitor selector, refresh/restart controls, and engine status.
- Middle:
  clip list with label editing, file status, hotkey editing, play, and remove actions.
- Bottom:
  microphone volume, soundboard volume, mute, stop-all, and log output.

## Robustness Choices

- Saved devices fall back to the first currently available endpoint if the stored device ID no longer exists.
- Missing clip files stay in settings but are clearly marked in the UI.
- Unexpected capture/output stop events are logged and surfaced through engine status rather than crashing the app.
- Audio resources are disposed on shutdown.

## Intended VB-CABLE Routing

The expected usage is:

1. In the app, select the real microphone as input.
2. In the app, select `VB-CABLE Input` as mixed output.
3. In the app, select your real speakers or headphones as speaker monitor if desired.
4. In Discord, select `VB-CABLE Output` as the microphone input.

This preserves the project goal of routing audio through an existing playback device instead of implementing a virtual driver.
