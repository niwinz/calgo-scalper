using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo {
  [Robot(TimeZone = TimeZones.WEuropeStandardTime, AccessRights = AccessRights.None)]
  public class Scalper50 : Robot {
    private class Timing : Tuple<Int32, Int32> {
      public Int32 Reference { get { return Item1; } }
      public Int32 Local { get { return Item2; } }

      public Timing(int reference, int local) : base(reference, local) { }

      public override string ToString() {
        return String.Format("{0}/{1}", Reference, Local);
      }
    }

    [Parameter("Stop Loss ATR's", DefaultValue = 1, Step = 0.5)]
    public double StopLossATRS { get; set; }

    [Parameter("Take Profit ATR's", DefaultValue = 1, Step = 0.5)]
    public double TakeProfitATRS { get; set; }

    [Parameter("Stop Loss PIPs", DefaultValue = 60)]
    public int StopLossPIPS { get; set; }

    [Parameter("Take Profit PIPS", DefaultValue = 8)]
    public int TakeProfitPIPS { get; set; }

    [Parameter("Risked %", DefaultValue = 1, Step = 0.5)]
    public double Risk { get; set; }

    [Parameter("Volume", DefaultValue = 10000)]
    public int Volume { get; set; }

    [Parameter("Label", DefaultValue = "scalper")]
    public String Label { get; set; }

    private ExponentialMovingAverage lmm8;
    private WeightedMovingAverage lmm100;
    private WeightedMovingAverage lmm200;
    private WeightedMovingAverage rmm200;

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private AverageTrueRange atr;
    private StochasticOscillator sto;

    //private Position currentPosition = null;
    private MarketSeries rseries;

    private long BarId;
    private long LastPositionBarId;

    private HashSet<Int32> ignorableIds;

    protected override void OnStart() {
      ignorableIds = new HashSet<int>();

      BarId = MarketSeries.Close.Count;
      LastPositionBarId = BarId;

      var reftf = GetReferenceTimeframe(MarketSeries.TimeFrame);
      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, reftf);

      lmm8 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 8);
      lmm100 = Indicators.WeightedMovingAverage(MarketSeries.Close, 100);
      lmm200 = Indicators.WeightedMovingAverage(MarketSeries.Close, 200);
      rmm200 = Indicators.WeightedMovingAverage(rseries.Close, 200);

      lmacd = Indicators.MacdCrossOver(MarketSeries.Close, 26, 12, 9);
      rmacd = Indicators.MacdCrossOver(rseries.Close, 26, 12, 9);

      atr = Indicators.AverageTrueRange(MarketSeries, 14, MovingAverageType.Simple);
      sto = Indicators.StochasticOscillator(MarketSeries, 13, 3, 3, MovingAverageType.Simple);

      Positions.Closed += PositionsOnClosed;
    }

    private void PositionsOnClosed(PositionClosedEventArgs obj) {
      var pos = obj.Position;
      ignorableIds.Remove(pos.Id);
    }

    //private int GetAtrInPips() {
    //  // TODO: Determine the pip position from Symbol
    //  return (int)Math.Round(atr.Result.LastValue * 10000);
    //}

    //private int CalculateStopLoss(int atr) {
    //  return (int)Math.Round(atr * StopLossATRS);
    //}

    //private int CalculateTakeProfit(int atr) {
    //  return (int)Math.Round(atr * TakeProfitATRS);
    //}


    protected override void OnTick() {
      BarId = MarketSeries.Close.Count;

      if (BarId > (LastPositionBarId + 2)) {
        var timing = CalculateMarketTiming();
        var signal = GetSignal(timing);

        if (signal == -1) {
          var volume = GetVolume(TradeType.Sell, StopLossPIPS);
          var result = ExecuteMarketOrder(TradeType.Sell, Symbol, volume, Label, StopLossPIPS, TakeProfitPIPS, 1);

          if (result.IsSuccessful) {
            LastPositionBarId = BarId;
          } else {
            Print("Error on open SELL possition.");
          }
        } else if (signal == 1) {
          var volume = GetVolume(TradeType.Buy, StopLossPIPS);
          var result = ExecuteMarketOrder(TradeType.Buy, Symbol, volume, Label, StopLossPIPS, TakeProfitPIPS, 1);

          if (result.IsSuccessful) {
            LastPositionBarId = BarId;
          } else {
            Print("Error on open BUY possition.");
          }
        }
      }

      //var positions = Positions.FindAll(Label);
      //foreach (var position in positions) {
      //  if (position.TradeType == TradeType.Buy
      //      && lmacd.Signal.Last(1) > lmacd.MACD.Last(1)) {
      //    ClosePosition(position);
      //  } else if (position.TradeType == TradeType.Sell
      //             && lmacd.Signal.Last(1) < lmacd.MACD.Last(1)) {
      //    ClosePosition(position);
      //  }
      //}

      //foreach (var position in positions) {
      //  if (!ignorableIds.Contains(position.Id)) {
      //    if (position.Pips > (TakeProfitPIPS / 2)) {
      //      var volumeToClose = Symbol.NormalizeVolume(Volume / 2);
      //      //ClosePosition(position, volumeToClose);
      //      if (position.TradeType == TradeType.Buy) {
      //        ModifyPosition(position, position.EntryPrice - (Symbol.PipSize * (StopLossPIPS / 2)), position.TakeProfit);

      //      } else {
      //        ModifyPosition(position, position.EntryPrice + (Symbol.PipSize * (StopLossPIPS / 2)), position.TakeProfit);
      //      }
      //      //ModifyPosition(position, position.EntryPrice, position.TakeProfit);
      //      ignorableIds.Add(position.Id);
      //    }
      //  }
      //}
    }

    private int GetSignal(Timing timing) {
      if (timing.Reference == 1
          && lmacd.MACD.HasCrossedAbove(0, 1)
          && lmacd.Signal.Last(1) < lmacd.MACD.Last(1)
          && sto.PercentK.Last(1) > 50) {
        return 1;
      }

      if (timing.Reference == -1 
          && lmacd.MACD.HasCrossedBelow(0, 1)
          && lmacd.Signal.Last(1) > lmacd.MACD.Last(1)
          && sto.PercentK.Last(1) < 50) {
        return -1;
      }

      return 0;
    }

    private long GetVolume(TradeType ttype, int stopLossPips) {
      //var riskedAmount = Account.Balance * (Risk / 100);
      //var price = ttype == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
      //var volume = 1 / ((price * Symbol.PipSize * stopLossPips) / riskedAmount);
      //return Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);
      return Volume;
    }

    //private bool IsOnTraidingTime() {
    //  return Server.Time.Hour >= StartTime && Server.Time.Hour <= StopTime;
    //}

    // -------------------------------------------
    // ----  Market Timing
    // -------------------------------------------

    private Timing CalculateMarketTiming() {
      var local = CalculateLocalTiming();
      var reference = CalculateReferenceTiming();

      return new Timing(reference, local);
    }

    private int CalculateLocalTiming() {
      var lseries = MarketSeries;

      if (IsTrendUp(lseries, lmm200)) {
        if (lmacd.Histogram.LastValue > 0 && lmacd.Signal.LastValue > 0) {
          return 1;
        } else if (lmacd.Histogram.LastValue > 0 && lmacd.Signal.LastValue < 0) {
          return 4;
        } else if (lmacd.Histogram.LastValue <= 0 && lmacd.Signal.LastValue >= 0) {
          return 2;
        } else {
          return 3;
        }
      } else {
        if (lmacd.Histogram.LastValue < 0 && lmacd.Signal.LastValue < 0) {
          return -1;
        } else if (lmacd.Histogram.LastValue < 0 && lmacd.Signal.LastValue > 0) {
          return -4;
        } else if (lmacd.Histogram.LastValue >= 0 && lmacd.Signal.LastValue <= 0) {
          return -2;
        } else {
          return -3;
        }
      }
    }

    private int CalculateReferenceTiming() {
      if (IsTrendUp(rseries, rmm200)) {
        if (rmacd.Histogram.LastValue > 0 && rmacd.Signal.LastValue > 0) {
          return 1;
        } else if (rmacd.Histogram.LastValue > 0 && rmacd.Signal.LastValue < 0) {
          return 4;
        } else if (rmacd.Histogram.LastValue <= 0 && rmacd.Signal.LastValue >= 0) {
          return 2;
        } else {
          return 3;
        }
      } else {
        if (rmacd.Histogram.LastValue < 0 && rmacd.Signal.LastValue < 0) {
          return -1;
        } else if (rmacd.Histogram.LastValue < 0 && rmacd.Signal.LastValue > 0) {
          return -4;
        } else if (rmacd.Histogram.LastValue >= 0 && rmacd.Signal.LastValue <= 0) {
          return -2;
        } else {
          return -3;
        }
      }
    }

    private TimeFrame GetReferenceTimeframe(TimeFrame tf) {
      if (tf == TimeFrame.Hour) {
        return TimeFrame.Daily;
      } else if (tf == TimeFrame.Minute5) {
        return TimeFrame.Hour;
      } else if (tf == TimeFrame.Daily) {
        return TimeFrame.Weekly;
      } else if (tf == TimeFrame.Weekly) {
        return TimeFrame.Weekly;
      } else {
        return TimeFrame.Hour;
      }
    }

    private bool IsTrendUp(MarketSeries series, WeightedMovingAverage wma) {
      var close = series.Close.LastValue;
      var value = wma.Result.LastValue;

      if (value < close) {
        return true;
      } else {
        return false;
      }
    }
  }
}