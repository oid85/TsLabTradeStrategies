using System;
using System.Collections.Generic;
using Centaur.MoneyManagements;
using Centaur.Tools;
using Framework.Centaur.MathExtensions;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.DoubleBollingerBands.DoubleBollingerBandsMiddle
{
    public class DoubleBollingerBandsMiddle_OF : IExternalScript
    {
        public IPosition LastActivePosition = null;

        public readonly OptimProperty Period = new OptimProperty(10, 10, 200, 5);
        public readonly OptimProperty Mult = new OptimProperty(1, 1, 3, 0.1);
        public readonly OptimProperty StdDev = new OptimProperty(2.0, 1.5, 3, 0.1);

		public readonly OptimProperty OptimalF = new OptimProperty(1, 1, 5, 0.1);

        public virtual void Execute(IContext ctx, ISecurity security)
        {
            // Плечо
            double optimalF = OptimalF;

            // Количество бумаг в лоте
            int lotSize = 1;
			
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1; // Количество лотов (не бумаг !!!)

            IList<double> closePrices = security.GetClosePrices(ctx);
            IList<double> highPrices = security.GetHighPrices(ctx);
            IList<double> lowPrices = security.GetLowPrices(ctx);

            bool signalBuy = false;
            bool signalShort = false;

            int periodSmall = Period;
            double mult = Mult;
            double stdDev = StdDev;
            int periodBig = Convert.ToInt32(Math.Floor(periodSmall * mult));

            // Цены для построения канала
            IList<double> priceForChannel = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);

            // Построение каналов
            IList<double> highLevelSmall = new Centaur.WealthLabIndicators.BBandUpper() { Period = periodSmall, StdDev = stdDev }.Execute(priceForChannel);
            IList<double> lowLevelSmall = new Centaur.WealthLabIndicators.BBandLower() { Period = periodSmall, StdDev = stdDev }.Execute(priceForChannel);
            IList<double> highLevelBig = new Centaur.WealthLabIndicators.BBandUpper() { Period = periodBig, StdDev = stdDev }.Execute(priceForChannel);
            IList<double> lowLevelBig = new Centaur.WealthLabIndicators.BBandLower() { Period = periodBig, StdDev = stdDev }.Execute(priceForChannel);

            firstValidValue = Math.Max(firstValidValue, periodSmall);
            firstValidValue = Math.Max(firstValidValue, periodBig);

            // Отрисовка индикаторов
            IGraphPane pricePane = ctx.First;
            pricePane.AddList(@"highLevelSmall", highLevelSmall, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"lowLevelSmall", lowLevelSmall, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"highLevelBig", highLevelBig, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"lowLevelBig", lowLevelBig, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);

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
                signalBuy = closePrices[bar] > highLevelSmall[bar];
                signalBuy &= closePrices[bar] > (highLevelBig[bar] + lowLevelBig[bar]) / 2.0;

                signalShort = closePrices[bar] < lowLevelSmall[bar];
                signalShort &= closePrices[bar] < (highLevelBig[bar] + lowLevelBig[bar]) / 2.0;

                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                // Задаем цену для заявки
                double orderPrice = security.Bars[bar].Close;

                // Управление капиталом
                double money = security.CurrentBalance(bar);
                var mm = new OptimalF(money, optimalF, orderPrice);
                lots = mm.GetShares(lotSize);

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
                    double startTrailingStop = (highLevelSmall[entryBar] + lowLevelSmall[entryBar]) / 2.0;
                    double curTrailingStop = (highLevelSmall[bar] + lowLevelSmall[bar]) / 2.0;

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
