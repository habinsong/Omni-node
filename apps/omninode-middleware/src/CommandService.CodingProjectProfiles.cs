using System.Net;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private sealed record CodingProjectProfile(
        string Language,
        string ProjectKind,
        IReadOnlyList<string> EntryFiles,
        string BuildTool,
        string TestTool,
        IReadOnlyList<string> RunCommands,
        bool PrefersMultiFile
    );

    private static CodingProjectProfile ResolveCodingProjectProfile(
        string? objective,
        string? languageHint,
        IReadOnlyList<string>? requestedPaths = null
    )
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        var language = NormalizeCodingLanguageHintPreservingAuto(languageHint);
        if (language == "auto" && !string.IsNullOrWhiteSpace(explicitLanguage))
        {
            language = explicitLanguage;
        }

        if (language == "auto" && requestedPaths != null)
        {
            var pathLanguage = requestedPaths
                .Select(path => CodingLanguagePolicy.GuessLanguageFromPath(path, "auto"))
                .FirstOrDefault(value => value != "auto");
            if (!string.IsNullOrWhiteSpace(pathLanguage))
            {
                language = pathLanguage;
            }
        }

        if (language == "auto")
        {
            language = ResolveInitialCodingLanguage(languageHint, objective ?? string.Empty);
        }

        var singleFile = IsExplicitSingleFileSimpleTask(objective, language, requestedPaths);
        var apiLike = ContainsAny(text, "api", "rest", "서버", "backend", "백엔드", "http server", "web server");
        var cliLike = ContainsAny(text, "cli", "command line", "명령줄", "커맨드", "인자", "stdin", "옵션");
        var libraryLike = ContainsAny(text, "라이브러리", "library", "패키지", "package", "모듈", "module", "sdk");
        var testLike = ContainsAny(text, "테스트", "test", "unit test", "pytest", "jest", "테스트 포함");
        var dataLike = ContainsAny(text, "데이터", "csv", "분석", "pandas", "시각화", "차트");
        var gameLike = IsGameLikeCodingTask(objective ?? string.Empty, language);
        var frontendLike = IsFrontendLikeCodingTask(objective ?? string.Empty, language);

        return language switch
        {
            "react-vite" => new("react-vite", "react-vite", new[] { "src/main.tsx", "index.html" }, "npm run build", "npm test 또는 npm run build", new[] { "npm run dev" }, true),
            "typescript" when frontendLike => new("typescript", "react-vite", new[] { "src/main.tsx", "index.html" }, "npm run build", "npm test 또는 npm run build", new[] { "npm run dev" }, true),
            "typescript" => new("typescript", "typescript-node", new[] { "src/index.ts" }, "npm run build 또는 npx tsc --noEmit", "npm test", new[] { "npm start", "node dist/index.js" }, !singleFile),
            "javascript" when frontendLike => new("javascript", "node-web", new[] { "index.html", "src/main.js" }, "npm run build 또는 정적 HTML smoke", "Playwright smoke", new[] { "npm run dev" }, true),
            "javascript" => new("javascript", "node-cli", new[] { "src/index.js" }, "npm run build 또는 node --check", "npm test", new[] { "npm start", "node src/index.js" }, !singleFile),
            "python" when gameLike => new("python", "python-game", new[] { "main.py", "requirements.txt" }, "python3 -m compileall -q .", "OMNI_HEADLESS_TEST=1 smoke 실행", new[] { "python3 main.py" }, true),
            "python" when libraryLike || testLike => new("python", "python-package", new[] { "pyproject.toml", "src/app/__init__.py", "tests/test_app.py" }, "python3 -m compileall -q .", "python3 -m pytest -q", new[] { "python3 -m app" }, true),
            "python" when cliLike || dataLike || apiLike => new("python", cliLike ? "python-cli" : "python-package", new[] { "main.py", "requirements.txt" }, "python3 -m compileall -q .", "python3 -m pytest -q", new[] { "python3 main.py" }, !singleFile),
            "python" => new("python", singleFile ? "python-script" : "python-package", singleFile ? new[] { "main.py" } : new[] { "main.py", "requirements.txt" }, "python3 -m compileall -q .", "python3 -m pytest -q", new[] { "python3 main.py" }, !singleFile),
            "go" => new("go", "go-module", new[] { "go.mod", "main.go" }, "go build ./...", "go test ./...", new[] { "go run ." }, true),
            "rust" => new("rust", "rust-cargo", new[] { "Cargo.toml", "src/main.rs" }, "cargo build", "cargo test", new[] { "cargo run" }, true),
            "java" => new("java", singleFile ? "java-single" : "java-gradle", singleFile ? new[] { "Main.java" } : new[] { "build.gradle", "src/main/java/Main.java" }, singleFile ? "javac Main.java" : "gradle build", singleFile ? "java Main" : "gradle test", singleFile ? new[] { "java Main" } : new[] { "gradle run" }, !singleFile),
            "kotlin" => new("kotlin", singleFile ? "kotlin-single" : "kotlin-gradle", singleFile ? new[] { "Main.kt" } : new[] { "build.gradle.kts", "src/main/kotlin/Main.kt" }, singleFile ? "kotlinc Main.kt -include-runtime -d app.jar" : "gradle build", "gradle test", singleFile ? new[] { "java -jar app.jar" } : new[] { "gradle run" }, !singleFile),
            "csharp" => new("csharp", "csharp-project", new[] { "App.csproj", "Program.cs" }, "dotnet build", "dotnet test 또는 dotnet build", new[] { "dotnet run --project App.csproj" }, true),
            "cpp" => new("cpp", "cpp-cmake", new[] { "CMakeLists.txt", "src/main.cpp", "include/app.hpp" }, "cmake -S . -B build && cmake --build build", "ctest --test-dir build", new[] { "./build/app" }, true),
            "c" => new("c", "c-cmake", new[] { "CMakeLists.txt", "src/main.c", "include/app.h" }, "cmake -S . -B build && cmake --build build", "ctest --test-dir build", new[] { "./build/app" }, true),
            "php" => new("php", "php-cli", new[] { "composer.json", "src/App.php", "bin/app.php" }, "composer install", "composer test 또는 php -l", new[] { "php bin/app.php" }, true),
            "ruby" => new("ruby", "ruby-cli", new[] { "Gemfile", "lib/app.rb", "bin/app" }, "bundle install", "bundle exec ruby -c lib/app.rb", new[] { "ruby bin/app" }, true),
            "swift" => new("swift", "swift-package", new[] { "Package.swift", "Sources/App/main.swift" }, "swift build", "swift test", new[] { "swift run" }, true),
            "bash" => new("bash", "bash-cli", new[] { "scripts/app.sh" }, "bash -n scripts/app.sh", "bash scripts/app.sh --help", new[] { "bash scripts/app.sh" }, !singleFile),
            "html" or "css" => new(language, "static-web", new[] { "index.html", "styles.css", "app.js" }, "정적 HTML 검증", "Playwright smoke", new[] { "open index.html" }, true),
            _ => new(language == "auto" ? "python" : language, "auto-project", new[] { "README.md", "src/main" }, "프로젝트별 빌드", "프로젝트별 테스트", Array.Empty<string>(), true)
        };
    }

    private static bool IsExplicitSingleFileSimpleTask(
        string? objective,
        string? languageHint,
        IReadOnlyList<string>? requestedPaths
    )
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var expectedOutputLines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(text);
        var pathCount = requestedPaths?.Count ?? 0;
        var singleFileIntent = CodingFallbackPolicy.HasSingleFileIntent(text)
            || ContainsAny(text, "single file", "one file", "파일 하나", "파일 한개", "단일 파일");
        var simpleStdout = expectedOutputLines.Count > 0
            || ContainsAny(text, "출력", "print", "echo", "stdout");
        var projectSignals = ContainsAny(
            text,
            "프로젝트",
            "project",
            "웹앱",
            "web app",
            "api",
            "서버",
            "게임",
            "테스트",
            "패키지",
            "라이브러리",
            "여러 파일",
            "multiple files",
            "multi-file"
        );

        if (projectSignals)
        {
            return false;
        }

        return singleFileIntent && simpleStdout && pathCount <= 1;
    }
}
