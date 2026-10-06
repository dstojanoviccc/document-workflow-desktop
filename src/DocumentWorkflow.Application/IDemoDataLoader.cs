namespace DocumentWorkflow.Application;

public interface IDemoDataLoader
{
    Task LoadDemoDataAsync(CancellationToken cancellationToken = default);
}
