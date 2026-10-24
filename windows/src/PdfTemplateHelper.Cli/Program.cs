using System.Text;
using System.Text.Json;
using PdfTemplateHelper;

Console.OutputEncoding = Encoding.UTF8;
string Required(string name) => Option(name) ?? throw new JobException("invalid_arguments", $"Missing {name}.");
string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[index + 1] : null;
}
void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JobService.Json));
try
{
    switch (args.FirstOrDefault()?.ToLowerInvariant())
    {
        case "capabilities": Write(JobService.Capabilities()); break;
        case "fill":
            var warnings = new List<string>();
            var result = Option("--job") is { } job
                ? JobService.FillJob(job, Required("--out"), warnings)
                : JobService.FillDirect(Required("--template"), Required("--json"), Required("--out"), warnings);
            Write(new { protocolVersion = 1, status = "ok", output = result, warnings });
            break;
        case "list-fields":
            Write(new { protocolVersion = 1, status = "ok", fields = JobService.ListFields(Required("--template")) });
            break;
        case "new-job":
            var output = Required("--out");
            JobService.CreateJob(Required("--template"), output, Option("--json"), Option("--assets"));
            Write(new { protocolVersion = 1, status = "ok", output = Path.GetFullPath(output), warnings = Array.Empty<string>() });
            break;
        case "--help": case "help": case null:
            Console.WriteLine("PDF Template Helper\ncapabilities\nfill --job file.pdfjob --out result.pdf\nfill --template template.pdf --json request.json --out result.pdf\nlist-fields --template template.pdf\nnew-job --template template.pdf [--json request.json] [--assets directory] --out job.pdfjob");
            break;
        default: throw new JobException("invalid_arguments", "Unknown command. Use --help.");
    }
    return 0;
}
catch (Exception error)
{
    var code = error switch
    {
        JobException jobError => jobError.Code,
        JsonException => "invalid_json",
        FileNotFoundException => "file_not_found",
        InvalidDataException => "invalid_zip",
        UnauthorizedAccessException => "access_denied",
        InvalidOperationException => "invalid_request",
        _ => "processing_error"
    };
    Console.Error.WriteLine(JsonSerializer.Serialize(new { protocolVersion = 1, status = "error", code, message = error.Message }, JobService.Json));
    return 1;
}
