using Microsoft.Win32;

namespace SoundboardMixer.App.Services;

internal interface IFileDialogService
{
    IReadOnlyList<string> PickClipFiles();
}

internal sealed class FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> PickClipFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Title = "Import sound clips",
            Filter = "Audio Files (*.wav;*.mp3)|*.wav;*.mp3|Wave Files (*.wav)|*.wav|MP3 Files (*.mp3)|*.mp3"
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
}
