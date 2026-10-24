using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using iText.Forms;
using iText.Kernel.Pdf;
using PdfPrinter;

namespace PdfTemplateHelper;

public sealed class JobException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class JobDocument
{
    public int ProtocolVersion { get; set; }
    public string? Template { get; set; }
    public PrintRequest? Request { get; set; }
    public List<PrintRequest>? Requests { get; set; }
}

public static class JobService
{
    public const long MaximumEntryBytes = 50 * 1024 * 1024;
    public const long MaximumTotalBytes = 100 * 1024 * 1024;
    public const int MaximumEntries = 256;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() }
    };

    public static object Capabilities() => new
    {
        protocolVersion = 1, operations = new[] { "fill", "list-fields", "new-job", "batch" },
        platform = "windows", jobFormat = "zip", extension = ".pdfjob",
        features = new[] { "acroform", "unicode", "checkboxes", "images", "imageFields", "stamps", "flatten" },
        maximumEntryBytes = MaximumEntryBytes, maximumTotalBytes = MaximumTotalBytes, maximumEntries = MaximumEntries, maximumManifestBytes = 1024 * 1024,
        application = "PDF Template Helper", version = "1.0.0", sourceUrl = "https://github.com/kotlet13/pdf-template-helper"
    };

    // Validate names for both ZIP extraction and JSON references, independent of host OS.
    public static string SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Contains('\\') || name.StartsWith('/') || name.Contains(':'))
            throw new JobException("unsafe_path", "Job paths must be relative POSIX paths without drive letters or backslashes.");
        var parts = name.Split('/');
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0];
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new JobException("unsafe_path", "Job path contains an unsafe component.");
        }
        return name;
    }

    public static string Resolve(string root, string? relative)
    {
        var safe = SafeName(relative);
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, safe.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new JobException("unsafe_path", "Job path escapes its directory.");
        if (!File.Exists(full)) throw new JobException("missing_asset", $"Missing packaged file: {safe}");
        return full;
    }

    public static string FillJob(string jobPath, string output, ICollection<string>? warnings = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Path.GetFullPath(jobPath).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new JobException("invalid_output", "Output must differ from the job package.");
        using var extracted = Extract(jobPath, cancellationToken);
        var manifestPath = Resolve(extracted.Path, "job.json");
        if (new FileInfo(manifestPath).Length > 1024 * 1024)
            throw new JobException("invalid_job", "job.json exceeds 1 MiB.");
        var manifest = JsonSerializer.Deserialize<JobDocument>(File.ReadAllText(manifestPath), Json)
            ?? throw new JobException("invalid_job", "job.json must be an object.");
        if (manifest.ProtocolVersion != 1) throw new JobException("unsupported_version", "Only protocolVersion 1 is supported.");
        if ((manifest.Request is null) == (manifest.Requests is null) || manifest.Requests is { Count: 0 } || manifest.Requests is { Count: > 100 })
            throw new JobException("invalid_job", "Provide either request or requests (1 to 100 entries).");
        var template = Resolve(extracted.Path, manifest.Template);
        var requests = manifest.Requests ?? [manifest.Request!];
        var results = new List<string>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null) throw new JobException("invalid_job", "Request entries cannot be null.");
            var requestNode = JsonSerializer.SerializeToNode(request, Json)!.AsObject();
            ResolveReferences(requestNode, p => Resolve(extracted.Path, p), warnings, true);
            var requestPath = Path.Combine(extracted.Path, Guid.NewGuid() + ".json");
            File.WriteAllText(requestPath, requestNode.ToJsonString(Json));
            var result = Path.Combine(extracted.Path, Guid.NewGuid() + ".pdf");
            FillDirect(template, requestPath, result, warnings);
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(result);
        }
        var final = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        var pending = Path.Combine(Path.GetDirectoryName(final)!, $".{Guid.NewGuid()}.pdf");
        try
        {
            if (results.Count == 1) File.Copy(results[0], pending);
            else
            {
                using var pdf = new PdfDocument(new PdfWriter(pending));
                var copier = new PdfPageFormCopier();
                for (var index = 0; index < results.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var source = new PdfDocument(new PdfReader(results[index]));
                    var form = PdfAcroForm.GetAcroForm(source, false);
                    if (form is not null)
                        foreach (var name in form.GetRootFormFields().Keys.ToArray())
                            form.RenameField(name, $"request{index + 1}_{name}");
                    source.CopyPagesTo(1, source.GetNumberOfPages(), pdf, copier);
                }
                PdfTemplateFiller.ApplyMetadata(pdf);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, final, true);
            return final;
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public static string FillDirect(string template, string requestJson, string output, ICollection<string>? warnings = null)
    {
        var final = Path.GetFullPath(output);
        if (string.Equals(final, Path.GetFullPath(template), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(final, Path.GetFullPath(requestJson), StringComparison.OrdinalIgnoreCase))
            throw new JobException("invalid_output", "Output must differ from input files.");
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        var pending = Path.Combine(Path.GetDirectoryName(final)!, $".{Guid.NewGuid()}.pdf");
        try
        {
            PdfTemplateFiller.FillFromJson(template, requestJson, pending, warnings);
            File.Move(pending, final, true);
            return final;
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private static void ResolveReferences(JsonObject request, Func<string, string> resolve, ICollection<string>? warnings = null, bool allowMissingImages = false)
    {
        var canSkip = allowMissingImages && request["strict"]?.GetValue<bool>() == false;
        string? ImagePath(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative) && canSkip)
            {
                warnings?.Add("Skipped image with an empty path.");
                return null;
            }
            try { return resolve(relative ?? ""); }
            catch (JobException ex) when (ex.Code == "missing_asset" && canSkip)
            {
                warnings?.Add($"Skipped missing packaged image '{relative}'.");
                return null;
            }
        }
        if (request["fontPath"] is JsonValue font) request["fontPath"] = resolve(font.GetValue<string>());
        if (request["images"] is JsonArray images)
            foreach (var item in images.ToArray())
            {
                if (item is not JsonObject image || image["path"] is not JsonValue path)
                {
                    if (!canSkip) throw new JobException("invalid_job", "Every image requires a path.");
                    warnings?.Add("Skipped image without a path."); images.Remove(item); continue;
                }
                var resolved = ImagePath(path.GetValue<string>());
                if (resolved is null) images.Remove(item); else image["path"] = resolved;
            }
        if (request["imageFields"] is JsonObject fields)
            foreach (var key in fields.Select(p => p.Key).ToArray())
            {
                var resolved = ImagePath(fields[key]?.GetValue<string>());
                if (resolved is null) fields.Remove(key); else fields[key] = resolved;
            }
    }

    public static object[] ListFields(string template)
    {
        using var pdf = new PdfDocument(new PdfReader(template));
        var form = PdfAcroForm.GetAcroForm(pdf, false);
        return form?.GetAllFormFields().OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (object)new { name = p.Key, type = p.Value.GetFormType()?.GetValue(),
                value = p.Value.GetValueAsString(), states = p.Value.GetAppearanceStates() }).ToArray() ?? [];
    }

    public static PrintRequest CreateStarterRequest(string template)
    {
        using var pdf = new PdfDocument(new PdfReader(template));
        var fields = PdfAcroForm.GetAcroForm(pdf, false)?.GetAllFormFields();
        return new PrintRequest
        {
            Fields = fields?.Where(p => !PdfName.Btn.Equals(p.Value.GetFormType())).ToDictionary(p => p.Key, _ => ""),
            Checkboxes = fields?.Where(p => PdfName.Btn.Equals(p.Value.GetFormType())).ToDictionary(p => p.Key, _ => false)
        };
    }

    public static void CreateJob(string template, string output, string? requestJson = null, string? assetsRoot = null)
    {
        var templateFull = Path.GetFullPath(template);
        var outputFull = Path.GetFullPath(output);
        if (outputFull.Equals(templateFull, StringComparison.OrdinalIgnoreCase) ||
            (requestJson is not null && outputFull.Equals(Path.GetFullPath(requestJson), StringComparison.OrdinalIgnoreCase)))
            throw new JobException("invalid_output", "Output must differ from input files.");
        using var temp = new TemporaryDirectory();
        JsonObject request;
        if (requestJson is null)
        {
            var initial = CreateStarterRequest(template);
            request = JsonSerializer.SerializeToNode(initial, Json)!.AsObject();
        }
        else
        {
            var parsed = JsonSerializer.Deserialize<PrintRequest>(File.ReadAllText(requestJson), Json)
                ?? throw new JobException("invalid_job", "Request must be an object.");
            request = JsonSerializer.SerializeToNode(parsed, Json)!.AsObject();
        }
        var root = Path.GetFullPath(assetsRoot ?? (requestJson is null ? Path.GetDirectoryName(templateFull)! : Path.GetDirectoryName(Path.GetFullPath(requestJson))!));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["template.pdf"] = templateFull };
        ResolveReferences(request, relative =>
        {
            var name = SafeName(relative);
            if (name.Equals("template.pdf", StringComparison.OrdinalIgnoreCase) || name.Equals("job.json", StringComparison.OrdinalIgnoreCase))
                throw new JobException("unsafe_path", "Asset uses a reserved package name.");
            files[name] = Resolve(root, name);
            return name;
        });
        // Include an open licensed font so a job is portable across platforms.
        if (request["fontPath"] is null)
        {
            files["assets/NotoSans-Regular.ttf"] = Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf");
            files["assets/OFL.txt"] = Path.Combine(AppContext.BaseDirectory, "fonts", "OFL.txt");
            request["fontPath"] = "assets/NotoSans-Regular.ttf";
        }
        var package = Path.Combine(temp.Path, "package.pdfjob");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            foreach (var file in files) zip.CreateEntryFromFile(file.Value, file.Key, CompressionLevel.Optimal);
            using var writer = new StreamWriter(zip.CreateEntry("job.json").Open());
            writer.Write(new JsonObject { ["protocolVersion"] = 1, ["template"] = "template.pdf", ["request"] = request }.ToJsonString(Json));
        }
        using (Extract(package)) { } // Enforce the same limits as consumers before publishing.
        Directory.CreateDirectory(Path.GetDirectoryName(outputFull)!);
        File.Copy(package, outputFull, true);
    }

    public static TemporaryDirectory Extract(string package, CancellationToken cancellationToken = default)
    {
        var temp = new TemporaryDirectory();
        try
        {
            using var zip = ZipFile.OpenRead(package);
            if (zip.Entries.Count > MaximumEntries) throw new JobException("job_too_large", "Too many ZIP entries.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = entry.FullName.EndsWith('/');
                var name = SafeName(directory ? entry.FullName[..^1] : entry.FullName);
                if (!names.Add(name)) throw new JobException("invalid_zip", "Duplicate ZIP entry name.");
                var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType != 0 && unixType != 0x8000 && unixType != 0x4000)
                    throw new JobException("unsafe_path", "ZIP links and special files are forbidden.");
                total = checked(total + entry.Length);
                if (entry.Length > MaximumEntryBytes || total > MaximumTotalBytes)
                    throw new JobException("job_too_large", "ZIP exceeds size limit.");
                var destination = Path.Combine(temp.Path, name.Replace('/', Path.DirectorySeparatorChar));
                if (directory) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew);
                var buffer = new byte[81920];
                long written = 0;
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    written += count;
                    if (written > entry.Length || written > MaximumEntryBytes) throw new JobException("job_too_large", "Expanded entry exceeds declared size.");
                    output.Write(buffer, 0, count);
                }
                if (written != entry.Length) throw new JobException("invalid_zip", "Truncated ZIP entry.");
            }
            return temp;
        }
        catch { temp.Dispose(); throw; }
    }
}

public sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pdf-template-helper", Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
