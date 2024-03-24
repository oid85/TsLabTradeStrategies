using System;
using System.Collections.Generic;
using Framework.Centaur.MathExtensions;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.AdaptivePCEr.AdaptivePCErMiddle
{
    public class AdaptivePCErMiddle_FixLot : IExternalScript
    {
        public IPosition LastActivePosition = null;

        // Объявление и инициализация параметров торговой системы
        public readonly OptimProperty Period = new OptimProperty(10, 10, 200, 1);
        
        public virtual void Execute(IContext ctx, ISecurity security) 
        {
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1.0; // Количество лотов (не бумаг !!!)           
            
            double orderPrice = 0.0;
            
            bool signalBuy = false;
            bool signalShort = false;

            // Определяем периоды каналов
            int period = Period;

            IList<double> closePrices = security.GetClosePrices(ctx);
            IList<double> highPrices = security.GetHighPrices(ctx);
            IList<double> lowPrices = security.GetLowPrices(ctx);

            // Цены для построения канала
            IList<double> analysePriceBuy = closePrices;
            IList<double> analysePriceShort = closePrices;
            IList<double> priceForChannelHighEntry = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double> priceForChannelHighExit = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double> priceForChannelLowEntry = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double> priceForChannelLowExit = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);

            // Построение каналов
            IList<double> erHighEntry = new Centaur.WealthLabIndicators.Er() { Period = period }.Execute(priceForChannelHighEntry);
            IList<double> erHighExit = new Centaur.WealthLabIndicators.Er() { Period = period }.Execute(priceForChannelHighEntry);
            IList<double> erLowEntry = new Centaur.WealthLabIndicators.Er() { Period = period }.Execute(priceForChannelHighEntry);
            IList<double> erLowExit = new Centaur.WealthLabIndicators.Er() { Period = period }.Execute(priceForChannelHighEntry);

            // Сглаживание
            int smoothPeriod = 5;
            erHighEntry = new Centaur.WealthLabIndicators.Sma() { Period = smoothPeriod }.Execute(erHighEntry);
            erHighExit = new Centaur.WealthLabIndicators.Sma() { Period = smoothPeriod }.Execute(erHighExit);
            erLowEntry = new Centaur.WealthLabIndicators.Sma() { Period = smoothPeriod }.Execute(erLowEntry);
            erLowExit = new Centaur.WealthLabIndicators.Sma() { Period = smoothPeriod }.Execute(erLowExit);

            firstValidValue = Math.Max(firstValidValue, Convert.ToInt32(Math.Floor(period * 1.1)));

            IList<double> highLevelEntry = new List<double>().InitValues(security.Bars.Count);
            IList<double> highLevelExit = new List<double>().InitValues(security.Bars.Count);
            IList<double> lowLevelEntry = new List<double>().InitValues(security.Bars.Count);
            IList<double> lowLevelExit = new List<double>().InitValues(security.Bars.Count);

            for (int i = 0; i < security.Bars.Count; i++)
            {
                int nHighEntry = period - Convert.ToInt32(Math.Floor((period - 1) * erHighEntry[i]));
                int nHighExit = period - Convert.ToInt32(Math.Floor((period - 1) * erHighExit[i]));
                int nLowEntry = period - Convert.ToInt32(Math.Floor((period - 1) * erLowEntry[i]));
                int nLowExit = period - Convert.ToInt32(Math.Floor((period - 1) * erLowExit[i]));

                double maxHighEntry = priceForChannelHighEntry[i];
                double maxHighExit = priceForChannelHighExit[i];
                double minLowEntry = priceForChannelLowEntry[i];
                double minLowExit = priceForChannelLowExit[i];

                int maxN = 0;
                maxN = Math.Max(maxN, nHighEntry);
                maxN = Math.Max(maxN, nHighExit);
                maxN = Math.Max(maxN, nLowEntry);
                maxN = Math.Max(maxN, nLowExit);

                if (i >= maxN)
                {
                    for (int j = i - nHighEntry; j < i; j++)
                        if (priceForChannelHighEntry[j] > maxHighEntry)
                            maxHighEntry = priceForChannelHighEntry[j];

                    for (int j = i - nHighExit; j < i; j++)
                        if (priceForChannelHighExit[j] > maxHighExit)
                            maxHighExit = priceForChannelHighExit[j];

                    for (int j = i - nLowEntry; j < i; j++)
                        if (priceForChannelLowEntry[j] < minLowEntry)
                            minLowEntry = priceForChannelLowEntry[j];

                    for (int j = i - nLowExit; j < i; j++)
                        if (priceForChannelLowExit[j] < minLowExit)
                            minLowExit = priceForChannelLowExit[j];
                }

                highLevelEntry[i] = maxHighEntry;
                highLevelExit[i] = maxHighExit;
                lowLevelEntry[i] = minLowEntry;
                lowLevelExit[i] = minLowExit;
            }

            // Отрисовка индикаторов
            IGraphPane pricePane = ctx.First;
            pricePane.AddList(@"Верхняя граница для входа", highLevelEntry, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"Нижняя граница для входа", lowLevelEntry, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"Верхняя граница для выхода", highLevelExit, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"Нижняя граница для выхода", lowLevelExit, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);

            // Сглаживание
            smoothPeriod = 5;
            highLevelEntry = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(highLevelEntry);
            lowLevelEntry = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(lowLevelEntry);
            highLevelExit = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(highLevelExit);
            lowLevelExit = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(lowLevelExit);

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
                signalBuy = analysePriceBuy[bar] > highLevelEntry[bar];
                signalShort = analysePriceShort[bar] < lowLevelEntry[bar];
                
                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                // Задаем цену для заявки
                orderPrice = security.Bars[bar].Close;

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
                    double startTrailingStop = (lowLevelExit[entryBar] + highLevelExit[entryBar]) / 2.0;
                    double curTrailingStop = (lowLevelExit[bar] + highLevelExit[bar]) / 2.0;

                    if (LastActivePosition.IsLong)
                    {
                        trailingStop = bar == entryBar
                            ? startTrailingStop
                            : System.Math.Max(trailingStop, curTrailingStop);
                        LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"LX");
                    }
                    else if (LastActivePosition.IsShort)
                    {
                        trailingStop = bar == entryBar
                            ? startTrailingStop
                            : System.Math.Min(trailingStop, curTrailingStop);
                        LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"SX");
                    }
                }
            }
        }
    }
}
