using System.Diagnostics;
using Microsoft.Win32;

namespace SoundboardMixer.App.Services;

internal sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DeceasedSound";

    public bool SetAutoStartEnabled(bool enabled, out string? errorMessage)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                              ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (runKey is null)
            {
                throw new InvalidOperationException("Could not open the current-user Windows startup registry key.");
            }

            if (enabled)
            {
                var executablePath = ResolveExecutablePath();
                runKey.SetValue(RunValueName, QuotePath(executablePath), RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            errorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private static string ResolveExecutablePath()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not resolve the DeceasedSound executable path.");
        }

        return executablePath;
    }

    private static string QuotePath(string path)
    {
        return $"\"{path}\"";
    }
}
