using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NYCAlphaTrader.Connectors;
using NYCAlphaTrader.Core;
using NYCAlphaTrader.Strategies;
using TradingSystem.Configuration;
using TradingSystem.Core;
using TradingSystem.Risk;
using TradingSystem.Execution;
using TradingSystem.Logging;

namespace NYCAlphaTrader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("===========================================");
            Console.WriteLine("NYC ALPHA TRADER - Simplified HFT System");
            Console.WriteLine("Target: $200-350/day on $47k capital");
            Console.WriteLine("===========================================\n");

            // Determine environment
            TradingSystem.Configuration.TradingEnvironment environment = DetermineEnvironment(args);

            Console.WriteLine($"\nINITIALIZING TRADING SYSTEM - {environment}\n");

            // Create parameter manager
            var paramManager = new StrategyParameterManager(environment);
            paramManager.LogConfiguration();

            // Configuration
            var config = new TradingConfig
            {
                Capital = 47000,
                MaxDailyLoss = 2350, // 5% of capital
                MaxPositionSize = 5000,
                TradingEnabled = true,

                // Enable strategies based on NYC latency profile
                EnableOffHoursTrading = true,
                EnableMeanReversion = true,
                EnableLiquidationWicks = true,
                EnableOBI = true,
                EnableRegimeTrading = true
            };

            // NEW: Initialize logging
            var logger = new LogManager("logs");

            // NEW: Initialize risk management with proper TradingSystem.Risk.TradingEnvironment
            var riskConfig = TradingSystem.Risk.RiskConfig.GetConfig(
                environment == TradingSystem.Configuration.TradingEnvironment.Production
                    ? TradingSystem.Risk.TradingEnvironment.Production
                    : TradingSystem.Risk.TradingEnvironment.Testnet
            );
            var newRiskManager = new TradingSystem.Risk.RiskManager(riskConfig);

            logger.Log("═══ NYC Alpha Trader Started ═══");

            // NEW: Initialize Market Regime Detector
            var regimeDetector = new MarketRegimeDetector(ema20Period: 20, ema50Period: 50);
            logger.Log("Market Regime Detector initialized");

            // Output: Total: 1000 | Valid: 987 | Rejected: 13 (1.30%)
            var throttler = new SignalThrottler(defaultMinSeconds: 30);
            // Set strategy-specific times if needed
            throttler.SetMinTime("OBI", 30);           // 30 seconds for OBI
            throttler.SetMinTime("MeanReversion", 45);  // 45 seconds for MR
            throttler.SetMinTime("LiquidationWick", 60); // 60 seconds for Wick

            // Initialize strategies
            var obiStrategy = new OBIStrategy(config, paramManager);
            var mrStrategy = new MeanReversionStrategy(config, paramManager);
            var wickStrategy = new LiquidationWickStrategy(config, paramManager);

            // Export configuration
            string configPath = $"config_{environment}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
            File.WriteAllText(configPath, paramManager.ExportConfiguration());

            // Initialize system components
            var riskManager = new NYCAlphaTrader.Core.RiskManager(config);
            var orderTracker = new OrderTracker();
            var strategyCoordinator = new StrategyCoordinator(config, riskManager, obiStrategy, mrStrategy, wickStrategy);

            // Note: OrderExecutor will be initialized in RunTradingLoop with actual Binance client
            // It needs the live client reference for balance/price queries

            logger.Log("System initialized successfully\n");
            Console.WriteLine("Enabled Strategies:");
            Console.WriteLine("  - Off-Hours Alpha (11pm-5am EST)");
            Console.WriteLine("  - Mean Reversion");
            Console.WriteLine("  - Liquidation Wick Capture");
            Console.WriteLine("  - Order Book Imbalance (OBI)");
            Console.WriteLine("  - Regime Trading\n");

            Console.WriteLine("Press Ctrl+C to stop...\n");

            // Start trading loop
            var cts = new CancellationTokenSource();

            Console.WriteLine("\n═══════════════════════════════════════");
            Console.WriteLine("To STOP trading:");
            Console.WriteLine("  • Press 'Q' key");
            Console.WriteLine("  • Press Ctrl+C");
            Console.WriteLine("═══════════════════════════════════════\n");

            // Method 1: Ctrl+C handler
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\n[SHUTDOWN] Ctrl+C received - shutting down gracefully...");
            };

            // Method 2: Q key handler (more reliable on Windows)
            _ = Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Q || key.KeyChar == 'q')
                            {
                                Console.WriteLine("\n[SHUTDOWN] Q key pressed - shutting down gracefully...");
                                cts.Cancel();
                                break;
                            }
                        }
                        Thread.Sleep(100);
                    }
                }
                catch { }
            });

            await RunTradingLoop(strategyCoordinator, riskManager, cts.Token, throttler, logger, newRiskManager, regimeDetector);

            logger.Log("Trading session complete.");
            PrintDailyStats(strategyCoordinator, riskManager);
            logger.Dispose();
        }

        static async Task RunTradingLoop(
            StrategyCoordinator coordinator,
            NYCAlphaTrader.Core.RiskManager riskManager,
            CancellationToken cancellationToken,
            SignalThrottler throttler,
            LogManager logger,
            TradingSystem.Risk.RiskManager newRiskManager,
            MarketRegimeDetector regimeDetector)
        {
            int updateCount = 0;
            MarketRegime currentRegime = MarketRegime.Ranging;
            MarketRegime previousRegime = MarketRegime.Ranging;

            var binance = new BinanceConnector();
            var fillProcessor = new FillProcessor(riskManager);

            // Initialize validator
            var orderBookValidator = new OrderBookValidator(
                maxSpreadPercent: 5.0,          // Reject spreads > 5% (catches your $84k-$105k bug)
                minPrice: 10000,                // BTC must be >= $10k
                maxPrice: 200000,               // BTC must be <= $200k
                maxPriceJumpPercent: 2.0        // Reject jumps > 2% since last valid
            );

            // Initialize Internal Risk Manager for SL/TP management
            var internalRiskManager = new InternalRiskManager();
            logger.Log("Internal Risk Manager initialized (Stealth SL/TP mode)");

            Console.WriteLine($"\n[VALIDATOR] Initialized with thresholds:");
            Console.WriteLine($"  Max Spread: 5.0%");
            Console.WriteLine($"  Price Range: $10,000 - $200,000");
            Console.WriteLine($"  Max Price Jump: 2.0%\n");


            await binance.Connect();
            await binance.SubscribeOrderBook("BTCUSDT");
            await binance.SubscribeTicker("BTCUSDT");

            var restClient = new BinanceRestClient();

            // Note: OrderExecutor integration requires IBinanceClient interface
            // For now, continue with existing restClient.PlaceOrder() approach
            // OrderExecutor will be integrated after refactoring BinanceRestClient to implement IBinanceClient

            // Start Fill Processor in the background
            // Start FillProcessor
            var fillTask = Task.Run(async () =>
                await fillProcessor.SubscribeFills(restClient));

            Console.WriteLine("Waiting 3 seconds for WS connection...");
            await Task.Delay(3000);  // Give it time to connect


            Console.WriteLine("FillProcessor started.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Receive real market data
                    var marketData = await binance.ReceiveOrderBookUpdate();
                    if (marketData == null)
                    {
                        Console.WriteLine("[WARN] Received null market data, skipping this update.");
                        continue;
                    }

                    // CRITICAL: Validate order book before processing
                    if (!orderBookValidator.ValidateOrderBook(marketData, out string rejectReason))
                    {
                        Console.WriteLine($"[VALIDATOR] ✗ REJECTED: {rejectReason}");
                        continue;  // Skip invalid data - don't process corrupted updates
                    }

                    // Log validation passed
                    double spreadPercent = (marketData.BestAsk - marketData.BestBid) / (marketData.BestBid + marketData.BestAsk) / 2.0 * 100.0;
                    Console.WriteLine($"[VALIDATOR] - PASSED: Spread {spreadPercent:F2}% (Bid=${marketData.BestBid:F2}, Ask=${marketData.BestAsk:F2})");

                    // CRITICAL: Check risk manager for exits FIRST (before generating new signals)
                    double currentPrice = marketData.LastPrice;
                    var exitsNeeded = internalRiskManager.CheckExits(currentPrice);
                    foreach (var position in exitsNeeded)
                    {
                        try
                        {
                            // Close position at market
                            var closeSignal = new TradingSignal
                            {
                                Symbol = position.Symbol,
                                Strategy = position.Strategy + "_Exit",
                                Side = position.Side == Side.Buy ? Side.Sell : Side.Buy,  // Reverse side to close
                                Price = position.Side == Side.Buy ? marketData.BestBid : marketData.BestAsk,
                                Quantity = position.Quantity,
                                GeneratedAt = DateTime.UtcNow
                            };

                            var closeOrderId = await restClient.PlaceOrder(closeSignal);
                            logger.LogTrade(
                                closeSignal.Strategy,
                                closeSignal.Side.ToString(),
                                closeSignal.Quantity,
                                closeSignal.Price,
                                closeOrderId.ToString()
                            );
                            Console.WriteLine($"[EXIT] Closed position {position.OrderId} via order {closeOrderId}");
                        }
                        catch (Exception ex)
                        {
                            logger.Log($"[EXIT ERROR] Failed to close position {position.OrderId}: {ex.Message}");
                            Console.WriteLine($"[EXIT ERROR] {ex.Message}");
                        }
                    }

                    Console.WriteLine(new string('-', 50));
                    Console.WriteLine(
                        $"Symbol: {marketData.Symbol} | " +
                        $"Timestamp: {marketData.Timestamp} | " +
                        $"BestBid: {marketData.BestBid} | BestAsk: {marketData.BestAsk} | " +
                        $"BidVol: {marketData.BidVolume} | AskVol: {marketData.AskVolume} | " +
                        $"LastPrice: {marketData.LastPrice} | " +
                        $"Volume24h: {marketData.Volume24h}"
                    );

                    Console.WriteLine("Top 5 Bid Levels:");
                    foreach (var level in marketData.BidLevels)
                        Console.WriteLine($"  Price: {level.Price} : Quantity: {level.Quantity}");

                    Console.WriteLine("Top 5 Ask Levels:");
                    foreach (var level in marketData.AskLevels)
                        Console.WriteLine($"  Price: {level.Price} : Quantity: {level.Quantity}");

                    // CRITICAL: Detect market regime BEFORE generating signals
                    currentRegime = regimeDetector.DetectRegime(marketData);

                    // Log regime changes
                    if (currentRegime != previousRegime)
                    {
                        string regimeMsg = $"[REGIME CHANGE] {previousRegime} → {currentRegime}";
                        Console.WriteLine($"\n{regimeMsg}\n");
                        logger.Log(regimeMsg);
                        previousRegime = currentRegime;
                    }

                    // Generate signals WITH regime awareness
                    var signals = coordinator.ProcessMarketUpdate(marketData, throttler, currentRegime);
                    if (signals == null || signals.Count == 0)
                    {
                        Console.WriteLine("[WARN] Received null signals, skipping this update.");
                        continue; // nothing to process
                    }

                    // Execute valid signals
                    foreach (var signal in signals)
                    {
                        if (riskManager.CheckOrder(signal))
                        {
                            try
                            {
                                var orderId = await restClient.PlaceOrder(signal);

                                // Calculate USDT required for this order
                                double usdtRequired = signal.Quantity * signal.Price;

                                // Log to persistent trade log
                                logger.LogTrade(
                                    signal.Strategy,
                                    signal.Side.ToString(),
                                    signal.Quantity,
                                    signal.Price,
                                    orderId.ToString()
                                );

                                // Track position with Internal Risk Manager (SL/TP/Time stops)
                                internalRiskManager.TrackPosition(
                                    orderId: orderId,
                                    symbol: signal.Symbol,
                                    side: signal.Side,
                                    entryPrice: signal.Price,
                                    quantity: signal.Quantity,
                                    strategy: signal.Strategy,
                                    stopLossPct: 0.003,      // 0.3% stop loss
                                    takeProfitPct: 0.005,    // 0.5% take profit
                                    maxHoldSeconds: 300      // 5 minutes max hold
                                );

                                Console.WriteLine($"[ORDER] {signal.Strategy}: {orderId} | USDT Required: ${usdtRequired:F2}");
                                Thread.Sleep(100); // Your timing
                            }
                            catch (Exception ex)
                            {
                                logger.Log($"[ORDER EXECUTION] {signal.Strategy} failed: {ex.Message} | Qty: {signal.Quantity} @ {signal.Price} | USDT Required: ${signal.Quantity * signal.Price:F2}");
                                Console.WriteLine($"[ERROR] Order failed: {ex.Message}");
                            }
                        }
                    }

                    updateCount++;
                    if (updateCount % 100 == 0)
                    {
                        // Log validator stats
                        var validatorStats = orderBookValidator.GetStats();
                        Console.WriteLine($"[VALIDATOR] {validatorStats}");
                        
                        // Alert if rejection rate is too high
                        if (validatorStats.RejectionRate > 0.10)  // More than 10%
                        {
                            Console.WriteLine($"[ALERT] High rejection rate detected: {validatorStats.RejectionRate * 100:F2}%");
                        }
                        
                        // Status logging with logger integration
                        PrintStatus(coordinator, riskManager, logger);
                    }

                    await Task.Delay(100, cancellationToken); // 10Hz update rate
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                }
            }
        }

        static MarketData SimulateMarketData()
        {
            // TODO: Replace with real market data from exchange
            var random = new Random();
            return new MarketData
            {
                Symbol = "BTCUSDT",
                Timestamp = DateTime.UtcNow,
                BestBid = 50000 + random.NextDouble() * 100,
                BestAsk = 50005 + random.NextDouble() * 100,
                BidVolume = 5 + random.NextDouble() * 10,
                AskVolume = 5 + random.NextDouble() * 10,
                LastPrice = 50002.5,
                Volume24h = 1000000
            };
        }

        static void PrintStatus(StrategyCoordinator coordinator, NYCAlphaTrader.Core.RiskManager riskManager, TradingSystem.Logging.LogManager logger)
        {
            var stats = coordinator.GetStats();
            var risk = riskManager.GetRiskStats();

            string statusMsg = $"[STATUS] Signals: {stats.TotalSignals} | P&L: ${risk.DailyPnL:F2} | Win%: {stats.WinRate:P1}";
            Console.WriteLine($"\n{statusMsg}");
            logger.Log(statusMsg);
        }

        static void PrintDailyStats(StrategyCoordinator coordinator, NYCAlphaTrader.Core.RiskManager riskManager)
        {
            Console.WriteLine("\n===========================================");
            Console.WriteLine("DAILY PERFORMANCE SUMMARY");
            Console.WriteLine("===========================================");

            var stats = coordinator.GetStats();
            var risk = riskManager.GetRiskStats();

            Console.WriteLine($"Total Signals Generated: {stats.TotalSignals}");
            Console.WriteLine($"Trades Executed: {stats.TradesExecuted}");
            Console.WriteLine($"Win Rate: {stats.WinRate:P1}");
            Console.WriteLine($"Daily P&L: ${risk.DailyPnL:F2}");
            Console.WriteLine($"Max Drawdown: ${risk.MaxDrawdown:F2}");
            Console.WriteLine($"Sharpe Ratio: {stats.SharpeRatio:F2}");

            Console.WriteLine("\nPer-Strategy Breakdown:");
            foreach (var kvp in stats.StrategyPnL)
            {
                Console.WriteLine($"  {kvp.Key}: ${kvp.Value:F2}");
            }

            Console.WriteLine("===========================================\n");
        }

        private static TradingSystem.Configuration.TradingEnvironment DetermineEnvironment(string[] args)
        {
            // Command line: dotnet run -- Testnet
            if (args.Length > 0 && Enum.TryParse<TradingSystem.Configuration.TradingEnvironment>(args[0], true, out var env))
                return env;

            // Environment variable: export TRADING_ENVIRONMENT=Production
            string envVar = Environment.GetEnvironmentVariable("TRADING_ENVIRONMENT");
            if (!string.IsNullOrEmpty(envVar) && Enum.TryParse<TradingSystem.Configuration.TradingEnvironment>(envVar, true, out env))
                return env;

            // Default to testnet for safety
            return TradingSystem.Configuration.TradingEnvironment.Testnet;
        }
    }
}
