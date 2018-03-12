using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

// EURUSD scalper, aprox 14% rentability for year 2017


namespace cAlgo {
  [Robot(TimeZone = TimeZones.WEuropeStandardTime, AccessRights = AccessRights.None)]
  public class Scalper43 : Robot {
    private class Timing : Tuple<Int32, Int32> {
      public Int32 Reference { get { return Item1; } }
      public Int32 Local { get { return Item2; } }

      public Timing(int reference, int local) : base(reference, local) { }

      public override string ToString() {
        return String.Format("{0}/{1}", Reference, Local);
      }
    }

    [Parameter("Stop Loss PIPs", DefaultValue = 30)]
    public double StopLossPIPS { get; set; }

    [Parameter("Take Profit PIPS", DefaultValue = 10)]
    public double TakeProfitPIPS { get; set; }

    //[Parameter("Stop Loss ATR's", DefaultValue = 2)]
    //public int StopLossATRS { get; set; }

    //[Parameter("Take Profit ATR's", DefaultValue = 1)]
    //public int TakeProfitATRS { get; set; }

    [Parameter("Minimum ATR", DefaultValue = 0.0002, Step = 0.0001)]
    public double MinimumAtr { get; set; }

    [Parameter("Risked %", DefaultValue = 1, Step = 0.5)]
    public double Risk { get; set; }

    [Parameter("Start Hour", DefaultValue = 2)]
    public int StartTime { get; set; }

    [Parameter("Stop Hour", DefaultValue = 22)]
    public int StopTime { get; set; }

    private StochasticOscillator sto;
    private AverageTrueRange atr;

    private ExponentialMovingAverage ema8;
    private ExponentialMovingAverage ema3;

    private WeightedMovingAverage lmm150;
    private WeightedMovingAverage lmm300;
    private WeightedMovingAverage rmm150;
    private WeightedMovingAverage rmm300;

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private long losing = 1;

    private Position currentPosition = null;
    private MarketSeries rseries;

    protected override void OnStart() {
      var reftf = GetReferenceTimeframe(MarketSeries.TimeFrame);
      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, reftf);

      atr = Indicators.AverageTrueRange(MarketSeries, 6, MovingAverageType.Weighted);
      sto = Indicators.StochasticOscillator(MarketSeries, 13, 9, 4, MovingAverageType.Simple);

      ema8 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 8);
      ema3 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 3);

      lmm150 = Indicators.WeightedMovingAverage(MarketSeries.Close, 150);
      lmm300 = Indicators.WeightedMovingAverage(MarketSeries.Close, 300);
      rmm150 = Indicators.WeightedMovingAverage(rseries.Close, 150);
      rmm300 = Indicators.WeightedMovingAverage(rseries.Close, 300);

      lmacd = Indicators.MacdCrossOver(MarketSeries.Close, 26, 12, 9);
      rmacd = Indicators.MacdCrossOver(rseries.Close, 26, 12, 9);

      Positions.Closed += PositionsOnClosed;
    }

    private void PositionsOnClosed(PositionClosedEventArgs obj) {
      var pos = obj.Position;
      if (currentPosition != null && currentPosition.Id == pos.Id) {
        currentPosition = null;

        // Handle lossing counter
        if (pos.GrossProfit < 0) {
          losing++;
        } else {
          losing--;
          if (losing < 1) losing = 1;
        }
      }
    }

    //private int GetCurrentAtrInPips() {
    //  return (int)Math.Floor(atr.Result.LastValue * 10000);
    //} 

    //private int CalculateStopLoss() {
    //  return GetCurrentAtrInPips() * StopLossATRS;
    //}

    //private int CalculateTakeProfit() {
    //  return GetCurrentAtrInPips() * TakeProfitATRS;
    //}

    protected override void OnBar() {
      if (currentPosition == null) {
        //if (!IsOnTraidingTime()) return;

        var signal = GetSignal();
        //var stopLoss = CalculateStopLoss();
        //var takeProfit = CalculateTakeProfit();

        var label = String.Format("scalper,t={0}", CalculateMarketTiming().ToString());

        if (signal == -1) {
          var volume = GetVolume(TradeType.Sell);
          var result = ExecuteMarketOrder(TradeType.Sell, Symbol, volume, label, StopLossPIPS, TakeProfitPIPS, 1);
          //var result = ExecuteMarketOrder(TradeType.Sell, Symbol, volume, "scalper", stopLoss, takeProfit, 1);

          if (result.IsSuccessful) {
            currentPosition = result.Position;
          } else {
            Print("Error on open SELL possition.");
          }
        } else if (signal == 1) {
          var volume = GetVolume(TradeType.Buy);
          var result = ExecuteMarketOrder(TradeType.Buy, Symbol, volume, label, StopLossPIPS, TakeProfitPIPS, 1);
          //var result = ExecuteMarketOrder(TradeType.Buy, Symbol, volume, "scalper", stopLoss, takeProfit, 1);


          if (result.IsSuccessful) {
            currentPosition = result.Position;
          } else {
            Print("Error on open BUY possition.");
          }
        }
      }
    }

    //protected override void OnTick() {
    //  if (currentPosition != null) {
    //    if (currentPosition.Pips >= 9) {
    //      if (currentPosition.TradeType == TradeType.Buy) {
    //        ModifyPosition(currentPosition, currentPosition.EntryPrice + (Symbol.PipSize * 5), currentPosition.TakeProfit);
    //      } else {
    //        ModifyPosition(currentPosition, currentPosition.EntryPrice - (Symbol.PipSize * 5), currentPosition.TakeProfit);
    //      }
    //    }
    //  }
    //}

    private long GetVolume(TradeType ttype) {
      var riskedAmount = Account.Balance * (Risk / 100);
      var price = ttype == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
      var volume = 1 / ((price * Symbol.PipSize * StopLossPIPS) / riskedAmount);
      return Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);
    }

    //private long GetVolume(TradeType ttype) {
    //  var riskedAmount = Account.Balance * (Risk / 100);
    //  var price = ttype == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
    //  var volume = 1 / ((price * Symbol.PipSize * CalculateStopLoss()) / riskedAmount);
    //  return Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);
    //}

    public int GetSignal() {
      if (atr.Result.LastValue < MinimumAtr) {
        return 0;
      }

      var timing = CalculateMarketTiming();
      if ((timing.Reference == -1 || timing.Reference == -4)
          && ema3.Result.HasCrossedBelow(ema8.Result, 1)
          && sto.PercentD.Last(1) > 80
          && sto.PercentK.Last(1) > 80) {
        return -1;
      } else if ((timing.Reference == 1 || timing.Reference == 4)
                 && ema3.Result.HasCrossedAbove(ema8.Result, 1)
                 && sto.PercentD.Last(1) < 20
                 && sto.PercentK.Last(1) < 20) {
        return 1;
      } else {
        return 0;
      }
    }

    private bool IsOnTraidingTime() {
      return Server.Time.Hour >= StartTime && Server.Time.Hour <= StopTime;
    }

    //private bool IsRising(DataSeries data) {
    //  int periods = 3;
    //  double sum = data.Last(1);

    //  for (int i = periods; i > 1; i--) {
    //    sum += data.Last(i);
    //  }

    //  return (sum / periods) > data.Last(periods);
    //}

    //private bool IsFalling(DataSeries data) {
    //  int periods = 3;
    //  double sum = data.Last(1);
    //  for (int i = periods; i > 1; i--) {
    //    sum += data.Last(i);
    //  }

    //  return (sum / periods) < data.Last(periods);
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

      if (IsTrendUp(lseries, lmm300)) {
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
      if (IsTrendUp(rseries, rmm300)) {
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