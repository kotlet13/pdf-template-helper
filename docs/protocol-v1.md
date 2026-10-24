# PDF Template Helper protocol 1

This is a generic PDF document transformation contract. Clients send PDF field
names and values; helpers do not receive database handles, business entities,
credentials or access to client-internal services.

## Input package

A `.pdfjob` file is a ZIP containing UTF-8 `job.json` at its root, one PDF
template and any referenced image/font assets. Example:

```json
{
  "protocolVersion": 1,
  "template": "template.pdf",
  "request": {
    "fields": {"customer": "Example customer", "date": "2026-10-24"},
    "checkboxes": {"accepted": true},
    "fontPath": "assets/NotoSans-Regular.ttf",
    "flatten": true,
    "strict": true
  }
}
```

For batches, replace `request` with `requests`, a nonempty array of request
objects. Supplying both is invalid. Each request fills a fresh copy of the same
template, and resulting documents are joined in array order without rasterizing.
Clients check the `batch` capability before submitting batches. One PDF is returned.

All file references are relative to the package root, use `/`, and must resolve
inside the extracted package. Absolute paths, drive paths, parent traversal,
links, duplicate file paths and oversized archives are rejected. Package input
never grants permission to open an arbitrary host file or a network URL. Helpers
clean up extracted inputs and intermediate outputs after processing.

Portable limits: 256 ZIP entries, 50 MiB per expanded file, 100 MiB expanded
total, 1 MiB `job.json`, and at most 100 requests. Both platforms reject path
components that are invalid/reserved on Windows, even when running on Android.

## Request properties

| Property | Meaning |
|---|---|
| `fields` | PDF field name to string value. |
| `checkboxes` | PDF checkbox name to boolean; appearance state selected from the template. |
| `values`, `fieldMap` | Optional key/value inputs and key-to-PDF-field mapping. |
| `stamps` | Text placements: `page`, `x`, `y`, `fontSize`, `align` (`Left`, `Center`, `Right`), `text` or `valueKey`. |
| `images` | Image placements: `page`, `x`, `y`, `width`, `height`, `path`, optional `anchor` and `fit`. |
| `imageFields` | PDF widget name to image asset path; use the widget rectangle. |
| `fontPath` | Optional packaged TTF/OTF font; otherwise use the helper's bundled Noto Sans font. |
| `defaultFontSize` | Field font size, `0` for automatic fitting. |
| `flatten` | Default `true`; turn editable form appearances into page content. |
| `strict` | Default `true`; missing referenced fields/assets/values are errors. Non-strict mode must report skipped items. |

Page numbers are 1-based. Coordinates are PDF points (1/72 inch), origin bottom
left. Image `anchor` is `BottomLeft`, `BottomRight`, `TopLeft`, or `TopRight`;
when supplied, `x`/`y` are margins from that edge. `fit` is `Contain` (default)
or `Stretch`. A zero width or height derives that dimension from the image aspect
ratio; if both are zero image pixels are interpreted as points. A signature image
is a graphic, not a certificate-based digital signature.

Unknown protocol versions fail clearly. Additional optional JSON properties may
be added compatibly; helpers must not advertise operations they do not implement.

## Windows process interface

Invoke the chosen executable without a shell:

```text
capabilities
fill --job INPUT.pdfjob --out OUTPUT.pdf
list-fields --template TEMPLATE.pdf
new-job --template TEMPLATE.pdf --out OUTPUT.pdfjob
```

`capabilities` returns a JSON object with integer `protocolVersion: 1` and
`operations` string array including `fill`, `list-fields`, `new-job`, `batch`.
The object may include helper name and version. Successful fill exits 0 and writes
JSON `{ "protocolVersion":1, "status":"ok", "output":"...", "warnings":[] }`
to stdout. Errors exit nonzero and return a structured error with `status`, `code`
and `message`. Clients also verify that a nonempty PDF was actually produced.
Diagnostic logging must not corrupt stdout JSON or disclose supplied field values.

## Android interface

The reference app ID is `org.pdfhelper.app`. Interfaces are public to other apps;
clients can discover compatible handlers and choose a preferred package.

- Action `org.pdfhelper.action.CAPABILITIES`: launch for result; success returns
  `protocolVersion` integer 1 and `operations` string array.
- Action `org.pdfhelper.action.FILL`: launch for result with `protocolVersion`
  integer 1 and a `.pdfjob` **content URI** in `Intent.data`. Include URI in
  `ClipData` and grant temporary read access. Do not put package bytes in extras.
- Success: `RESULT_OK`, `status=ok`, `protocolVersion=1`, PDF content URI in result
  data/ClipData with temporary read access; `warnings` is a JSON array string.
- Failure: `RESULT_FIRST_USER`, `status=error`, `code`, `message`.
- User cancellation: `RESULT_CANCELED`.

Both sides use Android lifecycle-aware result handling. Clients copy the returned
PDF to their own private storage before the temporary URI expires. Inputs/results
are scoped to a job; helpers do not request all-files access or client database
access. Fill must work with the device offline.

## Client behavior

Check capabilities and protocol compatibility, retain a correlation with the
pending job, handle cancellation/timeouts and show actionable errors. Never
silently substitute another layout when the user requested a particular template.
Separate device-local configuration from any synced business database.
