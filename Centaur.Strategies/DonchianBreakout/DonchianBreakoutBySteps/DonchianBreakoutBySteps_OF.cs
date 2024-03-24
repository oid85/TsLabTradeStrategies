using System;
using System.Collections.Generic;
using Centaur.MoneyManagements;
using Centaur.Tools;
using Framework.Centaur.MathExtensions;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;
using Color = System.Drawing.Color;

namespace Centaur.Strategies.DonchianBreakout.DonchianBreakoutBySteps
{
	public class DonchianBreakoutBySteps_OF : IExternalScript
	{
        public IPosition LastActivePosition = null;

        public OptimProperty Period = new OptimProperty(10, 5, 200, 5);        
        public OptimProperty Steps = new OptimProperty(1, 1, 50, 1);

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

            int period = Period;            
            int steps = Steps;

            double currentStep = 0; // Текущее значение шага
            double trailingStop = 0;

            // Цены для построения канала
            IList<double> priceForChannel = highPrices.Add(lowPrices).Add(closePrices).Add(closePrices).DivConst(4.0);

            // Трейлинг
            IList<double> trailing = new List<double>();
            trailing = trailing.InitValues(security.Bars.Count);

            // Построение каналов
            IList<double> up = ctx.GetData(@"up", new[] { period.ToString() }, () => Series.Highest(priceForChannel, period));
            IList<double> down = ctx.GetData(@"down", new[] { period.ToString() }, () => Series.Lowest(priceForChannel, period));

            // Сглаживание
            int smoothPeriod = 5;
            up = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(up);
            down = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(down);

            // Сдвигаем на 1 свечу вправо
            up = Series.Shift(up, 1);
            down = Series.Shift(down, 1);

            firstValidValue = Math.Max(firstValidValue, period);

            // Учтем возможность неполных свечей, которые появятся на пересчетах отличных от ИНТЕРВАЛ
            // нельзя использовать неполную свечку в расчетах, она всегда изменяется
            var count = ctx.BarsCount;
            if (!ctx.IsLastBarClosed)
                count--;

            for (int bar = firstValidValue; bar < count; bar++)
            {
                // Правило входа
                signalBuy = closePrices[bar] > up[bar];
                signalShort = closePrices[bar] < down[bar];

                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                // Задаем цену для заявки
                double orderPrice = closePrices[bar];

                // Управление капиталом
                double money = security.CurrentBalance(bar);
                var mm = new OptimalF(money, optimalF, orderPrice);
                lots = mm.GetShares(lotSize);

                if (LastActivePosition == null)
                {
                    trailingStop = (up[bar] + down[bar]) / 2.0;

                    if (signalBuy)
                        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, @"LN");

                    if (signalShort)
                        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, @"SN");
                }
                else
                {
                    if (LastActivePosition.IsLong)
                    {
                        if (closePrices[bar] < trailingStop)
                        {
                            LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"LX");
                        }
                        else // Пересчитываем трейлинг стоп
                        {
                            // Если обновился максимум
                            if (highPrices[bar] > highPrices[bar - 1])
                                currentStep = (up[bar - 1] - trailingStop) / steps;

                            trailingStop = Math.Max(trailingStop, trailingStop + currentStep);
                            trailing[bar] = trailingStop;
                        }
                    }

                    else if (LastActivePosition.IsShort)
                    {
                        if (closePrices[bar] > trailingStop)
                        {
                            LastActivePosition.CloseAtStop(bar + 1, trailingStop, @"SX");
                        }
                        else // Пересчитываем трейлинг стоп
                        {
                            // Если обновился минимум
                            if (lowPrices[bar] < lowPrices[bar - 1])
                                currentStep = (trailingStop - down[bar - 1]) / steps;

                            trailingStop = Math.Min(trailingStop, trailingStop - currentStep);
                            trailing[bar] = trailingStop;
                        }
                    }
                }
            }

            // Отрисовка индикаторов
            IGraphPane pricePane = ctx.First;
            pricePane.AddList(@"up", up, ListStyles.LINE, new TSLab.Script.Color(Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"down", down, ListStyles.LINE, new TSLab.Script.Color(Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"trailing", trailing, ListStyles.LINE_WO_ZERO, new TSLab.Script.Color(Color.DarkGreen.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
        }
	}
}

