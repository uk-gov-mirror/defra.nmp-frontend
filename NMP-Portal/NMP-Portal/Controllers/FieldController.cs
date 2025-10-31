using GovUk.Frontend.AspNetCore.TagHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using NMP.Portal.Enums;
using NMP.Portal.Helpers;
using NMP.Portal.Models;
using NMP.Portal.Resources;
using NMP.Portal.ServiceResponses;
using NMP.Portal.Services;
using NMP.Portal.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Error = NMP.Portal.ServiceResponses.Error;

namespace NMP.Portal.Controllers
{
    [Authorize]
    public class FieldController : Controller
    {
        private readonly ILogger<FieldController> _logger;
        private readonly IDataProtector _farmDataProtector;
        private readonly IDataProtector _fieldDataProtector;
        private readonly IDataProtector _soilAnalysisDataProtector;
        private readonly IDataProtector _cropDataProtector;
        private readonly IFarmService _farmService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFieldService _fieldService;
        private readonly ISoilService _soilService;
        private readonly IOrganicManureService _organicManureService;
        private readonly ISoilAnalysisService _soilAnalysisService;
        private readonly IPKBalanceService _pKBalanceService;
        private readonly ICropService _cropService;
        private readonly IPreviousCroppingService _previousCroppingService;

        public FieldController(ILogger<FieldController> logger, IDataProtectionProvider dataProtectionProvider,
             IFarmService farmService, IHttpContextAccessor httpContextAccessor, ISoilService soilService,
             IFieldService fieldService, IOrganicManureService organicManureService, ISoilAnalysisService soilAnalysisService, IPKBalanceService pKBalanceService, ICropService cropService, IPreviousCroppingService previousCroppingService)
        {
            _logger = logger;
            _farmService = farmService;
            _httpContextAccessor = httpContextAccessor;
            _farmDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FarmController");
            _fieldDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FieldController");
            _soilAnalysisDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.SoilAnalysisController");
            _fieldService = fieldService;
            _soilService = soilService;
            _organicManureService = organicManureService;
            _soilAnalysisService = soilAnalysisService;
            _pKBalanceService = pKBalanceService;
            _cropService = cropService;
            _cropDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.CropController");
            _previousCroppingService = previousCroppingService;
        }
        public IActionResult Index()
        {
            _logger.LogTrace($"Field Controller : Index() action called");
            return View();
        }

        public IActionResult CreateFieldCancel(string id)
        {
            _logger.LogTrace($"Field Controller : CreateFieldCancel({id}) action called");
            _httpContextAccessor.HttpContext?.Session.Remove("FieldData");
            return RedirectToAction("FarmSummary", "Farm", new { Id = id });
        }

        public async Task<IActionResult> BackActionForAddField(string id)
        {
            _logger.LogTrace($"Field Controller : BackActionForAddField({id}) action called");
            FieldViewModel model = new FieldViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                int farmID = Convert.ToInt32(_farmDataProtector.Unprotect(id));
                int fieldCount = await _fieldService.FetchFieldCountByFarmIdAsync(Convert.ToInt32(farmID));
                if (model.IsCheckAnswer)
                {
                    return RedirectToAction("CheckAnswer");
                }
                else if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate) && !string.IsNullOrWhiteSpace(model.EncryptedFieldId))
                {
                    return RedirectToAction("UpdateField", new
                    {
                        id = model.EncryptedFieldId,
                        farmId = model.EncryptedFarmId
                    });
                }
                else if (model.HarvestYear != null && (!string.IsNullOrWhiteSpace(model.EncryptedHarvestYear)))
                {
                    _httpContextAccessor.HttpContext?.Session.Remove("FieldData");
                    return RedirectToAction("HarvestYearOverview", "Crop", new
                    {
                        id = model.EncryptedFarmId,
                        year = model.EncryptedHarvestYear
                    });
                }
                else if (fieldCount > 0)
                {
                    if (model != null && model.CopyExistingField != null && model.CopyExistingField.Value)
                    {
                        return RedirectToAction("CopyFields", "Field");
                    }
                    else
                    {
                        return RedirectToAction("CopyExistingField", "Field", new { q = id });
                    }
                    //return RedirectToAction("ManageFarmFields", "Field", new { id = id });
                }
                else
                {
                    _httpContextAccessor.HttpContext?.Session.Remove("FieldData");
                    return RedirectToAction("FarmSummary", "Farm", new { id = id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in BackActionForAddField() action : {ex.Message}, {ex.StackTrace}");
                TempData["AddFieldError"] = ex.Message;
                return View("AddField", model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> AddField(string q, string? r)//EncryptedfarmId EncryptedYear
        {
            _logger.LogTrace($"Field Controller : AddField({q}) action called");
            FieldViewModel model = new FieldViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else if (string.IsNullOrWhiteSpace(q))
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if (!string.IsNullOrEmpty(q))
                {
                    model.FarmID = Convert.ToInt32(_farmDataProtector.Unprotect(q));
                    model.EncryptedFarmId = q;

                    (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmID);
                    model.isEnglishRules = farm.EnglishRules;
                    model.FarmName = farm.Name;
                    model.LastHarvestYear = farm.LastHarvestYear;  //if there is no plan created.
                    model.IsWithinNVZForFarm = farm.NVZFields == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
                    model.IsAbove300SeaLevelForFarm = farm.FieldsAbove300SeaLevel == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
                    //DateTime harvestEndDate = new DateTime(model.LastHarvestYear.Value, 7, 31);
                    DateTime currentDate = DateTime.Now;
                    DateTime harvestYearEndDate = new DateTime(currentDate.Year, 7, 31);



                    //if (currentDate <= harvestYearEndDate)
                    //{
                    //    model.LastHarvestYear = currentDate.Year - 1;
                    //}
                    //else
                    //{
                    //    model.LastHarvestYear = currentDate.Year;
                    //}
                    //if plan created then check the latest, less than or equal to current year.
                    List<PlanSummaryResponse> cropPlans = await _cropService.FetchPlanSummaryByFarmId(model.FarmID, 0);
                    cropPlans.RemoveAll(x => x.Year == 0);
                    if (cropPlans.Count() > 0)
                    {
                        int currentYear = currentDate.Year;

                        int? latestPreviousHarvestYear = cropPlans
                            .Where(p => p.Year <= currentYear)
                            .Select(p => (int?)p.Year)
                            .DefaultIfEmpty()
                            .Max();

                        model.LastHarvestYear = latestPreviousHarvestYear;
                    }

                }
                if (!string.IsNullOrWhiteSpace(r))
                {
                    model.EncryptedHarvestYear = r;
                    model.HarvestYear = Convert.ToInt32(_farmDataProtector.Unprotect(r));
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in BackActionForAddField() action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = string.Concat(error.Message == null ? "" : error.Message, ex.Message);
                return RedirectToAction("FarmSummary", "Farm", new { id = q });
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddField(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : AddField() action called");
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                ModelState.AddModelError("Name", Resource.MsgEnterTheFieldName);
            }

            bool isFieldAlreadyexist = await _fieldService.IsFieldExistAsync(field.FarmID, field.Name);
            if (isFieldAlreadyexist)
            {
                ModelState.AddModelError("Name", Resource.MsgFieldAlreadyExist);
            }

            if (!ModelState.IsValid)
            {
                return View(field);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);

            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }

            if (field.CopyExistingField != null && (field.CopyExistingField.Value))
            {
                (FieldResponse fieldResponse, Error error) = await _fieldService.FetchFieldSoilAnalysisAndSnsById(field.ID.Value);
                if (fieldResponse != null && string.IsNullOrWhiteSpace(error.Message))
                {
                    //field.Name = fieldData.Name;
                    field.IsCheckAnswer = true;
                    field.NationalGridReference = fieldResponse.Field.NationalGridReference;
                    field.OtherReference = fieldResponse.Field.OtherReference;
                    field.TotalArea = fieldResponse.Field.TotalArea;
                    field.CroppedArea = fieldResponse.Field.CroppedArea;
                    field.LPIDNumber = fieldResponse.Field.LPIDNumber;
                    field.ManureNonSpreadingArea = fieldResponse.Field.ManureNonSpreadingArea;
                    field.NVZProgrammeID = fieldResponse.Field.NVZProgrammeID;
                    field.IsWithinNVZ = fieldResponse.Field.IsWithinNVZ;
                    field.IsAbove300SeaLevel = fieldResponse.Field.IsAbove300SeaLevel;
                    field.SoilReleasingClay = fieldResponse.Field.SoilReleasingClay;
                    field.SoilOverChalk = fieldResponse.Field.SoilOverChalk;
                    field.SoilTypeID = fieldResponse.Field.SoilTypeID;
                    //field.SoilType = Enum.GetName(typeof(NMP.Portal.Enums.SoilTypeEngland), field.SoilTypeID);
                    List<SoilTypesResponse> soilTypes = await _fieldService.FetchSoilTypes();
                    SoilTypesResponse? soilType = soilTypes.FirstOrDefault(x => x.SoilTypeId == field.SoilTypeID);
                    if (soilType != null && soilType.KReleasingClay)
                    {
                        field.IsSoilReleasingClay = true;
                    }
                    else
                    {
                        field.IsSoilReleasingClay = false;
                    }
                    field.SoilType = await _soilService.FetchSoilTypeById(field.SoilTypeID.Value);

                    if (fieldResponse.SoilAnalysis != null)
                    {
                        field.SoilAnalyses.PH = fieldResponse.SoilAnalysis.PH;
                        field.SoilAnalyses.Phosphorus = fieldResponse.SoilAnalysis.Phosphorus;
                        field.SoilAnalyses.PhosphorusIndex = fieldResponse.SoilAnalysis.PhosphorusIndex;
                        field.SoilAnalyses.Potassium = fieldResponse.SoilAnalysis.Potassium;
                        field.SoilAnalyses.PotassiumIndex = fieldResponse.SoilAnalysis.PotassiumIndex;
                        field.SoilAnalyses.Magnesium = fieldResponse.SoilAnalysis.Magnesium;
                        field.SoilAnalyses.MagnesiumIndex = fieldResponse.SoilAnalysis.MagnesiumIndex;
                        field.SoilAnalyses.PhosphorusMethodologyID = fieldResponse.SoilAnalysis.PhosphorusMethodologyID;
                        field.SoilAnalyses.SulphurDeficient = fieldResponse.SoilAnalysis.SulphurDeficient;
                        field.SoilAnalyses.Date = fieldResponse.SoilAnalysis.Date.Value.ToLocalTime().Date;
                        field.RecentSoilAnalysisQuestion = true;
                        if (fieldResponse.SoilAnalysis.PotassiumIndex != null)
                        {
                            field.PotassiumIndexValue = fieldResponse.SoilAnalysis.PotassiumIndex.ToString() == Resource.lblMinusTwo ? Resource.lblTwoMinus : (fieldResponse.SoilAnalysis.PotassiumIndex.ToString() == Resource.lblPlusTwo ? Resource.lblTwoPlus : fieldResponse.SoilAnalysis.PotassiumIndex.ToString());
                        }
                        if (field.SoilAnalyses.Potassium != null || field.SoilAnalyses.Phosphorus != null
                            || field.SoilAnalyses.Magnesium != null)
                        {
                            field.IsSoilNutrientValueTypeIndex = false;
                        }
                        else
                        {
                            field.IsSoilNutrientValueTypeIndex = true;
                        }
                    }
                    else
                    {
                        field.RecentSoilAnalysisQuestion = false;
                    }

                    List<CropTypeResponse> cropTypeResponses = await _fieldService.FetchAllCropTypes();
                    if (fieldResponse.Crop != null)
                    {
                        field.CropTypeID = fieldResponse.Crop.CropTypeID;
                        field.CropType = await _fieldService.FetchCropTypeById(field.CropTypeID ?? 0);
                        if (cropTypeResponses.Count > 0)
                        {
                            var cropType = cropTypeResponses.FirstOrDefault(x => x.CropTypeId == field.CropTypeID);
                            if (cropType != null)
                            {
                                field.CropGroupId = cropType.CropGroupId;
                                field.CropGroup = await _fieldService.FetchCropGroupById(field.CropGroupId.Value);
                            }
                        }
                    }
                    if (fieldResponse.PreviousCroppings != null && fieldResponse.PreviousCroppings.Count > 0)
                    {

                        //foreach (var year in fieldResponse.PreviousCroppings)
                        //{
                        field.PreviousGrassYears = new List<int>();
                        if (fieldResponse.Crop != null)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(fieldResponse.Field.ID.Value);
                            if (cropList.Count > 0)
                            {
                                int cropYear = cropList.OrderBy(x => x.CreatedOn)
                               .Select(x => x.Year)
                               .FirstOrDefault();


                                if (fieldResponse.PreviousCroppings.Count == 3)
                                {
                                    field.IsPreviousYearGrass = true;
                                    field.PreviousGrassYears.Add(field.LastHarvestYear.Value);
                                    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 1);
                                    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 2);
                                }
                                else
                                {
                                    int yearGap = cropYear -
                                    fieldResponse.PreviousCroppings.Where(x => x.HarvestYear.Value != cropYear).Select(x => x.HarvestYear.Value).FirstOrDefault();
                                    if (fieldResponse.PreviousCroppings.Any(x => x.HarvestYear == cropYear))
                                    {
                                        field.PreviousGrassYears.Add(field.LastHarvestYear.Value);
                                        if (fieldResponse.PreviousCroppings.Count == 2)
                                        {
                                            field.IsPreviousYearGrass = true;
                                            field.PreviousGrassYears.Add(field.LastHarvestYear.Value - yearGap);
                                        }
                                    }
                                    else if (fieldResponse.PreviousCroppings.Count == 2)
                                    {
                                        field.IsPreviousYearGrass = false;
                                        field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 1);
                                        field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 2);
                                    }
                                    else if (fieldResponse.PreviousCroppings.Count == 1)
                                    {
                                        field.IsPreviousYearGrass = false;
                                        field.PreviousGrassYears.Add(field.LastHarvestYear.Value - yearGap);
                                    }
                                }
                            }
                        }
                        //if (fieldResponse.Crop != null && fieldResponse.PreviousCroppings.Count == 3)
                        //{
                        //    field.IsPreviousYearGrass = true;
                        //    field.PreviousGrassYears.Add(field.LastHarvestYear.Value);
                        //    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 1);
                        //    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 2);
                        //}
                        //else if (fieldResponse.Crop != null && fieldResponse.PreviousCroppings.Count == 2)
                        //{
                        //    field.IsPreviousYearGrass = false;
                        //    int yearGap = fieldResponse.Crop.Year - fieldResponse.PreviousCroppings.Select(p => p.HarvestYear.Value).FirstOrDefault();
                        //    if (yearGap == 0)
                        //    {
                        //        field.PreviousGrassYears.Add(field.LastHarvestYear.Value);
                        //        field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 1);
                        //    }
                        //    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 1);
                        //    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - 2);
                        //}
                        //else if (fieldResponse.Crop != null && fieldResponse.PreviousCroppings.Count == 1)
                        //{
                        //    field.IsPreviousYearGrass = false;
                        //    //int cropYear = field.Crops.Select(c=>c.Year).FirstOrDefault();
                        //    int yearGap = fieldResponse.Crop.Year - fieldResponse.PreviousCroppings.Select(p => p.HarvestYear.Value).FirstOrDefault();
                        //    field.PreviousGrassYears.Add(field.LastHarvestYear.Value - yearGap);
                        //}

                        //}
                        field.PreviousCroppings.GrassManagementOptionID = fieldResponse.PreviousCroppings[0].GrassManagementOptionID;
                        field.PreviousCroppings.HasGreaterThan30PercentClover = fieldResponse.PreviousCroppings[0].HasGreaterThan30PercentClover;
                        field.PreviousCroppings.SoilNitrogenSupplyItemID = fieldResponse.PreviousCroppings[0].SoilNitrogenSupplyItemID;
                        field.PreviousCroppings.HasGrassInLastThreeYear = fieldResponse.PreviousCroppings[0].HasGrassInLastThreeYear;
                        field.PreviousCroppings.LayDuration = fieldResponse.PreviousCroppings[0].LayDuration;
                        //field.PreviousGrassYears = previousGrassYears;

                    }
                    else
                    {
                        field.PreviousCroppings.HasGrassInLastThreeYear = false;
                    }

                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
                }
                else
                {
                    TempData["AddFieldError"] = error.Message;
                    return View("AddField", field);
                }
                return RedirectToAction("CheckAnswer");
            }


            return RedirectToAction("FieldMeasurements");
        }
        [HttpGet]
        public async Task<IActionResult> FieldMeasurements()
        {
            _logger.LogTrace($"Field Controller : FieldMeasurements() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FieldMeasurements(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : FieldMeasurements() post action called");
            if ((!ModelState.IsValid) && ModelState.ContainsKey("TotalArea"))
            {
                var InvalidFormatError = ModelState["TotalArea"].Errors.Count > 0 ?
                                ModelState["TotalArea"].Errors[0].ErrorMessage.ToString() : null;

                if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["TotalArea"].AttemptedValue, Resource.lblTotalFieldArea)))
                {
                    ModelState["TotalArea"].Errors.Clear();
                    ModelState["TotalArea"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblArea));
                }
            }
            if ((!ModelState.IsValid) && ModelState.ContainsKey("CroppedArea"))
            {
                var InvalidFormatError = ModelState["CroppedArea"].Errors.Count > 0 ?
                                ModelState["CroppedArea"].Errors[0].ErrorMessage.ToString() : null;

                if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["CroppedArea"].AttemptedValue, Resource.lblCroppedArea)))
                {
                    ModelState["CroppedArea"].Errors.Clear();
                    ModelState["CroppedArea"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblArea));
                }
            }
            if ((!ModelState.IsValid) && ModelState.ContainsKey("ManureNonSpreadingArea"))
            {
                var InvalidFormatError = ModelState["ManureNonSpreadingArea"].Errors.Count > 0 ?
                                ModelState["ManureNonSpreadingArea"].Errors[0].ErrorMessage.ToString() : null;

                if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["ManureNonSpreadingArea"].AttemptedValue, Resource.lblManureNonSpreadingArea)))
                {
                    ModelState["ManureNonSpreadingArea"].Errors.Clear();
                    ModelState["ManureNonSpreadingArea"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblArea));
                }
            }
            if (field.TotalArea == null || field.TotalArea == 0)
            {
                ModelState.AddModelError("TotalArea", Resource.MsgEnterTotalFieldArea);
            }
            if (field.TotalArea != null)
            {
                if (field.TotalArea < 0)
                {
                    ModelState.AddModelError("TotalArea", Resource.MsgEnterANumberWhichIsGreaterThanZero);
                }
            }

            if (field.CroppedArea > field.TotalArea)
            {
                ModelState.AddModelError("CroppedArea", Resource.MsgCroppedAreaIsGreaterThanTotalArea);
            }
            if (field.CroppedArea != null)
            {
                if (field.CroppedArea < 0)
                {
                    ModelState.AddModelError("CroppedArea", Resource.MsgEnterANumberWhichIsGreaterThanZero);
                }
            }

            if (field.ManureNonSpreadingArea > field.TotalArea)
            {
                ModelState.AddModelError("ManureNonSpreadingArea", Resource.MsgManureNonSpreadingAreaIsGreaterThanTotalArea);
            }
            if (field.ManureNonSpreadingArea != null)
            {
                if (field.ManureNonSpreadingArea < 0)
                {
                    ModelState.AddModelError("ManureNonSpreadingArea", Resource.MsgEnterANumberWhichIsGreaterThanZero);
                }
            }

            if (!ModelState.IsValid)
            {
                return View(field);
            }

            if (!(field.CroppedArea.HasValue) && !(field.ManureNonSpreadingArea.HasValue))
            {
                field.CroppedArea = field.TotalArea;
            }

            if ((!field.CroppedArea.HasValue) && (field.ManureNonSpreadingArea.HasValue) && field.ManureNonSpreadingArea > 0)
            {
                field.CroppedArea = field.TotalArea - field.ManureNonSpreadingArea;
            }

            string farmId = _farmDataProtector.Unprotect(field.EncryptedFarmId);
            (Farm farm, Error error) = await _farmService.FetchFarmByIdAsync(Convert.ToInt32(farmId));

            field.IsWithinNVZForFarm = farm.NVZFields == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
            field.IsAbove300SeaLevelForFarm = farm.FieldsAbove300SeaLevel == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("NVZField");
        }
        [HttpGet]
        public async Task<IActionResult> NVZField()
        {
            _logger.LogTrace($"Field Controller : NVZField() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            string farmId = _farmDataProtector.Unprotect(model.EncryptedFarmId);
            (Farm farm, error) = await _farmService.FetchFarmByIdAsync(Convert.ToInt32(farmId));
            if (model.IsWithinNVZForFarm.HasValue && model.IsWithinNVZForFarm.Value)
            {
                return View(model);
            }
            model.IsWithinNVZ = Convert.ToBoolean(farm.NVZFields);
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return RedirectToAction("ElevationField");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult NVZField(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : NVZField() post action called");
            if (field.IsWithinNVZ == null)
            {
                ModelState.AddModelError("IsWithinNVZ", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(field);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("ElevationField");
        }

        [HttpGet]
        public async Task<IActionResult> ElevationField()
        {
            _logger.LogTrace($"Field Controller : ElevationField() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            string farmId = _farmDataProtector.Unprotect(model.EncryptedFarmId);
            (Farm farm, error) = await _farmService.FetchFarmByIdAsync(Convert.ToInt32(farmId));
            if (model.IsAbove300SeaLevelForFarm.HasValue && model.IsAbove300SeaLevelForFarm.Value)
            {
                return View(model);
            }
            model.IsAbove300SeaLevel = Convert.ToBoolean(farm.FieldsAbove300SeaLevel);
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return RedirectToAction("SoilType");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ElevationField(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : ElevationField() post action called");
            if (field.IsAbove300SeaLevel == null)
            {
                ModelState.AddModelError("IsAbove300SeaLevel", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(field);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("SoilType");
        }
        [HttpGet]
        public async Task<IActionResult> SoilType()
        {
            _logger.LogTrace($"Field Controller : SoilType() action called");
            Error error = new Error();
            FieldViewModel model = new FieldViewModel();
            List<SoilTypesResponse> soilTypes = new List<SoilTypesResponse>();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }


                soilTypes = await _fieldService.FetchSoilTypes();
                if (soilTypes.Count > 0 && soilTypes.Any())
                {
                    var country = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                    var soilTypesList = soilTypes.Where(x => x.CountryId == country).ToList();
                    ViewBag.SoilTypesList = soilTypesList;
                }
                else
                {
                    ViewBag.SoilTypeError = Resource.MsgServiceNotAvailable;
                    RedirectToAction("ElevationField");
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in SoilType() action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = ex.Message;
                return RedirectToAction("ElevationField");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoilType(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : SoilType() post action called");
            List<SoilTypesResponse> soilTypes = new List<SoilTypesResponse>();
            try
            {
                if (field.SoilTypeID == null)
                {
                    ModelState.AddModelError("SoilTypeID", string.Format(Resource.MsgSelectANameOfFieldBeforeContinuing, Resource.lblSoilType.ToLower()));
                }

                soilTypes = await _fieldService.FetchSoilTypes();
                if (soilTypes.Count > 0 && soilTypes.Any())
                {
                    var country = field.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                    var soilTypesList = soilTypes.Where(x => x.CountryId == country).ToList();
                    soilTypes = soilTypesList;
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.SoilTypesList = soilTypes;
                    return View(field);
                }
                field.SoilType = await _soilService.FetchSoilTypeById(field.SoilTypeID.Value);
                SoilTypesResponse? soilType = soilTypes.FirstOrDefault(x => x.SoilTypeId == field.SoilTypeID);

                bool isSoilTypeChange = false;
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    FieldViewModel fieldData = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                    if (fieldData.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.DeepClayey &&
                        field.SoilTypeID == (int)NMP.Portal.Enums.SoilTypeEngland.DeepClayey)
                    {
                        isSoilTypeChange = true;
                    }
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
                if (field.SoilTypeID == (int)NMP.Portal.Enums.SoilTypeEngland.Shallow)
                {
                    return RedirectToAction("SoilOverChalk");
                }
                if (field.IsCheckAnswer && (!isSoilTypeChange))
                {
                    field.IsSoilReleasingClay = false;
                    field.SoilReleasingClay = null;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
                    return RedirectToAction("CheckAnswer");
                }

                if (soilType != null && soilType.KReleasingClay)
                {
                    field.IsSoilReleasingClay = true;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
                    return RedirectToAction("SoilReleasingClay");
                }

                field.SoilReleasingClay = null;
                field.IsSoilReleasingClay = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
                if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
                {
                    return RedirectToAction("UpdateField");
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in SoilType() post action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = ex.Message;
                return View(field);
            }
            return RedirectToAction("RecentSoilAnalysisQuestion");
        }

        [HttpGet]
        public IActionResult SoilReleasingClay()
        {
            _logger.LogTrace($"Field Controller : SoilReleasingClay() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoilReleasingClay(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : SoilReleasingClay() post action called");
            if (field.SoilReleasingClay == null)
            {
                ModelState.AddModelError("SoilReleasingClay", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            //if (field.SoilAnalyses.SulphurDeficient == null)
            //{
            //    ModelState.AddModelError("SoilAnalyses.SulphurDeficient", Resource.MsgSelectAnOptionBeforeContinuing);
            //}
            field.IsSoilReleasingClay = true;
            if (!ModelState.IsValid)
            {
                return View(field);
            }
            List<SoilTypesResponse> soilTypes = new List<SoilTypesResponse>();

            soilTypes = await _fieldService.FetchSoilTypes();
            if (soilTypes.Count > 0 && soilTypes.Any())
            {
                var country = field.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                var soilTypesList = soilTypes.Where(x => x.CountryId == country).ToList();
                soilTypes = soilTypesList;
            }
            var soilType = soilTypes?.Where(x => x.SoilTypeId == field.SoilTypeID).FirstOrDefault();
            if (!soilType.KReleasingClay)
            {
                field.SoilReleasingClay = false;
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer && (!field.IsRecentSoilAnalysisQuestionChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("RecentSoilAnalysisQuestion");
        }

        [HttpGet]
        public IActionResult SulphurDeficient()
        {
            _logger.LogTrace($"Field Controller : SulphurDeficient() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            model.IsSoilReleasingClay = false;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SulphurDeficient(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : SulphurDeficient() action called");
            if (field.SoilAnalyses.SulphurDeficient == null)
            {
                ModelState.AddModelError("SoilAnalyses.SulphurDeficient", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            //if (field.IsSoilReleasingClay)
            //{
            //    field.IsSoilReleasingClay = false;
            //    field.SoilReleasingClay = null;
            //}
            if (!ModelState.IsValid)
            {
                return View(field);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer && (!field.IsRecentSoilAnalysisQuestionChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            return RedirectToAction("SoilDate");
        }

        [HttpGet]
        public async Task<IActionResult> SoilDate()
        {
            _logger.LogTrace($"Field Controller : SoilDateAndPHLevel() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SoilDate(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : SoilDateAndPHLevel() post action called");
            if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilAnalyses.Date"))
            {
                var dateError = ModelState["SoilAnalyses.Date"].Errors.Count > 0 ?
                                ModelState["SoilAnalyses.Date"].Errors[0].ErrorMessage.ToString() : null;

                //if (dateError != null && dateError.Equals(string.Format(Resource.MsgDateMustBeARealDate, Resource.lblDateSampleTaken)))
                //{
                //    ModelState["SoilAnalyses.Date"].Errors.Clear();
                //    ModelState["SoilAnalyses.Date"].Errors.Add(Resource.MsgEnterTheDateInNumber);
                //}

                if (dateError != null && dateError.Equals(string.Format(Resource.MsgDateMustBeARealDate, Resource.lblTheDate)) ||
                    dateError.Equals(string.Format(Resource.MsgDateMustIncludeAMonth, Resource.lblTheDate)) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeAMonthAndYear, Resource.lblTheDate)) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeADayAndYear, Resource.lblTheDate)) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeAYear, Resource.lblTheDate)) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeADay, Resource.lblTheDate)) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeADayAndMonth, Resource.lblTheDate)))
                {
                    ModelState["SoilAnalyses.Date"].Errors.Clear();
                    ModelState["SoilAnalyses.Date"].Errors.Add(Resource.MsgTheDateMustInclude);
                }
            }
            if (model.SoilAnalyses.Date == null)
            {
                ModelState.AddModelError("SoilAnalyses.Date", Resource.MsgEnterADateBeforeContinuing);
            }
            //if (model.SoilAnalyses.PH == null)
            //{
            //    ModelState.AddModelError("SoilAnalyses.PH", Resource.MsgEnterAPHBeforeContinuing);
            //}
            if (DateTime.TryParseExact(model.SoilAnalyses.Date.ToString(), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                ModelState.AddModelError("SoilAnalyses.Date", Resource.MsgEnterTheDateInNumber);
            }

            if (model.SoilAnalyses.Date != null)
            {
                if (model.SoilAnalyses.Date.Value.Date.Year < 1601 || model.SoilAnalyses.Date.Value.Date.Year > DateTime.Now.AddYears(1).Year)
                {
                    ModelState.AddModelError("SoilAnalyses.Date", Resource.MsgEnterTheDateInNumber);
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.IsCheckAnswer && (!model.IsRecentSoilAnalysisQuestionChange))
            {
                return RedirectToAction("CheckAnswer");
            }

            return RedirectToAction("SoilNutrientValueType");
        }
        [HttpGet]
        public async Task<IActionResult> SoilNutrientValueType()
        {
            _logger.LogTrace($"Field Controller : SoilNutrientValueType() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SoilNutrientValueType(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : SoilNutrientValueType() post action called");
            if (model.IsSoilNutrientValueTypeIndex == null)
            {
                ModelState.AddModelError("IsSoilNutrientValueTypeIndex", Resource.MsgSelectAnOptionBeforeContinuing);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);

            return RedirectToAction("SoilNutrientValue");
        }
        [HttpGet]
        public async Task<IActionResult> SoilNutrientValue()
        {
            _logger.LogTrace($"Field Controller : SoilNutrientValue() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoilNutrientValue(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : SoilNutrientValue() post action called");
            Error error = null;
            try
            {
                if (model.IsSoilNutrientValueTypeIndex != null && model.IsSoilNutrientValueTypeIndex.Value)
                {
                    if (!string.IsNullOrEmpty(model.PotassiumIndexValue))
                    {
                        if (int.TryParse(model.PotassiumIndexValue, out int value))
                        {
                            if (value > 9 || value < 0)
                            {
                                ModelState.AddModelError("PotassiumIndexValue", Resource.MsgEnterValidValueForNutrientIndex);
                            }
                            if (value == 2)
                            {
                                ModelState.AddModelError("PotassiumIndexValue", string.Format(Resource.MsgValueIsNotAValidValueForPotassium, value));
                            }
                        }
                        else
                        {
                            if ((model.PotassiumIndexValue.ToString() != Resource.lblTwoMinus) &&
                                                   (model.PotassiumIndexValue.ToString() != Resource.lblTwoPlus))
                            {
                                ModelState.AddModelError("PotassiumIndexValue", Resource.MsgTheValueMustBeAnIntegerValueBetweenZeroAndNine);
                            }
                        }


                    }
                    if (model.SoilAnalyses.PH == null && (string.IsNullOrWhiteSpace(model.PotassiumIndexValue)) &&
                    model.SoilAnalyses.PhosphorusIndex == null && model.SoilAnalyses.MagnesiumIndex == null)
                    {
                        ViewData["IsPostRequest"] = true;
                        ModelState.AddModelError("FocusFirstEmptyField", Resource.MsgForPhPhosphorusPotassiumMagnesium);
                    }
                    if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilAnalyses.PhosphorusIndex"))
                    {
                        var InvalidFormatError = ModelState["SoilAnalyses.PhosphorusIndex"].Errors.Count > 0 ?
                                        ModelState["SoilAnalyses.PhosphorusIndex"].Errors[0].ErrorMessage.ToString() : null;

                        if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SoilAnalyses.PhosphorusIndex"].AttemptedValue, Resource.lblPhosphorusIndex)))
                        {
                            ModelState["SoilAnalyses.PhosphorusIndex"].Errors.Clear();
                            ModelState["SoilAnalyses.PhosphorusIndex"].Errors.Add(Resource.MsgTheValueMustBeAnIntegerValueBetweenZeroAndNine);
                        }
                    }



                    if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilAnalyses.MagnesiumIndex"))
                    {
                        var InvalidFormatError = ModelState["SoilAnalyses.MagnesiumIndex"].Errors.Count > 0 ?
                                        ModelState["SoilAnalyses.MagnesiumIndex"].Errors[0].ErrorMessage.ToString() : null;

                        if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SoilAnalyses.MagnesiumIndex"].AttemptedValue, Resource.lblMagnesiumIndex)))
                        {
                            ModelState["SoilAnalyses.MagnesiumIndex"].Errors.Clear();
                            ModelState["SoilAnalyses.MagnesiumIndex"].Errors.Add(Resource.MsgTheValueMustBeAnIntegerValueBetweenZeroAndNine);
                        }
                    }
                }
                else
                {
                    if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilAnalyses.Potassium"))
                    {
                        var InvalidFormatError = ModelState["SoilAnalyses.Potassium"].Errors.Count > 0 ?
                                        ModelState["SoilAnalyses.Potassium"].Errors[0].ErrorMessage.ToString() : null;

                        if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SoilAnalyses.Potassium"].AttemptedValue, Resource.lblPotassiumPerLitreOfSoil)))
                        {
                            ModelState["SoilAnalyses.Potassium"].Errors.Clear();
                            ModelState["SoilAnalyses.Potassium"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblAmount));
                        }
                    }
                    if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilAnalyses.Phosphorus"))
                    {
                        var InvalidFormatError = ModelState["SoilAnalyses.Phosphorus"].Errors.Count > 0 ?
                                        ModelState["SoilAnalyses.Phosphorus"].Errors[0].ErrorMessage.ToString() : null;

                        if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SoilAnalyses.Phosphorus"].AttemptedValue, Resource.lblPhosphorusPerLitreOfSoil)))
                        {
                            ModelState["SoilAnalyses.Phosphorus"].Errors.Clear();
                            ModelState["SoilAnalyses.Phosphorus"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblAmount));
                        }
                    }
                    if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilAnalyses.Magnesium"))
                    {
                        var InvalidFormatError = ModelState["SoilAnalyses.Magnesium"].Errors.Count > 0 ?
                                        ModelState["SoilAnalyses.Magnesium"].Errors[0].ErrorMessage.ToString() : null;

                        if (InvalidFormatError != null && InvalidFormatError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SoilAnalyses.Magnesium"].AttemptedValue, Resource.lblMagnesiumPerLitreOfSoil)))
                        {
                            ModelState["SoilAnalyses.Magnesium"].Errors.Clear();
                            ModelState["SoilAnalyses.Magnesium"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblAmount));
                        }
                    }
                    if (model.SoilAnalyses.PH == null && model.SoilAnalyses.Potassium == null &&
                        model.SoilAnalyses.Phosphorus == null && model.SoilAnalyses.Magnesium == null)
                    {
                        ViewData["IsPostRequest"] = true;
                        ModelState.AddModelError("FocusFirstEmptyField", Resource.MsgForPhPhosphorusPotassiumMagnesium);
                    }

                }

                if (!ModelState.IsValid)
                {
                    return View(model);
                }
                model.SoilAnalyses.PhosphorusMethodologyID = (int)PhosphorusMethodology.Olsens;

                if (model.SoilAnalyses.Phosphorus != null || model.SoilAnalyses.Potassium != null ||
                    model.SoilAnalyses.Magnesium != null)
                {
                    if (model.IsSoilNutrientValueTypeIndex != null && (!model.IsSoilNutrientValueTypeIndex.Value))
                    {
                        (List<NutrientResponseWrapper> nutrients, error) = await _fieldService.FetchNutrientsAsync();
                        if (error == null && nutrients.Count > 0)
                        {
                            int phosphorusId = 1;
                            int potassiumId = 2;
                            int magnesiumId = 3;

                            if (model.SoilAnalyses.Phosphorus != null)
                            {
                                var phosphorusNutrient = nutrients.FirstOrDefault(a => a.nutrient.Equals(Resource.lblPhosphate));
                                if (phosphorusNutrient != null)
                                {
                                    phosphorusId = phosphorusNutrient.nutrientId;
                                }
                                (string phosphorusIndexValue, error) = await _soilService.FetchSoilNutrientIndex(phosphorusId, model.SoilAnalyses.Phosphorus, (int)PhosphorusMethodology.Olsens);
                                if (!string.IsNullOrWhiteSpace(phosphorusIndexValue) && error == null)
                                {
                                    model.SoilAnalyses.PhosphorusIndex = Convert.ToInt32(phosphorusIndexValue.Trim());

                                }
                                else if (error != null)
                                {
                                    ViewBag.Error = error.Message;
                                    return View(model);
                                }
                            }
                            if (model.SoilAnalyses.Magnesium != null)
                            {
                                var magnesiumNutrient = nutrients.FirstOrDefault(a => a.nutrient.Equals(Resource.lblMagnesium));
                                if (magnesiumNutrient != null)
                                {
                                    magnesiumId = magnesiumNutrient.nutrientId;
                                }
                                (string magnesiumIndexValue, error) = await _soilService.FetchSoilNutrientIndex(magnesiumId, model.SoilAnalyses.Magnesium, (int)MagnesiumMethodology.None);
                                if (!string.IsNullOrWhiteSpace(magnesiumIndexValue) && error == null)
                                {
                                    model.SoilAnalyses.MagnesiumIndex = Convert.ToInt32(magnesiumIndexValue.Trim());
                                }
                                else if (error != null)
                                {
                                    ViewBag.Error = error.Message;
                                    return View(model);
                                }
                            }
                            if (model.SoilAnalyses.Potassium != null)
                            {
                                var potassiumNutrient = nutrients.FirstOrDefault(a => a.nutrient.Equals(Resource.lblPotash));
                                if (potassiumNutrient != null)
                                {
                                    potassiumId = potassiumNutrient.nutrientId;
                                }
                                (string potassiumIndexValue, error) = await _soilService.FetchSoilNutrientIndex(potassiumId, model.SoilAnalyses.Potassium, (int)PotassiumMethodology.None);
                                if (!string.IsNullOrWhiteSpace(potassiumIndexValue) && error == null)
                                {
                                    model.PotassiumIndexValue = potassiumIndexValue.Trim();
                                }
                                else if (error != null)
                                {
                                    ViewBag.Error = error.Message;
                                    return View(model);
                                }
                            }
                        }
                        if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                        {
                            ViewBag.Error = error.Message;
                            return View(model);
                        }
                    }
                    else
                    {
                        model.SoilAnalyses.Phosphorus = null;
                        model.SoilAnalyses.Magnesium = null;
                        model.SoilAnalyses.Potassium = null;

                    }
                }

                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
                if (model.IsCheckAnswer)
                {
                    return RedirectToAction("CheckAnswer");
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in SoilNutrientValue() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = string.Concat(error, ex.Message);
                return View(model);
            }
            return RedirectToAction("HasGrassInLastThreeYear");
        }

        [HttpGet]
        public async Task<IActionResult> CropGroups()
        {
            _logger.LogTrace($"Field Controller : CropGroups() action called");
            FieldViewModel model = new FieldViewModel();
            List<CropGroupResponse> cropGroups = new List<CropGroupResponse>();

            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate))
                {
                    int fieldId = Convert.ToInt32(_fieldDataProtector.Unprotect(model.EncryptedFieldId));
                    List<Crop> cropPlans = await _cropService.FetchCropsByFieldId(fieldId);
                    if(cropPlans.Any(cp => cp.Year == model.LastHarvestYear))
                    {
                        return RedirectToAction("UpdateField");
                    }
                }
                cropGroups = await _fieldService.FetchCropGroups();
                List<CropGroupResponse> cropGroupArables = cropGroups.Where(x => x.CropGroupId != (int)NMP.Portal.Enums.CropGroup.Grass).OrderBy(x => x.CropGroupName).ToList();
                //_httpContextAccessor.HttpContext.Session.SetObjectAsJson("CropGroupList", cropGroups);
                ViewBag.CropGroupList = cropGroupArables;
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in CropGroups() action : {ex.Message}, {ex.StackTrace}");
                //TempData["Error"] = ex.Message;
                if (model.RecentSoilAnalysisQuestion != null && model.RecentSoilAnalysisQuestion.Value == true)
                {
                    ViewBag.Error = ex.Message;
                    return RedirectToAction("SoilNutrientValue");
                }
                else if (model.PreviousCroppings.HasGrassInLastThreeYear != null)
                {
                    TempData["Error"] = ex.Message;
                    return RedirectToAction("HasGrassInLastThreeYear");
                }
                else
                {
                    TempData["Error"] = ex.Message;
                    return RedirectToAction("RecentSoilAnalysisQuestion");
                }
                //return RedirectToAction("SNSCalculationMethod");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CropGroups(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : CropGroups() post action called");
            if (field.CropGroupId == null)
            {
                ModelState.AddModelError("CropGroupId", string.Format(Resource.MsgSelectANameOfFieldBeforeContinuing, Resource.lblCropGroup.ToLower()));
            }
            if (!ModelState.IsValid)
            {
                List<CropGroupResponse> cropGroups = new List<CropGroupResponse>();
                cropGroups = await _fieldService.FetchCropGroups();
                if (cropGroups.Count > 0)
                {
                    ViewBag.CropGroupList = cropGroups.OrderBy(x => x.CropGroupName);
                }
                return View(field);
            }

            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                FieldViewModel fieldData = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                if (fieldData.CropGroupId != field.CropGroupId)
                {
                    field.CropType = string.Empty;
                    field.CropTypeID = null;
                }
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            field.CropGroup = await _fieldService.FetchCropGroupById(field.CropGroupId.Value);
            _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", field);

            return RedirectToAction("CropTypes");
        }

        [HttpGet]
        public async Task<IActionResult> CropTypes()
        {
            _logger.LogTrace($"Field Controller : CropTypes() action called");
            FieldViewModel model = new FieldViewModel();
            List<CropTypeResponse> cropTypes = new List<CropTypeResponse>();

            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                cropTypes = await _fieldService.FetchCropTypes(model.CropGroupId ?? 0);
                var country = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                var cropTypeList = cropTypes.Where(x => x.CountryId == country || x.CountryId == (int)NMP.Portal.Enums.RB209Country.All).OrderBy(c => c.CropType).ToList();

                ViewBag.CropTypeList = cropTypeList;
                if (cropTypeList.Count == 1)
                {
                    if (cropTypeList[0].CropTypeId == (int)NMP.Portal.Enums.CropTypes.Other)
                    {
                        model.CropTypeID = cropTypeList[0].CropTypeId;
                        model.CropType = cropTypeList[0].CropType;
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", model);
                        if (model.IsCheckAnswer)
                        {
                            return RedirectToAction("CheckAnswer");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in CropTypes() action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = ex.Message;
                return RedirectToAction("CropGroups");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CropTypes(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : CropTypes() post action called");
            if (field.CropTypeID == null)
            {
                ModelState.AddModelError("CropTypeID", string.Format(Resource.MsgSelectANameOfFieldBeforeContinuing, Resource.lblCropType.ToLower()));
            }
            if (!ModelState.IsValid)
            {
                List<CropTypeResponse> cropTypes = new List<CropTypeResponse>();
                cropTypes = await _fieldService.FetchCropTypes(field.CropGroupId ?? 0);
                var country = field.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                ViewBag.CropTypeList = cropTypes.Where(x => x.CountryId == country || x.CountryId == (int)NMP.Portal.Enums.RB209Country.All).OrderBy(c => c.CropType).ToList();
                return View(field);
            }
            field.CropType = await _fieldService.FetchCropTypeById(field.CropTypeID.Value);
            _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("CheckAnswer");
        }


        [HttpGet]
        public async Task<IActionResult> CheckAnswer()
        {
            _logger.LogTrace($"Field Controller : CheckAnswer() action called");
            FieldViewModel? model = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                if (model == null)
                {
                    model = new FieldViewModel();
                }
                model.IsRecentSoilAnalysisQuestionChange = false;
                model.IsCheckAnswer = true;
                if (model.SoilOverChalk != null && model.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.Shallow)
                {
                    model.SoilOverChalk = null;
                }
                if (model.SoilReleasingClay != null && model.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.DeepClayey)
                {
                    model.SoilReleasingClay = null;
                    model.IsSoilReleasingClay = false;
                }
                List<CommonResponse> grassManagements = await _fieldService.GetGrassManagementOptions();
                ViewBag.GrassManagementOptions = grassManagements?.FirstOrDefault(x => x.Id == model.PreviousCroppings.GrassManagementOptionID)?.Name;

                List<CommonResponse> soilNitrogenSupplyItems = await _fieldService.GetSoilNitrogenSupplyItems();
                ViewBag.SoilNitrogenSupplyItems = soilNitrogenSupplyItems?.FirstOrDefault(x => x.Id == model.PreviousCroppings.SoilNitrogenSupplyItemID)?.Name;
                model.IsHasGrassInLastThreeYearChange = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in CheckAnswer() action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = ex.Message;
                return RedirectToAction("CropTypes");
            }
            return View(model);

        }

        public async Task<IActionResult> BackCheckAnswer()
        {
            _logger.LogTrace($"Field Controller : BackCheckAnswer() action called");
            FieldViewModel? model = null;
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            model.IsCheckAnswer = false;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);

            if (model.PreviousCroppings.HasGrassInLastThreeYear != null && model.PreviousCroppings.HasGrassInLastThreeYear.Value)
            {
                if (!model.PreviousGrassYears.Contains(model.LastHarvestYear ?? 0))
                {
                    return RedirectToAction("CropTypes");
                }
                else
                {
                    if (model.PreviousCroppings.HasGreaterThan30PercentClover == false)
                    {
                        return RedirectToAction("SoilNitrogenSupplyItems");
                    }
                    else
                    {
                        return RedirectToAction("HasGreaterThan30PercentClover");
                    }
                }

            }
            return RedirectToAction("CropTypes");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckAnswer(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : CheckAnswer() post action called");
            try
            {
                if (model.PreviousCroppings != null && model.PreviousCroppings.HasGrassInLastThreeYear == false)
                {
                    if (!model.CropGroupId.HasValue)
                    {
                        ModelState.AddModelError("CropGroupId", string.Format("{0} {1}", string.Format(Resource.lblWhatWasThePreviousCropGroupForCheckAnswere, model.LastHarvestYear), Resource.lblNotSet));
                    }
                    if (!model.CropTypeID.HasValue)
                    {
                        ModelState.AddModelError("CropTypeID", string.Format("{0} {1}", string.Format(Resource.lblWhatWasThePreviousCropTypeForCheckAnswere, model.LastHarvestYear), Resource.lblNotSet));
                    }
                }
                if (model.PreviousCroppings != null && model.PreviousCroppings.HasGrassInLastThreeYear == true)
                {
                    if (!model.PreviousGrassYears.Contains(model.LastHarvestYear.Value))
                    {
                        if (string.IsNullOrWhiteSpace(model.CropGroup))
                        {
                            ModelState.AddModelError("CropGroupId", string.Format("{0} {1}", string.Format(Resource.lblWhatWasThePreviousCropGroupForCheckAnswere, model.LastHarvestYear), Resource.lblNotSet));
                        }
                        if (string.IsNullOrWhiteSpace(model.CropType))
                        {
                            ModelState.AddModelError("CropTypeID", string.Format("{0} {1}", string.Format(Resource.lblWhatWasThePreviousCropTypeForCheckAnswere, model.LastHarvestYear), Resource.lblNotSet));
                            //ModelState.AddModelError("CropGroupId", string.Format("{0} {1}", string.Format(Resource.lblWhatWasThePreviousCropGroup, model.LastHarvestYear), Resource.lblNotSet));
                        }
                    }


                    if (model.PreviousGrassYears == null)
                    {
                        ModelState.AddModelError("PreviousGrassYears", string.Format("{0} {1}", string.Format(Resource.lblInWhichYearsWasUsedForGrass, model.Name), Resource.lblNotSet));
                    }
                    if (model.PreviousCroppings.GrassManagementOptionID == null)
                    {
                        ModelState.AddModelError("PreviousCroppings.GrassManagementOptionID", string.Format("{0} {1}", Resource.lblHowWasTheGrassTypicallyManagedEachYear, Resource.lblNotSet));
                    }

                    if (model.PreviousCroppings.HasGreaterThan30PercentClover == null)
                    {
                        ModelState.AddModelError("PreviousCroppings.HasGreaterThan30PercentClover", string.Format("{0} {1}", string.Format(Resource.lblDoesFieldTypicallyHaveMoreThan30PercentClover, model.Name), Resource.lblNotSet));
                    }
                    else
                    {
                        if ((!model.PreviousCroppings.HasGreaterThan30PercentClover.Value) && model.PreviousCroppings.SoilNitrogenSupplyItemID == null)
                        {
                            ModelState.AddModelError("PreviousCroppings.SoilNitrogenSupplyItemID", string.Format("{0} {1}", string.Format(Resource.lblHowMuchNitrogenHasBeenAppliedToFieldEachYear, model.Name), Resource.lblNotSet));
                        }
                    }
                }

                //if (model.PreviousCroppings.HasGrassInLastThreeYear == false)
                //{
                //    if (model.WantToApplySns == null)
                //    {
                //        ModelState.AddModelError("WantToApplySns", string.Format("{0} {1}", string.Format(Resource.lblHowWouldYouLikeToCalculateSoilNitrogenSupply, model.Name), Resource.lblNotSet));
                //    }
                //}

                if (model.RecentSoilAnalysisQuestion.Value)
                {
                    if (model.IsSoilReleasingClay && !model.SoilReleasingClay.HasValue)
                    {
                        ModelState.AddModelError("SoilReleasingClay", Resource.MsgSoilReleasingClayNotSet);
                    }
                    if (!model.SoilAnalyses.Date.HasValue)
                    {
                        ModelState.AddModelError("SoilAnalyses.Date", Resource.MsgSampleDateNotSet);
                    }
                    if (!model.SoilAnalyses.SulphurDeficient.HasValue)
                    {
                        ModelState.AddModelError("SoilAnalyses.SulphurDeficient", Resource.lblSoilDeficientInSulpurForCheckAnswerNotset);
                    }

                    if (model.IsSoilNutrientValueTypeIndex.HasValue)
                    {

                        if (!model.IsSoilNutrientValueTypeIndex.Value)
                        {
                            if (!model.SoilAnalyses.PH.HasValue && !model.SoilAnalyses.Potassium.HasValue &&
                                !model.SoilAnalyses.Phosphorus.HasValue && !model.SoilAnalyses.Magnesium.HasValue)
                            {
                                if (!model.SoilAnalyses.PH.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.PH", Resource.MsgPhNotSet);
                                }
                                if (!model.SoilAnalyses.Potassium.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.Potassium", Resource.MsgPotassiumNotSet);
                                }
                                if (!model.SoilAnalyses.Phosphorus.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.Phosphorus", Resource.MsgPhosphorusNotSet);
                                }
                                if (!model.SoilAnalyses.Magnesium.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.Magnesium", Resource.MsgMagnesiumNotSet);
                                }
                            }

                        }
                        else
                        {
                            if (!model.SoilAnalyses.PH.HasValue && string.IsNullOrWhiteSpace(model.PotassiumIndexValue) &&
                                !model.SoilAnalyses.MagnesiumIndex.HasValue && !model.SoilAnalyses.PhosphorusIndex.HasValue)
                            {
                                if (!model.SoilAnalyses.PH.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.PH", Resource.MsgPhNotSet);
                                }
                                if (string.IsNullOrWhiteSpace(model.PotassiumIndexValue))
                                {
                                    ModelState.AddModelError("PotassiumIndexValue", Resource.MsgPotassiumIndexNotSet);
                                }
                                if (!model.SoilAnalyses.PhosphorusIndex.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.PhosphorusIndex", Resource.MsgPhosphorusIndexNotSet);
                                }
                                if (!model.SoilAnalyses.MagnesiumIndex.HasValue)
                                {
                                    ModelState.AddModelError("SoilAnalyses.MagnesiumIndex", Resource.MsgMagnesiumIndexNotSet);
                                }
                            }
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("IsSoilNutrientValueTypeIndex", Resource.MsgNutrientValueTypeForCheckAnswereNotSet);
                    }
                }
                if (!ModelState.IsValid)
                {
                    List<CommonResponse> grassManagements = await _fieldService.GetGrassManagementOptions();
                    ViewBag.GrassManagementOptions = grassManagements?.FirstOrDefault(x => x.Id == model.PreviousCroppings.GrassManagementOptionID)?.Name;


                    List<CommonResponse> soilNitrogenSupplyItems = await _fieldService.GetSoilNitrogenSupplyItems();
                    ViewBag.SoilNitrogenSupplyItems = soilNitrogenSupplyItems?.FirstOrDefault(x => x.Id == model.PreviousCroppings.SoilNitrogenSupplyItemID)?.Name;

                    return View("CheckAnswer", model);
                }
                int userId = Convert.ToInt32(HttpContext.User.FindFirst("UserId")?.Value);  // Convert.ToInt32(HttpContext.User.FindFirst(ClaimTypes.Sid)?.Value);
                var farmId = _farmDataProtector.Unprotect(model.EncryptedFarmId);
                //int farmId = model.FarmID;
                if (model.SoilAnalyses.Potassium != null || model.SoilAnalyses.Phosphorus != null || (!string.IsNullOrWhiteSpace(model.PotassiumIndexValue)) || model.SoilAnalyses.PhosphorusIndex != null)
                {
                    model.PKBalance.PBalance = 0;
                    model.PKBalance.KBalance = 0;
                    model.PKBalance.Year = model.SoilAnalyses.Date.Value.Year;
                }
                else
                {
                    model.PKBalance = null;
                }
                int? lastGroupNumber = null;
                Error error = new Error();
                (Farm farm, error) = await _farmService.FetchFarmByIdAsync(Convert.ToInt32(farmId));

                if (farm != null && (string.IsNullOrWhiteSpace(error.Message)))
                {
                    (List<HarvestYearPlanResponse> harvestYearPlanResponse, error) = await _cropService.FetchHarvestYearPlansByFarmId(model.LastHarvestYear.Value, Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId)));

                    if (harvestYearPlanResponse != null && harvestYearPlanResponse.Count > 0)
                    {
                        var lastGroup = harvestYearPlanResponse.Where(cg => !string.IsNullOrEmpty(cg.CropGroupName) && cg.CropGroupName.StartsWith("Crop group") &&
                                         int.TryParse(cg.CropGroupName.Split(' ')[2], out _))
                                        .OrderByDescending(cg => int.Parse(cg.CropGroupName.Split(' ')[2]))
                                        .FirstOrDefault();
                        if (lastGroup != null)
                        {
                            lastGroupNumber = int.Parse(lastGroup.CropGroupName.Split(' ')[2]);
                        }
                    }

                    if (lastGroupNumber != null)
                    {
                        model.CropGroupName = string.Format(Resource.lblCropGroupWithCounter, (lastGroupNumber + 1));
                    }
                    else
                    {
                        model.CropGroupName = string.Format(Resource.lblCropGroupWithCounter, 1);
                    }
                }
                else
                {
                    TempData["AddFieldError"] = Resource.MsgWeCouldNotAddYourFieldPleaseTryAgainLater;
                    return RedirectToAction("CheckAnswer");
                }
                List<PreviousCropping> previousCropping = new List<PreviousCropping>();

                if (model.IsPreviousYearGrass == true && model.PreviousGrassYears != null)
                {
                    model.CropGroupId = (int)NMP.Portal.Enums.CropGroup.Grass;
                    model.CropTypeID = (int)NMP.Portal.Enums.CropTypes.Grass;
                    foreach (var year in model.PreviousGrassYears)
                    {
                        model.PreviousCroppings.HarvestYear = year;

                        var newPreviousCropping = new PreviousCropping
                        {
                            CropGroupID = model.CropGroupId,
                            CropTypeID = model.CropTypeID,
                            HasGrassInLastThreeYear = model.PreviousCroppings.HasGrassInLastThreeYear ?? false,
                            HarvestYear = year,
                            LayDuration = model.PreviousCroppings.LayDuration,
                            GrassManagementOptionID = model.PreviousCroppings.GrassManagementOptionID,
                            HasGreaterThan30PercentClover = model.PreviousCroppings.HasGreaterThan30PercentClover,
                            SoilNitrogenSupplyItemID = model.PreviousCroppings.SoilNitrogenSupplyItemID
                        };
                        previousCropping.Add(newPreviousCropping);
                    }
                }
                else
                {
                    var newPreviousCropping = new PreviousCropping
                    {
                        CropGroupID = model.CropGroupId,
                        CropTypeID = model.CropTypeID,
                        HasGrassInLastThreeYear = model.PreviousCroppings.HasGrassInLastThreeYear ?? false,
                        HarvestYear = model.LastHarvestYear,
                        LayDuration = null,
                        GrassManagementOptionID = null,
                        HasGreaterThan30PercentClover = null,
                        SoilNitrogenSupplyItemID = null
                    };
                    previousCropping.Add(newPreviousCropping);

                    if(model.PreviousGrassYears != null)
                    {
                        model.CropGroupId = (int)NMP.Portal.Enums.CropGroup.Grass;
                        model.CropTypeID = (int)NMP.Portal.Enums.CropTypes.Grass;
                        foreach (var year in model.PreviousGrassYears)
                        {
                            model.PreviousCroppings.HarvestYear = year;

                            var newPreviousGass = new PreviousCropping
                            {
                                CropGroupID = model.CropGroupId,
                                CropTypeID = model.CropTypeID,
                                HasGrassInLastThreeYear = model.PreviousCroppings.HasGrassInLastThreeYear ?? false,
                                HarvestYear = year,
                                LayDuration = model.PreviousCroppings.LayDuration,
                                GrassManagementOptionID = model.PreviousCroppings.GrassManagementOptionID,
                                HasGreaterThan30PercentClover = model.PreviousCroppings.HasGreaterThan30PercentClover,
                                SoilNitrogenSupplyItemID = model.PreviousCroppings.SoilNitrogenSupplyItemID
                            };
                            previousCropping.Add(newPreviousGass);
                        }
                    }
                    
                }


                if (!string.IsNullOrWhiteSpace(model.PotassiumIndexValue))
                {
                    if (model.PotassiumIndexValue == Resource.lblTwoMinus)
                    {
                        model.SoilAnalyses.PotassiumIndex = Convert.ToInt32(Resource.lblMinusTwo);
                    }
                    else if (model.PotassiumIndexValue == Resource.lblTwoPlus)
                    {
                        model.SoilAnalyses.PotassiumIndex = Convert.ToInt32(Resource.lblPlusTwo);
                    }
                    else
                    {
                        model.SoilAnalyses.PotassiumIndex = Convert.ToInt32(model.PotassiumIndexValue.Trim());
                    }
                }

                FieldData fieldData = new FieldData
                {
                    Field = new Field
                    {
                        SoilTypeID = model.SoilTypeID,
                        NVZProgrammeID = model.IsWithinNVZ == true ? (int)NMP.Portal.Enums.NVZProgram.CurrentNVZRule : (int)NMP.Portal.Enums.NVZProgram.NotInNVZ,
                        Name = model.Name,
                        LPIDNumber = model.LPIDNumber,
                        NationalGridReference = model.NationalGridReference,
                        OtherReference = model.OtherReference,
                        TotalArea = model.TotalArea,
                        CroppedArea = model.CroppedArea,
                        ManureNonSpreadingArea = model.ManureNonSpreadingArea,
                        SoilReleasingClay = model.SoilReleasingClay,
                        SoilOverChalk = model.SoilOverChalk,
                        IsWithinNVZ = model.IsWithinNVZ,
                        IsAbove300SeaLevel = model.IsAbove300SeaLevel,
                        IsActive = true,
                        CreatedOn = DateTime.Now,
                        CreatedByID = userId,
                        ModifiedOn = model.ModifiedOn,
                        ModifiedByID = model.ModifiedByID
                    },
                    SoilAnalysis = (!model.RecentSoilAnalysisQuestion.Value) ? null : new SoilAnalysis
                    {
                        Year = model.SoilAnalyses.Date.Value.Month >= 8 ? model.SoilAnalyses.Date.Value.Year + 1 : model.SoilAnalyses.Date.Value.Year,
                        SulphurDeficient = model.SoilAnalyses.SulphurDeficient,
                        Date = model.SoilAnalyses.Date,
                        PH = model.SoilAnalyses.PH,
                        PhosphorusMethodologyID = model.SoilAnalyses.PhosphorusMethodologyID,
                        Phosphorus = model.SoilAnalyses.Phosphorus,
                        PhosphorusIndex = model.SoilAnalyses.PhosphorusIndex,
                        Potassium = model.SoilAnalyses.Potassium,
                        PotassiumIndex = model.SoilAnalyses.PotassiumIndex,
                        Magnesium = model.SoilAnalyses.Magnesium,
                        MagnesiumIndex = model.SoilAnalyses.MagnesiumIndex,
                        SoilNitrogenSupply = model.SoilAnalyses.SoilNitrogenSupply,
                        SoilNitrogenSupplyIndex = model.SoilAnalyses.SoilNitrogenSupplyIndex,
                        SoilNitrogenSampleDate = model.SampleForSoilMineralNitrogen,
                        Sodium = model.SoilAnalyses.Sodium,
                        Lime = model.SoilAnalyses.Lime,
                        PhosphorusStatus = model.SoilAnalyses.PhosphorusStatus,
                        PotassiumAnalysis = model.SoilAnalyses.PotassiumAnalysis,
                        PotassiumStatus = model.SoilAnalyses.PotassiumStatus,
                        MagnesiumAnalysis = model.SoilAnalyses.MagnesiumAnalysis,
                        MagnesiumStatus = model.SoilAnalyses.MagnesiumStatus,
                        NitrogenResidueGroup = model.SoilAnalyses.NitrogenResidueGroup,
                        Comments = model.SoilAnalyses.Comments,
                        PreviousID = model.SoilAnalyses.PreviousID,
                        CreatedOn = DateTime.Now,
                        CreatedByID = userId,
                        ModifiedOn = model.SoilAnalyses.ModifiedOn,
                        ModifiedByID = model.SoilAnalyses.ModifiedByID
                    },
                    PKBalance = model.PKBalance != null ? model.PKBalance : null,
                    PreviousCroppings = previousCropping

                };

                //FieldData fieldData = new FieldData
                //{
                //    Field = new Field
                //    {
                //        SoilTypeID = model.SoilTypeID,
                //        NVZProgrammeID = model.IsWithinNVZ == true ? (int)NMP.Portal.Enums.NVZProgram.CurrentNVZRule : (int)NMP.Portal.Enums.NVZProgram.NotInNVZ,
                //        Name = model.Name,
                //        LPIDNumber = model.LPIDNumber,
                //        NationalGridReference = model.NationalGridReference,
                //        OtherReference = model.OtherReference,
                //        TotalArea = model.TotalArea,
                //        CroppedArea = model.CroppedArea,
                //        ManureNonSpreadingArea = model.ManureNonSpreadingArea,
                //        SoilReleasingClay = model.SoilReleasingClay,
                //        SoilOverChalk = model.SoilOverChalk,
                //        IsWithinNVZ = model.IsWithinNVZ,
                //        IsAbove300SeaLevel = model.IsAbove300SeaLevel,
                //        IsActive = true,
                //        CreatedOn = DateTime.Now,
                //        CreatedByID = userId,
                //        ModifiedOn = model.ModifiedOn,
                //        ModifiedByID = model.ModifiedByID
                //    },
                //    SoilAnalysis = (!model.RecentSoilAnalysisQuestion.Value) ? null : new SoilAnalysis
                //    {
                //        Year = model.SoilAnalyses.Date.Value.Month >= 8 ? model.SoilAnalyses.Date.Value.Year + 1 : model.SoilAnalyses.Date.Value.Year,
                //        SulphurDeficient = model.SoilAnalyses.SulphurDeficient,
                //        Date = model.SoilAnalyses.Date,
                //        PH = model.SoilAnalyses.PH,
                //        PhosphorusMethodologyID = model.SoilAnalyses.PhosphorusMethodologyID,
                //        Phosphorus = model.SoilAnalyses.Phosphorus,
                //        PhosphorusIndex = model.SoilAnalyses.PhosphorusIndex,
                //        Potassium = model.SoilAnalyses.Potassium,
                //        PotassiumIndex = model.SoilAnalyses.PotassiumIndex,
                //        Magnesium = model.SoilAnalyses.Magnesium,
                //        MagnesiumIndex = model.SoilAnalyses.MagnesiumIndex,
                //        SoilNitrogenSupply = model.SoilAnalyses.SoilNitrogenSupply,
                //        SoilNitrogenSupplyIndex = model.SoilAnalyses.SoilNitrogenSupplyIndex,
                //        SoilNitrogenSampleDate = model.SampleForSoilMineralNitrogen,
                //        Sodium = model.SoilAnalyses.Sodium,
                //        Lime = model.SoilAnalyses.Lime,
                //        PhosphorusStatus = model.SoilAnalyses.PhosphorusStatus,
                //        PotassiumAnalysis = model.SoilAnalyses.PotassiumAnalysis,
                //        PotassiumStatus = model.SoilAnalyses.PotassiumStatus,
                //        MagnesiumAnalysis = model.SoilAnalyses.MagnesiumAnalysis,
                //        MagnesiumStatus = model.SoilAnalyses.MagnesiumStatus,
                //        NitrogenResidueGroup = model.SoilAnalyses.NitrogenResidueGroup,
                //        Comments = model.SoilAnalyses.Comments,
                //        PreviousID = model.SoilAnalyses.PreviousID,
                //        CreatedOn = DateTime.Now,
                //        CreatedByID = userId,
                //        ModifiedOn = model.SoilAnalyses.ModifiedOn,
                //        ModifiedByID = model.SoilAnalyses.ModifiedByID
                //    },
                //    Crops = new List<CropData>
                //    {
                //        new CropData
                //        {
                //            Crop = new Crop
                //            {
                //                Year=model.LastHarvestYear??0,
                //                Confirm=false,
                //                CropTypeID=model.CropTypeID,
                //                FieldType = model.CropGroupId == (int)NMP.Portal.Enums.CropGroup.Grass ? (int)NMP.Portal.Enums.FieldType.    Grass : (int)NMP.Portal.Enums.FieldType.Arable,
                //                CropOrder=1,
                //                CropGroupName=model.CropGroupName,
                //                IsBasePlan=true,
                //                CreatedOn =DateTime.Now,
                //                CreatedByID=userId
                //            },
                //            ManagementPeriods = new List<ManagementPeriod>
                //            {
                //                new ManagementPeriod
                //                {
                //                    Defoliation=1,
                //                    Utilisation1ID=2,
                //                    CreatedOn=DateTime.Now,
                //                    CreatedByID=userId
                //                }

                //            }
                //        },

                //    },
                //    PKBalance = model.PKBalance != null ? model.PKBalance : null,
                //    PreviousCroppings = model.PreviousCroppings.HasGrassInLastThreeYear == true ? grass : null

                //};

                (Field fieldResponse, Error error1) = await _fieldService.AddFieldAsync(fieldData, farm.ID, farm.Name);
                if (error1.Message == null && fieldResponse != null)
                {
                    string success = _farmDataProtector.Protect("true");
                    string fieldName = _farmDataProtector.Protect(fieldResponse.Name);
                    _httpContextAccessor.HttpContext?.Session.Remove("FieldData");

                    return RedirectToAction("ManageFarmFields", new { id = model.EncryptedFarmId, q = success, name = fieldName });
                }
                else
                {
                    TempData["AddFieldError"] = Resource.MsgWeCouldNotAddYourFieldPleaseTryAgainLater;
                    return RedirectToAction("CheckAnswer");
                }
            }
            catch (Exception ex)
            {
                TempData["AddFieldError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ManageFarmFields(string id, string? q, string? name, string? isDeleted)
        {
            _logger.LogTrace($"Field Controller : ManageFarmFields() action called");
            FarmFieldsViewModel model = new FarmFieldsViewModel();
            if (!string.IsNullOrWhiteSpace(q))
            {
                ViewBag.Success = true;
            }
            else
            {
                ViewBag.Success = false;
            }
            if (!string.IsNullOrWhiteSpace(isDeleted))
            {
                ViewBag.FieldName = _fieldDataProtector.Unprotect(name);
                ViewBag.IsDeleted = true;
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(id));
                model.Fields = await _fieldService.FetchFieldsByFarmId(farmId);

                if (model.Fields != null && model.Fields.Count > 0)
                {
                    model.Fields.ForEach(x => x.EncryptedFieldId = _fieldDataProtector.Protect(x.ID.ToString()));
                }
                (Farm farm, Error error) = await _farmService.FetchFarmByIdAsync(farmId);
                model.FarmName = farm.Name;
                if (string.IsNullOrWhiteSpace(isDeleted))
                {
                    if (name != null)
                    {
                        model.FieldName = _farmDataProtector.Unprotect(name);
                    }
                }

                ViewBag.FieldsList = model.Fields;
                model.EncryptedFarmId = id;
            }
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                _httpContextAccessor.HttpContext?.Session.Remove("FieldData");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ManageFarmFields(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : ManageFarmFields() post action called");
            return RedirectToAction("ManageFarmFields");
        }

        [HttpGet]
        public async Task<IActionResult> FieldSoilAnalysisDetail(string id, string farmId, string? q, string? r, string? s, string? t)//id encryptedFieldId,farmID=EncryptedFarmID,q=success,r=FiedlOrSoilAnalysis,s=soilUpdateOrSave
        {
            _logger.LogTrace($"Field Controller : FieldSoilAnalysisDetail() action called");
            FieldViewModel model = new FieldViewModel();
            Error error = new Error();
            (Farm farm, error) = await _farmService.FetchFarmByIdAsync(Convert.ToInt32(_farmDataProtector.Unprotect(farmId)));
            int fieldId = Convert.ToInt32(_fieldDataProtector.Unprotect(id));
            var field = await _fieldService.FetchFieldByFieldId(fieldId);
            model.LastHarvestYear = farm.LastHarvestYear;
            (List<PreviousCropping> prevCroppings, error) = await _previousCroppingService.FetchDataByFieldId(fieldId);

            if (string.IsNullOrWhiteSpace(error.Message))
            {
                List<int> previousYears = new List<int>();
                List<Crop> cropPlans = await _cropService.FetchCropsByFieldId(fieldId);
                List<PreviousCropping> grassCroppings = prevCroppings.Where(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                foreach (var item in grassCroppings)
                {
                    previousYears.Add(item.HarvestYear ?? 0);
                }
                model.PreviousGrassYears = previousYears;

                List<PreviousCropping> previousCroppingsExcludePlan = prevCroppings.Where(pc => !cropPlans.Any(cp => cp.Year == pc.HarvestYear)).ToList();

                var tasks = previousCroppingsExcludePlan.Select(async pc => new
                {
                    pc.ID,
                    pc.FieldID,
                    pc.CropGroupID,
                    pc.CropTypeID,
                    pc.HasGrassInLastThreeYear,
                    pc.HarvestYear,
                    pc.LayDuration,
                    pc.GrassManagementOptionID,
                    pc.HasGreaterThan30PercentClover,
                    pc.SoilNitrogenSupplyItemID,
                    pc.CreatedOn,
                    pc.CreatedByID,
                    pc.ModifiedOn,
                    pc.ModifiedByID,
                    CropTypeName = await _fieldService.FetchCropTypeById(pc.CropTypeID ?? 0)
                }).ToList();

                ViewBag.PreviousCroppingsList = (await Task.WhenAll(tasks)).ToList();

                bool? hasGrassInLastThreeYear = null;
                if (prevCroppings.All(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass))
                {
                    model.IsPreviousYearGrass = false;
                    hasGrassInLastThreeYear = false;
                }
                else
                {
                    model.IsPreviousYearGrass = prevCroppings.FirstOrDefault().CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass ? true : false;
                    hasGrassInLastThreeYear = true;
                    model.PreviousCroppings = prevCroppings.FirstOrDefault(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass);
                }
                model.CropGroupId = prevCroppings.FirstOrDefault(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)?.CropGroupID;
                model.CropTypeID = prevCroppings.FirstOrDefault(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)?.CropTypeID;
                if(model.CropGroupId != null && model.CropTypeID != null)
                {
                    model.CropGroup = await _fieldService.FetchCropGroupById(model.CropGroupId.Value);
                    model.CropType = await _fieldService.FetchCropTypeById(model.CropTypeID.Value);
                }
                
                ViewBag.HasGrassInLastThreeYear = hasGrassInLastThreeYear;


                if (hasGrassInLastThreeYear == true)
                {
                    
                    List<CommonResponse> grassManagements = await _fieldService.GetGrassManagementOptions();
                    ViewBag.GrassManagementOption = grassManagements?.FirstOrDefault(x => x.Id == prevCroppings
                             .Where(pc => pc.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass)
                             .Select(pc => pc.GrassManagementOptionID)
                             .FirstOrDefault())?.Name;

                    
                    List<CommonResponse> soilNitrogenSupplyItems = await _fieldService.GetSoilNitrogenSupplyItems();
                    ViewBag.SoilNitrogenSupplyItem = soilNitrogenSupplyItems?.FirstOrDefault(x =>
                          x.Id == prevCroppings
                            .Where(pc => pc.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass)
                            .Select(pc => pc.SoilNitrogenSupplyItemID)
                            .FirstOrDefault())?.Name;
                }
                
            }


            model.Name = field.Name;
            model.TotalArea = field.TotalArea ?? 0;
            model.CroppedArea = field.CroppedArea ?? 0;
            model.ManureNonSpreadingArea = field.ManureNonSpreadingArea ?? 0;
            //model.SoilType = await _fieldService.FetchSoilTypeById(field.SoilTypeID.Value); 
            model.SoilReleasingClay = field.SoilReleasingClay ?? false;
            model.IsWithinNVZ = field.IsWithinNVZ ?? false;
            model.IsAbove300SeaLevel = field.IsAbove300SeaLevel ?? false;
            if (!string.IsNullOrWhiteSpace(t))
            {
                model.HarvestYear = Convert.ToInt32(_farmDataProtector.Unprotect(t));
                model.EncryptedHarvestYear = t;
            }
            else
            {
                model.HarvestYear = null;
                model.EncryptedHarvestYear = null;
            }
            model.EncryptedFieldId = id;
            model.ID = fieldId;
            model.isEnglishRules = farm.EnglishRules;
            model.SoilOverChalk = field.SoilOverChalk;
            if (farm != null)
            {
                model.IsWithinNVZForFarm = farm.NVZFields == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
                model.IsAbove300SeaLevelForFarm = farm.FieldsAbove300SeaLevel == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
            }
            else
            {
                model.IsWithinNVZForFarm = false;
                model.IsAbove300SeaLevelForFarm = false;
            }
            List<SoilTypesResponse> soilTypes = await _fieldService.FetchSoilTypes();
            if (soilTypes != null && soilTypes.Count > 0)
            {
                SoilTypesResponse? soilType = soilTypes.FirstOrDefault(x => x.SoilTypeId == field.SoilTypeID);
                model.SoilType = !string.IsNullOrWhiteSpace(soilType.SoilType) ? soilType.SoilType : string.Empty;
                model.SoilTypeID = field.SoilTypeID;
                if (soilType != null && soilType.KReleasingClay)
                {
                    ViewBag.IsSoilReleasingClay = true;
                }
                else
                {
                    ViewBag.IsSoilReleasingClay = false;
                }
                if (model.SoilTypeID == (int)NMP.Portal.Enums.SoilTypeEngland.Shallow)
                {
                    ViewBag.IsSoilOverChalk = true;
                }
                else
                {
                    ViewBag.IsSoilOverChalk = false;
                }
            }
            model.EncryptedFarmId = farmId;
            model.FarmName = farm.Name;
            List<SoilAnalysisResponse> soilAnalysisResponse = (await _fieldService.FetchSoilAnalysisByFieldId(fieldId, Resource.lblFalse)).OrderByDescending(x => x.CreatedOn).ToList();
            if (soilAnalysisResponse != null && soilAnalysisResponse.Count > 0)
            {
                soilAnalysisResponse.ForEach(m => m.EncryptedSoilAnalysisId = _fieldDataProtector.Protect(m.ID.ToString()));
                ViewBag.SoilAnalysisList = soilAnalysisResponse;
            }
            if (!string.IsNullOrWhiteSpace(q))
            {

                if (!string.IsNullOrWhiteSpace(r))
                {
                    string statusFor = _fieldDataProtector.Unprotect(r);
                    if (!string.IsNullOrWhiteSpace(statusFor))
                    {
                        if (statusFor == Resource.lblField)
                        {
                            ViewBag.Success = Resource.lblTrue;
                            ViewBag.SuccessMsgContent = string.Format(Resource.lblYouHaveUpdated, model.Name);
                            ViewBag.SuccessMsgContentLink = Resource.MsgViewYourFarmDetails;
                        }
                        else if (statusFor == Resource.lblSoilAnalysis)
                        {
                            if (_soilAnalysisDataProtector.Unprotect(q) == Resource.lblFalse)
                            {
                                ViewBag.Success = Resource.lblFalse;
                                ViewBag.Error = Resource.MsgSoilAnalysisCouldNotAdded;
                            }
                            else
                            {
                                ViewBag.Success = Resource.lblTrue;
                                if (!string.IsNullOrWhiteSpace(s) && _soilAnalysisDataProtector.Unprotect(s) == Resource.lblAdd)
                                {
                                    ViewBag.SuccessMsgContent = string.Format(Resource.lblYouHaveAddedANewSoilAnalysisForFieldName, model.Name);
                                }
                                else if (!string.IsNullOrWhiteSpace(s) && _soilAnalysisDataProtector.Unprotect(s) == Resource.lblUpdate)
                                {
                                    ViewBag.SuccessMsgContent = string.Format(Resource.lblYouHaveUpdatedASoilAnalysisForFieldName, model.Name);
                                }
                                else if (!string.IsNullOrWhiteSpace(s) && _soilAnalysisDataProtector.Unprotect(s) == Resource.lblRemove)
                                {
                                    ViewBag.SuccessMsgContent = string.Format(Resource.lblYouHaveRemovedASoilAnalysisForFieldName, model.Name);
                                }
                                List<Crop> crop = (await _cropService.FetchCropsByFieldId(model.ID.Value)).ToList();
                                if (crop != null && crop.Count > 0)
                                {
                                    if (soilAnalysisResponse.Count > 0)
                                    {
                                        bool anyPlan = crop.Any(x => x.Year >= (soilAnalysisResponse.FirstOrDefault()?.Year ?? 0));
                                        if (anyPlan)
                                        {
                                            int cropYear = crop.FirstOrDefault(x => x.Year >= soilAnalysisResponse.FirstOrDefault().Year).Year;
                                            if (!string.IsNullOrWhiteSpace(s) && _soilAnalysisDataProtector.Unprotect(s) == Resource.lblAdd)
                                            {
                                                ViewBag.SuccessMsgAdditionalContent = Resource.lblNutrientRecommendationsWillBeBasedOnTheLatest;
                                            }
                                            else
                                            {
                                                ViewBag.SuccessMsgAdditionalContent = string.Format(Resource.lblThisMayChangeYourNutrientRecommendations);
                                            }

                                            ViewBag.CropYear = _farmDataProtector.Protect(cropYear.ToString());
                                            if (!string.IsNullOrWhiteSpace(s) && _soilAnalysisDataProtector.Unprotect(s) == Resource.lblAdd)
                                            {
                                                ViewBag.SuccessMsgAdditionalContentSecondForAdd = Resource.lblCheckYourCropPlans;
                                                ViewBag.SuccessMsgAdditionalContentThird = Resource.lblToSeeYourRecommendations;
                                            }
                                            else
                                            {
                                                ViewBag.SuccessMsgAdditionalContentSecondForUpdate = string.Format(Resource.lblCropPlan);
                                                ViewBag.SuccessMsgAdditionalContentThird = Resource.lblToSeeItsRecommendations;
                                            }

                                        }
                                    }
                                    else if (!string.IsNullOrWhiteSpace(s) && (_soilAnalysisDataProtector.Unprotect(s) == Resource.lblUpdate || _soilAnalysisDataProtector.Unprotect(s) == Resource.lblRemove))
                                    {
                                        ViewBag.SuccessMsgAdditionalContent = string.Format(Resource.lblThisMayChangeYourNutrientRecommendations);
                                        ViewBag.SuccessMsgAdditionalContentSecondForUpdate = string.Format(Resource.lblCropPlan);
                                        ViewBag.SuccessMsgAdditionalContentThird = Resource.lblToSeeItsRecommendations;
                                    }

                                }
                            }
                        }

                    }
                }

            }
            else
            {
                ViewBag.Success = null;
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);

            return View(model);
        }




        [HttpGet]
        public async Task<IActionResult> RecentSoilAnalysisQuestion()
        {
            _logger.LogTrace($"Field Controller : RecentSoilAnalysisQuestion() action called");
            FieldViewModel model = new FieldViewModel();

            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in RecentSoilAnalysisQuestion() action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = ex.Message;
                return RedirectToAction("SoilType");
            }
            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecentSoilAnalysisQuestion(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : RecentSoilAnalysisQuestion() post action called");
            if (model.RecentSoilAnalysisQuestion == null)
            {
                ModelState.AddModelError("RecentSoilAnalysisQuestion", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            try
            {
                if (model.IsCheckAnswer)
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                    {
                        FieldViewModel fieldData = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                        if (fieldData.RecentSoilAnalysisQuestion != model.RecentSoilAnalysisQuestion)
                        {
                            model.IsRecentSoilAnalysisQuestionChange = true;
                        }
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                }
                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", model);

                if (model.RecentSoilAnalysisQuestion.Value)
                {

                    return RedirectToAction("SulphurDeficient");
                }
                else
                {
                    model.SoilAnalyses.SulphurDeficient = null;
                    model.SoilAnalyses.Date = null;
                    model.SoilAnalyses.PH = null;
                    model.SoilAnalyses.Phosphorus = null;
                    model.SoilAnalyses.Magnesium = null;
                    model.SoilAnalyses.Potassium = null;
                    model.SoilAnalyses.PotassiumIndex = null;
                    model.SoilAnalyses.MagnesiumIndex = null;
                    model.SoilAnalyses.PhosphorusIndex = null;
                    model.IsSoilNutrientValueTypeIndex = null;
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", model);
                    if (model.IsCheckAnswer)
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                    return RedirectToAction("HasGrassInLastThreeYear");
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in RecentSoilAnalysisQuestion() post action : {ex.Message}, {ex.StackTrace}");
                TempData["Error"] = ex.Message;
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult SoilOverChalk()
        {
            _logger.LogTrace($"Field Controller : SoilOverChalk() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoilOverChalk(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : SoilOverChalk() post action called");
            if (field.SoilOverChalk == null)
            {
                ModelState.AddModelError("SoilOverChalk", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(field);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer && (!field.IsRecentSoilAnalysisQuestionChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(field.EncryptedIsUpdate))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("RecentSoilAnalysisQuestion");
        }



        [HttpGet]
        public async Task<IActionResult> UpdateField(string? id, string? farmId)
        {
            _logger.LogTrace($"Field Controller : UpdateField() action called");
            FieldViewModel model = new FieldViewModel();

            try
            {
                List<CommonResponse> grassManagements = await _fieldService.GetGrassManagementOptions();
                List<CommonResponse> soilNitrogenSupplyItems = await _fieldService.GetSoilNitrogenSupplyItems();

                if (!string.IsNullOrWhiteSpace(id))
                {
                    (Farm farm, Error error) = await _farmService.FetchFarmByIdAsync(Convert.ToInt32(_farmDataProtector.Unprotect(farmId)));
                    int fieldId = Convert.ToInt32(_fieldDataProtector.Unprotect(id));
                    var field = await _fieldService.FetchFieldByFieldId(fieldId);


                    model.LastHarvestYear = farm.LastHarvestYear;
                    (List<PreviousCropping> prevCroppings, error) = await _previousCroppingService.FetchDataByFieldId(fieldId);

                    if (string.IsNullOrWhiteSpace(error.Message))
                    {
                        List<int> previousYears = new List<int>();
                        List<Crop> cropPlans = await _cropService.FetchCropsByFieldId(fieldId);
                        ViewBag.isPlanCreatedForLastHarvestYear = cropPlans.Any(cp => cp.Year == model.LastHarvestYear);
                        model.PreviousCroppingsList = prevCroppings;
                        //List<PreviousCropping> previousCroppingsExcludePlan = previousCroppings.Where(pc => !cropPlans.Any(cp => cp.Year == pc.HarvestYear)).ToList();
                        List<PreviousCropping> grassCroppings = prevCroppings.Where(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                        foreach (var item in grassCroppings)
                        {
                            previousYears.Add(item.HarvestYear ?? 0);
                        }

                        model.PreviousGrassYears = previousYears;

                        bool? hasGrassInLastThreeYear = null;
                        if (prevCroppings.All(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass))
                        {
                            model.IsPreviousYearGrass = false;
                            hasGrassInLastThreeYear = false;
                        }
                        else
                        {
                            model.IsPreviousYearGrass = prevCroppings.FirstOrDefault().CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass ? true : false;
                            hasGrassInLastThreeYear = true;
                            model.PreviousCroppings = prevCroppings.FirstOrDefault(x=>x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass);
                        }
                        model.CropGroupId = prevCroppings.FirstOrDefault(x=>x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)?.CropGroupID;
                        model.CropTypeID = prevCroppings.FirstOrDefault(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)?.CropTypeID;
                        if (model.CropGroupId != null && model.CropTypeID != null)
                        {
                            model.CropGroup = await _fieldService.FetchCropGroupById(model.CropGroupId.Value);
                            model.CropType = await _fieldService.FetchCropTypeById(model.CropTypeID.Value);
                        }
                        ViewBag.HasGrassInLastThreeYear = hasGrassInLastThreeYear;

                        if (hasGrassInLastThreeYear == true)
                        {
                            ViewBag.GrassManagementOption = grassManagements?.FirstOrDefault(x => x.Id == prevCroppings
                              .Where(pc => pc.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass)
                              .Select(pc => pc.GrassManagementOptionID)
                              .FirstOrDefault())?.Name;

                      ViewBag.SoilNitrogenSupplyItem =soilNitrogenSupplyItems?.FirstOrDefault(x => 
                            x.Id == prevCroppings
                              .Where(pc => pc.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass)
                              .Select(pc => pc.SoilNitrogenSupplyItemID)
                              .FirstOrDefault())?.Name;
                        }
                        
                    }
                    model.Name = field.Name;
                    model.TotalArea = field.TotalArea ?? 0;
                    model.CroppedArea = field.CroppedArea ?? 0;
                    model.ManureNonSpreadingArea = field.ManureNonSpreadingArea ?? 0;
                    model.SoilReleasingClay = field.SoilReleasingClay ?? false;
                    model.IsWithinNVZ = field.IsWithinNVZ ?? false;
                    model.IsAbove300SeaLevel = field.IsAbove300SeaLevel ?? false;
                    var soilType = await _fieldService.FetchSoilTypeById(field.SoilTypeID.Value);
                    model.SoilType = !string.IsNullOrWhiteSpace(soilType) ? soilType : string.Empty;
                    model.SoilTypeID = field.SoilTypeID;
                    model.EncryptedFieldId = id;
                    model.ID = fieldId;
                    model.isEnglishRules = farm.EnglishRules;
                    model.SoilOverChalk = field.SoilOverChalk;

                    model.EncryptedFarmId = farmId;
                    model.FarmName = farm.Name;
                    //model.FarmID = Convert.ToInt32(_farmDataProtector.Unprotect(farmId));
                    if (farm != null)
                    {
                        model.IsWithinNVZForFarm = farm.NVZFields == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
                        model.IsAbove300SeaLevelForFarm = farm.FieldsAbove300SeaLevel == (int)NMP.Portal.Enums.NVZFields.SomeFieldsInNVZ ? true : false;
                    }
                    else
                    {
                        model.IsWithinNVZForFarm = false;
                        model.IsAbove300SeaLevelForFarm = false;
                    }
                    bool isUpdateField = true;
                    model.EncryptedIsUpdate = _fieldDataProtector.Protect(isUpdateField.ToString());
                    if (model.SoilOverChalk != null && model.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.Shallow)
                    {
                        model.SoilOverChalk = null;
                    }
                    if (model.SoilReleasingClay != null && model.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.DeepClayey)
                    {
                        model.SoilReleasingClay = null;
                        model.IsSoilReleasingClay = false;
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
                }
                else
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                    {
                        model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if (model != null)
                    {
                        if(model.PreviousCroppings.GrassManagementOptionID != null)
                        {
                            ViewBag.GrassManagementOption = grassManagements?.FirstOrDefault(x => x.Id == model.PreviousCroppings.GrassManagementOptionID)?.Name;

                        }
                        if(model.PreviousCroppings.SoilNitrogenSupplyItemID != null)
                        {
                            ViewBag.SoilNitrogenSupplyItem = soilNitrogenSupplyItems?.FirstOrDefault(x => x.Id == model.PreviousCroppings.SoilNitrogenSupplyItemID)?.Name;
                        }


                        if (model.PreviousCroppingsList != null && model.PreviousGrassYears != null)
                        {
                            // Sort descending by HarvestYear to get the top-most (latest year)
                            model.PreviousCroppingsList = model.PreviousCroppingsList
                                .OrderByDescending(pc => pc.HarvestYear)
                                .ToList();

                            var topMost = model.PreviousCroppingsList.FirstOrDefault();
                            int topYear = topMost?.HarvestYear ?? 0;

                            // Keep only records whose year is in PreviousGrassYears OR top-most year
                            model.PreviousCroppingsList = model.PreviousCroppingsList
                                .Where(pc => pc.HarvestYear == topYear || model.PreviousGrassYears.Contains(pc.HarvestYear ?? 0))
                                .ToList();

                            // Update existing records
                            foreach (var pc in model.PreviousCroppingsList)
                            {
                                // Grass or arable logic
                                if (model.PreviousGrassYears.Contains(pc.HarvestYear ?? 0))
                                {
                                    pc.FieldID = model.PreviousCroppings.FieldID;
                                    pc.CropGroupID = model.PreviousCroppings.CropGroupID;
                                    pc.CropTypeID = model.PreviousCroppings.CropTypeID;
                                    pc.HasGrassInLastThreeYear = model.PreviousCroppings.HasGrassInLastThreeYear;

                                    pc.LayDuration = model.PreviousCroppings.LayDuration;
                                    pc.GrassManagementOptionID = model.PreviousCroppings.GrassManagementOptionID;
                                    pc.HasGreaterThan30PercentClover = model.PreviousCroppings.HasGreaterThan30PercentClover;
                                    pc.SoilNitrogenSupplyItemID = model.PreviousCroppings.SoilNitrogenSupplyItemID;
                                }
                                else if (pc.HarvestYear == topYear)
                                {
                                    pc.FieldID = model.PreviousCroppings.FieldID;
                                    pc.CropGroupID = model.CropGroupId;  //arable
                                    pc.CropTypeID = model.CropTypeID;
                                    pc.HasGrassInLastThreeYear = model.PreviousCroppings.HasGrassInLastThreeYear;

                                    pc.LayDuration = null;
                                    pc.GrassManagementOptionID = null;
                                    pc.HasGreaterThan30PercentClover = null;
                                    pc.SoilNitrogenSupplyItemID = null;
                                }
                            }

                            // Add missing years from PreviousGrassYears
                            var existingYears = model.PreviousCroppingsList
                                .Select(pc => pc.HarvestYear ?? 0)
                                .ToHashSet();

                            var missingYears = model.PreviousGrassYears
                                .Where(y => !existingYears.Contains(y))
                                .ToList();

                            foreach (var year in missingYears)
                            {
                                model.PreviousCroppingsList.Add(new PreviousCropping
                                {
                                    FieldID = model.PreviousCroppings.FieldID,
                                    HarvestYear = year,
                                    CropGroupID = model.PreviousCroppings.CropGroupID,
                                    CropTypeID = model.PreviousCroppings.CropTypeID,
                                    HasGrassInLastThreeYear = true,
                                    LayDuration = model.PreviousCroppings.LayDuration,
                                    GrassManagementOptionID = model.PreviousCroppings.GrassManagementOptionID,
                                    HasGreaterThan30PercentClover = model.PreviousCroppings.HasGreaterThan30PercentClover,
                                    SoilNitrogenSupplyItemID = model.PreviousCroppings.SoilNitrogenSupplyItemID
                                });
                            }
                        }

                        if (model.SoilOverChalk != null && model.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.Shallow)
                        {
                            model.SoilOverChalk = null;
                        }
                        if (model.SoilReleasingClay != null && model.SoilTypeID != (int)NMP.Portal.Enums.SoilTypeEngland.DeepClayey)
                        {
                            model.SoilReleasingClay = null;
                            model.IsSoilReleasingClay = false;
                        }
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
                    }
                }

            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return View(model);
            }
            return View(model);

        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateField(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : UpdateField() post action called");
            try
            {
                int userId = Convert.ToInt32(HttpContext.User.FindFirst("UserId")?.Value);
                FieldData fieldData = new FieldData
                {
                    Field = new Field
                    {
                        SoilTypeID = model.SoilTypeID,
                        NVZProgrammeID = model.IsWithinNVZ == true ? (int)NMP.Portal.Enums.NVZProgram.CurrentNVZRule : (int)NMP.Portal.Enums.NVZProgram.NotInNVZ,
                        Name = model.Name,
                        LPIDNumber = model.LPIDNumber,
                        NationalGridReference = model.NationalGridReference,
                        OtherReference = model.OtherReference,
                        TotalArea = model.TotalArea,
                        CroppedArea = model.CroppedArea,
                        ManureNonSpreadingArea = model.ManureNonSpreadingArea,
                        SoilReleasingClay = model.SoilReleasingClay,
                        SoilOverChalk = model.SoilOverChalk,
                        IsWithinNVZ = model.IsWithinNVZ,
                        IsAbove300SeaLevel = model.IsAbove300SeaLevel,
                        IsActive = true,
                        CreatedOn = model.CreatedOn,
                        CreatedByID = model.CreatedByID,
                        ModifiedOn = DateTime.Now,
                        ModifiedByID = userId
                    },
                    PreviousCroppings=model.PreviousCroppingsList,
                };
                int fieldId = Convert.ToInt32(_fieldDataProtector.Unprotect(model.EncryptedFieldId));
                (Field fieldResponse, Error error1) = await _fieldService.UpdateFieldAsync(fieldData, fieldId);
                if (error1.Message == null && fieldResponse != null)
                {
                    string success = _farmDataProtector.Protect(Resource.lblTrue);
                    string fieldName = _farmDataProtector.Protect(fieldResponse.Name);
                    _httpContextAccessor.HttpContext?.Session.Remove("FieldData");

                    return RedirectToAction("FieldSoilAnalysisDetail", new { id = model.EncryptedFieldId, farmId = model.EncryptedFarmId, q = success, r = _fieldDataProtector.Protect(Resource.lblField) });
                }
                else
                {
                    TempData["UpdateFieldError"] = Resource.MsgWeCouldNotAddYourFieldPleaseTryAgainLater;
                    return RedirectToAction("UpdateField");
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("UpdateField");
            }


        }

        [HttpGet]
        public async Task<IActionResult> FieldRemove()
        {
            _logger.LogTrace($"Field Controller : FieldRemove() action called");
            FieldViewModel? model = new FieldViewModel();
            if (HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = HttpContext.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("ManageFarmFields", "Farm");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FieldRemove(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : FieldRemove() post action called");
            if (field.FieldRemove == null)
            {
                ModelState.AddModelError("FieldRemove", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View("FieldRemove", field);
            }
            if (!field.FieldRemove.Value)
            {
                return RedirectToAction("FieldSoilAnalysisDetail", new { id = field.EncryptedFieldId, farmId = field.EncryptedFarmId });
            }
            else
            {
                int fieldId = Convert.ToInt32(_fieldDataProtector.Unprotect(field.EncryptedFieldId));
                (string message, Error error) = await _fieldService.DeleteFieldByIdAsync(fieldId);
                if (!string.IsNullOrWhiteSpace(error.Message))
                {
                    ViewBag.DeleteFieldError = error.Message;
                    return View(field);
                }
                if (!string.IsNullOrWhiteSpace(message))
                {
                    string isDeleted = _fieldDataProtector.Protect("true");
                    string name = _fieldDataProtector.Protect(field.Name);
                    //int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(field.EncryptedFarmId));
                    HttpContext.Session.Remove("FieldData");

                    return RedirectToAction("ManageFarmFields", new { id = field.EncryptedFarmId, name = name, isDeleted = isDeleted });
                }
            }
            return View(field);

        }

        [HttpGet]
        public IActionResult CopyExistingField(string q)
        {
            _logger.LogTrace($"Field Controller : CopyExistingField() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else if (string.IsNullOrWhiteSpace(q))
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (!string.IsNullOrEmpty(q))
            {
                model.FarmID = Convert.ToInt32(_farmDataProtector.Unprotect(q));
                model.EncryptedFarmId = q;
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CopyExistingField(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : CopyExistingField() post action called");
            if (field.CopyExistingField == null)
            {
                ModelState.AddModelError("CopyExistingField", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(field);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            if (field.CopyExistingField != null && !(field.CopyExistingField.Value))
            {
                return RedirectToAction("AddField", new { q = field.EncryptedFarmId });
            }
            return RedirectToAction("CopyFields");
        }
        [HttpGet]
        public async Task<IActionResult> CopyFields()
        {
            _logger.LogTrace($"Field Controller : CopyFields() action called");
            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            (Error error, List<Field> fieldList) = await _fieldService.FetchFieldByFarmId(model.FarmID, Resource.lblTrue);
            if (string.IsNullOrWhiteSpace(error.Message))
            {
                ViewBag.FieldList = fieldList;
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyFields(FieldViewModel field)
        {
            _logger.LogTrace($"Field Controller : CopyFields() post action called");
            if (field.ID == null)
            {
                ModelState.AddModelError("ID", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                (Error error, List<Field> fieldList) = await _fieldService.FetchFieldByFarmId(field.FarmID, Resource.lblTrue);
                if (string.IsNullOrWhiteSpace(error.Message))
                {
                    ViewBag.FieldList = fieldList;
                }
                return View("CopyFields", field);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", field);
            if (field.IsCheckAnswer)
            {
                return RedirectToAction("CheckAnswer");
            }
            return RedirectToAction("AddField", new { q = field.EncryptedFarmId });
        }
        //Grass journey

        [HttpGet]
        public async Task<IActionResult> HasGrassInLastThreeYear()
        {
            _logger.LogTrace($"Field Controller : HasGrassInLastThreeYear() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult HasGrassInLastThreeYear(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : HasGrassInLastThreeYear() post action called");
            if (model.PreviousCroppings.HasGrassInLastThreeYear == null)
            {
                ModelState.AddModelError("PreviousCroppings.HasGrassInLastThreeYear", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.IsCheckAnswer)
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
                {
                    FieldViewModel fieldData = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                    if (fieldData.PreviousCroppings != null &&
                        model.PreviousCroppings != null &&
                        fieldData.PreviousCroppings.HasGrassInLastThreeYear != model.PreviousCroppings.HasGrassInLastThreeYear)
                    {
                        model.IsHasGrassInLastThreeYearChange = true;
                        if ((model.PreviousCroppings.HasGrassInLastThreeYear != null && (!model.PreviousCroppings.HasGrassInLastThreeYear.Value)))
                        {
                            model.CropGroupId = null;
                            model.CropGroup = string.Empty;
                            model.CropTypeID = null;
                            model.CropType = string.Empty;
                            model.PreviousCroppings.HarvestYear = null;
                            model.PreviousCroppings.GrassManagementOptionID = null;
                            model.PreviousCroppings.HasGreaterThan30PercentClover = null;
                            model.PreviousCroppings.SoilNitrogenSupplyItemID = null;
                            model.PreviousGrassYears = null;
                            model.IsPreviousYearGrass = null;
                            _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", model);
                            return RedirectToAction("CropGroups");
                        }
                        else
                        {
                            if (model.PreviousCroppings.HasGrassInLastThreeYear.Value)
                            {
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
                                return RedirectToAction("GrassLastThreeHarvestYear");
                            }
                        }
                    }
                    else
                    {
                        model.IsHasGrassInLastThreeYearChange = false;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
                        return RedirectToAction("CheckAnswer");
                    }
                }
                //if ((model.PreviousCroppings.HasGrassInLastThreeYear != null && (!model.PreviousCroppings.HasGrassInLastThreeYear.Value)))
                //{
                //    model.CropGroupId = null;
                //    model.CropGroup = string.Empty;
                //    model.CropTypeID = null;
                //    model.CropType = string.Empty;
                //    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", model);
                //    return RedirectToAction("CropGroups");
                //}
                //return RedirectToAction("CheckAnswer");
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.PreviousCroppings.HasGrassInLastThreeYear.Value)
            {

                return RedirectToAction("GrassLastThreeHarvestYear");
            }
            else
            {
                model.CropGroupId = null;
                model.CropGroup = string.Empty;
                model.CropTypeID = null;
                model.CropType = string.Empty;
                model.PreviousCroppings.HarvestYear = null;
                model.PreviousCroppings.GrassManagementOptionID = null;
                model.PreviousCroppings.HasGreaterThan30PercentClover = null;
                model.PreviousCroppings.SoilNitrogenSupplyItemID = null;
                model.PreviousGrassYears = null;
                model.IsPreviousYearGrass = null;
                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FieldData", model);
                if (model.IsCheckAnswer)
                {
                    return RedirectToAction("CheckAnswer");
                }
                if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate))
                {
                    return RedirectToAction("UpdateField");
                }
                return RedirectToAction("CropGroups");
            }

        }

        [HttpGet]
        public async Task<IActionResult> GrassLastThreeHarvestYear()
        {
            _logger.LogTrace($"Field Controller : GrassLastThreeHarvestYear() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            List<int> previousYears = new List<int>();
            int lastHarvestYear = model.LastHarvestYear ?? 0;
            previousYears.Add(lastHarvestYear);
            previousYears.Add(lastHarvestYear - 1);
            previousYears.Add(lastHarvestYear - 2);

            //if(!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate))
            //{
            //    int fieldId = Convert.ToInt32(_fieldDataProtector.Unprotect(model.EncryptedFieldId));
            //    List<Crop> cropPlans = await _cropService.FetchCropsByFieldId(fieldId);

            //    previousYears = previousYears.Where(py => !cropPlans.Any(cp => cp.Year == py)).ToList();
            //}
            
            ViewBag.PreviousCroppingsYear = previousYears;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GrassLastThreeHarvestYear(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : GrassLastThreeHarvestYear() post action called");
            int lastHarvestYear = 0;
            if (model.PreviousGrassYears == null)
            {
                ModelState.AddModelError("PreviousGrassYears", Resource.lblSelectAtLeastOneYearBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                List<int> previousYears = new List<int>();
                lastHarvestYear = model.LastHarvestYear ?? 0;
                previousYears.Add(lastHarvestYear);
                previousYears.Add(lastHarvestYear - 1);
                previousYears.Add(lastHarvestYear - 2);
                ViewBag.PreviousCroppingsYear = previousYears;
                return View(model);
            }
            //below condition is for select all
            if (model.PreviousGrassYears?.Count == 1 && model.PreviousGrassYears[0] == 0)
            {
                List<int> previousYears = new List<int>();
                lastHarvestYear = model.LastHarvestYear ?? 0;
                previousYears.Add(lastHarvestYear);
                previousYears.Add(lastHarvestYear - 1);
                previousYears.Add(lastHarvestYear - 2);
                model.PreviousGrassYears = previousYears;
            }
            lastHarvestYear = model.LastHarvestYear ?? 0;
            model.IsPreviousYearGrass = (model.PreviousGrassYears != null && model.PreviousGrassYears.Contains(lastHarvestYear)) ? true : false;

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);

            if (model.PreviousGrassYears?.Count == 3)
            {
                model.PreviousCroppings.LayDuration = (int)NMP.Portal.Enums.LayDuration.ThreeYearsOrMore;
            }
            else if (model.PreviousGrassYears?.Count <= 2 && model.PreviousGrassYears[0] == model.LastHarvestYear)
            {
                model.PreviousCroppings.LayDuration = (int)NMP.Portal.Enums.LayDuration.OneToTwoYears;
            }
            else
            {
                return RedirectToAction("LayDuration");
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.IsCheckAnswer && (!model.IsHasGrassInLastThreeYearChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            return RedirectToAction("GrassManagementOptions");
        }

        [HttpGet]
        public async Task<IActionResult> GrassManagementOptions()
        {
            _logger.LogTrace($"Field Controller : GrassManagementOptions() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            List<CommonResponse> commonResponses = await _fieldService.GetGrassManagementOptions();
            ViewBag.GrassManagementOptions = commonResponses;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GrassManagementOptions(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : GrassManagementOptions() post action called");

            if (model.PreviousCroppings.GrassManagementOptionID == null)
            {
                ModelState.AddModelError("PreviousCroppings.GrassManagementOptionID", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                List<CommonResponse> commonResponses = await _fieldService.GetGrassManagementOptions();
                ViewBag.GrassManagementOptions = commonResponses;
                return View(model);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.PreviousCroppings.GrassManagementOptionID == (int)NMP.Portal.Enums.GrassManagementOption.GrazedOnly)
            {
                return RedirectToAction("HasGreaterThan30PercentClover");
            }
            if (model.IsCheckAnswer && (!model.IsHasGrassInLastThreeYearChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate) && (!model.IsHasGrassInLastThreeYearChange) && model.IsPreviousYearGrass == true)
            {
                return RedirectToAction("UpdateField");
            }

            return RedirectToAction("HasGreaterThan30PercentClover");
        }



        [HttpGet]
        public async Task<IActionResult> HasGreaterThan30PercentClover()
        {
            _logger.LogTrace($"Field Controller : HasGreaterThan30PercentClover() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult HasGreaterThan30PercentClover(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : HasGreaterThan30PercentClover() post action called");
            if (model.PreviousCroppings.HasGreaterThan30PercentClover == null)
            {
                ModelState.AddModelError("PreviousCroppings.HasGreaterThan30PercentClover", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.IsCheckAnswer && (!model.IsHasGrassInLastThreeYearChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            

            if (model.PreviousCroppings.HasGreaterThan30PercentClover.Value)
            {
                model.PreviousCroppings.SoilNitrogenSupplyItemID = null;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
                if (model.IsPreviousYearGrass == false)
                {
                    return RedirectToAction("CropGroups");
                }

                if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate) && (!model.IsHasGrassInLastThreeYearChange))
                {
                    return RedirectToAction("UpdateField");
                }
                return RedirectToAction("CheckAnswer");
            }
            else
            {
                return RedirectToAction("SoilNitrogenSupplyItems");
            }

        }

        [HttpGet]
        public async Task<IActionResult> SoilNitrogenSupplyItems()
        {
            _logger.LogTrace($"Field Controller : SoilNitrogenSupplyItems() action called");
            Error error = new Error();

            FieldViewModel model = new FieldViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            List<CommonResponse> commonResponses = await _fieldService.GetSoilNitrogenSupplyItems();
            ViewBag.SoilNitrogenSupplyItems = commonResponses.OrderBy(x => x.Id);
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoilNitrogenSupplyItems(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : SoilNitrogenSupplyItems() post action called");

            if (model.PreviousCroppings.SoilNitrogenSupplyItemID == null)
            {
                ModelState.AddModelError("PreviousCroppings.SoilNitrogenSupplyItemID", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                List<CommonResponse> commonResponses = await _fieldService.GetSoilNitrogenSupplyItems();
                ViewBag.SoilNitrogenSupplyItems = commonResponses.OrderBy(x => x.Id);
                return View(model);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.IsCheckAnswer && (!model.IsHasGrassInLastThreeYearChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            if (model.IsPreviousYearGrass == false)
            {
                if(model.CropGroupId==null)
                {
                    return RedirectToAction("CropGroups");
                }
            }
            if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate) && (!model.IsHasGrassInLastThreeYearChange))
            {
                return RedirectToAction("UpdateField");
            }
            return RedirectToAction("CheckAnswer");
        }

        [HttpGet]
        public async Task<IActionResult> LayDuration()
        {
            _logger.LogTrace($"Field Controller : LayDuration() action called");
            FieldViewModel model = new FieldViewModel();

            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FieldData"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FieldViewModel>("FieldData");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LayDuration(FieldViewModel model)
        {
            _logger.LogTrace($"Field Controller : LayDuration() post action called");

            if (model.PreviousCroppings.LayDuration == null)
            {
                ModelState.AddModelError("PreviousCroppings.LayDuration", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FieldData", model);
            if (model.IsCheckAnswer && (!model.IsHasGrassInLastThreeYearChange))
            {
                return RedirectToAction("CheckAnswer");
            }
            if (!string.IsNullOrWhiteSpace(model.EncryptedIsUpdate) && (!model.IsHasGrassInLastThreeYearChange) && model.IsPreviousYearGrass==true)
            {
                return RedirectToAction("UpdateField");
            }

            return RedirectToAction("GrassManagementOptions");
        }

        [HttpGet]
        public IActionResult Cancel()
        {
            _logger.LogTrace("Field Controller : Cancel() action called");
            FieldViewModel model = new FieldViewModel();
            try
            {
                if (HttpContext.Session.Keys.Contains("FieldData"))
                {
                    model = HttpContext.Session.GetObjectFromJson<FieldViewModel>("FieldData");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Field Controller : Exception in Cancel() action : {ex.Message}, {ex.StackTrace}");
                TempData["AddFieldError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(FieldViewModel model)
        {
            _logger.LogTrace("Field Controller : Cancel() post action called");
            if (model.IsCancel == null)
            {
                ModelState.AddModelError("IsCancel", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View("Cancel", model);
            }
            if (!model.IsCancel.Value)
            {
                if (string.IsNullOrWhiteSpace(model.EncryptedFieldId))
                {
                    return RedirectToAction("CheckAnswer");
                }
                else
                {
                    return RedirectToAction("UpdateField");
                }
            }
            else
            {
                //HttpContext?.Session.Remove("FieldData");
                return RedirectToAction("CreateFieldCancel", new { id = model.EncryptedFarmId });
            }

        }
    }
}