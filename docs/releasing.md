# Releasing

1. Run the Windows integration harness and Android JVM/lint checks. Run Android
   connected instrumentation on a device/emulator. Inspect rendered single and
   batch fixture PDFs. Test both manual launchers and an independent client.
2. Build Windows with `windows/publish.ps1`; distribute each complete output
   folder, not just the executable. Include font and license files. ARM64 is a
   separate build and requires separate runtime verification.
3. Build the Android APK. A debug APK is for testing and must be labeled debug.
   Production distribution needs a stable, privately held signing key and a
   release signing configuration. Never commit the key or credentials. Keep
   release and debug signing expectations explicit for upgrade compatibility.
4. Create an immutable source tag for the exact binaries. Publish the complete
   corresponding source with the release; include dependency manifests, build
   scripts, Gradle wrapper, license texts and applicable third-party notices.
5. Include protocol version, supported operations, tested architectures/Android
   versions, checksums, and any known limitations in the release notes.
6. Verify the release has no customer data, private templates/branding,
   signatures, credentials, machine-local paths or signing material.

The repository CI builds and packages Windows and the Android debug APK. It does
not create a production Android signing identity or claim device versions that
have not been tested. The helper operates locally; no service deployment is part
of the release.
