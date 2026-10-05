using System.Windows;

namespace DocumentWorkflow.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Activation is UI-specific; all reevaluation remains in application services.
        Activated += async (_, _) => await viewModel.OnActivatedAsync();
    }
}
