/*
На основе индикатора NRTR - Nick Rypock Trailing Reverse
*/

using System;
using System.Collections.Generic;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.Nrtr.NrtrClassic
{
	public class NrtrClassic_FixLot : IExternalScript
	{
        public IPosition LastActivePosition = null;

        public OptimProperty PeriodNrtr = new OptimProperty(10, 5, 100, 5);
        public OptimProperty Mult = new OptimProperty(0.1, 0.1, 3, 0.1);

        public virtual void Execute(IContext ctx, ISecurity security)
		{
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1.0; // Количество лотов (не бумаг !!!)

            bool signalBuy;
            bool signalSell;
            bool signalShort;
            bool signalCover;

            double orderPrice = 0.0;

            int periodNrtr = PeriodNrtr;
			double multiple = Mult;

            // Индикаторы
            // NRTR
            var nrtrObject = new WealthLabIndicators.Nrtr() { Period = periodNrtr, Mult = multiple};
            IList<double> nrtr = nrtrObject.Execute(security);
            firstValidValue = Math.Max(firstValidValue, periodNrtr * 3);

            // Учтем возможность неполных свечей, которые появятся на пересчетах отличных от ИНТЕРВАЛ
            // нельзя использовать неполную свечку в расчетах, она всегда изменяется
            var count = ctx.BarsCount;
            if (!ctx.IsLastBarClosed)
                count--;

            for (int bar = firstValidValue; bar < count; bar++)
            {
                signalBuy = security.Bars[bar].Close > nrtr[bar];
                signalCover = security.Bars[bar].Close > nrtr[bar];

                signalShort = security.Bars[bar].Close < nrtr[bar];
                signalSell = security.Bars[bar].Close < nrtr[bar];

                orderPrice = security.Bars[bar].Close;

                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                // Если позиции нет
                if (LastActivePosition == null)
                {
                    if (signalBuy)
                    {
                        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, "LN");
                    }

                    if (signalShort)
                    {
                        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, "SN");
                    }
                }
                else
                {
                    // Если длинная позиция
                    if (LastActivePosition.IsLong)
                    {
                        if (signalSell)
                        {
                            LastActivePosition.CloseAtPrice(bar + 1, orderPrice, "LX");
                        }
                    }

                    // Если короткая позиция
                    else if (LastActivePosition.IsShort)
                    {
                        if (signalCover)
                        {
                            LastActivePosition.CloseAtPrice(bar + 1, orderPrice, "SX");
                        }
                    }
                }
            }

            // Берем основную панель (Pane)
            IGraphPane mainPane = ctx.First;

            // Отрисовка
            mainPane.AddList(String.Format("nrtr({0})", nrtr.Count), nrtr, ListStyles.LINE,
                            new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.DOT, PaneSides.RIGHT);

            ctx.Log(String.Format("bars({0})", security.Bars.Count));
            ctx.Log(String.Format("nrtr({0})", nrtr.Count));
        }
	}
}

