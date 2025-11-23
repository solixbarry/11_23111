using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NYCAlphaTrader.Core;

namespace NYCAlphaTrader.Connectors
{
    public class BinanceDepthUpdate
    {
        public string s { get; set; }   // symbol
        public List<List<string>> b { get; set; }  // bids
        public List<List<string>> a { get; set; }  // asks
    }

    public class BinanceTickerUpdate
    {
        public string s { get; set; }   // symbol
        public string c { get; set; }   // last price
        public string v { get; set; }   // 24h volume
        public string h { get; set; }   // high
        public string l { get; set; }   // low
    }

    public class UserDataUpdate
    {
        public string Type { get; set; }
        public string OrderStatus { get; set; }
        public string OrderId { get; set; }
        public string Symbol { get; set; }
        public string Side { get; set; }
        public double LastExecutedPrice { get; set; }
        public double LastExecutedQuantity { get; set; }
        public double Commission { get; set; }
    }

    public class BinanceConnector
    {
        private ClientWebSocket _ws = new ClientWebSocket();

        private readonly string _wsUrl = "wss://stream.binance.us:9443/ws";

        private List<List<string>> _lastBids = new();
        private List<List<string>> _lastAsks = new();

        private double? _LastPrice = null;
        private double? _24hrVol = null;
        private string _symbol;

        public async Task Connect()
        {
            await _ws.ConnectAsync(new Uri(_wsUrl), CancellationToken.None);
            Console.WriteLine("Connected to Binance.US WebSocket");
        }

        public async Task SubscribeOrderBook(string symbol)
        {
            // Binance.US symbols usually use no dash (BTCUSDT ok)
            string formatted = symbol.ToLower();

            var subscribe = new
            {
                method = "SUBSCRIBE",
                @params = new[] { $"{formatted}@depth@100ms" },
                id = 1
            };

            var json = JsonSerializer.Serialize(subscribe);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // Subscribe only to ticker stream
        public async Task SubscribeTicker(string symbol)
        {
            _symbol = symbol.ToUpper();
            string formatted = symbol.ToLower();

            var subscribe = new
            {
                method = "SUBSCRIBE",
                @params = new[] { $"{formatted}@ticker" },
                id = 2
            };

            var json = JsonSerializer.Serialize(subscribe);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task<MarketData> ReceiveOrderBookUpdate()
        {
            var buffer = new byte[8192];
            var result = await _ws.ReceiveAsync(buffer, CancellationToken.None);
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("e", out var eventProp))
            {
                string eventType = eventProp.GetString();

                if (eventType == "depthUpdate")
                {
                    if (root.TryGetProperty("b", out var bidsProp) && bidsProp.ValueKind == JsonValueKind.Array)
                    {
                        var bids = new List<List<string>>();
                        foreach (var bid in bidsProp.EnumerateArray())
                            if (bid.ValueKind == JsonValueKind.Array && bid.GetArrayLength() >= 2)
                                bids.Add(new List<string> { bid[0].GetString(), bid[1].GetString() });
                        if (bids.Count > 0) _lastBids = bids;
                    }

                    if (root.TryGetProperty("a", out var asksProp) && asksProp.ValueKind == JsonValueKind.Array)
                    {
                        var asks = new List<List<string>>();
                        foreach (var ask in asksProp.EnumerateArray())
                            if (ask.ValueKind == JsonValueKind.Array && ask.GetArrayLength() >= 2)
                                asks.Add(new List<string> { ask[0].GetString(), ask[1].GetString() });
                        if (asks.Count > 0) _lastAsks = asks;
                    }
                }
                else if (eventType == "24hrTicker")
                {
                    if (root.TryGetProperty("c", out var lastPriceProp) &&
                        double.TryParse(lastPriceProp.GetString(), out var lp))
                        _LastPrice = lp;

                    if (root.TryGetProperty("v", out var volProp) &&
                        double.TryParse(volProp.GetString(), out var vol))
                        _24hrVol = vol;
                }
            }

            // Return only if caches are filled
            if (_lastBids.Count == 0 || _lastAsks.Count == 0 || _LastPrice == null || _24hrVol == null)
                return null;

            var marketData = new MarketData
            {
                Symbol = _symbol,
                Timestamp = DateTime.UtcNow,
                BestBid = double.Parse(_lastBids[0][0]),
                BestAsk = double.Parse(_lastAsks[0][0]),
                BidVolume = double.Parse(_lastBids[0][1]),
                AskVolume = double.Parse(_lastAsks[0][1]),
                LastPrice = _LastPrice.Value,
                Volume24h = _24hrVol.Value,
                BidLevels = _lastBids.Take(5)
                    .Select(level => new Level
                    {
                        Price = double.Parse(level[0]),
                        Quantity = double.Parse(level[1])
                    }).ToList(),
                AskLevels = _lastAsks.Take(5)
                    .Select(level => new Level
                    {
                        Price = double.Parse(level[0]),
                        Quantity = double.Parse(level[1])
                    }).ToList()
            };

            return marketData;

            // Convert to your MarketData format
            // return new MarketData
            // {
            //     Symbol = "BTCUSDT",
            //     // ... map fields
            // };
        }

        
    }
}