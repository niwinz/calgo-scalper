﻿using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Indicators {
  [Indicator(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class HeikenAshiOscillator : Indicator {
    private IndicatorDataSeries _haOpen;
    private IndicatorDataSeries _haClose;

    [Output("Result Open", Color = Colors.Orange, PlotType = PlotType.Line)]
    public IndicatorDataSeries ResultOpen { get; set; }

    [Output("Result Close", Color = Colors.Blue, PlotType = PlotType.Line)]
    public IndicatorDataSeries ResultClose { get; set; }

    [Parameter("Periods", DefaultValue = 2)]
    public int Periods { get; set; }

    protected override void Initialize() {
      _haOpen = CreateDataSeries();
      _haClose = CreateDataSeries();
    }

    public double CalculateMedia(int index, int periods, IndicatorDataSeries source) {
      if (source.Count < periods) return 0;

      double sum = 0.0;

      for (int i = index - periods + 1; i <= index; i++) {
        sum += source[i];
      }

      return sum / periods;
    }

    public override void Calculate(int index) {
      var open = MarketSeries.Open[index];
      var high = MarketSeries.High[index];
      var low = MarketSeries.Low[index];
      var close = MarketSeries.Close[index];

      var haClose = (open + high + low + close) / 4;
      double haOpen;
      if (index > 0)
        haOpen = (_haOpen[index - 1] + _haClose[index - 1]) / 2;
      else
        haOpen = (open + close) / 2;

      var haHigh = Math.Max(Math.Max(high, haOpen), haClose);
      var haLow = Math.Min(Math.Min(low, haOpen), haClose);

      _haOpen[index] = haOpen;
      _haClose[index] = haClose;

      ResultOpen[index] = CalculateMedia(index, Periods, _haOpen);
      ResultClose[index] = CalculateMedia(index, Periods, _haClose);
    }
  }
}
