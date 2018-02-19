using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Indicators {
    [Indicator(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HeikenAshiOscillator : Indicator {
        private IndicatorDataSeries _haOpen;
        private IndicatorDataSeries _haClose;

        [Output("Result", Color = Colors.Orange)]
        public IndicatorDataSeries Result { get; set; }

        protected override void Initialize() {
            _haOpen = CreateDataSeries();
            _haClose = CreateDataSeries();
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

            Result[index] = haOpen > haClose ? -1 : 1;

            _haOpen[index] = haOpen;
            _haClose[index] = haClose;
        }
    }
}
