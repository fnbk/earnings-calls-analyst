using Azure;
using Azure.AI.OpenAI;
using ClosedXML.Excel;
using FinancialAnalysis.Configuration;
using FinancialAnalysis.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Serilog;

namespace FinancialAnalysis
{
    public class App : IDisposable
    {
        private readonly AppSettings _settings;

        private readonly string _summarySystemMessage;
        private readonly string _scoresSystemMessage;
        private readonly string _naiveScoreSystemMessage;

        private readonly HttpClient _httpClient;
        private readonly OpenAIClient _openAIClient;

        private readonly string _cacheDir;
        private readonly string _aiCacheDir;
        private readonly string _outDir;

        private static readonly SemaphoreSlim _apiSemaphore = new SemaphoreSlim(5); // Financial Modeling Prep API
        private static readonly SemaphoreSlim _openAiSemaphore = new SemaphoreSlim(3); // Match Azure OpenAI RPM limit
        private readonly TokenBucketRateLimiter _openAiRateLimiter = new(3, 10); // 3 tokens/sec (180 RPM), burst of 10

        private int _completedTickers;
        private bool _disposed;

        private readonly ILogger<App> _logger;
        //private readonly IConfiguration _configuration;

        public App(
            ILogger<App> logger,
            AppSettings settings,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _settings = settings;
            _httpClient = httpClientFactory.CreateClient();

            _openAIClient = new OpenAIClient(
                new Uri(_settings.AzureOaiEndpoint),
                new AzureKeyCredential(_settings.AzureOaiKey)
            );

            var scriptDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
            _cacheDir = Path.Combine(scriptDir, "cache", "http");
            _aiCacheDir = Path.Combine(scriptDir, "cache", "ai");
            _outDir = Path.Combine(scriptDir, "out");

            // make sure directories exists
            Directory.CreateDirectory(_cacheDir);
            Directory.CreateDirectory(_aiCacheDir);
            Directory.CreateDirectory(_outDir);

            _summarySystemMessage = File.ReadAllText("Content/system-message-transcript-summary.txt");
            _scoresSystemMessage = File.ReadAllText("Content/system-message-detailed-scores.txt");
            _naiveScoreSystemMessage = File.ReadAllText("Content/system-message-naive-score.txt");
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting financial analysis process");
            try
            {
                //
                // 1. Get S&P 500 constituents and data
                //

                var dateRange = _settings.OnlyLatest ?
                    new { Start = DateTime.Now.AddMonths(-4), End = DateTime.Now } :
                    new { Start = _settings.StartDate, End = _settings.EndDate };

                // Get S&P 500 constituents for the period
                var blacklist = new[] { "EXC", "COTY" };
                var tickers = await GetSp500ConstituentsAsync(dateRange.Start, dateRange.End, cancellationToken);
                tickers = tickers.Where(t => !blacklist.Contains(t)).ToList();

                // var tickers = new List<string>
                // {
                //     "4058.T", "KSPI", "PAX", "5032.T", "MPWR", "CUV.AX", "DLO", "GCT", "KNOS.L", "antin.pa",
                //     "SCT.L", "SHO.WA", "3679.T", "3856.T", "4832.T", "AOF.DE", "FNOX.ST", "MUM.DE", "300750.SZ", "IFI.WA",
                //     "RMD", "UPSALE.ST", "YU.l", "3769.T", "6920.T", "000858.SZ", "JIN.AX", "NTES", "bft.wa", "CSBB",
                //     "FDJ.PA", "HEM.ST", "MELI", "NVDA", "PCTY", "RVRC.ST", "SL.MI", "2413.T", "3306.hk", "6861.T",
                //     "BWMX", "EVO.ST", "FINV", "GPP.WA", "SECARE.ST", "SLEEP.ST", "TTD", "2020.HK", "2331.HK", "3038.T",
                //     "603259.SS", "ADBE", "DOCS", "GMAB.CO", "INTU", "NYT", "PLW.WA", "SPNS", "002475.SZ", "300015.sz",
                //     "3600.hk", "6088.T", "BMI", "CURN", "ESQ", "FIX", "GARAN.IS", "kap.il", "MSCI", "NBIX",
                //     "NOVO-B.CO", "ODD", "PGNY", "RBT.PA", "STMN.SW", "XPEL", "600519.SS", "AFYA", "ALESK.PA", "ANET",
                //     "bol.bk", "dom.wa", "DV", "HCLTECH.NS", "HUB.AX", "ICLR", "mza.wa", "NEX.PA", "PME.AX", "PPA.AT",
                //     "SQN.SW", "VAIAS.HE", "YALA", "YOU", "300274.SZ", "035420.KS", "3659.T", "6951.T", "AMAL", "crox",
                //     "cube", "data.l"
                // };

                // var tickers = new List<string> {
                //     "AAPL", "NVDA", "MSFT", "GOOGL", "GOOG", "AMZN", "META", "TSLA", "AVGO", "WMT",
                //     "JPM", "LLY", "V", "XOM", "MA", "UNH", "ORCL", "COST", "HD", "PG",
                //     "NFLX", "BAC", "JNJ", "CRM", "ABBV", "CVX", "KO", "WFC", "TMUS", "MRK",
                //     "CSCO", "BX", "NOW", "ACN", "MS", "AXP", "TMO", "ISRG", "LIN", "IBM",
                //     "PEP", "MCD", "GE", "ABT", "AMD", "GS", "DIS", "PM", "ADBE", "CAT",
                //     "QCOM", "TXN", "DHR", "INTU", "PLTR", "VZ", "BKNG", "RTX", "T", "SPGI",
                //     "BLK", "AMAT", "ANET", "C", "PFE", "LOW", "SYK", "AMGN", "NEE", "BSX",
                //     "HON", "PGR", "UBER", "UNP", "KKR", "CMCSA", "ETN", "TJX", "COP", "SCHW",
                //     "BA", "DE", "ADP", "FI", "MU", "PANW", "LMT", "GILD", "BMY", "MDT",
                //     "GEV", "UPS", "VRTX", "CB", "ADI", "SBUX", "MMC", "NKE", "LRCX", "PLD",
                // };
                
                // var tickers = new List<string> {
                //     "AAPL", "NVDA", "MSFT", "GOOGL", "GOOG", "AMZN", "META", "TSLA", "AVGO", "WMT",
                //     "JPM", "LLY", "V", "XOM", "MA", "UNH", "ORCL", "COST", "HD", "PG",
                // };

                // Use ConcurrentBag to safely collect results from parallel operations
                var earningCallData = new ConcurrentBag<EarningsCall>();

                // Process tickers in parallel with a maximum degree of parallelism
                var maxDegreeOfParallelism = 3; // Match Azure OpenAI RPM limit

                await Parallel.ForEachAsync(
                    tickers,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism,
                        CancellationToken = cancellationToken
                    },
                    async (ticker, ct) =>
                    {
                        try
                        {
                            var startTime = DateTime.Now;

                            var data = new Dictionary<string, EarningsCall>();

                            _logger.LogDebug("{Ticker}: Adding earning call dates", ticker);
                            await AddEarningCallDatesAsync(data, dateRange.Start, dateRange.End, ticker, ct);

                            _logger.LogDebug("{Ticker}: Adding earnings surprises", ticker);
                            await AddEarningsSurprisesAsync(data, _settings.NumberOfQuartersForEpsHistory, ticker, ct);

                            _logger.LogDebug("{Ticker}: Adding transcripts", ticker);
                            await AddTranscriptsAsync(data, ticker);

                            _logger.LogDebug("{Ticker}: Adding post earning call date & prices", ticker);
                            await AddPostEarningCallDatePricesAsync(data, ticker);

                            var earningCallDates = data.Keys.ToList();

                            foreach (var date in earningCallDates)
                            {
                                _logger.LogDebug("{Ticker}: Enriching with AI and SUE scores for {Date}", ticker, date);
                                await EnrichWithAisAndSueScoresAsync(data, date);
                            }

                            _logger.LogDebug("{Ticker}: Calculating AIS delta", ticker);
                            foreach (var date in earningCallDates)
                            {
                                CalculateAisDelta(data, date);
                            }

                            foreach (var date in earningCallDates)
                            {
                                earningCallData.Add(data[date]);
                            }

                            var completedCount = Interlocked.Increment(ref _completedTickers);
                            _logger.LogInformation("{Ticker} ({CompletedCount}/{TotalCount}): Completed in {Duration:F2}s",
                                ticker, completedCount, tickers.Count, (DateTime.Now - startTime).TotalSeconds);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing {Ticker}", ticker);
                            throw;
                        }
                    });

                //
                // 2. Group and Calculate Percentiles
                //

                var sortedEarningCallData = earningCallData.OrderBy(x => x.Date).ToList();
                var earningCallDateSnapshots = GetMostRecentCalls(sortedEarningCallData);
                EnrichWithPercentiles(earningCallDateSnapshots);

                //
                // 3. Export
                //

                // Write Snaphot File
                WriteSnapshotFile(earningCallDateSnapshots);

                // Create Excel File
                if (_settings.OnlyLatest)
                {
                    CreateLatestExcel(earningCallDateSnapshots);
                }
                else
                {
                    CreateFullExcel(earningCallDateSnapshots);
                }

                _logger.LogInformation("Financial analysis completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during financial analysis");
                throw;
            }
        }

        private void WriteSnapshotFile(Dictionary<string, List<EarningsCall>> earningCallDateSnapshots)
        {
            var keysToInclude = new[]
                {
                    "date", "symbol", "year", "quarter",
                    "estimatedEarning", "actualEarningResult", "ais", "ais_delta", "ais_naive", "sue",
                    "ais%", "ais_delta%", "ais_naive%", "sue%", "score",
                    "day0_date", "day1_date", "day2_date", "day7_date", "day30_date",
                    "day0_price", "day1_price", "day2_price", "day7_price", "day30_price",
                    "day0_sp500_price", "day1_sp500_price", "day2_sp500_price", "day7_sp500_price", "day30_sp500_price"
                };
            var orderList = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText("Content/custom-order.json"))
                .Where(key => keysToInclude.Contains(key))
                .ToList();

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new FilteredOrderedContractResolver(orderList, keysToInclude)
            });

            using var writer = new StringWriter();
            using var jsonWriter = new JsonTextWriter(writer);

            // Convert Dictionary to ordered list of key-value pairs
            var orderedSnapshots = earningCallDateSnapshots
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.OrderBy(x => x.Symbol).ToList()
                );

            serializer.Serialize(jsonWriter, orderedSnapshots);

            var snapshotsPath = Path.Combine(_outDir, "snapshots.json");
            File.WriteAllText(snapshotsPath, writer.ToString());
            _logger.LogInformation("Snapshot successfully written to {Path}", snapshotsPath);
        }

        private async Task AddEarningCallDatesAsync(Dictionary<string, EarningsCall> data, DateTime startDate, DateTime endDate, string ticker, CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var url = $"https://financialmodelingprep.com/api/v4/earning_call_transcript?symbol={ticker}&apikey={_settings.FinancialModelingPrepKey}";
            var response = await GetWithRetryAsync(url, 3, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching earnings call dates for {ticker}: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseData = JArray.Parse(responseContent);

            foreach (var item in responseData)
            {
                var quarter = item[0].Value<int>();
                var year = item[1].Value<int>();
                var dateTime = item[2].Value<string>();
                var date = dateTime.Split()[0];
                var callDate = DateTime.Parse(date);

                // Skip if outside date range
                if (callDate < startDate || callDate > endDate)
                    continue;

                data[date] = new EarningsCall
                {
                    Symbol = ticker,
                    DateTime = dateTime,
                    Year = year,
                    Quarter = quarter,
                    Date = date
                };
            }
        }

        private async Task AddEarningsSurprisesAsync(Dictionary<string, EarningsCall> data, int numberOfQuartersForEpsHistory, string ticker, CancellationToken cancellationToken = default)
        {
            // Set defaults for all dates
            foreach (var date in data.Keys)
            {
                data[date].ActualEarningResult = null;
                data[date].EstimatedEarning = null;
                data[date].HistoricalEps = null;
            }

            var url = $"https://financialmodelingprep.com/api/v3/earnings-surprises/{ticker}?apikey={_settings.FinancialModelingPrepKey}";
            var response = await GetWithRetryAsync(url, 3, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching EPS surprises for {ticker}: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseData = JArray.Parse(responseContent);

            // Transform into dictionary for easier handling
            var earningsSurprisesData = responseData
                .ToDictionary(
                    elem => elem["date"].Value<string>(),
                    elem => elem
                );

            var earningsSurprisesDates = earningsSurprisesData.Keys
                .OrderByDescending(date => DateTime.Parse(date))
                .ToList();

            foreach (var date in data.Keys)
            {
                var nearestEarningCallSurpriseDate = FindNearestDate(earningsSurprisesDates, date);

                if (nearestEarningCallSurpriseDate != null)
                {
                    var historicalEps = GetHistoricalEps(responseData, nearestEarningCallSurpriseDate, numberOfQuartersForEpsHistory);
                    var surpriseData = earningsSurprisesData[nearestEarningCallSurpriseDate];

                    try
                    {
                        data[date].ActualEarningResult = surpriseData["actualEarningResult"].Value<decimal>();
                        data[date].EstimatedEarning = surpriseData["estimatedEarning"].Value<decimal>();
                        data[date].HistoricalEps = historicalEps;
                    }
                    catch (InvalidCastException)
                    {
                        _logger.LogWarning("{Ticker}: Invalid earnings data for {Date}, removing entry", ticker, date);
                        data.Remove(date);
                    }
                }
            }
        }

        private string FindNearestDate(List<string> sortedDates, string targetDate)
        {
            var targetDateTime = DateTime.Parse(targetDate);

            return sortedDates
                .Select(date => new
                {
                    Date = date,
                    Diff = Math.Abs((DateTime.Parse(date) - targetDateTime).Days)
                })
                .OrderBy(x => x.Diff)
                .FirstOrDefault()
                ?.Date;
        }

        private List<decimal> GetHistoricalEps(JArray responseData, string currentDate, int numberOfHistoricalEps)
        {
            var sortedResponseData = responseData
                .OrderByDescending(x => DateTime.Parse(x["date"].Value<string>()))
                .ToList();

            var currentIndex = sortedResponseData.FindIndex(
                x => x["date"].Value<string>() == currentDate
            );

            if (currentIndex == -1)
            {
                return new List<decimal>();
            }

            return sortedResponseData
                .Skip(currentIndex + 1)
                .Take(numberOfHistoricalEps)
                .Select(x => x["actualEarningResult"].Value<decimal>())
                .ToList();
        }

        private async Task AddTranscriptsAsync(Dictionary<string, EarningsCall> data, string ticker)
        {
            foreach (var date in data.Keys)
            {
                var year = (int)data[date].Year;
                var quarter = (int)data[date].Quarter;

                var url = $"https://financialmodelingprep.com/api/v3/earning_call_transcript/{ticker}?year={year}&quarter={quarter}&apikey={_settings.FinancialModelingPrepKey}";
                var response = await GetWithRetryAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to retrieve transcript for {ticker} for {year} Q{quarter}, status code: {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var responseData = JArray.Parse(responseContent);

                var content = responseData[0]["content"].Value<string>();
                if (string.IsNullOrEmpty(content))
                {
                    _logger.LogWarning("{Ticker} no transcript for {Year} Q{Quarter}, date: {Date}", ticker, year, quarter, date);

                    data.Remove(date);
                    continue;
                }
                data[date].Content = content;
            }
        }

        private async Task AddPostEarningCallDatePricesAsync(Dictionary<string, EarningsCall> data, string ticker)
        {
            foreach (var dateString in data.Keys)
            {
                var earningCallDate = DateTime.Parse(dateString);
                var startDate = earningCallDate.AddDays(-7);
                var endDate = earningCallDate.AddDays(37);

                var start = startDate.ToString("yyyy-MM-dd");
                var end = endDate.ToString("yyyy-MM-dd");

                // Get stock prices
                var url = $"https://financialmodelingprep.com/api/v3/historical-price-full/{ticker}?from={start}&to={end}&apikey={_settings.FinancialModelingPrepKey}";
                var response = await GetWithRetryAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Error fetching stock prices for {ticker} on {dateString}: {response.StatusCode}");
                }

                var jsonResponse = JObject.Parse(await response.Content.ReadAsStringAsync());
                var historicalData = jsonResponse["historical"]?.ToList();
                if (historicalData == null)
                {
                    _logger.LogWarning("No historical price data found for {Ticker} on {Date}", ticker, dateString);
                    continue;
                }

                var historicalDataSorted = historicalData
                    .OrderBy(x => DateTime.Parse(x["date"].Value<string>()))
                    .ToList();

                // Get S&P 500 prices
                var urlSp500 = $"https://financialmodelingprep.com/api/v3/historical-price-full/%5EGSPC?from={start}&to={end}&apikey={_settings.FinancialModelingPrepKey}";
                var responseSp500 = await GetWithRetryAsync(urlSp500);

                if (!responseSp500.IsSuccessStatusCode)
                {
                    throw new Exception($"Error fetching stock prices for S&P500 on {dateString}: {responseSp500.StatusCode}");
                }

                var jsonResponseSp500 = JObject.Parse(await responseSp500.Content.ReadAsStringAsync());
                var historicalDataSp500 = jsonResponseSp500["historical"].ToList();
                var historicalDataSp500Sorted = historicalDataSp500
                    .OrderBy(x => DateTime.Parse(x["date"].Value<string>()))
                    .ToList();

                var afterDayTwo = earningCallDate.AddDays(1);
                var afterDaySeven = earningCallDate.AddDays(7);
                var afterDayThirty = earningCallDate.AddDays(30);

                // Initialize variables
                string closestDayZero = null, closestDayOne = null, closestDayTwo = null,
                       closestDaySeven = null, closestDayThirty = null;
                decimal? closestDayZeroPrice = null, closestDayOnePrice = null, closestDayTwoPrice = null,
                         closestDaySevenPrice = null, closestDayThirtyPrice = null;

                foreach (var elem in historicalDataSorted)
                {
                    var priceDate = DateTime.Parse(elem["date"].Value<string>());
                    var closePrice = elem["close"].Value<decimal>();

                    if (priceDate < earningCallDate)
                    {
                        closestDayZero = priceDate.ToString("yyyy-MM-dd");
                        closestDayZeroPrice = closePrice;
                    }
                    else if (priceDate == earningCallDate)
                    {
                        closestDayOne = priceDate.ToString("yyyy-MM-dd");
                        closestDayOnePrice = closePrice;
                    }
                    else if (priceDate >= afterDayTwo && closestDayTwo == null)
                    {
                        closestDayTwo = priceDate.ToString("yyyy-MM-dd");
                        closestDayTwoPrice = closePrice;
                    }
                    else if (priceDate >= afterDaySeven && closestDaySeven == null)
                    {
                        closestDaySeven = priceDate.ToString("yyyy-MM-dd");
                        closestDaySevenPrice = closePrice;
                    }
                    else if (priceDate >= afterDayThirty && closestDayThirty == null)
                    {
                        closestDayThirty = priceDate.ToString("yyyy-MM-dd");
                        closestDayThirtyPrice = closePrice;
                    }
                }

                // Create dictionary of S&P 500 prices
                var sp500Prices = historicalDataSp500Sorted.ToDictionary(
                    elem => elem["date"].Value<string>(),
                    elem => elem["close"].Value<decimal>()
                );

                // Update data dictionary with all collected values
                data[dateString].Day0Date = closestDayZero;
                data[dateString].Day1Date = closestDayOne;
                data[dateString].Day2Date = closestDayTwo;
                data[dateString].Day7Date = closestDaySeven;
                data[dateString].Day30Date = closestDayThirty;
                data[dateString].Day0Price = closestDayZeroPrice;
                data[dateString].Day1Price = closestDayOnePrice;
                data[dateString].Day2Price = closestDayTwoPrice;
                data[dateString].Day7Price = closestDaySevenPrice;
                data[dateString].Day30Price = closestDayThirtyPrice;
                data[dateString].Day0Sp500Price = closestDayZero != null ? sp500Prices.GetValueOrDefault(closestDayZero) : null;
                data[dateString].Day1Sp500Price = closestDayOne != null ? sp500Prices.GetValueOrDefault(closestDayOne) : null;
                data[dateString].Day2Sp500Price = closestDayTwo != null ? sp500Prices.GetValueOrDefault(closestDayTwo) : null;
                data[dateString].Day7Sp500Price = closestDaySeven != null ? sp500Prices.GetValueOrDefault(closestDaySeven) : null;
                data[dateString].Day30Sp500Price = closestDayThirty != null ? sp500Prices.GetValueOrDefault(closestDayThirty) : null;
            }
        }

        private async Task EnrichWithAisAndSueScoresAsync(Dictionary<string, EarningsCall> data, string date)
        {
            var ticker = (string)data[date].Symbol;
            var quarter = (int)data[date].Quarter;
            var year = (int)data[date].Year;
            var transcript = (string)data[date].Content;

            DateTime startTime;

            _logger.LogDebug("{Ticker}: Getting transcript summary for {Date}", ticker, date);
            startTime = DateTime.Now;
            var summary = await GetSummaryAsync(transcript);
            _logger.LogDebug("{Ticker}: Got transcript summary for {Date} in {Duration:F2}s", ticker, date, (DateTime.Now - startTime).TotalSeconds);

            _logger.LogDebug("{Ticker}: Getting AIS score for {Date}", ticker, date);
            startTime = DateTime.Now;
            var scoringDetails = await GetScoresAsync(summary);
            var overallScore = GetOverallScore(scoringDetails);
            _logger.LogDebug("{Ticker}: Got AIS score for {Date} in {Duration:F2}s", ticker, date, (DateTime.Now - startTime).TotalSeconds);

            _logger.LogDebug("{Ticker}: Getting naive AIS score for {Date}", ticker, date);
            startTime = DateTime.Now;
            var naiveScoringDetails = await GetNaiveScoreAsync(summary);
            _logger.LogDebug("{Ticker}: Got naive AIS score for {Date} in {Duration:F2}s", ticker, date, (DateTime.Now - startTime).TotalSeconds);

            var naiveScore = JObject.Parse(naiveScoringDetails)["score"].Value<decimal>();
            var sueScore = GetSueScore(data[date]);

            // Update data dictionary
            data[date].Sue = sueScore;
            data[date].Ais = overallScore;
            data[date].AisNaive = naiveScore;
            data[date].AisScoringDetails = JObject.Parse(scoringDetails);
            data[date].AisNaiveScoringDetails = JObject.Parse(naiveScoringDetails);
            data[date].TranscriptSummary = summary;
            data[date].PromptSummarize = _summarySystemMessage;
            data[date].PromtScore = _scoresSystemMessage;
            data[date].ExecutionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void CalculateAisDelta(Dictionary<string, EarningsCall> data, string date)
        {
            var symbol = (string)data[date].Symbol;
            var prevDate = FindPreviousDate(data, date);

            decimal aisPrev = 0;
            if (prevDate != null && data[prevDate].Ais != null)
            {
                aisPrev = Convert.ToDecimal(data[prevDate].Ais);
            }

            var ais = Convert.ToDecimal(data[date].Ais);
            var delta = ais - aisPrev;

            data[date].AisDelta = delta;
        }

        private decimal? GetSueScore(EarningsCall item)
        {
            var actualEarning = Convert.ToDecimal(item.ActualEarningResult);
            var estimatedEarning = Convert.ToDecimal(item.EstimatedEarning);
            var historicalEarnings = (List<decimal>)item.HistoricalEps;

            if (historicalEarnings == null || !historicalEarnings.Any())
            {
                return null;
            }

            var stdDev = CalculateStandardDeviation(historicalEarnings);
            return stdDev != 0 ? (actualEarning - estimatedEarning) / stdDev : 0;
        }

        private decimal CalculateStandardDeviation(List<decimal> values)
        {
            var avg = values.Average();
            var sumOfSquaresOfDifferences = values.Select(val => (val - avg) * (val - avg)).Sum();
            var variance = sumOfSquaresOfDifferences / values.Count;
            return (decimal)Math.Sqrt((double)variance);
        }

        private decimal GetOverallScore(string scoresJson)
        {
            var data = JObject.Parse(scoresJson);
            var scores = ExtractScores(data);
            return scores.Any() ? scores.Average() : 0;
        }

        private List<decimal> ExtractScores(JToken data)
        {
            var scores = new List<decimal>();

            if (data is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value is JObject valueObj)
                    {
                        if (valueObj["Score"] != null)
                        {
                            scores.Add(valueObj["Score"].Value<decimal>());
                        }
                        else
                        {
                            scores.AddRange(ExtractScores(valueObj));
                        }
                    }
                }
            }

            return scores;
        }

        private async Task<string> GetSummaryAsync(string earningsCallTranscript, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(earningsCallTranscript))
            {
                _logger.LogWarning("Empty earnings call transcript provided");
                return string.Empty;
            }

            // Limit transcript length for GPT-3.5
            if (_settings.AzureOaiDeployment == "gpt-35-turbo")
            {
                earningsCallTranscript = earningsCallTranscript[..Math.Min(earningsCallTranscript.Length, 16000)];
            }

            // Try cache first
            var (exists, cachedContent) = await TryGetFromAICacheAsync(earningsCallTranscript, _summarySystemMessage);
            if (exists)
            {
                return cachedContent;
            }

            return await GetOpenAIResponseWithRetryAsync(async (ct) =>
            {
                var messages = new List<ChatRequestMessage>
                {
                    new ChatRequestSystemMessage(_summarySystemMessage),
                    new ChatRequestUserMessage(earningsCallTranscript),
                };

                var options = new ChatCompletionsOptions
                {
                    MaxTokens = 4096,
                    Temperature = 0.2f,
                    NucleusSamplingFactor = 0.95f,
                    FrequencyPenalty = 0,
                    PresencePenalty = 0,
                    DeploymentName = _settings.AzureOaiDeployment,
                };

                foreach (var message in messages)
                {
                    options.Messages.Add(message);
                }

                var response = await _openAIClient.GetChatCompletionsAsync(options, ct);
                var content = response.Value.Choices[0].Message.Content;

                // Cache the response
                await SaveToAICacheAsync(earningsCallTranscript, _summarySystemMessage, content);

                return content;
            }, cancellationToken);
        }

        private async Task<string> GetScoresAsync(string earningsCallTranscriptSummary, CancellationToken cancellationToken = default)
        {
            // Try cache first
            var (exists, cachedContent) = await TryGetFromAICacheAsync(earningsCallTranscriptSummary, _scoresSystemMessage);
            if (exists)
            {
                return cachedContent;
            }

            return await GetOpenAIResponseWithRetryAsync(async (ct) =>
            {
                var messages = new List<ChatRequestMessage>
                {
                    new ChatRequestSystemMessage(_scoresSystemMessage),
                    new ChatRequestUserMessage(earningsCallTranscriptSummary),
                };

                var options = new ChatCompletionsOptions
                {
                    MaxTokens = 4096,
                    Temperature = 0.2f,
                    NucleusSamplingFactor = 0.95f,
                    FrequencyPenalty = 0,
                    PresencePenalty = 0,
                    ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
                    DeploymentName = _settings.AzureOaiDeployment,
                };

                foreach (var message in messages)
                {
                    options.Messages.Add(message);
                }

                var response = await _openAIClient.GetChatCompletionsAsync(options, ct);
                var content = response.Value.Choices[0].Message.Content;

                // Cache the response
                await SaveToAICacheAsync(earningsCallTranscriptSummary, _scoresSystemMessage, content);

                return content;
            }, cancellationToken);
        }

        private async Task<string> GetNaiveScoreAsync(string earningsCallTranscriptSummary, CancellationToken cancellationToken = default)
        {
            // Try cache first
            var (exists, cachedContent) = await TryGetFromAICacheAsync(earningsCallTranscriptSummary, _naiveScoreSystemMessage);
            if (exists)
            {
                return cachedContent;
            }

            return await GetOpenAIResponseWithRetryAsync(async (ct) =>
            {
                var messages = new List<ChatRequestMessage>
                {
                    new ChatRequestSystemMessage(_naiveScoreSystemMessage),
                    new ChatRequestUserMessage(earningsCallTranscriptSummary),
                };

                var options = new ChatCompletionsOptions
                {
                    MaxTokens = 4096,
                    Temperature = 0.2f,
                    NucleusSamplingFactor = 0.95f,
                    FrequencyPenalty = 0,
                    PresencePenalty = 0,
                    ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
                    DeploymentName = _settings.AzureOaiDeployment,
                };

                foreach (var message in messages)
                {
                    options.Messages.Add(message);
                }

                var response = await _openAIClient.GetChatCompletionsAsync(options, ct);
                var content = response.Value.Choices[0].Message.Content;

                // Cache the response
                await SaveToAICacheAsync(earningsCallTranscriptSummary, _naiveScoreSystemMessage, content);

                return content;
            }, cancellationToken);
        }

        private string FindPreviousDate(Dictionary<string, EarningsCall> data, string currentDate)
        {
            var currentDateTime = DateTime.Parse(currentDate);

            return data.Keys
                .Select(date => DateTime.Parse(date))
                .Where(date => date < currentDateTime)
                .OrderByDescending(date => date)
                .Select(date => date.ToString("yyyy-MM-dd"))
                .FirstOrDefault();
        }

        private Dictionary<string, List<EarningsCall>> GetMostRecentCalls(List<EarningsCall> earningCallData)
        {
            // Sort earnings call data by date
            var sortedData = earningCallData
                .OrderBy(x => DateTime.Parse((string)x.Date))
                .ToList();

            // Dictionary to store results
            var results = new Dictionary<string, List<EarningsCall>>();

            // Dictionary to track the most recent call for each symbol
            var mostRecentCalls = new Dictionary<string, EarningsCall>();

            // Iterate over sorted data
            foreach (var currentCall in sortedData)
            {
                var symbol = (string)currentCall.Symbol;
                var date = (string)currentCall.Date;

                // Update most recent call for this symbol
                mostRecentCalls[symbol] = currentCall;

                // Collect all most recent calls up to this date
                results[date] = mostRecentCalls.Values.ToList();
            }

            return results;
        }

        private void EnrichWithPercentiles(Dictionary<string, List<EarningsCall>> mostRecentCallsByDate)
        {
            foreach (var date in mostRecentCallsByDate.Keys)
            {
                var callsBlock = mostRecentCallsByDate[date];

                // Collect metrics from current block
                var aisCollection = new List<decimal>();
                var aisNaiveCollection = new List<decimal>();
                var aisDeltaCollection = new List<decimal>();
                var sueCollection = new List<decimal>();

                foreach (var call in callsBlock)
                {
                    if (call.Ais != null) aisCollection.Add(Convert.ToDecimal(call.Ais));
                    if (call.AisNaive != null) aisNaiveCollection.Add(Convert.ToDecimal(call.AisNaive));
                    if (call.AisDelta != null) aisDeltaCollection.Add(Convert.ToDecimal(call.AisDelta));
                    if (call.Sue != null) sueCollection.Add(Convert.ToDecimal(call.Sue));
                }

                foreach (var call in callsBlock)
                {
                    var ais = call.Ais != null ? Convert.ToDecimal(call.Ais) : (decimal?)null;
                    var aisNaive = call.AisNaive != null ? Convert.ToDecimal(call.AisNaive) : (decimal?)null;
                    var aisDelta = call.AisDelta != null ? Convert.ToDecimal(call.AisDelta) : (decimal?)null;
                    var sue = call.Sue != null ? Convert.ToDecimal(call.Sue) : (decimal?)null;

                    // Calculate percentiles
                    var aisPercentile = aisCollection.Any() && ais.HasValue
                        ? CalculatePercentile(aisCollection, ais.Value)
                        : (decimal?)null;
                    var aisNaivePercentile = aisNaiveCollection.Any() && aisNaive.HasValue
                        ? CalculatePercentile(aisNaiveCollection, aisNaive.Value)
                        : (decimal?)null;
                    var aisDeltaPercentile = aisDeltaCollection.Any() && aisDelta.HasValue
                        ? CalculatePercentile(aisDeltaCollection, aisDelta.Value)
                        : (decimal?)null;
                    var suePercentile = sueCollection.Any() && sue.HasValue
                        ? CalculatePercentile(sueCollection, sue.Value)
                        : (decimal?)null;

                    // Calculate final score
                    decimal? scoreFactor = null;
                    if (aisPercentile.HasValue && aisDeltaPercentile.HasValue && suePercentile.HasValue)
                    {
                        scoreFactor = (aisPercentile.Value + aisDeltaPercentile.Value + suePercentile.Value) / 3;
                    }

                    // Update the call with calculated metrics
                    call.AisPercentile = aisPercentile;
                    call.AisNaivePercentile = aisNaivePercentile;
                    call.AisDeltaPercentile = aisDeltaPercentile;
                    call.SuePercentile = suePercentile;
                    call.Score = scoreFactor;
                }
            }
        }

        private decimal CalculatePercentile(List<decimal> collection, decimal value)
        {
            // Port of scipy.stats.percentileofscore functionality
            var n = collection.Count;
            var countBelow = collection.Count(x => x < value);
            var countEqual = collection.Count(x => x == value);

            // Using 'rank' averaging method
            return (decimal)((countBelow + 0.5 * countEqual) / n);
        }

        private void CreateLatestExcel(Dictionary<string, List<EarningsCall>> earningCallDateSnapshots)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Latest Data");

            // Use different columns for latest view
            var keysToInclude = new[]
            {
                "date", "symbol", "year", "quarter",
                "estimatedEarning", "actualEarningResult", "ais", "ais_delta", "ais_naive", "sue",
                "ais%", "ais_delta%", "ais_naive%", "sue%",
                "score"
            };

            // Get latest date
            var latestDate = earningCallDateSnapshots.Keys
                .Select(date => DateTime.Parse(date))
                .Max()
                .ToString("yyyy-MM-dd");

            var latestCallGroup = earningCallDateSnapshots[latestDate];

            // Write headers
            for (int i = 0; i < keysToInclude.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = keysToInclude[i];
                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
            }

            // Write data
            int row = 2;
            foreach (var call in latestCallGroup)
            {
                // Write basic data
                for (int i = 0; i < keysToInclude.Length; i++)
                {
                    var property = typeof(EarningsCall).GetProperties()
                        .FirstOrDefault(p => p.GetCustomAttributes(typeof(JsonPropertyAttribute), false)
                            .Cast<JsonPropertyAttribute>()
                            .Any(attr => attr.PropertyName == keysToInclude[i]));

                    var value = property?.GetValue(call);
                    worksheet.Cell(row, i + 1).Value = value switch
                    {
                        null => XLCellValue.FromObject(""),
                        _ => XLCellValue.FromObject(value)
                    };
                }

                row++;
            }

            // Format worksheet
            worksheet.SheetView.FreezeRows(1);
            worksheet.RangeUsed().SetAutoFilter();

            var excelPath = Path.Combine(_outDir, "earnings-data-latest.xlsx");
            workbook.SaveAs(excelPath);
            _logger.LogInformation("Excel file successfully written to {Path}", excelPath);
        }

        private void CreateFullExcel(Dictionary<string, List<EarningsCall>> data)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Full Data");

            // Define columns to include
            var headers = new[]
            {
                "snapshot", "date", "symbol", "year", "quarter",
                "estimatedEarning", "actualEarningResult", "ais", "ais_delta", "ais_naive", "sue",
                "ais%", "ais_delta%", "ais_naive%", "sue%", "score",
                "day0_date", "day1_date", "day2_date", "day7_date", "day30_date",
                "day0_price", "day1_price", "day2_price", "day7_price", "day30_price",
                "day0_sp500_price", "day1_sp500_price", "day2_sp500_price", "day7_sp500_price", "day30_sp500_price",
                "day2_return", "day7_return", "day30_return",
                "day2_sp500_return", "day7_sp500_return", "day30_sp500_return",
            };

            // Set Headers
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
            }

            // Write data
            int rowNumber = 2;
            foreach (var date in data.Keys)
            {
                foreach (var call in data[date])
                {
                    // Add snapshot date
                    call.Snapshot = date;

                    // Write data to worksheet in order of headers
                    for (int i = 0; i < headers.Length; i++)
                    {
                        // find attribute name
                        var property = typeof(EarningsCall).GetProperties()
                            .FirstOrDefault(p => p.GetCustomAttributes(typeof(JsonPropertyAttribute), false)
                            .Cast<JsonPropertyAttribute>()
                            .Any(attr => attr.PropertyName == headers[i]));

                        // get value based on attribute name
                        var value = property?.GetValue(call);

                        // write value to worksheet
                        worksheet.Cell(rowNumber, i + 1).Value = value switch
                        {
                            null => XLCellValue.FromObject(""),
                            _ => XLCellValue.FromObject(value)
                        };
                    }

                    AddReturnFormula(worksheet, headers, rowNumber, "day1_price", "day2_price", "day2_return");
                    AddReturnFormula(worksheet, headers, rowNumber, "day1_price", "day7_price", "day7_return");
                    AddReturnFormula(worksheet, headers, rowNumber, "day1_price", "day30_price", "day30_return");
                    AddReturnFormula(worksheet, headers, rowNumber, "day1_sp500_price", "day2_sp500_price", "day2_sp500_return");
                    AddReturnFormula(worksheet, headers, rowNumber, "day1_sp500_price", "day7_sp500_price", "day7_sp500_return");
                    AddReturnFormula(worksheet, headers, rowNumber, "day1_sp500_price", "day30_sp500_price", "day30_sp500_return");

                    rowNumber++;
                }
            }

            // Format worksheet
            worksheet.SheetView.FreezeRows(1);
            worksheet.RangeUsed().SetAutoFilter();

            var excelPath = Path.Combine(_outDir, "earnings-data-history.xlsx");
            workbook.SaveAs(excelPath);
            _logger.LogInformation("Excel file successfully written to {Path}", excelPath);
        }

        private void AddReturnFormula(IXLWorksheet worksheet, string[] headers, int rowNumber, string colStartPrice, string colEndPrice, string colReturnPrice)
        {
            var colStartPriceLetter = GetColumnLetter(Array.IndexOf(headers, colStartPrice) + 1);
            var colEndPriceLetter = GetColumnLetter(Array.IndexOf(headers, colEndPrice) + 1);
            var colReturnPriceNumber = Array.IndexOf(headers, colReturnPrice) + 1;

            worksheet.Cell(rowNumber, colReturnPriceNumber).FormulaA1 =
                $"=IF(OR(ISBLANK({colEndPriceLetter}{rowNumber}),ISBLANK({colStartPriceLetter}{rowNumber})),\"\"," +
                $"({colEndPriceLetter}{rowNumber}-{colStartPriceLetter}{rowNumber})/{colStartPriceLetter}{rowNumber})";
        }

        public string GetColumnLetter(int colIndex)
        {
            if (colIndex <= 0)
                throw new ArgumentException("Column index must be positive", nameof(colIndex));

            string columnName = "";
            while (colIndex > 0)
            {
                colIndex--;
                int modulo = colIndex % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                colIndex = colIndex / 26;
            }
            return columnName;
        }


        private async Task<HttpResponseMessage> GetWithRetryAsync(string url, int maxRetries = 3, CancellationToken cancellationToken = default)
        {
            await _apiSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Try to get from cache first
                var (exists, cachedContent) = await TryGetFromCacheAsync(url);
                if (exists)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(cachedContent)
                    };
                }

                // If not in cache, make the actual request
                for (int i = 0; i < maxRetries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), cancellationToken);
                            continue;
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            // Cache the successful response
                            var content = await response.Content.ReadAsStringAsync(cancellationToken);
                            await SaveToCacheAsync(url, content);
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent(content)
                            };
                        }

                        return response;
                    }
                    catch (HttpRequestException ex)
                    {
                        if (i == maxRetries - 1) throw;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), cancellationToken);
                    }
                }
                throw new Exception("Max retries exceeded");
            }
            finally
            {
                _apiSemaphore.Release();
            }
        }

        private string GetCacheKey(string url)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
            return Convert.ToBase64String(hash).Replace('/', '_').Replace('+', '-').TrimEnd('=');
        }

        private string GetCachePath(string url)
        {
            return Path.Combine(_cacheDir, GetCacheKey(url) + ".json");
        }

        private async Task<(bool exists, string content)> TryGetFromCacheAsync(string url)
        {
            if (!_settings.UseCache)
                return (false, null);

            var cachePath = GetCachePath(url);
            if (File.Exists(cachePath))
            {
                var content = await File.ReadAllTextAsync(cachePath);
                return (true, content);
            }
            return (false, null);
        }

        private async Task SaveToCacheAsync(string url, string content)
        {
            if (!_settings.UseCache)
                return;

            var cachePath = GetCachePath(url);
            await File.WriteAllTextAsync(cachePath, content);
        }

        private string GetAICacheKey(string prompt, string systemMessage)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var input = systemMessage + "\n---\n" + prompt;
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash).Replace('/', '_').Replace('+', '-').TrimEnd('=');
        }

        private string GetAICachePath(string prompt, string systemMessage)
        {
            return Path.Combine(_aiCacheDir, GetAICacheKey(prompt, systemMessage) + ".json");
        }

        private async Task<(bool exists, string content)> TryGetFromAICacheAsync(string prompt, string systemMessage)
        {
            if (!_settings.UseCache)
                return (false, null);

            var cachePath = GetAICachePath(prompt, systemMessage);
            if (File.Exists(cachePath))
            {
                var content = await File.ReadAllTextAsync(cachePath);
                return (true, content);
            }
            return (false, null);
        }

        private async Task SaveToAICacheAsync(string prompt, string systemMessage, string content)
        {
            if (!_settings.UseCache)
                return;

            var cachePath = GetAICachePath(prompt, systemMessage);
            await File.WriteAllTextAsync(cachePath, content);
        }

        private async Task<string> GetOpenAIResponseWithRetryAsync(Func<CancellationToken, Task<string>> apiCall, CancellationToken cancellationToken)
        {
            int maxRetries = 5;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await _openAiRateLimiter.WaitForTokenAsync(cancellationToken);
                    await _openAiSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await apiCall(cancellationToken);
                    }
                    finally
                    {
                        _openAiSemaphore.Release();
                    }
                }
                catch (RequestFailedException ex) when (ex.Status == 429)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, i) + Random.Shared.NextDouble());
                    _logger.LogWarning("Rate limited by OpenAI. Retrying after {Delay:F1}s", delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            throw new Exception("Max retries exceeded for OpenAI API call");
        }

        private async Task<List<string>> GetSp500ConstituentsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Fetching S&P 500 constituents for period {StartDate} to {EndDate}", startDate, endDate);

            var constituents = new HashSet<string>();

            // Get current constituents as baseline
            var url = $"https://financialmodelingprep.com/api/v3/sp500_constituent?apikey={_settings.FinancialModelingPrepKey}";
            var response = await GetWithRetryAsync(url, 3, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching current S&P 500 constituents: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var currentData = JArray.Parse(responseContent);

            foreach (var item in currentData)
            {
                var symbol = item["symbol"]?.Value<string>();
                var dateFirstAddedStr = item["dateFirstAdded"]?.Value<string>();

                if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(dateFirstAddedStr))
                {
                    _logger.LogWarning("Skipping current constituent with missing data: {Data}", item.ToString());
                    continue;
                }

                var dateFirstAdded = DateTime.Parse(dateFirstAddedStr);
                if (dateFirstAdded <= endDate)
                {
                    constituents.Add(symbol);
                }
            }

            // Get historical constituents - apply all historical changes to the current constituents	
            url = $"https://financialmodelingprep.com/api/v3/historical/sp500_constituent?apikey={_settings.FinancialModelingPrepKey}";
            response = await GetWithRetryAsync(url, 3, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching S&P 500 constituents: {response.StatusCode}");
            }

            responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var historicalData = JArray.Parse(responseContent);

            foreach (var item in historicalData)
            {
                var addedSymbol = item["symbol"]?.Value<string>();
                var removedSymbol = item["removedTicker"]?.Value<string>();
                var dateStr = item["date"]?.Value<string>();

                if (string.IsNullOrEmpty(dateStr))
                {
                    _logger.LogWarning("Skipping invalid historical constituent data: {Data}", item.ToString());
                    continue;
                }

                var changeDate = DateTime.Parse(dateStr);

                // Check if the change occurred during our period
                if (changeDate >= startDate && changeDate <= endDate)
                {
                    if (!string.IsNullOrEmpty(addedSymbol))
                    {
                        constituents.Add(addedSymbol);
                    }
                    if (!string.IsNullOrEmpty(removedSymbol))
                    {
                        constituents.Remove(removedSymbol);
                    }
                }
            }

            var tickerList = constituents.ToList();
            _logger.LogInformation("Found {Count} unique S&P 500 constituents for the period", tickerList.Count);

            return tickerList;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    public class FilteredOrderedContractResolver : DefaultContractResolver
    {
        private readonly IList<string> _propertyOrder;
        private readonly HashSet<string> _propertiesToInclude;

        public FilteredOrderedContractResolver(IList<string> propertyOrder, IEnumerable<string> propertiesToInclude)
        {
            _propertyOrder = propertyOrder;
            _propertiesToInclude = new HashSet<string>(propertiesToInclude);
            NamingStrategy = new DefaultNamingStrategy();
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);

            return properties
                .Where(p => _propertiesToInclude.Contains(p.PropertyName))
                .OrderBy(p =>
                {
                    var index = _propertyOrder.IndexOf(p.PropertyName);
                    return index == -1 ? int.MaxValue : index;
                })
                .ToList();
        }
    }

    /**
    For Azure OpenAI API with GPT-3.5 Turbo with limits of:
        * 180 Requests Per Minute (RPM) = 3 requests per second
        * 30,000 Tokens Per Minute (TPM) = 500 tokens per second

    Here's how we should configure the TokenBucketRateLimiter:

    private readonly TokenBucketRateLimiter _openAiRateLimiter = new(3, 10); // 3 tokens/sec (180 RPM), burst of 10

    The configuration:

    1. tokensPerSecond = 3
        * 180 RPM ÷ 60 seconds = 3 requests per second
        * This ensures we stay under the 180 RPM limit

    2. maxTokens = 10
        * Allows for small bursts of requests (up to 10)
        * But ensures we quickly return to the 3 RPS rate
        * 10 is a good balance between burst capacity and safety margin

    We don't need to implement token counting for the 30K TPM limit because:
        * 500 tokens per second is much higher than our 3 RPS limit
        * Each request typically uses 2-4K tokens
        * At 3 RPS with 4K tokens per request, we'd use about 12K tokens per second
        * This is well below the 30K TPM (500 tokens/sec) limit

    Therefore, the RPM limit (180) is our primary constraint, and configuring for 3 tokens per second will keep us safely within both limits.
    **/
    public class TokenBucketRateLimiter
    {
        private readonly double _tokensPerSecond;
        private readonly double _maxTokens;
        private double _currentTokens;
        private DateTime _lastRefillTime;
        private readonly object _lock = new object();

        public TokenBucketRateLimiter(double tokensPerSecond, double maxTokens)
        {
            _tokensPerSecond = tokensPerSecond;
            _maxTokens = maxTokens;
            _currentTokens = maxTokens;
            _lastRefillTime = DateTime.UtcNow;
        }

        public async Task WaitForTokenAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    var timePassed = (now - _lastRefillTime).TotalSeconds;
                    _currentTokens = Math.Min(_maxTokens, _currentTokens + timePassed * _tokensPerSecond);
                    _lastRefillTime = now;

                    if (_currentTokens >= 1)
                    {
                        _currentTokens -= 1;
                        return;
                    }

                    // Calculate wait time needed
                    var waitTime = (1 - _currentTokens) / _tokensPerSecond;
                    Task.Delay(TimeSpan.FromSeconds(waitTime), cancellationToken).Wait(cancellationToken);
                }
            }
        }
    }
}