using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Application;

public interface IDocumentRepository
{
    Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken cancellationToken = default);
}
