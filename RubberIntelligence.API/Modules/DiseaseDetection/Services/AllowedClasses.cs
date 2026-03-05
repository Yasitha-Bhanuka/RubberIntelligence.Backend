using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    /// <summary>
    /// ╔═══════════════════════════════════════════════════════════════════╗
    ///   SINGLE POINT OF TRUTH — Allowed output classes for each
    ///   detection type.  Edit THIS file to add / remove classes.
    ///   The ONNX models were trained on exactly these labels.
    /// ╚═══════════════════════════════════════════════════════════════════╝
    /// </summary>
    public static class AllowedClasses
    {
        // ── Leaf Disease (Type 0) — 9 trained classes ───────────────────
        public static readonly string[] LeafDiseaseClasses =
        {
            "Anthracnose",
            "Birds_eye",
            "Colletorichum",
            "Corynespora",
            "Dry_Leaf",
            "Healthy",
            "Leaf_Spot",
            "Pesta",
            "Powdery_mildew"
        };

        // ── Pest (Type 1) — 19 trained classes ─────────────────────────
        public static readonly string[] PestClasses =
        {
            "Whitefly",
            "Snail",
            "Weevil",
            "Thrips",
            "Slug",
            "Riptortus",
            "RedSpider",
            "Grasshopper",
            "Mediterranean fruit fly",
            "FieldCricket",
            "Mites",
            "Earwig",
            "Cabbage Looper",
            "Bugs",
            "Cutworm",
            "Cicadellidae",
            "Beetle",
            "Aphids",
            "Adristyrannus"
        };

        // ── Keyword → Trained Class mapping ─────────────────────────────
        // Used to map free-form API labels to the trained class list.
        // Each entry: keyword (lowercase) → trained class name.
        // The FIRST matching keyword wins, so order by specificity.

        private static readonly (string Keyword, string TrainedClass)[] LeafDiseaseMapping =
        {
            // Exact / primary matches
            ("anthracnose",    "Anthracnose"),
            ("water excess or uneven watering",    "Anthracnose"),
            ("fungi",    "Anthracnose"),
            ("bird",           "Birds_eye"),
            ("birds eye",           "Birds_eye"),
            ("pseudomonas",           "Birds_eye"),
            ("feeding damage by insects",           "Birds_eye"),
            ("colletotrichum", "Colletorichum"),
            ("colletorichum",  "Colletorichum"),
            ("dead plant", "Colletorichum"),
            ("corynespora",    "Corynespora"),
            ("nutrient deficiency", "Corynespora"),
            ("mechanical damage", "Corynespora"),
            ("dry leaf",       "Dry_Leaf"),
            ("dry_leaf",       "Dry_Leaf"),
            ("drought",        "Dry_Leaf"),
            ("desiccation",    "Dry_Leaf"),
            ("healthy",        "Healthy"),
            ("leaf spot",      "Leaf_Spot"),
            ("leaf_spot",      "Leaf_Spot"),
            ("cercospora",     "Leaf_Spot"),       // common leaf-spot pathogen
            ("septoria",       "Leaf_Spot"),        // another leaf-spot genus
            ("blight",         "Leaf_Spot"),         // generic blight → closest match
            ("pest",           "Pesta"),
            ("insect damage",  "Pesta"),
            ("powdery mildew", "Powdery_mildew"),
            ("powdery_mildew", "Powdery_mildew"),
            ("pucciniales", "Powdery_mildew"),
            ("oidium",         "Powdery_mildew"),   // scientific name for powdery mildew
            ("microstromatales", "Powdery_mildew"),   // scientific name for powdery mildew
        };

        private static readonly (string Keyword, string TrainedClass)[] PestMapping =
        {
            // Primary / common-name matches
            ("beetle",                "Beetle"),
            ("coleoptera",            "Beetle"),
            ("chafer",                "Beetle"),
            ("beet",                  "Beetle"),
            ("hyalophora cecropia",   "Beetle"),
            ("ladybug",               "Beetle"),
            ("coccinellidae",         "Beetle"),
            ("scarab",                "Beetle"),
            ("scarabaeidae",          "Beetle"),
            ("japanese beetle",       "Beetle"),
            ("popillia",              "Beetle"),
            ("agriopis",              "Beetle"),
            ("leaf beetle",           "Beetle"),
            ("chrysomelidae",         "Beetle"),
            ("june bug",              "Beetle"),
            ("may beetle",            "Beetle"),
            ("phyllophaga",           "Beetle"),
            ("anomala",               "Beetle"),
            ("buprestidae",           "Beetle"),
            ("achaea janata",         "Beetle"),
            ("eudocima",              "Beetle"),
            ("hypsoropha",            "Beetle"),
            ("darkling beetle",       "Beetle"),
            ("tenebrionidae",         "Beetle"),
            ("click beetle",          "Beetle"),
            ("elateridae",            "Beetle"),
            ("firefly",               "Beetle"),
            ("lampyridae",            "Beetle"),
            ("cerambycidae",          "Beetle"),
            ("whitefly",              "Whitefly"),
            ("bemisia",               "Whitefly"),        // Bemisia tabaci
            ("trialeurodes",          "Whitefly"),        // Trialeurodes vaporariorum
            ("aleurodicus",           "Whitefly"),        // Aleurodicus dispersus (Spiralling whitefly)
            ("aleurocanthus",         "Whitefly"),        // Aleurocanthus woglumi (Citrus blackfly/whitefly)
            ("diyaleurodes",          "Whitefly"),        // Dialeurodes citri
            ("paraleyrodes",          "Whitefly"),        // Paraleyrodes
            ("siphoninus",            "Whitefly"),        // Ash whitefly
            ("aleyrodidae",           "Whitefly"),        // Family name
            ("silverleaf whitefly",   "Whitefly"),        
            ("greenhouse whitefly",   "Whitefly"),
            ("sweetpotato whitefly",  "Whitefly"),
            ("phyllocnistis",         "Whitefly"),
            ("planococcus",           "Whitefly"),
            ("acyphas",               "Whitefly"),
            ("drosophila",            "Whitefly"),
            ("planococcus",           "Whitefly"),
            ("snail",                 "Snail"),
            ("helix",                 "Snail"),
            ("cephalaspis",           "Snail"),
            ("gastropod",             "Snail"),
            ("mollusk",               "Snail"),
            ("achatina",              "Snail"),
            ("pomacea",               "Snail"),
            ("lissachatina",          "Snail"),
            ("syrphus",        "Snail"),
            ("xanthomelon",        "Snail"),
            ("cornu",                 "Snail"),
            ("cepaea",      "Snail"),
            ("valvata",      "Snail"),
            ("apple snail",           "Snail"),
            ("giant african snail",   "Snail"),
            ("weevil",                "Weevil"),
            ("curculio",              "Weevil"),
            ("curculionidae",         "Weevil"),          // True weevils family
            ("otiorhynchus",          "Weevil"),          // Vine weevil / root weevil
            ("sitophilus",            "Weevil"),          // Grain/Rice weevils
            ("rhynchophorus",         "Weevil"),          // Palm weevils
            ("anthonomus",            "Weevil"),          // Boll weevils, pepper weevils
            ("cylas",                 "Weevil"),          // Sweet potato weevil
            ("cosmopolites",          "Weevil"),          // Banana root weevil
            ("diaprepes",             "Weevil"),          // Root weevil
            ("snout beetle",          "Weevil"),          // Common name
            ("root weevil",           "Weevil"),
            ("vine weevil",           "Weevil"),
            ("sibinia", "Weevil"),
            ("archarius", "Weevil"),
            ("lixus",   "Weevil"),
            ("ceutorhynchus",   "Weevil"),
            ("sphenophorus",          "Weevil"),          // Billbugs
            ("hypera",                "Weevil"),          // Clover leaf weevils
            ("thrip",                 "Thrips"),           // matches "thrips" and "thrip"
            ("thysanoptera",          "Thrips"),
            ("frankliniella",         "Thrips"),          // Western flower thrips
            ("scirtothrips",          "Thrips"),          // Citrus thrips
            ("caliothrips",           "Thrips"),          // Bean thrips
            ("haplothrips",           "Thrips"),
            ("heliothrips",           "Thrips"),          // Greenhouse thrips
            ("echinothrips",          "Thrips"),
            ("phlaeothripidae",       "Thrips"),          // Tube-tailed thrips family
            ("thripidae",             "Thrips"),          // Common thrips family
            ("taeniothrips",          "Thrips"),
            ("thunder fly",           "Thrips"),          // Common UK name
            ("thunderbug",            "Thrips"),
            ("elattoneura", "Thrips"),
            ("linepithema", "Thrips"),
            ("melanostoma", "Thrips"),
            ("entomobrya", "Thrips"),
            ("slug",                  "Slug"),
            ("arion",                 "Slug"),            // Arion vulgaris (Spanish slug)
            ("deroceras",             "Slug"),            // Grey field slug
            ("limax",                 "Slug"),            // Leopard slug
            ("agriolimax",            "Slug"),
            ("milax",                 "Slug"),            // Budapest slug
            ("veronicella",           "Slug"),            // Leatherleaf slug
            ("coleophora", "Slug"),
            ("limax", "Slug"),
            ("leidyula", "Slug"),
            ("henosepilachna", "Slug"),
            ("limacidae",             "Slug"),            // Slug family
            ("agriolimacidae",        "Slug"),
            ("philomycidae",          "Slug"),
            ("riptortus",             "Riptortus"),
            ("hyalymenus", "Riptortus"),
            ("camptopus", "Riptortus"),
            ("bean bug",              "Riptortus"),
            ("riptortus pedestris",   "Riptortus"),
            ("riptortus clavatus",    "Riptortus"),
            ("alydidae",              "Riptortus"),       // Broad-headed bug family
            ("leptocorisa",           "Riptortus"),       // Rice bug (similar family)
            ("cletus",                "Riptortus"),
            ("red spider",            "RedSpider"),
            ("trombidium", "RedSpider"),
            ("redspider",             "RedSpider"),
            ("tetranychus",           "RedSpider"),       // Tetranychus urticae (red spider mite)
            ("panonychus",            "RedSpider"),       // Citrus red mite
            ("oligonychus",           "RedSpider"),       // Red mites on rubber
            ("tetranychidae",         "RedSpider"),       // Spider mite family
            ("spider mite",           "RedSpider"),
            ("nesticodes", "RedSpider"),
            ("erythraeidea", "RedSpider"),
            ("palpimanus", "RedSpider"),
            ("araneus", "RedSpider"),
            ("two-spotted spider",    "RedSpider"),
            ("bryobia",               "RedSpider"),
            ("grasshopper",           "Grasshopper"),
            ("oulema", "Grasshopper"),
            ("mylabris", "Grasshopper"),
            ("trimerotropis", "Grasshopper"),
            ("epicauta", "Grasshopper"),
            ("trichodes", "Grasshopper"),
            ("strongylium", "Grasshopper"),
            ("lytta", "Grasshopper"),
            ("locust",                "Grasshopper"),
            ("pyrochroa", "Grasshopper"),
            ("acrididae",             "Grasshopper"),
            ("epicauta", "Grasshopper"),
            ("caelifera",             "Grasshopper"),     // Suborder
            ("schistocerca",          "Grasshopper"),     // Desert locust
            ("melanoplus",            "Grasshopper"),     // Differential grasshopper
            ("tettigoniidae",         "Grasshopper"),     // Katydids / bush crickets
            ("katydid",               "Grasshopper"),
            ("romalea",               "Grasshopper"),     // Eastern lubber grasshopper
            ("oxya",                  "Grasshopper"),     // Rice grasshopper
            ("atractomorpha",         "Grasshopper"),
            ("acrida",                "Grasshopper"),
            ("pyrgomorphidae",        "Grasshopper"),
            ("short-horned grasshopper", "Grasshopper"),
            ("long-horned grasshopper", "Grasshopper"),
            ("band-winged grasshopper", "Grasshopper"),

            // ── Mediterranean fruit fly ──
            ("mediterranean fruit",   "Mediterranean fruit fly"),
            ("ensina", "Mediterranean fruit fly"),
            ("ceratitis",             "Mediterranean fruit fly"),  // Ceratitis capitata
            ("medfly",                "Mediterranean fruit fly"),
            ("fly",                   "Mediterranean fruit fly"), // general fly fallback
            ("fruit fly",             "Mediterranean fruit fly"),
            ("bactrocera",            "Mediterranean fruit fly"),  // Oriental fruit fly
            ("anastrepha",            "Mediterranean fruit fly"),  // Caribbean fruit fly
            ("dacus",                 "Mediterranean fruit fly"),
            ("tephritidae",           "Mediterranean fruit fly"),  // True fruit flies family
            ("rhagoletis",            "Mediterranean fruit fly"),  // Apple maggot fly
            ("drosophilidae",         "Mediterranean fruit fly"),  // Vinegar flies

            // ── FieldCricket ──
            ("field cricket",         "FieldCricket"),
            ("fieldcricket",          "FieldCricket"),
            ("gryllus",               "FieldCricket"),
            ("cricket",               "FieldCricket"),    // general cricket fallback
            ("gryllidae",             "FieldCricket"),    // Cricket family
            ("acheta",                "FieldCricket"),    // House cricket
            ("teleogryllus",          "FieldCricket"),    // Black field cricket
            ("gryllotalpa",           "FieldCricket"),    // Mole cricket
            ("mole cricket",          "FieldCricket"),
            ("gryllodes",  "FieldCricket"),
            ("neoscapteriscus", "FieldCricket"),
            ("house cricket",         "FieldCricket"),
            ("brachytrupes",          "FieldCricket"),    // Short-tailed cricket

            // ── Cabbage Looper ──
            ("cabbage looper",        "Cabbage Looper"),
            ("trichoplusia",          "Cabbage Looper"),  // Trichoplusia ni
            ("looper",                "Cabbage Looper"),
            ("plusiinae",             "Cabbage Looper"),   // Subfamily
            ("caterpillar",           "Cabbage Looper"),  // caterpillar fallback
            ("larva",                 "Cabbage Looper"),  // larva fallback
            ("moth",                  "Cabbage Looper"),  // moth fallback
            ("inchworm",              "Cabbage Looper"),
            ("geometrid",             "Cabbage Looper"),
            ("geometridae",           "Cabbage Looper"),  // Geometer moths
            ("noctuidae",             "Cabbage Looper"),  // Owlet moths (looper parent family)
            ("spodoptera",            "Cabbage Looper"),  // Armyworm / cutworm moths
            ("armyworm",              "Cabbage Looper"),
            ("helicoverpa",           "Cabbage Looper"),  // Corn earworm / cotton bollworm
            ("pieris",                "Cabbage Looper"),  // Cabbage white butterfly
            ("lepidoptera",           "Cabbage Looper"),  // Butterflies & moths order

            // ── Cutworm ──
            ("cutworm",               "Cutworm"),
            ("agrotis",               "Cutworm"),         // Agrotis ipsilon
            ("agrotis ipsilon",       "Cutworm"),         // Black cutworm
            ("peridroma",             "Cutworm"),         // Variegated cutworm
            ("feltia",                "Cutworm"),         // Dingy cutworm
            ("xestia",                "Cutworm"),
            ("euxoa",                 "Cutworm"),         // Army cutworm
            ("black cutworm",         "Cutworm"),
            ("variegated cutworm",    "Cutworm"),

            // ── Cicadellidae ──
            ("cicadellidae",          "Cicadellidae"),
            ("leafhopper",            "Cicadellidae"),
            ("jassid",                "Cicadellidae"),
            ("empoasca",              "Cicadellidae"),    // Green leafhopper
            ("nephotettix",           "Cicadellidae"),    // Green rice leafhopper
            ("circulifer",            "Cicadellidae"),    // Beet leafhopper
            ("erythroneura",          "Cicadellidae"),    // Grape leafhopper
            ("macrosteles",           "Cicadellidae"),    // Aster leafhopper
            ("amrasca",               "Cicadellidae"),    // Cotton jassid
            ("idioscopus",            "Cicadellidae"),    // Mango leafhopper
            ("green leafhopper",      "Cicadellidae"),
            ("sharpshooter",          "Cicadellidae"),    // Glassy-winged sharpshooter
            ("homalodisca",           "Cicadellidae"),

            // ── Aphids ──
            ("aphid",                 "Aphids"),
            ("aphis",                 "Aphids"),
            ("aphididae",             "Aphids"),          // Aphid family
            ("myzus",                 "Aphids"),          // Green peach aphid
            ("rhopalosiphum",         "Aphids"),          // Bird cherry-oat aphid
            ("macrosiphum",           "Aphids"),          // Potato aphid
            ("brevicoryne",           "Aphids"),          // Cabbage aphid
            ("sitobion",              "Aphids"),          // Grain aphid
            ("schizaphis",            "Aphids"),
            ("toxoptera",             "Aphids"),          // Black citrus aphid
            ("greenfly",              "Aphids"),          // Common UK name
            ("blackfly",              "Aphids"),          // Common UK name (bean aphid)
            ("plant lice",            "Aphids"),          // Old common name
            ("plant louse",           "Aphids"),
            ("woolly aphid",          "Aphids"),
            ("eriosoma",              "Aphids"),          // Woolly apple aphid

            // ── Bugs (general fallback — keep last) ──
            ("bug",                   "Bugs"),            // general "bug" fallback — last
            ("hemiptera",             "Bugs"),            // True bugs order
            ("heteroptera",           "Bugs"),            // Suborder of true bugs
            ("lygus",                 "Bugs"),            // Tarnished plant bug
            ("miridae",              "Bugs"),             // Plant bug family
            ("coreidae",              "Bugs"),            // Leaf-footed bugs
            ("leptoglossus",          "Bugs"),
            ("reduviidae",            "Bugs"),            // Assassin bugs
            ("tingidae",              "Bugs"),            // Lace bugs
            ("spider",               "RedSpider"),        // general spider fallback
        };

        // ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Maps a free-form API label to the closest trained class.
        /// Returns the trained class name, or <c>null</c> if no match.
        /// </summary>
        public static string? MapLabel(string apiLabel, DiseaseType type)
        {
            if (string.IsNullOrWhiteSpace(apiLabel))
                return null;

            // Weed detection (Type 2) has no class restriction — pass through
            if (type == DiseaseType.Weed)
                return apiLabel;

            var lower = apiLabel.ToLowerInvariant();

            // 1. Try exact match first (case-insensitive)
            var allowedList = type == DiseaseType.LeafDisease ? LeafDiseaseClasses : PestClasses;
            foreach (var cls in allowedList)
            {
                if (string.Equals(cls, apiLabel, StringComparison.OrdinalIgnoreCase))
                    return cls;
            }

            // 2. Try keyword mapping
            var mappings = type == DiseaseType.LeafDisease ? LeafDiseaseMapping : PestMapping;
            foreach (var (keyword, trainedClass) in mappings)
            {
                if (lower.Contains(keyword))
                    return trainedClass;
            }

            // 3. No match — out of trained boundary
            return null;
        }
    }
}
