using System.Text.Json;
using System.Text.Json.Serialization;

using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace PdfPrinter;

public static class PdfTemplateFiller
{
    public static void ApplyMetadata(PdfDocument document)
    {
        // iText maintains its Producer automatically; retain that attribution.
        document.GetDocumentInfo().SetCreator("PDF Template Helper")
            .SetMoreInfo("iTextLicense", "iText Core 9.5.0; Copyright Apryse Group NV; AGPLv3 (https://www.gnu.org/licenses/agpl-3.0.html) or commercial license. https://itextpdf.com/");
    }
    private sealed record ImagePlacement(
        int PageNumber,
        float X,
        float Y,
        float Width,
        float Height,
        string ImagePath,
        ImageFit Fit,
        ImageAnchor? Anchor
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // Allow enums to be passed as strings in JSON (e.g. "Contain", "Left").
            new JsonStringEnumConverter()
        }
    };

    public static string GetDefaultOutputPath(string templatePath)
    {
        var templateName = Path.GetFileNameWithoutExtension(templatePath);
        var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);
        return Path.Combine(outDir, $"{templateName}_filled.pdf");
    }

    public static void ListFields(string templatePath, TextWriter output)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Template PDF not found.", templatePath);
        }

        using var reader = new PdfReader(templatePath);
        using var pdfDoc = new PdfDocument(reader);

        var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
        if (acroForm is null)
        {
            output.WriteLine("No AcroForm found (no interactive form fields in this template).");
            return;
        }

        // iText 8+ / iText 9: AcroForm stores only root fields; use GetAllFormFields to include descendants.
        var fields = acroForm.GetAllFormFields();
        if (fields.Count == 0)
        {
            output.WriteLine("AcroForm present, but no fields found.");
            return;
        }

        output.WriteLine("Fields:");
        foreach (var kvp in fields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var name = kvp.Key;
            var field = kvp.Value;
            var value = TryGetFieldValue(field);
            var type = field.GetFormType()?.GetValue() ?? "?";
            var suffix = value is null ? "" : $" = {value}";

            // For checkboxes/radios/buttons, print appearance states (helps determine the correct "checked" value).
            if (PdfName.Btn.Equals(field.GetFormType()))
            {
                var states = field.GetAppearanceStates();
                if (states is { Length: > 0 })
                {
                    suffix += $" states=[{string.Join(", ", states)}]";
                }
            }

            output.WriteLine($"- {name} (type={type}){suffix}");
        }
    }

    public static void WriteStarterJobJson(string templatePath, string outJsonPath)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Template PDF not found.", templatePath);
        }

        using var reader = new PdfReader(templatePath);
        using var pdfDoc = new PdfDocument(reader);

        var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
        if (acroForm is null)
        {
            throw new InvalidOperationException("Template has no AcroForm (no form fields).");
        }

        var fields = acroForm.GetAllFormFields();
        if (fields.Count == 0)
        {
            throw new InvalidOperationException("AcroForm present, but no fields found.");
        }

        // Starter job uses direct field names so the user can quickly test end-to-end.
        var job = new PrintRequest
        {
            Strict = true,
            Flatten = true,
            Fields = fields
                .Where(kvp => !PdfName.Btn.Equals(kvp.Value.GetFormType()))
                .Select(kvp => kvp.Key)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k, _ => "", StringComparer.OrdinalIgnoreCase),
            Checkboxes = fields
                .Where(kvp => PdfName.Btn.Equals(kvp.Value.GetFormType()))
                .Select(kvp => kvp.Key)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k, _ => false, StringComparer.OrdinalIgnoreCase)
        };

        var json = JsonSerializer.Serialize(
            job,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }
        );

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outJsonPath));
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        File.WriteAllText(outJsonPath, json);
    }

    public static void FillFromJson(string templatePath, string jsonPath, string outputPath, ICollection<string>? warnings = null)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Template PDF not found.", templatePath);
        }

        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException("JSON input not found.", jsonPath);
        }

        var requestJson = File.ReadAllText(jsonPath);
        var request = JsonSerializer.Deserialize<PrintRequest>(
            requestJson,
            JsonOptions
        ) ?? new PrintRequest();

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using var reader = new PdfReader(templatePath);
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(reader, writer);

        ApplyMetadata(pdfDoc);
        var unicodeFont = ResolveUnicodeFont(pdfDoc, request);

        PdfAcroForm? acroForm = null;
        IDictionary<string, PdfFormField>? formFields = null;

        acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
        if (acroForm is not null)
        {
            // Helps ensure values are visible in common PDF viewers.
            acroForm.SetGenerateAppearance(true);
            formFields = acroForm.GetAllFormFields();
        }
        formFields ??= new Dictionary<string, PdfFormField>();

        var pendingImages = new List<ImagePlacement>();
        if (request.ImageFields is not null && request.ImageFields.Count > 0)
        {
            if ((acroForm is null || formFields.Count == 0) && request.Strict)
            {
                throw new InvalidOperationException("JSON contains 'imageFields', but the template has no AcroForm fields.");
            }

            foreach (var (fieldName, imagePath) in request.ImageFields)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    if (request.Strict) throw new InvalidOperationException("imageFields contains an empty field name.");
                    warnings?.Add("Skipped imageFields entry with an empty field name.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    if (request.Strict) throw new InvalidOperationException($"imageFields['{fieldName}'] has an empty image path.");
                    warnings?.Add($"Skipped imageFields '{fieldName}' with an empty path.");
                    continue;
                }

                if (!File.Exists(imagePath))
                {
                    if (request.Strict) throw new FileNotFoundException($"Image not found for field '{fieldName}'.", imagePath);
                    warnings?.Add($"Skipped missing image for field '{fieldName}'.");
                    continue;
                }

                if (!formFields.TryGetValue(fieldName, out var field))
                {
                    if (request.Strict)
                    {
                        throw new InvalidOperationException($"Template does not contain a field named '{fieldName}' (imageFields).");
                    }
                    warnings?.Add($"Skipped missing image field '{fieldName}'.");
                    continue;
                }

                var widgets = field.GetWidgets();
                if (widgets is null || widgets.Count == 0)
                {
                    if (request.Strict) throw new InvalidOperationException($"Field '{fieldName}' has no widget annotations to place an image into.");
                    warnings?.Add($"Skipped image field '{fieldName}' without widgets.");
                    continue;
                }

                foreach (var widget in widgets)
                {
                    var page = widget.GetPage();
                    if (page is null)
                    {
                        if (request.Strict) throw new InvalidOperationException($"Field '{fieldName}' widget has no page.");
                        warnings?.Add($"Skipped image field '{fieldName}' widget without page.");
                        continue;
                    }

                    var rectArr = widget.GetRectangle();
                    var rect = rectArr?.ToRectangle();
                    if (rect is null)
                    {
                        if (request.Strict) throw new InvalidOperationException($"Field '{fieldName}' widget has no rectangle.");
                        warnings?.Add($"Skipped image field '{fieldName}' widget without rectangle.");
                        continue;
                    }

                    var pageNumber = pdfDoc.GetPageNumber(page);
                    pendingImages.Add(
                        new ImagePlacement(pageNumber, rect.GetX(), rect.GetY(), rect.GetWidth(), rect.GetHeight(), imagePath, ImageFit.Contain, null)
                    );
                }
            }
        }

        if (request.Images is not null && request.Images.Count > 0)
        {
            foreach (var img in request.Images)
            {
                if (string.IsNullOrWhiteSpace(img.Path))
                {
                    if (request.Strict) throw new InvalidOperationException("An images[] entry has an empty path.");
                    warnings?.Add("Skipped image with empty path.");
                    continue;
                }

                if (!File.Exists(img.Path))
                {
                    if (request.Strict) throw new FileNotFoundException("Image not found.", img.Path);
                    warnings?.Add("Skipped missing image file.");
                    continue;
                }

                var pageNumber = img.Page <= 0 ? 1 : img.Page;
                pendingImages.Add(
                    new ImagePlacement(pageNumber, img.X, img.Y, img.Width, img.Height, img.Path, img.Fit, img.Anchor)
                );
            }
        }

        if (request.Fields is not null && request.Fields.Count > 0)
        {
            if ((acroForm is null || formFields.Count == 0) && request.Strict)
            {
                throw new InvalidOperationException(
                    "JSON contains 'fields', but the template has no AcroForm fields. Use 'stamps' for coordinate-based placement, or use a template with form fields."
                );
            }

            foreach (var (name, value) in request.Fields)
            {
                if (!formFields.TryGetValue(name, out var field))
                {
                    if (request.Strict)
                    {
                        throw new InvalidOperationException(
                            $"Template does not contain a field named '{name}'. Run 'list-fields' to see available names."
                        );
                    }
                    warnings?.Add($"Skipped missing field '{name}'.");
                    continue;
                }

                SetFieldValue(field, value, unicodeFont, request.DefaultFontSize);
            }
        }
        else if (request.FieldMap is not null && request.FieldMap.Count > 0)
        {
            if ((acroForm is null || formFields.Count == 0) && request.Strict)
            {
                throw new InvalidOperationException(
                    "JSON contains 'fieldMap', but the template has no AcroForm fields."
                );
            }

            foreach (var (valueKey, pdfFieldName) in request.FieldMap)
            {
                if (string.IsNullOrWhiteSpace(pdfFieldName))
                {
                    if (request.Strict)
                    {
                        throw new InvalidOperationException($"FieldMap entry for '{valueKey}' has an empty pdf field name.");
                    }
                    warnings?.Add($"Skipped mapping '{valueKey}' with an empty field name.");
                    continue;
                }

                if (!formFields.TryGetValue(pdfFieldName, out var field))
                {
                    if (request.Strict)
                    {
                        throw new InvalidOperationException(
                            $"Template does not contain a field named '{pdfFieldName}' (mapped from value key '{valueKey}')."
                        );
                    }
                    warnings?.Add($"Skipped missing mapped field '{pdfFieldName}'.");
                    continue;
                }

                var value = TryResolveValue(request, valueKey);
                if (value is null && request.Strict)
                {
                    throw new InvalidOperationException($"Missing value for key '{valueKey}'.");
                }
                if (value is null) warnings?.Add($"Used empty value for missing key '{valueKey}'.");

                SetFieldValue(field, value, unicodeFont, request.DefaultFontSize);
            }
        }

        if (request.Checkboxes is not null && request.Checkboxes.Count > 0)
        {
            if ((acroForm is null || formFields.Count == 0) && request.Strict)
            {
                throw new InvalidOperationException("JSON contains 'checkboxes', but the template has no AcroForm fields.");
            }

            foreach (var (name, isChecked) in request.Checkboxes)
            {
                if (!formFields.TryGetValue(name, out var field))
                {
                    if (request.Strict)
                    {
                        throw new InvalidOperationException($"Template does not contain a field named '{name}' (checkboxes).");
                    }
                    warnings?.Add($"Skipped missing checkbox '{name}'.");
                    continue;
                }

                SetCheckboxValue(field, isChecked);
            }
        }

        if (acroForm is not null && request.Flatten)
        {
            acroForm.FlattenFields();
        }

        // Stamp text and images AFTER flatten so they appear on top of flattened fields.
        if (request.Stamps is not null && request.Stamps.Count > 0)
        {
            var font = unicodeFont ?? PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            foreach (var stamp in request.Stamps)
            {
                var pageNumber = stamp.Page <= 0 ? 1 : stamp.Page;
                if (pageNumber > pdfDoc.GetNumberOfPages())
                {
                    if (request.Strict) throw new InvalidOperationException(
                        $"Stamp targets page {pageNumber}, but template only has {pdfDoc.GetNumberOfPages()} pages."
                    );
                    warnings?.Add($"Skipped stamp targeting missing page {pageNumber}.");
                    continue;
                }

                var page = pdfDoc.GetPage(pageNumber);
                var pdfCanvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);

                using var canvas = new Canvas(pdfCanvas, page.GetPageSize());
                canvas.SetFont(font);
                canvas.SetFontSize(stamp.FontSize <= 0 ? 10 : stamp.FontSize);

                var text = stamp.ValueKey is null ? stamp.Text : TryResolveValue(request, stamp.ValueKey);
                if (text is null && request.Strict && stamp.ValueKey is not null)
                {
                    throw new InvalidOperationException($"Missing value for key '{stamp.ValueKey}' (stamp on page {pageNumber}).");
                }
                if (text is null && stamp.ValueKey is not null) warnings?.Add($"Used empty stamp value for missing key '{stamp.ValueKey}'.");

                text ??= string.Empty;
                canvas.ShowTextAligned(
                    new Paragraph(text).SetMargin(0),
                    stamp.X,
                    stamp.Y,
                    pageNumber,
                    stamp.Align switch
                    {
                        TextAlign.Center => TextAlignment.CENTER,
                        TextAlign.Right => TextAlignment.RIGHT,
                        _ => TextAlignment.LEFT
                    },
                    VerticalAlignment.BOTTOM,
                    0
                );
            }
        }

        if (pendingImages.Count > 0)
        {
            foreach (var placement in pendingImages)
            {
                if (placement.PageNumber <= 0 || placement.PageNumber > pdfDoc.GetNumberOfPages())
                {
                    if (request.Strict) throw new InvalidOperationException(
                        $"Image targets page {placement.PageNumber}, but template only has {pdfDoc.GetNumberOfPages()} pages."
                    );
                    warnings?.Add($"Skipped image targeting missing page {placement.PageNumber}.");
                    continue;
                }

                StampImage(
                    pdfDoc,
                    placement.PageNumber,
                    placement.X,
                    placement.Y,
                    placement.Width,
                    placement.Height,
                    placement.ImagePath,
                    placement.Fit,
                    placement.Anchor
                );
            }
        }
    }

    private static string? TryResolveValue(PrintRequest request, string valueKey)
    {
        if (request.Values is null)
        {
            return null;
        }

        return request.Values.TryGetValue(valueKey, out var val) ? val : null;
    }

    private static PdfFont? ResolveUnicodeFont(PdfDocument pdfDoc, PrintRequest request)
    {
        var candidate = request.FontPath;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf");
        if (!File.Exists(candidate))
            throw new FileNotFoundException("Font file not found.", candidate);
        // Identity-H ensures Unicode glyph mapping; embed so output PDF is portable.
        return PdfFontFactory.CreateFont(
            File.ReadAllBytes(candidate),
            PdfEncodings.IDENTITY_H,
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED
        );
    }

    private static void SetFieldValue(PdfFormField field, string? value, PdfFont? unicodeFont, float defaultFontSize)
    {
        var v = value ?? string.Empty;

        // Only apply fonts to variable-text and choice fields. Checkboxes/radios don't need this.
        var type = field.GetFormType();
        if (unicodeFont is not null && (PdfName.Tx.Equals(type) || PdfName.Ch.Equals(type)))
        {
            var size = field.GetFontSize();
            if (size <= 0)
            {
                size = defaultFontSize;
            }

            field.SetValue(v, unicodeFont, size);
            return;
        }

        field.SetValue(v);
    }

    private static void SetCheckboxValue(PdfFormField field, bool isChecked)
    {
        // iText expects checkbox/radio values to match appearance state names.
        // "Off" is the standard unchecked state name.
        var states = field.GetAppearanceStates() ?? Array.Empty<string>();
        var onState = states.FirstOrDefault(s => !string.Equals(s, "Off", StringComparison.OrdinalIgnoreCase))
                      ?? "Yes";

        field.SetValue(isChecked ? onState : "Off");
    }

    private static void StampImage(
        PdfDocument pdfDoc,
        int pageNumber,
        float x,
        float y,
        float width,
        float height,
        string imagePath,
        ImageFit fit,
        ImageAnchor? anchor
    )
    {
        var imageData = ImageDataFactory.Create(imagePath);
        var iw = imageData.GetWidth();
        var ih = imageData.GetHeight();

        // Target box (points). If one dimension is missing, preserve aspect ratio.
        float targetW;
        float targetH;
        if (width > 0 && height > 0)
        {
            targetW = width;
            targetH = height;
        }
        else if (width > 0 && height <= 0)
        {
            targetW = width;
            targetH = ih * (targetW / iw);
        }
        else if (height > 0 && width <= 0)
        {
            targetH = height;
            targetW = iw * (targetH / ih);
        }
        else
        {
            // If no target size provided, use image px as pt.
            targetW = iw;
            targetH = ih;
        }

        var page = pdfDoc.GetPage(pageNumber);
        var pageSize = page.GetPageSize();
        float anchorX;
        float anchorY;

        if (anchor is not null)
        {
            // In anchor mode, x/y are margins from that edge.
            anchorX = anchor.Value switch
            {
                ImageAnchor.BottomRight or ImageAnchor.TopRight => pageSize.GetWidth() - x - targetW,
                _ => x
            };

            anchorY = anchor.Value switch
            {
                ImageAnchor.TopLeft or ImageAnchor.TopRight => pageSize.GetHeight() - y - targetH,
                _ => y
            };
        }
        else
        {
            // Backwards compatible mode:
            // - x = -36 means 36pt margin from right
            // - y = -36 means 36pt margin from top
            anchorX = x < 0 ? (pageSize.GetWidth() + x - targetW) : x;
            anchorY = y < 0 ? (pageSize.GetHeight() + y - targetH) : y;
        }

        float drawW;
        float drawH;
        float drawX = anchorX;
        float drawY = anchorY;

        if (fit == ImageFit.Stretch)
        {
            drawW = targetW;
            drawH = targetH;
        }
        else
        {
            var s = Math.Min(targetW / iw, targetH / ih);
            drawW = iw * s;
            drawH = ih * s;
            drawX = anchorX + (targetW - drawW) / 2f;
            drawY = anchorY + (targetH - drawH) / 2f;
        }

        var pdfCanvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
        pdfCanvas.AddImageWithTransformationMatrix(imageData, drawW, 0, 0, drawH, drawX, drawY, false);
    }

    private static string? TryGetFieldValue(PdfFormField field)
    {
        try
        {
            return field.GetValueAsString();
        }
        catch
        {
            return null;
        }
    }
}
