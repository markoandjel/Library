using System.Data;
using System.Globalization;
using System.Text.Json;
using Library.Api.Components;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using MySqlConnector;
using MudBlazor.Services;
using Renci.SshNet;
using Scalar.AspNetCore;

Library.Api.DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });
builder.Services.AddMudServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<Db>();
builder.Services.Configure<SftpSettings>(builder.Configuration.GetSection("Sftp"));
builder.Services.AddSingleton<SftpImageService>();
builder.Services.AddSingleton<AccessImportService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.UseSwagger();
app.UseSwaggerUI();
app.MapScalarApiReference(options =>
{
    options.WithTitle("Library API");
    options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
    options.WithTheme(ScalarTheme.Kepler);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = ".NET API running" }))
    .WithTags("Health");
app.MapPost("/access-uploads", (AccessUploadStart payload) =>
{
    var extension = Path.GetExtension(payload.FileName);
    if (!extension.Equals(".accdb", StringComparison.OrdinalIgnoreCase) &&
        !extension.Equals(".mdb", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Izaberite .accdb ili .mdb fajl." });
    }

    if (payload.Size <= 0 || payload.Size > 2_000_000_000)
    {
        return Results.BadRequest(new { error = "Velicina Access fajla nije dozvoljena." });
    }

    var root = AccessUploadRoot();
    Directory.CreateDirectory(root);
    var id = Guid.NewGuid().ToString("N");
    using (File.Create(AccessUploadPartPath(root, id))) { }
    File.WriteAllText(
        AccessUploadMetaPath(root, id),
        JsonSerializer.Serialize(new AccessUploadMetadata(Path.GetFileName(payload.FileName), payload.Size)));
    return Results.Ok(new { id, receivedBytes = 0L });
}).WithTags("access-import");

app.MapPut("/access-uploads/{id}", async Task<IResult> (string id, long offset, HttpContext context) =>
{
    if (!Guid.TryParseExact(id, "N", out _))
    {
        return Results.BadRequest(new { error = "Neispravan upload ID." });
    }

    var root = AccessUploadRoot();
    var partPath = AccessUploadPartPath(root, id);
    var metaPath = AccessUploadMetaPath(root, id);
    if (!File.Exists(partPath) || !File.Exists(metaPath))
    {
        return Results.NotFound(new { error = "Upload sesija nije pronadjena." });
    }

    var metadata = JsonSerializer.Deserialize<AccessUploadMetadata>(await File.ReadAllTextAsync(metaPath));
    if (metadata is null)
    {
        return Results.BadRequest(new { error = "Upload sesija je ostecena." });
    }

    await using var target = new FileStream(
        partPath, FileMode.Open, FileAccess.Write, FileShare.Read,
        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (target.Length != offset)
    {
        return Results.Conflict(new { receivedBytes = target.Length });
    }

    if (context.Request.ContentLength is > 16_777_216)
    {
        return Results.BadRequest(new { error = "Upload segment je prevelik." });
    }

    target.Position = offset;
    await context.Request.Body.CopyToAsync(target);
    await target.FlushAsync();
    if (target.Length > metadata.Size)
    {
        target.SetLength(offset);
        return Results.BadRequest(new { error = "Primljeno je vise podataka od velicine fajla." });
    }

    return Results.Ok(new { receivedBytes = target.Length });
}).DisableAntiforgery().WithTags("access-import");

app.MapPost("/access-uploads/{id}/complete", async Task<IResult> (string id) =>
{
    if (!Guid.TryParseExact(id, "N", out _))
    {
        return Results.BadRequest(new { error = "Neispravan upload ID." });
    }

    var root = AccessUploadRoot();
    var partPath = AccessUploadPartPath(root, id);
    var metaPath = AccessUploadMetaPath(root, id);
    if (!File.Exists(partPath) || !File.Exists(metaPath))
    {
        return Results.NotFound(new { error = "Upload sesija nije pronadjena." });
    }

    var metadata = JsonSerializer.Deserialize<AccessUploadMetadata>(await File.ReadAllTextAsync(metaPath));
    var actualSize = new FileInfo(partPath).Length;
    if (metadata is null || actualSize != metadata.Size)
    {
        return Results.BadRequest(new { error = $"Upload nije kompletan ({actualSize}/{metadata?.Size ?? 0} bajtova)." });
    }

    var extension = Path.GetExtension(metadata.FileName).ToLowerInvariant();
    var completedPath = Path.Combine(root, $"{id}{extension}");
    File.Move(partPath, completedPath, true);
    File.Delete(metaPath);
    return Results.Ok(new { id, path = completedPath, fileName = metadata.FileName, size = actualSize });
}).DisableAntiforgery().WithTags("access-import");

app.MapDelete("/access-uploads/{id}", (string id) =>
{
    if (!Guid.TryParseExact(id, "N", out _))
    {
        return Results.BadRequest(new { error = "Neispravan upload ID." });
    }

    var root = AccessUploadRoot();
    TryDeleteUploadFile(AccessUploadPartPath(root, id));
    TryDeleteUploadFile(AccessUploadMetaPath(root, id));
    return Results.NoContent();
}).DisableAntiforgery().WithTags("access-import");
app.MapGet("/book-cover/{bookId:int}", async Task<IResult> (int bookId, Db db, SftpImageService images, IWebHostEnvironment env) =>
{
    var row = await db.SingleAsync(
        "SELECT naziv FROM slika WHERE knjiga_id = @book_id ORDER BY id LIMIT 1",
        new { BookId = bookId });

    if (row is not null)
    {
        var fileName = Convert.ToString(row["naziv"], CultureInfo.InvariantCulture);
        var image = await Task.Run(() => images.TryDownloadBookImage(fileName, bookId));
        if (image is not null)
        {
            return Results.File(image.Bytes, image.ContentType);
        }
    }

    var placeholderPath = Path.Combine(env.WebRootPath, "images", "book-placeholder.svg");
    return Results.File(placeholderPath, "image/svg+xml");
}).WithTags("images");

app.MapGet("/access-uploads/{id}/book-cover/{bookId:int}", async Task<IResult> (string id, int bookId, AccessImportService accessImport, IWebHostEnvironment env) =>
{
    if (!Guid.TryParseExact(id, "N", out _))
    {
        return Results.BadRequest(new { error = "Neispravan upload ID." });
    }

    var root = AccessUploadRoot();
    var accessPath = new[] { ".accdb", ".mdb" }
        .Select(extension => Path.Combine(root, id + extension))
        .FirstOrDefault(File.Exists);
    if (accessPath is null)
    {
        return Results.NotFound(new { error = "Uploadovani Access fajl nije pronadjen u containeru." });
    }

    var image = await accessImport.ReadCoverAsync(accessPath, bookId);
    if (image is not null)
    {
        return Results.File(image.Bytes, image.ContentType);
    }

    return Results.File(Path.Combine(env.WebRootPath, "images", "book-placeholder.svg"), "image/svg+xml");
}).WithTags("images");
app.MapGet("/cabinet-image/{cabinetId:int}", async Task<IResult> (int cabinetId, Db db, SftpImageService images, IWebHostEnvironment env) =>
{
    var row = await db.SingleAsync("SELECT slika FROM orman WHERE id = @cabinet_id", new { CabinetId = cabinetId });
    if (row is not null)
    {
        var fileName = Convert.ToString(row["slika"], CultureInfo.InvariantCulture);
        var image = await Task.Run(() => images.TryDownloadCabinetImage(fileName));
        if (image is not null)
        {
            return Results.File(image.Bytes, image.ContentType);
        }
    }

    var placeholderPath = Path.Combine(env.WebRootPath, "images", "book-placeholder.svg");
    return Results.File(placeholderPath, "image/svg+xml");
}).WithTags("images");

MapNamedCrud(app, "/authors", "authors", "autor", "ime", "ime");
MapNamedCrud(app, "/languages", "languages", "jezik", "naziv", "naziv");
MapNamedCrud(app, "/publishers", "publishers", "izdavac", "naziv", "naziv");
MapCategories(app);
MapLetters(app);
MapCabinets(app);
MapShelves(app);
MapBooks(app);

app.Run();
static string AccessUploadRoot() =>
    Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } directory
        ? Path.GetFullPath(directory)
        : Path.Combine(Path.GetTempPath(), "library-imports");

static string AccessUploadPartPath(string root, string id) => Path.Combine(root, $"{id}.part");
static string AccessUploadMetaPath(string root, string id) => Path.Combine(root, $"{id}.json");

static void TryDeleteUploadFile(string path)
{
    try
    {
        if (File.Exists(path)) File.Delete(path);
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

static void MapNamedCrud(WebApplication app, string route, string tag, string table, string dbColumn, string apiProperty)
{
    app.MapGet(route, async (Db db) =>
        Results.Ok(await db.QueryAsync($"SELECT id, {dbColumn} AS {apiProperty} FROM {table} ORDER BY id")))
        .WithTags(tag);

    app.MapGet($"{route}/{{id:int}}", async (int id, Db db) =>
    {
        var row = await db.SingleAsync($"SELECT id, {dbColumn} AS {apiProperty} FROM {table} WHERE id = @id", new { id });
        return row is null ? Results.NotFound(new { detail = $"{tag} item with id={id} not found" }) : Results.Ok(row);
    }).WithTags(tag);

    app.MapPost(route, async (NamedPayload payload, Db db) =>
    {
        var value = payload.Value(apiProperty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Results.BadRequest(new { detail = $"{apiProperty} is required" });
        }

        var duplicate = await db.SingleAsync($"SELECT id FROM {table} WHERE LOWER({dbColumn}) = LOWER(@value)", new { value });
        if (duplicate is not null)
        {
            return Results.BadRequest(new { detail = $"{tag} item with this value already exists." });
        }

        var id = await db.InsertAsync($"INSERT INTO {table} ({dbColumn}) VALUES (@value)", new { value });
        var row = await db.SingleAsync($"SELECT id, {dbColumn} AS {apiProperty} FROM {table} WHERE id = @id", new { id });
        return Results.Created($"{route}/{id}", row);
    }).WithTags(tag);

    app.MapPut($"{route}/{{id:int}}", async (int id, NamedPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync($"SELECT id FROM {table} WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"{tag} item with id={id} not found" });
        }

        var value = payload.Value(apiProperty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Results.BadRequest(new { detail = $"{apiProperty} is required" });
        }

        await db.ExecuteAsync($"UPDATE {table} SET {dbColumn} = @value WHERE id = @id", new { value, id });
        var row = await db.SingleAsync($"SELECT id, {dbColumn} AS {apiProperty} FROM {table} WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags(tag);

    app.MapPatch($"{route}/{{id:int}}", async (int id, NamedPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync($"SELECT id, {dbColumn} AS {apiProperty} FROM {table} WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"{tag} item with id={id} not found" });
        }

        var value = payload.Value(apiProperty);
        if (!string.IsNullOrWhiteSpace(value))
        {
            await db.ExecuteAsync($"UPDATE {table} SET {dbColumn} = @value WHERE id = @id", new { value, id });
        }

        var row = await db.SingleAsync($"SELECT id, {dbColumn} AS {apiProperty} FROM {table} WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags(tag);

    app.MapDelete($"{route}/{{id:int}}", async (int id, Db db) =>
    {
        var affected = await db.ExecuteAsync($"DELETE FROM {table} WHERE id = @id", new { id });
        return affected == 0 ? Results.NotFound(new { detail = $"{tag} item with id={id} not found" }) : Results.NoContent();
    }).WithTags(tag);
}

static void MapCategories(WebApplication app)
{
    app.MapGet("/categories", async (Db db) =>
        Results.Ok(await db.QueryAsync("SELECT id, naziv, opis FROM kategorija ORDER BY id")))
        .WithTags("categories");

    app.MapGet("/categories/{id:int}", async (int id, Db db) =>
    {
        var row = await db.SingleAsync("SELECT id, naziv, opis FROM kategorija WHERE id = @id", new { id });
        return row is null ? Results.NotFound(new { detail = $"Category with id={id} not found" }) : Results.Ok(row);
    }).WithTags("categories");

    app.MapPost("/categories", async (CategoryPayload payload, Db db) =>
    {
        if (string.IsNullOrWhiteSpace(payload.Naziv))
        {
            return Results.BadRequest(new { detail = "naziv is required" });
        }

        var duplicate = await db.SingleAsync("SELECT id FROM kategorija WHERE LOWER(naziv) = LOWER(@naziv)", new { payload.Naziv });
        if (duplicate is not null)
        {
            return Results.BadRequest(new { detail = "Category with this name already exists." });
        }

        var id = await db.InsertAsync("INSERT INTO kategorija (naziv, opis) VALUES (@naziv, @opis)", payload);
        var row = await db.SingleAsync("SELECT id, naziv, opis FROM kategorija WHERE id = @id", new { id });
        return Results.Created($"/categories/{id}", row);
    }).WithTags("categories");

    app.MapPut("/categories/{id:int}", async (int id, CategoryPayload payload, Db db) =>
    {
        if (string.IsNullOrWhiteSpace(payload.Naziv))
        {
            return Results.BadRequest(new { detail = "naziv is required" });
        }

        var affected = await db.ExecuteAsync("UPDATE kategorija SET naziv = @naziv, opis = @opis WHERE id = @id", new { payload.Naziv, payload.Opis, id });
        if (affected == 0)
        {
            return Results.NotFound(new { detail = $"Category with id={id} not found" });
        }

        var row = await db.SingleAsync("SELECT id, naziv, opis FROM kategorija WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("categories");

    app.MapPatch("/categories/{id:int}", async (int id, CategoryPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync("SELECT id, naziv, opis FROM kategorija WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"Category with id={id} not found" });
        }

        await db.ExecuteAsync(
            "UPDATE kategorija SET naziv = COALESCE(@naziv, naziv), opis = COALESCE(@opis, opis) WHERE id = @id",
            new { payload.Naziv, payload.Opis, id });
        var row = await db.SingleAsync("SELECT id, naziv, opis FROM kategorija WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("categories");

    app.MapDelete("/categories/{id:int}", async (int id, Db db) =>
    {
        var affected = await db.ExecuteAsync("DELETE FROM kategorija WHERE id = @id", new { id });
        return affected == 0 ? Results.NotFound(new { detail = $"Category with id={id} not found" }) : Results.NoContent();
    }).WithTags("categories");
}

static void MapLetters(WebApplication app)
{
    app.MapGet("/letters", async (Db db) =>
        Results.Ok(await db.QueryAsync("SELECT id, naziv AS pismo FROM pismo ORDER BY id")))
        .WithTags("letters");

    app.MapGet("/letters/{id:int}", async (int id, Db db) =>
    {
        var row = await db.SingleAsync("SELECT id, naziv AS pismo FROM pismo WHERE id = @id", new { id });
        return row is null ? Results.NotFound(new { detail = $"Letter with id={id} not found" }) : Results.Ok(row);
    }).WithTags("letters");

    app.MapPost("/letters", async (LetterPayload payload, Db db) =>
    {
        if (string.IsNullOrWhiteSpace(payload.Pismo))
        {
            return Results.BadRequest(new { detail = "pismo is required" });
        }

        var duplicate = await db.SingleAsync("SELECT id FROM pismo WHERE LOWER(naziv) = LOWER(@pismo)", payload);
        if (duplicate is not null)
        {
            return Results.BadRequest(new { detail = "Letter with this name already exists." });
        }

        var id = await db.InsertAsync("INSERT INTO pismo (naziv) VALUES (@pismo)", payload);
        var row = await db.SingleAsync("SELECT id, naziv AS pismo FROM pismo WHERE id = @id", new { id });
        return Results.Created($"/letters/{id}", row);
    }).WithTags("letters");

    app.MapPut("/letters/{id:int}", async (int id, LetterPayload payload, Db db) =>
    {
        if (string.IsNullOrWhiteSpace(payload.Pismo))
        {
            return Results.BadRequest(new { detail = "pismo is required" });
        }

        var affected = await db.ExecuteAsync("UPDATE pismo SET naziv = @pismo WHERE id = @id", new { payload.Pismo, id });
        if (affected == 0)
        {
            return Results.NotFound(new { detail = $"Letter with id={id} not found" });
        }

        var row = await db.SingleAsync("SELECT id, naziv AS pismo FROM pismo WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("letters");

    app.MapPatch("/letters/{id:int}", async (int id, LetterPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync("SELECT id, naziv AS pismo FROM pismo WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"Letter with id={id} not found" });
        }

        if (!string.IsNullOrWhiteSpace(payload.Pismo))
        {
            await db.ExecuteAsync("UPDATE pismo SET naziv = @pismo WHERE id = @id", new { payload.Pismo, id });
        }

        var row = await db.SingleAsync("SELECT id, naziv AS pismo FROM pismo WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("letters");

    app.MapDelete("/letters/{id:int}", async (int id, Db db) =>
    {
        var affected = await db.ExecuteAsync("DELETE FROM pismo WHERE id = @id", new { id });
        return affected == 0 ? Results.NotFound(new { detail = $"Letter with id={id} not found" }) : Results.NoContent();
    }).WithTags("letters");
}

static void MapCabinets(WebApplication app)
{
    const string columns = "id, naziv, transparentnost, slika";

    app.MapGet("/cabinets", async (Db db) =>
        Results.Ok(await db.QueryAsync($"SELECT {columns} FROM orman ORDER BY naziv")))
        .WithTags("cabinets");

    app.MapGet("/cabinets/{id:int}", async (int id, Db db) =>
    {
        var row = await db.SingleAsync($"SELECT {columns} FROM orman WHERE id = @id", new { id });
        return row is null ? Results.NotFound(new { detail = $"Cabinet with id={id} not found" }) : Results.Ok(row);
    }).WithTags("cabinets");

    app.MapPost("/cabinets", async (CabinetPayload payload, Db db) =>
    {
        var id = await db.InsertAsync(
            "INSERT INTO orman (naziv, transparentnost, slika) VALUES (@naziv, @transparentnost, @slika)",
            payload);
        var row = await db.SingleAsync($"SELECT {columns} FROM orman WHERE id = @id", new { id });
        return Results.Created($"/cabinets/{id}", row);
    }).WithTags("cabinets");

    app.MapPut("/cabinets/{id:int}", async (int id, CabinetPayload payload, Db db) =>
    {
        var affected = await db.ExecuteAsync(
            "UPDATE orman SET naziv = @naziv, transparentnost = @transparentnost, slika = @slika WHERE id = @id",
            new { payload.Naziv, payload.Transparentnost, payload.Slika, id });
        if (affected == 0)
        {
            return Results.NotFound(new { detail = $"Cabinet with id={id} not found" });
        }

        var row = await db.SingleAsync($"SELECT {columns} FROM orman WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("cabinets");

    app.MapPatch("/cabinets/{id:int}", async (int id, CabinetPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync($"SELECT {columns} FROM orman WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"Cabinet with id={id} not found" });
        }

        await db.ExecuteAsync(
            """
            UPDATE orman
            SET naziv = COALESCE(@naziv, naziv),
                transparentnost = COALESCE(@transparentnost, transparentnost),
                slika = COALESCE(@slika, slika)
            WHERE id = @id
            """,
            new { payload.Naziv, payload.Transparentnost, payload.Slika, id });
        var row = await db.SingleAsync($"SELECT {columns} FROM orman WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("cabinets");

    app.MapDelete("/cabinets/{id:int}", async (int id, Db db) =>
    {
        var affected = await db.ExecuteAsync("DELETE FROM orman WHERE id = @id", new { id });
        return affected == 0 ? Results.NotFound(new { detail = $"Cabinet with id={id} not found" }) : Results.NoContent();
    }).WithTags("cabinets");
}

static void MapShelves(WebApplication app)
{
    const string columns = "id, x, y, orman_id";

    app.MapGet("/shelves", async (Db db) =>
        Results.Ok(await db.QueryAsync($"SELECT {columns} FROM polica ORDER BY orman_id, y, x")))
        .WithTags("shelves");

    app.MapGet("/shelves/{id:int}", async (int id, Db db) =>
    {
        var row = await db.SingleAsync($"SELECT {columns} FROM polica WHERE id = @id", new { id });
        return row is null ? Results.NotFound(new { detail = $"Shelf with id={id} not found" }) : Results.Ok(row);
    }).WithTags("shelves");

    app.MapPost("/shelves", async (ShelfPayload payload, Db db) =>
    {
        if (!await ExistsAsync(db, "orman", payload.OrmanId))
        {
            return Results.BadRequest(new { detail = $"Cabinet with id={payload.OrmanId} does not exist." });
        }

        var id = await db.InsertAsync("INSERT INTO polica (x, y, orman_id) VALUES (@x, @y, @orman_id)", payload);
        var row = await db.SingleAsync($"SELECT {columns} FROM polica WHERE id = @id", new { id });
        return Results.Created($"/shelves/{id}", row);
    }).WithTags("shelves");

    app.MapPut("/shelves/{id:int}", async (int id, ShelfPayload payload, Db db) =>
    {
        if (!await ExistsAsync(db, "orman", payload.OrmanId))
        {
            return Results.BadRequest(new { detail = $"Cabinet with id={payload.OrmanId} does not exist." });
        }

        var affected = await db.ExecuteAsync(
            "UPDATE polica SET x = @x, y = @y, orman_id = @orman_id WHERE id = @id",
            new { payload.X, payload.Y, payload.OrmanId, id });
        if (affected == 0)
        {
            return Results.NotFound(new { detail = $"Shelf with id={id} not found" });
        }

        var row = await db.SingleAsync($"SELECT {columns} FROM polica WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("shelves");

    app.MapPatch("/shelves/{id:int}", async (int id, ShelfPatchPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync($"SELECT {columns} FROM polica WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"Shelf with id={id} not found" });
        }

        var ormanId = payload.OrmanId ?? Convert.ToInt32(existing["orman_id"]);
        if (!await ExistsAsync(db, "orman", ormanId))
        {
            return Results.BadRequest(new { detail = $"Cabinet with id={ormanId} does not exist." });
        }

        await db.ExecuteAsync(
            "UPDATE polica SET x = COALESCE(@x, x), y = COALESCE(@y, y), orman_id = @orman_id WHERE id = @id",
            new { payload.X, payload.Y, ormanId, id });
        var row = await db.SingleAsync($"SELECT {columns} FROM polica WHERE id = @id", new { id });
        return Results.Ok(row);
    }).WithTags("shelves");

    app.MapDelete("/shelves/{id:int}", async (int id, Db db) =>
    {
        var affected = await db.ExecuteAsync("DELETE FROM polica WHERE id = @id", new { id });
        return affected == 0 ? Results.NotFound(new { detail = $"Shelf with id={id} not found" }) : Results.NoContent();
    }).WithTags("shelves");
}

static void MapBooks(WebApplication app)
{
    const string columns = """
        id, naslov, primedba_naslov, izdavac_id, godina, broj_strana, jezik_id,
        originalni_jezik_id, pismo_id, prevod, isbn, primedba_knjiga, domaci_autor,
        strani_autor, tvrdi_povez, kolor, fotokopija, sirina, visina, debljina,
        broj_primeraka, vreme, slika_nepotrebna, slika_velika, slika_unutrasnja,
        knjiga_id, broj_tomova, polica_id
        """;

    app.MapGet("/books", async (Db db) =>
    {
        var books = await db.QueryAsync($"SELECT {columns} FROM knjiga ORDER BY naslov");
        await LoadBookRelations(db, books);
        return Results.Ok(books);
    }).WithTags("books");

    app.MapGet("/books/{id:int}", async (int id, Db db) =>
    {
        var book = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        if (book is null)
        {
            return Results.NotFound(new { detail = $"Book with id={id} not found" });
        }

        await LoadBookRelations(db, [book]);
        return Results.Ok(book);
    }).WithTags("books");

    app.MapPost("/books", async (BookPayload payload, Db db) =>
    {
        var id = await db.InsertAsync(Sql.BookInsert, payload.ToDbParams());
        await SyncAllRelations(db, payload.KnjigaId ?? id, payload);
        var book = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        await LoadBookRelations(db, [book!]);
        return Results.Created($"/books/{id}", book);
    }).WithTags("books");

    app.MapPost("/books/{id:int}", async (int id, BookPayload payload, Db db) =>
    {
        var existing = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"Book with id={id} not found" });
        }

        await db.ExecuteAsync(Sql.BookPostUpdate, payload.ToDbParams(id));
        var book = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        await SyncAllRelations(db, BookRelationId(book!), payload);
        await LoadBookRelations(db, [book!]);
        return Results.Ok(book);
    }).WithTags("books");

    app.MapPut("/books/{id:int}", async (int id, BookPayload payload, Db db) =>
    {
        if (!await ExistsAsync(db, "knjiga", id))
        {
            return Results.NotFound(new { detail = $"Book with id={id} not found" });
        }

        await db.ExecuteAsync(Sql.BookUpdate, payload.ToDbParams(id));
        await SyncAllRelations(db, payload.KnjigaId ?? id, payload);
        var book = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        await LoadBookRelations(db, [book!]);
        return Results.Ok(book);
    }).WithTags("books");

    app.MapPatch("/books/{id:int}", async (int id, BookPayload payload, Db db) =>
    {
        if (!await ExistsAsync(db, "knjiga", id))
        {
            return Results.NotFound(new { detail = $"Book with id={id} not found" });
        }

        await db.ExecuteAsync(Sql.BookPatch, payload.ToDbParams(id));
        var book = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        await SyncAllRelations(db, BookRelationId(book!), payload);
        await LoadBookRelations(db, [book!]);
        return Results.Ok(book);
    }).WithTags("books");

    app.MapDelete("/books/{id:int}", async (int id, Db db) =>
    {
        var existing = await db.SingleAsync($"SELECT {columns} FROM knjiga WHERE id = @id", new { id });
        if (existing is null)
        {
            return Results.NotFound(new { detail = $"Book with id={id} not found" });
        }

        var relationBookId = BookRelationId(existing);
        await db.ExecuteAsync("DELETE FROM kategorijaknjiga WHERE knjiga_id = @id", new { Id = relationBookId });
        await db.ExecuteAsync("DELETE FROM autorknjiga WHERE knjiga_id = @id", new { Id = relationBookId });
        await db.ExecuteAsync("DELETE FROM jezikknjiga WHERE knjiga_id = @id", new { Id = relationBookId });
        await db.ExecuteAsync("DELETE FROM jezikoriginalknjiga WHERE knjiga_id = @id", new { Id = relationBookId });
        await db.ExecuteAsync("DELETE FROM pismoknjiga WHERE knjiga_id = @id", new { Id = relationBookId });
        await db.ExecuteAsync("DELETE FROM slika WHERE knjiga_id = @id", new { Id = relationBookId });
        await db.ExecuteAsync("DELETE FROM knjiga WHERE id = @id", new { id });
        return Results.NoContent();
    }).WithTags("books");
}

static async Task<bool> ExistsAsync(Db db, string table, int id)
{
    var row = await db.SingleAsync($"SELECT id FROM {table} WHERE id = @id", new { id });
    return row is not null;
}

static async Task LoadBookRelations(Db db, List<Dictionary<string, object?>> books)
{
    if (books.Count == 0)
    {
        return;
    }

    var relationIds = books.Select(BookRelationId).ToArray();
    foreach (var book in books)
    {
        book["kategorija_ids"] = new List<int>();
        book["autor_ids"] = new List<int>();
        book["jezik_ids"] = new List<int>();
        book["jezik_orig_ids"] = new List<int>();
        book["pismo_ids"] = new List<int>();
        book["slike"] = new List<Dictionary<string, object?>>();
    }

    await AttachRelation(db, books, relationIds, "kategorijaknjiga", "kategorija_id", "kategorija_ids");
    await AttachRelation(db, books, relationIds, "autorknjiga", "autor_id", "autor_ids");
    await AttachRelation(db, books, relationIds, "jezikknjiga", "jezik_id", "jezik_ids");
    await AttachRelation(db, books, relationIds, "jezikoriginalknjiga", "jezik_original_id", "jezik_orig_ids");
    await AttachRelation(db, books, relationIds, "pismoknjiga", "pismo_id", "pismo_ids");

    var images = await db.QueryInAsync("SELECT id, naziv, knjiga_id FROM slika WHERE knjiga_id IN ({0})", relationIds);
    var byRelationId = books
        .GroupBy(BookRelationId)
        .ToDictionary(group => group.Key, group => group.First());
    foreach (var image in images)
    {
        var relationBookId = Convert.ToInt32(image["knjiga_id"]);
        if (byRelationId.TryGetValue(relationBookId, out var book))
        {
            ((List<Dictionary<string, object?>>)book["slike"]!).Add(image);
        }
    }
}

static async Task AttachRelation(Db db, List<Dictionary<string, object?>> books, int[] relationIds, string table, string idColumn, string outputKey)
{
    var rows = await db.QueryInAsync($"SELECT knjiga_id, {idColumn} FROM {table} WHERE knjiga_id IN ({{0}})", relationIds);
    var byRelationId = books
        .GroupBy(BookRelationId)
        .ToDictionary(group => group.Key, group => group.First());
    foreach (var row in rows)
    {
        var relationBookId = Convert.ToInt32(row["knjiga_id"]);
        if (byRelationId.TryGetValue(relationBookId, out var book))
        {
            ((List<int>)book[outputKey]!).Add(Convert.ToInt32(row[idColumn]));
        }
    }
}

static int BookRelationId(Dictionary<string, object?> book) =>
    book["knjiga_id"] is null ? Convert.ToInt32(book["id"]) : Convert.ToInt32(book["knjiga_id"]);

static async Task SyncAllRelations(Db db, int bookId, BookPayload payload)
{
    await SyncRelation(db, "kategorijaknjiga", "kategorija_id", bookId, payload.KategorijaIds);
    await SyncRelation(db, "autorknjiga", "autor_id", bookId, payload.AutorIds);
    await SyncRelation(db, "jezikknjiga", "jezik_id", bookId, payload.JezikIds);
    await SyncRelation(db, "jezikoriginalknjiga", "jezik_original_id", bookId, payload.JezikOrigIds);
    await SyncRelation(db, "pismoknjiga", "pismo_id", bookId, payload.PismoIds);
}

static async Task SyncRelation(Db db, string table, string idColumn, int bookId, IReadOnlyCollection<int>? ids)
{
    if (ids is null)
    {
        return;
    }

    var requestedIds = ids.Where(id => id > 0).Distinct().ToHashSet();
    var existingRows = await db.QueryAsync($"SELECT {idColumn} AS relation_id FROM {table} WHERE knjiga_id = @book_id", new { bookId });
    var existingIds = existingRows.Select(row => Convert.ToInt32(row["relation_id"])).ToHashSet();

    foreach (var id in existingIds.Except(requestedIds))
    {
        await db.ExecuteAsync($"DELETE FROM {table} WHERE knjiga_id = @book_id AND {idColumn} = @id", new { bookId, id });
    }

    foreach (var id in requestedIds.Except(existingIds))
    {
        await db.ExecuteAsync($"INSERT INTO {table} (knjiga_id, {idColumn}) VALUES (@book_id, @id)", new { bookId, id });
    }
}

sealed class Db(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("Library")
        ?? throw new InvalidOperationException("ConnectionStrings:Library is missing.");

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, object? parameters = null)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = BuildCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        return await ReadRows(reader);
    }

    public async Task<List<Dictionary<string, object?>>> QueryInAsync(string sqlFormat, IReadOnlyList<int> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var parameterNames = ids.Select((_, index) => $"@id{index}").ToArray();
        var sql = string.Format(sqlFormat, string.Join(", ", parameterNames));
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        for (var index = 0; index < ids.Count; index++)
        {
            command.Parameters.AddWithValue(parameterNames[index], ids[index]);
        }

        await using var reader = await command.ExecuteReaderAsync();
        return await ReadRows(reader);
    }

    public async Task<Dictionary<string, object?>?> SingleAsync(string sql, object? parameters = null)
    {
        var rows = await QueryAsync(sql, parameters);
        return rows.FirstOrDefault();
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = BuildCommand(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertAsync(string sql, object? parameters = null)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = BuildCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
        return (int)command.LastInsertedId;
    }

    private static MySqlCommand BuildCommand(MySqlConnection connection, string sql, object? parameters)
    {
        var command = new MySqlCommand(sql, connection);
        if (parameters is null)
        {
            return command;
        }

        foreach (var property in parameters.GetType().GetProperties())
        {
            var name = "@" + ToSnakeCase(property.Name);
            var value = property.GetValue(parameters) ?? DBNull.Value;
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    private static async Task<List<Dictionary<string, object?>>> ReadRows(MySqlDataReader reader)
    {
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string ToSnakeCase(string value)
    {
        return string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    }
}

sealed class NamedPayload
{
    public string? Ime { get; set; }
    public string? Naziv { get; set; }

    public string? Value(string property) => property.Equals("ime", StringComparison.OrdinalIgnoreCase) ? Ime : Naziv;
}

sealed class CategoryPayload
{
    public string? Naziv { get; set; }
    public string? Opis { get; set; }
}

sealed class LetterPayload
{
    public string? Pismo { get; set; }
}

sealed class CabinetPayload
{
    public string? Naziv { get; set; }
    public bool? Transparentnost { get; set; }
    public string? Slika { get; set; }
}

sealed class ShelfPayload
{
    public int X { get; set; }
    public int Y { get; set; }
    public int OrmanId { get; set; }
}

sealed class ShelfPatchPayload
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? OrmanId { get; set; }
}

sealed class BookPayload
{
    public string? Naslov { get; set; }
    public string? PrimedbaNaslov { get; set; }
    public int? IzdavacId { get; set; }
    public int? Godina { get; set; }
    public int? BrojStrana { get; set; }
    public int? JezikId { get; set; }
    public int? OriginalniJezikId { get; set; }
    public int? PismoId { get; set; }
    public bool? Prevod { get; set; }
    public string? Isbn { get; set; }
    public string? PrimedbaKnjiga { get; set; }
    public bool? DomaciAutor { get; set; }
    public bool? StraniAutor { get; set; }
    public bool? TvrdiPovez { get; set; }
    public bool? Kolor { get; set; }
    public bool? Fotokopija { get; set; }
    public decimal? Sirina { get; set; }
    public decimal? Visina { get; set; }
    public decimal? Debljina { get; set; }
    public int? BrojPrimeraka { get; set; }
    public string? Vreme { get; set; }
    public bool? SlikaNepotrebna { get; set; }
    public bool? SlikaVelika { get; set; }
    public bool? SlikaUnutrasnja { get; set; }
    public int? KnjigaId { get; set; }
    public int? BrojTomova { get; set; }
    public int? PolicaId { get; set; }
    public List<int>? KategorijaIds { get; set; }
    public List<int>? AutorIds { get; set; }
    public List<int>? JezikIds { get; set; }
    public List<int>? JezikOrigIds { get; set; }
    public List<int>? PismoIds { get; set; }

    public object ToDbParams(int? id = null) => new
    {
        Id = id,
        Naslov,
        PrimedbaNaslov,
        IzdavacId,
        Godina,
        BrojStrana,
        JezikId,
        OriginalniJezikId,
        PismoId,
        Prevod,
        Isbn,
        PrimedbaKnjiga,
        DomaciAutor,
        StraniAutor,
        TvrdiPovez,
        Kolor,
        Fotokopija,
        Sirina,
        Visina,
        Debljina,
        BrojPrimeraka,
        Vreme,
        SlikaNepotrebna,
        SlikaVelika,
        SlikaUnutrasnja,
        KnjigaId,
        BrojTomova,
        PolicaId
    };
}

static class Sql
{
    public const string BookInsert = """
        INSERT INTO knjiga (
            id, naslov, primedba_naslov, izdavac_id, godina, broj_strana, jezik_id,
            originalni_jezik_id, pismo_id, prevod, isbn, primedba_knjiga, domaci_autor,
            strani_autor, tvrdi_povez, kolor, fotokopija, sirina, visina, debljina,
            broj_primeraka, vreme, slika_nepotrebna, slika_velika, slika_unutrasnja,
            knjiga_id, broj_tomova, polica_id
        )
        VALUES (
            @id, @naslov, @primedba_naslov, @izdavac_id, @godina, @broj_strana, @jezik_id,
            @originalni_jezik_id, @pismo_id, @prevod, @isbn, @primedba_knjiga, @domaci_autor,
            @strani_autor, @tvrdi_povez, @kolor, @fotokopija, @sirina, @visina, @debljina,
            @broj_primeraka, @vreme, @slika_nepotrebna, @slika_velika, @slika_unutrasnja,
            @knjiga_id, @broj_tomova, @polica_id
        )
        """;

    public const string BookUpdate = """
        UPDATE knjiga SET
            naslov = @naslov,
            primedba_naslov = @primedba_naslov,
            izdavac_id = @izdavac_id,
            godina = @godina,
            broj_strana = @broj_strana,
            jezik_id = @jezik_id,
            originalni_jezik_id = @originalni_jezik_id,
            pismo_id = @pismo_id,
            prevod = @prevod,
            isbn = @isbn,
            primedba_knjiga = @primedba_knjiga,
            domaci_autor = @domaci_autor,
            strani_autor = @strani_autor,
            tvrdi_povez = @tvrdi_povez,
            kolor = @kolor,
            fotokopija = @fotokopija,
            sirina = @sirina,
            visina = @visina,
            debljina = @debljina,
            broj_primeraka = @broj_primeraka,
            vreme = @vreme,
            slika_nepotrebna = @slika_nepotrebna,
            slika_velika = @slika_velika,
            slika_unutrasnja = @slika_unutrasnja,
            knjiga_id = @knjiga_id,
            broj_tomova = @broj_tomova,
            polica_id = @polica_id
        WHERE id = @id
        """;

    public const string BookPatch = """
        UPDATE knjiga SET
            naslov = COALESCE(@naslov, naslov),
            primedba_naslov = COALESCE(@primedba_naslov, primedba_naslov),
            izdavac_id = COALESCE(@izdavac_id, izdavac_id),
            godina = COALESCE(@godina, godina),
            broj_strana = COALESCE(@broj_strana, broj_strana),
            jezik_id = COALESCE(@jezik_id, jezik_id),
            originalni_jezik_id = COALESCE(@originalni_jezik_id, originalni_jezik_id),
            pismo_id = COALESCE(@pismo_id, pismo_id),
            prevod = COALESCE(@prevod, prevod),
            isbn = COALESCE(@isbn, isbn),
            primedba_knjiga = COALESCE(@primedba_knjiga, primedba_knjiga),
            domaci_autor = COALESCE(@domaci_autor, domaci_autor),
            strani_autor = COALESCE(@strani_autor, strani_autor),
            tvrdi_povez = COALESCE(@tvrdi_povez, tvrdi_povez),
            kolor = COALESCE(@kolor, kolor),
            fotokopija = COALESCE(@fotokopija, fotokopija),
            sirina = COALESCE(@sirina, sirina),
            visina = COALESCE(@visina, visina),
            debljina = COALESCE(@debljina, debljina),
            broj_primeraka = COALESCE(@broj_primeraka, broj_primeraka),
            vreme = COALESCE(@vreme, vreme),
            slika_nepotrebna = COALESCE(@slika_nepotrebna, slika_nepotrebna),
            slika_velika = COALESCE(@slika_velika, slika_velika),
            slika_unutrasnja = COALESCE(@slika_unutrasnja, slika_unutrasnja),
            knjiga_id = COALESCE(@knjiga_id, knjiga_id),
            broj_tomova = COALESCE(@broj_tomova, broj_tomova),
            polica_id = COALESCE(@polica_id, polica_id)
        WHERE id = @id
        """;

    public const string BookPostUpdate = """
        UPDATE knjiga SET
            naslov = COALESCE(@naslov, naslov),
            primedba_naslov = @primedba_naslov,
            izdavac_id = @izdavac_id,
            godina = @godina,
            broj_strana = @broj_strana,
            jezik_id = @jezik_id,
            originalni_jezik_id = @originalni_jezik_id,
            pismo_id = @pismo_id,
            prevod = @prevod,
            isbn = @isbn,
            primedba_knjiga = @primedba_knjiga,
            domaci_autor = @domaci_autor,
            strani_autor = @strani_autor,
            tvrdi_povez = @tvrdi_povez,
            sirina = @sirina,
            visina = @visina,
            debljina = @debljina,
            broj_tomova = @broj_tomova
        WHERE id = @id
        """;
}

sealed class SftpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string BasePath { get; set; } = "/";
    public string BasePathOrmani { get; set; } = "/";
}

sealed class SftpImageService(IConfiguration configuration)
{
    private readonly SftpSettings _settings = configuration.GetSection("Sftp").Get<SftpSettings>() ?? new SftpSettings();

    public RemoteImage? TryDownloadBookImage(string? fileName, int bookId)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var safeFileName = Path.GetFileName(fileName);
        return TryDownloadImage(safeFileName, [
            CombineRemotePath(_settings.BasePath, safeFileName),
            CombineRemotePath(CombineRemotePath(_settings.BasePath, bookId.ToString(CultureInfo.InvariantCulture)), safeFileName)
        ], _settings.BasePath);
    }

    public RemoteImage? TryDownloadCabinetImage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var safeFileName = Path.GetFileName(fileName);
        return TryDownloadImage(safeFileName, [CombineRemotePath(_settings.BasePathOrmani, safeFileName)], _settings.BasePathOrmani);
    }

    public bool TryUploadCabinetImage(byte[] bytes, string fileName)
    {
        if (bytes.Length == 0 || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var safeFileName = Path.GetFileName(fileName);
        var remotePath = CombineRemotePath(_settings.BasePathOrmani, safeFileName);

        try
        {
            using var client = new SftpClient(_settings.Host, _settings.Port, _settings.Username, _settings.Password);
            client.Connect();
            EnsureDirectory(client, _settings.BasePathOrmani);
            using var stream = new MemoryStream(bytes);
            client.UploadFile(stream, remotePath, true);
            client.Disconnect();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool BookImageExists(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var safeFileName = Path.GetFileName(fileName);
        try
        {
            using var client = new SftpClient(_settings.Host, _settings.Port, _settings.Username, _settings.Password);
            client.Connect();
            var exists = client.Exists(CombineRemotePath(_settings.BasePath, safeFileName));
            client.Disconnect();
            return exists;
        }
        catch
        {
            return true;
        }
    }

    public bool TryUploadBookImage(byte[] bytes, string fileName)
    {
        if (bytes.Length == 0 || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var safeFileName = Path.GetFileName(fileName);
        var remotePath = CombineRemotePath(_settings.BasePath, safeFileName);

        try
        {
            using var client = new SftpClient(_settings.Host, _settings.Port, _settings.Username, _settings.Password);
            client.Connect();
            EnsureDirectory(client, _settings.BasePath);
            if (client.Exists(remotePath))
            {
                client.Disconnect();
                return false;
            }

            using var stream = new MemoryStream(bytes);
            client.UploadFile(stream, remotePath, false);
            client.Disconnect();
            return true;
        }
        catch
        {
            return false;
        }
    }
    private RemoteImage? TryDownloadImage(string safeFileName, IReadOnlyList<string> candidates, string basePath)
    {
        try
        {
            using var client = new SftpClient(_settings.Host, _settings.Port, _settings.Username, _settings.Password);
            client.Connect();

            var remotePath = FindRemoteImagePath(client, safeFileName, candidates, basePath);
            if (remotePath is null)
            {
                client.Disconnect();
                return null;
            }

            using var stream = new MemoryStream();
            client.DownloadFile(remotePath, stream);
            client.Disconnect();

            return new RemoteImage(stream.ToArray(), ContentTypeFor(safeFileName));
        }
        catch
        {
            return null;
        }
    }

    private static string CombineRemotePath(string basePath, string fileName)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(basePath) ? "/" : basePath.Replace('\\', '/').TrimEnd('/');
        return $"{normalizedBase}/{fileName}";
    }

    private static void EnsureDirectory(SftpClient client, string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Replace('\\', '/').TrimEnd('/');
        if (normalized == "/" || client.Exists(normalized))
        {
            return;
        }

        var current = "";
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }

    private string? FindRemoteImagePath(SftpClient client, string fileName, IReadOnlyList<string> candidates, string searchBasePath)
    {
        foreach (var candidate in candidates)
        {
            if (client.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            var basePath = searchBasePath.Replace('\\', '/').TrimEnd('/');
            var match = client
                .ListDirectory(basePath)
                .FirstOrDefault(entry => !entry.IsDirectory && entry.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            return match?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string ContentTypeFor(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}

sealed record RemoteImage(byte[] Bytes, string ContentType);

record AccessUploadStart(string FileName, long Size);
record AccessUploadMetadata(string FileName, long Size);
