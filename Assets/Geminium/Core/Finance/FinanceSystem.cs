using System;
using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Economy;

namespace Geminium.Core.Finance
{
    // Финансы (GDD 7.8). В конце месяца, после всех остальных подсистем:
    // соцналог → платежи по кредитам → проценты за овердрафт → амортизация → P&L и налог на прибыль →
    // баланс и капитализация → кредитный рейтинг → проверка банкротства → месячный отчёт.
    // P&L по методу начисления: сырьё уходит в расходы при продаже (себестоимость), покупка станков
    // и кредиты расходами не считаются. Убытки на будущие периоды не переносятся (упрощение этапа 1).
    public sealed class FinanceSystem : SimulationSystem
    {
        private readonly GameWorld world;
        private readonly DataCatalog catalog;
        private readonly FinanceSettings settings;

        public FinanceSystem(GameWorld world)
        {
            this.world = world;
            catalog = world.Catalog;
            settings = catalog.Finance;
        }

        private EconomySystem Economy => world.Economy;

        // Кредитный рейтинг по последнему отчёту; у новой компании — по чистой истории.
        public double CreditRating(Company company) =>
            company.LastReport?.CreditRating ?? Formulas.CreditRating(1, 0, company.Debt, Math.Max(0, company.Cash));

        // Ставка, под которую компания возьмёт кредит сейчас.
        public double LoanRateFor(Company company) =>
            Formulas.LoanRate(Economy.KeyRate, settings.BankMargin, CreditRating(company), settings.TrustDiscount);

        internal void IssueLoan(Company company, long amount, int termMonths)
        {
            company.loans.Add(new Loan(amount, LoanRateFor(company), termMonths, Simulation.MonthIndex));
            company.Receive(LedgerCategory.LoanReceived, amount);
        }

        protected internal override void OnMonthEnded(int monthIndex)
        {
            foreach (var company in world.Companies)
            {
                if (!company.IsBankrupt) CloseMonth(company, monthIndex);
                else AddBankruptReport(company, monthIndex);
                company.CurrentMonth.Reset();
            }
        }

        private void CloseMonth(Company company, int month)
        {
            var ledger = company.Ledger;
            long Spent(LedgerCategory category) => -ledger.MonthTotal(month, category);

            company.Pay(LedgerCategory.SocialTax, (long)Math.Round(Spent(LedgerCategory.Wages) * settings.SocialTaxRate));
            PayLoans(company);
            if (company.Cash < 0)
                company.Pay(LedgerCategory.Interest, (long)Math.Round(-company.Cash * (Economy.KeyRate + settings.OverdraftMargin) / 12));

            long depreciation = 0, equipmentValue = 0;
            foreach (var item in company.Machines.Concat(company.Racks))
            {
                int age = month - item.PurchaseMonth;
                if (age < settings.DepreciationMonths) depreciation += (long)Math.Round((double)item.PurchasePrice / settings.DepreciationMonths);
                equipmentValue += Formulas.ResidualValue(item.PurchasePrice, age + 1, settings.DepreciationMonths);
            }

            var report = new MonthlyReport
            {
                Month = month,
                Revenue = ledger.MonthTotal(month, LedgerCategory.SalesRetail) + ledger.MonthTotal(month, LedgerCategory.SalesContracts),
                CostOfGoodsSold = company.CurrentMonth.CostOfGoodsSold,
                Wages = Spent(LedgerCategory.Wages),
                SocialTax = Spent(LedgerCategory.SocialTax),
                Rent = Spent(LedgerCategory.Rent),
                Energy = Spent(LedgerCategory.Energy),
                Penalties = Spent(LedgerCategory.Penalties),
                OtherExpenses = Spent(LedgerCategory.Maintenance) + Spent(LedgerCategory.Marketing) + Spent(LedgerCategory.Other),
                Interest = Spent(LedgerCategory.Interest),
                Depreciation = depreciation,
            };
            report.PreTaxProfit = report.Revenue - report.CostOfGoodsSold - report.Wages - report.SocialTax - report.Rent
                                  - report.Energy - report.Penalties - report.OtherExpenses - report.Interest - report.Depreciation;
            report.ProfitTax = report.PreTaxProfit > 0 ? (long)Math.Round(report.PreTaxProfit * settings.ProfitTaxRate) : 0;
            company.Pay(LedgerCategory.Taxes, report.ProfitTax);
            report.NetIncome = report.PreTaxProfit - report.ProfitTax;

            report.Cash = company.Cash;
            report.Debt = company.Debt;
            report.InventoryValue = company.Inventory.TotalValue();
            report.MachinesValue = equipmentValue;
            report.Reputation = company.Reputation;

            double averageProfit = company.Reports.Skip(Math.Max(0, company.Reports.Count - 5)).Select(r => (double)r.NetIncome)
                .Append(report.NetIncome).Average();
            report.Capitalization = Formulas.Capitalization(report.Cash, report.MachinesValue, 0, report.InventoryValue, report.Debt,
                averageProfit, Economy.CapitalizationMultiplier, company.Reputation);

            long payments = company.totalPaymentsOnTime + company.totalPaymentsMissed;
            double onTime = payments == 0 ? 1 : (double)company.totalPaymentsOnTime / payments;
            double margin = report.Revenue > 0 ? (double)report.NetIncome / report.Revenue : (report.NetIncome < 0 ? -0.2 : 0);
            long assets = Math.Max(0, report.Cash) + report.MachinesValue + report.InventoryValue;
            report.CreditRating = Formulas.CreditRating(onTime, margin, report.Debt, assets);

            company.NegativeCashMonths = company.Cash < 0 ? company.NegativeCashMonths + 1 : 0;
            bool overdue = company.Loans.Any(l => l.OverdueMonths > settings.MaxOverdueMonths);
            if (company.NegativeCashMonths >= settings.NegativeCashMonthsToBankrupt || overdue)
            {
                company.IsBankrupt = true;
                company.BankruptMonth = month;
            }
            report.Bankrupt = company.IsBankrupt;

            FillProductStats(company, report, month);
            company.reports.Add(report);
        }

        // Равные доли тела + проценты на остаток. Не хватает денег — платёж пропущен,
        // проценты добавляются к долгу, на следующий месяц причитается и пропущенная доля.
        private void PayLoans(Company company)
        {
            foreach (var loan in company.loans)
            {
                if (loan.Remaining <= 0) continue;
                long interest = (long)Math.Round(loan.Remaining * loan.AnnualRate / 12);
                long share = (long)Math.Ceiling((double)loan.Principal / loan.TermMonths);
                long principal = Math.Min(loan.Remaining, share * (1 + loan.OverdueMonths));
                if (company.Cash >= interest + principal)
                {
                    company.Pay(LedgerCategory.Interest, interest);
                    company.Pay(LedgerCategory.LoanRepayment, principal);
                    loan.Remaining -= principal;
                    loan.PaymentsMade += 1 + loan.OverdueMonths;
                    loan.OverdueMonths = 0;
                    company.totalPaymentsOnTime++;
                }
                else
                {
                    loan.Remaining += interest;
                    loan.OverdueMonths++;
                    company.totalPaymentsMissed++;
                }
            }
            company.loans.RemoveAll(l => l.Remaining <= 0);
        }

        private void FillProductStats(Company company, MonthlyReport report, int month)
        {
            var stats = company.CurrentMonth;
            report.UnitsProduced = stats.Produced.Values.Sum();
            report.UnitsDefective = stats.Defective.Values.Sum();
            report.UnitsSold = stats.Sold.Values.Sum();
            report.LostSales = stats.LostSales.Values.Sum();
            report.OverflowDays = stats.OverflowDays;
            foreach (var product in catalog.Products)
            {
                stats.Sold.TryGetValue(product.Id, out long sold);
                report.SoldByProduct[product.Id] = sold;
                stats.LostSales.TryGetValue(product.Id, out long lost);
                report.LostSalesByProduct[product.Id] = lost;
                report.MarketShareByProduct[product.Id] = world.Market.ShareOf(month, company.Id, product.Id);
            }
        }

        private void AddBankruptReport(Company company, int month)
        {
            var report = new MonthlyReport
            {
                Month = month,
                Cash = company.Cash,
                Debt = company.Debt,
                InventoryValue = company.Inventory.TotalValue(),
                Reputation = company.Reputation,
                Bankrupt = true,
            };
            FillProductStats(company, report, month);
            company.reports.Add(report);
        }
    }
}
