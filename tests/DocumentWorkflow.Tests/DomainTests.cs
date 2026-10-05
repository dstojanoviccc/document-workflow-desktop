using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Tests;

public class DomainTests
{
    [Fact]
    public void New_document_is_available_at_version_one()
    {
        var document = new DocumentRecord("Product catalog", "Product-Catalog.XLSX");
        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal(1, document.CurrentVersion);
        Assert.Equal(WorkingCopyState.Available, document.Status);
        Assert.Equal(".xlsx", document.FileExtension);
        Assert.Equal(DateTimeKind.Utc, document.UpdatedAt.Kind);
    }
    [Theory]
    [InlineData("")]
    [InlineData("../file.pdf")]
    public void Invalid_file_names_are_rejected(string name) =>
        Assert.Throws<ArgumentException>(() => new DocumentRecord("Guide", name));

    [Fact]
    public void Working_copy_records_checkout_baseline()
    {
        var id = Guid.NewGuid();
        var copy = new WorkingCopy(id, "workspace/guide.pdf", 2, "hash");
        Assert.Equal(id, copy.DocumentId);
        Assert.Equal(2, copy.BaseVersion);
        Assert.Equal(WorkingCopyState.CheckedOut, copy.State);
    }
    [Fact]
    public void Version_numbers_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentVersion(Guid.NewGuid(), 0, "hash", ""));
}
