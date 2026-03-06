using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    // Specific interfaces for Dependency Injection so CompositeDiseaseService 
    // knows which model to use for each image type.

    public interface ILeafDiseaseService : IDiseaseDetectionService { }
    
    public interface IPestDetectionService : IDiseaseDetectionService { }
    
    public interface IWeedDetectionService : IDiseaseDetectionService { }
}
