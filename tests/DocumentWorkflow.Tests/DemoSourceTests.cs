using System.IO.Compression;
using DocumentWorkflow.Infrastructure;

namespace DocumentWorkflow.Tests;

public class DemoSourceTests
{
    [Theory]
    [InlineData("Product-Catalog.xlsx", "xl/workbook.xml")]
    [InlineData("Pricing-Overview.xlsx", "xl/workbook.xml")]
    [InlineData("Supplier-Agreement.docx", "word/document.xml")]
    [InlineData("Technical-Specification.docx", "word/document.xml")]
    public void Office_demo_files_are_valid_packages(string name, string entry)
    {
        var source = new DemoDocumentSource(Path.Combine(AppContext.BaseDirectory, "demo-data"));
        using var archive = ZipFile.OpenRead(source.Resolve(name));
        Assert.NotNull(archive.GetEntry(entry));
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
    }
    [Fact]
    public void Pdf_demo_has_pdf_header()
    {
        var source = new DemoDocumentSource(Path.Combine(AppContext.BaseDirectory, "demo-data"));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(source.Resolve("Installation-Guide.pdf"))));
    }
    [Theory]
    [InlineData("../source.pdf")]
    [InlineData("CON.pdf")]
    [InlineData("file.pdf:other")]
    public void Unsafe_source_names_are_rejected(string name) =>
        Assert.Throws<ArgumentException>(() => new DemoDocumentSource(Path.GetTempPath()).Resolve(name));
}
