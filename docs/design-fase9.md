# Design Fase 9 & 9b: Keamanan (Enkripsi + Tanda Tangan)

**Menyusul requirement-fase9.md.** Keputusan klarifikasi (disetujui user):
1. Kerjakan **Fase 9 (enkripsi)** dan **Fase 9b (tanda tangan + incremental)** sekaligus.
2. Algoritma **semua**: tulis RC4-128 + AES-128 + AES-256; baca RC4-40/128 + AES-128 + AES-256.
3. RC4 manual wajib (untuk baca file lama).
4. Permission default = semua diizinkan.
5. R6: salt **random yang aman dan cepat** (`RandomNumberGenerator.Fill`, tidak deterministik khusus R6).
6. Nama API bebas (di bawah).

## 1. API publik

### Enkripsi (Fase 9, package inti)
```csharp
// LifeOS.Pdf
public enum PdfEncryptionAlgorithm { RC4_128, AES_128, AES_256 }

[Flags]
public enum PdfPermissions
{
    None = 0,
    Print = 1 << 2,
    Modify = 1 << 3,
    Copy = 1 << 4,
    Annotate = 1 << 5,
    FillForms = 1 << 8,
    ExtractAccessibility = 1 << 9,
    Assemble = 1 << 10,
    PrintHighQuality = 1 << 11,
    All = Print | Modify | Copy | Annotate | FillForms | ExtractAccessibility | Assemble | PrintHighQuality,
}

public sealed class PdfEncryptionOptions
{
    public string? UserPassword { get; set; }          // default null → password kosong
    public string? OwnerPassword { get; set; }         // default = user password
    public PdfEncryptionAlgorithm Algorithm { get; set; } = PdfEncryptionAlgorithm.AES_128;
    public PdfPermissions Permissions { get; set; } = PdfPermissions.All;
    public bool EncryptMetadata { get; set; } = true;
}

// PdfSaveOptions: + public PdfEncryptionOptions? Encryption { get; set; } + IncrementalUpdate
// PdfReadOptions:  + public string? Password { get; set; }
// PdfDocument:     + public PdfPermissions? Permissions { get; } + public bool IsEncrypted { get; }
```

### Tanda tangan (Fase 9b, package terpisah `LifeOS.Pdf.Signing`)
```csharp
// LifeOS.Pdf.Signing
public sealed class PdfSigner
{
    public PdfSigner(X509Certificate2 certificate);
    public string? Reason { get; set; }
    public string? Location { get; set; }
    public string? ContactInfo { get; set; }
    public void Sign(PdfDocument document, PdfPage page, PdfSignatureAppearance appearance);
    // → menghasilkan file ter-sign (signature field + /Contents CMS + ByteRange).
}

public sealed class PdfSignatureAppearance   // kotak visual (kiri-atas, point)
{
    public PdfSignatureAppearance(double x, double y, double width, double height);
    public string? Text { get; set; }        // mis. "Ditandatangani oleh X"
}

public sealed class PdfSignatureValidator
{
    public static PdfSignatureVerificationResult Verify(byte[] pdf);
}

public sealed class PdfSignatureVerificationResult
{
    public bool IsValid { get; }
    public string? SignerName { get; }
    public DateTimeOffset? SigningTime { get; }
    public bool CoversWholeDocument { get; }   // ByteRange menutup seluruh file
}
```

Contoh:
```csharp
var doc = new PdfDocument();
var page = doc.AddPage();
page.AddTextField("Nama", 72, 72, 200, 20).Value = "Budi";
page.AddSignatureField("Ttd", 400, 700, 160, 40);

var signer = new PdfSigner(cert) { Reason = "Persetujuan", Location = "Jakarta" };
signer.Sign(doc, page, new PdfSignatureAppearance(400, 700, 160, 40) { Text = "Ditandatangani" });
doc.Save("signed.pdf");
// atau signer mengembalikan byte[] hasil sign.
```

## 2. Desain internal — enkripsi

### 2.1 Algoritma kunci (§7.6.3)
- **File key (R2–R4)**: MD5(padded password) → (R3/4) 50× MD5(prev + /O + /P + /ID[0]) → truncate ke
  `/Length` byte (5–16). Untuk owner password: MD5 + 50× MD5 → RC4 dengan kunci → hash user password.
- **Object key**: fileKey + objectNumber (3 byte LE) + generation (2 byte LE) → MD5 → N byte pertama
  (N = min(16, fileKeyLen + 5)).
- **RC4**: stream cipher manual (~20 baris). String: RC4(key) XOR data. Stream: sama.
- **AES-128 (R4, /V 4 /CF /AESV2)**: AES-CBC, IV 16 byte acak per objek, data di-pad PKCS#7.
- **AES-256 (R6, PDF 2.0)**: password → SHA-256/384/512 + AES-CBC (Algorithm 2.A/2.B). `/O` `/U` `/OE`
  `/UE` 32 byte + hash validasi; `/Perms` (dienkripsi dengan kunci dari file key, memuat permission).
- **Permutation permission** (§7.6.3.2): `P` = 0xFFFFF000 | (perms << 0x0C) | ... — menulis nilai standar.

### 2.2 Integrasi writer (enkripsi saat serialize)
- `PdfOutput` dapat `SetEncryption(Encryptor? encryptor, int objectNumber, bool encrypt)`; sebelum menulis
  tiap objek, `DocumentWriter.Serialize` memanggilnya (objek `/Encrypt` → `encrypt=false`).
- `PdfString.WriteTo` dan `PdfStream.WriteTo` memeriksa konteks dan mengenkripsi data bila aktif.
  `/ID` di trailer dan `/Encrypt` tidak dienkripsi.
- Alur: bangun semua objek → hitung `/ID` (dari byte non-terenkripsi) → buat encryptor → tulis dengan
  enkripsi. Stream: kompres dulu (Filter), lalu enkripsi; `/Length` = panjang terenkripsi.

### 2.3 Integrasi reader (dekripsi saat parse)
- `PdfParser` membaca `/Encrypt` → `SecurityHandler.Open(...)` menurunkan file key (butuh `Password`).
- `ObjectParser` mendekripsi string (objek number+generation diketahui); `StreamDecoder` mendekripsi
  data stream **sebelum** filter decode. Gagal/`/R` tidak dikenal → `PdfEncryptedException`/`PdfReadException`.

## 3. Desain internal — incremental save (prasyarat Fase 9b)

- `PdfSaveOptions.IncrementalUpdate` (default false). Bila true dan dokumen berasal dari `Open` (punya
  byte asli), tulis ulang hanya objek yang berubah + append bagian baru ke file asli (§7.5.6):
  header asli dipertahankan, objek baru/berubah ditulis di akhir, xref-section baru + trailer baru dengan
  `/Prev` menunjuk xref lama.
- Untuk Fase 9b dipakai saat menambah signature ke file yang sudah ada. Implementasi awal mendukung
  append objek baru + update `startxref`/`/Prev`.

## 4. Desain internal — tanda tangan (Fase 9b)

- **Signature field + dictionary** (§12.8): `/Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached`
  `/Contents <CMS hex>` `/ByteRange [0 a b c]` `/M` `/Name` `/Reason` `/Location`; field `/FT /Sig`
  dengan `/V` → signature dictionary, widget di halaman.
- **CMS/PKCS#7** (`System.Security.Cryptography.Pkcs.CmsSigner`, detached) — dependency package terpisah,
  lisensi Microsoft (MIT-style) dicatat.
- **ByteRange & /Contents placeholder**: tulis `/Contents` sebagai `<00…00>` berukuran tetap (mis. 16 KB);
  hitung `/ByteRange` = [0, offsetContents, offsetContents+len, eof]; tandatangani byte di luar gap;
  sisipkan hex CMS ke gap. Untuk file yang baru dibuat, cukup rewrite (tanpa incremental); untuk file
  yang sudah ter-sign, append via incremental.
- **Verifikasi**: `SignedCms.Decode/CheckSignature`, cek `/ByteRange` menutup seluruh file (atau lapor
  cakupan), ekstrak signer + waktu.

## 5. Struktur file

```
src/LifeOS.Pdf/
  PdfEncryptionOptions.cs, PdfPermissions.cs, PdfEncryptionAlgorithm.cs   (baru, API publik)
  Internal/SecurityHandler.cs   (RC4 manual + AES + MD5/SHA; buat & buka kunci)
  Internal/Encryptor.cs         (konteks enkripsi objek; dipakai PdfOutput/PdfString/PdfStream)
  Internal/PdfOutput.cs         (ubah: konteks enkripsi)
  Objects/PdfString.cs, PdfStream.cs (ubah: enkripsi saat WriteTo)
  Internal/DocumentWriter.cs    (ubah: opsi enkripsi + incremental)
  Parsing/PdfParser.cs, ObjectParser.cs, StreamDecoder.cs (ubah: dekripsi)
src/LifeOS.Pdf.Signing/          (baru, package terpisah, target net6.0+)
  LifeOS.Pdf.Signing.csproj      (dependency: System.Security.Cryptography.Pkcs)
  PdfSigner.cs, PdfSignatureAppearance.cs, PdfSignatureValidator.cs, PdfSignatureVerificationResult.cs
  Internal/SignatureWriter.cs    (field + dictionary + ByteRange + placeholder + insert CMS)
```

## 6. Kompatibilitas target framework

- Enkripsi di package inti: RC4 manual + `System.Security.Cryptography` (Aes, MD5, SHA256/384/512) —
  semua tersedia di netstandard2.0. Tanpa dependency baru.
- `LifeOS.Pdf.Signing` target **net6.0+** (minimal) karena `System.Security.Cryptography.Pkcs` tidak
  tersedia di netstandard2.0; dicatat di README. (Netstandard2.0 bisa lewat BouncyCastle — backlog.)

## 7. Strategi test

- **Enkripsi**: unit test round-trip per algoritma (tulis terenkripsi → buka dengan password → teks sama);
  cek `/Encrypt` + `/O`/`/U`/`/P`; password salah → exception; password kosong; `EncryptMetadata` off;
  string vs stream; determinisme AES-128 (non-R6); permission.
- **Validasi eksternal**: `qpdf --password=X --decrypt` berhasil; pypdf buka dengan password; PDFium
  render sama. `tools/validate_pdf.py` tidak bisa membaca terenkripsi → uji dekripsi via qpdf.
- **Signing**: buat self-signed cert (test) → sign → `PdfSignatureValidator.Verify` valid; tanda tangan
  rusak → invalid; `/ByteRange` benar; appearance visual; file ter-sign valid (PdfStructure + qpdf).
- **Incremental**: buka → ubah → `IncrementalUpdate` → buka lagi → perubahan ada, `%PDF` asli + `/Prev`.

## 8. Risiko

- **AES-256/R6** paling rumit (2.A/2.B + /Perms) — uji lintas dengan qpdf (`--encrypt` 256-bit).
- **Incremental save** bisa merusak xref — mitigasi: validasi ketat + `PdfStructure`/qpdf.
- **CMS/Pkcs** di netstandard2.0 tidak ada → package signing net6.0+; sertifikat uji self-signed.
- **LTV (OCSP/CRL/embedded timestamps)** sangat besar → Fase 9b hanya B-B (+ B-T timestamp bila sempat);
  LTV ditunda.
