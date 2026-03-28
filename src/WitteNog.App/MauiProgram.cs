using CommunityToolkit.Maui;
using MediatR;
using IoFileSystem = System.IO.Abstractions.FileSystem;
using IIoFileSystem = System.IO.Abstractions.IFileSystem;
using Microsoft.Extensions.Logging;
using WitteNog.App.Services;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Audio;
using WitteNog.Infrastructure.Parsing;
using WitteNog.Infrastructure.Settings;
using WitteNog.Infrastructure.Storage;
using WitteNog.Infrastructure.Tasks;

namespace WitteNog.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();

        // Infrastructure
        builder.Services.AddSingleton<IIoFileSystem, IoFileSystem>();
        builder.Services.AddSingleton<NoteParser>();
        builder.Services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        builder.Services.AddSingleton<INoteRepository>(sp =>
            new NoteRepository(
                sp.GetRequiredService<IIoFileSystem>(),
                sp.GetRequiredService<IWikiLinkParser>(),
                sp.GetRequiredService<NoteParser>()));
        builder.Services.AddSingleton<IMarkdownStorage>(sp =>
            (IMarkdownStorage)sp.GetRequiredService<INoteRepository>());
        builder.Services.AddSingleton<JsonSettingsProvider>();
        builder.Services.AddSingleton<ILinkMetadataService>(sp =>
            sp.GetRequiredService<JsonSettingsProvider>());
        builder.Services.AddSingleton<IVaultSettings>(sp =>
            sp.GetRequiredService<JsonSettingsProvider>());
        builder.Services.AddSingleton<ITaskCache>(sp =>
            sp.GetRequiredService<JsonSettingsProvider>());
        builder.Services.AddSingleton<TaskScanService>();
        builder.Services.AddSingleton<ITaskRepository, TaskRepository>();

#if ANDROID
        builder.Services.AddSingleton<IAudioRecorder, AndroidAudioRecorderService>();
        var whisperModelDir = Path.Combine(FileSystem.AppDataDirectory, "whisper-models");
#else
        builder.Services.AddSingleton<IAudioRecorder, AudioRecorderService>();
        var whisperModelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WitteNog", "models");
#endif
        builder.Services.AddSingleton<ITranscriptionService>(_ => new WhisperTranscriptionService(whisperModelDir));
        builder.Services.AddSingleton<RecordingWorkflowService>();

        // Application (MediatR)
        builder.Services.AddLogging();
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(
                typeof(WitteNog.Application.Queries.GetNotesForDateQueryHandler).Assembly));

        // App services
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<VaultWatcherService>();
        builder.Services.AddSingleton<VaultContextService>();
        builder.Services.AddSingleton<FolderPickerService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
