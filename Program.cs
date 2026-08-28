using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS to allow mobile and local testing
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

// Configure server to listen on all interfaces (HTTP 5000 and HTTPS 5001)
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
    catch { /* Ignore if https binding fails */ }
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoint: Server Information & Local IPs for phone connection
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

// Endpoint: Get Global Server Keys
app.MapGet("/api/keys", (IConfiguration config) =>
{
    return Results.Ok(new
    {
        geminiApiKey = config["Gemini:ApiKey"] ?? "",
        tmdbApiKey = config["TMDB:ApiKey"] ?? ""
    });
});

// Endpoint: Save Global Server Keys (Syncs between PC and Phone)
app.MapPost("/api/save-keys", async (HttpRequest request, IConfiguration config) =>
{
    try
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var json = JsonNode.Parse(body);
        var geminiKey = json?["geminiApiKey"]?.ToString() ?? "";
        var tmdbKey = json?["tmdbApiKey"]?.ToString() ?? "";

        config["Gemini:ApiKey"] = geminiKey;
        config["TMDB:ApiKey"] = tmdbKey;

        if (File.Exists("appsettings.json"))
        {
            var currentJsonStr = await File.ReadAllTextAsync("appsettings.json");
            var rootNode = JsonNode.Parse(currentJsonStr) ?? new JsonObject();
            rootNode["Gemini"] = new JsonObject { ["ApiKey"] = geminiKey };
            rootNode["TMDB"] = new JsonObject { ["ApiKey"] = tmdbKey };
            await File.WriteAllTextAsync("appsettings.json", rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        return Results.Ok(new { success = true, message = "Ключи успешно сохранены на сервере!" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// Endpoint: Recognize TV Screen Image via Gemini Vision API
app.MapPost("/api/recognize-image", async (HttpRequest request, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    try
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var bodyText = await reader.ReadToEndAsync();
        var json = JsonNode.Parse(bodyText);

        if (json == null || json["imageBase64"] == null)
        {
            return Results.BadRequest(new { error = "Отсутствует изображение (imageBase64)." });
        }

        var rawBase64 = json["imageBase64"]!.ToString();
        var base64Data = rawBase64.Contains(",") ? rawBase64.Split(',')[1] : rawBase64;
        var mimeType = "image/jpeg";
        if (rawBase64.StartsWith("data:image/png")) mimeType = "image/png";
        else if (rawBase64.StartsWith("data:image/webp")) mimeType = "image/webp";

        // Support both single image and multi-frame array
        var partsList = new List<object>();

        var prompt = @"Ты эксперт-киновед и ИИ-сканер экрана телевизора.
Тебе предоставлена фотография, на которой может быть видна комната и экран телевизора/монитора (ТВ).

ИНСТРУКЦИЯ ПО ОБРАБОТКЕ КАДРА:
1. ЛОКАЛИЗАЦИЯ ТВ: Найди в кадре границы экрана телевизора или монитора. Он может находиться в центре, сверху, снизу или под углом.
2. ОТСЕЧЕНИЕ ФОНА: Полностью проигнорируй окружение комнаты (стены, обои, мебель, полки, комнатное освещение, блики за пределами экрана).
3. АНАЛИЗ КОНТЕНТА ТВ: Сконцентрируйся ИСКЛЮЧИТЕЛЬНО на изображении, лицах актеров, титрах, логотипах или сцене, показанной ВНУТРИ экрана телевизора.
4. ОПРЕДЕЛЕНИЕ: Точно определи фильм, сериал, мультфильм или шоу.
5. ФОРМАТ: Ответь СТРОГО в формате JSON без markdown и лишнего текста следующей структуры:
{
  ""title"": ""Официальное название на русском языке"",
  ""originalTitle"": ""Original title in native language"",
  ""type"": ""Фильм"" | ""Сериал"" | ""Мультфильм"" | ""Аниме"" | ""Шоу"",
  ""releaseYear"": 2023,
  ""countries"": [""США""],
  ""genres"": [""Фантастика"", ""Боевик"", ""Триллер""],
  ""duration"": ""2 ч 16 мин"",
  ""ageRating"": ""16+"",
  ""director"": ""Имя Режиссера"",
  ""ratings"": {
    ""imdb"": 8.4,
    ""kinopoisk"": 8.2
  },
  ""confidence"": ""high"" | ""medium"" | ""low"",
  ""sceneDescription"": ""Подробный анализ того, какая именно сцена фильма показана на кадре и кто в ней участвует"",
  ""explanation"": ""Почему ты уверен в определении (конкретные актеры, костюмы, реквизит, локация)"",
  ""overview"": ""Подробное, интересное и качественное описание сюжета (о чем фильм/сериал, завязка и суть конфликта) на русском языке"",
  ""actors"": [
    {
      ""name"": ""Полное имя актера на русском"",
      ""originalName"": ""Actor Name in English"",
      ""character"": ""Имя персонажа в фильме"",
      ""bio"": ""Краткая справка об актере и 2-3 других его главных фильма"",
      ""searchQuery"": ""Имя актера""
    }
  ],
  ""interestingFacts"": [
    ""Любопытный факт о съемках фильма или этой сцены..."",
    ""Еще один интересный факт...""
  ],
  ""whereToWatch"": [""Кинопоиск"", ""Иви"", ""Okko""],
  ""trailerQuery"": ""Название фильма официальный трейлер""
}";

        partsList.Add(new { text = prompt });

        if (json["imagesBase64"] is JsonArray imagesArray && imagesArray.Count > 0)
        {
            foreach (var imgNode in imagesArray)
            {
                if (imgNode == null) continue;
                var raw = imgNode.ToString();
                var data = raw.Contains(",") ? raw.Split(',')[1] : raw;
                partsList.Add(new
                {
                    inline_data = new
                    {
                        mime_type = "image/jpeg",
                        data = data
                    }
                });
            }
        }
        else if (json["imageBase64"] != null)
        {
            var raw = json["imageBase64"]!.ToString();
            var data = raw.Contains(",") ? raw.Split(',')[1] : raw;
            var mime = raw.StartsWith("data:image/png") ? "image/png" : "image/jpeg";
            partsList.Add(new
            {
                inline_data = new
                {
                    mime_type = mime,
                    data = data
                }
            });
        }
        else
        {
            return Results.BadRequest(new { error = "Отсутствует изображение (imageBase64 или imagesBase64)." });
        }

        var clientApiKey = json["apiKey"]?.ToString();
        var apiKey = !string.IsNullOrWhiteSpace(clientApiKey) ? clientApiKey : config["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Results.BadRequest(new
            {
                error = "Не указан Gemini API Key. Введите ключ в настройках приложения или укажите в appsettings.json."
            });
        }

        var client = httpClientFactory.CreateClient("DefaultClient");
        client.Timeout = TimeSpan.FromSeconds(30);

        var geminiPayload = new
        {
            contents = new[]
            {
                new
                {
                    parts = partsList.ToArray()
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.2
            }
        };

        var contentString = JsonSerializer.Serialize(geminiPayload);
        var modelsToTry = new[] { "gemini-2.5-flash", "gemini-3.5-flash" };
        var serverApiKey = config["Gemini:ApiKey"];
        var keysToTry = new List<string>();
        if (!string.IsNullOrWhiteSpace(apiKey)) keysToTry.Add(apiKey);
        if (!string.IsNullOrWhiteSpace(serverApiKey) && !keysToTry.Contains(serverApiKey)) keysToTry.Add(serverApiKey);

        HttpResponseMessage? response = null;
        string responseBody = "";

        foreach (var tryKey in keysToTry)
        {
            foreach (var modelName in modelsToTry)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(22));
                    var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={tryKey}";
                    var content = new StringContent(contentString, Encoding.UTF8, "application/json");
                    response = await client.PostAsync(geminiUrl, content, cts.Token);
                    responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                    
                    // If 429 (quota) or 404/503, continue to next model/key
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode == 503 || (int)response.StatusCode == 404)
                    {
                        continue;
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Model took too long (>8s), try next model
                    continue;
                }
                catch
                {
                    continue;
                }
            }

            if (response != null && response.IsSuccessStatusCode)
            {
                break;
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Results.Json(new { error = $"Ошибка Gemini API: {response?.StatusCode}", details = responseBody }, statusCode: (int)(response?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }

        var geminiResponseJson = JsonNode.Parse(responseBody);
        var rawText = geminiResponseJson?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return Results.Json(new { error = "Gemini вернул пустой ответ." }, statusCode: 500);
        }

        var cleanedText = rawText.Trim();
        if (cleanedText.StartsWith("```json")) cleanedText = cleanedText.Substring(7);
        if (cleanedText.StartsWith("```")) cleanedText = cleanedText.Substring(3);
        if (cleanedText.EndsWith("```")) cleanedText = cleanedText.Substring(0, cleanedText.Length - 3);
        cleanedText = cleanedText.Trim();

        var parsedResult = JsonNode.Parse(cleanedText);
        return Results.Ok(parsedResult);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Внутренняя ошибка сервера", message = ex.Message }, statusCode: 500);
    }
});

// Endpoint: Search Movie/TV Info (Kinopoisk Unofficial + TMDB fallback)
app.MapGet("/api/tmdb-search", async (string query, int? year, string? clientApiKey, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.BadRequest(new { error = "Параметр поиска (query) обязателен." });
        }

        var client = httpClientFactory.CreateClient("DefaultClient");
        client.Timeout = TimeSpan.FromSeconds(10);

        // 1. Try Kinopoisk Unofficial API First (reliable in RU/CIS, official HD posters and actor photos)
        var kpKey = config["Kinopoisk:ApiKey"] ?? "8c8e1a50-6322-4135-8875-5d40a5420d86";
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var kpSearchUrl = $"https://kinopoiskapiunofficial.tech/api/v2.1/films/search-by-keyword?keyword={encodedQuery}";
            var kpRequest = new HttpRequestMessage(HttpMethod.Get, kpSearchUrl);
            kpRequest.Headers.Add("X-API-KEY", kpKey);

            var kpResponse = await client.SendAsync(kpRequest);
            if (kpResponse.IsSuccessStatusCode)
            {
                var kpBody = await kpResponse.Content.ReadAsStringAsync();
                var kpJson = JsonNode.Parse(kpBody);
                var films = kpJson?["films"]?.AsArray();

                if (films != null && films.Count > 0)
                {
                    var topFilm = films[0];
                    var filmId = topFilm?["filmId"]?.GetValue<int>() ?? 0;
                    var posterUrl = topFilm?["posterUrl"]?.ToString();
                    var filmTitle = topFilm?["nameRu"]?.ToString() ?? topFilm?["nameEn"]?.ToString() ?? query;
                    var origTitle = topFilm?["nameEn"]?.ToString() ?? "";
                    var filmYear = topFilm?["year"]?.ToString() ?? (year.HasValue ? year.Value.ToString() : "");
                    var description = topFilm?["description"]?.ToString();
                    var ratingStr = topFilm?["rating"]?.ToString();
                    double.TryParse(ratingStr?.Replace("%", "").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedRating);

                    // Fetch actors & staff
                    var castList = new List<object>();
                    if (filmId > 0)
                    {
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
                                    var actors = staffArray.Where(s => s?["professionKey"]?.ToString() == "ACTOR").Take(10);
                                    foreach (var a in actors)
                                    {
                                        var aName = a?["nameRu"]?.ToString() ?? a?["nameEn"]?.ToString() ?? "Актер";
                                        var aChar = a?["description"]?.ToString() ?? "Роль";
                                        var aPoster = a?["posterUrl"]?.ToString();
                                        castList.Add(new
                                        {
                                            name = aName,
                                            character = aChar,
                                            profilePath = !string.IsNullOrWhiteSpace(aPoster) ? $"/api/image-proxy?url={Uri.EscapeDataString(aPoster)}" : null
                                        });
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    return Results.Ok(new
                    {
                        found = true,
                        source = "kinopoisk",
                        id = filmId,
                        title = filmTitle,
                        originalTitle = origTitle,
                        overview = description,
                        releaseDate = filmYear,
                        voteAverage = parsedRating,
                        posterPath = !string.IsNullOrWhiteSpace(posterUrl) ? $"/api/image-proxy?url={Uri.EscapeDataString(posterUrl)}" : null,
                        backdropPath = !string.IsNullOrWhiteSpace(posterUrl) ? $"/api/image-proxy?url={Uri.EscapeDataString(posterUrl)}" : null,
                        genres = topFilm?["genres"]?.AsArray().Select(g => g?["genre"]?.ToString()).Where(g => g != null).ToList(),
                        cast = castList
                    });
                }
            }
        }
        catch { }

        // 2. Fallback to TMDB API if configured
        var apiKey = !string.IsNullOrWhiteSpace(clientApiKey) ? clientApiKey : config["TMDB:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var isBearerToken = apiKey.StartsWith("ey") || apiKey.Length > 40;
            if (isBearerToken) client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var encodedQuery = Uri.EscapeDataString(query);
            var authParam = isBearerToken ? "" : $"api_key={apiKey}&";
            var tmdbUrl = $"https://api.themoviedb.org/3/search/multi?{authParam}language=ru-RU&query={encodedQuery}&include_adult=false";
            if (year.HasValue && year.Value > 1900) tmdbUrl += $"&year={year.Value}";

            var response = await client.GetAsync(tmdbUrl);
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var tmdbJson = JsonNode.Parse(responseBody);
                var results = tmdbJson?["results"]?.AsArray();
                if (results != null && results.Count > 0)
                {
                    var top = results.FirstOrDefault(r => r?["media_type"]?.ToString() == "movie" || r?["media_type"]?.ToString() == "tv") ?? results[0];
                    var mediaType = top?["media_type"]?.ToString() ?? "movie";
                    var id = top?["id"]?.GetValue<int>() ?? 0;

                    var detailUrl = isBearerToken
                        ? $"https://api.themoviedb.org/3/{mediaType}/{id}?language=ru-RU&append_to_response=credits,videos"
                        : $"https://api.themoviedb.org/3/{mediaType}/{id}?api_key={apiKey}&language=ru-RU&append_to_response=credits,videos";

                    var detailResponse = await client.GetAsync(detailUrl);
                    if (detailResponse.IsSuccessStatusCode)
                    {
                        var detailBody = await detailResponse.Content.ReadAsStringAsync();
                        var detailJson = JsonNode.Parse(detailBody);
                        var rawPoster = detailJson?["poster_path"]?.ToString();
                        var rawBackdrop = detailJson?["backdrop_path"]?.ToString();

                        return Results.Ok(new
                        {
                            found = true,
                            source = "tmdb",
                            mediaType,
                            id,
                            title = detailJson?["title"]?.ToString() ?? detailJson?["name"]?.ToString(),
                            originalTitle = detailJson?["original_title"]?.ToString() ?? detailJson?["original_name"]?.ToString(),
                            overview = detailJson?["overview"]?.ToString(),
                            releaseDate = detailJson?["release_date"]?.ToString() ?? detailJson?["first_air_date"]?.ToString(),
                            voteAverage = detailJson?["vote_average"]?.GetValue<double>() ?? 0,
                            voteCount = detailJson?["vote_count"]?.GetValue<int>() ?? 0,
                            posterPath = rawPoster != null ? $"/api/image-proxy?url={Uri.EscapeDataString($"https://image.tmdb.org/t/p/w500{rawPoster}")}" : null,
                            backdropPath = rawBackdrop != null ? $"/api/image-proxy?url={Uri.EscapeDataString($"https://image.tmdb.org/t/p/w1280{rawBackdrop}")}" : null,
                            genres = detailJson?["genres"]?.AsArray().Select(g => g?["name"]?.ToString()).Where(g => g != null).ToList(),
                            cast = detailJson?["credits"]?["cast"]?.AsArray().Take(8).Select(c => {
                                var pPath = c?["profile_path"]?.ToString();
                                return new
                                {
                                    name = c?["name"]?.ToString(),
                                    character = c?["character"]?.ToString(),
                                    profilePath = pPath != null ? $"/api/image-proxy?url={Uri.EscapeDataString($"https://image.tmdb.org/t/p/w185{pPath}")}" : null
                                };
                            }).ToList(),
                            trailer = detailJson?["videos"]?["results"]?.AsArray()
                                .FirstOrDefault(v => v?["site"]?.ToString() == "YouTube" && v?["type"]?.ToString() == "Trailer")?["key"]?.ToString()
                        });
                    }
                }
            }
        }

        return Results.Ok(new { found = false, message = "Фильм не найден в базе данных" });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { found = false, message = ex.Message });
    }
});

// Endpoint: Search by text title (Smart Text Search)
app.MapGet("/api/search-title", async (string query, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(query)) return Results.BadRequest(new { error = "Запрос не может быть пустым" });

    try
    {
        var client = httpClientFactory.CreateClient("DefaultClient");
        client.Timeout = TimeSpan.FromSeconds(18);

        var serverApiKey = config["Gemini:ApiKey"];
        var modelsToTry = new[] { "gemini-2.5-flash", "gemini-3.5-flash" };

        var prompt = $@"Ты киноведческая энциклопедия. Пользователь ищет фильм, сериал или мультфильм по запросу: ""{query}"".
Предоставь подробную информацию в формате JSON:
{{
  ""title"": ""Название на русском"",
  ""originalTitle"": ""Original Title"",
  ""type"": ""Фильм"" | ""Сериал"" | ""Мультфильм"",
  ""releaseYear"": 2023,
  ""countries"": [""Страна""],
  ""genres"": [""Жанр1"", ""Жанр2""],
  ""duration"": ""2 ч 10 мин"",
  ""ageRating"": ""16+"",
  ""director"": ""Режиссер"",
  ""ratings"": {{ ""imdb"": 8.0, ""kinopoisk"": 7.9 }},
  ""confidence"": ""high"",
  ""sceneDescription"": ""Классическое кинопроизведение"",
  ""explanation"": ""Найдено по прямому текстовому запросу"",
  ""overview"": ""Подробный сюжет фильма на русском языке..."",
  ""actors"": [
    {{
      ""name"": ""Имя актера на русском"",
      ""originalName"": ""Actor Name"",
      ""character"": ""Персонаж"",
      ""bio"": ""Справка об актере и его ролях""
    }}
  ],
  ""interestingFacts"": [""Интересный факт о фильме...""]
}}";

        var payload = new
        {
            contents = new[] { new { parts = new object[] { new { text = prompt } } } },
            generationConfig = new { response_mime_type = "application/json", temperature = 0.2 }
        };
        var contentString = JsonSerializer.Serialize(payload);

        HttpResponseMessage? response = null;
        string responseBody = "";

        foreach (var modelName in modelsToTry)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={serverApiKey}";
                var content = new StringContent(contentString, Encoding.UTF8, "application/json");
                response = await client.PostAsync(url, content, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    break;
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Results.Json(new { error = "Не удалось выполнить поиск" }, statusCode: 500);
        }

        var geminiResponseJson = JsonNode.Parse(responseBody);
        var rawText = geminiResponseJson?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
        var cleanedText = rawText?.Trim() ?? "{}";
        if (cleanedText.StartsWith("```json")) cleanedText = cleanedText.Substring(7);
        if (cleanedText.StartsWith("```")) cleanedText = cleanedText.Substring(3);
        if (cleanedText.EndsWith("```")) cleanedText = cleanedText.Substring(0, cleanedText.Length - 3);

        return Results.Ok(JsonNode.Parse(cleanedText));
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// Endpoint: Reliable Image Proxy for Posters & Cast Photos
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
