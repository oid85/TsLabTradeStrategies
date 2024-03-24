using System;
using System.Collections.Generic;
using Framework.Centaur.MathExtensions;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace Centaur.Strategies.AdaptivePCAdx.AdaptivePCAdxCombine
{
    public class AdaptivePCAdxCombine_FixLot : IExternalScript
    {
        public IPosition LastActivePosition = null;

        // Объявление и инициализация параметров торговой системы
        public readonly OptimProperty Period = new OptimProperty(10, 10, 200, 5);
        public readonly OptimProperty PeriodAdx = new OptimProperty(10, 10, 50, 5);
        public readonly OptimProperty Koeff = new OptimProperty(1, 0.5, 2.5, 0.5);

        public virtual void Execute(IContext ctx, ISecurity security)
        {
            // Объявление переменных
            int firstValidValue = 0;
            double lots = 1.0; // Количество лотов (не бумаг !!!)           

            IList<double> closePrices = security.GetClosePrices(ctx);
            IList<double> highPrices = security.GetHighPrices(ctx);
            IList<double> lowPrices = security.GetLowPrices(ctx);         
            
            double orderPrice = 0.0;
            
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

            IList<double> lustraTrailing = new List<double>().InitValues(security.Bars.Count);
            IList<double> channelTrailing = new List<double>().InitValues(security.Bars.Count);

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

            // Сглаживание
            int smoothPeriod = 5;
            highLevelEntry = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(highLevelEntry);
            lowLevelEntry = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(lowLevelEntry);
            highLevelExit = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(highLevelExit);
            lowLevelExit = new Centaur.WealthLabIndicators.Ema() { Period = smoothPeriod }.Execute(lowLevelExit);

            // Переменные для обслуживания позиции
            double koeff = Koeff.Value;

            double holdPeriod = 0.0;
            double holdPeriodLimit = koeff * period;

            double widthLustra = 0.0;

            double currentChannelWidth = 1.0;
            
            double lustraStop = 0.0;
            double currentLustraStop = 0.0;

            double channelStop = 0.0;
            double currentChannelStop = 0.0;
                                    
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
                
                // Задаем тип ордера и цену для заявки
                orderPrice = security.Bars[bar].Close;

                // Сопровождение позиции
                if (LastActivePosition == null) // Если позиции нет
                {
                    if (signalBuy) // Если пришел сигнал на покупку
                    {
                        security.Positions.BuyAtPrice(bar + 1, lots, orderPrice, @"LN");
                    }
                    else if (signalShort) // Если пришел сигнал на короткую продажу
                    {
                        security.Positions.SellAtPrice(bar + 1, lots, orderPrice, @"SN");
                    }
                }
                else // Если позиция есть
                {
                    if (LastActivePosition.IsLong) // Для длинной позиции
                    {
                        // 1. Ищем стоп люстры          
                        if (bar == LastActivePosition.EntryBarNum)
                        {
                            // Первоначальный уровень Стопа в момент входа в позицию
                            widthLustra = security.Bars[LastActivePosition.EntryBarNum].High - lowLevelExit[LastActivePosition.EntryBarNum];
                            lustraStop = lowLevelExit[LastActivePosition.EntryBarNum];
                        }
                        else
                        {
                            // Расcчитываем размер стопа люстры
                            double maxPrice = security.Bars[LastActivePosition.FindHighBar(bar)].High;
                            currentLustraStop = maxPrice - widthLustra;
                            lustraStop = System.Math.Max(lustraStop, currentLustraStop);
                        }

                        // 2. Ищем стоп канала
                        holdPeriod = bar - LastActivePosition.EntryBarNum;

                        // Рассчитываем ширину канала d %
                        if (holdPeriod >= 0 && holdPeriod <= holdPeriodLimit * 0.5) // Если период удержания позиции равен половине лимитного периода 
                            currentChannelWidth = 1.0;

                        if (holdPeriod > holdPeriodLimit * 0.5) // Если период удержания позиции больше половины лимитного периода
                            currentChannelWidth = 1.0 - (holdPeriod - holdPeriodLimit * 0.5) / holdPeriodLimit; // Ширина канала резко подтягивается к ценам

                        currentChannelStop = lowLevelExit[bar] + (highLevelExit[bar] - lowLevelExit[bar]) * (1.0 - currentChannelWidth);
                        channelStop = System.Math.Max(currentChannelStop, lowLevelExit[bar]);

                        lustraTrailing[bar] = lustraStop;
                        channelTrailing[bar] = channelStop;

                        //  Выход из длинной позиции
                        LastActivePosition.CloseAtStop(bar + 1, Math.Max(lustraStop, channelStop), @"LX");
                    }
                    else if (LastActivePosition.IsShort)
                    {
                        // 1. Ищем стоп люстры          
                        if (bar == LastActivePosition.EntryBarNum)
                        {
                            // Первоначальный уровень Стопа в момент входа в позицию
                            widthLustra = highLevelExit[LastActivePosition.EntryBarNum] - security.Bars[LastActivePosition.EntryBarNum].Low;
                            lustraStop = highLevelExit[LastActivePosition.EntryBarNum];
                        }
                        else
                        {
                            // Расcчитываем размер стопа люстры
                            double minPrice = security.Bars[LastActivePosition.FindLowBar(bar)].Low;
                            currentLustraStop = minPrice + widthLustra;
                            lustraStop = System.Math.Min(lustraStop, currentLustraStop);
                        }

                        // 2. Ищем стоп канала
                        holdPeriod = bar - LastActivePosition.EntryBarNum;

                        // Рассчитываем ширину канала d %
                        if (holdPeriod >= 0 && holdPeriod <= holdPeriodLimit * 0.5)
                            currentChannelWidth = 1.0;

                        if (holdPeriodLimit * 0.5 < holdPeriod)
                            currentChannelWidth = 1.0 - (holdPeriod - holdPeriodLimit * 0.5) / holdPeriodLimit;

                        currentChannelStop = highLevelExit[bar] - (highLevelExit[bar] - lowLevelExit[bar]) * (1.0 - currentChannelWidth);
                        channelStop = System.Math.Min(currentChannelStop, highLevelExit[bar]);

                        lustraTrailing[bar] = lustraStop;
                        channelTrailing[bar] = channelStop;

                        // Выход из короткой позиции 
                        LastActivePosition.CloseAtStop(bar + 1, Math.Min(lustraStop, channelStop), @"SX");
                    }
                }               
            }

            // Отрисовка
            IGraphPane pricePane = ctx.First;
            pricePane.AddList(@"highLevelEntry", highLevelEntry, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"lowLevelEntry", lowLevelEntry, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"highLevelExit", highLevelExit, ListStyles.LINE, new Color(System.Drawing.Color.DarkBlue.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"lowLevelExit", lowLevelExit, ListStyles.LINE, new Color(System.Drawing.Color.DarkRed.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);

            pricePane.AddList(@"lustraTrailing", lustraTrailing, ListStyles.LINE_WO_ZERO, new Color(System.Drawing.Color.DarkGreen.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
            pricePane.AddList(@"channelTrailing", channelTrailing, ListStyles.LINE_WO_ZERO, new Color(System.Drawing.Color.LimeGreen.ToArgb()), LineStyles.SOLID, PaneSides.RIGHT);
        }
    }
}
