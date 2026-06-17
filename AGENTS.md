# AGENTS.md

## Project intent
Build a lightweight Windows 11 local-only soundboard and microphone mixer.
Route mixed audio into an existing playback device such as VB-CABLE Input.
Do not attempt to create a virtual audio driver.

## Required stack
- C#
- .NET 8
- WPF
- MVVM with CommunityToolkit.Mvvm
- NAudio

## Constraints
- No MAUI
- No WinForms
- No Avalonia
- No Electron
- No cloud services
- No AI features
- No custom audio driver
- No unnecessary dependencies

## Architecture preferences
- Prefer one WPF app project for runtime code unless another runtime project is clearly justified
- A separate test project is acceptable for automated coverage
- Keep implementations concrete and simple
- Persist settings with System.Text.Json
- Use RegisterHotKey for global hotkeys
- Use an internal mix format of 48 kHz, 32-bit float, stereo
- Preload sound clips into memory for low trigger latency
- Store clip source file paths in settings; do not build a media library system in v1

## Quality bar
- Run `dotnet restore`
- Run `dotnet build`
- Run `dotnet test`
- Fix compile errors before finishing
- Leave the repository in a buildable state
- Add TODOs only where genuinely necessary
