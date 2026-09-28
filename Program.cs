using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddHttpClient("DefaultClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseProxy = false,
        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
    });
builder.Services.AddHttpClient();

// Listen on all network interfaces
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(IPAddress.Any, 5000);
    try
    {
        serverOptions.Listen(IPAddress.Any, 5001, listenOptions =>
        {
            listenOptions.UseHttps();
        });
    }
    catch { /* Ignore https error */ }
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoint: Server Information
app.MapGet("/api/server-info", (HttpRequest req) =>
{
    var hostName = Dns.GetHostName();
    var ipAddresses = Dns.GetHostAddresses(hostName)
        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
        .Select(ip => ip.ToString())
        .ToList();

    var httpUrls = ipAddresses.Select(ip => $"http://{ip}:5000").ToList();
    var httpsUrls = ipAddresses.Select(ip => $"https://{ip}:5001").ToList();

    return Results.Ok(new
    {
        hostName,
        localIps = ipAddresses,
        connectionUrls = httpUrls,
        httpsUrls = httpsUrls,
        primaryUrl = req.IsHttps ? (httpsUrls.FirstOrDefault() ?? "https://localhost:5001") : (httpUrls.FirstOrDefault() ?? "http://localhost:5000")
    });
});

// Endpoint: API Keys
app.MapGet("/api/keys", (IConfiguration config) =>
{
    return Results.Ok(new
    {
        geminiApiKey = config["Gemini:ApiKey"] ?? "",
        kinopoiskApiKey = config["Kinopoisk:ApiKey"] ?? "8c8e1a50-6322-4135-8875-5d40a5420d86",
        tmdbApiKey = config["TMDB:ApiKey"] ?? ""
    });
});

// Endpoint: Save Keys
app.MapPost("/api/save-keys", async (HttpRequest request, IConfiguration config) =>
{
    try
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var json = JsonNode.Parse(body);
        var geminiKey = json?["geminiApiKey"]?.ToString() ?? "";
        var kpKey = json?["kinopoiskApiKey"]?.ToString() ?? "";
        var tmdbKey = json?["tmdbApiKey"]?.ToString() ?? "";

        if (!string.IsNullOrWhiteSpace(geminiKey)) config["Gemini:ApiKey"] = geminiKey;
        if (!string.IsNullOrWhiteSpace(kpKey)) config["Kinopoisk:ApiKey"] = kpKey;
        if (!string.IsNullOrWhiteSpace(tmdbKey)) config["TMDB:ApiKey"] = tmdbKey;

        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// Helper to fetch rich details for a Kinopoisk film ID
async Task<object?> FetchKinopoiskDetails(int filmId, string kpKey, HttpClient client)
{
    try
    {
        var detUrl = $"https://kinopoiskapiunofficial.tech/api/v2.2/films/{filmId}";
        var detReq = new HttpRequestMessage(HttpMethod.Get, detUrl);
        detReq.Headers.Add("X-API-KEY", kpKey);
        var detRes = await client.SendAsync(detReq);
        if (!detRes.IsSuccessStatusCode) return null;

        var detBody = await detRes.Content.ReadAsStringAsync();
        var detJson = JsonNode.Parse(detBody);
        if (detJson == null) return null;

        var titleRu = detJson["nameRu"]?.ToString() ?? detJson["nameOriginal"]?.ToString() ?? "Фильм";
        var titleOrig = detJson["nameOriginal"]?.ToString() ?? detJson["nameEn"]?.ToString() ?? "";
        var posterUrl = detJson["posterUrl"]?.ToString() ?? detJson["posterUrlPreview"]?.ToString() ?? "";
        var coverUrl = detJson["coverUrl"]?.ToString() ?? posterUrl;
        var year = detJson["year"]?.GetValue<int>() ?? 0;
        var kpRating = detJson["ratingKinopoisk"]?.GetValue<double>() ?? 0;
        var imdbRating = detJson["ratingImdb"]?.GetValue<double>() ?? 0;
        var kpVoteCount = detJson["ratingKinopoiskVoteCount"]?.GetValue<int>() ?? 0;
        var imdbVoteCount = detJson["ratingImdbVoteCount"]?.GetValue<int>() ?? 0;

        var filmLength = detJson["filmLength"]?.GetValue<int>() ?? 0;
        var durationStr = filmLength > 0 ? $"{filmLength / 60} ч {filmLength % 60} мин" : "Фильм";
        var desc = detJson["description"]?.ToString() ?? detJson["shortDescription"]?.ToString() ?? "Описание сюжета...";
        var slogan = detJson["slogan"]?.ToString();
        var ageRating = detJson["ratingAgeLimits"]?.ToString()?.Replace("age", "") + "+" ?? "16+";

        var genres = detJson["genres"]?.AsArray().Select(g => g?["genre"]?.ToString()).Where(g => g != null).ToList() ?? new List<string?> { "Кино" };
        var countries = detJson["countries"]?.AsArray().Select(c => c?["country"]?.ToString()).Where(c => c != null).ToList() ?? new List<string?> { "Мир" };

        // 1. Staff
        string director = "Не указан";
        string composer = "Не указан";
        var castList = new List<object>();

        try
        {
            var staffUrl = $"https://kinopoiskapiunofficial.tech/api/v1/staff?filmId={filmId}";
            var staffReq = new HttpRequestMessage(HttpMethod.Get, staffUrl);
            staffReq.Headers.Add("X-API-KEY", kpKey);
            var staffRes = await client.SendAsync(staffReq);
            if (staffRes.IsSuccessStatusCode)
            {
                var staffBody = await staffRes.Content.ReadAsStringAsync();
                var staffArray = JsonNode.Parse(staffBody)?.AsArray();
                if (staffArray != null)
                {
                    var dir = staffArray.FirstOrDefault(s => s?["professionKey"]?.ToString() == "DIRECTOR");
                    if (dir != null) director = dir?["nameRu"]?.ToString() ?? dir?["nameEn"]?.ToString() ?? director;

                    var comp = staffArray.FirstOrDefault(s => s?["professionKey"]?.ToString() == "COMPOSER");
                    if (comp != null) composer = comp?["nameRu"]?.ToString() ?? comp?["nameEn"]?.ToString() ?? composer;

                    var actors = staffArray.Where(s => s?["professionKey"]?.ToString() == "ACTOR").Take(14);
                    foreach (var a in actors)
                    {
                        var aName = a?["nameRu"]?.ToString() ?? a?["nameEn"]?.ToString() ?? "Актер";
                        var aChar = a?["description"]?.ToString() ?? "Роль";
                        var aPhoto = a?["posterUrl"]?.ToString() ?? "";
                        castList.Add(new
                        {
                            name = aName,
                            character = aChar,
                            bio = $"{aName} в роли «{aChar}» в фильме «{titleRu}».",
                            profilePath = !string.IsNullOrWhiteSpace(aPhoto) ? $"/api/image-proxy?url={Uri.EscapeDataString(aPhoto)}" : null
                        });
                    }
                }
            }
        }
        catch { }

        // 2. Box Office
        var boxOffice = new Dictionary<string, string>();
        try
        {
            var boxReq = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.2/films/{filmId}/box_office");
            boxReq.Headers.Add("X-API-KEY", kpKey);
            var boxRes = await client.SendAsync(boxReq);
            if (boxRes.IsSuccessStatusCode)
            {
                var boxJson = JsonNode.Parse(await boxRes.Content.ReadAsStringAsync());
                var items = boxJson?["items"]?.AsArray();
                if (items != null)
                {
                    foreach (var b in items)
                    {
                        var type = b?["type"]?.ToString();
                        var amount = b?["amount"]?.GetValue<long>() ?? 0;
                        var symbol = b?["symbol"]?.ToString() ?? "$";
                        if (amount > 0 && !string.IsNullOrWhiteSpace(type))
                        {
                            if (type == "BUDGET") boxOffice["budget"] = $"{symbol}{amount:N0}";
                            else if (type == "WORLD") boxOffice["world"] = $"{symbol}{amount:N0}";
                            else if (type == "RUS") boxOffice["rus"] = $"{symbol}{amount:N0}";
                            else if (type == "USA") boxOffice["usa"] = $"{symbol}{amount:N0}";
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Stills
        var stills = new List<string>();
        try
        {
            var imgReq = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.2/films/{filmId}/images?type=STILL&page=1");
            imgReq.Headers.Add("X-API-KEY", kpKey);
            var imgRes = await client.SendAsync(imgReq);
            if (imgRes.IsSuccessStatusCode)
            {
                var imgJson = JsonNode.Parse(await imgRes.Content.ReadAsStringAsync());
                var items = imgJson?["items"]?.AsArray();
                if (items != null)
                {
                    foreach (var img in items.Take(16))
                    {
                        var u = img?["imageUrl"]?.ToString() ?? img?["previewUrl"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(u))
                        {
                            stills.Add($"/api/image-proxy?url={Uri.EscapeDataString(u)}");
                        }
                    }
                }
            }
        }
        catch { }

        // 4. Franchise
        var franchise = new List<object>();
        try
        {
            var seqReq = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.1/films/{filmId}/sequels_and_prequels");
            seqReq.Headers.Add("X-API-KEY", kpKey);
            var seqRes = await client.SendAsync(seqReq);
            if (seqRes.IsSuccessStatusCode)
            {
                var seqArray = JsonNode.Parse(await seqRes.Content.ReadAsStringAsync())?.AsArray();
                if (seqArray != null)
                {
                    foreach (var s in seqArray)
                    {
                        var sId = s?["filmId"]?.GetValue<int>() ?? 0;
                        var sName = s?["nameRu"]?.ToString() ?? s?["nameOriginal"]?.ToString();
                        var sYear = s?["year"]?.ToString();
                        var sPoster = s?["posterUrlPreview"]?.ToString() ?? s?["posterUrl"]?.ToString();
                        if (sId > 0 && !string.IsNullOrWhiteSpace(sName))
                        {
                            franchise.Add(new
                            {
                                id = sId,
                                title = sName,
                                year = sYear,
                                relationType = s?["relationType"]?.ToString() ?? "SEQUEL",
                                posterPath = !string.IsNullOrWhiteSpace(sPoster) ? $"/api/image-proxy?url={Uri.EscapeDataString(sPoster)}" : null
                            });
                        }
                    }
                }
            }
        }
        catch { }

        // 5. Trailers
        string? trailerEmbedUrl = null;
        string? trailerName = null;
        try
        {
            var vidReq = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.2/films/{filmId}/videos");
            vidReq.Headers.Add("X-API-KEY", kpKey);
            var vidRes = await client.SendAsync(vidReq);
            if (vidRes.IsSuccessStatusCode)
            {
                var vidJson = JsonNode.Parse(await vidRes.Content.ReadAsStringAsync());
                var items = vidJson?["items"]?.AsArray();
                if (items != null && items.Count > 0)
                {
                    var topTrailer = items.FirstOrDefault(v => (v?["site"]?.ToString() == "YOUTUBE" || v?["site"]?.ToString() == "KINOPOISK_WIDGET") && (v?["name"]?.ToString()?.Contains("трейлер", StringComparison.OrdinalIgnoreCase) == true))
                                   ?? items.FirstOrDefault(v => v?["site"]?.ToString() == "KINOPOISK_WIDGET" || v?["site"]?.ToString() == "YOUTUBE")
                                   ?? items[0];

                    if (topTrailer != null)
                    {
                        var rawUrl = topTrailer["url"]?.ToString();
                        trailerName = topTrailer["name"]?.ToString() ?? "Официальный трейлер";

                        if (!string.IsNullOrWhiteSpace(rawUrl))
                        {
                            if (rawUrl.Contains("youtube.com") || rawUrl.Contains("youtu.be"))
                            {
                                var videoId = "";
                                if (rawUrl.Contains("v=")) videoId = rawUrl.Split("v=")[1].Split("&")[0];
                                else if (rawUrl.Contains("youtu.be/")) videoId = rawUrl.Split("youtu.be/")[1].Split("?")[0];
                                if (!string.IsNullOrWhiteSpace(videoId))
                                {
                                    trailerEmbedUrl = $"https://www.youtube-nocookie.com/embed/{videoId}?autoplay=1&rel=0";
                                }
                            }
                            else if (rawUrl.Contains("widgets.kinopoisk.ru"))
                            {
                                trailerEmbedUrl = rawUrl;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // Facts
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(slogan)) facts.Add($"Слоган: «{slogan}»");
        if (kpRating > 0) facts.Add($"Рейтинг Кинопоиска: {kpRating:0.1} / 10 {(kpVoteCount > 0 ? $"({kpVoteCount:N0} оценок)" : "")}.");
        if (imdbRating > 0) facts.Add($"Рейтинг IMDb: {imdbRating:0.1} / 10 {(imdbVoteCount > 0 ? $"({imdbVoteCount:N0} оценок)" : "")}.");
        if (composer != "Не указан") facts.Add($"Композитор саундтрека: {composer}.");
        facts.Add($"Страны производства: {string.Join(", ", countries)}, премьера в {year} году.");

        return new
        {
            isList = false,
            id = filmId,
            title = titleRu,
            originalTitle = titleOrig,
            type = (detJson["type"]?.ToString() == "TV_SERIES" || detJson["type"]?.ToString() == "MINI_SERIES") ? "Сериал" : "Фильм",
            releaseYear = year > 0 ? year : 2023,
            countries,
            genres,
            duration = durationStr,
            ageRating,
            director,
            composer,
            ratings = new
            {
                kinopoisk = kpRating > 0 ? kpRating : 7.8,
                kinopoiskVotes = kpVoteCount,
                imdb = imdbRating > 0 ? imdbRating : 7.6,
                imdbVotes = imdbVoteCount
            },
            overview = desc,
            posterPath = !string.IsNullOrWhiteSpace(posterUrl) ? $"/api/image-proxy?url={Uri.EscapeDataString(posterUrl)}" : null,
            backdropPath = !string.IsNullOrWhiteSpace(coverUrl) ? $"/api/image-proxy?url={Uri.EscapeDataString(coverUrl)}" : null,
            actors = castList,
            boxOffice,
            stills,
            franchise,
            trailer = new
            {
                embedUrl = trailerEmbedUrl,
                name = trailerName,
                searchQuery = $"{titleRu} {year} трейлер русский"
            },
            interestingFacts = facts,
            whereToWatch = new[] { "Кинопоиск", "Иви", "Okko", "Premier", "Wink" },
            trailerQuery = $"{titleRu} {year} трейлер"
        };
    }
    catch
    {
        return null;
    }
}

// Endpoint: Direct Movie Details by Kinopoisk ID
app.MapGet("/api/movie-details", async (int id, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Некорректный ID фильма" });
    var client = httpClientFactory.CreateClient("DefaultClient");
    var kpKey = config["Kinopoisk:ApiKey"] ?? "8c8e1a50-6322-4135-8875-5d40a5420d86";
    var details = await FetchKinopoiskDetails(id, kpKey, client);
    if (details != null) return Results.Ok(details);
    return Results.NotFound(new { error = "Фильм не найден" });
});

// Main Endpoint: Smart Voice & Text Movie Search (Supports Collections / Lists & Single Films)
app.MapGet("/api/smart-search", async (string query, string? clientApiKey, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(query)) return Results.BadRequest(new { error = "Запрос не может быть пустым" });

    try
    {
        var client = httpClientFactory.CreateClient("DefaultClient");
        client.Timeout = TimeSpan.FromSeconds(15);
        var kpKey = config["Kinopoisk:ApiKey"] ?? "8c8e1a50-6322-4135-8875-5d40a5420d86";

        var cleanQuery = query.Trim();
        var lower = cleanQuery.ToLowerInvariant();

        // Check if user is asking for a Collection / Thematic List
        string[] listKeywords = { "про ", "все ", "список", "лучшие", "топ ", "фильмы ", "комедии", "боевики", "ужасы", "триллеры", "сериалы", "мультфильмы", "подборка", "какие фильмы", "новинки", "кино про" };
        bool isListIntent = listKeywords.Any(k => lower.Contains(k)) || lower.Split(' ').Length >= 3;

        // Clean query for search
        var stripped = cleanQuery;
        string[] prefixesToRemove = { "фильмы про", "фильм про", "кино про", "сериалы про", "мультфильмы про", "все про", "список фильмов", "лучшие", "топ", "подборка", "покажи", "найди" };
        foreach (var p in prefixesToRemove)
        {
            if (stripped.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                stripped = stripped.Substring(p.Length).Trim();
            }
        }
        if (string.IsNullOrWhiteSpace(stripped)) stripped = cleanQuery;

        // 1. Search Kinopoisk Unofficial by Keyword
        var encKeyword = Uri.EscapeDataString(stripped);
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.1/films/search-by-keyword?keyword={encKeyword}");
        req.Headers.Add("X-API-KEY", kpKey);

        var kpRes = await client.SendAsync(req);
        if (kpRes.IsSuccessStatusCode)
        {
            var kpBody = await kpRes.Content.ReadAsStringAsync();
            var kpJson = JsonNode.Parse(kpBody);
            var films = kpJson?["films"]?.AsArray();

            if (films != null && films.Count > 0)
            {
                // If query was thematic / multi-film, return a rich Collection List
                if (isListIntent && films.Count > 1)
                {
                    var listItems = new List<object>();
                    foreach (var f in films.Take(15))
                    {
                        var fid = f?["filmId"]?.GetValue<int>() ?? 0;
                        var tRu = f?["nameRu"]?.ToString() ?? f?["nameEn"]?.ToString() ?? "Фильм";
                        var tEn = f?["nameEn"]?.ToString() ?? "";
                        var yr = f?["year"]?.ToString();
                        int.TryParse(yr, out int releaseYr);
                        var poster = f?["posterUrlPreview"]?.ToString() ?? f?["posterUrl"]?.ToString();
                        var rating = f?["rating"]?.ToString() ?? "7.5";
                        var desc = f?["description"]?.ToString() ?? "";
                        var genresList = f?["genres"]?.AsArray().Select(g => g?["genre"]?.ToString()).Where(g => g != null).ToList() ?? new List<string?> { "Кино" };
                        var countriesList = f?["countries"]?.AsArray().Select(c => c?["country"]?.ToString()).Where(c => c != null).ToList() ?? new List<string?> { "Мир" };

                        listItems.Add(new
                        {
                            id = fid,
                            title = tRu,
                            originalTitle = tEn,
                            releaseYear = releaseYr > 0 ? releaseYr : (int.TryParse(yr, out int y) ? y : 2023),
                            rating,
                            genres = genresList,
                            countries = countriesList,
                            posterPath = !string.IsNullOrWhiteSpace(poster) ? $"/api/image-proxy?url={Uri.EscapeDataString(poster)}" : null,
                            overview = desc
                        });
                    }

                    return Results.Ok(new
                    {
                        isList = true,
                        collectionTitle = $"Подборка: {cleanQuery}",
                        total = listItems.Count,
                        items = listItems
                    });
                }
                else
                {
                    // Single Film Match
                    var top = films[0];
                    var filmId = top?["filmId"]?.GetValue<int>() ?? 0;
                    if (filmId > 0)
                    {
                        var details = await FetchKinopoiskDetails(filmId, kpKey, client);
                        if (details != null) return Results.Ok(details);
                    }
                }
            }
        }

        return Results.Json(new { error = "Фильмы по вашему запросу не найдены. Попробуйте уточнить тему." }, statusCode: 404);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// Endpoint: Image Proxy
app.MapGet("/api/image-proxy", async (string url, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(url)) return Results.BadRequest();

    try
    {
        var client = httpClientFactory.CreateClient("DefaultClient");
        client.Timeout = TimeSpan.FromSeconds(10);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        req.Headers.Add("Referer", "https://www.kinopoisk.ru/");

        var res = await client.SendAsync(req);
        if (!res.IsSuccessStatusCode) return Results.NotFound();

        var stream = await res.Content.ReadAsStreamAsync();
        var contentType = res.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return Results.Stream(stream, contentType);
    }
    catch
    {
        return Results.NotFound();
    }
});

app.Run();
