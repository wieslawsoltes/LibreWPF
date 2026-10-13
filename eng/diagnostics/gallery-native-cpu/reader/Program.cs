using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
int pid = int.Parse(args[1]);
if (File.Exists(output)) throw new InvalidOperationException("Fresh output required.");
string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
using var conversion = File.CreateText(output + ".conversion.log");
using var trace = TraceLog.OpenOrConvert(input, new TraceLogOptions { ContinueOnError = false, ConversionLog = conversion });
var processes = trace.Processes.Where(p => p.ProcessID == pid).ToArray();
if (processes.Length != 1 || !processes[0].Name.Equals("GalleryClipboardApp", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("ETW must contain exactly the verified Gallery process, without PID reuse.");
var stacks = new Dictionary<string, long>();
var nativeCallers = new Dictionary<string, long>();
var modules = new Dictionary<string, long>();
var bins = new Dictionary<string, long>();
long samples = 0, missingStacks = 0;
double first = double.PositiveInfinity, last = 0;
foreach (var e in processes[0].EventsInProcess.ByEventType<SampledProfileTraceData>()) {
    samples++; first = Math.Min(first, e.TimeStampRelativeMSec); last = Math.Max(last, e.TimeStampRelativeMSec);
    var names = new List<string>();
    string leaf = "unknown";
    for (var s = e.CallStack(); s != null && names.Count < 128; s = s.Caller) {
        string module = s.CodeAddress.ModuleFile?.Name ?? "unknown";
        if (names.Count == 0) leaf = module;
        string method = s.CodeAddress.FullMethodName;
        names.Add(string.IsNullOrEmpty(method) ? module + "!0x" + s.CodeAddress.Address.ToString("x") : module + "!" + method);
    }
    if (leaf == "d3dcompiler_47") {
        var native = new List<string>();
        for (var f = e.CallStack(); f != null; f = f.Caller) {
            var m = f.CodeAddress.ModuleFile;
            if (m?.Name == "progpu_native") native.Add("progpu_native+0x" + (f.CodeAddress.Address - m.ImageBase).ToString("x"));
        }
        string caller = string.Join(" / ", native);
        nativeCallers[caller] = nativeCallers.GetValueOrDefault(caller) + 1;
    }
    if (names.Count == 0) missingStacks++;
    string key = "thread=" + e.ThreadID + "\n" + string.Join("\n", names);
    stacks[key] = stacks.GetValueOrDefault(key) + 1;
    modules[leaf] = modules.GetValueOrDefault(leaf) + 1;
    string bin = ((int)(e.TimeStampRelativeMSec / 5000) * 5) + "s/" + leaf;
    bins[bin] = bins.GetValueOrDefault(bin) + 1;
}
if (samples == 0) throw new InvalidOperationException("No target CPU samples.");
File.WriteAllText(output, JsonSerializer.Serialize(new {
    diagnosticOnly = true, qualified = false, inputSha256 = hash,
    inputUnchanged = hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
    pid, samples, missingStacks, eventsLost = trace.EventsLost, firstMs = first, lastMs = last,
    loadedModules = processes[0].LoadedModules.Where(m => new[] { "progpu_native", "wgpu_native", "d3dcompiler_47" }.Contains(m.Name))
        .Select(m => new { m.Name, m.FilePath, m.ImageBase, m.ModuleFile.ImageSize, m.ModuleFile.PdbName, m.ModuleFile.PdbSignature, m.ModuleFile.PdbAge }),
    nativeCompilerCallers = nativeCallers.OrderByDescending(x => x.Value).Select(x => new { call = x.Key, samples = x.Value }),
    sampleKind = "Kernel CPU sampled stacks, not whole-run wall-time qualification",
    modules = modules.OrderByDescending(x => x.Value).Select(x => new { name=x.Key, samples=x.Value }),
    bins = bins.OrderBy(x => x.Key).Select(x => new { name=x.Key, samples=x.Value }),
    stacks = stacks.OrderByDescending(x => x.Value).Take(100).Select(x => new { stack=x.Key, samples=x.Value })
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Owned target CPU samples={samples}, missing stacks={missingStacks}, lost events={trace.EventsLost}.");

