using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AltoBank.Services;

/// <summary>A checked upload, held in memory until it's saved: bytes, the real type (from its signature) and its SHA-256.</summary>
public sealed record UploadedImage(byte[] Bytes, string Extension, string ContentType, string Sha256);

/// <summary>Either an image that passed every check, or the message to show the customer.</summary>
public sealed record ImageCheck(UploadedImage? Image, string? Error)
{
    public bool Ok => Image != null;
}

/// <summary>
/// SHARED UPLOAD STORE (mobile check deposit now; dispute attachments later).
///  • Files live in App_Data/uploads (outside wwwroot, git-ignored; "Uploads:Root" can move it), so they can only
///    be reached through controller actions that check who is asking.
///  • Saved under a RANDOM name the server picks (32 hex chars + extension); the customer's file name is never used,
///    stored or trusted, so it can't overwrite another file or escape the folder.
///  • JPG and PNG only, decided by the file's first bytes (its "magic number"), not its name or Content-Type, so a
///    PDF or EXE renamed to .jpg is refused. 5 MB at most.
///  • Nothing is ever deleted: a file stays with the record that points to it.
/// </summary>
public interface IFileStore
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Reads and checks an upload (type by signature, size, not empty). Nothing is written yet.</summary>
    Task<ImageCheck> ReadImageAsync(IFormFile? file, string what);
    /// <summary>Writes a checked image under a new random name and returns that name.</summary>
    Task<string> SaveAsync(UploadedImage image);
    /// <summary>Opens a stored file for reading, or null if the name isn't one of ours or the file is missing.</summary>
    (Stream Stream, string ContentType)? Open(string storedName);
}

public sealed partial class FileStore : IFileStore
{
    private readonly string _root;

    public FileStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Uploads:Root"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(_root);
    }

    // JPEG starts FF D8 FF; PNG starts 89 50 4E 47 0D 0A 1A 0A.
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public async Task<ImageCheck> ReadImageAsync(IFormFile? file, string what)
    {
        if (file == null || file.Length == 0) return new(null, $"Add a photo of the {what}.");
        if (file.Length > IFileStore.MaxBytes)
            return new(null, $"The photo of the {what} is {file.Length / 1024d / 1024d:0.0} MB. Photos can be up to 5 MB; try a smaller photo.");

        // Read at most MaxBytes + 1, in case Length was wrong.
        using var buffer = new MemoryStream();
        await using (var input = file.OpenReadStream())
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(chunk)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > IFileStore.MaxBytes)
                    return new(null, $"The photo of the {what} is larger than 5 MB. Try a smaller photo.");
            }
        }
        var bytes = buffer.ToArray();

        (string Ext, string Type)? kind =
            StartsWith(bytes, Jpeg) ? (".jpg", "image/jpeg") :
            StartsWith(bytes, Png) ? (".png", "image/png") : null;
        if (kind == null)
            return new(null, $"The {what} must be a JPG or PNG photo. That file isn't one, whatever its name says.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(new UploadedImage(bytes, kind.Value.Ext, kind.Value.Type, hash), null);
    }

    public async Task<string> SaveAsync(UploadedImage image)
    {
        var name = Guid.NewGuid().ToString("N") + image.Extension;
        // FileMode.CreateNew: never overwrite an existing file.
        await using var stream = new FileStream(Path.Combine(_root, name), FileMode.CreateNew, FileAccess.Write);
        await stream.WriteAsync(image.Bytes);
        return name;
    }

    public (Stream Stream, string ContentType)? Open(string storedName)
    {
        // Only names this store creates are accepted, so a value like "../../appsettings.json" never reaches the disk.
        var m = StoredName().Match(storedName ?? "");
        if (!m.Success) return null;
        var path = Path.Combine(_root, storedName!);
        if (!File.Exists(path)) return null;
        var type = m.Groups[1].Value == "png" ? "image/png" : "image/jpeg";
        return (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), type);
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix) =>
        bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png)$")]
    private static partial Regex StoredName();
}
