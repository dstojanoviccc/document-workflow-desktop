using System.Windows;

namespace DocumentWorkflow.App;

public interface IUserDialogService
{
    bool ConfirmDiscard(string fileName, bool localEditsMayBeLost);
    string? SelectLocalCopyDestination(string fileName) => null;
}
public sealed class UserDialogService : IUserDialogService
{
    public string? SelectLocalCopyDestination(string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save local edits as a separate copy", FileName = System.IO.Path.GetFileNameWithoutExtension(fileName) + "-local-copy" + System.IO.Path.GetExtension(fileName),
            DefaultExt = System.IO.Path.GetExtension(fileName), AddExtension = true, OverwritePrompt = false,
            CheckPathExists = true
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
    public static string ConfirmationText(string fileName, bool localEditsMayBeLost) => localEditsMayBeLost
        ? $"Discard checkout for {fileName}?\n\nThis local copy contains changes, or its contents could not be verified. Discarding will permanently delete local edits that have not been checked in.\n\nThe central source file remains unchanged. This action cannot be undone."
        : $"Discard checkout for {fileName}?\n\nThe local working copy will be deleted. The central source file remains unchanged.\n\nThis action cannot be undone.";
    public bool ConfirmDiscard(string fileName, bool localEditsMayBeLost) => MessageBox.Show(System.Windows.Application.Current.MainWindow,
        ConfirmationText(fileName, localEditsMayBeLost),
        "Discard Checkout", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
