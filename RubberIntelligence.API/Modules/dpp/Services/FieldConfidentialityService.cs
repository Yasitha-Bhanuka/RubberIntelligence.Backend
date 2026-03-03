namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Rule-based field-level confidentiality classification.
    /// Classifies each extracted field as CONFIDENTIAL or NON-CONFIDENTIAL
    /// based on its name, using a curated rubber-trade keyword corpus.
    ///
    /// KEYWORD CORPUS DESIGN
    /// ─────────────────────
    /// Fields are split into two tiers:
    ///
    ///   HIGH CONFIDENCE (0.95) — unambiguous financial / identity secrets.
    ///     Match any substring for maximum recall.
    ///
    ///   MEDIUM CONFIDENCE (0.75) — contextually sensitive fields that may
    ///     warrant manual review in edge cases.
    ///
    /// Non-matching fields are classified NON-CONFIDENTIAL (0.80 confidence).
    ///
    /// FUTURE IMPROVEMENT
    /// ──────────────────
    /// Replace with an ML model trained on labelled DPP fields for higher
    /// precision on ambiguous names (e.g., "reference", "note", "remarks").
    /// </summary>
    public class FieldConfidentialityService
    {
        // ── Tier-1: High-confidence confidential fields (0.95) ────────────────────
        // Financial & payment
        private static readonly HashSet<string> HighConfidentialKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Pricing
            "price", "rate", "cost", "amount", "value", "total", "subtotal",
            "unit price", "unit cost", "unitprice", "unitcost",
            "net price", "gross price", "netprice", "grossprice",
            "discount", "tax", "vat", "duty", "tariff", "levy",
            "invoice amount", "invoiceamount", "payment", "settlement",
            "outstanding", "balance", "deposit", "advance", "prepayment",
            "commission", "margin", "markup", "rebate",
            // Banking & financial accounts
            "bank", "account", "iban", "swift", "bic", "sort code", "routing",
            "credit", "debit", "wire", "remittance", "transfer",
            "letter of credit", "lc number", "lcnumber",
            // Identity secrets
            "tax id", "taxid", "tin", "ein", "vat number", "vatnumber",
            "registration number", "regnumber", "company id", "companyid",
            "national id", "nationalid", "passport", "license number", "licenseno",
            "password", "secret", "pin", "token", "api key", "apikey",
            // Trade secrets
            "supplier", "vendor", "manufacturer", "producer", "mill",
            "estate name", "estate", "plantation", "plantationnumber",
            "supplier address", "supplieraddress",
            "buyer name", "buyername", "purchaser",
            "contract number", "contractno", "agreement",
            "profit", "revenue", "turnover", "earnings"
        };

        // ── Tier-2: Medium-confidence contextually sensitive fields (0.75) ───────
        private static readonly HashSet<string> MediumConfidentialKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Geographic routing that may reveal supply-chain relationships
            "address", "origin", "destination", "port of loading", "port of discharge",
            "ship to", "bill to", "consignee", "shipper", "freight forwarder",
            "customs", "hscode", "hs code",
            // Contact details
            "email", "phone", "mobile", "fax", "contact",
            // Reference numbers that can link to other sensitive documents
            "po number", "ponumber", "purchase order", "order number", "orderno",
            "reference", "ref no", "refno", "invoice number", "invoiceno",
            "shipment", "container", "tracking",
            // Quality / certification identifiers (may be proprietary)
            "certificate", "certno", "batch", "lot number", "lotno",
            "inspection", "analyst", "laboratory"
        };

        /// <summary>
        /// Classifies a single field as confidential or non-confidential
        /// based on its field name.  Value is used only for multi-word context.
        /// </summary>
        public FieldClassificationResult Classify(string fieldName, string value)
        {
            var lowerName = fieldName.ToLowerInvariant().Trim();

            // Tier-1: substring match against high-confidence set
            bool isHighConfidential = HighConfidentialKeywords
                .Any(kw => lowerName.Contains(kw.ToLowerInvariant()));

            if (isHighConfidential)
                return new FieldClassificationResult
                {
                    IsConfidential       = true,
                    ConfidenceScore      = 0.95,
                    ManualReviewRequired = false
                };

            // Tier-2: substring match against medium-confidence set
            bool isMediumConfidential = MediumConfidentialKeywords
                .Any(kw => lowerName.Contains(kw.ToLowerInvariant()));

            if (isMediumConfidential)
                return new FieldClassificationResult
                {
                    IsConfidential       = true,
                    ConfidenceScore      = 0.75,
                    ManualReviewRequired = true  // Flag for human review
                };

            // Non-confidential
            return new FieldClassificationResult
            {
                IsConfidential       = false,
                ConfidenceScore      = 0.80,
                ManualReviewRequired = false
            };
        }
    }

    /// <summary>
    /// Result of classifying a single extracted field.
    /// </summary>
    public class FieldClassificationResult
    {
        public bool   IsConfidential       { get; set; }
        public double ConfidenceScore      { get; set; }
        public bool   ManualReviewRequired { get; set; }
    }
}
