using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace GameTranslationEngine.App.Services;

/// <summary>File pickers and simple dialogs, so view models don't touch windows.</summary>
public interface IDialogService
{
    Task<string?> PickFileAsync(string title, string filterName, params string[] patterns);
    Task<string?> PickFolderAsync(string title);
    Task<string?> SaveFileAsync(string title, string suggestedPath, string filterName, params string[] patterns);
    Task ShowMessageAsync(string title, string message);
}

public sealed class DialogService(Window owner) : IDialogService
{
    public async Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedPath, string filterName, params string[] patterns)
    {
        var folder = Path.GetDirectoryName(suggestedPath);
        if (folder is not null) Directory.CreateDirectory(folder);
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = Path.GetFileName(suggestedPath),
            SuggestedStartLocation = folder is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(folder),
            FileTypeChoices = [new FilePickerFileType(filterName) { Patterns = patterns }],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var close = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
        var dialog = new Window
        {
            Title = title,
            Width = 620,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 460,
                        Content = new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
