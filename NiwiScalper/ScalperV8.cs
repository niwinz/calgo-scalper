using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo {
  [Robot(TimeZone = TimeZones.WEuropeStandardTime, AccessRights = AccessRights.None)]
  public class Scalper80 : Robot {
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

    //[Parameter("Stop Loss PIPs", DefaultValue = 60)]
    //public int StopLossPIPS { get; set; }

    //[Parameter("Take Profit PIPS", DefaultValue = 8)]
    //public int TakeProfitPIPS { get; set; }

    [Parameter("Risked %", DefaultValue = 1, Step = 0.5)]
    public double Risk { get; set; }

    //[Parameter("Volume", DefaultValue = 10000)]
    //public int Volume { get; set; }

    [Parameter("Label", DefaultValue = "scalperv8")]
    public String Label { get; set; }

    private ExponentialMovingAverage lmm50;
    private SimpleMovingAverage rmm200;

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private AverageTrueRange atr;
    private MarketSeries rseries;

    private long barId;
    private long pendingOrderBarId;
    private PendingOrder pendingOrder;
    private Position currentPosition;

    protected override void OnStart() {
      barId = MarketSeries.Close.Count;
      pendingOrderBarId = barId;

      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, TimeFrame.Day2);

      lmm50 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 50);
      rmm200 = Indicators.SimpleMovingAverage(rseries.Close, 200);

      lmacd = Indicators.MacdCrossOver(MarketSeries.Close, 26, 12, 9);
      rmacd = Indicators.MacdCrossOver(rseries.Close, 26, 12, 9);

      atr = Indicators.AverageTrueRange(MarketSeries, 14, MovingAverageType.Simple);
      Positions.Closed += PositionsOnClosed;
      Positions.Opened += PositionsOnOpened;
    }

    private void PositionsOnOpened(PositionOpenedEventArgs obj) {
      Print("Position opened.");
      var pos = obj.Position;
      if (pos.Label == Label) {
        currentPosition = pos;
        pendingOrder = null;
      }
    }

    private void PositionsOnClosed(PositionClosedEventArgs obj) {
      var pos = obj.Position;
      if (currentPosition != null && currentPosition.Id == pos.Id) {
        currentPosition = null;
      }
    }

    private int GetAtr() {
      // TODO: Determine the pip position from Symbol
      return (int)Math.Round(atr.Result.LastValue * 10000);
    }

    private int CalculateStopLoss(int atr) {
      //return (int)Math.Round(atr * StopLossATRS);
      return 40;
    }

    private int CalculateTakeProfit(int atr) {
      //return (int)Math.Round(atr * TakeProfitATRS);
      return 80;
    }

    protected override void OnBar() {
      barId = MarketSeries.Close.Count;
      var timing = CalculateTiming();
      var atr = GetAtr();

      // Do nothing if atr is not good
      //if (atr < 20) return;

      if (currentPosition != null) {
        return;
      }
         
      if (currentPosition == null) {
        int? signal = GetSignal(timing);

        if (signal == null) return;

        var takeProfit = CalculateTakeProfit(atr);
        var stopLoss = CalculateStopLoss(atr);

        if (signal > 0) {
          var target = MarketSeries.High.Last(1);
          var volume = GetVolume(target, stopLoss);
          DateTime expiration = Server.Time.AddHours(8).Date;
          var result = PlaceStopOrder(TradeType.Buy, Symbol, volume, target, Label, stopLoss, takeProfit, expiration);
          if (result.IsSuccessful) {
            Print("Opening buy stop order");
            pendingOrder = result.PendingOrder;
            pendingOrderBarId = barId;
          } else {
            Print("Error on open buy stop order at {0}.", target);
          }
        } else {
          var target = MarketSeries.Low.Last(1);
          var volume = GetVolume(target, stopLoss);
          Print("Opening sell stop order");
          DateTime expiration = Server.Time.AddHours(8).Date;
          var result = PlaceStopOrder(TradeType.Sell, Symbol, volume, target, Label, stopLoss, takeProfit, expiration);
          if (result.IsSuccessful) {
            pendingOrder = result.PendingOrder;
            pendingOrderBarId = barId;
          } else {
            Print("Error on open sell stop order at {0}.", target);
          }
        }
      }
    }

    private int? GetSignal(Int32 timing) {
      var close = MarketSeries.Close;
      var high = MarketSeries.High;
      var low = MarketSeries.Low;

      if (close.Last(1) > lmm50.Result.Last(1)
          && close.Last(2) < lmm50.Result.Last(2)
          && close.Last(3) < lmm50.Result.Last(3)) {
        return 1;
      }

      if (close.Last(1) < lmm50.Result.Last(1)
          && close.Last(2) > lmm50.Result.Last(2)
          && close.Last(3) > lmm50.Result.Last(3)) {
        return -1;
      }

      return null;
    }

    private long GetVolume(double price, int stopLoss) {
      var riskedAmount = Account.Balance * (Risk / 100);
      var volume = 1 / ((price * Symbol.PipSize * stopLoss) / riskedAmount);
      return Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);
    }

    //private bool IsOnTraidingTime() {
    //  return Server.Time.Hour >= StartTime && Server.Time.Hour <= StopTime;
    //}

    // -------------------------------------------
    // ----  Market Timing
    // -------------------------------------------

    private int CalculateTiming() {

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

    private bool IsTrendUp(MarketSeries series, SimpleMovingAverage wma) {
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