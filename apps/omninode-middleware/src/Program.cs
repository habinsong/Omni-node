namespace OmniNode.Middleware;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var config = AppConfig.LoadFromEnvironment();
        var pathResolver = DefaultStatePathResolver.CreateDefault();
        var runtimeSettings = new RuntimeSettings(config);
        var coreClient = new UdsCoreClient(config.CoreSocketPath);
        var copilotWrapper = new CopilotCliWrapper(
            config.CopilotCliBinary,
            config.CopilotDirectBinary,
            config.CopilotModel,
            config.CopilotUsageStatePath,
            Math.Max(config.LlmTimeoutSec, 120)
        );
        var codexWrapper = new CodexCliWrapper(
            config.CodexBinary,
            runtimeSettings,
            config.WorkspaceRootDir,
            config.CodexModel,
            Math.Max(config.LlmTimeoutSec, 120)
        );
        var geminiGroundedRetriever = new GeminiGroundedRetriever(config, runtimeSettings);
        var searchEvidencePackBuilder = new DefaultSearchEvidencePackBuilder();
        var searchGuard = new DefaultSearchGuard();
        var searchGateway = new LegacyGeminiGroundingSearchGateway(geminiGroundedRetriever, searchEvidencePackBuilder);
        var searchAnswerComposer = new EvidenceFallbackSearchAnswerComposer(searchGateway, searchGuard);
        var sandboxClient = new PythonSandboxClient(config.PythonBinary, config.SandboxExecutorPath);
        var doctorService = new DoctorService(
            new IDoctorCheck[]
            {
                new CoreSocketDoctorCheck(config, coreClient),
                new WorkspaceDoctorCheck(config, pathResolver),
                new SandboxDoctorCheck(config, sandboxClient),
                new SqliteDoctorCheck(),
                new ProviderSecretsDoctorCheck(runtimeSettings),
                new CodexDoctorCheck(codexWrapper),
                new CopilotDoctorCheck(copilotWrapper),
                new TelegramDoctorCheck(config, runtimeSettings),
                new SearchPipelineDoctorCheck(config, runtimeSettings, searchGateway, searchGuard, searchAnswerComposer)
            },
            new FileDoctorReportStore(pathResolver),
            config
        );

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var coreBootstrapper = new CoreProcessBootstrapper(config, coreClient);
        await coreBootstrapper.EnsureRunningAsync(cts.Token);

        if (await DoctorCli.TryHandleAsync(args, doctorService, cts.Token))
        {
            return;
        }

        using var telegramClient = new TelegramClient(runtimeSettings);
        var sessionManager = new SessionManager(config.AuthSessionStatePath);
        using var llmRouter = new LlmRouter(config, runtimeSettings);
        using var groqModelCatalog = new GroqModelCatalog(config, runtimeSettings);
        var memoryNoteStore = new MemoryNoteStore(config.MemoryNotesRootDir);
        var conversationStore = new ConversationStore(config.ConversationStatePath);
        IMemoryNoteStore memoryNoteStoreService = memoryNoteStore;
        IConversationStore conversationStoreService = conversationStore;
        IAuthSessionStore authSessionStore = sessionManager;
        IRoutineStore routineStore = new FileRoutineStore(config.RoutineStatePath);
        var planStore = new FilePlanStore(pathResolver);
        var taskGraphStore = new FileTaskGraphStore(pathResolver);
        IRunArtifactStore runArtifactStore = new FileRunArtifactStore(config.RoutineRunsRootDir);
        var codeRunner = new UniversalCodeRunner(config.CodeRunsRootDir, config.CodeExecutionTimeoutSec, config.PythonBinary);
        var providerRegistry = new ProviderRegistry(llmRouter, copilotWrapper, codexWrapper);
        var routingPolicyStore = new FileRoutingPolicyStore(pathResolver);
        var routingPolicyResolver = new RoutingPolicyResolver(routingPolicyStore);
        var instructionLoader = new AgentInstructionLoader(pathResolver, config);
        var skillManifestLoader = new SkillManifestLoader(pathResolver);
        var commandTemplateLoader = new CommandTemplateLoader(pathResolver);
        var projectContextLoader = new ProjectContextLoader(
            instructionLoader,
            skillManifestLoader,
            commandTemplateLoader
        );
        var planService = new PlanService(
            planStore,
            llmRouter,
            routingPolicyResolver,
            config,
            conversationStoreService,
            memoryNoteStoreService,
            projectContextLoader
        );
        var planReviewService = new PlanReviewService(llmRouter, routingPolicyResolver, projectContextLoader);
        var taskGraphService = new TaskGraphService(taskGraphStore, planService);
        var taskGraphCoordinator = new BackgroundTaskCoordinator(taskGraphService, pathResolver);
        var notebookStore = new FileNotebookStore(pathResolver);
        var notebookService = new NotebookService(notebookStore, projectContextLoader);
        var workspaceContainerRoot = Directory.GetParent(config.WorkspaceRootDir)?.FullName ?? config.WorkspaceRootDir;
        var refactorPreviewRoot = Path.Combine(workspaceContainerRoot, ".runtime", "refactor-preview");
        var refactorPreviewStore = new FileRefactorPreviewStore(
            pathResolver,
            config.RefactorPreviewTtlMinutes,
            refactorPreviewRoot
        );
        var refactorToolAvailability = new RefactorToolAvailability();
        var anchorReadService = new AnchorReadService(config);
        var anchorEditService = new AnchorEditService();
        var diffPreviewService = new DiffPreviewService(config, refactorPreviewStore);
        var lspRefactorService = new LspRefactorService(
            config,
            refactorToolAvailability,
            anchorReadService,
            diffPreviewService
        );
        var astGrepRefactorService = new AstGrepRefactorService(
            config,
            refactorToolAvailability,
            anchorReadService,
            diffPreviewService
        );
        var toolRegistry = new ToolRegistry(runtimeSettings);
        var webFetchTool = new WebFetchTool(config);
        var memorySearchTool = new MemorySearchTool(config);
        var memoryGetTool = new MemoryGetTool(config);
        var sessionListTool = new SessionListTool(conversationStore);
        var sessionHistoryTool = new SessionHistoryTool(conversationStore);
        var sessionSendTool = new SessionSendTool(conversationStore);
        var acpSessionBindingAdapter = new AcpSessionBindingAdapter(
            config.WorkspaceRootDir,
            config.CodexBinary,
            runtimeSettings
        );
        var sessionSpawnTool = new SessionSpawnTool(conversationStore, acpSessionBindingAdapter);
        var browserTool = new BrowserTool(config);
        var canvasTool = new CanvasTool(config);
        var nodesTool = new NodesTool(config);
        var memoryIndexSchemaBootstrap = new MemoryIndexSchemaBootstrap(config);
        try
        {
            var memoryIndexSnapshot = memoryIndexSchemaBootstrap.EnsureInitialized();
            var memoryIndexDocumentSync = new MemoryIndexDocumentSync(config, memoryIndexSnapshot);
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
                + $"fts={(syncSnapshot.FtsAvailable ? "available" : "unavailable")}"
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[memory-index] bootstrap or sync failed: {ex.Message}");
        }
        var auditLogger = new AuditLogger(config.AuditLogPath);
        var commandService = new CommandService(
            config,
            llmRouter,
            groqModelCatalog,
            coreClient,
            telegramClient,
            runtimeSettings,
            providerRegistry,
            routingPolicyResolver,
            toolRegistry,
            searchGateway,
            searchGuard,
            searchAnswerComposer,
            webFetchTool,
            memorySearchTool,
            memoryGetTool,
            sessionListTool,
            sessionHistoryTool,
            sessionSendTool,
            sessionSpawnTool,
            browserTool,
            canvasTool,
            nodesTool,
            copilotWrapper,
            codexWrapper,
            sandboxClient,
            memoryNoteStoreService,
            conversationStoreService,
            routineStore,
            runArtifactStore,
            codeRunner,
            auditLogger,
            doctorService,
            planService,
            planReviewService,
            taskGraphService,
            taskGraphCoordinator,
            projectContextLoader,
            notebookService,
            anchorReadService,
            anchorEditService,
            diffPreviewService,
            lspRefactorService,
            astGrepRefactorService
        );
        var commandExecutionService = new CommandExecutionService(commandService);
        var settingsApplicationService = new SettingsApplicationService(commandService);
        var conversationApplicationService = new ConversationApplicationService(commandService);
        var memoryApplicationService = new MemoryApplicationService(commandService);
        var toolApplicationService = new ToolApplicationService(commandService);
        var routineApplicationService = new RoutineApplicationService(commandService);
        var logicGraphRuntimeCoordinator = new LogicGraphRuntimeCoordinator(pathResolver);
        commandService.ConfigureLogicGraphRuntime(pathResolver, logicGraphRuntimeCoordinator);
        var logicApplicationService = new LogicApplicationService(commandService);
        var doctorApplicationService = new DoctorApplicationService(commandService);
        var planApplicationService = new PlanApplicationService(commandService);
        var taskGraphApplicationService = new TaskGraphApplicationService(commandService);
        var refactorApplicationService = new RefactorApplicationService(commandService);
        var contextApplicationService = new ContextApplicationService(commandService);
        var notebookApplicationService = new NotebookApplicationService(commandService);
        var chatApplicationService = new ChatApplicationService(commandService);
        var codingApplicationService = new CodingApplicationService(commandService);
        taskGraphCoordinator.ConfigureExecutors(codingApplicationService, commandExecutionService);

        var webSocketGateway = new WebSocketGateway(
            config,
            config.WebSocketPort,
            authSessionStore,
            telegramClient,
            commandExecutionService,
            settingsApplicationService,
            conversationApplicationService,
            memoryApplicationService,
            toolApplicationService,
            routineApplicationService,
            logicApplicationService,
            doctorApplicationService,
            planApplicationService,
            taskGraphApplicationService,
            refactorApplicationService,
            contextApplicationService,
            notebookApplicationService,
            chatApplicationService,
            codingApplicationService,
            llmRouter,
            groqModelCatalog,
            new GuardRetryTimelineStore(config.GuardRetryTimelineStatePath),
            auditLogger
        );

        var telegramPollingStateStore = new TelegramPollingStateStore(
            pathResolver.ResolveStateFilePath("telegram_update_offset.txt"),
            pathResolver.ResolveStateFilePath("telegram_update_loop.lock")
        );
        var telegramReplyOutboxStore = new FileTelegramReplyOutboxStore(pathResolver);
        var telegramUpdateLoop = new TelegramUpdateLoop(
            telegramClient,
            commandExecutionService,
            config,
            telegramPollingStateStore,
            telegramReplyOutboxStore
        );

        Console.WriteLine($"[middleware] starting (ws={config.WebSocketPort}, core={config.CoreSocketPath})");

        var webTask = webSocketGateway.RunAsync(cts.Token);
        if (config.EnableGatewayStartupProbe)
        {
            var startupProbe = new GatewayStartupProbe(config, config.WebSocketPort);
            _ = startupProbe.RunAsync(cts.Token);
        }

        var telegramTask = telegramUpdateLoop.RunAsync(cts.Token);
        var firstCompleted = await Task.WhenAny(webTask, telegramTask);

        if (firstCompleted.IsFaulted)
        {
            cts.Cancel();
            await Task.WhenAll(webTask, telegramTask);
        }

        await Task.WhenAll(webTask, telegramTask);
        await taskGraphCoordinator.StopAsync();
        await logicGraphRuntimeCoordinator.StopAsync();
    }
}
