
using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

// EURUSD scalper, aprox 14% rentability for year 2017


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

    //[Parameter("Stop Loss PIPs", DefaultValue = 30)]
    //public double StopLossPIPS { get; set; }

    //[Parameter("Take Profit PIPS", DefaultValue = 10)]
    //public double TakeProfitPIPS { get; set; }

    [Parameter("Stop Loss ATR's", DefaultValue = 1, Step = 0.5)]
    public double StopLossATRS { get; set; }

    [Parameter("Take Profit ATR's", DefaultValue = 1, Step = 0.5)]
    public double TakeProfitATRS { get; set; }

    [Parameter("Minimum ATR", DefaultValue = 4, Step = 1)]
    public int MinimumAtr { get; set; }

    [Parameter("Risked %", DefaultValue = 1, Step = 0.5)]
    public double Risk { get; set; }

    //[Parameter("Start Hour", DefaultValue = 2)]
    //public int StartTime { get; set; }

    //[Parameter("Stop Hour", DefaultValue = 22)]
    //public int StopTime { get; set; }

    private StochasticOscillator sto;
    private AverageTrueRange atr;

    private ExponentialMovingAverage ema10;
    private ExponentialMovingAverage ema3;

    //private WeightedMovingAverage lmm150;
    //private WeightedMovingAverage lmm300;
    //private WeightedMovingAverage rmm150;
    //private WeightedMovingAverage rmm300;

    private MacdCrossOver lmacd;
    private MacdCrossOver rmacd;

    private long losing = 1;

    private Position currentPosition = null;
    private MarketSeries rseries;

    protected override void OnStart() {
      var reftf = GetReferenceTimeframe(MarketSeries.TimeFrame);
      rseries = MarketData.GetSeries(MarketSeries.SymbolCode, reftf);

      atr = Indicators.AverageTrueRange(MarketSeries, 14, MovingAverageType.Exponential);
      sto = Indicators.StochasticOscillator(MarketSeries, 13, 9, 4, MovingAverageType.Simple);

      ema10 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 10);
      ema3 = Indicators.ExponentialMovingAverage(MarketSeries.Close, 3);

      //lmm150 = Indicators.WeightedMovingAverage(MarketSeries.Close, 150);
      //lmm300 = Indicators.WeightedMovingAverage(MarketSeries.Close, 300);
      //rmm150 = Indicators.WeightedMovingAverage(rseries.Close, 150);
      //rmm300 = Indicators.WeightedMovingAverage(rseries.Close, 300);

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

    private int GetAtrInPips() {
      // TODO: Determine the pip position from Symbol
      return (int)Math.Round(atr.Result.LastValue * 10000);
    }

    private int CalculateStopLoss(int atr) {
      return (int)Math.Round(atr * StopLossATRS);
    }

    private int CalculateTakeProfit(int atr) {
      return (int)Math.Round(atr * TakeProfitATRS);
    }

    protected override void OnBar() {
      if (currentPosition == null) {
        //if (!IsOnTraidingTime()) return;

        var signal = GetSignal();
        var atr = GetAtrInPips();

        var stopLoss = CalculateStopLoss(atr);
        var takeProfit = CalculateTakeProfit(atr);

        var label = String.Format("scalper,t={0}", CalculateMarketTiming().ToString());

        // Do nothing if atr is too small
        if (atr < MinimumAtr) return;

        if (signal == -1) {
          var volume = GetVolume(TradeType.Sell, stopLoss);
          var result = ExecuteMarketOrder(TradeType.Sell, Symbol, volume, label, stopLoss, null, 1);

          if (result.IsSuccessful) {
            currentPosition = result.Position;
          } else {
            Print("Error on open SELL possition.");
          }
        } else if (signal == 1) {
          var volume = GetVolume(TradeType.Buy, stopLoss);
          var result = ExecuteMarketOrder(TradeType.Buy, Symbol, volume, label, stopLoss, null, 1);

          if (result.IsSuccessful) {
            currentPosition = result.Position;
          } else {
            Print("Error on open BUY possition.");
          }
        }
      } else {
        if (currentPosition.TradeType == TradeType.Buy) {
          if (lmacd.MACD.HasCrossedBelow(lmacd.Signal, 1)) {
            ClosePosition(currentPosition);
            currentPosition = null;
          }
        } else {
          if (lmacd.MACD.HasCrossedAbove(lmacd.Signal, 1)) {
            ClosePosition(currentPosition);
            currentPosition = null;
          }
        }

      }
    }

    public int GetSignal() {
      var timing = CalculateMarketTiming();

      if (timing.Reference == 1
          && lmacd.MACD.HasCrossedAbove(lmacd.Signal, 0)
          && lmacd.MACD.LastValue < -0.00025
          && lmacd.Signal.LastValue < -0.00025) {
        return 1;
      } else if (timing.Reference == -1
                 && lmacd.MACD.HasCrossedBelow(lmacd.Signal, 0)
                 && lmacd.MACD.LastValue > 0.00025
                 && lmacd.Signal.LastValue > 0.00025) {
        return -1;
      }

      return 0;
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

    private long GetVolume(TradeType ttype, int stopLoss) {
      var riskedAmount = Account.Balance * (Risk / 100);
      var price = ttype == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
      var volume = 1 / ((price * Symbol.PipSize * stopLoss) / riskedAmount);
      return Symbol.NormalizeVolume(Symbol.QuantityToVolume(volume / 100000), RoundingMode.ToNearest);
    }


    //private bool IsOnTraidingTime() {
    //  return Server.Time.Hour >= StartTime && Server.Time.Hour <= StopTime;
    //}

    // -------------------------------------------
    // ----  Market Timing
    // -------------------------------------------

    private Timing CalculateMarketTiming() {
      int local = 0;
      int reference = 0;

      if (lmacd.Histogram.LastValue > 0) {
        local = 1;
      } else if (lmacd.Histogram.LastValue < 0) {
        local = -1;
      }

      if (rmacd.Histogram.LastValue > 0) {
        reference = 1;
      } else if (rmacd.Histogram.LastValue < 0) {
        reference = -1;
      }

      return new Timing(reference, local);
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
  }
}