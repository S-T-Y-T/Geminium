// Генератор GameDatabase.asset (Unity YAML) и того же набора в JSON (для быстрых прогонов ядра в .NET).
// Стартовые данные этапов 1–4 (заглушки до калибровки). После генерации источник истины — файл данных:
// генератор перезаписывает его целиком, поэтому правки из инспектора Unity сначала переносите сюда.
// Запуск из корня проекта: node Tools/gen_database.js Assets/Geminium/Data/GameDatabase.asset <файл.json для LabRun>.
const fs = require('fs');
const [assetOut, jsonOut] = process.argv.slice(2);

const M = (Id, Group, Unit, Origin, BasePrice, Volatility, UnitsPerCell, MarketSupplyPerMonth) =>
  ({ Id, Group, Unit, Origin, BasePrice, Volatility, UnitsPerCell, MarketSupplyPerMonth });
const S = (Id, MaterialId, Origin, PriceDeviation, Reliability, QualityMin, QualityMax, MinBatch, LeadTimeDays) =>
  ({ Id, MaterialId, Origin, PriceDeviation, Reliability, QualityMin, QualityMax, MinBatch, LeadTimeDays });
const Mc = (Id, Zone, Tier, Power, EnergyKw, DurabilityOperations, Price, SizeX, SizeY) =>
  ({ Id, Zone, Tier, Power, EnergyKw, DurabilityOperations, Price, SizeX, SizeY });
const Op = (Zone, BaseTimeMinutes, inputs) => ({ Zone, BaseTimeMinutes, Inputs: inputs.map(([MaterialId, Amount]) => ({ MaterialId, Amount })) });
const Goal = (Metric, Target, Zero, ProductId = '', Weight = 1) => ({ Metric, Target, Zero, ProductId, Weight });
const Mission = (m) => ({ Id: '', Months: 2, Rivals: false, Layout: 0, LayoutArchetype: 'balanced', StartingCash: 0, StartCondition: 100, Products: [], Features: [],
  Modules: [], Tenders: [], Goals: [], PassScore: 60, ...m });
const P = (Id, BasePrice, BaseDemandPerMonth, BaseDefectChance, UnitsPerCell, UnlockedByDefault, Operations) =>
  ({ Id, BasePrice, BaseDemandPerMonth, BaseDefectChance, UnitsPerCell, UnlockedByDefault, Operations });
const E = (Id, MonthlyProbability, MinDurationMonths, MaxDurationMonths, SeasonStartMonth, SeasonEndMonth, Effect, Magnitude, Target) =>
  ({ Id, MonthlyProbability, MinDurationMonths, MaxDurationMonths, SeasonStartMonth, SeasonEndMonth, Effect, Magnitude, Target });

const hoguOps = [Op(1, 20, [['pvc', 0.6], ['polyester', 0.4]]), Op(3, 40, [['pu_foam', 0.5]]), Op(2, 50, [['velcro', 0.6], ['elastic_band', 1.0], ['reinforced_thread', 0.08]])];

const Arch = (o) => Object.assign({
  Id: '', PriceFactor: 1.0, MinPriceFactor: 0.85, MaxPriceFactor: 1.2, PreferQuality: false, MachineTier: 1, PlanGrowth: 1.05, QcStation: true,
  InputQc: false, Mechanic: true, Hiring: 0, DefectPolicy: 0, MaintenanceInterval: 400, RawRacks: 4, FinishedRacks: 3, SalesManagers: 0,
  AdBudgetShare: 0.03, MaxAdBudgetShare: 0.1, TenderMarkup: 0.9, TenderAppetite: 0.3, Certify: false, SponsorUpTo: 0, Ambassadors: 0, HillClimbStep: 0.05,
}, o);
const data = {
  Materials: [
    M('cotton', 0, 0, 0, 25000, 0.10, 200, 20000), M('polyester', 0, 0, 2, 18000, 0.10, 200, 15000),
    M('eva_foam', 1, 0, 1, 60000, 0.15, 40, 3000), M('pu_foam', 1, 0, 1, 75000, 0.15, 40, 2500),
    M('eco_leather', 1, 0, 2, 55000, 0.12, 100, 4000), M('pvc', 1, 0, 2, 30000, 0.12, 100, 5000),
    M('velcro', 2, 1, 1, 6000, 0.08, 500, 20000), M('elastic_band', 2, 1, 0, 4000, 0.08, 500, 25000),
    M('reinforced_thread', 2, 2, 2, 15000, 0.08, 300, 3000), M('sensor_module', 3, 2, 1, 450000, 0.20, 100, 500),
  ],
  Suppliers: [
    S('cotton_tashkent', 'cotton', 0, 0.0, 0.92, 55, 75, 50, 2), S('cotton_fergana', 'cotton', 0, -0.05, 0.85, 45, 70, 100, 3),
    S('polyester_local', 'polyester', 0, 0.05, 0.88, 50, 70, 50, 3), S('polyester_import', 'polyester', 1, -0.08, 0.80, 60, 85, 200, 10),
    S('eva_import_a', 'eva_foam', 1, 0.0, 0.85, 60, 85, 20, 12), S('eva_import_b', 'eva_foam', 1, -0.10, 0.75, 45, 70, 40, 15),
    S('pu_import_a', 'pu_foam', 1, 0.0, 0.85, 60, 85, 20, 12), S('pu_import_b', 'pu_foam', 1, 0.08, 0.92, 70, 90, 10, 8),
    S('eco_leather_local', 'eco_leather', 0, 0.0, 0.88, 50, 70, 30, 3), S('eco_leather_import', 'eco_leather', 1, 0.10, 0.90, 70, 90, 50, 10),
    S('pvc_local', 'pvc', 0, 0.0, 0.90, 50, 75, 30, 2), S('pvc_import', 'pvc', 1, -0.05, 0.80, 60, 80, 100, 12),
    S('velcro_import', 'velcro', 1, 0.0, 0.88, 60, 85, 100, 10), S('velcro_trader', 'velcro', 0, 0.12, 0.95, 55, 75, 50, 2),
    S('elastic_local', 'elastic_band', 0, 0.0, 0.90, 50, 75, 100, 2), S('elastic_import', 'elastic_band', 1, -0.05, 0.82, 60, 85, 300, 10),
    S('thread_local', 'reinforced_thread', 0, 0.0, 0.92, 55, 75, 10, 2), S('thread_import', 'reinforced_thread', 1, 0.05, 0.88, 70, 90, 20, 10),
    S('sensor_import_a', 'sensor_module', 1, 0.0, 0.85, 70, 95, 5, 20), S('sensor_import_b', 'sensor_module', 1, -0.10, 0.75, 55, 80, 10, 25),
  ],
  Machines: [
    Mc('cutting_knife_table', 1, 1, 1.0, 0.2, 5000, 3000000, 2, 1), Mc('cutting_band', 1, 2, 1.6, 1.5, 4000, 25000000, 2, 2), Mc('cutting_laser', 1, 3, 2.5, 4.0, 3500, 120000000, 3, 2),
    Mc('sewing_straight', 2, 1, 1.0, 0.4, 4000, 4000000, 1, 1), Mc('sewing_overlock', 2, 2, 1.4, 0.6, 3500, 9000000, 1, 1), Mc('sewing_automatic', 2, 3, 2.2, 1.2, 3000, 45000000, 2, 1),
    Mc('molding_hand_press', 3, 1, 1.0, 0.5, 3000, 6000000, 1, 1), Mc('molding_thermo_press', 3, 2, 1.5, 3.0, 2500, 30000000, 2, 1), Mc('molding_vacuum', 3, 3, 2.3, 6.0, 2200, 95000000, 2, 2),
    Mc('electronics_bench', 4, 1, 1.0, 0.3, 6000, 15000000, 2, 1),
  ],
  Products: [
    P('dobok', 450000, 2000, 0.04, 20, true, [Op(1, 25, [['cotton', 2.5]]), Op(2, 60, [['reinforced_thread', 0.1]])]),
    P('helmet', 350000, 800, 0.06, 10, true, [Op(3, 30, [['eva_foam', 0.35]]), Op(1, 10, [['eco_leather', 0.3]]), Op(2, 35, [['velcro', 0.3], ['reinforced_thread', 0.05]])]),
    P('hogu', 600000, 600, 0.07, 8, true, hoguOps),
    P('shin_forearm_guards', 250000, 1200, 0.05, 25, true, [Op(3, 25, [['eva_foam', 0.4]]), Op(2, 30, [['polyester', 0.3], ['elastic_band', 1.2], ['velcro', 0.4]])]),
    P('e_vest', 4500000, 50, 0.10, 8, false, [...hoguOps, Op(4, 45, [['sensor_module', 1]])]),
  ],
  Professions: [
    { Id: 'laborer', BaseMonthlyWage: 3000000, OperatedZone: 0 }, { Id: 'cutter', BaseMonthlyWage: 4000000, OperatedZone: 1 },
    { Id: 'seamstress', BaseMonthlyWage: 3800000, OperatedZone: 2 }, { Id: 'molder', BaseMonthlyWage: 4200000, OperatedZone: 3 },
    { Id: 'mechanic', BaseMonthlyWage: 5500000, OperatedZone: 0 }, { Id: 'qc_inspector', BaseMonthlyWage: 4500000, OperatedZone: 0 },
    { Id: 'sales_manager', BaseMonthlyWage: 6000000, OperatedZone: 0 },
  ],
  Events: [
    E('eva_price_spike', 0.06, 1, 3, 0, 0, 1, 0.3, 'eva_foam'), E('cotton_price_spike', 0.06, 1, 3, 0, 0, 1, 0.3, 'cotton'),
    E('supplier_force_majeure', 0.08, 1, 1, 0, 0, 6, 0.3, ''), E('bad_raw_batch', 0.05, 1, 1, 0, 0, 7, -25, ''),
    E('currency_shock', 0.03, 3, 6, 0, 0, 2, 0.15, ''), E('digital_uzbekistan_program', 0.08, 3, 6, 0, 0, 8, 0.5, ''),
    E('power_restrictions', 0.05, 1, 1, 0, 0, 5, -0.15, ''), E('flu_epidemic', 0.15, 1, 1, 12, 2, 5, -0.15, ''),
    E('sports_boom', 0.05, 2, 3, 0, 0, 3, 0.2, ''),
  ],
  Calendar: { StartYear: 2027, StartMonth: 1 },
  DefaultWorkSchedule: { WorkingDaysPerWeek: 5, ShiftsPerDay: 1, ShiftLengthMinutes: 480, FirstShiftStartMinute: 480 },
  Pacing: { SecondsPerShiftAtX1: 40, NonWorkingSpeedup: 60, MaxTicksPerAdvance: 20000, AutoPauseAtMonthEnd: true },
  StartingCash: 80000000,
  Economy: {
    InitialIndex: 1, IndexReversion: 0.1, IndexVolatility: 0.06, IndexMin: 0.5, IndexMax: 1.5, InitialInflation: 0.085, InflationTarget: 0.05,
    InflationReversion: 0.08, InflationIndexSensitivity: 0.01, InflationVolatility: 0.004, KeyRateNeutral: 0.1, KeyRateInflationSensitivity: 1,
    KeyRateMin: 0.05, KeyRateMax: 0.3, SeasonalityAmplitude: 0.3, SeasonalityPeakMonth: 9, ElasticityCrisisSensitivity: 1, CapitalizationMultiplierMonths: 60,
  },
  Market: { QualityWeight: 1, ReputationWeight: 0.5, PriceElasticity: 1.5, MarketSizeElasticity: 0.5, DefaultReputation: 50 },
  Finance: {
    ProfitTaxRate: 0.15, SocialTaxRate: 0.12, BankMargin: 0.04, TrustDiscount: 0.06, OverdraftMargin: 0.1, DepreciationMonths: 60,
    MaxLoanTermMonths: 36, MaxDebt: 300000000, NegativeCashMonthsToBankrupt: 2, MaxOverdueMonths: 1,
  },
  Supply: {
    LoyaltyDiscountPerMillion: 0.001, MaxLoyaltyDiscount: 0.1, WeeklyDiscount: 0.05, TwiceWeeklyDiscount: 0.02, DailyDiscount: 0,
    PriceRatioMin: 0.5, PriceRatioMax: 2, MaxDelayDays: 5, FailureShare: 0.2,
  },
  Warehouse: { StartingCells: 20, DailyRent: 150000, DailyCostPerOccupiedCell: 2000, OverflowPenaltyPerUnit: 500, CellsPerRack: 10, RackPrice: 2500000 },
  Production: { WearExponent: 2, EnergyPricePerKwh: 1000, MismatchEfficiency: 0.6, DefaultTransportBatch: 5, MaxTransportBatch: 20, FlushMinutes: 120, RequestIntervalMinutes: 10, CuttingWasteShare: 0.12 },
  Floor: {
    BuildingWidth: 32, BuildingHeight: 20, CellSizeMeters: 2, ExpansionColumns: 8, MaxExpansions: 2, ExpansionMonthlyRent: 5000000,
    WalkMetersPerMinute: 40, LoadMinutes: 1, RearFeedExtraMinutes: 0.5, RelocationCostShare: 0.05, QcTablePrice: 3000000, DeskPrice: 1000000,
  },
  Maintenance: { DefaultIntervalOperations: 400, PlannedCostShare: 0.01, PlannedMinutes: 120, UnplannedCostShare: 0.05, UnplannedMinutes: 480, NoMechanicFactor: 2, ConditionAfterRepair: 80 },
  Staff: {
    LaborerProfessionId: 'laborer', MechanicProfessionId: 'mechanic', QcInspectorProfessionId: 'qc_inspector', SalesManagerProfessionId: 'sales_manager', CandidatesPerProfession: 4,
    CandidateExperienceMean: 35, CandidateExperienceSd: 20, WageExperienceFactor: 0.004, FatiguePerHour: 8, FatigueRecoveryPerHour: 6,
    ExperiencePerHour: 0.02, MaxOvertimeHours: 4, OvertimePayFactor: 1.5, OvertimeFatigueFactor: 1.5, OvertimeDaysTolerance: 8,
    MoralePerExcessOvertimeDay: 1.5, MoralePerBreakdown: 2, MoraleWageGapFactor: 0.5, MoraleRecoveryPerMonth: 2, StartingMorale: 70,
    QuitMorale: 25, SeveranceMonths: 1, TrainingCost: 1500000, TrainingExperienceGain: 10,
  },
  Quality: { OutputQcDetection: 0.9, QcUnitsPerHour: 60, InputQcThreshold: 40, RepairMinutes: 20, RepairQualityPenalty: 10, DiscountPriceShare: 0.4, ReturnFee: 15000 },
  Tenders: {
    MinTendersPerMonth: 1, MaxTendersPerMonth: 3, ReferenceDemand: 2000, WeightPrice: 0.5, WeightReputation: 0.2, WeightQualityHistory: 0.2,
    WeightManagers: 0.1, ManagerBonusPerManager: 0.5, SponsorshipScoreBonus: 0.1, SponsorshipBonusMonths: 6, LatePenaltyPerWeek: 0.02,
    MaxWeeksLate: 4, TerminationReputationLoss: 10, ComplaintReputationLoss: 2, FederationMinReputation: 30,
    Levels: [
      { Level: 0, MinUnits: 50, MaxUnits: 200, MinReputation: 0, CertificateRequired: false, CertificateBonus: 0, MinDeliveryMonths: 1, MaxDeliveryMonths: 1, Weight: 0.4, MaxPriceShare: 0.7 },
      { Level: 1, MinUnits: 100, MaxUnits: 500, MinReputation: 30, CertificateRequired: false, CertificateBonus: 0, MinDeliveryMonths: 1, MaxDeliveryMonths: 2, Weight: 0.35, MaxPriceShare: 0.65 },
      { Level: 2, MinUnits: 500, MaxUnits: 1500, MinReputation: 40, CertificateRequired: false, CertificateBonus: 0.1, MinDeliveryMonths: 2, MaxDeliveryMonths: 2, Weight: 0.18, MaxPriceShare: 0.6 },
      { Level: 3, MinUnits: 1000, MaxUnits: 3000, MinReputation: 50, CertificateRequired: true, CertificateBonus: 0, MinDeliveryMonths: 2, MaxDeliveryMonths: 3, Weight: 0.07, MaxPriceShare: 0.55 },
    ],
  },
  Marketing: {
    AdMaxBoost: 0.3, AdBudgetScale: 5000000, AdCarryover: 0.5, ReputationReversion: 0.05, ReturnReputationPerPercent: 0.3,
    RegionalTournamentChance: 0.5, NationalTournamentChance: 0.15, WorldTournamentChance: 0.04,
    RegionalSponsorshipCost: 8000000, NationalSponsorshipCost: 25000000, WorldSponsorshipCost: 70000000,
    RegionalSponsorshipReputation: 2, NationalSponsorshipReputation: 5, WorldSponsorshipReputation: 10,
    WinChanceScale: 0.6, RegionalWinExponent: 1, NationalWinExponent: 3, WorldWinExponent: 6, AmbassadorWinGainMin: 0.15, AmbassadorWinGainMax: 0.2,
    ScandalChance: 0.02, ScandalReputationLoss: 8, CertificationCost: 12000000, CertificationMinMonths: 2, CertificationMaxMonths: 3,
    CertificationQuality: 60, CertificateValidityMonths: 24, CertifiedTournamentBonus: 0.1, ManagerMarketingDiscount: 0.05, MaxManagerMarketingDiscount: 0.2,
  },
  Athletes: [
    { Id: 'athlete_1', Rating: 95, MonthlyFee: 12000000 }, { Id: 'athlete_2', Rating: 88, MonthlyFee: 9000000 },
    { Id: 'athlete_3', Rating: 82, MonthlyFee: 7000000 }, { Id: 'athlete_4', Rating: 75, MonthlyFee: 5000000 },
    { Id: 'athlete_5', Rating: 68, MonthlyFee: 3500000 }, { Id: 'athlete_6', Rating: 60, MonthlyFee: 2500000 },
    { Id: 'athlete_7', Rating: 52, MonthlyFee: 1500000 }, { Id: 'athlete_8', Rating: 45, MonthlyFee: 1000000 },
  ],
  BotArchetypes: [
    Arch({ Id: 'balanced', SalesManagers: 1 }),
    Arch({ Id: 'premium', PriceFactor: 1.2, MinPriceFactor: 1.0, MaxPriceFactor: 1.5, PreferQuality: true, MachineTier: 2, InputQc: true,
      Hiring: 1, DefectPolicy: 2, MaintenanceInterval: 300, SalesManagers: 2, AdBudgetShare: 0.06, MaxAdBudgetShare: 0.15, TenderMarkup: 0.95,
      TenderAppetite: 0.4, Certify: true, SponsorUpTo: 2, Ambassadors: 1 }),
    Arch({ Id: 'dumper', PriceFactor: 0.85, MinPriceFactor: 0.7, MaxPriceFactor: 1.0, PlanGrowth: 1.1, QcStation: false, Mechanic: false,
      Hiring: 2, MaintenanceInterval: 800, RawRacks: 3, FinishedRacks: 2, AdBudgetShare: 0.01, MaxAdBudgetShare: 0.05, TenderMarkup: 0.8, TenderAppetite: 0.5 }),
  ],
  Surrogate: { Utilization: 1.43, PlanFulfilment: 0.92, DefectMultiplier: 1.578, EnergyPerMinute: 8.3, MaintenancePerUnit: 569, CalibrationSamples: 552, OutputR2: 0.958, DefectR2: 0.712 },
  Digital: {
    ConditionErrorWithoutIot: 15, PredictiveConditionThreshold: 55, TwinRelocationCostShare: 0.01, TwinReviewMonths: 3, TwinMinSavingCells: 2,
    AiCuttingWasteShare: 0.04, AiQualityDetection: 0.97, PricingMinFactor: 0.7, PricingMaxFactor: 1.6, PricingStep: 0.05, DepreciationMonths: 36, LabPurchaseFromMonth: 3, LabCashReserve: 3,
  },
  DigitalModules: [
    { Module: 0, Capex: 30000000, MonthlyLicense: 1500000, Requires: -1 },
    { Module: 1, Capex: 20000000, MonthlyLicense: 2000000, Requires: 0 },
    { Module: 2, Capex: 40000000, MonthlyLicense: 3000000, Requires: -1 },
    { Module: 3, Capex: 25000000, MonthlyLicense: 2000000, Requires: -1 },
    { Module: 4, Capex: 35000000, MonthlyLicense: 2500000, Requires: -1 },
    { Module: 5, Capex: 15000000, MonthlyLicense: 1500000, Requires: -1 },
    { Module: 6, Capex: 15000000, MonthlyLicense: 2000000, Requires: -1 },
  ],
  Scenarios: [
    { Id: 'a_baseline', Modules: [], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'b_predictive_maintenance', Modules: [0, 1], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'c0_naive_layout', Modules: [], Subsidy: false, NaiveLayout: true, Baseline: '' },
    { Id: 'c_digital_twin', Modules: [2], Subsidy: false, NaiveLayout: true, Baseline: 'c0_naive_layout' },
    { Id: 'd_ai_cutting', Modules: [3], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'e_ai_quality', Modules: [4], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'f_demand_forecast', Modules: [5], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'g_ai_pricing', Modules: [6], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'h_all_modules', Modules: [0, 1, 2, 3, 4, 5, 6], Subsidy: false, NaiveLayout: false, Baseline: '' },
    { Id: 'i_all_modules_subsidy', Modules: [0, 1, 2, 3, 4, 5, 6], Subsidy: true, NaiveLayout: false, Baseline: '' },
  ],
  // Кампания (GDD 4.1). Возможности: 0 формовка, 1 логистика, 2 качество, 3 ТО, 4 маркетинг, 5 тендеры, 6 кредит,
  // 7 сертификация, 8 цифровизация. Цех: 0 пустой, 1 готовый, 2 готовый с неудачной планировкой.
  // Цели: балл = 100 × (факт − Zero) / (Target − Zero) в пределах 0–100.
  Missions: [
    Mission({ Id: 'm1_first_shop', Months: 2, Layout: 0, Products: ['dobok'], Features: [],
      Goals: [Goal(0, 250, 0, 'dobok'), Goal(1, 5, 15)] }),
    Mission({ Id: 'm2_logistics', Months: 2, Layout: 2, Products: ['dobok'], Features: [1],
      Goals: [Goal(2, 30, 5, '', 2), Goal(0, 250, 50, 'dobok'), Goal(3, 0, 10)] }),
    Mission({ Id: 'm3_quality', Months: 2, Layout: 1, LayoutArchetype: 'dumper', Products: ['dobok'], Features: [1, 2],
      Goals: [Goal(1, 2.5, 6), Goal(4, 0, 8)] }),
    Mission({ Id: 'm4_equipment', Months: 3, Layout: 1, LayoutArchetype: 'dumper', StartCondition: 1, Products: ['dobok', 'helmet'],
      Features: [0, 1, 2, 3, 8], Modules: [0, 1], Goals: [Goal(5, 2, 12)] }),
    Mission({ Id: 'm5_market', Months: 3, Rivals: true, Layout: 1, Products: ['dobok', 'helmet'], Features: [0, 1, 2, 3, 4, 8], Modules: [0, 1],
      Goals: [Goal(6, 33, 15)] }),
    Mission({ Id: 'm6_contracts', Months: 3, Rivals: true, Layout: 1, Products: ['dobok', 'helmet', 'hogu'], Features: [0, 1, 2, 3, 4, 5, 6, 7, 8],
      Modules: [0, 1, 2, 3, 4, 5, 6], Tenders: [{ Month: 0, Level: 2, ProductId: 'dobok', Units: 200, DeliveryMonths: 2 }],
      Goals: [Goal(7, 1, 0.5)] }),
  ],
  FreeModeMonths: 36,
};

// ---------- Unity YAML ----------
function scalar(v) {
  if (typeof v === 'boolean') return v ? '1' : '0';
  if (typeof v === 'string') return v; // пустая строка — пустое значение
  return String(v);
}
function emit(obj, indent) {
  let out = '';
  const pad = ' '.repeat(indent);
  for (const [k, v] of Object.entries(obj)) {
    if (Array.isArray(v) && v.length > 0 && (v[0] === null || typeof v[0] !== 'object')) {
      // Список простых значений (например, модули сценария): Unity пишет каждый элемент строкой «- значение».
      out += `${pad}${k}:\n` + v.map((x) => `${pad}- ${scalar(x)}\n`).join('');
    } else if (Array.isArray(v)) {
      out += `${pad}${k}:\n`;
      for (const item of v) {
        const inner = emit(item, indent + 2);
        out += pad + '- ' + inner.slice(indent + 2);
      }
      if (v.length === 0) out = out.replace(new RegExp(`${pad}${k}:\\n$`), `${pad}${k}: []\n`);
    } else if (v !== null && typeof v === 'object') {
      out += `${pad}${k}:\n` + emit(v, indent + 2);
    } else {
      const s = scalar(v);
      out += s === '' ? `${pad}${k}: \n` : `${pad}${k}: ${s}\n`;
    }
  }
  return out;
}
const yaml = `%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: b59034a20f4feb9d280438e70edec7c0, type: 3}
  m_Name: GameDatabase
  m_EditorClassIdentifier:
  Data:
` + emit(data, 4);
if (assetOut) fs.writeFileSync(assetOut, yaml);
if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(data, null, 1));
console.log('yaml lines', yaml.split('\n').length);
