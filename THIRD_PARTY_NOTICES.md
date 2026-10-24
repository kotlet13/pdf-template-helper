# Third-party notices

## iText

The helper uses iText Core (Windows .NET and Android Java editions), licensed
under AGPLv3 or a commercial license. This project uses the AGPL option. Exact
dependency versions are pinned in the platform project files.

- Source: https://github.com/itext/itext-dotnet and https://github.com/itext/itext-java
- License information: https://itextpdf.com/how-buy/AGPLv3-license

Keep iText producer/copyright/license metadata in PDFs created or modified with
the helper. The helper source is distributed under AGPL-3.0-or-later; dependency
licenses apply independently and are not relicensed by this repository.

## Noto Sans

The bundled NotoSans-Regular.ttf comes from the Noto project, distributed under
the SIL Open Font License 1.1. The full copyright notice and license are in
`fonts/OFL.txt` and accompany copies in platform assets and example jobs.
Upstream: https://github.com/notofonts/noto-fonts

## Other dependencies

The .NET runtime, Windows Forms, Android/AndroidX, Kotlin, Bouncy Castle, logging
and JSON dependencies retain their upstream licenses and notices. Build/package
manifests specify the actual dependencies. Gradle wrapper files are distributed
under the Apache License 2.0. Redistribution must preserve applicable notices.
