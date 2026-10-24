# PDF Template Helper

A standalone, offline PDF form utility for Windows and Android. Supply any PDF
template and a JSON request; the helper fills fields, applies checkboxes, places
text/images, and produces a PDF. It does not require an account, database, server,
or a particular client application.

The helper is licensed under GNU AGPL version 3 or later. iText and bundled fonts
retain their own notices; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Components

- [Windows](windows/): .NET CLI, reusable engine and WinForms application.
- [Android](android/): native Kotlin application, manual workflow and public intent interface.
- [Protocol](docs/protocol-v1.md): versioned, application-independent `.pdfjob` exchange format.
- [Examples](examples/): synthetic form, job and Python client that do not depend on any business application.

Both platforms implement the same file contract. PDF generation happens on the
device. The Android app does not require internet access. Native iText bindings
differ across platforms; interoperability tests compare document content and
form state rather than expecting byte-identical PDF files.

## Try without writing an application

Open the Windows GUI or Android launcher and import `examples/sample.pdfjob`.
Generate the PDF and save it. Use the field inspector/starter request workflow to
prepare a request for your own PDF. The sample contains fictitious data only.

From the Windows CLI:

```text
pdf-template-helper.exe capabilities
pdf-template-helper.exe fill --job examples/sample.pdfjob --out output/sample.pdf
```

The GUI executable is `PdfTemplateHelper.exe`. The CLI contract is documented in
[protocol-v1.md](docs/protocol-v1.md).

For an independent client example:

```text
python examples/client.py --helper PATH_TO_CLI --job examples/sample.pdfjob --out output/sample.pdf
```

## Source and distribution

Build instructions are in each platform directory. Distributions must include
the license and notices and provide the corresponding source for the distributed
version. Do not remove the iText producer metadata from generated PDFs. Releases
must distinguish debug Android APKs from production-signed packages.

Optional client applications communicate through the documented file/process or
Android intent interface. This repository does not license or determine the legal
status of separately developed applications; separate installation alone is not
a conclusion about whether a particular integration creates a combined work.

Public examples are generated from source in `examples/make_samples.py`. Do not
add customer data, handwritten signatures, private branding, private keys or
third-party templates without redistribution rights.
