// Failure-only offscreen replay of original retained bytes. No application/input qualification.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ProGPU.Backend;
using ProGPU.Backend.Native;
using Silk.NET.WebGPU;

internal static class SceneReplay
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private static string output = "";

    private static void Save(string name, object value)
    {
        using FileStream file = new(Path.Combine(output, name + ".json"), FileMode.CreateNew);
        JsonSerializer.Serialize(file, value, Json);
        Console.WriteLine(name + " seconds=" + Clock.Elapsed.TotalSeconds);
    }

    public static int Main(string[] args)
    {
        if (args.Length != 3 || !OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF")
            throw new InvalidOperationException("This diagnostic requires the isolated hosted Windows runner.");
        output = Path.GetFullPath(args[2]);
        if (!Directory.Exists(output)) throw new DirectoryNotFoundException(output);
        string[] hashes = [
            "F9FB7C73ACB86D730ADD0E812B82219658811829E63F45BDC3CA1066B0CC773C",
            "4C54CCF28FD236FF7B1193E4680F6CF49A804A3E3CE64A3E74DF373E21A2E8ED"];
        byte[][] scenes = [File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1])];
        for (int i = 0; i < scenes.Length; ++i)
            if (Convert.ToHexString(SHA256.HashData(scenes[i])) != hashes[i])
                throw new InvalidOperationException("Original scene bytes changed.");
        Save("started", new { diagnosticOnly = true, qualified = false, deadlineSeconds = 60,
            pid = Environment.ProcessId, sceneSha256 = hashes });
        try
        {
            using var context = new WgpuContext();
            context.Initialize(window: null);
            Save("adapter", new { context.AdapterName, backend = context.AdapterBackendType.ToString(),
                glyph = context.GlyphRasterizationPath.ToString(), sampling = context.ImageSamplingPath.ToString(),
                compiler = context.SelectedDx12ShaderCompiler.ToString(), context.Dx12CompilerLibraryPath,
                width = 884, height = 861, dpi = 1, format = "Bgra8Unorm", offscreen = true });
            if (context.AdapterName != "Microsoft Basic Render Driver" || context.AdapterBackendType.ToString() != "D3D12")
                throw new InvalidOperationException("Replay must use the original Windows software adapter/backend.");
            string expectedCompiler = Environment.GetEnvironmentVariable("GALLERY_EXPECTED_COMPILER") ?? throw new InvalidOperationException("Missing compiler observation.");
            if (context.SelectedDx12ShaderCompiler.ToString() != expectedCompiler)
                throw new InvalidOperationException("Wrong actual compiler selection.");
            using var target = new GpuTexture(context, 884, 861, TextureFormat.Bgra8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.CopySrc, "Original Gallery scene diagnostic");
            using var renderer = new NativeCompositor(context, TextureFormat.Bgra8Unorm);
            for (int i = 0; i < scenes.Length; ++i)
            {
                byte[] scene = scenes[i];
                ulong id = BinaryPrimitives.ReadUInt64LittleEndian(scene.AsSpan(24));
                ulong generation = BinaryPrimitives.ReadUInt64LittleEndian(scene.AsSpan(32));
                var installed = renderer.UpdateScene(scene);
                Save($"frame-{i + 1}-installed", new { installed, elapsedSeconds = Clock.Elapsed.TotalSeconds });
                var metrics = renderer.RenderScene(target, 1, id, generation, Vector4.One);
                var token = renderer.GetLastSubmissionToken();
                Save($"frame-{i + 1}-submitted", new { metrics, token, elapsedSeconds = Clock.Elapsed.TotalSeconds });
                double deadline = Math.Min(55, Clock.Elapsed.TotalSeconds + 30);
                bool complete;
                do
                {
                    complete = renderer.IsSubmissionComplete(token);
                    if (complete || Clock.Elapsed.TotalSeconds >= deadline) break;
                    Thread.Sleep(1);
                } while (true);
                Save($"frame-{i + 1}-completion", new { token, complete, elapsedSeconds = Clock.Elapsed.TotalSeconds });
                if (!complete) throw new TimeoutException("Original renderer submission did not complete within the diagnostic bound.");
                byte[] pixels = target.ReadPixels();
                if (pixels.Length != 884 * 861 * 4) throw new InvalidOperationException("Incomplete native readback.");
                byte[] rgb = new byte[884 * 861 * 3];
                int nonwhite = 0;
                for (int p = 0; p < 884 * 861; ++p)
                {
                    rgb[p * 3] = pixels[p * 4 + 2]; rgb[p * 3 + 1] = pixels[p * 4 + 1]; rgb[p * 3 + 2] = pixels[p * 4];
                    if (rgb[p * 3] != 255 || rgb[p * 3 + 1] != 255 || rgb[p * 3 + 2] != 255) ++nonwhite;
                }
                using (FileStream image = new(Path.Combine(output, $"frame-{i + 1}.ppm"), FileMode.CreateNew))
                {
                    image.Write("P6\n884 861\n255\n"u8); image.Write(rgb);
                }
                Save($"frame-{i + 1}-pixels", new { diagnosticOnly = true, qualified = false, nonwhite,
                    rgbSha256 = Convert.ToHexString(SHA256.HashData(rgb)),
                    bgraSha256 = Convert.ToHexString(SHA256.HashData(pixels)), elapsedSeconds = Clock.Elapsed.TotalSeconds });
            }
            Save("loaded-modules", Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(module => new[] { "wgpu_native.dll", "progpu_native.dll", "dxcompiler.dll", "dxil.dll", "d3dcompiler_47.dll" }
                    .Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                .Select(module => new { module.ModuleName, module.FileName,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))) }).ToArray());
            return 0;
        }
        catch (Exception error)
        {
            Save("error", new { diagnosticOnly = true, qualified = false, error = error.ToString(), elapsedSeconds = Clock.Elapsed.TotalSeconds });
            return 1;
        }
    }
}
