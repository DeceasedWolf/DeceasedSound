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
    private static readonly TimeSpan RealtimeWarningInterval = TimeSpan.FromSeconds(5);

    private readonly object _engineLock = new();
    private readonly object _clipCommandLock = new();
    private readonly ILogService _logService;
    private readonly ThrottledAudioLogger _microphoneOverflowLogger;
    private readonly ClipPlaybackState _mixedOutputClips = new();
    private readonly ClipPlaybackState _speakerMonitorClips = new();

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
        _microphoneOverflowLogger = new ThrottledAudioLogger(logService, RealtimeWarningInterval);
    }

    public event EventHandler<string>? StatusChanged;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        return GetAudioDevices(DataFlow.Capture, "capture");
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        return GetAudioDevices(DataFlow.Render, "render");
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

                var waveProvider = BuildOutputWaveProvider(outputDevice, "mixed output");

                _output = CreateOutput(outputDevice, waveProvider, "mixed output");
                _output.PlaybackStopped += OnOutputPlaybackStopped;
                _output.Play();

                var shouldStartSpeakerMonitor =
                    speakerMonitorEnabled &&
                    speakerDevice is not null &&
                    !string.Equals(speakerDevice.ID, outputDevice.ID, StringComparison.OrdinalIgnoreCase);

                if (shouldStartSpeakerMonitor)
                {
                    var speakerWaveProvider = BuildOutputWaveProvider(
                        speakerDevice!,
                        new SpeakerMonitorSampleProvider(this),
                        "speaker monitor");
                    try
                    {
                        _speakerOutput = CreateOutput(speakerDevice!, speakerWaveProvider, "speaker monitor");
                        _speakerOutput.PlaybackStopped += OnSpeakerOutputPlaybackStopped;
                        _speakerOutput.Play();
                        _logService.Info(DescribeOutputSettings("Speaker monitor", speakerDevice!, speakerWaveProvider));
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
                _logService.Info($"Internal mix format: {InternalSampleRate} Hz, 32-bit float, {InternalChannels} channels.");
                _logService.Info(DescribeMicrophoneSettings(microphoneDevice, _microphoneCapture, _microphoneBuffer));
                _logService.Info(DescribeOutputSettings("Mixed output", outputDevice, waveProvider));
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

        var clampedVolume = Math.Clamp(volume, 0.0f, 1.0f);
        lock (_clipCommandLock)
        {
            _mixedOutputClips.Add(new ActiveClipPlayback(clip, clampedVolume));

            if (_speakerOutput is not null)
            {
                _speakerMonitorClips.Add(new ActiveClipPlayback(clip, clampedVolume));
            }
        }
    }

    public void StopAllClips()
    {
        lock (_clipCommandLock)
        {
            _mixedOutputClips.Clear();
            _speakerMonitorClips.Clear();
        }

        _mixedOutputClips.WaitForMixers();
        _speakerMonitorClips.WaitForMixers();
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

    private IReadOnlyList<AudioDeviceInfo> GetAudioDevices(DataFlow dataFlow, string deviceRole)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator
                .EnumerateAudioEndPoints(dataFlow, DeviceState.Active)
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
                .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception)
        {
            _logService.Warning($"Failed to enumerate {deviceRole} audio devices. {exception.Message}");
            return Array.Empty<AudioDeviceInfo>();
        }
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
            _logService.Info(
                $"Event-driven WASAPI {outputRole} initialized on '{outputDevice.FriendlyName}' with requested latency {EventDrivenRenderLatencyMilliseconds} ms.");
            return output;
        }
        catch (Exception exception)
        {
            output?.Dispose();
            _logService.Warning(
                $"Event-driven WASAPI {outputRole} failed on '{outputDevice.FriendlyName}'. Falling back to polling mode. {exception.Message}");

            output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, PollingRenderLatencyMilliseconds);
            output.Init(waveProvider);
            _logService.Info(
                $"Polling WASAPI {outputRole} initialized on '{outputDevice.FriendlyName}' with requested latency {PollingRenderLatencyMilliseconds} ms.");
            return output;
        }
    }

    private DeviceOutputWaveProvider BuildOutputWaveProvider(MMDevice outputDevice, string outputRole)
    {
        return BuildOutputWaveProvider(outputDevice, new AudioEngineSampleProvider(this), outputRole);
    }

    private DeviceOutputWaveProvider BuildOutputWaveProvider(
        MMDevice outputDevice,
        ISampleProvider sourceProvider,
        string outputRole)
    {
        var targetFormat = outputDevice.AudioClient.MixFormat;
        ISampleProvider sampleProvider = sourceProvider;

        if (targetFormat.SampleRate != InternalSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetFormat.SampleRate);
        }

        sampleProvider = AudioSampleProviderUtilities.ConvertChannelCount(sampleProvider, targetFormat.Channels);
        return new DeviceOutputWaveProvider(sampleProvider, targetFormat, _logService, outputRole);
    }

    private static string DescribeMicrophoneSettings(
        MMDevice microphoneDevice,
        WasapiCapture microphoneCapture,
        BufferedWaveProvider microphoneBuffer)
    {
        return
            $"Microphone capture settings: device '{microphoneDevice.FriendlyName}', format {DescribeWaveFormat(microphoneCapture.WaveFormat)}, requested latency {CaptureLatencyMilliseconds} ms, buffer {microphoneBuffer.BufferLength} bytes/{MicrophoneBufferMilliseconds} ms.";
    }

    private static string DescribeOutputSettings(
        string label,
        MMDevice outputDevice,
        DeviceOutputWaveProvider waveProvider)
    {
        return
            $"{label} render settings: device '{outputDevice.FriendlyName}', device mix {DescribeWaveFormat(outputDevice.AudioClient.MixFormat)}, app output {DescribeWaveFormat(waveProvider.WaveFormat)}, encoding {waveProvider.EncodingDescription}.";
    }

    private static string DescribeWaveFormat(WaveFormat waveFormat)
    {
        return
            $"{waveFormat.SampleRate} Hz, {waveFormat.BitsPerSample}-bit {waveFormat.Encoding}, {waveFormat.Channels} channel(s), block align {waveFormat.BlockAlign}, avg {waveFormat.AverageBytesPerSecond} B/s";
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

            lock (_clipCommandLock)
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
        var microphoneBuffer = _microphoneBuffer;
        if (microphoneBuffer is null)
        {
            return;
        }

        try
        {
            var availableBytes = Math.Max(0, microphoneBuffer.BufferLength - microphoneBuffer.BufferedBytes);
            if (eventArgs.BytesRecorded > availableBytes)
            {
                _microphoneOverflowLogger.Warning(
                    $"Microphone buffer overflow risk: received {eventArgs.BytesRecorded} bytes with {availableBytes} bytes free in a {microphoneBuffer.BufferLength}-byte buffer. Incoming capture audio may be discarded.");
            }

            microphoneBuffer.AddSamples(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        }
        catch (Exception exception)
        {
            _microphoneOverflowLogger.Warning($"Microphone buffer overflowed or failed. {exception.Message}");
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

    private void FillMixBuffer(float[] buffer, int offset, int count, AudioRenderScratch scratch)
    {
        Array.Clear(buffer, offset, count);
        MixMicrophone(buffer, offset, count, scratch);
        MixClips(buffer, offset, count, _mixedOutputClips, scratch.ClipSegments);
        ApplySoftLimiter(buffer, offset, count);
    }

    private void FillSpeakerMonitorBuffer(float[] buffer, int offset, int count, AudioRenderScratch scratch)
    {
        Array.Clear(buffer, offset, count);
        MixClips(buffer, offset, count, _speakerMonitorClips, scratch.ClipSegments);
        ApplySoftLimiter(buffer, offset, count);
    }

    private void MixMicrophone(float[] buffer, int offset, int count, AudioRenderScratch scratch)
    {
        var microphoneSampleProvider = _microphoneSampleProvider;
        if (microphoneSampleProvider is null || Interlocked.CompareExchange(ref _microphoneMuted, 0, 0) == 1)
        {
            return;
        }

        var tempBuffer = scratch.EnsureMicrophoneBuffer(count);
        var samplesRead = microphoneSampleProvider.Read(tempBuffer, 0, count);
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

    private void MixClips(
        float[] buffer,
        int offset,
        int count,
        ClipPlaybackState activeClips,
        List<ClipMixSegment> clipSegments)
    {
        var soundboardVolume = _soundboardVolume;

        if (!activeClips.TryReserveSegments(count, clipSegments))
        {
            return;
        }

        try
        {
            foreach (var clipSegment in clipSegments)
            {
                var samples = clipSegment.SampleBuffer;
                var sourceOffset = clipSegment.Offset;
                var volume = soundboardVolume * clipSegment.Volume;

                for (var sampleIndex = 0; sampleIndex < clipSegment.Count; sampleIndex++)
                {
                    buffer[offset + sampleIndex] += samples[sourceOffset + sampleIndex] * volume;
                }
            }
        }
        finally
        {
            clipSegments.Clear();
            activeClips.CompleteMix();
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
        private readonly object _readLock = new();
        private readonly AudioEngineService _owner;
        private readonly AudioRenderScratch _scratch = new();

        public AudioEngineSampleProvider(AudioEngineService owner)
        {
            _owner = owner;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(InternalSampleRate, InternalChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_readLock)
            {
                _owner.FillMixBuffer(buffer, offset, count, _scratch);
                return count;
            }
        }
    }

    private sealed class SpeakerMonitorSampleProvider : ISampleProvider
    {
        private readonly object _readLock = new();
        private readonly AudioEngineService _owner;
        private readonly AudioRenderScratch _scratch = new();

        public SpeakerMonitorSampleProvider(AudioEngineService owner)
        {
            _owner = owner;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(InternalSampleRate, InternalChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_readLock)
            {
                _owner.FillSpeakerMonitorBuffer(buffer, offset, count, _scratch);
                return count;
            }
        }
    }

    private sealed class AudioRenderScratch
    {
        private float[] _microphoneBuffer = [];

        public List<ClipMixSegment> ClipSegments { get; } = [];

        public float[] EnsureMicrophoneBuffer(int minimumLength)
        {
            if (_microphoneBuffer.Length < minimumLength)
            {
                _microphoneBuffer = new float[minimumLength];
            }

            return _microphoneBuffer;
        }
    }

    private readonly record struct ClipMixSegment(float[] SampleBuffer, int Offset, int Count, float Volume);

    private sealed class ClipPlaybackState
    {
        private readonly object _gate = new();
        private readonly List<ActiveClipPlayback> _activeClips = [];
        private readonly ManualResetEventSlim _mixersIdle = new(true);
        private int _activeMixers;

        public void Add(ActiveClipPlayback activeClip)
        {
            lock (_gate)
            {
                _activeClips.Add(activeClip);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _activeClips.Clear();
                if (_activeMixers == 0)
                {
                    _mixersIdle.Set();
                }
            }
        }

        public void WaitForMixers()
        {
            _mixersIdle.Wait();
        }

        public bool TryReserveSegments(int sampleCount, List<ClipMixSegment> clipSegments)
        {
            clipSegments.Clear();

            lock (_gate)
            {
                if (_activeClips.Count == 0)
                {
                    return false;
                }

                _activeMixers++;
                _mixersIdle.Reset();

                try
                {
                    clipSegments.EnsureCapacity(_activeClips.Count);

                    for (var clipIndex = _activeClips.Count - 1; clipIndex >= 0; clipIndex--)
                    {
                        var activeClip = _activeClips[clipIndex];
                        var sampleBuffer = activeClip.Clip.SampleBuffer;
                        var availableSamples = sampleBuffer.Length - activeClip.Position;

                        if (availableSamples <= 0)
                        {
                            _activeClips.RemoveAt(clipIndex);
                            continue;
                        }

                        var samplesToMix = Math.Min(sampleCount, availableSamples);
                        clipSegments.Add(new ClipMixSegment(
                            sampleBuffer,
                            activeClip.Position,
                            samplesToMix,
                            activeClip.Volume));

                        activeClip.Position += samplesToMix;

                        if (activeClip.Position >= sampleBuffer.Length)
                        {
                            _activeClips.RemoveAt(clipIndex);
                        }
                    }

                    if (clipSegments.Count > 0)
                    {
                        return true;
                    }

                    CompleteMixNoLock();
                    return false;
                }
                catch
                {
                    CompleteMixNoLock();
                    throw;
                }
            }
        }

        public void CompleteMix()
        {
            lock (_gate)
            {
                CompleteMixNoLock();
            }
        }

        private void CompleteMixNoLock()
        {
            if (_activeMixers <= 0)
            {
                return;
            }

            _activeMixers--;
            if (_activeMixers == 0)
            {
                _mixersIdle.Set();
            }
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
