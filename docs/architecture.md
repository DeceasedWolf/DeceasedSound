# Architecture

## Overview

The solution uses a single WPF application project at `src/SoundboardMixer.App/` and keeps the design deliberately concrete:

- `ViewModels/`
  UI state and commands for the main window and clip rows.
- `Services/`
  Settings persistence, logging, file picking, audio engine, clip loading, and global hotkeys.
- `Models/`
  Persisted settings and simple data objects shared across the app.

Automated tests live in `tests/SoundboardMixer.App.Tests/`. The extra project is for coverage only; runtime implementation remains in the WPF app project.

The application is composed manually in `App.xaml.cs` without a dependency injection framework.

## Audio Flow

The audio pipeline is:

1. Enumerate the selected microphone endpoint and output/render endpoint by WASAPI device ID.
2. Start `WasapiCapture` for the selected microphone.
3. Push captured microphone buffers into a bounded `BufferedWaveProvider`.
4. Drop stale queued microphone audio only when the queue crosses the configured trim threshold, keeping monitoring latency bounded without treating normal scheduling jitter as a fault.
5. Convert microphone samples into the internal mix format:
   - 48 kHz
   - 32-bit float
   - stereo
6. Load each imported clip with `AudioFileReader`, decode it up front, and normalize it into the same internal mix format.
7. Mix live microphone samples plus all currently active clips inside a single custom `ISampleProvider`.
8. Apply a simple soft limiter/clamp pass before output.
9. Send the full mic+clip mix to the selected mixed output device through `WasapiOut`.
10. Optionally send a second clip-only render stream to a separate speaker/headphone device through another `WasapiOut` instance, controlled by a user-facing enable toggle.

This keeps microphone capture, clip scheduling, mixing, and playback routing in one focused service.

## Main Services

### `AudioEngineService`

- Enumerates capture and render devices.
- Starts/stops WASAPI microphone capture and render output.
- Requests a preferred low-latency shared-mode WASAPI profile and falls back when the device rejects it.
- Mixes live microphone audio with active in-memory clips.
- Supports stopping a specific active clip as well as stopping all clips.
- Maintains a second clip-only speaker-monitor output when configured.
- Bounds stale queued microphone audio with a trim threshold/target so delayed callbacks do not grow into persistent monitoring latency.
- Clears queued microphone audio while muted so unmuting does not replay stale capture samples.
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
  - speaker-monitor device ID
  - speaker-monitor enabled state
  - clip list
  - clip display names
  - hotkey text
  - microphone volume
  - soundboard volume
  - mute state
  - Windows startup preference
  - close-to-system-tray preference
  - basic window placement

### `WindowsStartupRegistrationService`

- Enables or disables current-user Windows startup by writing the `DeceasedSound` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Resolves the current executable path at runtime and stores it as a quoted command string.
- Does not require administrator elevation because it uses the current-user startup key.

### `GlobalHotkeyService`

- Registers optional clip and stop-all hotkeys with `RegisterHotKey`.
- Tracks registration conflicts and reports them back to the UI.
- Raises trigger events when a registered hotkey is pressed.

### `LogService`

- Publishes info, warning, and error entries for the in-app log pane.

## View Model State

`MainWindowViewModel` owns the main UI state and commands. In addition to device selection, mixer settings, clip editing, and hotkey status, it keeps a filtered Dashboard clip view for searching larger soundboards.

Dashboard tile playback is represented as UI state on each `ClipItemViewModel`. A dispatcher timer updates per-tile progress from the loaded clip duration, while click handling routes through the audio engine to either start playback or stop the active clip.

## UI Layout

The WPF window uses a Catppuccin Mocha resource theme and is organized into compact WPF panels:

- Header:
  application title, engine status, refresh, and restart controls.
- Devices:
  microphone selector, mixed-output selector, speaker-monitor selector, and speaker-monitor toggle.
- Mixer:
  microphone volume, soundboard volume, mute, and stop-all.
- Clips:
  editable clip table with label editing, file status, per-clip volume, hotkey editing, play, and remove actions.
- Dashboard:
  expanded searchable sound-tile view for quickly finding and clicking sounds without needing hotkeys.
- Settings:
  application preferences for Windows startup, close-to-system-tray behavior, and the stop-all shortcut.
- Logs:
  collapsible in-app log output.

## Window And Tray Behavior

`MainWindow` owns the native Windows notification-area integration through `Shell_NotifyIcon`. The implementation stays in WPF and Win32 interop rather than using WinForms `NotifyIcon`.

When `Minimize to System Tray when clicking X` is enabled, the close button captures window placement, cancels the close, hides the window, and adds a tray icon. Left-clicking the tray icon restores the window. The tray context menu provides `Open` and `Exit`; `Exit` performs an explicit close so normal shutdown cleanup still runs.

## Robustness Choices

- Saved devices fall back to the first currently available endpoint if the stored device ID no longer exists.
- Missing clip files stay in settings but are clearly marked in the UI.
- Windows startup registration failures are logged and the toggle is reverted when the app cannot update the current-user startup key.
- Unexpected capture/output stop events are logged and surfaced through engine status rather than crashing the app.
- Audio resources are disposed on shutdown.

## Intended VB-CABLE Routing

The expected usage is:

1. In the app, select the real microphone as input.
2. In the app, select `VB-CABLE Input` as mixed output.
3. In the app, select your real speakers or headphones as speaker monitor if desired.
4. In Discord, select `VB-CABLE Output` as the microphone input.

This preserves the project goal of routing audio through an existing playback device instead of implementing a virtual driver.
