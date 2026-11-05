using System.Linq;
using Geminium.Core.Data;

namespace Geminium.Core.Companies
{
    // Решения по сбыту и маркетингу (GDD 7.2, 7.3).

    // Заявка на тендер: цена за единицу. Повторная заявка заменяет прежнюю.
    public sealed class BidTenderCommand : CompanyCommand
    {
        public int TenderId { get; }
        public long UnitPrice { get; }

        public BidTenderCommand(int tenderId, long unitPrice)
        {
            TenderId = tenderId;
            UnitPrice = unitPrice;
        }

        public override string Validate(Company company, GameWorld world) => world.Tenders.CheckBid(company, TenderId, UnitPrice);

        internal override void Apply(Company company, GameWorld world) => world.Tenders.PlaceBid(company, TenderId, UnitPrice);
    }

    // Месячный бюджет таргетированной рекламы. Списывается в начале каждого месяца; 0 — выключить.
    public sealed class SetAdBudgetCommand : CompanyCommand
    {
        public long MonthlyBudget { get; }

        public SetAdBudgetCommand(long monthlyBudget) { MonthlyBudget = monthlyBudget; }

        public override string Validate(Company company, GameWorld world) =>
            MonthlyBudget < 0 ? CoreText.T("err.ad_negative", "Бюджет рекламы не может быть отрицательным.") : null;

        internal override void Apply(Company company, GameWorld world) => company.Sales.AdBudget = MonthlyBudget;
    }

    // Спонсорство объявленного турнира: разовая плата сразу.
    public sealed class SponsorTournamentCommand : CompanyCommand
    {
        public int TournamentId { get; }

        public SponsorTournamentCommand(int tournamentId) { TournamentId = tournamentId; }

        public override string Validate(Company company, GameWorld world)
        {
            string error = world.Marketing.CheckSponsorship(company, TournamentId);
            if (error != null) return error;
            var tournament = world.Marketing.Tournaments.First(t => t.Id == TournamentId);
            return CheckCash(company, world.Marketing.MarketingCost(company, world.Marketing.SponsorshipCost(tournament.Level)));
        }

        internal override void Apply(Company company, GameWorld world) => world.Marketing.Sponsor(company, TournamentId);
    }

    // Контракт с амбассадором: гонорар — в начале каждого месяца.
    public sealed class HireAmbassadorCommand : CompanyCommand
    {
        public string AthleteId { get; }

        public HireAmbassadorCommand(string athleteId) { AthleteId = athleteId; }

        public override string Validate(Company company, GameWorld world) => world.Marketing.CheckAmbassador(AthleteId);

        internal override void Apply(Company company, GameWorld world) => world.Marketing.HireAmbassador(company, AthleteId);
    }

    // Расторгнуть контракт с амбассадором.
    public sealed class ReleaseAmbassadorCommand : CompanyCommand
    {
        public string AthleteId { get; }

        public ReleaseAmbassadorCommand(string athleteId) { AthleteId = athleteId; }

        public override string Validate(Company company, GameWorld world) =>
            company.Sales.Ambassadors.Contains(AthleteId) ? null : CoreText.T("err.not_ambassador", "«{0}» не амбассадор компании.", AthleteId);

        internal override void Apply(Company company, GameWorld world) => world.Marketing.Release(company, AthleteId);
    }

    // Сертификация продукта в федерации: плата сразу, проверка 2–3 месяца, затем сертификат на 2 года.
    public sealed class CertifyProductCommand : CompanyCommand
    {
        public string ProductId { get; }
        public CertificationBody Body { get; }

        public CertifyProductCommand(string productId, CertificationBody body)
        {
            ProductId = productId;
            Body = body;
        }

        public override string Validate(Company company, GameWorld world) =>
            world.Marketing.CheckCertification(company, ProductId, Body)
            ?? CheckCash(company, (long)System.Math.Round(world.Catalog.Marketing.CertificationCost * world.Economy.PriceLevel));

        internal override void Apply(Company company, GameWorld world) => world.Marketing.StartCertification(company, ProductId, Body);
    }
}
