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
        var filmLength = detJson["filmLength"]?.GetValue<int>() ?? 0;
        var durationStr = filmLength > 0 ? $"{filmLength / 60} ч {filmLength % 60} мин" : "Фильм";
        var desc = detJson["description"]?.ToString() ?? detJson["shortDescription"]?.ToString() ?? "Описание сюжета...";
        var slogan = detJson["slogan"]?.ToString();
        var ageRating = detJson["ratingAgeLimits"]?.ToString()?.Replace("age", "") + "+" ?? "16+";

        var genres = detJson["genres"]?.AsArray().Select(g => g?["genre"]?.ToString()).Where(g => g != null).ToList() ?? new List<string?> { "Кино" };
        var countries = detJson["countries"]?.AsArray().Select(c => c?["country"]?.ToString()).Where(c => c != null).ToList() ?? new List<string?> { "Мир" };

        // Fetch staff (Director, Actors)
        string director = "Не указан";
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

                    var actors = staffArray.Where(s => s?["professionKey"]?.ToString() == "ACTOR").Take(12);
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

        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(slogan)) facts.Add($"Слоган: «{slogan}»");
        if (kpRating > 0) facts.Add($"Рейтинг Кинопоиска: {kpRating:0.1} / 10.");
        if (imdbRating > 0) facts.Add($"Рейтинг IMDb: {imdbRating:0.1} / 10.");
        facts.Add($"Производство: {string.Join(", ", countries)}, премьера в {year} году.");

        return new
        {
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
            ratings = new
            {
                kinopoisk = kpRating > 0 ? kpRating : 7.8,
                imdb = imdbRating > 0 ? imdbRating : 7.6
            },
            overview = desc,
            posterPath = !string.IsNullOrWhiteSpace(posterUrl) ? $"/api/image-proxy?url={Uri.EscapeDataString(posterUrl)}" : null,
            backdropPath = !string.IsNullOrWhiteSpace(coverUrl) ? $"/api/image-proxy?url={Uri.EscapeDataString(coverUrl)}" : null,
            actors = castList,
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

// Main Endpoint: Smart Voice & Text Natural Language Movie Search
app.MapGet("/api/smart-search", async (string query, string? clientApiKey, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(query)) return Results.BadRequest(new { error = "Запрос не может быть пустым" });

    try
    {
        var client = httpClientFactory.CreateClient("DefaultClient");
        client.Timeout = TimeSpan.FromSeconds(15);
        var kpKey = config["Kinopoisk:ApiKey"] ?? "8c8e1a50-6322-4135-8875-5d40a5420d86";

        // Clean conversational noise (e.g. "фильм садовник с ван даммом" -> extract candidate keywords)
        var cleanQuery = query.Trim();
        var lower = cleanQuery.ToLowerInvariant();

        // 1. Direct Search on Kinopoisk Unofficial
        var encoded = Uri.EscapeDataString(cleanQuery);
        var searchUrl = $"https://kinopoiskapiunofficial.tech/api/v2.1/films/search-by-keyword?keyword={encoded}";
        var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        req.Headers.Add("X-API-KEY", kpKey);

        var kpRes = await client.SendAsync(req);
        if (kpRes.IsSuccessStatusCode)
        {
            var kpBody = await kpRes.Content.ReadAsStringAsync();
            var kpJson = JsonNode.Parse(kpBody);
            var films = kpJson?["films"]?.AsArray();

            if (films != null && films.Count > 0)
            {
                var top = films[0];
                var filmId = top?["filmId"]?.GetValue<int>() ?? 0;
                if (filmId > 0)
                {
                    var details = await FetchKinopoiskDetails(filmId, kpKey, client);
                    if (details != null) return Results.Ok(details);
                }
            }
        }

        // 2. If direct search didn't find, try searching stripped clean words (e.g. remove "фильм", "сериал", "с актером")
        var stripped = cleanQuery;
        string[] prefixesToRemove = { "фильм", "сериал", "мультфильм", "кино", "покажи", "найди", "расскажи про", "про", "где играет", "с участием" };
        foreach (var p in prefixesToRemove)
        {
            if (stripped.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                stripped = stripped.Substring(p.Length).Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(stripped) && stripped != cleanQuery)
        {
            var encStripped = Uri.EscapeDataString(stripped);
            var reqStripped = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.1/films/search-by-keyword?keyword={encStripped}");
            reqStripped.Headers.Add("X-API-KEY", kpKey);
            var resStripped = await client.SendAsync(reqStripped);
            if (resStripped.IsSuccessStatusCode)
            {
                var bodyS = await resStripped.Content.ReadAsStringAsync();
                var jsonS = JsonNode.Parse(bodyS);
                var filmsS = jsonS?["films"]?.AsArray();
                if (filmsS != null && filmsS.Count > 0)
                {
                    var topS = filmsS[0];
                    var fid = topS?["filmId"]?.GetValue<int>() ?? 0;
                    if (fid > 0)
                    {
                        var details = await FetchKinopoiskDetails(fid, kpKey, client);
                        if (details != null) return Results.Ok(details);
                    }
                }
            }
        }

        // 3. Fallback: Use Gemini AI to parse complex natural query (e.g. "фильм где человек просыпается в ванне со льдом")
        const string DefaultGeminiKey = "AIzaSyA33U6-qWAWJpN3VEZftv-CGVSN_pPt_Hs";
        var serverApiKey = config["Gemini:ApiKey"] ?? DefaultGeminiKey;
        var keysToTry = new List<string>();
        if (!string.IsNullOrWhiteSpace(clientApiKey)) keysToTry.Add(clientApiKey);
        if (!string.IsNullOrWhiteSpace(serverApiKey) && !keysToTry.Contains(serverApiKey)) keysToTry.Add(serverApiKey);
        if (!keysToTry.Contains(DefaultGeminiKey)) keysToTry.Add(DefaultGeminiKey);

        var prompt = $@"Ты интеллектуальный кинопоисковик. Пользователь ищет фильм/сериал по описанию или запросу: ""{query}"".
1. Определи точное официальное название фильма на русском языке и год выпуска.
2. Предоставь информацию в JSON формате:
{{
  ""title"": ""Точное название фильма на русском"",
  ""originalTitle"": ""Original Title"",
  ""type"": ""Фильм"" | ""Сериал"",
  ""releaseYear"": 2023,
  ""director"": ""Имя Режиссера"",
  ""genres"": [""Жанр1"", ""Жанр2""],
  ""duration"": ""1 ч 45 мин"",
  ""ratings"": {{ ""kinopoisk"": 8.0, ""imdb"": 8.0 }},
  ""overview"": ""Подробный сюжет на русском языке..."",
  ""actors"": [
    {{ ""name"": ""Имя Актера"", ""character"": ""Имя персонажа"", ""bio"": ""Справка об актере"" }}
  ],
  ""interestingFacts"": [""Интересный факт о фильме...""]
}}";

        var payload = new
        {
            contents = new[] { new { parts = new object[] { new { text = prompt } } } },
            generationConfig = new { response_mime_type = "application/json", temperature = 0.2 }
        };
        var contentString = JsonSerializer.Serialize(payload);

        foreach (var tryKey in keysToTry)
        {
            foreach (var modelName in new[] { "gemini-2.5-flash", "gemini-3.5-flash" })
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={tryKey}";
                    var content = new StringContent(contentString, Encoding.UTF8, "application/json");
                    var gRes = await client.PostAsync(url, content, cts.Token);
                    if (gRes.IsSuccessStatusCode)
                    {
                        var gBody = await gRes.Content.ReadAsStringAsync();
                        var gJson = JsonNode.Parse(gBody);
                        var rawText = gJson?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()?.Trim() ?? "{}";
                        if (rawText.StartsWith("```json")) rawText = rawText.Substring(7);
                        if (rawText.StartsWith("```")) rawText = rawText.Substring(3);
                        if (rawText.EndsWith("```")) rawText = rawText.Substring(0, rawText.Length - 3);

                        var parsed = JsonNode.Parse(rawText);
                        var exactTitle = parsed?["title"]?.ToString();

                        // Try fetching poster from Kinopoisk using the exact title identified by Gemini
                        if (!string.IsNullOrWhiteSpace(exactTitle))
                        {
                            var encT = Uri.EscapeDataString(exactTitle);
                            var kpTReq = new HttpRequestMessage(HttpMethod.Get, $"https://kinopoiskapiunofficial.tech/api/v2.1/films/search-by-keyword?keyword={encT}");
                            kpTReq.Headers.Add("X-API-KEY", kpKey);
                            var kpTRes = await client.SendAsync(kpTReq);
                            if (kpTRes.IsSuccessStatusCode)
                            {
                                var bodyT = await kpTRes.Content.ReadAsStringAsync();
                                var jsonT = JsonNode.Parse(bodyT);
                                var fT = jsonT?["films"]?.AsArray();
                                if (fT != null && fT.Count > 0)
                                {
                                    var fid = fT[0]?["filmId"]?.GetValue<int>() ?? 0;
                                    if (fid > 0)
                                    {
                                        var details = await FetchKinopoiskDetails(fid, kpKey, client);
                                        if (details != null) return Results.Ok(details);
                                    }
                                }
                            }
                        }

                        return Results.Ok(parsed);
                    }
                }
                catch { }
            }
        }

        return Results.Json(new { error = "Фильм не найден. Попробуйте уточнить название или имя актера." }, statusCode: 404);
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
