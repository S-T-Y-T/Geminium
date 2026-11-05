using System;
using System.Linq;
using UnityEngine.UIElements;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Digital;
using Geminium.Core.Sales;

namespace Geminium.UI
{
    // Сбыт (GDD 7.2): цены B2C и план выпуска, тендеры и контракты.
    public sealed class SalesPanel : Panel
    {
        public SalesPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.sales";
        public override string Icon => "₴";

        protected override void Build(VisualElement root)
        {
            var last = Player.LastReport;
            bool aiPrices = Player.Digital.Has(DigitalModule.AiPricing) && Player.Digital.AutoPricing;
            var box = Ui.Section(root, Loc.Get("sales.products"));
            if (aiPrices) box.Add(Ui.Hint(Loc.Get("sales.ai_prices")));
            foreach (var product in World.Rules.OpenProducts(Catalog))
            {
                var p = product;
                var card = new VisualElement().With("card").AddTo(box);
                card.Add(Ui.Text(Loc.Name(p.Id), "strong"));
                long sold = 0, lost = 0;
                double share = 0;
                if (last != null)
                {
                    last.SoldByProduct.TryGetValue(p.Id, out sold);
                    last.LostSalesByProduct.TryGetValue(p.Id, out lost);
                    last.MarketShareByProduct.TryGetValue(p.Id, out share);
                }
                card.Add(Ui.Text(Loc.Get("sales.last_month", sold, lost, Fmt.Percent(share), Player.Inventory.ProductUnits(p.Id)), "hint", "wrap"));
                var price = Ui.Field(Player.RetailPrice(p.Id).ToString(), 110);
                var plan = Ui.Field(Player.MonthlyPlan(p.Id).ToString(), 80);
                var row = Ui.Row(Ui.Text(Loc.Get("sales.price"), "pair-label-short"), price).AddTo(card);
                var setPrice = Ui.Button(Loc.Get("ui.apply"), () => { var v = Ui.ParseLong(price.value); if (v != null) Submit(new SetRetailPriceCommand(p.Id, v.Value)); }, "small").AddTo(row);
                setPrice.SetEnabled(!aiPrices);
                row.Add(Ui.Text(Loc.Get("sales.plan"), "pair-label-short"));
                row.Add(plan);
                Ui.Button(Loc.Get("ui.apply"), () => { var v = Ui.ParseLong(plan.value); if (v != null) Submit(new SetMonthlyPlanCommand(p.Id, (int)v.Value)); }, "small").AddTo(row);
                card.Add(Ui.Hint(Loc.Get("sales.reference_price", Fmt.Money(p.BasePrice * World.Economy.PriceLevel))));
            }

            if (!World.Rules.Allows(Feature.Tenders)) return;
            var tenders = Ui.Section(root, Loc.Get("sales.tenders"));
            if (World.Tenders.OpenTenders.Count == 0) tenders.Add(Ui.Hint(Loc.Get("sales.no_tenders")));
            foreach (var tender in World.Tenders.OpenTenders)
            {
                var t = tender;
                var card = new VisualElement().With("card").AddTo(tenders);
                card.Add(Ui.Text(Loc.Get("sales.tender_title", Loc.Enum(t.Level), t.ProductId, t.Units), "strong"));
                card.Add(Ui.Text(Loc.Get("sales.tender_info", Fmt.Money(t.MaxUnitPrice), t.DeliveryMonths), "hint", "wrap"));
                string eligibility = World.Tenders.CheckEligibility(Player, t);
                var mine = t.Bids.FirstOrDefault(b => b.CompanyId == Player.Id);
                if (eligibility != null) card.Add(Ui.Text(eligibility, "negative", "wrap"));
                var bid = Ui.Field((mine?.UnitPrice ?? (long)(t.MaxUnitPrice * 0.9)).ToString(), 110);
                Ui.Row(Ui.Text(Loc.Get("sales.bid_price"), "pair-label-short"), bid,
                    Ui.Button(Loc.Get(mine == null ? "sales.bid" : "sales.rebid"), () => { var v = Ui.ParseLong(bid.value); if (v != null) Submit(new BidTenderCommand(t.Id, v.Value)); }, "small", "primary")).AddTo(card);
                if (mine != null) card.Add(Ui.Hint(Loc.Get("sales.bid_placed", Fmt.Money(mine.UnitPrice))));
            }

            var contracts = Ui.Section(root, Loc.Get("sales.contracts"));
            Ui.Table(contracts, new float[] { 160, 110, 90, 90, 80 },
                new[] { Loc.Get("sales.product"), Loc.Get("sales.level"), Loc.Get("sales.delivered"), Loc.Get("sales.deadline"), Loc.Get("sales.status") },
                Player.Sales.Contracts.OrderByDescending(c => c.Id).Take(12).Select(c => new VisualElement[]
                {
                    Ui.Text(Loc.Name(c.ProductId)), Ui.Text(Loc.Enum(c.Level)), Ui.Text($"{c.Delivered}/{c.Units}"),
                    Ui.Text(Loc.Get("hud.month_short", c.DeadlineMonth + 1)), Ui.Text(Loc.Enum(c.Status)),
                }));
        }
    }

    // Маркетинг (GDD 7.3): реклама, турниры, амбассадоры, сертификация.
    public sealed class MarketingPanel : Panel
    {
        public MarketingPanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.marketing";
        public override string Icon => "★";
        public override Feature? RequiredFeature => Feature.Marketing;

        protected override void Build(VisualElement root)
        {
            var ads = Ui.Section(root, Loc.Get("marketing.ads"));
            var budget = Ui.Field(Player.Sales.AdBudget.ToString(), 130);
            Ui.Row(Ui.Text(Loc.Get("marketing.budget"), "pair-label"), budget,
                Ui.Button(Loc.Get("ui.apply"), () => { var v = Ui.ParseLong(budget.value); if (v != null) Submit(new SetAdBudgetCommand(v.Value)); }, "small")).AddTo(ads);
            ads.Add(Ui.Hint(Loc.Get("marketing.ads_hint", Fmt.Money(Catalog.Marketing.AdBudgetScale * World.Economy.PriceLevel))));
            Ui.Pair(ads, Loc.Get("hud.reputation_label"), Fmt.Number(Player.Reputation));

            var tournaments = Ui.Section(root, Loc.Get("marketing.tournaments"));
            Ui.Table(tournaments, new float[] { 150, 110, 140, 90 },
                new[] { Loc.Get("sales.level"), Loc.Get("marketing.month"), Loc.Get("marketing.cost"), "" },
                World.Marketing.UpcomingTournaments.OrderBy(t => t.Month).Select(t =>
                {
                    bool mine = t.Sponsors.Contains(Player.Id);
                    var action = mine ? (VisualElement)Ui.Text(Loc.Get("marketing.sponsoring")) : Ui.Button(Loc.Get("marketing.sponsor"), () => Submit(new SponsorTournamentCommand(t.Id)), "tiny", "primary");
                    return new VisualElement[]
                    {
                        Ui.Text(Loc.Enum(t.Level)), Ui.Text(Loc.Get("hud.month_short", t.Month + 1)),
                        Ui.Text(Fmt.Money(World.Marketing.MarketingCost(Player, World.Marketing.SponsorshipCost(t.Level)))), action,
                    };
                }));

            var athletes = Ui.Section(root, Loc.Get("marketing.ambassadors"));
            Ui.Table(athletes, new float[] { 150, 70, 140, 120 },
                new[] { Loc.Get("marketing.athlete"), Loc.Get("marketing.rating"), Loc.Get("marketing.fee"), "" },
                Catalog.Athletes.OrderByDescending(a => a.Rating).Select(a =>
                {
                    string owner = World.Marketing.AmbassadorOwner(a.Id);
                    VisualElement action = owner == Player.Id ? Ui.Button(Loc.Get("marketing.release"), () => Submit(new ReleaseAmbassadorCommand(a.Id)), "tiny", "danger")
                        : owner != null ? Ui.Text(Loc.Name(owner)) : Ui.Button(Loc.Get("marketing.sign"), () => Submit(new HireAmbassadorCommand(a.Id)), "tiny", "primary");
                    return new VisualElement[] { Ui.Text(Loc.Name(a.Id)), Ui.Text(Fmt.Number(a.Rating)), Ui.Text(Fmt.Money(a.MonthlyFee * World.Economy.PriceLevel)), action };
                }));

            if (!World.Rules.Allows(Feature.Certification)) return;
            var certification = Ui.Section(root, Loc.Get("marketing.certification"));
            certification.Add(Ui.Hint(Loc.Get("marketing.certification_hint", Fmt.Money(Catalog.Marketing.CertificationCost * World.Economy.PriceLevel), Fmt.Number(Catalog.Marketing.CertificationQuality))));
            int month = World.Simulation.MonthIndex;
            var bodies = Enum.GetValues(typeof(CertificationBody)).Cast<CertificationBody>().ToList();
            foreach (var product in World.Rules.OpenProducts(Catalog))
            {
                var p = product;
                var certificate = Player.Sales.Certificates.Where(c => c.ProductId == p.Id).OrderByDescending(c => c.ReadyMonth).FirstOrDefault();
                string status = certificate == null ? Loc.Get("marketing.no_certificate")
                    : certificate.IsValid(month) ? Loc.Get("marketing.certificate_valid", Loc.Enum(certificate.Body), certificate.ValidUntilMonth + 1)
                    : !certificate.Checked ? Loc.Get("marketing.certificate_pending", certificate.ReadyMonth + 1)
                    : Loc.Get("marketing.certificate_failed");
                int body = 0;
                Ui.Row(Ui.Text(Loc.Name(p.Id), "pair-label-short"), Ui.Text(status, "hint", "grow"),
                    Ui.Dropdown(bodies.Select(b => b.ToString()).ToList(), 0, i => body = i, 90),
                    Ui.Button(Loc.Get("marketing.certify"), () => Submit(new CertifyProductCommand(p.Id, bodies[body])), "tiny")).AddTo(certification);
            }
        }
    }

    // Финансы (GDD 7.8): прибыли и убытки, деньги, кредиты, капитализация.
    public sealed class FinancePanel : Panel
    {
        public FinancePanel(GameApp app) : base(app) { }

        public override string TitleKey => "screen.finance";
        public override string Icon => "∑";

        protected override void Build(VisualElement root)
        {
            var balance = Ui.Section(root, Loc.Get("finance.balance"));
            Ui.Pair(balance, Loc.Get("finance.cash"), Fmt.Money(Player.Cash));
            Ui.Pair(balance, Loc.Get("finance.debt"), Fmt.Money(Player.Debt));
            Ui.Pair(balance, Loc.Get("finance.credit_rating"), Fmt.Number(World.Finance.CreditRating(Player), 2));
            if (Player.NegativeCashMonths > 0) balance.Add(Ui.Text(Loc.Get("finance.negative_warning", Player.NegativeCashMonths), "negative", "wrap"));

            var r = Player.LastReport;
            var pnl = Ui.Section(root, r == null ? Loc.Get("finance.pnl_none") : Loc.Get("finance.pnl", r.Month + 1));
            if (r != null)
            {
                Ui.Pair(pnl, Loc.Get("finance.revenue"), Fmt.Money(r.Revenue));
                Ui.Pair(pnl, Loc.Get("finance.contract_revenue"), Fmt.Money(r.ContractRevenue));
                Ui.Pair(pnl, Loc.Get("finance.cogs"), Fmt.Money(-r.CostOfGoodsSold));
                Ui.Pair(pnl, Loc.Get("finance.wages"), Fmt.Money(-(r.Wages + r.SocialTax)));
                Ui.Pair(pnl, Loc.Get("finance.rent"), Fmt.Money(-r.Rent));
                Ui.Pair(pnl, Loc.Get("finance.energy"), Fmt.Money(-r.Energy));
                Ui.Pair(pnl, Loc.Get("finance.other"), Fmt.Money(-(r.OtherExpenses + r.Penalties)));
                Ui.Pair(pnl, Loc.Get("finance.depreciation"), Fmt.Money(-r.Depreciation));
                Ui.Pair(pnl, Loc.Get("finance.interest"), Fmt.Money(-r.Interest));
                Ui.Pair(pnl, Loc.Get("finance.tax"), Fmt.Money(-r.ProfitTax));
                Ui.Pair(pnl, Loc.Get("finance.net_income"), Fmt.Money(r.NetIncome)).With("pair-total");
                Ui.Pair(pnl, Loc.Get("finance.capitalization"), Fmt.Money(r.Capitalization)).With("pair-total");
            }

            var loans = Ui.Section(root, Loc.Get("finance.loans"));
            Ui.Table(loans, new float[] { 140, 80, 90, 140 },
                new[] { Loc.Get("finance.principal"), Loc.Get("finance.rate"), Loc.Get("finance.term"), Loc.Get("finance.remaining") },
                Player.Loans.Select(l => new VisualElement[]
                {
                    Ui.Text(Fmt.Number(l.Principal)), Ui.Text(Fmt.Percent(l.AnnualRate, 1)), Ui.Text(Loc.Get("finance.months", l.TermMonths)), Ui.Text(Fmt.Number(l.Remaining)),
                }));
            if (!World.Rules.Allows(Feature.Loans)) return;
            loans.Add(Ui.Hint(Loc.Get("finance.loan_offer", Fmt.Percent(World.Finance.LoanRateFor(Player), 1), Fmt.Money(Catalog.Finance.MaxDebt))));
            var amount = Ui.Field("20000000", 130);
            var term = Ui.Field("12", 50);
            Ui.Row(Ui.Text(Loc.Get("finance.amount"), "pair-label-short"), amount, Ui.Text(Loc.Get("finance.term"), "pair-label-short"), term,
                Ui.Button(Loc.Get("finance.take_loan"), () =>
                {
                    var a = Ui.ParseLong(amount.value);
                    var t = Ui.ParseLong(term.value);
                    if (a != null && t != null) Submit(new TakeLoanCommand(a.Value, (int)t.Value));
                }, "small", "primary")).AddTo(loans);
        }
    }
}
