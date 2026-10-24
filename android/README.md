# PDF Template Helper for Android

An independent offline Android app, package `org.pdfhelper.app`. It uses the official iText Core Android Java API from Kotlin. No network or storage permission is required. Licensed under AGPL-3.0-or-later; see the repository license. The bundled Noto Sans font is under SIL OFL; its license is in `app/src/main/assets/fonts/OFL.txt`.

## Build

Requires JDK 17+ and Android SDK 36; the app runs on Android 8.1/API 27 or newer, matching iText's documented Android support floor. Set `JAVA_HOME` and `ANDROID_HOME`, then run:

```powershell
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest
```

The APK is `app/build/outputs/apk/debug/app-debug.apk`. The release build uses no signing credentials: provide your own signing configuration before distribution. For Android Studio JBR builds on Windows where Java reports `Unable to establish loopback connection`, set `TEMP`/`TMP` to an existing short path such as `C:\tmp`, and `JAVA_TOOL_OPTIONS=-Djava.io.tmpdir=C:\tmp -Djdk.net.unixdomain.tmpdir=C:\tmp`.

## Use without another app

Launch **PDF Template Helper**. Import a `.pdfjob` archive, then save or open the generated PDF. Or select any PDF template: the app lists its nested AcroForm names and creates starter JSON. Edit the JSON values, fill, save JSON, or save a reusable `.pdfjob`. A template with no form can use coordinate text stamps. The template-only editor packs `template.pdf` and `job.json`; import an externally prepared archive when referencing image/font assets.

The system document picker controls imports and save locations. Processing stays in private app cache. Job extraction is deleted immediately after success/failure, including batch temporary PDFs; result and manual template cache files expire after 24 hours on the next operation. Copy returned PDFs to durable storage before that expiry. Android may clear cached files earlier.

## App integration

Use `org.pdfhelper.action.CAPABILITIES` for an activity result. It returns `protocolVersion=1`, `status=ok`, and the string array `operations=[fill,batch,list-fields,new-job]`. Field inspection and starter creation are available through the launcher GUI.

Launch `org.pdfhelper.action.FILL` for result, with `protocolVersion` integer `1`, a readable `content://` URI in `Intent.data` and `ClipData`, `FLAG_GRANT_READ_URI_PERMISSION`, and MIME `application/zip`, `application/vnd.pdfhelper.job+zip`, or `application/octet-stream`. `RESULT_OK` returns a PDF URI in data and ClipData with a read grant, plus `protocolVersion=1`, `status=ok`, and `warnings` as a JSON array string. Errors use `RESULT_FIRST_USER` with `status=error`, `code`, and `message`. User cancellation returns `RESULT_CANCELED`.

The ZIP contains root `job.json`, one template, and optional assets. The manifest accepts one `request` or a nonempty `requests` array (up to 100); batch fills are merged as PDF content in order. Editable batch field names receive `request1_`, `request2_`, etc. prefixes to keep values independent. Coordinates, image anchors, fit, field values/maps, checkboxes, Unicode text and flattening follow the repository protocol. Missing fields raise an error in strict mode; lenient mode reports warnings. Paths are relative to the ZIP root and must identify contained files. Absolute paths, drive names, backslashes, traversal, Windows reserved names, trailing dots/spaces, invalid/control characters, paths longer than 240 characters, and duplicate ZIP entries are rejected. Limits: 256 ZIP entries, 50 MiB per expanded file, 100 MiB total expanded data, and 1 MiB manifest.

## Tests

JVM tests cover actual nested field filling, Unicode extraction, checkbox handling, flattening, batch order, strict/lenient errors, malicious ZIP/path rejection and cleanup, starter generation, image geometry, and the shared synthetic example archives. To run activity-result tests on a connected emulator/device:

```powershell
.\gradlew.bat :app:connectedDebugAndroidTest
```

The test result receiver exists only in debug builds. Instrumentation tests check real capability results, structured errors, and filled PDF content URIs/metadata.

Official iText Android setup: https://kb.itextpdf.com/itext/installing-itext-on-android

Official Android support floor: https://itextpdf.com/blog/technical-notes/itext-7-suite-723-released
