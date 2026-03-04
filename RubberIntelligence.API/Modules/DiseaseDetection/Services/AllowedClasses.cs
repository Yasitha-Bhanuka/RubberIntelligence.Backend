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
            ("bird",           "Birds_eye"),
            ("colletotrichum", "Colletorichum"),
            ("colletorichum",  "Colletorichum"),
            ("corynespora",    "Corynespora"),
            ("dry leaf",       "Dry_Leaf"),
            ("dry_leaf",       "Dry_Leaf"),
            ("drought",        "Dry_Leaf"),
            ("desiccation",    "Dry_Leaf"),
            ("healthy",        "Healthy"),
            ("leaf spot",      "Leaf_Spot"),
            ("leaf_spot",      "Leaf_Spot"),
            ("cercospora",     "Leaf_Spot"),       // common leaf-spot pathogen
            ("septoria",       "Leaf_Spot"),        // another leaf-spot genus
            ("pest",           "Pesta"),
            ("insect damage",  "Pesta"),
            ("powdery mildew", "Powdery_mildew"),
            ("powdery_mildew", "Powdery_mildew"),
            ("oidium",         "Powdery_mildew"),   // scientific name for powdery mildew
            ("blight",         "Leaf_Spot"),         // generic blight → closest match
        };

        private static readonly (string Keyword, string TrainedClass)[] PestMapping =
        {
            // Primary / common-name matches
            ("whitefly",              "Whitefly"),
            ("bemisia",               "Whitefly"),        // Bemisia tabaci
            ("trialeurodes",          "Whitefly"),        // Trialeurodes vaporariorum
            ("snail",                 "Snail"),
            ("helix",                 "Snail"),
            ("weevil",                "Weevil"),
            ("curculio",              "Weevil"),
            ("thrip",                 "Thrips"),           // matches "thrips" and "thrip"
            ("thysanoptera",          "Thrips"),
            ("slug",                  "Slug"),
            ("riptortus",             "Riptortus"),
            ("bean bug",              "Riptortus"),
            ("red spider",            "RedSpider"),
            ("redspider",             "RedSpider"),
            ("tetranychus",           "RedSpider"),       // Tetranychus urticae (red spider mite)
            ("grasshopper",           "Grasshopper"),
            ("locust",                "Grasshopper"),
            ("acrididae",             "Grasshopper"),
            ("mediterranean fruit",   "Mediterranean fruit fly"),
            ("ceratitis",             "Mediterranean fruit fly"),  // Ceratitis capitata
            ("medfly",                "Mediterranean fruit fly"),
            ("field cricket",         "FieldCricket"),
            ("fieldcricket",          "FieldCricket"),
            ("gryllus",               "FieldCricket"),
            ("cricket",               "FieldCricket"),    // general cricket fallback
            ("mite",                  "Mites"),
            ("acari",                 "Mites"),
            ("earwig",                "Earwig"),
            ("dermaptera",            "Earwig"),
            ("forficula",             "Earwig"),
            ("cabbage looper",        "Cabbage Looper"),
            ("trichoplusia",          "Cabbage Looper"),  // Trichoplusia ni
            ("looper",                "Cabbage Looper"),
            ("cutworm",               "Cutworm"),
            ("agrotis",               "Cutworm"),         // Agrotis ipsilon
            ("cicadellidae",          "Cicadellidae"),
            ("leafhopper",            "Cicadellidae"),
            ("jassid",                "Cicadellidae"),
            ("beetle",                "Beetle"),
            ("coleoptera",            "Beetle"),
            ("chafer",                "Beetle"),
            ("aphid",                 "Aphids"),
            ("aphis",                 "Aphids"),
            ("adristyrannus",         "Adristyrannus"),
            ("bug",                   "Bugs"),            // general "bug" fallback — last
            ("caterpillar",           "Cabbage Looper"),  // caterpillar fallback
            ("larva",                 "Cabbage Looper"),  // larva fallback
            ("moth",                  "Cabbage Looper"),  // moth fallback
            ("spider",               "RedSpider"),        // general spider fallback
            ("fly",                   "Mediterranean fruit fly"), // general fly fallback
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
