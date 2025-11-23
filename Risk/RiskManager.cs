using System;
using System.Linq;

namespace TradingSystem.Risk
{
    /// <summary>
    /// Balance-aware risk management and position sizing
    /// Prevents account blow-up by sizing positions based on available capital
    /// </summary>
    public class RiskManager
    {
        private readonly RiskConfig _config;

        public RiskManager(RiskConfig config)
        {
            _config = config;

            Console.WriteLine($"[RiskManager] Initialized");
            Console.WriteLine($"[RiskManager] Max Position: {_config.MaxPositionPercent * 100:F1}%");
            Console.WriteLine($"[RiskManager] Max Exposure: {_config.MaxTotalExposure * 100:F1}%");
            Console.WriteLine($"[RiskManager] Min Balance: ${_config.MinBalanceThreshold:F2}");
            Console.WriteLine($"[RiskManager] Max Open Positions: {_config.MaxOpenPositions}");
        }

        /// <summary>
        /// Calculate position size based on available balance and risk parameters
        /// </summary>
        public double CalculatePositionSize(
            double availableBalanceUSDT,
            double currentPrice,
            double signalConfidence,
            int currentOpenPositions)
        {
            // 1. Validate inputs
            if (availableBalanceUSDT <= 0)
            {
                Console.WriteLine($"[RISK] Invalid balance: ${availableBalanceUSDT:F2}");
                return 0;
            }

            if (currentPrice <= 0)
            {
                Console.WriteLine($"[RISK] Invalid price: ${currentPrice:F2}");
                return 0;
            }

            if (signalConfidence < 0 || signalConfidence > 1)
            {
                Console.WriteLine($"[RISK] Invalid confidence: {signalConfidence:F2}");
                return 0;
            }

            // 2. Check minimum balance threshold
            if (availableBalanceUSDT < _config.MinBalanceThreshold)
            {
                Console.WriteLine($"[RISK] Balance ${availableBalanceUSDT:F2} below threshold ${_config.MinBalanceThreshold:F2} - BLOCKING TRADE");
                return 0;
            }

            // 3. Check max open positions
            if (currentOpenPositions >= _config.MaxOpenPositions)
            {
                Console.WriteLine($"[RISK] Max positions reached ({currentOpenPositions}/{_config.MaxOpenPositions}) - BLOCKING TRADE");
                return 0;
            }

            // 4. Calculate base position size (% of available capital)
            double basePositionUSD = availableBalanceUSDT * _config.MaxPositionPercent;

            // 5. Adjust for signal confidence (scale between 50% and 100% of base)
            // Confidence of 0.5 = 50% of base position
            // Confidence of 1.0 = 100% of base position
            double confidenceMultiplier = 0.5 + (signalConfidence * 0.5);
            double adjustedPositionUSD = basePositionUSD * confidenceMultiplier;

            // 6. Check total exposure limit
            double maxExposureUSD = availableBalanceUSDT * _config.MaxTotalExposure;
            if (adjustedPositionUSD > maxExposureUSD)
            {
                Console.WriteLine($"[RISK] Position ${adjustedPositionUSD:F2} exceeds max exposure ${maxExposureUSD:F2} - CAPPING");
                adjustedPositionUSD = maxExposureUSD;
            }

            // 7. Convert USD to BTC (or whatever base currency)
            double positionSizeBTC = adjustedPositionUSD / currentPrice;

            // 8. Apply exchange minimum (Binance minimum is 0.00001 BTC)
            double exchangeMinimum = 0.00001;
            if (positionSizeBTC < exchangeMinimum)
            {
                Console.WriteLine($"[RISK] Position {positionSizeBTC:F8} BTC below exchange minimum {exchangeMinimum:F8} - BLOCKING TRADE");
                return 0;
            }

            // 9. Round to exchange precision (6 decimals for BTC)
            positionSizeBTC = Math.Round(positionSizeBTC, 6);

            // 10. Log final position
            double finalPositionUSD = positionSizeBTC * currentPrice;
            double percentOfBalance = (finalPositionUSD / availableBalanceUSDT) * 100;

            Console.WriteLine($"[RISK] Balance: ${availableBalanceUSDT:F2} | Position: {positionSizeBTC:F6} BTC (${finalPositionUSD:F2} = {percentOfBalance:F1}%) | Confidence: {signalConfidence:F2}");

            return positionSizeBTC;
        }

        /// <summary>
        /// Check if we can place a new trade
        /// </summary>
        public bool CanTrade(double availableBalanceUSDT, int currentOpenPositions, out string reason)
        {
            // Check balance
            if (availableBalanceUSDT < _config.MinBalanceThreshold)
            {
                reason = $"Balance ${availableBalanceUSDT:F2} below threshold ${_config.MinBalanceThreshold:F2}";
                return false;
            }

            // Check max positions
            if (currentOpenPositions >= _config.MaxOpenPositions)
            {
                reason = $"Max positions reached ({currentOpenPositions}/{_config.MaxOpenPositions})";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// Calculate daily loss limit in USDT
        /// </summary>
        public double GetDailyLossLimit(double startingBalance)
        {
            return startingBalance * _config.MaxDailyLossPercent;
        }

        /// <summary>
        /// Check if daily loss limit exceeded
        /// </summary>
        public bool IsDailyLossLimitExceeded(double startingBalance, double currentBalance, out double lossAmount)
        {
            lossAmount = startingBalance - currentBalance;
            double lossLimit = GetDailyLossLimit(startingBalance);

            if (lossAmount >= lossLimit)
            {
                Console.WriteLine($"[RISK] DAILY LOSS LIMIT EXCEEDED: -${lossAmount:F2} >= ${lossLimit:F2}");
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Risk configuration per environment
    /// </summary>
    public class RiskConfig
    {
        // Position sizing
        public double MaxPositionPercent { get; set; }     // Max % of capital per trade
        public double MaxTotalExposure { get; set; }        // Max % of capital deployed
        public double MinBalanceThreshold { get; set; }     // Stop trading below this

        // Position limits
        public int MaxOpenPositions { get; set; }           // Max concurrent positions

        // Loss limits
        public double MaxDailyLossPercent { get; set; }     // Max daily loss as % of starting balance

        /// <summary>
        /// Get risk configuration for specific environment
        /// </summary>
        public static RiskConfig GetConfig(TradingEnvironment environment)
        {
            switch (environment)
            {
                case TradingEnvironment.Testnet:
                    return new RiskConfig
                    {
                        MaxPositionPercent = 0.15,      // 15% per trade (aggressive for testing)
                        MaxTotalExposure = 0.90,        // 90% max deployed
                        MinBalanceThreshold = 10,       // $10 minimum
                        MaxOpenPositions = 3,           // 3 concurrent
                        MaxDailyLossPercent = 0.20      // 20% daily loss limit
                    };

                case TradingEnvironment.Production:
                    return new RiskConfig
                    {
                        MaxPositionPercent = 0.08,      // 8% per trade (conservative)
                        MaxTotalExposure = 0.70,        // 70% max deployed
                        MinBalanceThreshold = 500,      // $500 reserve
                        MaxOpenPositions = 5,           // 5 concurrent
                        MaxDailyLossPercent = 0.02      // 2% daily loss limit
                    };

                case TradingEnvironment.Backtesting:
                    return new RiskConfig
                    {
                        MaxPositionPercent = 0.10,
                        MaxTotalExposure = 0.80,
                        MinBalanceThreshold = 100,
                        MaxOpenPositions = 4,
                        MaxDailyLossPercent = 0.10
                    };

                default:
                    throw new ArgumentException($"Unknown environment: {environment}");
            }
        }
    }

    public enum TradingEnvironment
    {
        Testnet,
        Production,
        Backtesting
    }
}
