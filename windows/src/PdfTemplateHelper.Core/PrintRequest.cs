namespace PdfPrinter;

public sealed class PrintRequest
{
    /// <summary>
    /// PDF AcroForm field values by field name.
    /// </summary>
    public Dictionary<string, string>? Fields { get; init; }

    /// <summary>
    /// Application/business values by key (stable JSON keys you control).
    /// Use together with <see cref="FieldMap"/> (and/or <see cref="TextStamp.ValueKey"/>).
    /// </summary>
    public Dictionary<string, string>? Values { get; init; }

    /// <summary>
    /// Maps a key in <see cref="Values"/> to a PDF field name in the template.
    /// Example: { "CustomerName": "customer_name" }
    /// </summary>
    public Dictionary<string, string>? FieldMap { get; init; }

    /// <summary>
    /// Checkbox/radio values by PDF field name.
    /// true = checked, false = unchecked.
    /// </summary>
    public Dictionary<string, bool>? Checkboxes { get; init; }

    /// <summary>
    /// Extra text placements (coordinate-based), useful when the template has no form fields.
    /// </summary>
    public List<TextStamp>? Stamps { get; init; }

    /// <summary>
    /// Image placements by coordinates.
    /// </summary>
    public List<ImageStamp>? Images { get; init; }

    /// <summary>
    /// Places images into form fields by name (uses the field widget rectangle).
    /// Useful for signatures (matches the old "pushbutton image" approach).
    /// </summary>
    public Dictionary<string, string>? ImageFields { get; init; }

    /// <summary>
    /// If true (default), flattens form fields after filling.
    /// </summary>
    public bool Flatten { get; init; } = true;

    /// <summary>
    /// If true, missing fields/values cause an error. If false, they are ignored (filled with empty string).
    /// </summary>
    public bool Strict { get; init; } = true;

    /// <summary>
    /// Path to a .ttf/.otf font file to embed/use for text fields (needed for čćđšž etc).
    /// If not provided, uses the bundled open Noto Sans font.
    /// </summary>
    public string? FontPath { get; init; }

    /// <summary>
    /// Default font size for form fields when a template field has no explicit size.
    /// Use 0 for auto-size.
    /// </summary>
    public float DefaultFontSize { get; init; } = 0;
}

public sealed class TextStamp
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>X coordinate in PDF points (1/72 inch), origin bottom-left.</summary>
    public float X { get; init; }

    /// <summary>Y coordinate in PDF points (1/72 inch), origin bottom-left.</summary>
    public float Y { get; init; }

    public float FontSize { get; init; } = 10;

    public TextAlign Align { get; init; } = TextAlign.Left;

    public string? Text { get; init; }

    /// <summary>
    /// Optional key to read text from <see cref="PrintRequest.Values"/>.
    /// If set, it overrides <see cref="Text"/>.
    /// </summary>
    public string? ValueKey { get; init; }
}

public enum TextAlign
{
    Left = 0,
    Center = 1,
    Right = 2
}

public sealed class ImageStamp
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Optional anchor mode. If set, X/Y are treated as margins from that edge.
    /// For example: anchor=TopLeft, x=36, y=36 means "36pt from left, 36pt from top".
    /// </summary>
    public ImageAnchor? Anchor { get; init; }

    /// <summary>X coordinate in PDF points (1/72 inch), origin bottom-left.</summary>
    public float X { get; init; }

    /// <summary>Y coordinate in PDF points (1/72 inch), origin bottom-left.</summary>
    public float Y { get; init; }

    /// <summary>
    /// Target width in points.
    /// If 0 and Height &gt; 0, width will be auto-calculated to preserve aspect ratio.
    /// If 0 and Height == 0, uses the image pixel width as points.
    /// </summary>
    public float Width { get; init; }

    /// <summary>
    /// Target height in points.
    /// If 0 and Width &gt; 0, height will be auto-calculated to preserve aspect ratio.
    /// If 0 and Width == 0, uses the image pixel height as points.
    /// </summary>
    public float Height { get; init; }

    public ImageFit Fit { get; init; } = ImageFit.Contain;

    /// <summary>Path to an image file (png/jpg/etc).</summary>
    public string? Path { get; init; }
}

public enum ImageAnchor
{
    BottomLeft = 0,
    BottomRight = 1,
    TopLeft = 2,
    TopRight = 3
}

public enum ImageFit
{
    Contain = 0,
    Stretch = 1
}
