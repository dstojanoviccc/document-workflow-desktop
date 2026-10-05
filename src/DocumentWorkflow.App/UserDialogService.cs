using System.Windows;

namespace DocumentWorkflow.App;

public interface IUserDialogService
{
    bool ConfirmDiscard(string fileName);
}
public sealed class UserDialogService : IUserDialogService
{
    public bool ConfirmDiscard(string fileName) => MessageBox.Show(System.Windows.Application.Current.MainWindow,
        $"Discard checkout for {fileName}?\n\nThe local working copy and any edits will be deleted. The central source file will remain unchanged.\n\nThis action cannot be undone.",
        "Discard Checkout", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
