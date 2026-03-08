# SoundboardMixer

SoundboardMixer is a local-only Windows 11 WPF application for mixing a real microphone with triggered soundboard clips and sending the combined result to a normal playback endpoint such as `CABLE Input (VB-Audio Virtual Cable)`.

It does not create a virtual audio driver. The expected routing is:

1. In SoundboardMixer, choose your real microphone as the input device.
2. In SoundboardMixer, choose `VB-CABLE Input` as the output device.
3. In Discord or another chat app, choose `VB-CABLE Output` as the microphone input.

That arrangement lets the app mix your live microphone plus sound clips into VB-CABLE, while Discord reads the mixed signal from the other side of the cable.

## Features

- Enumerates physical microphone capture devices and playback/render devices.
- Persists the selected microphone and output device IDs in `%AppData%\SoundboardMixer\settings.json`.
- Imports `.wav` and `.mp3` clips by file path without copying them into the app.
- Preloads clips into memory and converts them to a 48 kHz, 32-bit float, stereo internal mix format.
- Supports overlapping clip playback.
- Mixes live microphone capture with currently playing clips.
- Includes microphone volume, soundboard master volume, microphone mute, stop-all, engine status, and an in-app log.
- Supports optional global clip hotkeys through `RegisterHotKey`.

## Build

```powershell
dotnet restore SoundboardMixer.sln
dotnet build SoundboardMixer.sln
```

## Using The App

1. Launch the app.
2. Choose the microphone device you actually speak into.
3. Choose the playback device that should receive the mixed result.
   For VB-CABLE setups this should be `CABLE Input`.
4. Click `Add Clips` and import `.wav` or `.mp3` files.
5. Trigger clips with the inline `Play` buttons, `Play Selected`, or an optional global hotkey.
6. Adjust `Microphone Volume` and `Soundboard Volume` as needed.
7. In Discord, set the microphone input device to `CABLE Output`.

## Hotkeys

- Enter hotkeys in the `Hotkey` column using formats such as `Ctrl+Alt+1`, `Shift+F8`, or `Alt+Num1`.
- If a hotkey cannot be registered because another app already uses it, the `Hotkey Status` column shows the conflict.
- Hotkeys are optional. Leave the field blank to disable one.

## Notes And Limitations

- The app stores source file paths only. If a clip file moves or is deleted, the UI marks it as missing.
- Device disconnects are handled without crashing, but you may need to click `Refresh Devices` or `Restart Audio` after a hardware change.
- The mixer runs internally at 48 kHz stereo float. Output compatibility still depends on the selected playback device and its shared-mode WASAPI support.
