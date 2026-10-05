using System.Collections.ObjectModel;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.App;

public sealed class DocumentListItemViewModel
{
    private readonly DocumentRecord document;
    public DocumentListItemViewModel(DocumentRecord document, Action<DocumentListItemViewModel> showDetails)
    {
        this.document = document;
        ViewDetailsCommand = new RelayCommand(() => showDetails(this));
    }
    public Guid Id => document.Id;
    public string LogicalName => document.LogicalName;
    public string FileName => document.FileName;
    public string Version => $"v{document.CurrentVersion}";
    public string Status => document.Status.ToString();
    public string Updated => document.UpdatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm");
    public RelayCommand ViewDetailsCommand { get; }
}

public sealed class MainViewModel : ObservableViewModel
{
    private readonly IDocumentRepository repository;
    private readonly ILogger<MainViewModel> logger;
    public MainViewModel(IDocumentRepository repository, ILogger<MainViewModel> logger)
    {
        this.repository = repository;
        this.logger = logger;
        RefreshCommand = new AsyncCommand(RefreshAsync, ReportError);
        CloseDetailsCommand = new RelayCommand(() => { DetailsVisible = false; Notify(nameof(DetailsVisible)); });
    }
    public ObservableCollection<DocumentListItemViewModel> Documents { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand CloseDetailsCommand { get; }
    public bool IsBusy { get; private set; }
    public string Message { get; private set; } = "Loading documents…";
    public string CountText => $"{Documents.Count} documents";
    public bool DetailsVisible { get; private set; }
    public DocumentListItemViewModel? SelectedDocument { get; private set; }
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Notify(nameof(IsBusy));
        Message = "Loading documents…";
        Notify(nameof(Message));
        try
        {
            var documents = await repository.ListAsync();
            Documents.Clear();
            foreach (var document in documents) Documents.Add(new(document, ShowDetails));
            DetailsVisible = false;
            Notify(nameof(DetailsVisible));
            Notify(nameof(CountText));
            Message = documents.Count == 0 ? "No documents found." : $"Loaded from SQLite • refreshed {DateTime.Now:HH:mm:ss}";
            logger.LogInformation("Loaded {DocumentCount} documents", documents.Count);
        }
        catch (Exception error) { ReportError(error); }
        finally { IsBusy = false; Notify(nameof(IsBusy)); Notify(nameof(Message)); }
    }
    private void ShowDetails(DocumentListItemViewModel document)
    {
        SelectedDocument = document;
        DetailsVisible = true;
        Notify(nameof(SelectedDocument));
        Notify(nameof(DetailsVisible));
    }
    private void ReportError(Exception error)
    {
        logger.LogError(error, "Could not load documents");
        Message = "Could not load documents. Check the application log and try Refresh again.";
        Notify(nameof(Message));
    }
}

