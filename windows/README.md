# Windows helper

Standalone local PDF template filler using .NET 10, iText 9.5, and an embedded Noto Sans font. No client data, business-specific templates, or network service is required. iText requires compliance with its AGPL terms or a commercial license; see repository licensing documentation. The bundled font's redistribution license is in `src/PdfTemplateHelper.Core/fonts/OFL.txt`.

Build with a .NET 10 SDK:

```powershell
./test.ps1
./publish.ps1 -Runtime win-x64
```

The self-contained outputs need no installed .NET runtime. Distribute each entire output folder, including `fonts/`, together with repository license and notice files. `win-arm64` is also supported by the publish script. The manual WinForms application is `dist/win-x64/gui/PdfTemplateHelper.exe`.

CLI examples (from a published CLI directory):

```powershell
./pdf-template-helper.exe capabilities
./pdf-template-helper.exe list-fields --template template.pdf
./pdf-template-helper.exe new-job --template template.pdf --out starter.pdfjob
./pdf-template-helper.exe new-job --template template.pdf --json request.json --assets ./input --out job.pdfjob
./pdf-template-helper.exe fill --job job.pdfjob --out result.pdf
./pdf-template-helper.exe fill --template template.pdf --json request.json --out result.pdf
```

`new-job` writes a ZIP package, including only referenced assets. Paths in the supplied request must be relative to `--assets` (defaults to the request JSON directory). Without JSON it inspects the template and creates empty field/checkbox values. Without a font it packages Noto Sans and its license automatically. Manual direct `--template --json` mode accepts explicitly trusted local paths; use packaged jobs for application integration.

The GUI loads a template, inspects its fields, accepts request JSON and an asset folder, creates portable packages, and fills/saves PDFs. Choose **Use starter request** to replace the editor with empty named fields and unchecked checkboxes from the current template. It also opens existing `.pdfjob` packages, including batches. The package manifest appears read-only in package mode. Choose a template to resume manual request editing. Filling runs in the background; mutation controls are disabled while it works. **Cancel** stops at the next safe document boundary and preserves an existing output. Closing the window requests cancellation and waits for cleanup.

Protocol 1 packages contain `job.json`, a relative template path, and either `request` or `requests` (one to 100 requests). Batches use the shared template for each request in array order and merge PDF pages without rendering. Editable batch fields receive `request1_`, `request2_`, etc. prefixes to preserve independent values. Request keys are `fields`, `values`, `fieldMap`, `checkboxes`, `stamps`, `images`, `imageFields`, `flatten`, `strict`, `fontPath`, `defaultFontSize`. Field names are case-sensitive. Enum values accept strings such as `TopRight`, `Contain`, `Stretch`, `Left`. Page numbers are one-based and coordinates are PDF points from the bottom-left; image anchors interpret x/y as edge margins.

Package paths must use `/`; absolute paths, traversal, links, device names, duplicate names and Windows alternate data streams are rejected. References can read only extracted files; omitted fonts use the bundled runtime Noto Sans font. Package limits: 256 entries, 50 MiB per file, 100 MiB expanded total, 1 MiB manifest. Errors return nonzero and `{protocolVersion,status,code,message}` JSON on stderr; successful fills return protocol version, status, absolute output path and warnings on stdout. Non-strict requests report skipped missing fields, checkbox/image placements and missing stamp values in warnings. Unsafe paths and missing templates/fonts always fail. Failed fills preserve an existing result.

`test.ps1` creates generic fixtures and runs integration checks for Unicode, checkboxes, images, flattening, batches, strict missing fields, atomic output, and hostile packages. Tests use a console harness without a separate test framework. Generated artifacts are local build output, not source templates.
