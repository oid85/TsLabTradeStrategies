/*
Граница канала подтягивается за ценой 
(по аналлогии со скользящим стопом) на расстоянии, вычисляемом через ATR. При движении цены в обратную сторону 
граница остается на месте, и при ее пересечении происходит переворот позиции (типа Stop And Reverse).
*/

using System;
using System.Collections.Generic;
using Centaur.MoneyManagements;
using Centaur.Tools;
using Framework.Centaur.MathExtensions;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.SuperTrend.SuperTrendAtr
{
	public class SuperTrendAtr_OF : IExternalScript
	{				 
		public OptimProperty PeriodAtr = new OptimProperty(5, 5, 50, 5);
		public OptimProperty Mult = new OptimProperty(1, 1, 10, 0.5);

		public readonly OptimProperty OptimalF = new OptimProperty(1, 1, 5, 0.1);

		public virtual void Execute(IContext ctx, ISecurity security)
		{
            // Плечо
            double optimalF = OptimalF;

            // Количество бумаг в лоте
            int lotSize = 1;
			
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1.0; // Количество лотов (не бумаг !!!)

		    IList<double> upSeries = new List<double>();
			IList<double> downSeries = new List<double>();
			IList<double> trendDirectionSeries = new List<double>();
			IList<double> trendSeries = new List<double>();

            upSeries = upSeries.InitValues(security.Bars.Count);
            downSeries = downSeries.InitValues(security.Bars.Count);
            trendDirectionSeries = trendDirectionSeries.InitValues(security.Bars.Count);
            trendSeries = trendSeries.InitValues(security.Bars.Count);

            int currentTrendDirection = 0;
			double up = 0.0;
			double down = 0.0;

            bool signalBuy;
            bool signalSell;
            bool signalShort;
            bool signalCover;

            IPosition longPosition;
            IPosition shortPosition;

            int periodAtr = PeriodAtr;
			double mult = Mult;

            // ATR
            var atrObject = new WealthLabIndicators.Atr() { Period = periodAtr };
            IList<double> atrSeries = atrObject.Execute(security);
            firstValidValue = Math.Max(firstValidValue, periodAtr * 3);

            // Учтем возможность неполных свечей, которые появятся на пересчетах отличных от ИНТЕРВАЛ
            // нельзя использовать неполную свечку в расчетах, она всегда изменяется
            var count = ctx.BarsCount;
            if (!ctx.IsLastBarClosed)
                count--;

            for (int bar = firstValidValue; bar < count; bar++)
			{
                double closePrice = security.Bars[bar].Close;
                double currentAtr = atrSeries[bar];
				double trend = 0;

				double prevUp = up;
				double prevDown = down;
				int prevTrendDirection = currentTrendDirection;

				var averagePrice = (security.Bars[bar].High + security.Bars[bar].Low) / 2.0;
				up = averagePrice + mult * currentAtr;
				down = averagePrice - mult * currentAtr;
				
				if (closePrice > prevUp)
                    currentTrendDirection = 1;

				if (closePrice < prevDown)
                    currentTrendDirection = -1;
				
				if (currentTrendDirection > 0 && down < prevDown)
                    down = prevDown;

				if (currentTrendDirection < 0 && up > prevUp)
                    up = prevUp;

				if (currentTrendDirection > 0 && prevTrendDirection < 0)
                    down = averagePrice - mult * currentAtr;

				if (currentTrendDirection < 0 && prevTrendDirection > 0)
                    up = averagePrice + mult * currentAtr;
					
				if (currentTrendDirection == 1)
                    trend = down;

				if (currentTrendDirection == -1)
                    trend = up;

                upSeries[bar] = up;
                downSeries[bar] = down;
                atrSeries[bar] = currentAtr;
                trendDirectionSeries[bar] = currentTrendDirection;
                trendSeries[bar] = trend;

				// сброс значений сигналов
				signalBuy = false;
				signalSell = false;
				signalShort = false;
				signalCover = false;
				
				// установка сигналов по условиям
				if (currentTrendDirection > 0 && prevTrendDirection < 0)
				{
					signalBuy = true;
					signalCover = true;
				}
				if (currentTrendDirection < 0 && prevTrendDirection > 0) 
				{
					signalShort = true;
					signalSell = true;
				}

                double orderPrice = security.Bars[bar].Close;
				
                // Управление капиталом
                double money = security.CurrentBalance(bar);
                var mm = new OptimalF(money, optimalF, orderPrice);
                lots = mm.GetShares(lotSize);				
				
				longPosition = security.Positions.GetLastActiveForSignal("LN", bar);
                shortPosition = security.Positions.GetLastActiveForSignal("SN", bar);

                if (longPosition == null)
				{
				    if (signalBuy)
				        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, @"LN");
				}
				else
				{
				    if (signalSell)
				        longPosition.CloseAtStop(bar + 1, orderPrice, @"LX");
				}				
				
				if (shortPosition == null)
				{
				    if (signalShort)
				        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, @"SN");
				}
				else
				{
				    if (signalCover)
				        shortPosition.CloseAtStop(bar + 1, orderPrice, @"SX");
				}
			} 

			// Берем основную панель (Pane)
			IGraphPane mainPane = ctx.First;

            // Отрисовка верхней и нижней границы условных прямоугольников 
            mainPane.AddList(String.Format("upSeries({0})", upSeries.Count), upSeries, ListStyles.LINE, 
                new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.DOT, PaneSides.RIGHT);

			mainPane.AddList(String.Format("downSeries({0})", downSeries.Count), downSeries, ListStyles.LINE, 
                new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.DOT, PaneSides.RIGHT);

			mainPane.AddList(String.Format("trendSeries({0})", trendSeries.Count), trendSeries, ListStyles.LINE, 
                new Color(System.Drawing.Color.Green.ToArgb()), LineStyles.DOT, PaneSides.RIGHT);

            // Создаем дополнительную панель для ATR.
            IGraphPane atrPane = ctx.CreateGraphPane("ATR", "ATR");

			// Отрисовка графика ATR
			atrPane.AddList(String.Format("atrSeries({0})", atrSeries.Count), atrSeries, ListStyles.LINE,
            new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);

            // Создаем дополнительную панель для филтра тренда.
            IGraphPane filterPane = ctx.CreateGraphPane("FILTR", "FILTR");

			// Отрисовка графика фильтра тренда
			filterPane.AddList(String.Format("trendDirectionSeries({0})", trendDirectionSeries.Count), trendDirectionSeries, ListStyles.HISTOHRAM_FILL,
            new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
        }
	}
}

