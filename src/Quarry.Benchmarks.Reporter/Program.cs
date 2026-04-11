using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/Quarry.Benchmarks.Reporter -- <input.json> <output.html> [commit-sha] [commit-message] [commit-date]");
    return 1;
}

var inputPath = args[0];
var outputPath = args[1];
var commitSha = args.Length > 2 ? args[2] : "";
var commitMessage = args.Length > 3 ? args[3] : "";
var commitDate = args.Length > 4 ? args[4] : "";
const string RepoUrl = "https://github.com/Dtronix/Quarry";

using var doc = JsonDocument.Parse(File.ReadAllBytes(inputPath));
var root = doc.RootElement;
var hostInfo = root.GetProperty("HostEnvironmentInfo");
var benchmarks = root.GetProperty("Benchmarks");

var groups = new SortedDictionary<string, List<JsonElement>>(StringComparer.Ordinal);
foreach (var bench in benchmarks.EnumerateArray())
{
    var type = bench.GetProperty("Type").GetString() ?? "Unknown";
    if (!groups.TryGetValue(type, out var list))
    {
        list = new List<JsonElement>();
        groups[type] = list;
    }
    list.Add(bench);
}

var sb = new StringBuilder();
sb.Append("""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Quarry Benchmark Report</title>
<style>
* { box-sizing: border-box; }
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; color: #24292e; background: #f5f5f5; }
header { background: white; padding: 1.25rem 2rem; border-bottom: 1px solid #e1e4e8; position: sticky; top: 0; z-index: 10; }
header h1 { margin: 0 0 0.4rem; font-size: 1.4rem; }
header .commit { font-family: monospace; font-size: 0.9rem; }
header .commit a { color: #0366d6; text-decoration: none; }
header .commit a:hover { text-decoration: underline; }
header .meta { color: #586069; font-size: 0.8rem; margin-top: 0.4rem; }
header nav { margin-top: 0.6rem; }
header nav a { color: #0366d6; text-decoration: none; margin-right: 1.25rem; font-size: 0.9rem; }
header nav a:hover { text-decoration: underline; }
.layout { display: grid; grid-template-columns: 260px 1fr; gap: 0; }
.sidebar { background: white; border-right: 1px solid #e1e4e8; padding: 1rem; height: calc(100vh - 140px); overflow-y: auto; position: sticky; top: 140px; }
.sidebar h2 { font-size: 0.75rem; text-transform: uppercase; color: #586069; margin: 0 0 0.5rem; letter-spacing: 0.5px; }
.sidebar ul { list-style: none; padding: 0; margin: 0; }
.sidebar li { margin-bottom: 0.15rem; }
.sidebar a { text-decoration: none; color: #0366d6; font-size: 0.85rem; display: block; padding: 0.3rem 0.5rem; border-radius: 3px; font-family: monospace; }
.sidebar a:hover { background: #f1f8ff; }
main { padding: 1.5rem 2rem; min-width: 0; }
section { background: white; padding: 1.25rem 1.5rem; margin-bottom: 1.5rem; border-radius: 6px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }
section h2 { margin: 0 0 1rem; font-family: monospace; font-size: 1.05rem; color: #24292e; }
table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
th, td { padding: 0.5rem 0.75rem; text-align: right; border-bottom: 1px solid #f0f0f0; white-space: nowrap; }
th { background: #f6f8fa; font-weight: 600; text-align: right; color: #586069; font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.3px; }
th:first-child, td:first-child { text-align: left; font-family: monospace; }
tr.quarry { background: #fffbdd; }
tr.quarry:hover { background: #fff8c5; }
tr:hover:not(.quarry) { background: #f6f8fa; }
.numeric { font-variant-numeric: tabular-nums; }
</style>
</head>
<body>
""");

sb.Append("<header>\n<h1>Quarry Benchmark Report</h1>\n");
if (!string.IsNullOrEmpty(commitSha))
{
    var shortSha = commitSha[..Math.Min(8, commitSha.Length)];
    sb.Append("<div class=\"commit\"><a href=\"");
    sb.Append(RepoUrl).Append("/commit/").Append(HtmlEncode(commitSha));
    sb.Append("\" target=\"_blank\" rel=\"noopener\">");
    sb.Append(HtmlEncode(shortSha));
    sb.Append("</a>");
    if (!string.IsNullOrEmpty(commitMessage))
    {
        sb.Append(" \u2014 ");
        sb.Append(HtmlEncode(commitMessage));
    }
    sb.Append("</div>\n");
}

var os = TryGetString(hostInfo, "OsVersion") ?? "Unknown OS";
var processor = TryGetString(hostInfo, "ProcessorName") ?? "Unknown CPU";
var runtime = TryGetString(hostInfo, "RuntimeVersion") ?? "Unknown runtime";
sb.Append("<div class=\"meta\">");
sb.Append(HtmlEncode(os));
sb.Append(" &middot; ");
sb.Append(HtmlEncode(processor));
sb.Append(" &middot; ");
sb.Append(HtmlEncode(runtime));
if (!string.IsNullOrEmpty(commitDate))
{
    sb.Append(" &middot; ");
    sb.Append(HtmlEncode(commitDate));
}
sb.Append("</div>\n");
sb.Append("<nav>");
sb.Append("<a href=\"../../../\">Home</a>");
sb.Append("<a href=\"../\">Dashboard</a>");
sb.Append("<a href=\"./\">Run History</a>");
sb.Append("<a href=\"").Append(RepoUrl).Append("\" target=\"_blank\" rel=\"noopener\">Source</a>");
sb.Append("</nav>\n</header>\n");

sb.Append("<div class=\"layout\">\n<aside class=\"sidebar\">\n<h2>Benchmark Classes</h2>\n<ul>\n");
foreach (var (type, _) in groups)
{
    var anchor = AnchorFor(type);
    sb.Append("<li><a href=\"#").Append(anchor).Append("\">").Append(HtmlEncode(type)).Append("</a></li>\n");
}
sb.Append("</ul>\n</aside>\n<main>\n");

foreach (var (type, benches) in groups)
{
    var anchor = AnchorFor(type);
    sb.Append("<section id=\"").Append(anchor).Append("\">\n<h2>").Append(HtmlEncode(type)).Append("</h2>\n");
    sb.Append("<table>\n<thead><tr>");
    sb.Append("<th>Method</th>");
    sb.Append("<th>Mean</th>");
    sb.Append("<th>Error</th>");
    sb.Append("<th>StdDev</th>");
    sb.Append("<th>Median</th>");
    sb.Append("<th>Allocated</th>");
    sb.Append("<th>Gen0</th>");
    sb.Append("</tr></thead>\n<tbody>\n");

    foreach (var bench in benches.OrderBy(b => b.GetProperty("Statistics").GetProperty("Mean").GetDouble()))
    {
        var method = TryGetString(bench, "Method") ?? "";
        var stats = bench.GetProperty("Statistics");
        var mean = stats.GetProperty("Mean").GetDouble();
        var stdErr = stats.TryGetProperty("StandardError", out var se) ? se.GetDouble() : 0;
        var stdDev = stats.GetProperty("StandardDeviation").GetDouble();
        var median = stats.GetProperty("Median").GetDouble();
        var error = stdErr * 1.96;

        long allocated = -1;
        double gen0 = 0;
        if (bench.TryGetProperty("Memory", out var mem))
        {
            if (mem.TryGetProperty("BytesAllocatedPerOperation", out var alloc))
                allocated = alloc.GetInt64();
            if (mem.TryGetProperty("Gen0Collections", out var g0))
                gen0 = g0.GetDouble();
        }

        var rowClass = method.StartsWith("Quarry", StringComparison.Ordinal) ? " class=\"quarry\"" : "";
        sb.Append("<tr").Append(rowClass).Append(">");
        sb.Append("<td>").Append(HtmlEncode(method)).Append("</td>");
        sb.Append("<td class=\"numeric\">").Append(FormatTime(mean)).Append("</td>");
        sb.Append("<td class=\"numeric\">").Append(FormatTime(error)).Append("</td>");
        sb.Append("<td class=\"numeric\">").Append(FormatTime(stdDev)).Append("</td>");
        sb.Append("<td class=\"numeric\">").Append(FormatTime(median)).Append("</td>");
        sb.Append("<td class=\"numeric\">").Append(FormatBytes(allocated)).Append("</td>");
        sb.Append("<td class=\"numeric\">").Append(gen0 > 0 ? gen0.ToString("F3", CultureInfo.InvariantCulture) : "-").Append("</td>");
        sb.Append("</tr>\n");
    }
    sb.Append("</tbody></table>\n</section>\n");
}

sb.Append("</main>\n</div>\n</body>\n</html>\n");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, sb.ToString());
Console.WriteLine($"Wrote {outputPath} with {benchmarks.GetArrayLength()} benchmarks across {groups.Count} classes");
return 0;

static string? TryGetString(JsonElement element, string property) =>
    element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

static string HtmlEncode(string s) => WebUtility.HtmlEncode(s);

static string AnchorFor(string s)
{
    var sb = new StringBuilder(s.Length);
    foreach (var c in s)
    {
        if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        else sb.Append('-');
    }
    return sb.ToString();
}

static string FormatTime(double ns)
{
    if (ns >= 1_000_000_000) return (ns / 1_000_000_000).ToString("F3", CultureInfo.InvariantCulture) + " s";
    if (ns >= 1_000_000) return (ns / 1_000_000).ToString("F3", CultureInfo.InvariantCulture) + " ms";
    if (ns >= 1_000) return (ns / 1_000).ToString("F3", CultureInfo.InvariantCulture) + " us";
    return ns.ToString("F3", CultureInfo.InvariantCulture) + " ns";
}

static string FormatBytes(long bytes)
{
    if (bytes < 0) return "-";
    if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " GB";
    if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
    if (bytes >= 1024) return (bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " KB";
    return bytes.ToString(CultureInfo.InvariantCulture) + " B";
}
