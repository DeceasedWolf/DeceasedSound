using Microsoft.VisualStudio.TestTools.UnitTesting;
using SoundboardMixer.App.Services.Audio;
using SoundboardMixer.App.Tests.Support;

namespace SoundboardMixer.App.Tests;

[TestClass]
public sealed class ChannelMappingSampleProviderTests
{
    [TestMethod]
    public void Read_AveragesSourceChannels_WhenMappingToMono()
    {
        var source = new ArraySampleProvider(
            [1.0f, 0.5f, -0.5f, 0.3f, 0.6f, 0.9f],
            channels: 3);
        var provider = new ChannelMappingSampleProvider(source, targetChannels: 1);
        var output = new float[2];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.AreEqual(2, samplesRead);
        AssertSamples([1.0f / 3.0f, 0.6f], output);
    }

    [TestMethod]
    public void Read_DuplicatesMonoSamples_WhenMappingToMoreChannels()
    {
        var source = new ArraySampleProvider([0.25f, -0.5f], channels: 1);
        var provider = new ChannelMappingSampleProvider(source, targetChannels: 3);
        var output = new float[6];

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.AreEqual(6, samplesRead);
        AssertSamples([0.25f, 0.25f, 0.25f, -0.5f, -0.5f, -0.5f], output);
    }

    [TestMethod]
    public void Read_CopiesSharedChannelsAndLeavesExtraChannelsSilent()
    {
        var source = new ArraySampleProvider([1.0f, -1.0f], channels: 2);
        var provider = new ChannelMappingSampleProvider(source, targetChannels: 4);
        var output = Enumerable.Repeat(99.0f, 4).ToArray();

        var samplesRead = provider.Read(output, 0, output.Length);

        Assert.AreEqual(4, samplesRead);
        AssertSamples([1.0f, -1.0f, 0.0f, 0.0f], output);
    }

    private static void AssertSamples(float[] expected, float[] actual)
    {
        Assert.AreEqual(expected.Length, actual.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], actual[index], 0.0001f, $"Sample {index}");
        }
    }
}
