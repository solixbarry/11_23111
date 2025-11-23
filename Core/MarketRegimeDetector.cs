using System;
using System.Collections.Generic;
using System.Linq;

namespace NYCAlphaTrader.Core
{
    public enum MarketRegime
    {
        Ranging,        // Good for mean reversion
        Uptrend,        // Only long positions
        Downtrend,      // Mean reversion DISABLED
        HighVolatility  // Reduce size
    }

    public class MarketRegimeDetector
    {
        private Queue<double> _priceHistory;
        private MarketRegime _currentRegime = MarketRegime.Ranging;
        
        public MarketRegimeDetector(int ema20Period = 20, int ema50Period = 50)
        {
            _priceHistory = new Queue<double>(300);  // 5 hours at 1-min bars
        }
        
        public MarketRegime DetectRegime(MarketData marketData)
        {
            _priceHistory.Enqueue(marketData.LastPrice);
            if (_priceHistory.Count > 300)
                _priceHistory.Dequeue();
            
            if (_priceHistory.Count < 60)
                return MarketRegime.Ranging;  // Default until enough data
            
            var prices = _priceHistory.ToArray();
            double current = prices[prices.Length - 1];
            
            // Calculate EMAs
            double ema20 = CalculateEMA(prices, 20);
            double ema50 = CalculateEMA(prices, 50);
            
            // Calculate trend strength (4-hour % change)
            int lookback = Math.Min(240, prices.Length - 1);
            double price4hAgo = prices[prices.Length - lookback - 1];
            double trendStrength = Math.Abs((current - price4hAgo) / price4hAgo);
            
            // Calculate volatility
            double volatility = CalculateVolatility(prices, 60);
            double avgVolatility = CalculateVolatility(prices, 240);
            
            // Check for high volatility first
            if (volatility > avgVolatility * 1.5)
            {
                _currentRegime = MarketRegime.HighVolatility;
                return _currentRegime;
            }
            
            // Check for strong trend (>1.5% move in 4 hours)
            if (trendStrength > 0.015)
            {
                // Determine direction using EMA alignment
                if (current > ema20 && ema20 > ema50)
                {
                    _currentRegime = MarketRegime.Uptrend;
                    return _currentRegime;
                }
                else if (current < ema20 && ema20 < ema50)
                {
                    _currentRegime = MarketRegime.Downtrend;  // ← BLOCKS MEAN REVERSION
                    return _currentRegime;
                }
            }
            
            // No strong trend = ranging
            _currentRegime = MarketRegime.Ranging;
            return _currentRegime;
        }
        
        private double CalculateEMA(double[] prices, int period)
        {
            if (prices.Length < period)
                return prices.Average();
            
            double multiplier = 2.0 / (period + 1);
            double ema = prices.Take(period).Average();
            
            for (int i = period; i < prices.Length; i++)
            {
                ema = (prices[i] * multiplier) + (ema * (1 - multiplier));
            }
            
            return ema;
        }
        
        private double CalculateVolatility(double[] prices, int lookback)
        {
            int start = Math.Max(0, prices.Length - lookback);
            var recentPrices = prices.Skip(start).ToArray();
            
            if (recentPrices.Length < 2)
                return 0;
            
            double mean = recentPrices.Average();
            double sumSquares = recentPrices.Sum(p => Math.Pow(p - mean, 2));
            return Math.Sqrt(sumSquares / recentPrices.Length) / mean;
        }
        
        public string GetRegimeString()
        {
            return _currentRegime.ToString();
        }
    }
}