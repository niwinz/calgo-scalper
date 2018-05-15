using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;



// Bot that uses MACD for general tendence following and Sochastic for local
// entry and exit points. For now show pretty good results with default
// parameters from 01/09/2017 to 02/04/2018 on AUDUSD AND USDCAD


namespace cAlgo {
  [Robot(TimeZone = TimeZones.WEuropeStandardTime, AccessRights = AccessRights.None)]
  public class Scalpe70 : Robot {
    [Parameter("Volume", DefaultValue = 50000)]
    public int Volume { get; set; }

    [Parameter("Label", DefaultValue = "scalperv7")]
    public String Label { get; set; }

    private WeightedMovingAverage lmm50;
    private WeightedMovingAverage lmm100;
    private WeightedMovingAverage lmm200;
    private WeightedMovingAverage rmm200;

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private RelativeStrengthIndex rsi;
    private StochasticOscillator sto;

    private MarketSeries rseries;

    private long BarId;
    private long LastPositionBarId;
    private Position currentPosition;

    protected override void OnStart() {
      BarId = MarketSeries.Close.Count;
      LastPositionBarId = BarId;
      
      var reftf = GetReferenceTimeframe(MarketSeries.TimeFrame);
      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, reftf);

      lmm50 = Indicators.WeightedMovingAverage(MarketSeries.Close, 50);
      lmm100 = Indicators.WeightedMovingAverage(MarketSeries.Close, 100);
      lmm200 = Indicators.WeightedMovingAverage(MarketSeries.Close, 200);
      rmm200 = Indicators.WeightedMovingAverage(rseries.Close, 200);

      lmacd = Indicators.MacdCrossOver(MarketSeries.Close, 26, 12, 9);
      rmacd = Indicators.MacdCrossOver(rseries.Close, 26, 12, 9);

      rsi = Indicators.RelativeStrengthIndex(MarketSeries.Close, 6);
      sto = Indicators.StochasticOscillator(MarketSeries, 14, 3, 3, MovingAverageType.Simple);

      Positions.Closed += PositionsOnClosed;
      currentPosition = Positions.Find(Label, Symbol);
    }

    private void PositionsOnClosed(PositionClosedEventArgs obj) {
      var pos = obj.Position;
      if (currentPosition != null && currentPosition.Id == pos.Id) {
        currentPosition = null;
      }
    }

    protected override void OnTick() {
      BarId = MarketSeries.Close.Count;
      var timing = CalculateReferenceTiming();

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
        if (position.TradeType == TradeType.Buy) {
          // Close position when we have arrived to the target.
          if (sto.PercentK.LastValue >= 80) {
            ClosePosition(position);
            currentPosition = null;
            return;
          }

          // Close possition if market direction is changed.
          if (lmm50.Result.HasCrossedBelow(lmm100.Result, 2)
              && lmm50.Result.Last(1) < lmm100.Result.Last(1)) {
            ClosePosition(position);
            currentPosition = null;
            return;
          }
        } else {
          // Close position when we have arrived to the target.
          if (sto.PercentK.LastValue <= 20) {
            ClosePosition(position);
            currentPosition = null;
            return;
          }

          // Close possition if market direction is changed.
          if (lmm50.Result.HasCrossedAbove(lmm100.Result, 2)
              && lmm50.Result.Last(1) > lmm100.Result.Last(1)) {
            ClosePosition(position);
            currentPosition = null;
            return;
          }
        }
      }
    }

    private int GetSignal(Int32 timing) {
      var buyTimings = new int[] { 1, 4, -2, -3 };
      var sellTimings = new int[] { -1, -4, 2, 3 };

      if (buyTimings.Contains(timing)
          && sto.PercentK.HasCrossedAbove(sto.PercentD, 2)
          && sto.PercentK.Last(2) <= 20
          && sto.PercentD.Last(2) <= 20
          && sto.PercentK.Last(1) <= 20
          && sto.PercentD.Last(1) <= 20
          && sto.PercentK.Last(1) > sto.PercentD.Last(1)
          && sto.PercentK.LastValue > sto.PercentD.LastValue
          && lmm50.Result.LastValue > lmm100.Result.LastValue) {
        return 1;
      }

      if (sellTimings.Contains(timing)
          && sto.PercentK.HasCrossedBelow(sto.PercentD, 2)
          && sto.PercentK.Last(2) >= 80
          && sto.PercentD.Last(2) >= 80
          && sto.PercentK.Last(1) >= 80
          && sto.PercentD.Last(1) >= 80
          && sto.PercentK.Last(1) < sto.PercentD.Last(1)
          && sto.PercentK.LastValue < sto.PercentD.LastValue
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
      } else if (tf == TimeFrame.Minute15) {
        return TimeFrame.Daily;
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