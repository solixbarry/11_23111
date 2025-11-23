using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NYCAlphaTrader.Core;
using System.Net.Http;
using System.Security.Cryptography;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.Threading;

namespace NYCAlphaTrader.Connectors
{
    public class BinanceOrderResponse
    {
        public long orderId { get; set; }
        public string symbol { get; set; }
        public string status { get; set; }
        public string side { get; set; }
        public string type { get; set; }
        public double price { get; set; }
        public double origQty { get; set; }
    }

    public class BinanceRestClient
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly string _apiKey = "Lw5ZK7w31dO3cLPmjeu55v881UMhj18uFydh8hF31cHjL4Rj1TdxCnYtzh8T2bbC";
        private readonly string _secretKey = "whJ3majI1kNukm4FB7ogwkScn1osi4MSSqG6kdmZOEfAcJnvITrbHFuOibyWMhTK";
        // private const string BaseUrl = "https://api.binance.com";

        // for testing
        private const string BaseUrl = "https://testnet.binance.vision";

        // For FillProcessor.cs
        private string _listenKey;
        private ClientWebSocket _userWs = new ClientWebSocket();
        private System.Timers.Timer _keepAliveTimer;


        public async Task<string> PlaceOrder(TradingSignal signal)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var queryString = $"symbol={signal.Symbol}" +
                             $"&side={signal.Side.ToString().ToUpper()}" +
                             $"&type=LIMIT" +
                             $"&timeInForce=GTC" +
                             $"&quantity={signal.Quantity:F8}" +
                             $"&price={signal.Price:F2}" +
                             $"&timestamp={timestamp}";

            var signature = GenerateSignature(queryString);
            queryString += $"&signature={signature}";

            // _http.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);

            var response = await _http.PostAsync(
                $"{BaseUrl}/api/v3/order?{queryString}",
                null
            );

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Binance API error: {content}");

            // Parse response to get order ID
            var orderResponse = JsonSerializer.Deserialize<BinanceOrderResponse>(
                content,
                new JsonSerializerOptions
                {
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                }
            );
            return orderResponse?.orderId.ToString(); // Return order ID

            // return content; // Return order ID
        }

        private string GenerateSignature(string queryString)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_secretKey);
            var queryBytes = Encoding.UTF8.GetBytes(queryString);

            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(queryBytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }


        public async Task SubscribeUserDataStream()
        {
            Console.WriteLine("[DEBUG] Creating listenKey...");

            // using var client = new HttpClient();
            _http.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);

            var response = await _http.PostAsync("https://testnet.binance.vision/api/v3/userDataStream", null);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] Failed to get listenKey: {await response.Content.ReadAsStringAsync()}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            // Console.WriteLine($"[DEBUG] ListenKey response: {json}");

            using var doc = JsonDocument.Parse(json);
            _listenKey = doc.RootElement.GetProperty("listenKey").GetString();

            // Console.WriteLine($"[DEBUG] UserDataStream listenKey: {_listenKey}");

            // Connect WS
            _userWs = new ClientWebSocket();
            var wsUrl = $"wss://stream.testnet.binance.vision/ws/{_listenKey}";
            // Console.WriteLine($"[DEBUG] Connecting to: {wsUrl}");

            await _userWs.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
            Console.WriteLine("[DEBUG] Connected to User Data Stream");

            // Keep-alive every 25 minutes (Binance closes after 60 min, renew early)
            // _keepAliveTimer = new System.Timers.Timer(25 * 60 * 1000);
            // _keepAliveTimer.Elapsed += async (_, __) =>
            // {
            //     using var keepClient = new HttpClient();
            //     keepClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);

            //     var content = new FormUrlEncodedContent(new[]
            //     {
            // new KeyValuePair<string, string>("listenKey", _listenKey)
            //     });

            //     var resp = await keepClient.PutAsync("https://testnet.binance.vision/api/v3/userDataStream", content);
            //     if (resp.IsSuccessStatusCode)
            //         Console.WriteLine($"[KEEPALIVE] OK @ {DateTime.Now:HH:mm:ss}");
            //     else
            //         Console.WriteLine($"[KEEPALIVE] FAILED: {await resp.Content.ReadAsStringAsync()}");
            // };
            // _keepAliveTimer.AutoReset = true;
            // _keepAliveTimer.Start();

            // Console.WriteLine("[DEBUG] Keep-alive timer started (every 25 min)");
        }

        public async Task<UserDataUpdate> ReceiveUserUpdate()
        {
            var buffer = new byte[8192];
            var messageBuffer = new List<byte>();

            while (true)
            {
                Console.WriteLine("[DEBUG] Waiting for message on UserData WS...");
                var result = await _userWs.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("[WARN] UserData WS closed by server");
                    return null;
                }

                // Append received bytes
                messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                // If this is the final frame of the message
                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.Clear(); // Important: reset for next message

                    // Console.WriteLine($"[RAW WS] {json}");

                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        var eventType = root.GetProperty("e").GetString();

                        // Only process execution reports
                        if (eventType == "executionReport")
                        {
                            var update = new UserDataUpdate
                            {
                                Type = "executionReport",
                                OrderStatus = root.GetProperty("X").GetString(),
                                OrderId = root.GetProperty("i").ToString(), // <-- FIXED
                                Symbol = root.GetProperty("s").GetString(),
                                Side = root.GetProperty("S").GetString(),
                                LastExecutedPrice = SafeParse(root, "L"),
                                LastExecutedQuantity = SafeParse(root, "l"),
                                Commission = root.TryGetProperty("n", out var n) ? SafeParse(n) : 0
                            };
                            Console.WriteLine($"[FILL] {update.Side} {update.LastExecutedQuantity} {update.Symbol} @ {update.LastExecutedPrice} | Status: {update.OrderStatus}");
                            return update;
                        }
                        else
                        {
                            Console.WriteLine($"[DEBUG] Ignored event type: {eventType}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Failed to parse WS message: {ex.Message}\nJSON: {json}");
                    }

                    // If not executionReport or failed to parse, continue waiting
                    // (do NOT return null here unless you want to skip forever)
                }
            }
        }

        private double SafeParse(JsonElement element, string property)
            => double.TryParse(element.GetProperty(property).GetString(), out var v) ? v : 0;

        private double SafeParse(JsonElement element)
            => double.TryParse(element.GetString(), out var v) ? v : 0;
    }
}