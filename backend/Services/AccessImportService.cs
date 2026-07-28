using System.Diagnostics;
using System.Text.Json;
using System.Net;
using Microsoft.AspNetCore.Components.Forms;

public sealed class AccessImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const long MaxUploadBytes = 2_000_000_000;
    private const int CopyBufferSize = 1024 * 1024;

    private static readonly string[] CategoryColumns =
    [
        "Praktikum", "Hobi, kuća", "Turizam", "Enciklopedija, rečnik", "Katalog", "Priručnik",
        "Zbirka zadataka", "Zbornik radova", "Unikat", "Školski udžbenik", "Stručna literatura",
        "Popularno izdanje", "Antikvarna knjiga", "Beletristika", "Proza", "Drame, priče", "Roman",
        "Poezija", "Anglosaksonska literatura", "Francuska literatura", "Ruska literatura",
        "Nemačka literatura", "Dečja literatura", "Periodika", "Umetnost", "Slikarstvo",
        "Istorija, religija", "Društvene nauke", "Tehničke nauke", "Prirodne nauke", "Biologija",
        "Geografija", "Fizika", "Matematika", "Geometrija", "Lingvistika", "Arhitektura",
        "Građevinarstvo", "Vodosnabdevanje", "Urbanizam", "Hortikultura, biljke", "Medicina",
        "Kompjuteri", "Hemija", "Neorganska hemija", "Organska hemija", "Analitička hemija",
        "Instrumentalne metode", "Hromatografija", "Masena spektrometrija", "Fizička hemija",
        "Elektrohemija", "Biohemija", "Ekologija", "Hemijska tehnologija", "Hemijska sinteza",
        "Materijali", "Hemijska oprema", "Ruski"
    ];


    public async Task<string> StoreUploadedFileAsync(
        IBrowserFile file,
        Action<int>? reportUploadProgress = null)
    {
        var extension = Path.GetExtension(file.Name);
        if (!extension.Equals(".accdb", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mdb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Izaberite .accdb ili .mdb fajl.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"library-import-{Guid.NewGuid():N}{extension}");
        await CopyUploadedFile(file, tempPath, reportUploadProgress);
        return tempPath;
    }

    public async Task<IReadOnlyList<AccessBookPreview>> ReadUploadedPreviewAsync(
        IBrowserFile file,
        IReadOnlySet<int> existingBookIds,
        int count = 100,
        Action<int>? reportUploadProgress = null)
    {
        var tempPath = await StoreUploadedFileAsync(file, reportUploadProgress);
        try
        {
            return await ReadPreviewFromPathAsync(tempPath, existingBookIds, count);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public Task<IReadOnlyList<AccessBookPreview>> ReadPreviewFromPathAsync(string path, IReadOnlySet<int> existingBookIds, int count = 100) =>
        Task.Run<IReadOnlyList<AccessBookPreview>>(() => ReadMissingPreviewFromPath(path, existingBookIds, count));

    public Task<AccessBookImport?> ReadBookImportAsync(string path, int bookId) =>
        Task.Run(() => ReadBookImportFromPath(path, bookId));

    public Task<AccessImage?> ReadCoverAsync(string path, int bookId) =>
        Task.Run(() => ReadCoverFromPath(path, bookId));


    private static async Task CopyUploadedFile(IBrowserFile file, string tempPath, Action<int>? reportUploadProgress)
    {
        var totalBytes = Math.Max(file.Size, 1);
        var copiedBytes = 0L;
        var buffer = new byte[CopyBufferSize];

        await using var target = File.Create(tempPath);
        await using var source = file.OpenReadStream(MaxUploadBytes);
        reportUploadProgress?.Invoke(0);

        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read));
            copiedBytes += read;
            reportUploadProgress?.Invoke((int)Math.Min(100, copiedBytes * 100 / totalBytes));
        }

        reportUploadProgress?.Invoke(100);
    }

    private static List<AccessBookPreview> ReadMissingPreviewFromPath(string path, IReadOnlySet<int> existingBookIds, int count)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Access fajl nije pronadjen.", path);
        }

        var preview = RunReader<List<ReaderPreview>>("preview", path) ?? [];
        var books = new List<AccessBookPreview>();
        var requestedCount = Math.Clamp(count, 1, 500);

        foreach (var item in preview)
        {
            if (books.Count >= requestedCount)
            {
                break;
            }

            if (item.Id <= 0 || existingBookIds.Contains(item.Id))
            {
                continue;
            }

            books.Add(new AccessBookPreview(
                item.Id,
                ToText(item.Title),
                ToText(item.Authors),
                ToText(item.ImageFileName)));
        }

        return books;
    }

    private static AccessBookImport? ReadBookImportFromPath(string path, int bookId)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Access fajl nije pronadjen.", path);
        }

        var values = RunReader<Dictionary<string, JsonElement>?>("book", path, bookId);
        if (values is null)
        {
            return null;
        }
        var categories = CategoryColumns
            .Where(column => ToBool(Get(values, column)))
            .Concat(SplitAreas(ToText(Get(values, "Oblasti niz")))
                .Where(name => !name.Equals("domaći autor", StringComparison.OrdinalIgnoreCase))
                .Where(name => !name.Equals("strani autor", StringComparison.OrdinalIgnoreCase))
                .Where(name => !name.Equals("tvrdi povez", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AccessBookImport
        {
            Id = ToInt(Get(values, "ID Knjiga")),
            Title = ToText(Get(values, "Naslov")),
            Authors = SplitNames(ToText(Get(values, "Autori niz"))).ToList(),
            TitleNote = ToText(Get(values, "Primedba uz naslov")),
            Publisher = ToText(Get(values, "Izdavac")),
            Year = ToNullableInt(Get(values, "Godina izdanja")),
            PageCount = ToNullableInt(Get(values, "Broj strana")),
            VolumeCount = ToNullableInt(Get(values, "Broj tomova")) ?? 1,
            Languages = SplitNames(ToText(Get(values, "Jezik"))).ToList(),
            OriginalLanguages = SplitNames(ToText(Get(values, "Originalni jezik"))).ToList(),
            Scripts = SplitNames(ToText(Get(values, "Pismo"))).ToList(),
            Categories = categories,
            Isbn = ToText(Get(values, "ISBN")),
            Note = ToText(Get(values, "Primedba")),
            Prevod = ToBool(Get(values, "Prevod")),
            DomaciAutor = ToBool(Get(values, "Domaći autor")) || HasArea(values, "domaći autor"),
            StraniAutor = ToBool(Get(values, "Strani autor")) || HasArea(values, "strani autor"),
            TvrdiPovez = ToBool(Get(values, "Tvrdi povez")) || HasArea(values, "tvrdi povez"),
            Kolor = ToBool(Get(values, "Kolor")),
            Fotokopija = ToBool(Get(values, "Fotokopija")),
            Width = ToNullableDecimal(Get(values, "Širina (mm)")),
            Height = ToNullableDecimal(Get(values, "Visina (mm)")),
            Thickness = ToNullableDecimal(Get(values, "Debljina (mm)")),
            Copies = ToNullableInt(Get(values, "Broj primeraka")) ?? 1,
            Time = ToText(Get(values, "Vreme")),
            SlikaNepotrebna = ToBool(Get(values, "Slika nepotrebna")),
            SlikaVelika = ToBool(Get(values, "Velika slika")),
            SlikaUnutrasnja = ToBool(Get(values, "Slika unutrašnja")),
            ShelfId = ToNullableInt(Get(values, "ID Polica")),
            Cover = ReadCover(Get(values, "_cover"))
        };
    }

    private static bool HasArea(Dictionary<string, JsonElement> values, string area) =>
        SplitAreas(ToText(Get(values, "Oblasti niz"))).Any(name => name.Equals(area, StringComparison.OrdinalIgnoreCase));

    private static AccessImage? ReadCoverFromPath(string path, int bookId)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return ReadCover(RunReader<JsonElement?>("cover", path, bookId));
    }

    private static AccessImage? ReadCover(object? value)
    {
        if (value is not JsonElement element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var fileName = element.GetProperty("fileName").GetString() ?? "";
        var encoded = element.GetProperty("data").GetString();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        var bytes = ExtractImageBytes(Convert.FromBase64String(encoded));
        return bytes.Length == 0 ? null : new AccessImage(bytes, ContentTypeFor(fileName, bytes));
    }

    private static T? RunReader<T>(string command, string path, int? bookId = null)
    {
        var jarPath = Path.Combine(AppContext.BaseDirectory, "access-reader.jar");
        if (!File.Exists(jarPath))
        {
            throw new FileNotFoundException("Linux Access reader nije pronadjen.", jarPath);
        }

        var startInfo = new ProcessStartInfo("java")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jarPath);
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(path);
        if (bookId.HasValue)
        {
            startInfo.ArgumentList.Add(bookId.Value.ToString());
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Java Access reader nije mogao da se pokrene.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Access fajl nije mogao da se procita: {error.Trim()}");
        }

        return JsonSerializer.Deserialize<T>(output, JsonOptions);
    }

    private static IEnumerable<string> SplitNames(string value) =>
        value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToText)
            .Where(part => !string.IsNullOrWhiteSpace(part));

    private static IEnumerable<string> SplitAreas(string value) =>
        value.Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToText)
            .Where(part => !string.IsNullOrWhiteSpace(part));

    private static object? Get(Dictionary<string, JsonElement> values, string column) =>
        values.TryGetValue(column, out var value) ? value : null;

    private static byte[] ExtractImageBytes(byte[] rawBytes)
    {
        var offset = FindSignature(rawBytes, [0xFF, 0xD8]);
        if (offset < 0)
        {
            offset = FindSignature(rawBytes, [0x89, 0x50, 0x4E, 0x47]);
        }

        if (offset < 0)
        {
            return rawBytes;
        }

        return rawBytes[offset..];
    }

    private static int FindSignature(byte[] bytes, byte[] signature)
    {
        for (var index = 0; index <= bytes.Length - signature.Length; index++)
        {
            var match = true;
            for (var sigIndex = 0; sigIndex < signature.Length; sigIndex++)
            {
                if (bytes[index + sigIndex] != signature[sigIndex])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return index;
            }
        }

        return -1;
    }

    private static string ContentTypeFor(string fileName, byte[] bytes)
    {
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return "image/jpeg";
        }

        if (bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private static int ToInt(object? value) => ToNullableInt(value) ?? 0;

    private static int? ToNullableInt(object? value) => value switch
    {
        null or DBNull => null,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement element when element.TryGetInt32(out var number) => number,
        JsonElement element when int.TryParse(element.ToString(), out var number) => number,
        _ => Convert.ToInt32(value)
    };

    private static decimal? ToNullableDecimal(object? value) => value switch
    {
        null or DBNull => null,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement element when element.TryGetDecimal(out var number) => number,
        JsonElement element when decimal.TryParse(element.ToString(), out var number) => number,
        _ => Convert.ToDecimal(value)
    };

    private static bool ToBool(object? value) => value switch
    {
        null or DBNull => false,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined } => false,
        JsonElement element when element.TryGetInt32(out var number) => number != 0,
        JsonElement element when bool.TryParse(element.ToString(), out var boolean) => boolean,
        _ => Convert.ToBoolean(value)
    };

    private static string ToText(object? value)
    {
        var text = value switch
        {
            null or DBNull => "",
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => "",
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? "",
            JsonElement element => element.ToString(),
            _ => Convert.ToString(value) ?? ""
        };
        return WebUtility.HtmlDecode(text).Trim();
    }

    public static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp cleanup is best-effort; a locked temp file should not break the import workflow.
        }
    }
}

internal sealed record ReaderPreview(int Id, string Title, string Authors, string ImageFileName);

public sealed record AccessBookPreview(int Id, string Title, string Authors, string ImageFileName);
public sealed record AccessImage(byte[] Bytes, string ContentType);

public sealed class AccessBookImport
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<string> Authors { get; set; } = [];
    public string TitleNote { get; set; } = "";
    public string Publisher { get; set; } = "";
    public int? Year { get; set; }
    public int? PageCount { get; set; }
    public int? VolumeCount { get; set; }
    public List<string> Languages { get; set; } = [];
    public List<string> OriginalLanguages { get; set; } = [];
    public List<string> Scripts { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public string Isbn { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Prevod { get; set; }
    public bool DomaciAutor { get; set; }
    public bool StraniAutor { get; set; }
    public bool TvrdiPovez { get; set; }
    public bool Kolor { get; set; }
    public bool Fotokopija { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? Thickness { get; set; }
    public int? Copies { get; set; }
    public string Time { get; set; } = "";
    public bool SlikaNepotrebna { get; set; }
    public bool SlikaVelika { get; set; }
    public bool SlikaUnutrasnja { get; set; }
    public int? ShelfId { get; set; }
    public AccessImage? Cover { get; set; }
}