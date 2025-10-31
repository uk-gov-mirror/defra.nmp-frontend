using NMP.Portal.Resources;
using System.ComponentModel.DataAnnotations;

namespace NMP.Portal.Models
{
    public class OrganicManure
    {
        public int? ID { get; set; }
        public int ManagementPeriodID { get; set; }
        public int ManureTypeID { get; set; }
        public string? ManureTypeName { get; set; }
        public DateTime? ApplicationDate { get; set; }
        public bool Confirm { get; set; }
        public decimal? N { get; set; }
        public decimal? P2O5 { get; set; }
        public decimal? K2O { get; set; }
        public decimal? MgO { get; set; }
        public decimal? SO3 { get; set; }
        public decimal AvailableN { get; set; }
        
        public decimal? ApplicationRate { get; set; }
        public decimal? DryMatterPercent { get; set; }
        public decimal? UricAcid { get; set; }
        public DateTime EndOfDrain { get; set; }
        public int Rainfall { get; set; }
        public decimal? AreaSpread { get; set; }
        public decimal? ManureQuantity { get; set; }
        public int? ApplicationMethodID { get; set; }
        public int? IncorporationMethodID { get; set; }
        public int? IncorporationDelayID { get; set; }
        public decimal? NH4N { get; set; }
        public decimal? NO3N { get; set; }
        public decimal AvailableP2O5 { get; set; }
        public decimal AvailableK2O { get; set; }
        public decimal? AvailableSO3 { get; set; }
        public decimal? TotalP2O5 { get; set; }
        public decimal? TotalK2O { get; set; }
        public decimal? TotalSO3 { get; set; }
        public decimal? TotalN { get; set; }
        public decimal? TotalMgO { get; set; }
        public int WindspeedID { get; set; }
        public int RainfallWithinSixHoursID { get; set; }
        public int MoistureID { get; set; }
        public int AutumnCropNitrogenUptake { get; set; }
        public DateTime SoilDrainageEndDate { get; set; }
        public decimal? AvailableNForNMax { get; set; }
        public int? Defoliation { get; set; }
        public string? DefoliationName { get; set; }
        public int? FieldID { get; set; }
        public string? FieldName { get; set; }
        public string? EncryptedCounter { get; set; }
        public int? AvailableNForNextYear { get; set; }
        public int? AvailableNForNextDefoliation { get; set; }
    }
}
