using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo {
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class ScalperV1 : Robot {
        [Parameter("Source")]
        public DataSeries Source { get; set; }

        [Parameter("Stop Loss PIPs", DefaultValue = 10)]
        public double StopLossPIPS { get; set; }

        [Parameter("Take Profit PIPS", DefaultValue = 10)]
        public double TakeProfitPIPS { get; set; }

        [Parameter("Volume", DefaultValue = 10000)]
        public int Volume { get; set; }

        [Parameter("MACD Threshold", DefaultValue = 0.0003)]
        public double MacdThreshold { get; set; }

        //[Parameter("Start Hour", DefaultValue = 8)]
        //public int StartTime { get; set; }

        //[Parameter("Stop Hour", DefaultValue = 21)]
        //public int StopTime { get; set; }

        private ZeroLagMacd macd;
        private ExponentialMovingAverage mm8;
        private WeightedMovingAverage mm50;
        private WeightedMovingAverage mm150;
        private WeightedMovingAverage mm300;

        private long ticks = 0;
        private long bars = 0;
        private long losing = 1;
        private Position currentPosition = null;

        protected override void OnStart() {
            bars = Source.Count;
            macd = Indicators.GetIndicator<ZeroLagMacd>(26, 12, 9);

            mm8 = Indicators.ExponentialMovingAverage(Source, 8);
            mm50 = Indicators.WeightedMovingAverage(Source, 50);
            mm150 = Indicators.WeightedMovingAverage(Source, 150);
            mm300 = Indicators.WeightedMovingAverage(Source, 300);

            Positions.Closed += PositionsOnClosed;
        }

        private void PositionsOnClosed(PositionClosedEventArgs obj) {
            var pos = obj.Position;
            if (currentPosition != null && currentPosition.Id == pos.Id) {
                currentPosition = null;
                //Print("Position closed with {0} profit", pos.GrossProfit);
                if (pos.GrossProfit < 0) {
                    losing++;
                } else {
                    losing--;
                    if (losing < 1) losing = 1;
                    //losing = 1;
                }
            }
        }

        protected override void OnTick() {
            ticks++;

            if (bars < Source.Count) {
                bars = Source.Count;

                //if (!IsOnTraidingTime())
                //    return;

                HandleOnBarV1();
            }
        }

        //private bool IsOnTraidingTime() {
        //    return Server.Time.Hour >= StartTime && Server.Time.Hour <= StopTime;
        //}

        private long GetVolume() {
            if (losing == 1) {
                return Volume;
            } else {
                return Volume + (10000*losing);
            }
        }

        private bool IsBuySignal() {
            return (mm150.Result.Last(1) > mm300.Result.Last(1)
                    && IsRising(mm50.Result)
                    && IsRising(mm150.Result)
                    && IsRising(mm300.Result)
                    && macd.MACD.Last(1) <= -MacdThreshold
                    && macd.MACD.HasCrossedAbove(macd.Signal, 0));
        }

        private bool IsSellSignal() {
            return (mm150.Result.Last(1) < mm300.Result.Last(1)
                    && IsFalling(mm50.Result)
                    && IsFalling(mm150.Result)
                    && IsFalling(mm300.Result)
                    && macd.MACD.Last(1) >= MacdThreshold
                    && macd.MACD.HasCrossedBelow(macd.Signal, 0));
        }

        private bool PossibleSellReversal() {
            return ((mm150.Result.Last(1) > mm300.Result.Last(1)
                     //&& IsRising(mm50.Result)
                     //&& IsRising(mm150.Result)
                     //&& IsRising(mm300.Result)
                     && macd.MACD.HasCrossedBelow(macd.Signal, 1)) ||
                    (mm8.Result.HasCrossedBelow(mm150.Result, 0)));
        }

        private bool PossibleBuyReversal() {
            return ((mm150.Result.Last(1) < mm300.Result.Last(1)
                     //&& IsFalling(mm50.Result)
                     //&& IsFalling(mm150.Result)
                     //&& IsFalling(mm300.Result)
                     && macd.MACD.HasCrossedAbove(macd.Signal, 1)) ||
                    (mm8.Result.HasCrossedAbove(mm150.Result, 0)));
        }

        private void HandleOnBarV1() {
            if (currentPosition == null) {
                if (IsBuySignal()) {
                    var result = ExecuteMarketOrder(TradeType.Buy, Symbol, GetVolume(), "NiwiScalper", StopLossPIPS, TakeProfitPIPS);
                    if (result.IsSuccessful) {
                        currentPosition = result.Position;
                    } else {
                        Print("Error on open BUY possition.");
                    }
                } else if (IsSellSignal()) {
                    var result = ExecuteMarketOrder(TradeType.Sell, Symbol, GetVolume(), "NiwiScalper", StopLossPIPS, TakeProfitPIPS);
                    if (result.IsSuccessful) {
                        currentPosition = result.Position;
                    } else {
                        Print("Error on open SELL possition.");
                    }
                }
            } else {
                if (currentPosition.TradeType == TradeType.Buy && PossibleSellReversal()) {
                    ClosePosition(currentPosition);
                    currentPosition = null;
                } else if (currentPosition.TradeType == TradeType.Sell && PossibleBuyReversal()) {
                    ClosePosition(currentPosition);
                    currentPosition = null;
                    losing++; // TODO: maybe not necessary
                }
            }
        }

        private bool IsRising(DataSeries data) {
            int periods = 5;
            double sum = data.Last(1);

            for (int i = periods; i > 1; i--) {
                sum += data.Last(i);
            }

            return (sum / periods) > data.Last(periods);
        }

        private bool IsFalling(DataSeries data) {
            int periods = 5;
            double sum = data.Last(1);
            for (int i = periods; i > 1; i--) {
                sum += data.Last(i);
            }

            return (sum / periods) < data.Last(periods);
        }
    }
}