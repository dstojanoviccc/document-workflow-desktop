namespace DocumentWorkflow.Application;

public interface IDocumentSource
{
    string Resolve(string fileName);
}
