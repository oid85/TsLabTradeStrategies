using System;
using System.Collections.Generic;
using Framework.Centaur.MathExtensions;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.DonchianBreakout.DonchianBreakoutMiddleSmaCA
{
    public class DonchianBreakoutMiddleSmaCA_FixLot : IExternalScript
    {
        public IPosition LastActivePosition = null;

        public readonly OptimProperty Period = new OptimProperty(10, 10, 200, 5);
        public readonly OptimProperty PeriodSma = new OptimProperty(10, 10, 200, 5);
        public readonly OptimProperty CandlesAgo = new OptimProperty(10, 1, 20, 1);

        public virtual void Execute(IContext ctx, ISecurity security)
        {
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1; // Количество лотов (не бумаг !!!)

            IList<double> closePrices = security.GetClosePrices(ctx);
            IList<double> highPrices = security.GetHighPrices(ctx);
            IList<double> lowPrices = security.GetLowPrices(ctx);

            bool signalBuy = false;
            bool signalShort = false;

            // Определяем периоды каналов
            int periodHighEntry = Period;
            int periodLowEntry = Period;
            int periodHighExit = Period;
            int periodLowExit = Period;

            // Цены для построения канала
            IList<double>  priceForChannelHighEntry = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double>  priceForChannelHighExit = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double>  priceForChannelLowEntry = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double>  priceForChannelLowExit = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);

            // Построение каналов
            IList<double>  highLevelEntry = ctx.GetData(@"highLevelEntry", new[] { periodHighEntry.ToString() }, () => Series.Highest(priceForChannelHighEntry, periodHighEntry));
            IList<double>  lowLevelEntry = ctx.GetData(@"lowLevelEntry", new[] { periodLowEntry.ToString() }, () => Series.Lowest(priceForChannelHighExit, periodLowEntry));
            IList<double>  highLevelExit = ctx.GetData(@"highLevelExit", new[] { periodHighExit.ToString() }, () => Series.Highest(priceForChannelLowEntry, periodHighExit));
            IList<double>  lowLevelExit = ctx.GetData(@"lowLevelExit", new[] { periodLowExit.ToString() }, () => Series.Lowest(priceForChannelLowExit, periodLowExit));

            // Сглаживание
            int smoothPeriod = 5;
            highLevelEntry = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(highLevelEntry);
            lowLevelEntry = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(lowLevelEntry);
            highLevelExit = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(highLevelExit);
            lowLevelExit = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(lowLevelExit);

            // Сдвигаем на 1 свечу вправо
            highLevelEntry = Series.Shift(highLevelEntry, 1);
            lowLevelEntry = Series.Shift(lowLevelEntry, 1);
            highLevelExit = Series.Shift(highLevelExit, 1);
            lowLevelExit = Series.Shift(lowLevelExit, 1);

            firstValidValue = Math.Max(firstValidValue, periodHighEntry);
            firstValidValue = Math.Max(firstValidValue, periodLowEntry);
            firstValidValue = Math.Max(firstValidValue, periodHighExit);
            firstValidValue = Math.Max(firstValidValue, periodLowExit);

            // Параметры для фильтра
            int periodSma = PeriodSma;
            int candlesAgo = CandlesAgo;

            // SMA
            var smaObject = new WealthLabIndicators.Sma(){ Period = periodSma };
            IList<double> sma = smaObject.Execute(closePrices);

            firstValidValue = Math.Max(firstValidValue, periodSma);
            firstValidValue = Math.Max(firstValidValue, candlesAgo);

            // Отрисовка индикаторов
            IGraphPane pricePane = ctx.First;
            pricePane.AddList(@"highLevelEntry", highLevelEntry, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"highLevelExit", highLevelExit, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"lowLevelEntry", lowLevelEntry, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"lowLevelExit", lowLevelExit, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);

            // Переменные для обслуживания позиции
            double trailingStop = 0.0;

            // Учтем возможность неполных свечей, которые появятся на пересчетах отличных от ИНТЕРВАЛ
            // нельзя использовать неполную свечку в расчетах, она всегда изменяется
            var count = ctx.BarsCount;
            if (!ctx.IsLastBarClosed)
                count--;

            for (int bar = firstValidValue; bar < count; bar++) // Пробегаемся по всем свечкам
            {
                // Правило входа
                signalBuy = closePrices[bar] > highLevelEntry[bar];
                signalBuy &= sma[bar] > sma[bar - candlesAgo];

                signalShort = closePrices[bar] < lowLevelEntry[bar];
                signalShort &= sma[bar] < sma[bar - candlesAgo];

                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                // Задаем цену для заявки
                double orderPrice = security.Bars[bar].Close;

                // Сопровождение позиции
                if (LastActivePosition == null)
                {
                    if (signalBuy)
                    {
                        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, @"LN");
                    }

                    if (signalShort)
                    {
                        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, @"SN");
                    }
                }
                else
                {
                    int entryBar = LastActivePosition.EntryBarNum;
                    double startTrailingStop = (highLevelExit[entryBar] + lowLevelExit[entryBar]) / 2.0;
                    double curTrailingStop = (highLevelExit[bar] + lowLevelExit[bar]) / 2.0;

                    if (LastActivePosition.IsLong)
                    {
                        trailingStop = bar == entryBar
                            ? startTrailingStop
                            : Math.Max(trailingStop, curTrailingStop);

                        LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"LX");
                    }

                    else if (LastActivePosition.IsShort)
                    {
                        trailingStop = bar == entryBar
                            ? startTrailingStop
                            : Math.Min(trailingStop, curTrailingStop);

                        LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"SX");
                    }
                }
            }
        }
    }
}
