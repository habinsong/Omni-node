namespace OmniNode.Middleware;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var config = AppConfig.LoadFromEnvironment();
        var paths = config.Paths;
        var providers = config.Providers;
        var gateway = config.Gateway;
        var context = config.Context;
        var pathResolver = DefaultStatePathResolver.CreateDefault();
        var runtimeSettings = new RuntimeSettings(config);
        var coreAuthToken = CoreAuthToken.Create();
        var coreClient = new UdsCoreClient(paths.CoreSocketPath, coreAuthToken);
        var copilotWrapper = new CopilotCliWrapper(
            providers.CopilotCliBinary,
            providers.CopilotDirectBinary,
            providers.CopilotModel,
            paths.CopilotUsageStatePath,
            Math.Max(context.LlmTimeoutSec, 120)
        );
        var codexWrapper = new CodexCliWrapper(
            providers.CodexBinary,
            runtimeSettings,
            paths.WorkspaceRootDir,
            providers.CodexModel,
            Math.Max(context.LlmTimeoutSec, 120)
        );
        var searchServices = ConfigureSearchServices(providers, context, runtimeSettings);
        var sandboxClient = new PythonSandboxClient(providers.PythonBinary, paths.SandboxExecutorPath);
        var doctorService = ConfigureDoctorService(
            config,
            pathResolver,
            runtimeSettings,
            coreClient,
            copilotWrapper,
            codexWrapper,
            searchServices,
            sandboxClient
        );

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var coreBootstrapper = new CoreProcessBootstrapper(paths, coreClient, coreAuthToken);
        await coreBootstrapper.EnsureRunningAsync(cts.Token);

        if (await DoctorCli.TryHandleAsync(args, doctorService, cts.Token))
        {
            return;
        }

        using var telegramClient = new TelegramClient(runtimeSettings);
        using var llmRouter = new LlmRouter(providers, paths, context, runtimeSettings);
        using var groqModelCatalog = new GroqModelCatalog(providers, context, runtimeSettings);
        using var cerebrasModelCatalog = new CerebrasModelCatalog(providers, runtimeSettings);
        var persistence = ConfigurePersistence(config, pathResolver);
        var codeRunner = new UniversalCodeRunner(paths.CodeRunsRootDir, config.CodeExecutionTimeoutSec, providers.PythonBinary);
        var providerRegistry = new ProviderRegistry(llmRouter, copilotWrapper, codexWrapper);
        var routingPolicyStore = new FileRoutingPolicyStore(pathResolver);
        var routingPolicyResolver = new RoutingPolicyResolver(routingPolicyStore);
        var projectContextServices = ConfigureProjectContextServices(pathResolver, config);
        var workflowServices = ConfigureWorkflowServices(
            config,
            pathResolver,
            persistence,
            llmRouter,
            routingPolicyResolver,
            projectContextServices.Loader
        );
        var refactorServices = ConfigureRefactorServices(config, pathResolver);
        var toolServices = ConfigureToolServices(config, runtimeSettings, persistence.ConversationStore);
        BootstrapMemoryIndex(paths);
        var auditLogger = new AuditLogger(config.AuditLogPath);
        var appServices = ConfigureApplicationServices(
            config,
            runtimeSettings,
            routingPolicyResolver,
            auditLogger,
            llmRouter,
            copilotWrapper,
            codexWrapper,
            telegramClient,
            coreClient,
            doctorService,
            workflowServices.Notebook,
            refactorServices.AnchorRead,
            refactorServices.AnchorEdit,
            refactorServices.DiffPreview,
            refactorServices.LspRefactor,
            refactorServices.AstGrepRefactor,
            projectContextServices.Loader,
            workflowServices.TaskGraph,
            workflowServices.Plan,
            workflowServices.PlanReview,
            workflowServices.TaskGraphCoordinator,
            persistence.ConversationStoreService,
            persistence.MemoryNoteStoreService,
            toolServices.MemorySearch,
            toolServices.MemoryGet,
            toolServices.SessionList,
            toolServices.SessionHistory,
            toolServices.SessionSend,
            toolServices.SessionSpawn,
            toolServices.WebFetch,
            toolServices.Browser,
            toolServices.Canvas,
            toolServices.Nodes
        );
        var llmPreferenceContext = new LlmPreferenceContext(config, copilotWrapper.GetSelectedModel());
        var executionContext = new ExecutionContext();
        var routineRegistry = new RoutineRegistry(persistence.RoutineStore);
        var commandService = new CommandService(
            config,
            llmRouter,
            groqModelCatalog,
            coreClient,
            telegramClient,
            runtimeSettings,
            providerRegistry,
            routingPolicyResolver,
            toolServices.Registry,
            searchServices.Gateway,
            searchServices.Guard,
            searchServices.AnswerComposer,
            toolServices.WebFetch,
            toolServices.MemorySearch,
            toolServices.MemoryGet,
            toolServices.SessionList,
            toolServices.SessionHistory,
            toolServices.SessionSend,
            toolServices.SessionSpawn,
            toolServices.Browser,
            toolServices.Canvas,
            toolServices.Nodes,
            copilotWrapper,
            codexWrapper,
            sandboxClient,
            persistence.MemoryNoteStoreService,
            persistence.ConversationStoreService,
            persistence.RunArtifactStore,
            codeRunner,
            auditLogger,
            doctorService,
            workflowServices.Plan,
            workflowServices.PlanReview,
            workflowServices.TaskGraph,
            workflowServices.TaskGraphCoordinator,
            projectContextServices.Loader,
            workflowServices.Notebook,
            refactorServices.AnchorRead,
            refactorServices.AnchorEdit,
            refactorServices.DiffPreview,
            refactorServices.LspRefactor,
            refactorServices.AstGrepRefactor,
            appServices.Doctor,
            appServices.Notebook,
            appServices.Settings,
            appServices.Refactor,
            appServices.Context,
            appServices.Cleanup,
            appServices.TaskGraph,
            appServices.Memory,
            appServices.Plan,
            appServices.Conversation,
            appServices.Tool,
            llmPreferenceContext,
            executionContext,
            routineRegistry
        );
        appServices.Memory.ConfigureCreateMemoryNoteDelegate(commandService.CreateMemoryNoteAsync);
        appServices.Conversation.ConfigureClearActiveSkillDelegate(commandService.ClearActiveSkillForConversation);
        appServices.Tool.ConfigureCronAndSearchDelegates(new ToolApplicationService.CronSearchDelegates(
            commandService.GetCronStatus,
            commandService.ListCronJobs,
            commandService.ListCronRuns,
            commandService.AddCronJob,
            commandService.UpdateCronJob,
            commandService.RunCronJobAsync,
            commandService.WakeCron,
            commandService.RemoveCronJob,
            commandService.SearchWebAsync
        ));
        var commandExecutionService = new CommandExecutionService(commandService, executionContext);
        var routineApplicationService = new RoutineApplicationService(commandService);
        var logicGraphRuntimeCoordinator = new LogicGraphRuntimeCoordinator(pathResolver);
        commandService.ConfigureLogicGraphRuntime(pathResolver, logicGraphRuntimeCoordinator);
        var runtimeServices = ConfigureRuntimeServices(
            config,
            pathResolver,
            llmRouter,
            groqModelCatalog,
            cerebrasModelCatalog,
            telegramClient,
            persistence.AuthSessionStore,
            commandExecutionService,
            runtimeSettings,
            appServices,
            routineApplicationService,
            commandService,
            workflowServices.TaskGraphCoordinator,
            auditLogger
        );

        Console.WriteLine($"[middleware] starting (ws={gateway.WebSocketPort}, core={paths.CoreSocketPath})");

        var webTask = runtimeServices.WebSocketGateway.RunAsync(cts.Token);
        if (gateway.EnableGatewayStartupProbe)
        {
            var startupProbe = new GatewayStartupProbe(gateway, paths, gateway.WebSocketPort);
            _ = startupProbe.RunAsync(cts.Token);
        }

        var telegramTask = runtimeServices.TelegramUpdateLoop.RunAsync(cts.Token);
        var firstCompleted = await Task.WhenAny(webTask, telegramTask);

        if (firstCompleted.IsFaulted)
        {
            cts.Cancel();
            await Task.WhenAll(webTask, telegramTask);
        }

        await Task.WhenAll(webTask, telegramTask);
        await workflowServices.TaskGraphCoordinator.StopAsync();
        await logicGraphRuntimeCoordinator.StopAsync();
    }

    private static PersistenceServices ConfigurePersistence(
        AppConfig config,
        IStatePathResolver pathResolver
    )
    {
        var paths = config.Paths;
        var sessionManager = new SessionManager(paths.AuthSessionStatePath);
        var memoryNoteStore = new MemoryNoteStore(paths.MemoryNotesRootDir);
        var conversationStore = new ConversationStore(paths.ConversationStatePath);
        return new PersistenceServices(
            sessionManager,
            memoryNoteStore,
            conversationStore,
            memoryNoteStore,
            conversationStore,
            sessionManager,
            new FileRoutineStore(paths.RoutineStatePath),
            new FilePlanStore(pathResolver),
            new FileTaskGraphStore(pathResolver),
            new FileRunArtifactStore(paths.RoutineRunsRootDir),
            new FileNotebookStore(pathResolver)
        );
    }

    private static SearchServices ConfigureSearchServices(
        ProviderOptions providers,
        ContextOptions context,
        RuntimeSettings runtimeSettings
    )
    {
        var geminiGroundedRetriever = new GeminiGroundedRetriever(providers, context, runtimeSettings);
        var searchEvidencePackBuilder = new DefaultSearchEvidencePackBuilder();
        var searchGuard = new DefaultSearchGuard();
        var searchGateway = new LegacyGeminiGroundingSearchGateway(geminiGroundedRetriever, searchEvidencePackBuilder);
        var searchAnswerComposer = new EvidenceFallbackSearchAnswerComposer(searchGateway, searchGuard);
        return new SearchServices(searchGateway, searchGuard, searchAnswerComposer);
    }

    private static DoctorService ConfigureDoctorService(
        AppConfig config,
        IStatePathResolver pathResolver,
        RuntimeSettings runtimeSettings,
        UdsCoreClient coreClient,
        CopilotCliWrapper copilotWrapper,
        CodexCliWrapper codexWrapper,
        SearchServices searchServices,
        PythonSandboxClient sandboxClient
    )
    {
        return new DoctorService(
            new IDoctorCheck[]
            {
                new CoreSocketDoctorCheck(config.Paths, coreClient),
                new WorkspaceDoctorCheck(config.Paths, pathResolver),
                new SandboxDoctorCheck(config.Providers, config.Paths, config.Doctor, sandboxClient),
                new SqliteDoctorCheck(),
                new ProviderSecretsDoctorCheck(runtimeSettings),
                new CodexDoctorCheck(codexWrapper),
                new CopilotDoctorCheck(copilotWrapper),
                new TelegramDoctorCheck(config.Security, runtimeSettings),
                new SearchPipelineDoctorCheck(
                    config.Providers,
                    config.Context,
                    runtimeSettings,
                    searchServices.Gateway,
                    searchServices.Guard,
                    searchServices.AnswerComposer
                )
            },
            new FileDoctorReportStore(pathResolver),
            config.Doctor
        );
    }

    private static ProjectContextServices ConfigureProjectContextServices(
        IStatePathResolver pathResolver,
        AppConfig config
    )
    {
        var instructionLoader = new AgentInstructionLoader(pathResolver, config);
        var skillManifestLoader = new SkillManifestLoader(pathResolver);
        var commandTemplateLoader = new CommandTemplateLoader(pathResolver);
        var projectContextLoader = new ProjectContextLoader(
            instructionLoader,
            skillManifestLoader,
            commandTemplateLoader
        );
        return new ProjectContextServices(
            instructionLoader,
            skillManifestLoader,
            commandTemplateLoader,
            projectContextLoader
        );
    }

    private static WorkflowServices ConfigureWorkflowServices(
        AppConfig config,
        IStatePathResolver pathResolver,
        PersistenceServices persistence,
        LlmRouter llmRouter,
        RoutingPolicyResolver routingPolicyResolver,
        ProjectContextLoader projectContextLoader
    )
    {
        var planService = new PlanService(
            persistence.PlanStore,
            llmRouter,
            routingPolicyResolver,
            config.Paths,
            persistence.ConversationStoreService,
            persistence.MemoryNoteStoreService,
            projectContextLoader
        );
        var planReviewService = new PlanReviewService(llmRouter, routingPolicyResolver, projectContextLoader);
        var taskGraphService = new TaskGraphService(persistence.TaskGraphStore, planService);
        var taskGraphCoordinator = new BackgroundTaskCoordinator(taskGraphService, pathResolver);
        var notebookService = new NotebookService(persistence.NotebookStore, projectContextLoader);
        return new WorkflowServices(
            planService,
            planReviewService,
            taskGraphService,
            taskGraphCoordinator,
            notebookService
        );
    }

    private static void BootstrapMemoryIndex(PathOptions paths)
    {
        var memoryIndexSchemaBootstrap = new MemoryIndexSchemaBootstrap(paths);
        try
        {
            var memoryIndexSnapshot = memoryIndexSchemaBootstrap.EnsureInitialized();
            var memoryIndexDocumentSync = new MemoryIndexDocumentSync(paths, memoryIndexSnapshot);
            var syncSnapshot = memoryIndexDocumentSync.SyncOnce();
            if (memoryIndexSnapshot.FtsAvailable)
            {
                Console.WriteLine($"[memory-index] ready db={memoryIndexSnapshot.DbPath} fts=available");
            }
            else
            {
                Console.WriteLine(
                    $"[memory-index] ready db={memoryIndexSnapshot.DbPath} fts=unavailable error={memoryIndexSnapshot.FtsError}"
                );
            }

            Console.WriteLine(
                "[memory-index] sync "
                + $"scanned={syncSnapshot.ScannedDocuments} "
                + $"indexed={syncSnapshot.IndexedDocuments} "
                + $"skipped={syncSnapshot.SkippedDocuments} "
                + $"removed={syncSnapshot.RemovedDocuments} "
                + $"memory={syncSnapshot.MemoryDocuments} "
                + $"sessions={syncSnapshot.SessionDocuments} "
                + $"project={syncSnapshot.ProjectDocuments} "
                + $"elapsedMs={syncSnapshot.ElapsedMs} "
                + $"fts={(syncSnapshot.FtsAvailable ? "available" : "unavailable")}"
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[memory-index] bootstrap or sync failed: {ex.Message}");
        }
    }

    private static RefactorServices ConfigureRefactorServices(AppConfig config, IStatePathResolver pathResolver)
    {
        var paths = config.Paths;
        var refactor = config.Refactor;
        var workspaceContainerRoot = Directory.GetParent(paths.WorkspaceRootDir)?.FullName ?? paths.WorkspaceRootDir;
        var refactorPreviewRoot = Path.Combine(workspaceContainerRoot, ".runtime", "refactor-preview");
        var refactorPreviewStore = new FileRefactorPreviewStore(
            pathResolver,
            refactor.RefactorPreviewTtlMinutes,
            refactorPreviewRoot
        );
        var refactorToolAvailability = new RefactorToolAvailability();
        var anchorReadService = new AnchorReadService(paths);
        var anchorEditService = new AnchorEditService();
        var diffPreviewService = new DiffPreviewService(paths, refactorPreviewStore);
        var lspRefactorService = new LspRefactorService(
            paths,
            refactor,
            refactorToolAvailability,
            anchorReadService,
            diffPreviewService
        );
        var astGrepRefactorService = new AstGrepRefactorService(
            refactor,
            refactorToolAvailability,
            anchorReadService,
            diffPreviewService
        );
        return new RefactorServices(
            anchorReadService,
            anchorEditService,
            diffPreviewService,
            lspRefactorService,
            astGrepRefactorService
        );
    }

    private static ToolServices ConfigureToolServices(
        AppConfig config,
        RuntimeSettings runtimeSettings,
        ConversationStore conversationStore
    )
    {
        var paths = config.Paths;
        var providers = config.Providers;
        var toolRegistry = new ToolRegistry(runtimeSettings);
        var webFetchTool = new WebFetchTool(config);
        var memorySearchTool = new MemorySearchTool(paths);
        var memoryGetTool = new MemoryGetTool(paths);
        var sessionListTool = new SessionListTool(conversationStore);
        var sessionHistoryTool = new SessionHistoryTool(conversationStore);
        var sessionSendTool = new SessionSendTool(conversationStore);
        var acpSessionBindingAdapter = new AcpSessionBindingAdapter(
            paths.WorkspaceRootDir,
            providers.CodexBinary,
            runtimeSettings
        );
        var sessionSpawnTool = new SessionSpawnTool(conversationStore, acpSessionBindingAdapter);
        var browserTool = new BrowserTool(config);
        var canvasTool = new CanvasTool(config);
        var nodesTool = new NodesTool(config);
        return new ToolServices(
            toolRegistry,
            webFetchTool,
            memorySearchTool,
            memoryGetTool,
            sessionListTool,
            sessionHistoryTool,
            sessionSendTool,
            sessionSpawnTool,
            browserTool,
            canvasTool,
            nodesTool
        );
    }

    private sealed record PersistenceServices(
        SessionManager SessionManager,
        MemoryNoteStore MemoryNoteStore,
        ConversationStore ConversationStore,
        IMemoryNoteStore MemoryNoteStoreService,
        IConversationStore ConversationStoreService,
        IAuthSessionStore AuthSessionStore,
        IRoutineStore RoutineStore,
        FilePlanStore PlanStore,
        FileTaskGraphStore TaskGraphStore,
        IRunArtifactStore RunArtifactStore,
        FileNotebookStore NotebookStore
    );

    private sealed record SearchServices(
        LegacyGeminiGroundingSearchGateway Gateway,
        DefaultSearchGuard Guard,
        EvidenceFallbackSearchAnswerComposer AnswerComposer
    );

    private sealed record RefactorServices(
        AnchorReadService AnchorRead,
        AnchorEditService AnchorEdit,
        DiffPreviewService DiffPreview,
        LspRefactorService LspRefactor,
        AstGrepRefactorService AstGrepRefactor
    );

    private sealed record ProjectContextServices(
        AgentInstructionLoader InstructionLoader,
        SkillManifestLoader SkillManifestLoader,
        CommandTemplateLoader CommandTemplateLoader,
        ProjectContextLoader Loader
    );

    private sealed record WorkflowServices(
        PlanService Plan,
        PlanReviewService PlanReview,
        TaskGraphService TaskGraph,
        BackgroundTaskCoordinator TaskGraphCoordinator,
        NotebookService Notebook
    );

    private sealed record ToolServices(
        ToolRegistry Registry,
        WebFetchTool WebFetch,
        MemorySearchTool MemorySearch,
        MemoryGetTool MemoryGet,
        SessionListTool SessionList,
        SessionHistoryTool SessionHistory,
        SessionSendTool SessionSend,
        SessionSpawnTool SessionSpawn,
        BrowserTool Browser,
        CanvasTool Canvas,
        NodesTool Nodes
    );

    private static ApplicationServices ConfigureApplicationServices(
        AppConfig config,
        RuntimeSettings runtimeSettings,
        RoutingPolicyResolver routingPolicyResolver,
        AuditLogger auditLogger,
        LlmRouter llmRouter,
        CopilotCliWrapper copilotWrapper,
        CodexCliWrapper codexWrapper,
        TelegramClient telegramClient,
        UdsCoreClient coreClient,
        DoctorService doctorService,
        NotebookService notebookService,
        AnchorReadService anchorReadService,
        AnchorEditService anchorEditService,
        DiffPreviewService diffPreviewService,
        LspRefactorService lspRefactorService,
        AstGrepRefactorService astGrepRefactorService,
        ProjectContextLoader projectContextLoader,
        TaskGraphService taskGraphService,
        PlanService planService,
        PlanReviewService planReviewService,
        BackgroundTaskCoordinator taskGraphCoordinator,
        IConversationStore conversationStore,
        IMemoryNoteStore memoryNoteStore,
        MemorySearchTool memorySearchTool,
        MemoryGetTool memoryGetTool,
        SessionListTool sessionListTool,
        SessionHistoryTool sessionHistoryTool,
        SessionSendTool sessionSendTool,
        SessionSpawnTool sessionSpawnTool,
        WebFetchTool webFetchTool,
        BrowserTool browserTool,
        CanvasTool canvasTool,
        NodesTool nodesTool
    )
    {
        var paths = config.Paths;
        var cleanupService = new CleanupService(paths);
        var doctorApplicationService = new DoctorApplicationService(doctorService, paths);
        var notebookApplicationService = new NotebookApplicationService(notebookService);
        var settingsApplicationService = new SettingsApplicationService(
            runtimeSettings,
            routingPolicyResolver,
            auditLogger,
            llmRouter,
            copilotWrapper,
            codexWrapper,
            telegramClient,
            coreClient
        );
        var refactorApplicationService = new RefactorApplicationService(
            anchorReadService,
            anchorEditService,
            diffPreviewService,
            lspRefactorService,
            astGrepRefactorService,
            auditLogger,
            paths
        );
        var contextApplicationService = new ContextApplicationService(projectContextLoader);
        var taskGraphApplicationService = new TaskGraphApplicationService(
            taskGraphService,
            planService,
            taskGraphCoordinator
        );
        var memoryApplicationService = new MemoryApplicationService(
            conversationStore,
            memoryNoteStore,
            auditLogger,
            paths,
            memorySearchTool,
            memoryGetTool
        );
        var planApplicationService = new PlanApplicationService(
            planService,
            planReviewService,
            taskGraphService,
            taskGraphCoordinator
        );
        var conversationApplicationService = new ConversationApplicationService(
            conversationStore,
            memoryNoteStore,
            auditLogger,
            paths,
            memorySearchTool
        );
        var toolApplicationService = new ToolApplicationService(
            sessionListTool,
            sessionHistoryTool,
            sessionSendTool,
            sessionSpawnTool,
            webFetchTool,
            browserTool,
            canvasTool,
            nodesTool,
            cleanupService
        );

        return new ApplicationServices(
            doctorApplicationService,
            notebookApplicationService,
            settingsApplicationService,
            refactorApplicationService,
            contextApplicationService,
            cleanupService,
            taskGraphApplicationService,
            memoryApplicationService,
            planApplicationService,
            conversationApplicationService,
            toolApplicationService
        );
    }

    private static RuntimeServices ConfigureRuntimeServices(
        AppConfig config,
        IStatePathResolver pathResolver,
        LlmRouter llmRouter,
        GroqModelCatalog groqModelCatalog,
        CerebrasModelCatalog cerebrasModelCatalog,
        TelegramClient telegramClient,
        IAuthSessionStore authSessionStore,
        CommandExecutionService commandExecutionService,
        RuntimeSettings runtimeSettings,
        ApplicationServices appServices,
        RoutineApplicationService routineApplicationService,
        CommandService commandService,
        BackgroundTaskCoordinator taskGraphCoordinator,
        AuditLogger auditLogger
    )
    {
        var logicApplicationService = new LogicApplicationService(commandService);
        var chatApplicationService = new ChatApplicationService(commandService);
        var codingApplicationService = new CodingApplicationService(commandService);
        taskGraphCoordinator.ConfigureExecutors(codingApplicationService, commandExecutionService);
        return new RuntimeServices(
            new WebSocketGateway(
                config.Providers,
                config.Gateway,
                config.Paths,
                config.Security,
                config.WebSocketPort,
                authSessionStore,
                telegramClient,
                commandExecutionService,
                appServices.Settings,
                appServices.Conversation,
                appServices.Memory,
                appServices.Tool,
                routineApplicationService,
                logicApplicationService,
                appServices.Doctor,
                appServices.Plan,
                appServices.TaskGraph,
                appServices.Refactor,
                appServices.Context,
                appServices.Notebook,
                chatApplicationService,
                codingApplicationService,
                llmRouter,
                groqModelCatalog,
                cerebrasModelCatalog,
                new GuardRetryTimelineStore(config.GuardRetryTimelineStatePath),
                auditLogger
            ),
            new TelegramUpdateLoop(
                telegramClient,
                commandExecutionService,
                config.Security,
                runtimeSettings,
                new TelegramPollingStateStore(
                    pathResolver.ResolveStateFilePath("telegram_update_offset.txt"),
                    pathResolver.ResolveStateFilePath("telegram_update_loop.lock")
                ),
                new FileTelegramReplyOutboxStore(pathResolver)
            )
        );
    }

    private sealed record ApplicationServices(
        DoctorApplicationService Doctor,
        NotebookApplicationService Notebook,
        SettingsApplicationService Settings,
        RefactorApplicationService Refactor,
        ContextApplicationService Context,
        CleanupService Cleanup,
        TaskGraphApplicationService TaskGraph,
        MemoryApplicationService Memory,
        PlanApplicationService Plan,
        ConversationApplicationService Conversation,
        ToolApplicationService Tool
    );

    private sealed record RuntimeServices(
        WebSocketGateway WebSocketGateway,
        TelegramUpdateLoop TelegramUpdateLoop
    );
}
