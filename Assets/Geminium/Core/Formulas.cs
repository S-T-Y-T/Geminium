using System;

namespace Geminium.Core
{
    // Формулы GDD (Приложение Б) как чистые функции: без состояния и случайности внутри.
    // Подсистемы вызывают их, тесты проверяют каждую отдельно.
    public static class Formulas
    {
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        // ---------- ИИ-ядро (GDD 7.1) ----------

        // I(t+1) = I(t) + θ·(1 − I(t)) + σ·ξ, в пределах [min; max].
        public static double NextEconomyIndex(double index, double reversion, double volatility, double gaussian, double min, double max) =>
            Clamp(index + reversion * (1 - index) + volatility * gaussian, min, max);

        // r_key = r_0 + k_π·(π − π_target), в пределах [min; max].
        public static double KeyRate(double neutralRate, double sensitivity, double inflation, double target, double min, double max) =>
            Clamp(neutralRate + sensitivity * (inflation - target), min, max);

        // Сезонность S(t): годовая волна от 1 − A до 1 + A с пиком в месяце peakMonth (1–12).
        public static double Seasonality(int calendarMonth, double amplitude, int peakMonth) =>
            1 + amplitude * Math.Cos(2 * Math.PI * (calendarMonth - peakMonth) / 12.0);

        // Ценовая эластичность растёт в кризис: w_p = база · (1 + s·(1 − I)), не меньше 0.
        public static double PriceElasticity(double baseElasticity, double crisisSensitivity, double economyIndex) =>
            Math.Max(0, baseElasticity * (1 + crisisSensitivity * (1 - economyIndex)));

        // ---------- Рынок (GDD 7.2) ----------

        // D = D_base · S(t) · E · I — спрос на продукт за месяц.
        public static double MonthlyDemand(double baseDemand, double seasonality, double eventMultiplier, double economyIndex) =>
            Math.Max(0, baseDemand * seasonality * eventMultiplier * economyIndex);

        // D_m = D · (P_ref / P_avg)^ε_m — объём рынка с учётом средней цены.
        public static double MarketSize(double demand, double referencePrice, double averagePrice, double marketSizeElasticity) =>
            averagePrice <= 0 ? demand : demand * Math.Pow(referencePrice / averagePrice, marketSizeElasticity);

        // A_i = (w_q·Q_i + w_r·R_i) / (P_i / P_ref)^w_p — привлекательность компании.
        public static double Attractiveness(double quality, double reputation, double price, double referencePrice,
            double qualityWeight, double reputationWeight, double priceElasticity)
        {
            if (price <= 0 || referencePrice <= 0) throw new ArgumentOutOfRangeException(nameof(price), "Цены должны быть больше нуля.");
            return (qualityWeight * quality + reputationWeight * reputation) / Math.Pow(price / referencePrice, priceElasticity);
        }

        // ---------- Тендеры и маркетинг (GDD 7.2, 7.3) ----------

        // Score = w_1·(P_max − P_bid)/P_max + w_2·R/100 + w_3·QH/100 + w_4·B_mgr (+ бонусы сертификата и спонсорства).
        public static double TenderScore(long maxPrice, long bid, double reputation, double qualityHistory, double managerBonus,
            double wPrice, double wReputation, double wQuality, double wManagers, double extraBonus)
        {
            if (maxPrice <= 0) throw new ArgumentOutOfRangeException(nameof(maxPrice), "P_max должна быть больше нуля.");
            return wPrice * (maxPrice - bid) / (double)maxPrice + wReputation * reputation / 100 + wQuality * qualityHistory / 100
                   + wManagers * Clamp(managerBonus, 0, 1) + extraBonus;
        }

        // k_ads = 1 + a·(1 − e^(−B/B_0)) — множитель спроса от рекламы с убывающей отдачей.
        public static double AdMultiplier(double adStock, double scale, double maxBoost) =>
            scale <= 0 || adStock <= 0 ? 1 : 1 + maxBoost * (1 - Math.Exp(-adStock / scale));

        // R(t+1) = R(t) + ΔR_маркетинг + ΔR_победы − ΔR_рекламации + λ·(50 − R(t)), в пределах 0–100.
        public static double NextReputation(double reputation, double gains, double losses, double reversion) =>
            Clamp(reputation + gains - losses + reversion * (50 - reputation), 0, 100);

        // Шанс победы амбассадора на турнире: scale · (рейтинг/100)^степень (чем выше уровень, тем больше степень).
        public static double WinChance(double rating, double scale, double exponent) =>
            Clamp(scale * Math.Pow(Clamp(rating, 0, 100) / 100, exponent), 0, 1);

        // ---------- Производство (GDD 7.6.4) ----------

        // Q = 0,4·Q_raw + 0,4·Skill + 0,2·Cond.
        public static double UnitQuality(double rawQuality, double skill, double machineCondition) =>
            0.4 * rawQuality + 0.4 * skill + 0.2 * machineCondition;

        // M(Cond) = 1 при Cond ≥ 50; e^(k·(50 − Cond)/50) ниже 50.
        public static double WearMultiplier(double machineCondition, double k) =>
            machineCondition >= 50 ? 1 : Math.Exp(k * (50 - machineCondition) / 50);

        // p = clamp(p_base·(1,5 − Skill/100)·(1,3 − Q_raw/100)·M(Cond), 0, 0,95); при усталости > 70 — не меньше 0,30.
        public static double DefectChance(double baseChance, double skill, double rawQuality, double machineCondition, double fatigue, double wearK)
        {
            double p = Clamp(baseChance * (1.5 - skill / 100) * (1.3 - rawQuality / 100) * WearMultiplier(machineCondition, wearK), 0, 0.95);
            return fatigue > 70 ? Math.Max(p, 0.30) : p;
        }

        // Eff = (0,5 + Exp/100) · k_fatigue · k_fit; k_fatigue = 0,5 при усталости > 70.
        public static double WorkerEfficiency(double experience, double fatigue, double fit) =>
            (0.5 + experience / 100) * (fatigue > 70 ? 0.5 : 1) * fit;

        // T = T_base / (Power · Eff) + t_feed.
        public static double OperationTime(double baseTime, double power, double efficiency, double feedTime) =>
            baseTime / (power * efficiency) + feedTime;

        // ---------- Снабжение (GDD 7.4, Приложение Д) ----------

        // P_raw = P_base · (1 + σ·ξ) · ratio, ratio = 1 + D/S в пределах [min; max].
        // D — закупки всех компаний за прошлый месяц, S — свободное предложение рынка.
        public static double ExchangePrice(double basePrice, double volatility, double gaussian, double factoryDemand, double marketSupply,
            double ratioMin, double ratioMax)
        {
            double ratio = Clamp(1 + factoryDemand / marketSupply, ratioMin, ratioMax);
            double noise = Math.Max(0.1, 1 + volatility * gaussian);
            return basePrice * noise * ratio;
        }

        // P_sup = P_raw · (1 + δ) · (1 − d_loyal − d_freq).
        public static double SupplierPrice(double exchangePrice, double deviation, double loyaltyDiscount, double frequencyDiscount) =>
            exchangePrice * (1 + deviation) * Math.Max(0, 1 - loyaltyDiscount - frequencyDiscount);

        // ---------- Склад (GDD 7.5) ----------

        // C_wh = Аренда + Ячейки_занятые · c_ячейки — расходы склада в день.
        public static long WarehouseDailyCost(long rent, int occupiedCells, long costPerCell) => rent + occupiedCells * costPerCell;

        // ---------- Финансы (GDD 7.8) ----------

        // r = r_key + m_bank − CR·k_trust, не меньше r_key / 2.
        public static double LoanRate(double keyRate, double bankMargin, double creditRating, double trustDiscount) =>
            Math.Max(keyRate / 2, keyRate + bankMargin - creditRating * trustDiscount);

        // CR = 0,5·OnTime + 0,3·Margin_norm + 0,2·(1 − Debt/Assets), в пределах [0; 1].
        // Margin_norm — маржа −20%…+20%, переведённая в 0…1.
        public static double CreditRating(double onTimeShare, double netMargin, long debt, long assets)
        {
            double marginNorm = Clamp((netMargin + 0.2) / 0.4, 0, 1);
            double debtShare = assets <= 0 ? (debt > 0 ? 1 : 0) : Clamp((double)debt / assets, 0, 1);
            return Clamp(0.5 * onTimeShare + 0.3 * marginNorm + 0.2 * (1 - debtShare), 0, 1);
        }

        // Cap = (Cash + V_m + V_d + V_inv − Debt) + max(0, NI_avg6) · M · (0,5 + R/100).
        public static long Capitalization(long cash, long machinesValue, long digitalValue, long inventoryValue, long debt,
            double averageMonthlyProfit, double multiplier, double reputation)
        {
            double netAssets = cash + machinesValue + digitalValue + inventoryValue - debt;
            double goodwill = Math.Max(0, averageMonthlyProfit) * multiplier * (0.5 + reputation / 100);
            return (long)Math.Round(netAssets + goodwill);
        }

        // Остаточная стоимость при линейной амортизации.
        public static long ResidualValue(long price, int ageMonths, int depreciationMonths) =>
            ageMonths >= depreciationMonths ? 0 : (long)Math.Round(price * (1 - (double)ageMonths / depreciationMonths));
    }
}
