using BankingOffers.API.Configuration;
using BankingOffers.API.Data;
using BankingOffers.API.Entities;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace BankingOffers.API.Services;

public class BankScraperService : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly ILogger<BankScraperService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<ScrapingTarget> _scrapingTargets;

    public BankScraperService(IServiceScopeFactory scopeFactory, ILogger<BankScraperService> logger, IOptions<List<ScrapingTarget>> scrapingTargets)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scrapingTargets = scrapingTargets.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Фоновый сервис парсинга запущен.");
        _timer = new Timer(DoWork, null, TimeSpan.FromSeconds(5), TimeSpan.FromHours(6));
        return Task.CompletedTask;
    }

    private void DoWork(object? state) { _ = ScrapeAllTargets(); }

    private async Task ScrapeAllTargets()
    {
        try
        {
            var extra = new PuppeteerExtra();
            extra.Use(new StealthPlugin());

            _logger.LogInformation("Загрузка браузера...");
            var fetcher = new BrowserFetcher();
            await fetcher.DownloadAsync();

            var launchOptions = new LaunchOptions
            {
                Headless = true, // false - чтобы видеть глазами
                Args = new[] { 
                    "--no-sandbox", 
                    "--disable-setuid-sandbox",
                    "--ignore-certificate-errors", 
                    "--window-size=1920,1080",
                    "--disable-dev-shm-usage", 
                    "--disable-gpu"}
            };

            await using var browser = await extra.LaunchAsync(launchOptions);
            _logger.LogInformation("Браузер запущен.");

            foreach (var target in _scrapingTargets)
            {
                try
                {
                    await using var page = await browser.NewPageAsync();
                    await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                    await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    _logger.LogInformation($"[1/2] Главная: {target.BankName}");
                    await page.GoToAsync(target.Url, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, // Ждем затихания сети
                        Timeout = 60000
                    });

                    try
                    {
                        _logger.LogInformation("Ждем прогрузки карточек...");
                        // Ждем появления элемента с классом product-card__wrap (макс 15 сек)
                        // Если используешь XPath в конфиге, здесь лучше использовать CSS селектор для скорости
                        await page.WaitForSelectorAsync(".product-card__wrap", new WaitForSelectorOptions { Timeout = 15000 });
                    }
                    catch
                    {
                        _logger.LogWarning("Таймаут ожидания карточек. Пробуем читать то, что есть...");
                    }

                    await ScrollPage(page);
                    //Делаем скрин что бы видить что там у бота
                    var debugFilename = $"DEBUG_{target.BankName.Replace(" ", "_")}";
                    var debugImgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{debugFilename}.png");
                    var debugHtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{debugFilename}.html");
                    string html = await page.GetContentAsync();
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(html);

                    var productNodes = htmlDoc.DocumentNode.SelectNodes(target.Selectors.ProductContainer);
                    if (productNodes == null)
                    {
                        _logger.LogWarning($"Контейнеры не найдены: {target.BankName}");
                        continue;
                    }

                    // : ОПРЕДЕЛЯЕМ ЧЕРНЫЙ СПИСОК ===
                    var blackList = new List<string>
                    {
                         "Экспресс-кредит",          // ВТБ (пишется через дефис)
                         "Экспресс кредит",          // На случай, если напишут через пробел
                         "Рассрочка от Сбера",       // Сбер
                         "Кредит на образование",    // Сбер (образовательный)
                         "образование с господдержкой", // Для надежности
                         "Кредит под залог",
                         "Залог недвижимости"
                    };
                    // ===========================================

                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    // --- СПИСОК УЖЕ ОБРАБОТАННЫХ НАЗВАНИЙ В ЭТОМ ЦИКЛЕ ---
                    var processedNames = new HashSet<string>();

                    foreach (var node in productNodes)
                    {
                        var rawName = ParseField(node, target.Selectors.ProductName);
                        var productName = CleanText(rawName);

                        // --- ФИЛЬТР МУСОРА
                        if (string.IsNullOrWhiteSpace(productName)) continue;

                        if (productName.Contains("Перейти в раздел", StringComparison.OrdinalIgnoreCase) ||
                            productName.Contains("На какие цели", StringComparison.OrdinalIgnoreCase) ||
                            productName.Contains("Могу ли я", StringComparison.OrdinalIgnoreCase) ||
                            productName.Contains("Вопросы и ответы", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation($"Пропущен мусорный блок: '{productName}'");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(productName)) continue;

                        if (blackList.Any(b => productName.Contains(b, StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.LogInformation($"Пропущен (черный список): {productName}");
                            continue; // Переходим к следующему продукту, не заходя внутрь
                        }

                        // ИГНОРИРУЕМ ДУБЛИКАТЫ НА СТРАНИЦЕ
                        if (processedNames.Contains(productName))
                        {
                            _logger.LogInformation($"Пропуск дубля на странице: {productName}");
                            continue;
                        }
                        processedNames.Add(productName);

                        var productUrlNode = node.SelectSingleNode(target.Selectors.ProductUrl);
                        var productUrl = productUrlNode?.GetAttributeValue("href", "") ?? "";

                        string interestRateText = "0", maxAmountText = "0", maxTermText = "0";

                        // --- ЭТАП 2: ЗАХОД ВНУТРЬ ---
                        if (!string.IsNullOrEmpty(productUrl))
                        {
                            try
                            {
                                var absoluteUrl = productUrl.StartsWith("http") ? productUrl : new Uri(new Uri(target.Url), productUrl).ToString();
                                _logger.LogInformation($"--> {productName}: Заходим внутрь...");

                                await using var detailPage = await browser.NewPageAsync();
                                await detailPage.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });

                                // 1. ИЗМЕНЕНИЕ: Используем DomContentLoaded вместо Networkidle2
                                // Это скажет браузеру: "Как только HTML загрузился, иди дальше, не жди картинки и скрипты"
                                await detailPage.GoToAsync(absoluteUrl, new NavigationOptions
                                {
                                    WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                                    Timeout = 60000
                                });

                                // 2. ИЗМЕНЕНИЕ: Явно ждем появления калькулятора (максимум 10 сек)
                                // Если он не появится, просто пойдем дальше, но дадим ему шанс прогрузиться
                                try
                                {
                                    await detailPage.WaitForSelectorAsync(".credit-calc", new WaitForSelectorOptions { Timeout = 30000 });
                                }
                                catch
                                {
                                    _logger.LogWarning("Калькулятор не появился за 10 сек, пробуем парсить так...");
                                }

                                // КЛИК ПО ВКЛАДКЕ "ПОДРОБНЕЕ" (Оставляем как было, но уменьшаем таймаут)
                                try
                                {
                                    // Используем префикс xpath/ чтобы Puppeteer понял, что это не CSS
                                    var moreTab = await detailPage.WaitForSelectorAsync("xpath///div[@role='tab' and contains(., 'Подробнее')]", new WaitForSelectorOptions { Timeout = 3000 });

                                    if (moreTab != null)
                                    {
                                        await moreTab.ClickAsync();
                                        await Task.Delay(1000);
                                    }
                                }
                                catch { /* Вкладки нет — не страшно */ }

                                await ScrollPage(detailPage);

                                // Получаем контент
                                var detailHtml = await detailPage.GetContentAsync();
                                var detailDoc = new HtmlDocument();
                                detailDoc.LoadHtml(detailHtml);

                                // === НАЧАЛО ВСТАВКИ: ПАРСИНГ КАЛЬКУЛЯТОРА СБЕРА ===
                                if (target.Selectors.DetailPageSelectors != null &&
      target.Selectors.DetailPageSelectors.ContainsKey("CalculatorContainer"))
                                {
                                    var calcSelector = target.Selectors.DetailPageSelectors["CalculatorContainer"];
                                    var calcNode = detailDoc.DocumentNode.SelectSingleNode(calcSelector);

                                    if (calcNode != null)
                                    {
                                        _logger.LogInformation($"Найден калькулятор для {productName}");

                                        // 1. Парсим скрытую ставку (надежный способ)
                                        // Ищем элемент с классом, содержащим 'hidden-rate'
                                        var rateNode = calcNode.SelectSingleNode(".//*[contains(@class, 'hidden-rate')]");
                                        if (rateNode != null && !string.IsNullOrWhiteSpace(rateNode.InnerText))
                                        {
                                            // Очищаем строку от мусора, оставляем только цифры и точку
                                            var rawRate = rateNode.InnerText; // "rate: 19.9%"
                                            var Ratematch = Regex.Match(rawRate, @"\d+[.,]\d+"); // Ищем именно дробное число
                                            if (Ratematch.Success)
                                            {
                                                interestRateText = Ratematch.Value.Replace(",", "."); // "19.9"
                                                _logger.LogInformation($"Извлечена точная ставка: {interestRateText}%");
                                            }
                                        }

                                        // 2. Умный поиск слайдеров (Сумма и Срок)
                                        // Находим ВСЕ элементы, которые являются слайдерами
                                        var sliders = calcNode.SelectNodes(".//div[@role='slider']");

                                        if (sliders != null)
                                        {
                                            foreach (var slider in sliders)
                                            {
                                                var valMaxStr = slider.GetAttributeValue("aria-valuemax", "0");

                                                // Пробуем распарсить максимум слайдера
                                                if (decimal.TryParse(valMaxStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var valMax))
                                                {
                                                    // ЛОГИКА ОПРЕДЕЛЕНИЯ:

                                                    // Если максимум больше 1 миллиона - это точно Сумма
                                                    if (valMax > 1_000_000)
                                                    {
                                                        maxAmountText = valMaxStr; // Записываем "20000000"
                                                        _logger.LogInformation($"Найден слайдер суммы: {maxAmountText}");
                                                    }
                                                    // Если максимум меньше 1000 (например, 60, 120, 360) - это точно Срок (мес)
                                                    else if (valMax > 0 && valMax <= 1000)
                                                    {
                                                        maxTermText = valMaxStr + " мес"; // Записываем "360 мес"
                                                        _logger.LogInformation($"Найден слайдер срока: {maxTermText}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Контейнер калькулятора (.credit-calc) не найден в HTML");
                                    }
                                }

                               // БЛОК ДЛЯ ВТБ(Вкладки) ===
                                if (target.BankName.Contains("ВТБ"))
                                {
                                    try
                                    {
                                        _logger.LogInformation("Пробуем найти вкладку 'Ставки'...");

                                        // 1. Ищем кнопку "Ставки" (по тексту в li)
                                        // Используем XPath, так как текст внутри
                                        var ratesTab = await detailPage.WaitForSelectorAsync("xpath///li[contains(., 'Ставки')]", new WaitForSelectorOptions { Timeout = 5000 });

                                        if (ratesTab != null)
                                        {
                                            await ratesTab.ClickAsync();
                                            await Task.Delay(1500); // Ждем прогрузки контента

                                            // Обновляем HTML после клика
                                            detailHtml = await detailPage.GetContentAsync();
                                            detailDoc.LoadHtml(detailHtml);

                                            _logger.LogInformation("Вкладка 'Ставки' открыта. Парсим условия...");

                                            // Вспомогательная функция для поиска значения по названию (Сумма, Срок, Ставка)
                                            // Логика: Ищем span с текстом "Сумма", берем его соседа сверху (preceding-sibling) и ищем там текст
                                            string GetValueByLabel(string label)
                                            {
                                                var node = detailDoc.DocumentNode.SelectSingleNode($"//span[contains(text(), '{label}')]/preceding-sibling::div//p");
                                                return node?.InnerText ?? "";
                                            }

                                            // 1. СТАВКА
                                            var rateRaw = GetValueByLabel("Ставка"); // "от 12,7%"
                                            if (!string.IsNullOrEmpty(rateRaw))
                                            {
                                                // Парсим "от 12,7%" -> 12.7
                                                var Ratematch = Regex.Match(rateRaw, @"\d+[.,]\d+");
                                                if (Ratematch.Success) interestRateText = Ratematch.Value.Replace(",", ".");
                                                _logger.LogInformation($"ВТБ Ставка: {interestRateText}");
                                            }

                                            // 2. СУММА (Там диапазон "100 тыс. – 7 млн ₽")
                                            var amountRaw = GetValueByLabel("Сумма");
                                            if (!string.IsNullOrEmpty(amountRaw))
                                            {
                                                // Нам нужна правая часть (максимум)
                                                // Разбиваем по тире (– или -)
                                                var parts = amountRaw.Split(new[] { '–', '-' });
                                                if (parts.Length > 0)
                                                {
                                                    // Берем последнюю часть (" 7 млн ₽")
                                                    maxAmountText = parts.Last().Trim();
                                                    _logger.LogInformation($"ВТБ Сумма: {maxAmountText}");
                                                }
                                            }

                                            // 3. СРОК (Там диапазон "6 мес – 7 лет")
                                            var termRaw = GetValueByLabel("Срок");
                                            if (!string.IsNullOrEmpty(termRaw))
                                            {
                                                var parts = termRaw.Split(new[] { '–', '-' });
                                                if (parts.Length > 0)
                                                {
                                                    maxTermText = parts.Last().Trim(); // "7 лет"
                                                    _logger.LogInformation($"ВТБ Срок: {maxTermText}");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning($"ВТБ: Не удалось пропарсить вкладки (возможно Экспресс-кредит): {ex.Message}");
                                        // Ничего страшного, дальше сработает стандартный поиск по тексту страницы
                                    }
                                }

                                  //ПРОВЕРЯЕМ, НЕ НАШЛИ ЛИ МЫ УЖЕ ДАННЫЕ ===
                                // 1. Ищем ставку в тексте, ТОЛЬКО ЕСЛИ мы её еще не нашли в калькуляторе/вкладках
                                if (ParseRate(interestRateText) == 0)
                                {
                                    interestRateText = FindTextByKeywords(detailDoc, new[] { "%", "ставка" }) ?? "0";
                                }

                                // 2. Ищем сумму в тексте, ТОЛЬКО ЕСЛИ мы её еще не нашли
                                if (ParseAmount(maxAmountText) == 0)
                                {
                                    maxAmountText = FindTextByKeywords(detailDoc, new[] { "₽", "руб", "сумма", "лимит" }) ?? "0";

                                    // Если в тексте не нашли, ищем в input'ах
                                    if (ParseAmount(maxAmountText) == 0)
                                    {
                                        var inputValue = FindValueInInputs(detailDoc, "amount");
                                        if (!string.IsNullOrEmpty(inputValue)) maxAmountText = inputValue;
                                    }
                                }

                                // 3. Ищем срок в тексте, ТОЛЬКО ЕСЛИ мы его еще не нашли
                                if (ParseTermInMonths(maxTermText) == 0)
                                {
                                    maxTermText = FindTextByKeywords(detailDoc, new[] { "лет", "год", "мес", "срок" }) ?? "0";
                                }

                            } 
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Ошибка деталей: {ex.Message}");
                            } 
                        } 
                        decimal rate = ParseRate(interestRateText);
                        decimal amount = ParseAmount(maxAmountText);
                        int term = ParseTermInMonths(maxTermText);

                        // === ВСТАВКА: ФИЛЬТР НЕРЕАЛИСТИЧНЫХ ДАННЫХ ===

                        // 1. Фильтр ставки

                        if (rate < 4.0m)
                        {
                            rate = 0; 
                        }

                        // 2. Фильтр суммы

                        if (amount < 50000)
                        {
                            amount = 0; // Сбрасываем, так как это явно не максимальный лимит
                        }

                        //  БАЗА ДАННЫХ 
                        var existingProduct = await dbContext.LoanProducts
                            .ToListAsync(); // В память для сравнения

                        var match = existingProduct.FirstOrDefault(p =>
                            p.BankName == target.BankName &&
                            CleanText(p.ProductName).Equals(productName, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            // === УМНОЕ ОБНОВЛЕНИЕ (SMART UPDATE) ===
                            // Мы обновляем поле в базе ТОЛЬКО если новое значение валидное (> 0).
                            // Если мы нашли "0" или мусор, мы оставляем старое значение из базы.

                            if (rate > 0)
                            {
                                match.InterestRate = rate;
                            }

                            if (amount > 0)
                            {
                                match.MaxAmount = amount;
                            }

                            if (term > 0)
                            {
                                match.MaxTermInMonths = term;
                            }

                            // Ссылку обновляем всегда, вдруг поменялась
                            if (!string.IsNullOrEmpty(productUrl))
                            {
                                match.Url = productUrl;
                            }

                            match.LastUpdated = DateTime.UtcNow;

                            _logger.LogInformation($"Обновлен: {productName} | Ставка: {match.InterestRate}% | Сумма: {match.MaxAmount}");
                        }
                        else
                        {
                            // Если продукта нет в базе - создаем как есть (даже если там нули)
                            var newProduct = new LoanProduct
                            {
                                BankName = target.BankName,
                                ProductName = productName,
                                Url = productUrl ?? "",
                                InterestRate = rate,
                                MaxAmount = amount,
                                MaxTermInMonths = term,
                                LastUpdated = DateTime.UtcNow
                            };
                            await dbContext.LoanProducts.AddAsync(newProduct);
                            _logger.LogInformation($"Добавлен новый: {productName} | {amount}");
                        }
                        await dbContext.SaveChangesAsync();
                    }
                    _logger.LogInformation($"--- ГОТОВО: {target.BankName} ---");
                }
                catch (Exception ex) { _logger.LogError(ex, $"Ошибка банка {target.BankName}"); }
            }
        }
        catch (Exception ex) { _logger.LogCritical(ex, "КРИТИЧЕСКАЯ ОШИБКА"); }
    }

    // --- МЕТОДЫ ---

    private string? FindValueInInputs(HtmlDocument doc, string typeHint)
    {
        // Ищем input, у которого в value есть большая цифра (для суммы)
        var inputs = doc.DocumentNode.SelectNodes("//input[@value]");
        if (inputs == null) return null;

        foreach (var input in inputs)
        {
            var val = input.GetAttributeValue("value", "");
            if (string.IsNullOrEmpty(val)) continue;

            // Если ищем сумму - ищем большое число
            if (typeHint == "amount" && Regex.IsMatch(val, @"\d{5,}")) return val;
        }
        return null;
    }

    private async Task ScrollPage(IPage page)
    {
        if (page == null || page.IsClosed) return;
        try
        {
            // Проверяем, жив ли контекст перед выполнением
            await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(2000);
            await page.EvaluateExpressionAsync("window.scrollTo(0, 0)");
        }
        catch (Exception ex)
        {
            // Игнорируем ошибку уничтоженного контекста, так как это значит, что страница и так обновилась
            if (!ex.Message.Contains("Execution context was destroyed") &&
                !ex.Message.Contains("Target closed"))
            {
                // Логируем только реальные ошибки, а не навигацию
                _logger.LogWarning($"Ошибка скролла (не критично): {ex.Message}");
            }
        }
    }


    private string ParseField(HtmlNode node, string xpath)
    {
        if (string.IsNullOrEmpty(xpath)) return "";
        try { return node.SelectSingleNode(xpath)?.InnerText.Trim() ?? ""; } catch { return ""; }
    }

    private string CleanText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var decoded = WebUtility.HtmlDecode(input);
        decoded = decoded.Replace('\u00A0', ' ');
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private string? FindTextByKeywords(HtmlDocument doc, string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//*[contains(text(), '{keyword}') and not(self::script)]");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    if (Regex.IsMatch(node.InnerText, @"\d")) return CleanText(node.InnerText);
                    if (node.ParentNode != null && Regex.IsMatch(node.ParentNode.InnerText, @"\d"))
                        return CleanText(node.ParentNode.InnerText);
                }
            }
        }
        return null;
    }
    private decimal ParseAmount(string? input)
    {
        if (string.IsNullOrEmpty(input)) return 0;

        // 1. Чистим строку
        string lowerInput = input.ToLower().Replace("&nbsp;", " ").Replace('\u00A0', ' ');

        // 2. Убираем пробелы между цифрами (делаем из "300 000" -> "300000")
        string cleanInput = Regex.Replace(lowerInput, @"(?<=\d)\s+(?=\d)", "");

        // 3. Ищем число
        var match = Regex.Match(cleanInput, @"\d+([.,]\d+)?");
        if (match.Success)
        {
            var numberStr = match.Value.Replace(",", ".");
            if (decimal.TryParse(numberStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                // Если число уже большое (больше 10000), считаем, что это уже рубли.
                // Например, если нашли "30000000", то не надо умножать его на "млн", даже если слово "млн" есть в строке.
                if (result > 10000)
                {
                    return result;
                }

                // Множители применяем ТОЛЬКО для маленьких чисел (например "5 млн", "300 тыс")
                decimal multiplier = 1;

                if (lowerInput.Contains("млн") || lowerInput.Contains("миллион") || lowerInput.Contains("mln"))
                    multiplier = 1_000_000;
                else if (lowerInput.Contains("тыс") || lowerInput.Contains("тысяч") || lowerInput.Contains("k"))
                    multiplier = 1_000;

                return result * multiplier;
            }
        }
        return 0;
    }

    // ---  ПАРСИНГ СТАВКИ ---
    private decimal ParseRate(string? input)
    {
        if (string.IsNullOrEmpty(input)) return 0;

        // Сразу меняем запятую на точку, чтобы не было путаницы
        input = input.Replace(",", ".");

        // Ищем дробное число (например 19.9) ИЛИ целое (например 20)
        var match = Regex.Match(input, @"\d+(\.\d+)?");

        if (match.Success)
        {
            if (decimal.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                // Фильтр от мусора: ставка обычно от 0.1% до 100%
                if (result > 0 && result < 100) return result;
            }
        }
        return 0;
    }


    private int ParseTermInMonths(string? input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        var match = Regex.Match(input, @"\d+");
        if (!match.Success) return 0;
        var number = int.Parse(match.Value);
        if (input.Contains("лет") || input.Contains("год")) return number * 12;
        if (input.Contains("мес")) return number;
        if (number <= 30) return number * 12;
        return number;
    }

    public Task StopAsync(CancellationToken c) { _timer?.Change(Timeout.Infinite, 0); return Task.CompletedTask; }
    public void Dispose() { _timer?.Dispose(); }
}