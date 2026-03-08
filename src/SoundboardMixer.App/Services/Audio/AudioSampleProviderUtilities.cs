using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundboardMixer.App.Services.Audio;

internal static class AudioSampleProviderUtilities
{
    public static ISampleProvider ConvertToInternalMixFormat(ISampleProvider source)
    {
        var working = ConvertChannelCount(source, AudioEngineService.InternalChannels);

        if (working.WaveFormat.SampleRate != AudioEngineService.InternalSampleRate)
        {
            working = new WdlResamplingSampleProvider(working, AudioEngineService.InternalSampleRate);
        }

        return working;
    }

    public static ISampleProvider ConvertChannelCount(ISampleProvider source, int targetChannels)
    {
        if (source.WaveFormat.Channels == targetChannels)
        {
            return source;
        }

        if (source.WaveFormat.Channels == 1 && targetChannels == 2)
        {
            return new MonoToStereoSampleProvider(source);
        }

        if (source.WaveFormat.Channels == 2 && targetChannels == 1)
        {
            return new StereoToMonoSampleProvider(source)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        return new ChannelMappingSampleProvider(source, targetChannels);
    }
}
