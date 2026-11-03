using System.Collections.Generic;
using Geminium.Core.Data;

namespace Geminium.Core.Sales
{
    // Заявка компании на тендер: цена за единицу.
    public sealed class TenderBid
    {
        public string CompanyId;
        public long UnitPrice;
        public double Score;       // балл на момент подведения итогов
        public bool Eligible;      // прошла требования
    }

    // Тендер ИИ-ядра (GDD 7.2): публикуется в начале месяца, заявки — до конца месяца.
    public sealed class Tender
    {
        public int Id;
        public TenderLevel Level;
        public string ProductId;
        public int Units;
        public long MaxUnitPrice;       // P_max
        public int DeliveryMonths;      // срок поставки после подведения итогов
        public int PublishedMonth;
        public readonly List<TenderBid> Bids = new List<TenderBid>();
        public string WinnerId;         // null — заявок не было или никто не прошёл требования
        public bool Closed;
    }

    public enum ContractStatus
    {
        Active = 0,
        Completed = 1,
        Terminated = 2,
    }

    // Контракт по выигранному тендеру: поставить партию к концу месяца DeadlineMonth.
    public sealed class Contract
    {
        public int Id;
        public int TenderId;
        public TenderLevel Level;
        public string ProductId;
        public int Units;
        public int Delivered;
        public long UnitPrice;
        public int AwardedMonth;
        public int DeadlineMonth;       // последний месяц, в котором поставка ещё в срок
        public int WeeksLate;
        public ContractStatus Status;

        public int Remaining => Units - Delivered;
        public long Value => (long)Units * UnitPrice;
    }

    // Сертификат на продукт (GDD 7.3). Пока идёт проверка — ReadyMonth в будущем.
    public sealed class Certificate
    {
        public string ProductId;
        public CertificationBody Body;
        public int ReadyMonth;          // в начале этого месяца проверка заканчивается
        public int ValidUntilMonth;     // действует по этот месяц включительно (после проверки)
        public bool Checked;            // проверка завершена
        public bool Passed;

        public bool IsValid(int month) => Checked && Passed && month <= ValidUntilMonth;
    }

    // Турнир: объявляется за месяц, проходит в начале своего месяца.
    public sealed class Tournament
    {
        public int Id;
        public TournamentLevel Level;
        public int Month;               // месяц проведения
        public readonly List<string> Sponsors = new List<string>();
        public string WinnerAthleteId;  // победил амбассадор (если чей-то)
    }

    // Сбыт и маркетинг компании: контракты, сертификаты, реклама, амбассадоры, история качества B2B.
    public sealed class SalesState
    {
        internal readonly List<Contract> contracts = new List<Contract>();
        internal readonly List<Certificate> certificates = new List<Certificate>();
        internal readonly List<string> ambassadors = new List<string>();
        internal readonly Dictionary<TenderLevel, int> sponsorshipBonusUntil = new Dictionary<TenderLevel, int>();

        public IReadOnlyList<Contract> Contracts => contracts;
        public IReadOnlyList<Certificate> Certificates => certificates;
        public IReadOnlyList<string> Ambassadors => ambassadors;

        public long AdBudget { get; internal set; }       // в месяц, списывается в начале месяца
        public double AdStock { get; internal set; }      // накопленный эффект рекламы, сумы
        public int B2bBatches { get; internal set; }      // поставок по контрактам
        public int B2bComplaints { get; internal set; }   // из них с рекламациями

        // QH — история качества: доля партий без рекламаций, 0–100 (до первой поставки — 100).
        public double QualityHistory => 100.0 * (B2bBatches - B2bComplaints + 1) / (B2bBatches + 1);

        public bool HasValidCertificate(string productId, int month)
        {
            foreach (var c in certificates)
                if (c.ProductId == productId && c.IsValid(month)) return true;
            return false;
        }

        // Бонус спонсора к тендерам этого уровня ещё действует.
        public bool HasSponsorshipBonus(TenderLevel level, int month) =>
            sponsorshipBonusUntil.TryGetValue(level, out int until) && month <= until;

        // Сколько единиц продукта ещё нужно поставить по активным контрактам.
        public int CommittedUnits(string productId)
        {
            int units = 0;
            foreach (var c in contracts)
                if (c.Status == ContractStatus.Active && c.ProductId == productId) units += c.Remaining;
            return units;
        }
    }
}
