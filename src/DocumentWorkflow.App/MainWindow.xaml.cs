using System.Windows;

namespace DocumentWorkflow.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        HistoryWindow? historyWindow = null;
        viewModel.HistoryRequested += history =>
        {
            historyWindow?.Close();
            historyWindow = new HistoryWindow(history) { Owner = this };
            historyWindow.Closed += (_, _) => viewModel.CloseHistory(history);
            historyWindow.Show();
        };
        // Activation is UI-specific; all reevaluation remains in application services.
        Activated += async (_, _) => await viewModel.OnActivatedAsync();
    }
}
