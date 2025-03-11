using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialAnalysis.Model
{

    public class EarningsCall
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("datetime")]
        public string DateTime { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("year")]
        public int Year { get; set; }

        [JsonProperty("quarter")]
        public int Quarter { get; set; }

        [JsonProperty("actualEarningResult")]
        public decimal? ActualEarningResult { get; set; }

        [JsonProperty("estimatedEarning")]
        public decimal? EstimatedEarning { get; set; }

        [JsonProperty("historical_eps")]
        public List<decimal> HistoricalEps { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("ais")]
        public decimal? Ais { get; set; }

        [JsonProperty("ais_naive")]
        public decimal? AisNaive { get; set; }

        [JsonProperty("ais_delta")]
        public decimal? AisDelta { get; set; }

        [JsonProperty("sue")]
        public decimal? Sue { get; set; }

        [JsonProperty("ais%")]
        public decimal? AisPercentile { get; set; }

        [JsonProperty("ais_naive%")]
        public decimal? AisNaivePercentile { get; set; }

        [JsonProperty("ais_delta%")]
        public decimal? AisDeltaPercentile { get; set; }

        [JsonProperty("sue%")]
        public decimal? SuePercentile { get; set; }

        [JsonProperty("score")]
        public decimal? Score { get; set; }

        [JsonProperty("ais_scoring_details")]
        public JObject AisScoringDetails { get; set; }

        [JsonProperty("ais_naive_scoring_details")]
        public JObject AisNaiveScoringDetails { get; set; }

        [JsonProperty("transcript_summary")]
        public string TranscriptSummary { get; set; }

        [JsonProperty("prompt_summarize")]
        public string PromptSummarize { get; set; }

        [JsonProperty("promt_score")]
        public string PromtScore { get; set; }

        [JsonProperty("execution_time")]
        public string ExecutionTime { get; set; }

        [JsonProperty("snapshot")]
        public string Snapshot { get; set; }

        [JsonProperty("day0_date")]
        public string Day0Date { get; set; }

        [JsonProperty("day1_date")]
        public string Day1Date { get; set; }

        [JsonProperty("day2_date")]
        public string Day2Date { get; set; }

        [JsonProperty("day7_date")]
        public string Day7Date { get; set; }

        [JsonProperty("day30_date")]
        public string Day30Date { get; set; }

        [JsonProperty("day0_price")]
        public decimal? Day0Price { get; set; }

        [JsonProperty("day1_price")]
        public decimal? Day1Price { get; set; }

        [JsonProperty("day2_price")]
        public decimal? Day2Price { get; set; }

        [JsonProperty("day7_price")]
        public decimal? Day7Price { get; set; }

        [JsonProperty("day30_price")]
        public decimal? Day30Price { get; set; }

        [JsonProperty("day0_sp500_price")]
        public decimal? Day0Sp500Price { get; set; }

        [JsonProperty("day1_sp500_price")]
        public decimal? Day1Sp500Price { get; set; }

        [JsonProperty("day2_sp500_price")]
        public decimal? Day2Sp500Price { get; set; }

        [JsonProperty("day7_sp500_price")]
        public decimal? Day7Sp500Price { get; set; }

        [JsonProperty("day30_sp500_price")]
        public decimal? Day30Sp500Price { get; set; }

        // "day2_return", "day7_return", "day30_return",
        // "day2_sp500_return", "day7_sp500_return", "day30_sp500_return",

        [JsonProperty("day2_return")]
        public decimal? Day2Return { get; set; }

        [JsonProperty("day7_return")]
        public decimal? Day7Return { get; set; }

        [JsonProperty("day30_return")]
        public decimal? Day30Return { get; set; }

        [JsonProperty("day2_sp500_return")]
        public decimal? Day2Sp500Return { get; set; }

        [JsonProperty("day7_sp500_return")]
        public decimal? Day7Sp500Return { get; set; }

        [JsonProperty("day30_sp500_return")]
        public decimal? Day30Sp500Return { get; set; }

    }

}
