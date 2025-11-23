using System;
using System.Collections.Generic;

namespace NYCAlphaTrader.Core
{
    public enum Side
    {
        Buy,
        Sell
    }

    public enum OrderType
    {
        Limit,
        Market,
        LimitMaker
    }

    public class TradingConfig
    {
        public double Capital { get; set; } = 47000;
        public double MaxDailyLoss { get; set; } = 2350;
        public double MaxPositionSize { get; set; } = 5000;
        public bool TradingEnabled { get; set; } = true;
        
        // Strategy toggles
        public bool EnableOffHoursTrading { get; set; } = true;
        public bool EnableMeanReversion { get; set; } = true;
        public bool EnableLiquidationWicks { get; set; } = true;
        public bool EnableOBI { get; set; } = true;
        public bool EnableRegimeTrading { get; set; } = true;
        
        // Off-hours config
        public TimeSpan OffHoursStart { get; set; } = new TimeSpan(23, 0, 0); // 11pm EST
        public TimeSpan OffHoursEnd { get; set; } = new TimeSpan(5, 0, 0);    // 5am EST
        
        // OBI config
        public int ObiNumLevels { get; set; } = 5;
        public double ObiThreshold { get; set; } = 0.65;
        public double ObiSurvivalSeconds { get; set; } = 1.8;
        public int ObiMinEvents { get; set; } = 3;
        
        // Mean reversion config
        public double MrVwapDeviation { get; set; } = 2.0; // Standard deviations
        public double MrVolumeMultiplier { get; set; } = 1.5;
        public double MrTargetBps { get; set; } = 8.0;
        public double MrStopBps { get; set; } = 4.0;
        
        // Liquidation wick config
        public double LiqVolumeSpike { get; set; } = 2.0;
        public double LiqWickSizePercent { get; set; } = 0.45; // 0.35-0.55%
        public double LiqObiConfirmation { get; set; } = 0.5;
        
        // Risk config
        public double MaxSpreadBps { get; set; } = 1.5;
        public double TrailingStopPercent { get; set; } = 0.5;
    }

    public class MarketData
    {
        public string Symbol { get; set; }
        public DateTime Timestamp { get; set; }
        public double BestBid { get; set; }
        public double BestAsk { get; set; }
        public double BidVolume { get; set; }
        public double AskVolume { get; set; }
        public double LastPrice { get; set; }
        public double Volume24h { get; set; }
        
        public double MidPrice => (BestBid + BestAsk) / 2.0;
        public double SpreadBps => ((BestAsk - BestBid) / MidPrice) * 10000.0;
        
        // Order book levels (top 5)
        public List<Level> BidLevels { get; set; } = new List<Level>();
        public List<Level> AskLevels { get; set; } = new List<Level>();
    }

    public class Level
    {
        public double Price { get; set; }
        public double Quantity { get; set; }
    }

    public class TradingSignal
    {
        public string Symbol { get; set; }
        public string Strategy { get; set; }
        public Side Side { get; set; }
        public OrderType Type { get; set; } = OrderType.Limit;
        public double Price { get; set; }
        public double Quantity { get; set; }
        public double TargetPrice { get; set; }
        public double StopPrice { get; set; }
        public double Confidence { get; set; } // 0-1
        public DateTime GeneratedAt { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        
        public bool IsValid => Quantity > 0 && Price > 0;
    }

    public class Position
    {
        public string Symbol { get; set; }
        public double Quantity { get; set; } // Positive = long, negative = short
        public double AvgPrice { get; set; }
        public double RealizedPnL { get; set; }
        public double UnrealizedPnL { get; set; }
        public DateTime OpenedAt { get; set; }
        
        public bool IsFlat => Math.Abs(Quantity) < 0.0001;
        public bool IsLong => Quantity > 0.0001;
        public bool IsShort => Quantity < -0.0001;
        
        public double NotionalValue(double currentPrice) => Math.Abs(Quantity * currentPrice);
        
        public void UpdateUnrealized(double currentPrice)
        {
            if (IsFlat) 
            {
                UnrealizedPnL = 0;
                return;
            }
            UnrealizedPnL = Quantity * (currentPrice - AvgPrice);
        }
    }

    public class Fill
    {
        public string OrderId { get; set; }
        public string Symbol { get; set; }
        public Side Side { get; set; }
        public double Price { get; set; }
        public double Quantity { get; set; }
        public double Fee { get; set; }
        public bool IsMaker { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class StrategyStats
    {
        public int TotalSignals { get; set; }
        public int TradesExecuted { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double TotalPnL { get; set; }
        private double? _winRate;
        public double WinRate
        {
            get => _winRate ?? (TradesExecuted > 0 ? (double)WinningTrades / TradesExecuted : 0);
            set => _winRate = value;
        }
        public double SharpeRatio { get; set; }
        public Dictionary<string, double> StrategyPnL { get; set; } = new Dictionary<string, double>();
    }

    public class RiskStats
    {
        public double DailyPnL { get; set; }
        public double PeakPnL { get; set; }
        public double MaxDrawdown { get; set; }
        public double GrossExposure { get; set; }
        public double NetExposure { get; set; }
        public int ActivePositions { get; set; }
    }
}
