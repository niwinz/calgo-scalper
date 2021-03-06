﻿using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo {
  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class ScalperV2 : Robot {

    public class Timing : Tuple<Int32, Int32> {
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

    [Parameter("Risked %", DefaultValue = 2)]
    public double Risk { get; set; }

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private ExponentialMovingAverage mm8;
    private WeightedMovingAverage lmm50;
    private WeightedMovingAverage lmm150;
    private WeightedMovingAverage lmm300;
    private WeightedMovingAverage rmm150;
    private WeightedMovingAverage rmm300;

    private AverageTrueRange atr;

    private long ticks = 0;
    private long bars = 0;
    private long losing = 1;

    private Position currentPosition = null;
    private MarketSeries rseries;

    protected override void OnStart() {
      bars = MarketSeries.Close.Count;

      var reftf = GetReferenceTimeframe(MarketSeries.TimeFrame);
      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, reftf);

      lmacd = Indicators.MacdCrossOver(MarketSeries.Close, 26, 12, 9);
      rmacd = Indicators.MacdCrossOver(rseries.Close, 26, 12, 9);

      atr = Indicators.AverageTrueRange(MarketSeries, 14, MovingAverageType.Exponential);

      mm8 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 8);

      lmm50 = Indicators.WeightedMovingAverage(MarketSeries.Close, 50);
      lmm150 = Indicators.WeightedMovingAverage(MarketSeries.Close, 150);
      lmm300 = Indicators.WeightedMovingAverage(MarketSeries.Close, 300);
      rmm150 = Indicators.WeightedMovingAverage(rseries.Close, 150);
      rmm300 = Indicators.WeightedMovingAverage(rseries.Close, 300);

      Positions.Closed += PositionsOnClosed;
    }

    private void PositionsOnClosed(PositionClosedEventArgs obj) {
      var pos = obj.Position;
      if (currentPosition != null && currentPosition.Id == pos.Id) {
        currentPosition = null;
        if (pos.GrossProfit < 0) {
          losing++;
        } else {
          losing--;
          if (losing < 1) losing = 1;
        }
      }
    }

    protected override void OnTick() {
      ticks++;

      if (currentPosition == null) {
        var timing = CalculateMarketTiming();
        var signal = GetVcnSignal(timing);
        var label = String.Format("scalper-{0}", timing.ToString());

        if (signal == -2) {
          var volume = GetVolume(TradeType.Sell);

          var result = ExecuteMarketOrder(TradeType.Sell, Symbol, volume, label, StopLossPIPS, TakeProfitPIPS, 0);
          if (result.IsSuccessful) {
            currentPosition = result.Position;
          } else {
            Print("Error on open SELL possition.");
          }
        } else if (signal == 2) {
          var volume = GetVolume(TradeType.Buy);
          var result = ExecuteMarketOrder(TradeType.Buy, Symbol, volume, label, StopLossPIPS, TakeProfitPIPS, 0);
          if (result.IsSuccessful) {
            currentPosition = result.Position;
          } else {
            Print("Error on open BUY possition.");
          }
        }
      } else {
        if (currentPosition.Pips >= 8) {
          if (currentPosition.TradeType == TradeType.Buy) {
            var stopLoss = currentPosition.EntryPrice + (Symbol.PipSize * 5);
            var takeProfit = currentPosition.TakeProfit;
            ModifyPosition(currentPosition, stopLoss, takeProfit);
          } else {
            var stopLoss = currentPosition.EntryPrice - (Symbol.PipSize * 5);
            var takeProfit = currentPosition.TakeProfit;
            ModifyPosition(currentPosition, stopLoss, takeProfit);
          }
        }
      }
    }

    //private long GetVolume(TradeType ttype) {
    //  var riskedAmount = Account.Balance * (Risk / 100);
    //  var price = ttype == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
    //  var volume = 1 / ((price * Symbol.PipSize * StopLossPIPS) / riskedAmount);
    //  return Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);
    //}
    private long GetVolume(TradeType ttype) {
      var riskedAmount = Account.Balance * (Risk / 100);
      var price = ttype == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
      var volume = 1 / ((price * Symbol.PipSize * StopLossPIPS) / riskedAmount);
      volume = Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);

      var result = volume;
      for (int i=1; i<losing; i++) {
        result += result;
      }

      return (int)result;
    }

    public int GetVcnSignal(Timing timing) {
      var atrThreshold = 0.0002;
      var series = MarketSeries;
      var ema8 = mm8;

      var upTiming = new[] { 1, 4 };
      var downTiming = new[] { -1, -4 };

      if (upTiming.Contains(timing.Reference) && upTiming.Contains(timing.Local)
          && ema8.Result.LastValue > lmm50.Result.LastValue
          && series.Low.Last(1) > ema8.Result.Last(1)
          && series.Low.Last(2) > ema8.Result.Last(2)
          && series.Low.Last(3) > ema8.Result.Last(3)
          && series.Low.LastValue <= ema8.Result.LastValue
          && Symbol.Ask <= (ema8.Result.LastValue + Symbol.PipSize)
          && lmm150.Result.LastValue > lmm300.Result.LastValue
          && lmm50.Result.LastValue > lmm150.Result.LastValue
          && ema8.Result.LastValue > lmm50.Result.LastValue
          && atr.Result.LastValue >= atrThreshold) {
        return 2;
      } else if (downTiming.Contains(timing.Reference) && downTiming.Contains(timing.Local)
                 && ema8.Result.LastValue < lmm50.Result.LastValue
                 && series.High.Last(1) < ema8.Result.Last(1)
                 && series.High.Last(2) < ema8.Result.Last(2)
                 && series.High.Last(3) < ema8.Result.Last(3)
                 && series.High.LastValue >= ema8.Result.LastValue
                 && Symbol.Bid >= ema8.Result.LastValue
                 && lmm150.Result.LastValue < lmm300.Result.LastValue
                 && lmm50.Result.LastValue < lmm150.Result.LastValue
                 && ema8.Result.LastValue < lmm50.Result.LastValue
                 && atr.Result.LastValue >= atrThreshold) {
        return -2;
      } else {
        return 0;
      }
    }

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