using System.Windows;

namespace DocumentWorkflow.App;

public interface IUserDialogService
{
    bool ConfirmDiscard(string fileName, bool localEditsMayBeLost);
}
public sealed class UserDialogService : IUserDialogService
{
    public static string ConfirmationText(string fileName, bool localEditsMayBeLost) => localEditsMayBeLost
        ? $"Discard checkout for {fileName}?\n\nThis local copy contains changes, or its contents could not be verified. Discarding will permanently delete local edits that have not been checked in.\n\nThe central source file remains unchanged. This action cannot be undone."
        : $"Discard checkout for {fileName}?\n\nThe local working copy will be deleted. The central source file remains unchanged.\n\nThis action cannot be undone.";
    public bool ConfirmDiscard(string fileName, bool localEditsMayBeLost) => MessageBox.Show(System.Windows.Application.Current.MainWindow,
        ConfirmationText(fileName, localEditsMayBeLost),
        "Discard Checkout", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
