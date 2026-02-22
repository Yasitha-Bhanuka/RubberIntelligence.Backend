namespace RubberIntelligence.API.Modules.DiseaseDetection.Models
{
    public class AlertSettings
    {
        public const string SectionName = "AlertSettings";

        /// <summary>
        /// Radius in kilometers for proximity-based disease alerts.
        /// </summary>
        public double RadiusInKm { get; set; } = 5;
    }
}
