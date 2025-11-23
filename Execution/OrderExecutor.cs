using System;
using System.Linq;
using TradingSystem.Risk;
using TradingSystem.Logging;

namespace TradingSystem.Execution
{
    /// <summary>
    /// Order execution with integrated risk management
    /// This replaces your hard-coded position sizing
    /// </summary>
    public class OrderExecutor
    {
        private readonly RiskManager _riskManager;
        private readonly IBinanceClient _binanceClient;
        private readonly LogManager _logManager;

        // Track open positions
        private int _openPositionsCount = 0;

        // Daily P&L tracking
        private double _startingDailyBalance;
        private DateTime _dailyResetTime;

        public OrderExecutor(RiskManager riskManager, IBinanceClient binanceClient, LogManager logManager)
        {
            _riskManager = riskManager;
            _binanceClient = binanceClient;
            _logManager = logManager;

            // Initialize daily tracking
            _dailyResetTime = DateTime.UtcNow.Date;
            _startingDailyBalance = GetAvailableBalance();

            Console.WriteLine($"[OrderExecutor] Initialized");
            Console.WriteLine($"[OrderExecutor] Starting balance: ${_startingDailyBalance:F2}");
        }

        /// <summary>
        /// Execute order with risk management
        /// THIS IS WHAT YOU CALL FROM YOUR TRADING LOOP
        /// </summary>
        public bool ExecuteSignal(SignalResult signal, string strategyName, string symbol = "BTCUSDT")
        {
            try
            {
                // 1. Check daily reset
                CheckDailyReset();

                // 2. Get current balance
                double availableBalance = GetAvailableBalance();

                // 3. Check daily loss limit
                if (_riskManager.IsDailyLossLimitExceeded(_startingDailyBalance, availableBalance, out double lossAmount))
                {
                    _logManager.Log($"[{strategyName}] DAILY LOSS LIMIT EXCEEDED: -${lossAmount:F2} - HALTING TRADING");
                    return false;
                }

                // 4. Check if we can trade
                if (!_riskManager.CanTrade(availableBalance, _openPositionsCount, out string reason))
                {
                    _logManager.Log($"[{strategyName}] Cannot trade: {reason}");
                    return false;
                }

                // 5. Get current price
                double currentPrice = GetCurrentPrice(symbol);
                if (currentPrice <= 0)
                {
                    _logManager.Log($"[{strategyName}] Invalid price: ${currentPrice:F2}");
                    return false;
                }

                // 6. Calculate position size with risk management
                double positionSize = _riskManager.CalculatePositionSize(
                    availableBalance,
                    currentPrice,
                    signal.Confidence,
                    _openPositionsCount
                );

                if (positionSize <= 0)
                {
                    _logManager.Log($"[{strategyName}] Risk manager blocked trade (position size: {positionSize:F6})");
                    return false;
                }

                // 7. Determine order side
                OrderSide side = signal.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;

                // 8. Place order
                _logManager.Log($"[{strategyName}] Placing {side} order: {positionSize:F6} BTC @ ${currentPrice:F2} (${positionSize * currentPrice:F2})");

                var order = _binanceClient.PlaceOrder(
                    symbol: symbol,
                    side: side,
                    type: OrderType.Market,
                    quantity: positionSize
                );

                // 9. Log successful execution
                _openPositionsCount++;
                _logManager.LogTrade(strategyName, side.ToString(), positionSize, currentPrice, order.OrderId.ToString());
                _logManager.Log($"[{strategyName}] - Order filled: ID={order.OrderId}, Executed={order.ExecutedQty:F6} BTC, Avg Price=${order.AvgPrice:F2}");

                return true;
            }
            catch (Exception ex)
            {
                _logManager.Log($"[{strategyName}] ✗ Order failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get available USDT balance from exchange
        /// </summary>
        private double GetAvailableBalance()
        {
            try
            {
                var accountInfo = _binanceClient.GetAccountInfo();
                var usdtBalance = accountInfo.Balances.FirstOrDefault(b => b.Asset == "USDT");

                if (usdtBalance == null)
                {
                    Console.WriteLine($"[OrderExecutor] WARNING: No USDT balance found");
                    return 0;
                }

                // Return available (free) balance
                return (double)usdtBalance.Free;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OrderExecutor] Error getting balance: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Get current price for symbol
        /// </summary>
        private double GetCurrentPrice(string symbol)
        {
            try
            {
                var ticker = _binanceClient.GetPrice(symbol);
                return (double)ticker.Price;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OrderExecutor] Error getting price for {symbol}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Check if we need to reset daily loss tracking
        /// </summary>
        private void CheckDailyReset()
        {
            DateTime now = DateTime.UtcNow;
            if (now.Date > _dailyResetTime)
            {
                // New day - reset tracking
                _dailyResetTime = now.Date;
                _startingDailyBalance = GetAvailableBalance();

                _logManager.Log($"[OrderExecutor] ═══ NEW TRADING DAY ═══");
                _logManager.Log($"[OrderExecutor] Starting balance: ${_startingDailyBalance:F2}");
            }
        }

        /// <summary>
        /// Update open positions count (call after closing a position)
        /// </summary>
        public void OnPositionClosed()
        {
            if (_openPositionsCount > 0)
                _openPositionsCount--;
        }

        /// <summary>
        /// Get current status
        /// </summary>
        public ExecutorStatus GetStatus()
        {
            double currentBalance = GetAvailableBalance();
            double dailyPnL = currentBalance - _startingDailyBalance;
            double dailyPnLPercent = _startingDailyBalance > 0 ? (dailyPnL / _startingDailyBalance) * 100 : 0;

            return new ExecutorStatus
            {
                StartingBalance = _startingDailyBalance,
                CurrentBalance = currentBalance,
                DailyPnL = dailyPnL,
                DailyPnLPercent = dailyPnLPercent,
                OpenPositions = _openPositionsCount
            };
        }
    }

    /// <summary>
    /// Executor status for monitoring
    /// </summary>
    public class ExecutorStatus
    {
        public double StartingBalance { get; set; }
        public double CurrentBalance { get; set; }
        public double DailyPnL { get; set; }
        public double DailyPnLPercent { get; set; }
        public int OpenPositions { get; set; }

        public override string ToString()
        {
            string pnlSign = DailyPnL >= 0 ? "+" : "";
            return $"Balance: ${CurrentBalance:F2} | Daily P&L: {pnlSign}${DailyPnL:F2} ({pnlSign}{DailyPnLPercent:F2}%) | Open: {OpenPositions}";
        }
    }

    // Supporting types (simplified - adapt to your actual Binance client)
    public interface IBinanceClient
    {
        AccountInfo GetAccountInfo();
        TickerPrice GetPrice(string symbol);
        Order PlaceOrder(string symbol, OrderSide side, OrderType type, double quantity);
    }

    public class AccountInfo
    {
        public Balance[] Balances { get; set; }
    }

    public class Balance
    {
        public string Asset { get; set; }
        public decimal Free { get; set; }
        public decimal Locked { get; set; }
    }

    public class TickerPrice
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
    }

    public class Order
    {
        public long OrderId { get; set; }
        public decimal ExecutedQty { get; set; }
        public decimal AvgPrice { get; set; }
    }

    public enum OrderSide { Buy, Sell }
    public enum OrderType { Market, Limit }

    // From your existing code
    public class SignalResult
    {
        public bool HasSignal { get; set; }
        public TradeDirection Direction { get; set; }
        public double Confidence { get; set; }
    }

    public enum TradeDirection { Long, Short, None }
}