using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Communicator;
using Binance.Client.Websocket.Responses;
using Binance.Client.Websocket.Responses.Books;
using Binance.Client.Websocket.Responses.Trades;
using Binance.Client.Websocket.Sample.WinForms.Statistics;
using Binance.Client.Websocket.Sample.WinForms.Views;
using Binance.Client.Websocket.Subscriptions;
using Binance.Client.Websocket.Websockets;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI.Relational;
using Serilog;
using Websocket.Client;

namespace Binance.Client.Websocket.Sample.WinForms.Presenters
{
    class StatsPresenter
    {
        private readonly IStatsView _view;

        private TradeStatsComputer _tradeStatsComputer;
        private OrderBookStatsComputer _orderBookStatsComputer;

        private IBinanceCommunicator _communicator;
        private BinanceWebsocketClient _client;

        private IDisposable _pingSubscription;
        private DateTime _pingRequest;

        private readonly string _defaultPair = "btcusdt";
        private readonly string _currency = "$";

        private MySqlConnection Connection; 

        public StatsPresenter(IStatsView view)
        {
            _view = view;
            string server = System.Configuration.ConfigurationManager.AppSettings["Server"];
            string database = System.Configuration.ConfigurationManager.AppSettings["Database"];
            string username = System.Configuration.ConfigurationManager.AppSettings["UserName"];
            string password = System.Configuration.ConfigurationManager.AppSettings["Password"];

            Connection = new MySqlConnection($"Server='{server}'; database='{database}'; UID='{username}'; password='{password}'");
            HandleCommands();
        }

        private void HandleCommands()
        {
            _view.OnInit = OnInit;
            _view.OnStart = async () => await OnStart();
            _view.OnStop = OnStop;
        }

        private void OnInit()
        {
            Clear();
        }

        private async Task OnStart()
        {
            Connection.Open();
            var pair = _view.Pair;
            if (string.IsNullOrWhiteSpace(pair))
                pair = _defaultPair;
            pair = pair.ToUpper();

            _tradeStatsComputer = new TradeStatsComputer();
            _orderBookStatsComputer = new OrderBookStatsComputer();

            var url = BinanceValues.ApiWebsocketUrl;
            _communicator = new BinanceWebsocketCommunicator(url);
            _client = new BinanceWebsocketClient(_communicator);

            Subscribe(_client);

            _communicator.ReconnectionHappened.Subscribe(type =>
            {
                _view.Status($"Reconnected", StatusType.Info);
            });

            _communicator.DisconnectionHappened.Subscribe(type =>
            {
                if (type.Type == DisconnectionType.Error)
                {
                    _view.Status($"Disconnected by error, next try in {_communicator.ErrorReconnectTimeout.Value.TotalMilliseconds/1000} sec", StatusType.Error);
                    return;
                }
                _view.Status($"Disconnected", StatusType.Warning);
            });

            SetSubscriptions(_client, pair);
            await _communicator.Start();

            StartPingCheck(_client);
        }

        private void OnStop()
        {
            Connection.Close();
            _pingSubscription?.Dispose();
            _client.Dispose();
            _communicator.Dispose();
            _client = null;
            _communicator = null;
            Clear();
        }

        private void Subscribe(BinanceWebsocketClient client)
        {
            client.Streams.TradesStream.ObserveOn(TaskPoolScheduler.Default).Subscribe(HandleTrades);
            client.Streams.OrderBookPartialStream.ObserveOn(TaskPoolScheduler.Default).Subscribe(HandleOrderBook);
            client.Streams.PongStream.ObserveOn(TaskPoolScheduler.Default).Subscribe(HandlePong);
        }

        private void SetSubscriptions(BinanceWebsocketClient client, string pair)
        {
            client.SetSubscriptions(
                new TradeSubscription(pair),
                new OrderBookPartialSubscription(pair, 20)
                );
        }

        private void HandleTrades(TradeResponse response)
        {
            var trade = response.Data;
            Log.Information($"Received [{trade.Side}] trade, price: {trade.Price}, amount: {trade.Quantity}");
            _tradeStatsComputer.HandleTrade(trade);

            FormatTradesStats(_view.Trades1Min, _tradeStatsComputer.GetStatsFor(1));
            FormatTradesStats(_view.Trades5Min, _tradeStatsComputer.GetStatsFor(5));
            FormatTradesStats(_view.Trades15Min, _tradeStatsComputer.GetStatsFor(15));
            FormatTradesStats(_view.Trades1Hour, _tradeStatsComputer.GetStatsFor(60));
            FormatTradesStats(_view.Trades24Hours, _tradeStatsComputer.GetStatsFor(60 * 24));
        }

        private void FormatTradesStats(Action<string, Side> setAction, TradeStats trades)
        {
            if (trades == TradeStats.NULL)
                return;

            if (trades.BuysPerc >= trades.SellsPerc)
            {
                setAction($"{trades.BuysPerc:###}% buys{Environment.NewLine}{trades.TotalCount}", Side.Buy);
                return;
            }
            setAction($"{trades.SellsPerc:###}% sells{Environment.NewLine}{trades.TotalCount}", Side.Sell);
        }

        private void HandleOrderBook(OrderBookPartialResponse response)
        {
            _orderBookStatsComputer.HandleOrderBook(response);

            var stats = _orderBookStatsComputer.GetStats();
            if (stats == OrderBookStats.NULL)
                return;

            _view.Bid = stats.Bid.ToString("#.000000000");
            _view.Ask = stats.Ask.ToString("#.000000000");

            _view.BidAmount = $"{stats.BidAmountPerc:###}%{Environment.NewLine}{FormatToMillions(stats.BidAmount)}";
            _view.AskAmount = $"{stats.AskAmountPerc:###}%{Environment.NewLine}{FormatToMillions(stats.AskAmount)}";


            string table = System.Configuration.ConfigurationManager.AppSettings["Table"];
            string query = $"INSERT INTO {table}(pair, ask, bid, ask_quantity, bid_quantity) VALUES";
            query += $"('{_view.Pair.ToString()}', '{_view.Ask.ToString()}', '{_view.Bid.ToString()}', '{stats.AskQuantity.ToString()}', '{stats.BidQuantity.ToString()}')";
            var cmd = new MySqlCommand(query, Connection);
            cmd.ExecuteNonQuery();
        }

        private string FormatToMillions(double amount)
        {
            var millions = amount / 1000000;
            return $"{_currency}{millions:#.00} M";
        }

        private void StartPingCheck(BinanceWebsocketClient client)
        {
            //_pingSubscription = Observable
            //    .Interval(TimeSpan.FromSeconds(5))
            //    .Subscribe(async x =>
            //    {
            //        _pingRequest = DateTime.UtcNow;
            //        await client.Send(new PingRequest());
            //    });      
        }

        private void HandlePong(PongResponse pong)
        {
            var current = DateTime.UtcNow;
            ComputePing(current, _pingRequest);
        }

        private void ComputePing(DateTime current, DateTime before)
        {
            var diff = current.Subtract(before);
            _view.Ping = $"{diff.TotalMilliseconds:###} ms";
            _view.Status("Connected", StatusType.Info);
        }

        private void Clear()
        {
            _view.Bid = string.Empty;
            _view.Ask = string.Empty;
            _view.BidAmount = string.Empty;
            _view.AskAmount = string.Empty;
            _view.Trades1Min(string.Empty, Side.Buy);
            _view.Trades5Min(string.Empty, Side.Buy);
            _view.Trades15Min(string.Empty, Side.Buy);
            _view.Trades1Hour(string.Empty, Side.Buy);
            _view.Trades24Hours(string.Empty, Side.Buy);
        }
    }
}
