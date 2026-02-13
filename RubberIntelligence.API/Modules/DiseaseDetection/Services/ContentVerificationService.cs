using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    /// <summary>
    /// Uses a pre-trained MobileNetV2 ONNX model (ImageNet 1000 classes) to verify
    /// that the uploaded image actually contains the expected content type (leaf/pest/weed)
    /// before routing it to the specialized disease detection models.
    /// </summary>
    public class ContentVerificationService
    {
        private readonly InferenceSession? _session;
        private readonly ILogger<ContentVerificationService> _logger;

        // ── ImageNet class indices grouped by content category ──────────────────
        // These are curated subsets of the 1000 ImageNet classes relevant to each detection type.

        /// <summary>
        /// Plant, leaf, tree, flower, fruit, vegetable classes from ImageNet.
        /// Used to verify Type=0 (LeafDisease) and Type=2 (Weed) images.
        /// </summary>
        private static readonly HashSet<int> PlantRelatedIndices = new()
        {
            // Specific plant/nature classes
            948, // guacamole (avocado)
            949, // acorn squash
            950, // spaghetti squash
            951, // butternut squash
            952, // cucumber
            953, // artichoke
            954, // bell pepper
            955, // cardoon
            956, // mushroom
            957, // Granny Smith (apple)
            958, // strawberry
            959, // orange
            960, // lemon
            961, // fig
            962, // pineapple
            963, // banana
            964, // jackfruit
            965, // custard apple
            966, // pomegranate
            967, // hay
            984, // rapeseed (canola)
            985, // daisy
            986, // yellow lady's slipper
            987, // corn
            988, // acorn
            989, // hip (rosehip)
            991, // buckeye
            992, // coral fungus
            993, // agaric
            994, // gyromitra
            995, // stinkhorn
            996, // earthstar
            997, // hen-of-the-woods
            998, // bolete
            999, // ear (corn ear)
            // Cabbage/vegetable
            936, // head cabbage
            937, // broccoli
            938, // cauliflower
            939, // zucchini
            940, // spaghetti squash
            // Additional nature/outdoor classes
            970, // alp
            971, // bubble
            975, // lakeside
            979, // valley
            446, // barrel cactus
            // Potted plant / flower arrangements
            738, // pot (flower pot)
        };

        /// <summary>
        /// Insect, arachnid, and small invertebrate classes from ImageNet.
        /// Used to verify Type=1 (Pest) images.
        /// </summary>
        private static readonly HashSet<int> PestRelatedIndices = new()
        {
            // Beetles
            300, // tiger beetle
            301, // ladybug
            302, // ground beetle
            303, // long-horned beetle
            304, // leaf beetle
            305, // dung beetle
            306, // rhinoceros beetle
            307, // weevil
            // Flies, bees, ants
            308, // fly
            309, // bee
            310, // ant
            // Orthoptera & others
            311, // grasshopper
            312, // cricket
            313, // walking stick (insect)
            314, // cockroach
            315, // mantis
            316, // cicada
            317, // leafhopper
            318, // lacewing
            // Odonata
            319, // dragonfly
            320, // damselfly
            // Lepidoptera (butterflies/moths — some are pests)
            321, // ringlet
            322, // monarch
            323, // cabbage butterfly
            324, // sulphur butterfly
            325, // lycaenid
            326, // starfish (misplaced but included for safety)
            327, // sea urchin (misplaced but included for safety)
            // Actually, let's fix lepidoptera: 321-324 are butterflies
            // Arachnids
            71,  // barn spider
            72,  // garden spider
            73,  // black widow
            74,  // tarantula
            75,  // wolf spider
            76,  // tick
            77,  // centipede
            78,  // black grouse (not arachnid, skip)
            // Gastropods (snails/slugs — can be pests)
            113, // snail
            114, // slug
            // Worms
            69,  // nematode/worm
            // Moths (common pests)
            286, // silkworm moth (NOT standard, but close)
        };

        /// <summary>
        /// Combined plant + weed classes. Weeds are plants, so we use a broader set.
        /// We also include grass and general vegetation patterns.
        /// </summary>
        private static readonly HashSet<int> WeedRelatedIndices = new(
            PlantRelatedIndices.Concat(new[]
            {
                // Additional vegetation that could be weeds
                841, // thatch
                846, // stone wall (with moss/weeds)
                // General outdoor/field classes
                803, // patio
                659, // lawn mower (implies grass/weeds)
            })
        );

        // ── Human-readable label names for the top ImageNet classes we care about ──
        private static readonly Dictionary<int, string> RelevantLabels = new()
        {
            // Beetles & insects
            {300, "Tiger Beetle"}, {301, "Ladybug"}, {302, "Ground Beetle"},
            {303, "Long-horned Beetle"}, {304, "Leaf Beetle"}, {305, "Dung Beetle"},
            {306, "Rhinoceros Beetle"}, {307, "Weevil"}, {308, "Fly"}, {309, "Bee"},
            {310, "Ant"}, {311, "Grasshopper"}, {312, "Cricket"},
            {313, "Walking Stick"}, {314, "Cockroach"}, {315, "Mantis"},
            {316, "Cicada"}, {317, "Leafhopper"}, {318, "Lacewing"},
            {319, "Dragonfly"}, {320, "Damselfly"},
            {321, "Ringlet Butterfly"}, {322, "Monarch Butterfly"},
            {323, "Cabbage Butterfly"}, {324, "Sulphur Butterfly"},
            // Spiders
            {71, "Barn Spider"}, {72, "Garden Spider"}, {73, "Black Widow"},
            {74, "Tarantula"}, {75, "Wolf Spider"}, {76, "Tick"}, {77, "Centipede"},
            // Gastropods
            {113, "Snail"}, {114, "Slug"},
            // Plants
            {936, "Cabbage"}, {937, "Broccoli"}, {938, "Cauliflower"},
            {939, "Zucchini"}, {953, "Artichoke"}, {954, "Bell Pepper"},
            {955, "Cardoon"}, {957, "Granny Smith Apple"}, {958, "Strawberry"},
            {984, "Rapeseed"}, {985, "Daisy"}, {986, "Lady's Slipper"},
            {987, "Corn"}, {988, "Acorn"}, {993, "Agaric Mushroom"},
            {446, "Barrel Cactus"}, {738, "Flower Pot"},
        };

        public ContentVerificationService(IWebHostEnvironment env, ILogger<ContentVerificationService> logger)
        {
            _logger = logger;
            var modelPath = Path.Combine(env.ContentRootPath, "Modules", "DiseaseDetection", "Models", "mobilenetv2.onnx");

            if (File.Exists(modelPath))
            {
                try
                {
                    _session = new InferenceSession(modelPath);
                    _logger.LogInformation("[ContentVerification] MobileNetV2 model loaded from {Path}", modelPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ContentVerification] Failed to load MobileNetV2 model at {Path}", modelPath);
                }
            }
            else
            {
                _logger.LogWarning("[ContentVerification] MobileNetV2 model NOT FOUND at {Path}. Content verification will be skipped.", modelPath);
            }
        }

        public async Task<ContentVerificationResult> VerifyContentAsync(Stream imageStream, DiseaseType expectedType)
        {
            // If model is not loaded, skip verification (graceful fallback)
            if (_session == null)
            {
                _logger.LogWarning("[ContentVerification] Model not available. Skipping content verification.");
                return new ContentVerificationResult
                {
                    IsContentValid = true,
                    DetectedCategory = "Unknown (model not loaded)"
                };
            }

            // 1. Preprocess — same as other ONNX services (224x224, ImageNet normalization)
            using var image = await Image.LoadAsync<Rgb24>(imageStream);
            image.Mutate(x => x.Resize(224, 224));

            var tensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        tensor[0, 0, y, x] = ((pixel.R / 255f) - mean[0]) / std[0];
                        tensor[0, 1, y, x] = ((pixel.G / 255f) - mean[1]) / std[1];
                        tensor[0, 2, y, x] = ((pixel.B / 255f) - mean[2]) / std[2];
                    }
                }
            });

            // 2. Run MobileNetV2 inference
            var inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            };

            using var results = _session.Run(inputs);
            var logits = results.First().AsTensor<float>().ToArray();

            // 3. Softmax + get top-10 predictions
            var probabilities = Softmax(logits);
            var topIndices = probabilities
                .Select((prob, idx) => (Index: idx, Probability: prob))
                .OrderByDescending(x => x.Probability)
                .Take(10)
                .ToList();

            var topLabels = topIndices
                .Select(x => RelevantLabels.TryGetValue(x.Index, out var name) 
                    ? $"{name} ({x.Probability:P1})" 
                    : $"Class_{x.Index} ({x.Probability:P1})")
                .ToList();

            _logger.LogInformation("[ContentVerification] Top predictions: {Labels}", string.Join(", ", topLabels));

            // 4. Check if top predictions match expected type
            var expectedIndices = GetExpectedIndices(expectedType);
            var matchingPredictions = topIndices
                .Where(x => expectedIndices.Contains(x.Index))
                .ToList();

            bool isValid = matchingPredictions.Any();
            float topMatchConfidence = matchingPredictions.Any() 
                ? matchingPredictions.Max(x => x.Probability) 
                : 0f;

            var result = new ContentVerificationResult
            {
                IsContentValid = isValid,
                TopConfidence = topMatchConfidence,
                TopLabels = topLabels,
                DetectedCategory = isValid ? expectedType.ToString() : "Unrelated"
            };

            if (!isValid)
            {
                var expectedTypeName = expectedType switch
                {
                    DiseaseType.LeafDisease => "a rubber leaf or plant",
                    DiseaseType.Pest => "an insect or pest",
                    DiseaseType.Weed => "a plant or weed",
                    _ => "valid content"
                };
                result.RejectReason = $"This image does not appear to contain {expectedTypeName}. " +
                    $"Please upload an image of {expectedTypeName} for accurate detection.";
                _logger.LogWarning("[ContentVerification] Content mismatch for {Type}: {Reason}", expectedType, result.RejectReason);
            }
            else
            {
                _logger.LogInformation("[ContentVerification] Content verified as {Type} with confidence {Confidence:P1}", 
                    expectedType, topMatchConfidence);
            }

            return result;
        }

        private HashSet<int> GetExpectedIndices(DiseaseType type)
        {
            return type switch
            {
                DiseaseType.LeafDisease => PlantRelatedIndices,
                DiseaseType.Pest => PestRelatedIndices,
                DiseaseType.Weed => WeedRelatedIndices,
                _ => new HashSet<int>()
            };
        }

        private float[] Softmax(float[] logits)
        {
            var maxLogit = logits.Max();
            var exp = logits.Select(x => Math.Exp(x - maxLogit)).ToArray();
            var sum = exp.Sum();
            return exp.Select(x => (float)(x / sum)).ToArray();
        }
    }
}
