using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using SoundboardMixer.App.Services.Audio;
using SoundboardMixer.App.Tests.Support;

namespace SoundboardMixer.App.Tests;

[TestClass]
public sealed class DeviceOutputWaveProviderTests
{
    [TestMethod]
    public void Read_WritesClampedPcm16Samples()
    {
        var source = new ArraySampleProvider([-2.0f, -1.0f, 0.0f, 0.5f, 2.0f]);
        var provider = new DeviceOutputWaveProvider(source, new WaveFormat(48_000, 16, 1));
        var output = new byte[10];

        var bytesRead = provider.Read(output, 0, output.Length);

        Assert.AreEqual(output.Length, bytesRead);
        CollectionAssert.AreEqual(
            new short[] { -32767, -32767, 0, 16384, 32767 },
            Enumerable.Range(0, output.Length / sizeof(short))
                .Select(index => BitConverter.ToInt16(output, index * sizeof(short)))
                .ToArray());
    }

    [TestMethod]
    public void Read_CopiesFloat32Samples()
    {
        var samples = new[] { 0.25f, -0.75f, 1.0f };
        var source = new ArraySampleProvider(samples);
        var provider = new DeviceOutputWaveProvider(source, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1));
        var output = new byte[samples.Length * sizeof(float)];

        var bytesRead = provider.Read(output, 0, output.Length);

        Assert.AreEqual(output.Length, bytesRead);

        for (var index = 0; index < samples.Length; index++)
        {
            Assert.AreEqual(samples[index], BitConverter.ToSingle(output, index * sizeof(float)), 0.0001f);
        }
    }
}
