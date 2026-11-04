using System.Collections.Generic;
using System.Linq;
using Geminium.Core.Companies;
using Geminium.Core.Data;
using Geminium.Core.Factory;

namespace Geminium.Core
{
    // Что открыто в партии (GDD 4.1): в свободном режиме — всё, в задании кампании — только то, что задание открывает.
    // Ядро проверяет правила при подаче любой команды, поэтому закрытое нельзя сделать ни из интерфейса, ни ботом.
    public sealed class GameRules
    {
        private readonly HashSet<string> products;       // null — продукты из данных (UnlockedByDefault)
        private readonly HashSet<Feature> features;      // null — все возможности
        private readonly HashSet<DigitalModule> modules; // null — все модули

        public const string LockedMessage = "Это откроется в следующих заданиях кампании.";

        public GameRules(IEnumerable<string> products = null, IEnumerable<Feature> features = null, IEnumerable<DigitalModule> modules = null)
        {
            this.products = products == null ? null : new HashSet<string>(products);
            this.features = features == null ? null : new HashSet<Feature>(features);
            this.modules = modules == null ? null : new HashSet<DigitalModule>(modules);
        }

        public static GameRules Free => new GameRules();

        public bool IsProductOpen(ProductDef product) => products == null ? product.UnlockedByDefault : products.Contains(product.Id);

        public bool Allows(Feature feature) => features == null || features.Contains(feature);

        public bool IsModuleOpen(DigitalModule module) => Allows(Feature.Digital) && (modules == null || modules.Contains(module));

        public IEnumerable<ProductDef> OpenProducts(DataCatalog catalog) => catalog.Products.Where(IsProductOpen);

        // null — команда разрешена правилами партии; иначе — почему нет.
        public string Check(CompanyCommand command, GameWorld world)
        {
            switch (command)
            {
                case BatchCommand _:
                    return null; // подкоманды пакета проверяются при применении и закрытые пропускаются
                case PlaceObjectCommand place:
                    if (place.Kind == FloorObjectKind.Rack) return Need(Feature.Logistics);
                    if (place.Kind == FloorObjectKind.QcTable) return Need(Feature.Quality);
                    if (place.Kind == FloorObjectKind.Machine && world.Catalog.Machines.Any(m => m.Id == place.MachineId)
                        && world.Catalog.Machine(place.MachineId).Zone == ProductionZone.Molding) return Need(Feature.Molding);
                    return null;
                case SetTransportBatchCommand _:
                case SetFeedSideCommand _:
                    return Need(Feature.Logistics);
                case SetInputQcCommand _:
                case SetDefectPolicyCommand _:
                    return Need(Feature.Quality);
                case SetMaintenanceIntervalCommand _:
                    return Need(Feature.Maintenance);
                case SetAdBudgetCommand _:
                case SponsorTournamentCommand _:
                case HireAmbassadorCommand _:
                case ReleaseAmbassadorCommand _:
                    return Need(Feature.Marketing);
                case BidTenderCommand _:
                    return Need(Feature.Tenders);
                case TakeLoanCommand _:
                    return Need(Feature.Loans);
                case CertifyProductCommand _:
                    return Need(Feature.Certification);
                case BuyDigitalModuleCommand buy:
                    return IsModuleOpen(buy.Module) ? null : LockedMessage;
                case DisableDigitalModuleCommand _:
                case SetAutoPricingCommand _:
                    return Need(Feature.Digital);
                case SetMonthlyPlanCommand plan:
                    return plan.Units > 0 && world.Catalog.Products.Any(p => p.Id == plan.ProductId)
                           && !IsProductOpen(world.Catalog.Product(plan.ProductId)) ? $"Продукт «{plan.ProductId}» ещё не открыт." : null;
                default:
                    return null;
            }
        }

        private string Need(Feature feature) => Allows(feature) ? null : LockedMessage;
    }
}
