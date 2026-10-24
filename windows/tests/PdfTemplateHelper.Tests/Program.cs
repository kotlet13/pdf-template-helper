using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using PdfPrinter;
using PdfTemplateHelper;

var root = args.FirstOrDefault() is { } artifactsPath ? System.IO.Path.GetFullPath(artifactsPath) : System.IO.Path.Combine(AppContext.BaseDirectory, "generated");
Directory.CreateDirectory(root);
var template = System.IO.Path.Combine(root, "generic-template.pdf");
using (var doc = new PdfDocument(new PdfWriter(template)))
{
    var page = doc.AddNewPage();
    var form = PdfAcroForm.GetAcroForm(doc, true);
    form.AddField(new TextFormFieldBuilder(doc, "customer_name").SetWidgetRectangle(new Rectangle(50, 720, 450, 30)).CreateText(), page);
    form.AddField(new CheckBoxFormFieldBuilder(doc, "approved").SetWidgetRectangle(new Rectangle(50, 675, 20, 20)).CreateCheckBox(), page);
    form.AddField(new TextFormFieldBuilder(doc, "signature").SetWidgetRectangle(new Rectangle(50, 620, 100, 40)).CreateText(), page);
    doc.GetDocumentInfo().SetTitle("Generic form fixture").SetCreator("PDF Template Helper test fixture");
}
var assets = System.IO.Path.Combine(root, "assets"); Directory.CreateDirectory(assets);
File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"), System.IO.Path.Combine(assets, "NotoSans-Regular.ttf"), true);
File.WriteAllBytes(System.IO.Path.Combine(assets, "logo.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQkAAAAASUVORK5CYII="));
var tests = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); tests++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message, string? code = null)
{
    try { action(); } catch (JobException ex) when (code is null || ex.Code == code) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
string Zip(string name, Action<ZipArchive> write)
{
    var target = System.IO.Path.Combine(root, name + ".pdfjob");
    if (File.Exists(target)) File.Delete(target);
    using var zip = ZipFile.Open(target, ZipArchiveMode.Create); write(zip); return target;
}
void Text(ZipArchive zip, string name, string value)
{
    using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); writer.Write(value);
}
var request = new PrintRequest
{
    Fields = new() { ["customer_name"] = "Črt Šime Žagar ć đ" },
    Checkboxes = new() { ["approved"] = true },
    Images = [new() { Path = "assets/logo.png", Page = 1, Anchor = ImageAnchor.TopRight, X = 30, Y = 30, Width = 40, Height = 40 }],
    ImageFields = new() { ["signature"] = "assets/logo.png" },
    Stamps = [new() { Text = "Generic Unicode: čšž ĆĐ", X = 50, Y = 575, FontSize = 14 }],
    FontPath = "assets/NotoSans-Regular.ttf", Flatten = false, Strict = true
};
var json = System.IO.Path.Combine(root, "request.json"); File.WriteAllText(json, JsonSerializer.Serialize(request, JobService.Json));
var job = System.IO.Path.Combine(root, "generic.pdfjob"); JobService.CreateJob(template, job, json, root);
var filled = System.IO.Path.Combine(root, "generic-filled.pdf"); JobService.FillJob(job, filled);
using (var doc = new PdfDocument(new PdfReader(filled)))
{
    var form = PdfAcroForm.GetAcroForm(doc, false);
    Check(form.GetField("customer_name").GetValueAsString() == "Črt Šime Žagar ć đ", "Unicode form value preserved");
    Check(form.GetField("approved").GetValueAsString() != "Off", "Checkbox is checked");
    Check(PdfTextExtractor.GetTextFromPage(doc.GetPage(1)).Contains("čšž ĆĐ"), "Unicode stamp extracts correctly");
    Check(doc.GetPage(1).GetResources().GetResource(PdfName.XObject).Size() > 0, "Image XObject embedded");
    Check(doc.GetDocumentInfo().GetProducer().Contains("iText"), "iText producer attribution retained");
    Check(doc.GetDocumentInfo().GetMoreInfo("iTextLicense").Contains("AGPLv3"), "Explicit iText license metadata retained");
}
Check(JsonSerializer.Serialize(JobService.ListFields(template), JobService.Json).Contains("customer_name"), "Field inspection finds template fields");

var flattened = JsonNode.Parse(File.ReadAllText(json))!.AsObject(); flattened["flatten"] = true;
File.WriteAllText(json, flattened.ToJsonString()); JobService.CreateJob(template, job, json, root); JobService.FillJob(job, filled);
using (var doc = new PdfDocument(new PdfReader(filled)))
{
    Check((PdfAcroForm.GetAcroForm(doc, false)?.GetAllFormFields().Count ?? 0) == 0, "Flatten removes interactive fields");
    Check(PdfTextExtractor.GetTextFromPage(doc.GetPage(1)).Contains("Črt Šime Žagar ć đ"), "Flattened Unicode text preserved");
}
flattened["checkboxes"]!["approved"] = false; flattened["flatten"] = false;
File.WriteAllText(json, flattened.ToJsonString()); JobService.CreateJob(template, job, json, root); JobService.FillJob(job, filled);
using (var doc = new PdfDocument(new PdfReader(filled)))
    Check(PdfAcroForm.GetAcroForm(doc, false).GetField("approved").GetValueAsString() == "Off", "Checkbox false uses Off appearance");

flattened["fields"]!["missing_field"] = "bad"; flattened["flatten"] = true;
File.WriteAllText(json, flattened.ToJsonString()); JobService.CreateJob(template, job, json, root);
var before = File.ReadAllBytes(filled);
try { JobService.FillJob(job, filled); throw new Exception("Missing field accepted"); }
catch (InvalidOperationException) { Check(true, "Strict missing field rejected"); }
Check(before.SequenceEqual(File.ReadAllBytes(filled)), "Failure preserves existing output atomically");
flattened["strict"] = false; File.WriteAllText(json, flattened.ToJsonString()); JobService.CreateJob(template, job, json, root); JobService.FillJob(job, filled);
Check(File.Exists(filled), "Non-strict missing field ignored");
var warnings = new List<string>(); JobService.FillJob(job, filled, warnings);
Check(warnings.Any(w => w.Contains("missing_field")), "Non-strict missing field reports warning");
var warnRequest = (JsonObject)flattened.DeepClone();
warnRequest["checkboxes"]!["missing_checkbox"] = true;
warnRequest["imageFields"]!["missing_image_field"] = "assets/logo.png";
warnRequest["stamps"]!.AsArray().Add(new JsonObject { ["valueKey"] = "missing_value", ["x"] = 10, ["y"] = 10 });
warnRequest["stamps"]!.AsArray().Add(new JsonObject { ["text"] = "Missing page", ["page"] = 999 });
warnRequest["images"]!.AsArray().Add(new JsonObject { ["path"] = "assets/logo.png", ["page"] = 999 });
File.WriteAllText(json, warnRequest.ToJsonString()); JobService.CreateJob(template, job, json, root);
warnings.Clear(); JobService.FillJob(job, filled, warnings);
Check(warnings.Any(w => w.Contains("missing_checkbox")), "Non-strict missing checkbox reports warning");
Check(warnings.Any(w => w.Contains("missing_image_field")), "Non-strict missing image field reports warning");
Check(warnings.Any(w => w.Contains("missing_value")), "Non-strict missing stamp value reports warning");
Check(warnings.Any(w => w.Contains("stamp targeting")), "Non-strict missing stamp page reports warning");
Check(warnings.Any(w => w.Contains("image targeting")), "Non-strict missing image page reports warning");

foreach (var name in new[] { "../escape", "/absolute", "C:/absolute", "assets\\logo.png", "assets/../secret", "assets/CON", "assets/file.", "assets/file:stream" })
{
    var hostile = Zip("hostile", z => Text(z, name, "x"));
    Reject(() => { using var temp = JobService.Extract(hostile); }, "Reject ZIP path " + name, "unsafe_path");
}
var duplicate = Zip("duplicate", z => { Text(z, "job.json", "{}"); Text(z, "JOB.JSON", "{}"); });
Reject(() => { using var temp = JobService.Extract(duplicate); }, "Reject case-insensitive duplicate ZIP", "invalid_zip");
var symlink = Zip("symlink", z => { var e = z.CreateEntry("assets/link"); e.ExternalAttributes = unchecked((int)0xA1FF0000); });
Reject(() => { using var temp = JobService.Extract(symlink); }, "Reject symlink ZIP", "unsafe_path");
var tooMany = Zip("many", z => { for (int i = 0; i <= JobService.MaximumEntries; i++) Text(z, "a" + i, ""); });
Reject(() => { using var temp = JobService.Extract(tooMany); }, "Reject excessive ZIP entries", "job_too_large");
var tooLarge = Zip("large", z => { using var stream = z.CreateEntry("large").Open(); var buffer = new byte[1024 * 1024]; for (int i = 0; i < 65; i++) stream.Write(buffer); });
Reject(() => { using var temp = JobService.Extract(tooLarge); }, "Reject oversized expanded entry", "job_too_large");

string ManifestJob(string name, JsonObject manifest)
    => Zip(name, z => { Text(z, "job.json", manifest.ToJsonString()); z.CreateEntryFromFile(template, "template.pdf"); });
var baseManifest = new JsonObject { ["protocolVersion"] = 1, ["template"] = "template.pdf", ["request"] = new JsonObject() };
var missingAsset = (JsonObject)baseManifest.DeepClone(); missingAsset["request"]!["fontPath"] = "assets/missing.ttf";
Reject(() => JobService.FillJob(ManifestJob("missing", missingAsset), filled), "Reject missing packaged asset", "missing_asset");
foreach (var path in new[] { "../outside.ttf", "C:/Windows/Fonts/arial.ttf", "https://example.test/font.ttf" })
{
    var hostile = (JsonObject)baseManifest.DeepClone(); hostile["request"]!["fontPath"] = path;
    Reject(() => JobService.FillJob(ManifestJob("reference", hostile), filled), "Reject external font " + path, "unsafe_path");
}
var externalImage = (JsonObject)baseManifest.DeepClone(); externalImage["request"]!["imageFields"] = new JsonObject { ["signature"] = "../outside.png" };
Reject(() => JobService.FillJob(ManifestJob("image-reference", externalImage), filled), "Reject imageFields traversal", "unsafe_path");
var version = (JsonObject)baseManifest.DeepClone(); version["protocolVersion"] = 2;
Reject(() => JobService.FillJob(ManifestJob("version", version), filled), "Reject unsupported protocol", "unsupported_version");
var both = (JsonObject)baseManifest.DeepClone(); both["requests"] = new JsonArray(new JsonObject());
Reject(() => JobService.FillJob(ManifestJob("both", both), filled), "Reject request plus requests", "invalid_job");
var empty = (JsonObject)baseManifest.DeepClone(); empty.Remove("request"); empty["requests"] = new JsonArray();
Reject(() => JobService.FillJob(ManifestJob("empty", empty), filled), "Reject empty batch", "invalid_job");

var batch = new JsonObject { ["protocolVersion"] = 1, ["template"] = "template.pdf", ["requests"] = new JsonArray(
    new JsonObject { ["fields"] = new JsonObject { ["customer_name"] = "First document" }, ["flatten"] = true },
    new JsonObject { ["fields"] = new JsonObject { ["customer_name"] = "Second document" }, ["flatten"] = true }) };
var batchPath = ManifestJob("batch", batch); var batchPdf = System.IO.Path.Combine(root, "batch-filled.pdf");
JobService.FillJob(batchPath, batchPdf);
using (var doc = new PdfDocument(new PdfReader(batchPdf)))
{
    Check(doc.GetNumberOfPages() == 2, "Batch produces two ordered pages");
    Check(PdfTextExtractor.GetTextFromPage(doc.GetPage(1)).Contains("First document"), "Batch first request order");
    Check(PdfTextExtractor.GetTextFromPage(doc.GetPage(2)).Contains("Second document"), "Batch second request order");
    Check((PdfAcroForm.GetAcroForm(doc, false)?.GetAllFormFields().Count ?? 0) == 0, "Batch remains flattened");
    Check(doc.GetDocumentInfo().GetMoreInfo("iTextLicense").Contains("AGPLv3"), "Batch license metadata retained");
}
foreach (var entry in batch["requests"]!.AsArray()) entry!["flatten"] = false;
JobService.FillJob(ManifestJob("batch-editable", batch), batchPdf);
using (var doc = new PdfDocument(new PdfReader(batchPdf)))
{
    var fields = PdfAcroForm.GetAcroForm(doc, false);
    Check(fields.GetField("request1_customer_name").GetValueAsString() == "First document", "Editable batch retains first value");
    Check(fields.GetField("request2_customer_name").GetValueAsString() == "Second document", "Editable batch namespaces duplicate fields");
}
var starter = System.IO.Path.Combine(root, "starter.pdfjob"); JobService.CreateJob(template, starter);
JobService.FillJob(starter, System.IO.Path.Combine(root, "starter-filled.pdf"));
Check(File.Exists(starter), "Starter job packs template and default font");
var starterRequest = JobService.CreateStarterRequest(template);
Check(starterRequest.Fields!["customer_name"] == "", "Starter request includes empty named text fields");
Check(starterRequest.Checkboxes!["approved"] == false, "Starter request includes unchecked named checkboxes");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { JobService.FillJob(job, filled, cancellationToken: cancelled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "Pre-cancelled fill stops before work"); }
}
var cancelledManifest = new JsonObject { ["protocolVersion"] = 1, ["template"] = "template.pdf", ["requests"] = new JsonArray(
    new JsonObject { ["fields"] = new JsonObject { ["missing_to_trigger_cancellation"] = "value" }, ["strict"] = false },
    new JsonObject { ["fields"] = new JsonObject { ["customer_name"] = "Must not publish" } }) };
var cancelledJob = ManifestJob("cancelled-batch", cancelledManifest);
var existing = File.ReadAllBytes(filled);
using (var cancelled = new CancellationTokenSource())
{
    try { JobService.FillJob(cancelledJob, filled, new CancelOnWarning(cancelled), cancelled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "Cancellation during document processing stops batch"); }
}
Check(existing.SequenceEqual(File.ReadAllBytes(filled)), "Cancelled batch preserves existing final output");
Check(Directory.GetFiles(root, ".*.pdf").Length == 0, "Cancelled fill leaves no pending output beside destination");
// Leave only useful generic fixture artifacts, including one portable package with Unicode and images.
flattened["fields"]!.AsObject().Remove("missing_field"); flattened["strict"] = true; flattened["flatten"] = true;
File.WriteAllText(json, flattened.ToJsonString(JobService.Json)); JobService.CreateJob(template, job, json, root); JobService.FillJob(job, filled);
Console.WriteLine($"All {tests} integration assertions passed. Generic outputs: {root}");

sealed class CancelOnWarning(CancellationTokenSource source) : System.Collections.ObjectModel.Collection<string>
{
    protected override void InsertItem(int index, string item) { base.InsertItem(index, item); source.Cancel(); }
}
