using System.IO;
using System.Windows;
using DocumentWorkflow.Application;
using DocumentWorkflow.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.App;

public partial class App : System.Windows.Application
{
    private IHost? host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args, ContentRootPath = AppContext.BaseDirectory
            });
            var directory = builder.Configuration["DocumentWorkflow:DataDirectory"];
            if (string.IsNullOrWhiteSpace(directory))
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentWorkflowDesktop");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "documents.db") }.ToString();
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Logging.AddProvider(new JsonFileLoggerProvider(Path.Combine(directory, "logs")));
            builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connection));
            var sourceDirectory = Path.GetFullPath(builder.Configuration["DocumentWorkflow:DemoSourceDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "demo-data"));
            var workspaceDirectory = Path.GetFullPath(builder.Configuration["DocumentWorkflow:WorkspaceDirectory"] ?? Path.Combine(directory, "workspace"));
            builder.Services.AddSingleton<IDocumentSource>(new DemoDocumentSource(sourceDirectory));
            builder.Services.AddSingleton<IWorkspaceService>(new LocalWorkspaceService(workspaceDirectory, sourceDirectory));
            builder.Services.AddSingleton<IFileHashService, FileHashService>();
            builder.Services.AddSingleton<IWorkflowStore, WorkflowStore>();
            builder.Services.AddSingleton<IWorkingCopyOpener, ShellWorkingCopyOpener>();
            builder.Services.AddSingleton(provider => new WorkingCopyStateService(provider.GetRequiredService<IWorkspaceService>(), provider.GetRequiredService<IFileHashService>(), provider.GetRequiredService<ILogger<WorkingCopyStateService>>()));
            builder.Services.AddSingleton<IDocumentWorkflowService, DocumentWorkflowService>();
            builder.Services.AddSingleton<IUserDialogService, UserDialogService>();
            builder.Services.AddSingleton<DatabaseInitializer>();
            builder.Services.AddSingleton<IDocumentRepository, DocumentRepository>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainWindow>();
            host = builder.Build();
            await host.StartAsync();
            host.Services.GetRequiredService<ILogger<App>>().LogInformation("Starting document workflow with data directory {DataDirectory}", directory);
            await host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            await host.Services.GetRequiredService<IDocumentWorkflowService>().ReconcileAsync();
            var window = host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            await host.Services.GetRequiredService<MainViewModel>().RefreshAsync();
        }
        catch (Exception exception)
        {
            host?.Services.GetService<ILogger<App>>()?.LogError(exception, "Application startup failed");
            MessageBox.Show("The application could not start. Check the configured data and workspace directories and the local application log.", "Document Workflow", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { host.StopAsync(timeout.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            finally { host.Dispose(); }
        }
        base.OnExit(e);
    }
}
