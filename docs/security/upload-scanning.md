# Attachment hardening and the upload scanner hook (SEC-18)

Applies to `POST /api/attachments` (photos, scans, PDFs on documents and products). Code: `UploadAttachmentHandler`,
`ImageMetadataStripper` and `AttachmentRules` (Domain), `IUploadScanner` (Application), `NoOpUploadScanner` (Infrastructure).

## 1. What happens to an upload, in order

1. Owner policy (warehouse scope, state of the document) and the per-owner file count are checked first, before any bytes are read.
2. The upload is read **once into memory**, never more than 10 MB (an understated `Content-Length` cannot get past this). Every later check sees the same bytes.
3. The type is decided from the first bytes (JPEG, PNG, WebP, PDF), never from the client's file name or content type.
4. **Images** are rewritten without metadata (section 2). A damaged or truncated image is refused with 400 `FileNotAValidImage`.
5. **PDFs** are refused when they contain `/JavaScript`, `/JS`, `/Launch`, `/EmbeddedFile` or `/RichMedia` (unchanged rule, now run before the file reaches disk). Otherwise they are stored byte for byte.
6. The **upload scanner** (section 3) gets the final bytes. Not clean: 400 `FileRejectedByScanner`, nothing is stored.
7. The bytes go to storage under a generated name; the recorded size and SHA-256 are those of the stored (cleaned) bytes. If the database write fails, the file is removed again.

Downloads are unchanged: `nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: private, no-store`, PDFs always as a download.

## 2. Metadata removal (`ImageMetadataStripper`)

No image library and no decoding: the container structure is parsed and everything that is not pixels or colour is dropped, so pixels are never
re-compressed and a file without metadata is returned **byte for byte**.

| Format | Removed | Kept |
|---|---|---|
| JPEG | APP1 (EXIF with GPS and camera data, XMP), APP3 to APP15 except Adobe (IPTC, vendor data), COM comments, every byte after the end-of-image marker | JFIF (APP0), ICC colour profile (APP2), Adobe colour transform (APP14), all table, frame and scan segments |
| PNG | `tEXt`, `zTXt`, `iTXt`, `eXIf`, `tIME` and every private or unknown chunk, every byte after `IEND` | `IHDR`, `PLTE`, `IDAT`, `IEND`, `tRNS`, `gAMA`, `cHRM`, `sRGB`, `iCCP`, `sBIT`, `bKGD`, `hIST`, `pHYs`, `sPLT`, animation chunks, `cICP`, `mDCV`, `cLLI` |
| WebP | `EXIF` and `XMP ` chunks, unknown chunks, bytes beyond the RIFF size; the `VP8X` flags for EXIF and XMP are cleared and the RIFF size rewritten | `VP8 `, `VP8L`, `VP8X`, `ALPH`, `ANIM`, `ANMF`, `ICCP` |

A file whose structure is broken (missing frame or scan, chunk longer than the file, no `IHDR` first, no end marker, nothing to display) is refused.
Not done, on purpose: PDFs keep their document information and XMP (re-writing a PDF safely needs a PDF library; the active-content refusal is the control that matters), and the ICC profile is kept because removing it changes colours.

Why it matters: a phone photo carries the GPS position of the worker (PDPA: personal data nobody asked for) and the appended-bytes trick hides a second file behind a valid image.

## 3. The scanner hook (`IUploadScanner`)

```csharp
public interface IUploadScanner
{
    Task<UploadScanResult> ScanAsync(UploadScanRequest file, CancellationToken ct);
}
// UploadScanRequest(Stream Content, string FileName, FileKind Kind, long Length)
// UploadScanResult.Clean   |   UploadScanResult.Infected("signature name")
```

- Called once per upload, after the checks above, with a seekable stream at position 0 holding exactly the bytes that would be stored. The caller owns the stream: do not dispose it.
- `FileName` is the sanitised display name, never the client's raw name.
- `Infected(reason)`: the reason goes to the **server log only** (control characters removed, 200 characters at most); the client receives the generic `Messages.FileRejectedByScanner`.
- **Fail closed**: if the scanner cannot reach its engine it should throw. The upload then fails with the generic 500 and nothing is stored. A scanner that prefers to fail open must log that decision itself.
- Default: `NoOpUploadScanner` accepts everything (registered in `Infrastructure/DependencyInjection.cs`, `AddFileStorage`).

### Plugging in a real scanner (for example ClamAV)

1. Add a class in `Ims.Infrastructure` (for example `Scanning/ClamAvUploadScanner.cs`) that implements `IUploadScanner`, streams `file.Content` to the engine (clamd `INSTREAM`, or a vendor API) and maps the verdict to `UploadScanResult`.
2. Bind its settings to an options class validated at start-up (host, port, timeout), with the defaults in `appsettings.json` and secrets from the environment.
3. In `AddFileStorage` replace the line `services.AddSingleton<IUploadScanner, NoOpUploadScanner>();` with the new class (or choose by a setting such as `Storage:Scanner = None | ClamAv`).
4. Give it a timeout shorter than the request timeout for uploads (120 s) and keep the engine on an internal network only.
5. Tests: `tests/Ims.Api.Tests/Support/RecordingUploadScanner.cs` shows how to replace the scanner in a test host (`fixture.CreateHost(b => b.ConfigureTestServices(...))`).

A quarantine state (keep the file, mark it unavailable) is not needed while rejected files are never stored. Add it only if a scanner must run later than the upload (asynchronous scanning).

## 4. Tests

- `tests/Ims.Domain.Tests/Attachments/ImageMetadataStripperTests.cs`: each format with and without metadata, byte-for-byte for clean files, idempotence, broken and truncated files, the 1x1 PNG the smoke test uploads.
- `tests/Ims.Domain.Tests/Attachments/UploadScanResultTests.cs`.
- `tests/Ims.Api.Tests/Tests/Attachments/UploadSecurityTests.cs`: GPS EXIF gone from the downloaded bytes, magic-bytes-only files refused, default scanner is the no-op, plugged-in scanner sees the cleaned bytes, a refused file is never stored, a failing scanner fails closed, the scanner is not called for files that fail earlier checks.
