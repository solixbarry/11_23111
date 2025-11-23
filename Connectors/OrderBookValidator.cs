using System;
using NYCAlphaTrader.Core;

namespace NYCAlphaTrader.Connectors
{
    /// <summary>
    /// Filter bad order book data from exchange
    /// Critical for HFT - one bad tick can blow up position sizing
    /// </summary>
    public class OrderBookValidator
    {
        private readonly double _maxSpreadPercent;
        private readonly double _minPrice;
        private readonly double _maxPrice;
        private readonly double _maxPriceJumpPercent;
        
        private double _lastValidMidPrice = 0;
        
        // Statistics for monitoring
        public int TotalUpdates { get; private set; }
        public int RejectedUpdates { get; private set; }
        public double RejectionRate => TotalUpdates > 0 ? (double)RejectedUpdates / TotalUpdates : 0;
        
        public OrderBookValidator(
            double maxSpreadPercent = 5.0,      // Reject spreads > 5%
            double minPrice = 10000,            // Reject BTC < $10k
            double maxPrice = 200000,           // Reject BTC > $200k
            double maxPriceJumpPercent = 2.0)   // Reject jumps > 2%
        {
            _maxSpreadPercent = maxSpreadPercent;
            _minPrice = minPrice;
            _maxPrice = maxPrice;
            _maxPriceJumpPercent = maxPriceJumpPercent;
            
            Console.WriteLine($"[OrderBookValidator] Initialized");
            Console.WriteLine($"[OrderBookValidator] Max Spread: {_maxSpreadPercent}%");
            Console.WriteLine($"[OrderBookValidator] Price Range: ${_minPrice:F0} - ${_maxPrice:F0}");
            Console.WriteLine($"[OrderBookValidator] Max Price Jump: {_maxPriceJumpPercent}%");
        }
        
        /// <summary>
        /// Validate order book update and return true if valid
        /// </summary>
        public bool ValidateOrderBook(MarketData orderBook, out string rejectReason)
        {
            TotalUpdates++;
            
            // 1. Check for null or empty
            if (orderBook == null)
            {
                rejectReason = "Null order book";
                RejectedUpdates++;
                return false;
            }
            
            double bestBid = orderBook.BestBid;
            double bestAsk = orderBook.BestAsk;
            
            // 2. Check for zero or negative prices
            if (bestBid <= 0 || bestAsk <= 0)
            {
                rejectReason = $"Invalid prices: Bid={bestBid:F2}, Ask={bestAsk:F2}";
                RejectedUpdates++;
                return false;
            }
            
            // 3. Check price range (sanity check)
            if (bestBid < _minPrice || bestBid > _maxPrice)
            {
                rejectReason = $"Bid ${bestBid:F2} outside valid range [${_minPrice:F0}, ${_maxPrice:F0}]";
                RejectedUpdates++;
                return false;
            }
            
            if (bestAsk < _minPrice || bestAsk > _maxPrice)
            {
                rejectReason = $"Ask ${bestAsk:F2} outside valid range [${_minPrice:F0}, ${_maxPrice:F0}]";
                RejectedUpdates++;
                return false;
            }
            
            // 4. Check bid < ask (crossed book detection)
            if (bestBid >= bestAsk)
            {
                rejectReason = $"Crossed book: Bid ${bestBid:F2} >= Ask ${bestAsk:F2}";
                RejectedUpdates++;
                return false;
            }
            
            // 5. Check spread percentage - THIS IS YOUR KEY FIX
            double spread = bestAsk - bestBid;
            double midPrice = (bestBid + bestAsk) / 2.0;
            double spreadPercent = (spread / midPrice) * 100.0;
            
            if (spreadPercent > _maxSpreadPercent)
            {
                rejectReason = $"Spread {spreadPercent:F2}% > max {_maxSpreadPercent}% (Bid=${bestBid:F2}, Ask=${bestAsk:F2})";
                RejectedUpdates++;
                
                // Log this prominently - it's the bug you found
                Console.WriteLine($"[VALIDATOR] ✗ REJECTED: {rejectReason}");
                return false;
            }
            
            // 6. Check for price jumps (if we have previous data)
            if (_lastValidMidPrice > 0)
            {
                double priceChange = Math.Abs(midPrice - _lastValidMidPrice);
                double priceChangePercent = (priceChange / _lastValidMidPrice) * 100.0;
                
                if (priceChangePercent > _maxPriceJumpPercent)
                {
                    rejectReason = $"Price jump {priceChangePercent:F2}% > max {_maxPriceJumpPercent}% (${_lastValidMidPrice:F2} → ${midPrice:F2})";
                    RejectedUpdates++;
                    Console.WriteLine($"[VALIDATOR] ✗ REJECTED: {rejectReason}");
                    return false;
                }
            }
            
            // 7. Check volumes are reasonable
            if (orderBook.BidVolume <= 0 || orderBook.AskVolume <= 0)
            {
                rejectReason = $"Invalid volumes: BidVol={orderBook.BidVolume:F4}, AskVol={orderBook.AskVolume:F4}";
                RejectedUpdates++;
                return false;
            }
            
            // All checks passed - update last valid price
            _lastValidMidPrice = midPrice;
            rejectReason = "OK";
            return true;
        }
        
        /// <summary>
        /// Get validation statistics
        /// </summary>
        public ValidationStats GetStats()
        {
            return new ValidationStats
            {
                TotalUpdates = TotalUpdates,
                ValidUpdates = TotalUpdates - RejectedUpdates,
                RejectedUpdates = RejectedUpdates,
                RejectionRate = RejectionRate,
                LastValidMidPrice = _lastValidMidPrice
            };
        }
        
        /// <summary>
        /// Reset statistics (call at start of each trading day)
        /// </summary>
        public void ResetStats()
        {
            TotalUpdates = 0;
            RejectedUpdates = 0;
        }
    }

    // COMMENTED OUT - Using MarketData from Core/Types.cs instead
    // /// <summary>
    // /// Order book snapshot from exchange
    // /// </summary>
    // public class OrderBookSnapshot
    // {
    //     public string Symbol { get; set; }
    //     public double BestBid { get; set; }
    //     public double BestAsk { get; set; }
    //     public double BidVolume { get; set; }
    //     public double AskVolume { get; set; }
    //     public DateTime Timestamp { get; set; }
    //
    //     public double MidPrice => (BestBid + BestAsk) / 2.0;
    //     public double Spread => BestAsk - BestBid;
    //     public double SpreadBasisPoints => (Spread / MidPrice) * 10000.0;
    // }
    
    /// <summary>
    /// Validation statistics for monitoring
    /// </summary>
    public class ValidationStats
    {
        public int TotalUpdates { get; set; }
        public int ValidUpdates { get; set; }
        public int RejectedUpdates { get; set; }
        public double RejectionRate { get; set; }
        public double LastValidMidPrice { get; set; }
        
        public override string ToString()
        {
            return $"Total: {TotalUpdates} | Valid: {ValidUpdates} | Rejected: {RejectedUpdates} ({RejectionRate * 100:F2}%)";
        }
    }
}
