using Microsoft.VisualStudio.TestTools.UnitTesting;
using SoundboardMixer.App.Services.Hotkeys;
using System.Windows.Input;

namespace SoundboardMixer.App.Tests;

[TestClass]
public sealed class HotkeyGestureTests
{
    [TestMethod]
    public void TryParse_NormalizesModifierOrderAndDigitKeys()
    {
        var parsed = HotkeyGesture.TryParse("alt + control + 1", out var gesture, out var error);

        Assert.IsTrue(parsed, error);
        Assert.IsNotNull(gesture);
        Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Alt, gesture.Modifiers);
        Assert.AreEqual(Key.D1, gesture.Key);
        Assert.AreEqual("Ctrl+Alt+1", gesture.ToString());
        Assert.AreEqual(0x0003u, gesture.GetNativeModifiers());
    }

    [TestMethod]
    public void TryParse_NormalizesNamedKeyAliases()
    {
        var parsed = HotkeyGesture.TryParse("windows+PgDn", out var gesture, out var error);

        Assert.IsTrue(parsed, error);
        Assert.IsNotNull(gesture);
        Assert.AreEqual(ModifierKeys.Windows, gesture.Modifiers);
        Assert.AreEqual(Key.PageDown, gesture.Key);
        Assert.AreEqual("Win+PgDn", gesture.ToString());
        Assert.AreEqual(0x0008u, gesture.GetNativeModifiers());
    }

    [DataTestMethod]
    [DataRow("   ", "Hotkey is empty")]
    [DataRow("Ctrl+Ctrl+A", "Duplicate modifier 'Ctrl'")]
    [DataRow("Ctrl+Alt", "A non-modifier key is required")]
    [DataRow("Ctrl+A+B", "Specify only one non-modifier key")]
    [DataRow("Ctrl+DefinitelyNotAKey", "Unsupported key 'DefinitelyNotAKey'")]
    public void TryParse_ReportsInvalidHotkeys(string text, string expectedError)
    {
        var parsed = HotkeyGesture.TryParse(text, out var gesture, out var error);

        Assert.IsFalse(parsed);
        Assert.IsNull(gesture);
        Assert.AreEqual(expectedError, error);
    }
}
