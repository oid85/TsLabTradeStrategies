// ТС Пробой волатильности 
// Вход разработан на основе информации из бюллетени Чака Лебо №3 "Построение ТС из независимых компонентов". 
// Выход разработан на основе информации из бюллетени Чака Лебо №14 "Применение ATR для выходов". 
// 
// Вход в лонг - пробитие Close величины (Open + Close) / 2.0 + atr * koef
// Вход в шорт - пробитие Close величины (Open + Close) / 2.0 - atr * koef
// 
// Фильтр отсутствует
// 
// Выход - трейлинг по центру канала
//
// Управление капиталом - OF

using System;
using System.Collections.Generic;
using Centaur.MoneyManagements;
using Centaur.Tools;
using Framework.Centaur.MathExtensions;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.VolatilityBreakout.VolatilityBreakoutMiddle
{
    public class VolatilityBreakoutMiddle_OF : IExternalScript
    {
        public IPosition LastActivePosition = null;

        public OptimProperty PeriodAtr = new OptimProperty(100, 50, 300, 10);
        public OptimProperty PeriodPc = new OptimProperty(100, 50, 300, 10);
        public OptimProperty KoeffAtr = new OptimProperty(0.5, 0.5, 3, 0.5);

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

            IList<double> openPrices = security.GetOpenPrices(ctx);
            IList<double> closePrices = security.GetClosePrices(ctx);

            int periodAtr = PeriodAtr;
            int periodPc = PeriodPc;
            double koeffAtr = KoeffAtr;

            // Индикаторы
            // ATR
            var atrObject = new WealthLabIndicators.Atr() { Period = periodAtr };
            IList<double> atr = atrObject.Execute(security);
            firstValidValue = Math.Max(firstValidValue, periodAtr * 3);

            // Расчетные цены, от которых будет откладывать волатильность
            IList<double> price = openPrices.Add(closePrices).DivConst(2.0);

            // Границы каналов волатильности
            IList<double> up = atr.MultConst(koeffAtr).Add(price);   // up = price + atr * KoeffAtr;
            IList<double> down = atr.MultConst(koeffAtr).Add(price); // down = price + atr * KoeffAtr;

            up = ctx.GetData(@"up", new[] { periodPc.ToString() }, () => Series.Highest(up, periodPc));
            down = ctx.GetData(@"down", new[] { periodPc.ToString() }, () => Series.Lowest(down, periodPc));

            up = Series.Shift(up, 1);
            down = Series.Shift(down, 1);
			
			firstValidValue = Math.Max(firstValidValue, periodPc * 2);

            // Сглаживание
			int smoothPeriod = 5;
            up = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(up);
            down = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(down);
			firstValidValue += smoothPeriod * 2;

            // Трэйлинг-стоп
            IList<double> trailing = up.Add(down).DivConst(2.0);

            // Отрисовка индикаторов
            IGraphPane pricePane = ctx.First;
            pricePane.AddList(@"up", up, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"down", down, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);

            // Переменные для обслуживания позиции
            double startTrailing = 0.0;     // Стоп, выставляемый при открытии позиции
            double currentTrailing = 0.0;   // Величина текущего стопа

            bool signalBuy = false;
            bool signalShort = false;
            double orderPrice = 0.0;

            // Учтем возможность неполных свечей, которые появятся на пересчетах отличных от ИНТЕРВАЛ
            // нельзя использовать неполную свечку в расчетах, она всегда изменяется
            var count = ctx.BarsCount;
            if (!ctx.IsLastBarClosed)
                count--;

            for (int bar = firstValidValue; bar < count; bar++) // Пробегаемся по всем свечкам
            {
                signalBuy = security.Bars[bar].Close > up[bar];
                signalShort = security.Bars[bar].Close < down[bar];

                orderPrice = security.Bars[bar].Close;

                // Управление капиталом
                double money = security.CurrentBalance(bar);
                var mm = new OptimalF(money, optimalF, orderPrice);
                lots = mm.GetShares(lotSize);

                // Получить ссылку на последнию активную позицию
                LastActivePosition = security.Positions.GetLastPositionActive(bar);

                if (LastActivePosition == null) // Если позиции нет
                {
                    if (signalBuy) // При получении сигнала на вход в длинную позицию
                    {
                        startTrailing = trailing[bar];
                        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, @"LN");
                    }
                    else if (signalShort) // При получении сигнала на вход в короткую позицию
                    {
                        startTrailing = trailing[bar];
                        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, @"SN");
                    }
                }
                else // Если позиция есть
                {
                    if (LastActivePosition.IsLong)
                    {
                        int entryBar = LastActivePosition.EntryBarNum;

                        // Вычисление текущего стопа
                        currentTrailing = bar == entryBar ? startTrailing : Math.Max(currentTrailing, trailing[bar]);

                        LastActivePosition.CloseAtStop(bar + 1, currentTrailing, @"LX"); // Попробовать выйти
                    }

                    else if (LastActivePosition.IsShort)
                    {
                        int entryBar = LastActivePosition.EntryBarNum;

                        // Вычисление текущего стопа
                        currentTrailing = bar == entryBar ? startTrailing : Math.Min(currentTrailing, trailing[bar]);

                        LastActivePosition.CloseAtStop(bar + 1, currentTrailing, @"SX"); // Попробовать выйти
                    }
                }
            }
        }
    }
}