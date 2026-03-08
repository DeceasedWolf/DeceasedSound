using System.Buffers;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services.Audio;

public sealed class AudioEngineService : IAudioEngineService
{
    public const int InternalSampleRate = 48_000;
    public const int InternalChannels = 2;
    private const int CaptureLatencyMilliseconds = 25;
    private const int MicrophoneBufferMilliseconds = 50;
    private const int EventDrivenRenderLatencyMilliseconds = 25;
    private const int PollingRenderLatencyMilliseconds = 40;

    private readonly object _engineLock = new();
    private readonly object _clipPlaybackLock = new();
    private readonly ILogService _logService;
    private readonly List<ActiveClipPlayback> _mixedOutputClips = [];
    private readonly List<ActiveClipPlayback> _speakerMonitorClips = [];

    private WasapiCapture? _microphoneCapture;
    private BufferedWaveProvider? _microphoneBuffer;
    private ISampleProvider? _microphoneSampleProvider;
    private WasapiOut? _output;
    private WasapiOut? _speakerOutput;

    private float _microphoneVolume = 1.0f;
    private float _soundboardVolume = 1.0f;
    private int _microphoneMuted;
    private int _isStopping;
    private string _status = "Stopped";

    public AudioEngineService(ILogService logService)
    {
        _logService = logService;
    }

    public event EventHandler<string>? StatusChanged;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
            .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
            .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Start(string? microphoneDeviceId, string? outputDeviceId, string? speakerDeviceId, bool speakerMonitorEnabled)
    {
        lock (_engineLock)
        {
            StopInternal(updateStatus: false);

            using var enumerator = new MMDeviceEnumerator();
            var microphoneDevice = ResolveDevice(enumerator, DataFlow.Capture, microphoneDeviceId);
            var outputDevice = ResolveDevice(enumerator, DataFlow.Render, outputDeviceId);
            var speakerDevice = ResolveDevice(enumerator, DataFlow.Render, speakerDeviceId);

            if (microphoneDevice is null)
            {
                SetStatus("No active microphone device is available.");
                return;
            }

            if (outputDevice is null)
            {
                SetStatus("No active playback device is available.");
                return;
            }

            try
            {
                _microphoneCapture = new WasapiCapture(microphoneDevice, true, CaptureLatencyMilliseconds)
                {
                    ShareMode = AudioClientShareMode.Shared
                };
                _microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
                _microphoneCapture.RecordingStopped += OnMicrophoneRecordingStopped;

                _microphoneBuffer = new BufferedWaveProvider(_microphoneCapture.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(MicrophoneBufferMilliseconds),
                    DiscardOnBufferOverflow = true,
                    ReadFully = false
                };

                _microphoneSampleProvider = AudioSampleProviderUtilities
                    .ConvertToInternalMixFormat(_microphoneBuffer.ToSampleProvider());

                var waveProvider = BuildOutputWaveProvider(outputDevice);

                _output = CreateOutput(outputDevice, waveProvider, "mixed output");
                _output.PlaybackStopped += OnOutputPlaybackStopped;
                _output.Play();

                var shouldStartSpeakerMonitor =
                    speakerMonitorEnabled &&
                    speakerDevice is not null &&
                    !string.Equals(speakerDevice.ID, outputDevice.ID, StringComparison.OrdinalIgnoreCase);

                if (shouldStartSpeakerMonitor)
                {
                    var speakerWaveProvider = BuildOutputWaveProvider(speakerDevice!, new SpeakerMonitorSampleProvider(this));
                    try
                    {
                        _speakerOutput = CreateOutput(speakerDevice!, speakerWaveProvider, "speaker monitor");
                        _speakerOutput.PlaybackStopped += OnSpeakerOutputPlaybackStopped;
                        _speakerOutput.Play();
                        _logService.Info($"Speaker monitor format: {speakerWaveProvider.WaveFormat}");
                    }
                    catch (Exception exception)
                    {
                        _speakerOutput?.Dispose();
                        _speakerOutput = null;
                        _logService.Warning(
                            $"Speaker monitor failed to start on '{speakerDevice!.FriendlyName}'. Clips will only play to the mixed output. {exception.Message}");
                    }
                }
                else if (speakerMonitorEnabled && speakerDevice is not null)
                {
                    _logService.Info("Speaker monitor disabled because it matches the mixed output device.");
                }
                else if (!speakerMonitorEnabled)
                {
                    _logService.Info("Speaker monitor playback is turned off.");
                }

                _microphoneCapture.StartRecording();
                _logService.Info($"Microphone capture format: {_microphoneCapture.WaveFormat}");
                _logService.Info($"Output render format: {waveProvider.WaveFormat}");
                _logService.Info(
                    $"Latency profile: capture {CaptureLatencyMilliseconds} ms, mic buffer {MicrophoneBufferMilliseconds} ms, render {EventDrivenRenderLatencyMilliseconds}/{PollingRenderLatencyMilliseconds} ms.");
                SetStatus(
                    _speakerOutput is not null
                        ? $"Running: {microphoneDevice.FriendlyName} -> {outputDevice.FriendlyName} | Clips -> {speakerDevice!.FriendlyName}"
                        : $"Running: {microphoneDevice.FriendlyName} -> {outputDevice.FriendlyName}");
            }
            catch (Exception exception)
            {
                _logService.Error(
                    $"Failed to start the audio engine for microphone '{microphoneDevice.FriendlyName}' and mixed output '{outputDevice.FriendlyName}'.",
                    exception);
                StopInternal(updateStatus: false);
                SetStatus("Audio start failed. See the log for details.");
            }
        }
    }

    public void Stop()
    {
        lock (_engineLock)
        {
            StopInternal(updateStatus: true);
        }
    }

    public void PlayClip(LoadedClip clip, float volume)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (clip.SampleBuffer.Length == 0)
        {
            return;
        }

        lock (_clipPlaybackLock)
        {
            var clampedVolume = Math.Clamp(volume, 0.0f, 1.0f);
            _mixedOutputClips.Add(new ActiveClipPlayback(clip, clampedVolume));

            if (_speakerOutput is not null)
            {
                _speakerMonitorClips.Add(new ActiveClipPlayback(clip, clampedVolume));
            }
        }
    }

    public void StopAllClips()
    {
        lock (_clipPlaybackLock)
        {
            _mixedOutputClips.Clear();
            _speakerMonitorClips.Clear();
        }
    }

    public void UpdateMixSettings(float microphoneVolume, float soundboardVolume, bool microphoneMuted)
    {
        _microphoneVolume = Math.Clamp(microphoneVolume, 0.0f, 1.0f);
        _soundboardVolume = Math.Clamp(soundboardVolume, 0.0f, 1.0f);
        Interlocked.Exchange(ref _microphoneMuted, microphoneMuted ? 1 : 0);
    }

    public void Dispose()
    {
        Stop();
    }

    private static MMDevice? ResolveDevice(MMDeviceEnumerator enumerator, DataFlow dataFlow, string? preferredDeviceId)
    {
        var devices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active).ToList();
        if (devices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            var preferred = devices.FirstOrDefault(device =>
                string.Equals(device.ID, preferredDeviceId, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
            {
                return preferred;
            }
        }

        return devices[0];
    }

    private WasapiOut CreateOutput(MMDevice outputDevice, IWaveProvider waveProvider, string outputRole)
    {
        WasapiOut? output = null;

        try
        {
            output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, EventDrivenRenderLatencyMilliseconds);
            output.Init(waveProvider);
            return output;
        }
        catch (Exception exception)
        {
            output?.Dispose();
            _logService.Warning(
                $"Event-driven WASAPI {outputRole} failed on '{outputDevice.FriendlyName}'. Falling back to polling mode. {exception.Message}");

            output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, PollingRenderLatencyMilliseconds);
            output.Init(waveProvider);
            return output;
        }
    }

    private IWaveProvider BuildOutputWaveProvider(MMDevice outputDevice)
    {
        return BuildOutputWaveProvider(outputDevice, new AudioEngineSampleProvider(this));
    }

    private IWaveProvider BuildOutputWaveProvider(MMDevice outputDevice, ISampleProvider sourceProvider)
    {
        var targetFormat = outputDevice.AudioClient.MixFormat;
        ISampleProvider sampleProvider = sourceProvider;

        if (targetFormat.SampleRate != InternalSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetFormat.SampleRate);
        }

        sampleProvider = AudioSampleProviderUtilities.ConvertChannelCount(sampleProvider, targetFormat.Channels);
        return new DeviceOutputWaveProvider(sampleProvider, targetFormat);
    }

    private void StopInternal(bool updateStatus)
    {
        if (Interlocked.Exchange(ref _isStopping, 1) == 1)
        {
            return;
        }

        try
        {
            if (_output is not null)
            {
                _output.PlaybackStopped -= OnOutputPlaybackStopped;
                TryExecute(_output.Stop);
                _output.Dispose();
                _output = null;
            }

            if (_speakerOutput is not null)
            {
                _speakerOutput.PlaybackStopped -= OnSpeakerOutputPlaybackStopped;
                TryExecute(_speakerOutput.Stop);
                _speakerOutput.Dispose();
                _speakerOutput = null;
            }

            if (_microphoneCapture is not null)
            {
                _microphoneCapture.DataAvailable -= OnMicrophoneDataAvailable;
                _microphoneCapture.RecordingStopped -= OnMicrophoneRecordingStopped;
                TryExecute(_microphoneCapture.StopRecording);
                _microphoneCapture.Dispose();
                _microphoneCapture = null;
            }

            _microphoneBuffer = null;
            _microphoneSampleProvider = null;

            lock (_clipPlaybackLock)
            {
                _mixedOutputClips.Clear();
                _speakerMonitorClips.Clear();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isStopping, 0);
        }

        if (updateStatus)
        {
            SetStatus("Stopped");
        }
    }

    private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        try
        {
            _microphoneBuffer?.AddSamples(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        }
        catch (Exception exception)
        {
            _logService.Warning($"Microphone buffer overflowed or failed. {exception.Message}");
        }
    }

    private void OnMicrophoneRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (Interlocked.CompareExchange(ref _isStopping, 0, 0) == 1)
        {
            return;
        }

        HandleTransportStopped("Microphone capture stopped unexpectedly.", eventArgs.Exception);
    }

    private void OnOutputPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (Interlocked.CompareExchange(ref _isStopping, 0, 0) == 1)
        {
            return;
        }

        HandleTransportStopped("Audio output stopped unexpectedly.", eventArgs.Exception);
    }

    private void OnSpeakerOutputPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (Interlocked.CompareExchange(ref _isStopping, 0, 0) == 1)
        {
            return;
        }

        HandleTransportStopped("Speaker monitor output stopped unexpectedly.", eventArgs.Exception);
    }

    private void HandleTransportStopped(string message, Exception? exception)
    {
        if (exception is not null)
        {
            _logService.Warning($"{message} {exception.Message}");
        }
        else
        {
            _logService.Warning(message);
        }

        lock (_engineLock)
        {
            StopInternal(updateStatus: false);
        }

        SetStatus(message);
    }

    private void FillMixBuffer(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        MixMicrophone(buffer, offset, count);
        MixClips(buffer, offset, count, _mixedOutputClips);
        ApplySoftLimiter(buffer, offset, count);
    }

    private void FillSpeakerMonitorBuffer(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        MixClips(buffer, offset, count, _speakerMonitorClips);
        ApplySoftLimiter(buffer, offset, count);
    }

    private void MixMicrophone(float[] buffer, int offset, int count)
    {
        if (_microphoneSampleProvider is null || Interlocked.CompareExchange(ref _microphoneMuted, 0, 0) == 1)
        {
            return;
        }

        var tempBuffer = ArrayPool<float>.Shared.Rent(count);
        try
        {
            Array.Clear(tempBuffer, 0, count);
            var samplesRead = _microphoneSampleProvider.Read(tempBuffer, 0, count);
            if (samplesRead <= 0)
            {
                return;
            }

            var volume = _microphoneVolume;
            for (var index = 0; index < samplesRead; index++)
            {
                buffer[offset + index] += tempBuffer[index] * volume;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tempBuffer);
        }
    }

    private void MixClips(float[] buffer, int offset, int count, List<ActiveClipPlayback> activeClips)
    {
        var soundboardVolume = _soundboardVolume;

        lock (_clipPlaybackLock)
        {
            for (var clipIndex = activeClips.Count - 1; clipIndex >= 0; clipIndex--)
            {
                var activeClip = activeClips[clipIndex];
                var availableSamples = activeClip.Clip.SampleBuffer.Length - activeClip.Position;

                if (availableSamples <= 0)
                {
                    activeClips.RemoveAt(clipIndex);
                    continue;
                }

                var samplesToMix = Math.Min(count, availableSamples);
                for (var sampleIndex = 0; sampleIndex < samplesToMix; sampleIndex++)
                {
                    buffer[offset + sampleIndex] += activeClip.Clip.SampleBuffer[activeClip.Position + sampleIndex] * soundboardVolume * activeClip.Volume;
                }

                activeClip.Position += samplesToMix;

                if (activeClip.Position >= activeClip.Clip.SampleBuffer.Length)
                {
                    activeClips.RemoveAt(clipIndex);
                }
            }
        }
    }

    private static void ApplySoftLimiter(float[] buffer, int offset, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var sample = buffer[offset + index];
            var absoluteValue = MathF.Abs(sample);

            if (absoluteValue > 0.95f)
            {
                var excess = absoluteValue - 0.95f;
                var limited = 0.95f + (0.05f * MathF.Tanh(excess / 0.05f));
                sample = MathF.CopySign(MathF.Min(limited, 1.0f), sample);
            }

            buffer[offset + index] = Math.Clamp(sample, -1.0f, 1.0f);
        }
    }

    private void SetStatus(string status)
    {
        _status = status;
        StatusChanged?.Invoke(this, _status);
    }

    private static void TryExecute(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Disposal paths remain best-effort.
        }
    }

    private sealed class AudioEngineSampleProvider : ISampleProvider
    {
        private readonly AudioEngineService _owner;

        public AudioEngineSampleProvider(AudioEngineService owner)
        {
            _owner = owner;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(InternalSampleRate, InternalChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            _owner.FillMixBuffer(buffer, offset, count);
            return count;
        }
    }

    private sealed class SpeakerMonitorSampleProvider : ISampleProvider
    {
        private readonly AudioEngineService _owner;

        public SpeakerMonitorSampleProvider(AudioEngineService owner)
        {
            _owner = owner;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(InternalSampleRate, InternalChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            _owner.FillSpeakerMonitorBuffer(buffer, offset, count);
            return count;
        }
    }

    private sealed class ActiveClipPlayback
    {
        public ActiveClipPlayback(LoadedClip clip, float volume)
        {
            Clip = clip;
            Volume = volume;
        }

        public LoadedClip Clip { get; }

        public float Volume { get; }

        public int Position { get; set; }
    }
}
