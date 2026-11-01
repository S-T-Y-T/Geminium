using System;
using System.Collections.Generic;

namespace Geminium.Core.Data
{
    // Описания игровых сущностей. Это данные, а не состояние игры: во время игры их только читают.
    // Деньги — в сумах, целым числом. Id — латиница в нижнем регистре (из него строится
    // ключ локализации, например «material.cotton»).

    // Вид сырья (GDD 7.4).
    [Serializable]
    public sealed class MaterialDef
    {
        public string Id;
        public MaterialGroup Group;
        public MaterialUnit Unit;
        public MaterialOrigin Origin;
        public long BasePrice;     // базовая биржевая цена за единицу, P_base
        public double Volatility;  // волатильность биржевой цены, σ (0–1)
        public int UnitsPerCell;   // сколько единиц помещается в одну ячейку склада
        public double MarketSupplyPerMonth; // свободное предложение рынка в месяц, S (биржевая цена)
    }

    // Поставщик одного вида сырья (GDD 7.4).
    [Serializable]
    public sealed class SupplierDef
    {
        public string Id;
        public string MaterialId;
        public MaterialOrigin Origin;    // Local или Import (импорт зависит от курса)
        public double PriceDeviation;    // δ — отклонение от биржевой цены, например −0,05 или +0,1
        public double Reliability;       // доля поставок без проблем, 0–1
        public double QualityMin;        // скрытое качество партии — от QualityMin до QualityMax (0–100)
        public double QualityMax;
        public double MinBatch;          // минимальная партия, единиц
        public int LeadTimeDays;         // срок поставки, дней
    }

    // Событие ИИ-ядра (GDD 7.1): с вероятностью раз в месяц начинается и действует несколько месяцев.
    [Serializable]
    public sealed class EventDef
    {
        public string Id;
        public double MonthlyProbability;
        public int MinDurationMonths = 1;
        public int MaxDurationMonths = 1;
        public int SeasonStartMonth;     // 1–12; 0 — круглый год
        public int SeasonEndMonth;       // включительно; сезон может переходить через Новый год (12–2)
        public EventEffect Effect;
        public double Magnitude;         // для множителей: +0,3 = +30%; для качества партии — пункты
        public string Target;            // Id сырья или продукта; пусто — действует на всё
    }

    // Модель станка (GDD 7.6.2).
    [Serializable]
    public sealed class MachineDef
    {
        public string Id;
        public ProductionZone Zone;
        public int Tier;                  // уровень 1–3
        public double Power;              // производительность (мощность), 1 — базовая
        public double EnergyKw;           // энергопотребление, кВт
        public int DurabilityOperations;  // ресурс прочности: операций от состояния 100 до 0
        public long Price;
        public int SizeX = 1;             // размер на сетке цеха, клеток
        public int SizeY = 1;
    }

    // Сколько сырья уходит на одну операцию.
    [Serializable]
    public sealed class MaterialAmount
    {
        public string MaterialId;
        public double Amount;
    }

    // Операция рецепта: на станке какой зоны, сколько длится и что расходует.
    [Serializable]
    public sealed class OperationDef
    {
        public ProductionZone Zone;
        public double BaseTimeMinutes;  // T_base
        public List<MaterialAmount> Inputs = new List<MaterialAmount>();
    }

    // Продукт и его рецепт (GDD 7.6.3).
    [Serializable]
    public sealed class ProductDef
    {
        public string Id;
        public long BasePrice;            // эталонная цена, P_ref
        public int BaseDemandPerMonth;    // базовый спрос рынка B2C в месяц, D_base
        public double BaseDefectChance;   // базовый шанс брака рецепта, p_base (0–1)
        public int UnitsPerCell;          // сколько единиц помещается в одну ячейку склада
        public bool UnlockedByDefault = true;
        public List<OperationDef> Operations = new List<OperationDef>();
    }

    // Профессия (GDD 7.7).
    [Serializable]
    public sealed class ProfessionDef
    {
        public string Id;
        public long BaseMonthlyWage;
        public ProductionZone OperatedZone;  // на станках какой зоны работает; None — не у станка
    }
}
