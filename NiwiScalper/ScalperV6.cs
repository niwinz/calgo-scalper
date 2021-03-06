﻿using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;


// Bot that uses MACD for general tendence following and RSI for local
// entry and exit points. For now show pretty good results with default
// parameters from 01/09/2017 to 02/04/2018 on AUDUSD AND USDCAD

namespace cAlgo {
  [Robot(TimeZone = TimeZones.WEuropeStandardTime, AccessRights = AccessRights.None)]
  public class Scalper60 : Robot {
    private class Timing : Tuple<Int32, Int32> {
      public Int32 Reference { get { return Item1; } }
      public Int32 Local { get { return Item2; } }

      public Timing(int reference, int local) : base(reference, local) { }

      public override string ToString() {
        return String.Format("{0}/{1}", Reference, Local);
      }
    }
    [Parameter("Volume", DefaultValue = 50000)]
    public int Volume { get; set; }

    [Parameter("Label", DefaultValue = "scalperv6")]
    public String Label { get; set; }

    private ExponentialMovingAverage lmm50;
    private WeightedMovingAverage lmm100;
    private WeightedMovingAverage lmm200;
    private WeightedMovingAverage rmm200;

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private RelativeStrengthIndex rsi;

    private MarketSeries rseries;

    private long BarId;
    private long LastPositionBarId;
    private Position currentPosition;


    protected override void OnStart() {
      BarId = MarketSeries.Close.Count;
      LastPositionBarId = BarId;

      var reftf = GetReferenceTimeframe(MarketSeries.TimeFrame);
      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, reftf);

      lmm50 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 50);
      lmm100 = Indicators.WeightedMovingAverage(MarketSeries.Close, 100);
      lmm200 = Indicators.WeightedMovingAverage(MarketSeries.Close, 200);
      rmm200 = Indicators.WeightedMovingAverage(rseries.Close, 200);

      lmacd = Indicators.MacdCrossOver(MarketSeries.Close, 26, 12, 9);
      rmacd = Indicators.MacdCrossOver(rseries.Close, 26, 12, 9);

      rsi = Indicators.RelativeStrengthIndex(MarketSeries.Close, 6);
      Positions.Closed += PositionsOnClosed;

    }

    private void PositionsOnClosed(PositionClosedEventArgs obj) {
      var pos = obj.Position;
      if (currentPosition != null && currentPosition.Id == pos.Id) {
        currentPosition = null;
      }
    }

    protected override void OnTick() {
      BarId = MarketSeries.Close.Count;
      var timing = CalculateMarketTiming();

      if (currentPosition == null) {
        if (LastPositionBarId == BarId) return;

        var signal = GetSignal(timing);

        if (signal == -1) {
          var volume = GetVolume(TradeType.Sell);
          var result = ExecuteMarketOrder(TradeType.Sell, Symbol, volume, Label, null, null, 1);

          if (result.IsSuccessful) {
            currentPosition = result.Position;
            LastPositionBarId = BarId;
          } else {
            Print("Error on open SELL possition.");
          }
        } else if (signal == 1) {
          var volume = GetVolume(TradeType.Buy);
          var result = ExecuteMarketOrder(TradeType.Buy, Symbol, volume, Label, null, null, 1);

          if (result.IsSuccessful) {
            currentPosition = result.Position;
            LastPositionBarId = BarId;
          } else {
            Print("Error on open BUY possition.");
          }
        }
      } else {
        var position = currentPosition;
        if (position.TradeType == TradeType.Buy && rsi.Result.LastValue >= 70) {
          ClosePosition(position);
        } else if (position.TradeType == TradeType.Sell && rsi.Result.LastValue <= 30) {
          ClosePosition(position);
        }
      }
    }

    private int GetSignal(Timing timing) {
      var buyTimings = new int[] { 1, 4, -2, -3 };
      var sellTimings = new int[] { -1, -4, 2, 3 };


      if (buyTimings.Contains(timing.Reference)
          && rsi.Result.Last(2) <= 30
          && rsi.Result.Last(1) >= 30
          && rsi.Result.Last(1) < 40
          && lmm50.Result.LastValue > lmm100.Result.LastValue) {
        return 1;
      }

      if (sellTimings.Contains(timing.Reference)
          && rsi.Result.Last(2) >= 70
          && rsi.Result.Last(1) <= 70
          && rsi.Result.Last(1) > 60
          && lmm50.Result.LastValue < lmm100.Result.LastValue) {
        return -1;
      }

      return 0;
    }

    private long GetVolume(TradeType ttype) {
      return Volume;
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