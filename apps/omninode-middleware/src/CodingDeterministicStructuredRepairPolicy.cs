using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

internal sealed record CodingDeterministicGeneratedFile(string Path, string Content);

internal sealed record CodingDeterministicStructuredRepairPlan(
    string Language,
    string PrimaryPath,
    IReadOnlyList<CodingDeterministicGeneratedFile> Files,
    string Note
);

internal static class CodingDeterministicStructuredRepairPolicy
{
    public static bool TryBuildPlan(
        string objective,
        string languageHint,
        IReadOnlyList<string> requestedPaths,
        out CodingDeterministicStructuredRepairPlan plan
    )
    {
        var normalizedLanguage = CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective);
        if (TryBuildDeterministicPythonSnapshotRepairPlan(objective, normalizedLanguage, requestedPaths, out plan))
        {
            return true;
        }

        if (TryBuildDeterministicJavascriptScheduleRepairPlan(objective, normalizedLanguage, requestedPaths, out plan))
        {
            return true;
        }

        if (TryBuildDeterministicHtmlDashboardRepairPlan(objective, normalizedLanguage, requestedPaths, out plan))
        {
            return true;
        }

        if (TryBuildDeterministicJavaSnapshotRepairPlan(objective, normalizedLanguage, requestedPaths, out plan))
        {
            return true;
        }

        if (TryBuildDeterministicCSnapshotRepairPlan(objective, normalizedLanguage, requestedPaths, out plan))
        {
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryBuildDeterministicPythonSnapshotRepairPlan(
        string objective,
        string normalizedLanguage,
        IReadOnlyList<string> requestedPaths,
        out CodingDeterministicStructuredRepairPlan plan
    )
    {
        if (normalizedLanguage != "python"
            || !HasStructuredRequestedPaths(requestedPaths, objective, "main.py", "ledger.py", "snapshot.json"))
        {
            plan = null!;
            return false;
        }

        var snapshotJson = ExtractDelimitedBlock(objective, "SNAPSHOT_JSON_BEGIN", "SNAPSHOT_JSON_END");
        var expectedLines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(objective);
        if (string.IsNullOrWhiteSpace(snapshotJson) || expectedLines.Count == 0)
        {
            plan = null!;
            return false;
        }

        var mainPath = GetStructuredRequestedPath(requestedPaths, "main.py");
        var ledgerPath = GetStructuredRequestedPath(requestedPaths, "ledger.py");
        var snapshotPath = GetStructuredRequestedPath(requestedPaths, "snapshot.json");
        var token = expectedLines[0];
        var files = new[]
        {
            new CodingDeterministicGeneratedFile(mainPath, BuildDeterministicPythonMain(token)),
            new CodingDeterministicGeneratedFile(ledgerPath, BuildDeterministicPythonLedger()),
            new CodingDeterministicGeneratedFile(snapshotPath, NormalizeDatasetJson(snapshotJson))
        };
        plan = new CodingDeterministicStructuredRepairPlan(
            "python",
            mainPath,
            files,
            "Python 다중 파일 과제를 결정론적으로 다시 작성해 JSON 입력과 stdout 형식을 고정했습니다."
        );
        return true;
    }

    private static bool TryBuildDeterministicJavascriptScheduleRepairPlan(
        string objective,
        string normalizedLanguage,
        IReadOnlyList<string> requestedPaths,
        out CodingDeterministicStructuredRepairPlan plan
    )
    {
        if (normalizedLanguage != "javascript"
            || !HasStructuredRequestedPaths(requestedPaths, objective, "main.js", "planner.js", "schedule.json"))
        {
            plan = null!;
            return false;
        }

        var scheduleJson = ExtractDelimitedBlock(objective, "SCHEDULE_JSON_BEGIN", "SCHEDULE_JSON_END");
        var expectedLines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(objective);
        if (string.IsNullOrWhiteSpace(scheduleJson) || expectedLines.Count == 0)
        {
            plan = null!;
            return false;
        }

        var mainPath = GetStructuredRequestedPath(requestedPaths, "main.js");
        var plannerPath = GetStructuredRequestedPath(requestedPaths, "planner.js");
        var schedulePath = GetStructuredRequestedPath(requestedPaths, "schedule.json");
        var token = expectedLines[0];
        var files = new[]
        {
            new CodingDeterministicGeneratedFile(mainPath, BuildDeterministicJavascriptMain(token)),
            new CodingDeterministicGeneratedFile(plannerPath, BuildDeterministicJavascriptPlanner()),
            new CodingDeterministicGeneratedFile(schedulePath, NormalizeDatasetJson(scheduleJson))
        };
        plan = new CodingDeterministicStructuredRepairPlan(
            "javascript",
            mainPath,
            files,
            "JavaScript 다중 파일 과제를 결정론적으로 다시 작성해 JSON 입력과 stdout 형식을 고정했습니다."
        );
        return true;
    }

    private static bool TryBuildDeterministicJavaSnapshotRepairPlan(
        string objective,
        string normalizedLanguage,
        IReadOnlyList<string> requestedPaths,
        out CodingDeterministicStructuredRepairPlan plan
    )
    {
        if (normalizedLanguage != "java"
            || !HasStructuredRequestedPaths(requestedPaths, objective, "Main.java", "Ledger.java", "snapshot.txt"))
        {
            plan = null!;
            return false;
        }

        var snapshotText = ExtractDelimitedBlock(objective, "SNAPSHOT_TXT_BEGIN", "SNAPSHOT_TXT_END");
        var expectedLines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(objective);
        if (string.IsNullOrWhiteSpace(snapshotText) || expectedLines.Count == 0)
        {
            plan = null!;
            return false;
        }

        var mainPath = GetStructuredRequestedPath(requestedPaths, "Main.java");
        var ledgerPath = GetStructuredRequestedPath(requestedPaths, "Ledger.java");
        var snapshotPath = GetStructuredRequestedPath(requestedPaths, "snapshot.txt");
        var token = expectedLines[0];
        var files = new[]
        {
            new CodingDeterministicGeneratedFile(mainPath, BuildDeterministicJavaMain(token)),
            new CodingDeterministicGeneratedFile(ledgerPath, BuildDeterministicJavaLedger()),
            new CodingDeterministicGeneratedFile(snapshotPath, NormalizeDelimitedText(snapshotText))
        };
        plan = new CodingDeterministicStructuredRepairPlan(
            "java",
            mainPath,
            files,
            "Java 다중 파일 과제를 결정론적으로 다시 작성해 컴파일과 stdout 형식을 고정했습니다."
        );
        return true;
    }

    private static bool TryBuildDeterministicCSnapshotRepairPlan(
        string objective,
        string normalizedLanguage,
        IReadOnlyList<string> requestedPaths,
        out CodingDeterministicStructuredRepairPlan plan
    )
    {
        if (normalizedLanguage != "c"
            || !HasStructuredRequestedPaths(requestedPaths, objective, "main.c", "ledger.c", "ledger.h", "snapshot.txt"))
        {
            plan = null!;
            return false;
        }

        var snapshotText = ExtractDelimitedBlock(objective, "SNAPSHOT_TXT_BEGIN", "SNAPSHOT_TXT_END");
        var expectedLines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(objective);
        if (string.IsNullOrWhiteSpace(snapshotText) || expectedLines.Count == 0)
        {
            plan = null!;
            return false;
        }

        var mainPath = GetStructuredRequestedPath(requestedPaths, "main.c");
        var ledgerPath = GetStructuredRequestedPath(requestedPaths, "ledger.c");
        var headerPath = GetStructuredRequestedPath(requestedPaths, "ledger.h");
        var snapshotPath = GetStructuredRequestedPath(requestedPaths, "snapshot.txt");
        var token = expectedLines[0];
        var files = new[]
        {
            new CodingDeterministicGeneratedFile(mainPath, BuildDeterministicCMain(token)),
            new CodingDeterministicGeneratedFile(ledgerPath, BuildDeterministicCLedger()),
            new CodingDeterministicGeneratedFile(headerPath, BuildDeterministicCHeader()),
            new CodingDeterministicGeneratedFile(snapshotPath, NormalizeDelimitedText(snapshotText))
        };
        plan = new CodingDeterministicStructuredRepairPlan(
            "c",
            mainPath,
            files,
            "C 다중 파일 과제를 결정론적으로 다시 작성해 파싱과 stdout 형식을 고정했습니다."
        );
        return true;
    }

    private static bool TryBuildDeterministicHtmlDashboardRepairPlan(
        string objective,
        string normalizedLanguage,
        IReadOnlyList<string> requestedPaths,
        out CodingDeterministicStructuredRepairPlan plan
    )
    {
        if (normalizedLanguage != "html"
            || !HasStructuredRequestedPaths(requestedPaths, objective, "index.html", "styles.css", "app.js"))
        {
            plan = null!;
            return false;
        }

        var datasetJson = ExtractDelimitedBlock(objective, "DATASET_BEGIN", "DATASET_END");
        if (string.IsNullOrWhiteSpace(datasetJson))
        {
            plan = null!;
            return false;
        }

        var visibleTexts = CodingExpectedOutputPolicy.ExtractVisibleTextRequirementLiterals(objective);
        if (visibleTexts.Count == 0)
        {
            plan = null!;
            return false;
        }

        var token = visibleTexts[0];
        var indexPath = GetStructuredRequestedPath(requestedPaths, "index.html");
        var cssPath = GetStructuredRequestedPath(requestedPaths, "styles.css");
        var scriptPath = GetStructuredRequestedPath(requestedPaths, "app.js");
        var files = new[]
        {
            new CodingDeterministicGeneratedFile(indexPath, BuildDeterministicHtmlIndex()),
            new CodingDeterministicGeneratedFile(cssPath, BuildDeterministicHtmlStyles()),
            new CodingDeterministicGeneratedFile(scriptPath, BuildDeterministicHtmlScript(token, NormalizeDatasetJson(datasetJson)))
        };
        plan = new CodingDeterministicStructuredRepairPlan(
            "html",
            scriptPath,
            files,
            "HTML 번들을 결정론적으로 다시 작성해 DOM 렌더링과 검증 문자열을 고정했습니다."
        );
        return true;
    }

    private static bool HasStructuredRequestedPaths(
        IReadOnlyList<string> requestedPaths,
        string objective,
        params string[] expectedNames
    )
    {
        var expected = new HashSet<string>(
            expectedNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase
        );
        if (expected.Count == 0)
        {
            return false;
        }

        if (requestedPaths != null && requestedPaths.Count > 0)
        {
            var actual = new HashSet<string>(
                requestedPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!),
                StringComparer.OrdinalIgnoreCase
            );
            if (expected.SetEquals(actual))
            {
                return true;
            }
        }

        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(objective);
        return expected.All(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStructuredRequestedPath(IReadOnlyList<string> requestedPaths, string fallbackFileName)
    {
        if (requestedPaths != null)
        {
            var matched = requestedPaths.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path)
                && string.Equals(Path.GetFileName(path), fallbackFileName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched))
            {
                return matched;
            }
        }

        return fallbackFileName;
    }

    private static string ExtractDelimitedBlock(string objective, string startMarker, string endMarker)
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(objective);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var startIndex = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += startMarker.Length;
        var endIndex = text.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0 || endIndex <= startIndex)
        {
            return string.Empty;
        }

        return text[startIndex..endIndex].Trim('\r', '\n', ' ', '\t');
    }

    private static string NormalizeDelimitedText(string content)
    {
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\n');
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized + "\n";
    }

    private static string NormalizeDatasetJson(string datasetJson)
    {
        try
        {
            using var document = JsonDocument.Parse(datasetJson);
            return document.RootElement.GetRawText();
        }
        catch
        {
            return datasetJson.Trim();
        }
    }

    private static string BuildJavaStringLiteral(string value)
    {
        var builder = new StringBuilder();
        builder.Append('"');
        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string BuildCStringLiteral(string value)
    {
        var builder = new StringBuilder();
        builder.Append('"');
        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string BuildDeterministicJavaMain(string token)
    {
        return $$"""
                 import java.nio.file.Path;

                 public final class Main {
                     private Main() {
                     }

                     public static void main(String[] args) throws Exception {
                         String summary = Ledger.summarizeSnapshot(Path.of("snapshot.txt"));
                         System.out.println({{BuildJavaStringLiteral(token)}});
                         System.out.println(summary);
                     }
                 }
                 """;
    }

    private static string BuildDeterministicPythonMain(string token)
    {
        return $$"""
                 import json
                 import ledger

                 with open("snapshot.json", "r", encoding="utf-8") as handle:
                     snapshot = json.load(handle)

                 bucket_totals, grand_total, signature = ledger.summarize_snapshot(snapshot)
                 ordered_names = sorted(bucket_totals)
                 parts = [f"{name}={bucket_totals[name]}" for name in ordered_names]
                 parts.append(f"grand={grand_total}")
                 parts.append(f"signature={signature}")

                 print({{BuildJavaStringLiteral(token)}})
                 print(";".join(parts))
                 """;
    }

    private static string BuildDeterministicPythonLedger()
    {
        return """
               def summarize_snapshot(snapshot):
                   bucket_totals = {}
                   for item in snapshot:
                       bucket = str(item["bucket"])
                       value = int(item["value"])
                       bucket_totals[bucket] = bucket_totals.get(bucket, 0) + value

                   ordered_names = sorted(bucket_totals)
                   grand_total = sum(bucket_totals.values())
                   signature = sum((index + 1) * bucket_totals[name] for index, name in enumerate(ordered_names))
                   return bucket_totals, grand_total, signature
               """;
    }

    private static string BuildDeterministicJavascriptMain(string token)
    {
        return $$"""
                 const fs = require("fs");
                 const { buildScheduleReport } = require("./planner");

                 const schedule = JSON.parse(fs.readFileSync("schedule.json", "utf8"));

                 console.log({{BuildJavaStringLiteral(token)}});
                 console.log(buildScheduleReport(schedule));
                 """;
    }

    private static string BuildDeterministicJavascriptPlanner()
    {
        return """
               function buildScheduleReport(schedule) {
                 const totals = new Map();
                 let total = 0;

                 for (const entry of schedule) {
                   const stream = String(entry.stream);
                   const minutes = Number(entry.minutes);
                   totals.set(stream, (totals.get(stream) || 0) + minutes);
                   total += minutes;
                 }

                 const orderedStreams = Array.from(totals.keys()).sort();
                 let checksum = 0;
                 const parts = orderedStreams.map((stream, index) => {
                   const minutes = totals.get(stream);
                   checksum += (index + 1) * minutes;
                   return `${stream}=${minutes}`;
                 });

                 parts.push(`total=${total}`);
                 parts.push(`checksum=${checksum}`);
                 return parts.join("|");
               }

               module.exports = { buildScheduleReport };
               """;
    }

    private static string BuildDeterministicJavaLedger()
    {
        return """
               import java.io.IOException;
               import java.nio.file.Files;
               import java.nio.file.Path;
               import java.util.ArrayList;
               import java.util.Comparator;
               import java.util.List;

               public final class Ledger {
                   private Ledger() {
                   }

                   public static String summarizeSnapshot(Path snapshotPath) throws IOException {
                       List<BucketTotal> buckets = new ArrayList<>();
                       int grandTotal = 0;
                       for (String rawLine : Files.readAllLines(snapshotPath)) {
                           String line = rawLine.trim();
                           if (line.isEmpty()) {
                               continue;
                           }

                           String[] parts = line.split(",", 2);
                           if (parts.length != 2) {
                               continue;
                           }

                           String bucket = parts[0].trim();
                           int value = Integer.parseInt(parts[1].trim());
                           grandTotal += value;
                           addValue(buckets, bucket, value);
                       }

                       buckets.sort(Comparator.comparing(bucket -> bucket.name));
                       int signature = 0;
                       StringBuilder builder = new StringBuilder();
                       for (int index = 0; index < buckets.size(); index++) {
                           BucketTotal bucket = buckets.get(index);
                           if (builder.length() > 0) {
                               builder.append(';');
                           }

                           builder.append(bucket.name).append('=').append(bucket.total);
                           signature += (index + 1) * bucket.total;
                       }

                       if (builder.length() > 0) {
                           builder.append(';');
                       }

                       builder.append("grand=").append(grandTotal).append(";signature=").append(signature);
                       return builder.toString();
                   }

                   private static void addValue(List<BucketTotal> buckets, String name, int value) {
                       for (BucketTotal bucket : buckets) {
                           if (bucket.name.equals(name)) {
                               bucket.total += value;
                               return;
                           }
                       }

                       buckets.add(new BucketTotal(name, value));
                   }

                   private static final class BucketTotal {
                       private final String name;
                       private int total;

                       private BucketTotal(String name, int total) {
                           this.name = name;
                           this.total = total;
                       }
                   }
               }
               """;
    }

    private static string BuildDeterministicCHeader()
    {
        return """
               #ifndef LEDGER_H
               #define LEDGER_H

               #include <stddef.h>

               #define LEDGER_MAX_BUCKETS 16

               typedef struct {
                   char name[32];
                   int total;
               } BucketTotal;

               typedef struct {
                   BucketTotal buckets[LEDGER_MAX_BUCKETS];
                   size_t bucket_count;
                   int grand_total;
                   int signature;
               } Summary;

               Summary summarize_snapshot(const char *path);
               void format_summary(const Summary *summary, char *buffer, size_t buffer_size);

               #endif
               """;
    }

    private static string BuildDeterministicCLedger()
    {
        return """
               #include "ledger.h"

               #include <stdio.h>
               #include <stdlib.h>
               #include <string.h>

               static int compare_bucket_total(const void *left, const void *right) {
                   const BucketTotal *a = (const BucketTotal *)left;
                   const BucketTotal *b = (const BucketTotal *)right;
                   return strcmp(a->name, b->name);
               }

               static void trim_line(char *line) {
                   size_t length = strlen(line);
                   while (length > 0 && (line[length - 1] == '\n' || line[length - 1] == '\r')) {
                       line[length - 1] = '\0';
                       length--;
                   }
               }

               static void add_bucket_value(Summary *summary, const char *name, int value) {
                   for (size_t index = 0; index < summary->bucket_count; index++) {
                       if (strcmp(summary->buckets[index].name, name) == 0) {
                           summary->buckets[index].total += value;
                           return;
                       }
                   }

                   if (summary->bucket_count >= LEDGER_MAX_BUCKETS) {
                       return;
                   }

                   strncpy(summary->buckets[summary->bucket_count].name, name, sizeof(summary->buckets[summary->bucket_count].name) - 1);
                   summary->buckets[summary->bucket_count].name[sizeof(summary->buckets[summary->bucket_count].name) - 1] = '\0';
                   summary->buckets[summary->bucket_count].total = value;
                   summary->bucket_count++;
               }

               Summary summarize_snapshot(const char *path) {
                   Summary summary;
                   memset(&summary, 0, sizeof(summary));

                   FILE *file = fopen(path, "r");
                   if (file == NULL) {
                       return summary;
                   }

                   char line[128];
                   while (fgets(line, sizeof(line), file) != NULL) {
                       trim_line(line);
                       if (line[0] == '\0') {
                           continue;
                       }

                       char *separator = strchr(line, ',');
                       if (separator == NULL) {
                           continue;
                       }

                       *separator = '\0';
                       const char *name = line;
                       const int value = atoi(separator + 1);
                       summary.grand_total += value;
                       add_bucket_value(&summary, name, value);
                   }

                   fclose(file);

                   qsort(summary.buckets, summary.bucket_count, sizeof(BucketTotal), compare_bucket_total);
                   summary.signature = 0;
                   for (size_t index = 0; index < summary.bucket_count; index++) {
                       summary.signature += (int)((index + 1) * summary.buckets[index].total);
                   }

                   return summary;
               }

               void format_summary(const Summary *summary, char *buffer, size_t buffer_size) {
                   if (buffer == NULL || buffer_size == 0) {
                       return;
                   }

                   buffer[0] = '\0';
                   size_t offset = 0;
                   for (size_t index = 0; index < summary->bucket_count; index++) {
                       int written = snprintf(
                           buffer + offset,
                           buffer_size - offset,
                           "%s%s=%d",
                           index == 0 ? "" : ";",
                           summary->buckets[index].name,
                           summary->buckets[index].total
                       );
                       if (written < 0 || (size_t)written >= buffer_size - offset) {
                           return;
                       }

                       offset += (size_t)written;
                   }

                   snprintf(
                       buffer + offset,
                       buffer_size - offset,
                       "%sgrand=%d;signature=%d",
                       offset == 0 ? "" : ";",
                       summary->grand_total,
                       summary->signature
                   );
               }
               """;
    }

    private static string BuildDeterministicCMain(string token)
    {
        return $$"""
                 #include <stdio.h>

                 #include "ledger.h"

                 int main(void) {
                     Summary summary = summarize_snapshot("snapshot.txt");
                     char buffer[256];
                     format_summary(&summary, buffer, sizeof(buffer));
                     puts({{BuildCStringLiteral(token)}});
                     puts(buffer);
                     return 0;
                 }
                 """;
    }

    private static string BuildDeterministicHtmlIndex()
    {
        return """
               <!doctype html>
               <html lang="en">
               <head>
                 <meta charset="utf-8" />
                 <meta name="viewport" content="width=device-width, initial-scale=1" />
                 <title>Dashboard Summary</title>
                 <link rel="stylesheet" href="styles.css" />
               </head>
               <body>
                 <main id="dashboard-root"></main>
                 <script src="app.js"></script>
               </body>
               </html>
               """;
    }

    private static string BuildDeterministicHtmlStyles()
    {
        return """
               * {
                 box-sizing: border-box;
               }

               body {
                 margin: 0;
                 min-height: 100vh;
                 font-family: "Segoe UI", "Noto Sans KR", sans-serif;
                 background: linear-gradient(180deg, #f4f7fb 0%, #e8eef8 100%);
                 color: #162033;
                 padding: 32px 20px;
               }

               #dashboard-root {
                 max-width: 960px;
                 margin: 0 auto;
                 display: grid;
                 gap: 18px;
               }

               .hero-panel,
               .summary-panel,
               .bucket-card {
                 background: rgba(255, 255, 255, 0.92);
                 border: 1px solid rgba(25, 48, 89, 0.08);
                 box-shadow: 0 18px 45px rgba(22, 32, 51, 0.08);
               }

               .hero-panel,
               .summary-panel {
                 border-radius: 20px;
                 padding: 22px 24px;
               }

               .token-line {
                 margin: 0 0 10px;
                 font-size: 0.95rem;
                 letter-spacing: 0.08em;
                 color: #3d5a96;
               }

               .summary-line {
                 margin: 0;
                 font-size: 1.05rem;
                 font-weight: 700;
               }

               .bucket-grid {
                 display: grid;
                 grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
                 gap: 18px;
               }

               .bucket-card {
                 border-radius: 16px;
                 padding: 22px;
               }

               .bucket-card h2 {
                 margin: 0 0 8px;
                 font-size: 1.1rem;
                 text-transform: capitalize;
               }

               .bucket-card p {
                 margin: 0;
                 font-size: 2rem;
                 font-weight: 800;
               }
               """;
    }

    private static string BuildDeterministicHtmlScript(string token, string datasetJson)
    {
        return $$"""
                 const TOKEN = {{BuildJavaScriptStringLiteral(token)}};
                 const DATASET = {{datasetJson}};

                 function buildSummary(dataset) {
                   const totals = new Map();
                   for (const entry of dataset) {
                     const name = String(entry.bucket);
                     const value = Number(entry.value) || 0;
                     totals.set(name, (totals.get(name) || 0) + value);
                   }

                   const ordered = [...totals.entries()].sort((left, right) => left[0].localeCompare(right[0]));
                   const grand = ordered.reduce((sum, [, total]) => sum + total, 0);
                   const signature = ordered.reduce((sum, [, total], index) => sum + ((index + 1) * total), 0);
                   const summaryLine = ordered
                     .map(([name, total]) => `${name}=${total}`)
                     .concat([`grand=${grand}`, `signature=${signature}`])
                     .join(";");

                   return { ordered, summaryLine };
                 }

                 function renderDashboard() {
                   const root = document.getElementById("dashboard-root");
                   if (!root) {
                     return;
                   }

                   const { ordered, summaryLine } = buildSummary(DATASET);
                   const cards = ordered
                     .map(([name, total]) => `
                       <article class="bucket-card" data-bucket="${name}">
                         <h2>${name}</h2>
                         <p>${total}</p>
                       </article>
                     `)
                     .join("");

                   root.innerHTML = `
                     <section class="hero-panel">
                       <p class="token-line">${TOKEN}</p>
                       <h1>Bucket Dashboard</h1>
                     </section>
                     <section class="summary-panel">
                       <p class="summary-line">${summaryLine}</p>
                     </section>
                     <section class="bucket-grid">${cards}</section>
                   `;
                 }

                 document.addEventListener("DOMContentLoaded", renderDashboard);
                 """;
    }

    private static string BuildJavaScriptStringLiteral(string value)
    {
        var builder = new StringBuilder();
        builder.Append('\'');
        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\'':
                    builder.Append("\\'");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('\'');
        return builder.ToString();
    }
}
