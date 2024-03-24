using System.Collections.Generic;
using Framework.Centaur.MathExtensions;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.AdaptivePCAdx.AdaptivePCAdxMiddle
{
    public class AdaptivePCAdxMiddle_FixLot : IExternalScript
    {
        public IPosition LastActivePosition = null;
    
        public readonly OptimProperty Period = new OptimProperty(10, 10, 200, 5);
        public readonly OptimProperty PeriodAdx = new OptimProperty(10, 10, 50, 5);

        public virtual void Execute(IContext ctx, ISecurity security)
        {
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1.0; // Количество лотов (не бумаг !!!)

            IList<double> closePrices = security.GetClosePrices(ctx);
            IList<double> highPrices = security.GetHighPrices(ctx);
            IList<double> lowPrices = security.GetLowPrices(ctx);

            double orderPrice = 0.0;
            double riskStopLevel = 0.0;
            
            bool signalBuy = false;
            bool signalShort = false;

            // Определяем периоды каналов            
            int period = Period;

            // Цены для построения канала
            IList<double> priceForChannelHighEntry = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double> priceForChannelHighExit = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double> priceForChannelLowEntry = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);
            IList<double> priceForChannelLowExit = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);

            // Построение каналов            
            int periodAdx = PeriodAdx;
            IList<double> adx = new Centaur.WealthLabIndicators.Adx() { Period = periodAdx }.Execute(security);

            firstValidValue = System.Math.Max(firstValidValue, (int)System.Math.Floor(period * 1.1));
            firstValidValue = System.Math.Max(firstValidValue, (int)System.Math.Floor(periodAdx * 1.1));

            IList<double> highLevelEntry = new List<double>().InitValues(security.Bars.Count);
            IList<double> highLevelExit = new List<double>().InitValues(security.Bars.Count);
            IList<double> lowLevelEntry = new List<double>().InitValues(security.Bars.Count);
            IList<double> lowLevelExit = new List<double>().InitValues(security.Bars.Count);

            for (int i = firstValidValue; i < security.Bars.Count; i++)
            {
                int nHighEntry = (int)System.Math.Floor(period * ((100.0 - adx[i]) / 100.0));
                int nHighExit = (int)System.Math.Floor(period * ((100.0 - adx[i]) / 100.0));
                int nLowEntry = (int)System.Math.Floor(period * ((100.0 - adx[i]) / 100.0));
                int nLowExit = (int)System.Math.Floor(period * ((100.0 - adx[i]) / 100.0));

                double maxHighEntry = priceForChannelHighEntry[i];
                double maxHighExit = priceForChannelHighExit[i];
                double minLowEntry = priceForChannelLowEntry[i];
                double minLowExit = priceForChannelLowExit[i];

                for (int j = i - nHighEntry; j < i; j++) if (priceForChannelHighEntry[j] > maxHighEntry) maxHighEntry = priceForChannelHighEntry[j];
                for (int j = i - nHighExit; j < i; j++) if (priceForChannelHighExit[j] > maxHighExit) maxHighExit = priceForChannelHighExit[j];
                for (int j = i - nLowEntry; j < i; j++) if (priceForChannelLowEntry[j] < minLowEntry) minLowEntry = priceForChannelLowEntry[j];
                for (int j = i - nLowExit; j < i; j++) if (priceForChannelLowExit[j] < minLowExit) minLowExit = priceForChannelLowExit[j];

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
            int smoothPeriod = 5;
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
                signalBuy = closePrices[bar] > highLevelEntry[bar];
                signalShort = closePrices[bar] < lowLevelEntry[bar];
                
                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                // Задаем цену для заявки
                orderPrice = security.Bars[bar].Close;

                // Сопровождение позиции                
                if (LastActivePosition != null)
                {
                    int entryBar = LastActivePosition.EntryBarNum;
                    double startTrailingStop = (lowLevelExit[entryBar] + highLevelExit[entryBar]) / 2.0;
                    double curTrailingStop = (lowLevelExit[bar] + highLevelExit[bar]) / 2.0;

                    if (LastActivePosition.IsLong)
                    {
                        trailingStop = bar == entryBar ? startTrailingStop : System.Math.Max(trailingStop, curTrailingStop);
                        LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"LX");
                    }
                    else if (LastActivePosition.IsShort)
                    {
                        trailingStop = bar == entryBar ? startTrailingStop : System.Math.Min(trailingStop, curTrailingStop);
                        LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"SX");
                    }
                }
                else
                {
                    if (signalBuy)
                    {
                        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, @"LN");
                    }
                    else if (signalShort)
                    {
                        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, @"SN");
                    }
                }               
            }
        }
    }
}
