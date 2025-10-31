using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Abstractions;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using NMP.Portal.Enums;
using NMP.Portal.Helpers;
using NMP.Portal.Models;
using NMP.Portal.Resources;
using NMP.Portal.ServiceResponses;
using NMP.Portal.Services;
using NMP.Portal.ViewModels;
using System;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;

namespace NMP.Portal.Controllers
{
    [Authorize]
    public class OrganicManureController : Controller
    {
        private readonly ILogger<OrganicManureController> _logger;
        private readonly IDataProtector _farmDataProtector;
        private readonly IDataProtector _fieldDataProtector;
        private readonly IDataProtector _cropDataProtector;
        private readonly IDataProtector _organicManureProtector;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOrganicManureService _organicManureService;
        private readonly IFarmService _farmService;
        private readonly ICropService _cropService;
        private readonly IFieldService _fieldService;
        private readonly IMannerService _mannerService;
        private readonly IFertiliserManureService _fertiliserManureService;

        public OrganicManureController(ILogger<OrganicManureController> logger, IDataProtectionProvider dataProtectionProvider,
              IHttpContextAccessor httpContextAccessor, IOrganicManureService organicManureService, IFarmService farmService, ICropService cropService, IFieldService fieldService, IMannerService mannerService,
              IFertiliserManureService fertiliserManureService)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _farmDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FarmController");
            _fieldDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FieldController");
            _cropDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.CropController");
            _organicManureProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.OrganicManureController");
            _organicManureService = organicManureService;
            _farmService = farmService;
            _cropService = cropService;
            _fieldService = fieldService;
            _mannerService = mannerService;
            _fertiliserManureService = fertiliserManureService;
        }

        public IActionResult Index()
        {
            _logger.LogTrace($"Organic Manure Controller : Index() action called");
            return View();
        }
        public IActionResult CreateManureCancel(string q, string r)
        {
            _logger.LogTrace($"Organic Manure Controller : CreateManureCancel({q}, {r}) action called");
            _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
            return RedirectToAction("HarvestYearOverview", "Crop", new { Id = q, year = r });
        }

        [HttpGet]
        public async Task<IActionResult> FieldGroup(string q, string r, string? s)//q=FarmId,r=harvestYear,s=fieldId
        {
            _logger.LogTrace($"Organic Manure Controller : FieldGroup({q}, {r}, {s}) action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(r))
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if ((!string.IsNullOrWhiteSpace(q)) && (!string.IsNullOrWhiteSpace(r)))
                {
                    model.FarmId = Convert.ToInt32(_farmDataProtector.Unprotect(q));
                    model.HarvestYear = Convert.ToInt32(_farmDataProtector.Unprotect(r));
                    model.EncryptedFarmId = q;
                    model.EncryptedHarvestYear = r;
                    (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                    if (error.Message == null)
                    {
                        model.FarmName = farm.Name;
                        model.isEnglishRules = farm.EnglishRules;
                        model.FarmCountryId = farm.CountryID;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    }
                    else
                    {
                        TempData["FieldGroupError"] = error.Message;
                    }
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        model.FieldList = new List<string>();
                        model.FieldGroup = Resource.lblSelectSpecificFields;
                        model.FieldGroupName = Resource.lblSelectSpecificFields;
                        model.IsComingFromRecommendation = true;
                        string fieldId = _fieldDataProtector.Unprotect(s);
                        model.FieldList.Add(fieldId);
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                        if (cropList != null && cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();

                            //cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == Convert.ToInt32(model.FieldGroup)).ToList();
                            if (cropList.Count > 0)
                            {
                                model.CropOrder = Convert.ToInt32(cropList.Select(x => x.CropOrder).FirstOrDefault());
                            }

                            if (cropList.Count > 1)
                            {
                                model.IsDoubleCropAvailable = true;
                                int counter = 0;
                                model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name;
                                model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                            }
                            else
                            {
                                model.IsDoubleCropAvailable = false;
                            }
                        }

                        //(List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, model.CropOrder);

                        (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);
                        if (error == null)
                        {
                            if (managementIds.Count > 0)
                            {
                                if (model.OrganicManures == null)
                                {
                                    model.OrganicManures = new List<OrganicManure>();
                                }
                                if (model.OrganicManures.Count > 0)
                                {
                                    model.OrganicManures.Clear();
                                }
                                foreach (var manIds in managementIds)
                                {
                                    var organicManure = new OrganicManure
                                    {
                                        ManagementPeriodID = manIds,
                                        FieldID = Convert.ToInt32(fieldId),
                                        FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name
                                    };
                                    model.OrganicManures.Add(organicManure);
                                }
                            }
                        }
                        else
                        {
                            TempData["NutrientRecommendationsError"] = error.Message;
                            return RedirectToAction("Recommendations", "Crop", new { q = q, r = s, s = r });
                        }
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("ManureGroup");
                    }


                }
                (List<ManureCropTypeResponse> cropTypeList, error) = await _organicManureService.FetchCropTypeByFarmIdAndHarvestYear(model.FarmId.Value, model.HarvestYear.Value);
                cropTypeList = cropTypeList.DistinctBy(x => x.CropTypeId).ToList();
                if (error == null)
                {
                    var SelectListItem = cropTypeList.Select(f => new SelectListItem
                    {
                        Value = f.CropTypeId.ToString(),
                        Text = string.Format(Resource.lblTheCropTypeField, f.CropType.ToString())
                    }).ToList();
                    SelectListItem.Insert(0, new SelectListItem { Value = Resource.lblAll, Text = string.Format(Resource.lblAllFieldsInTheYearPlan, model.HarvestYear) });
                    SelectListItem.Add(new SelectListItem { Value = Resource.lblSelectSpecificFields, Text = Resource.lblSelectSpecificFields });
                    ViewBag.FieldGroupList = SelectListItem;
                }
                else
                {
                    TempData["FieldGroupError"] = error.Message;
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in FieldGroup() action : {ex.Message}, {ex.StackTrace}");
                TempData["FieldGroupError"] = ex.Message;
            }

            if (model.IsCheckAnswer && (string.IsNullOrWhiteSpace(s)))
            {
                model.IsFieldGroupChange = true;
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return View("Views/OrganicManure/FieldGroup.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FieldGroup(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : FieldGroup() post action called");
            Error error = null;
            if (model.FieldGroup == null)
            {
                ModelState.AddModelError("FieldGroup", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            try
            {
                (List<ManureCropTypeResponse> cropTypeList, error) = await _organicManureService.FetchCropTypeByFarmIdAndHarvestYear(model.FarmId.Value, model.HarvestYear.Value);
                if (!ModelState.IsValid)
                {
                    if (error == null)
                    {
                        cropTypeList = cropTypeList.DistinctBy(x => x.CropTypeId).ToList();
                        if (cropTypeList.Count > 0)
                        {

                            var SelectListItem = cropTypeList.Select(f => new SelectListItem
                            {
                                Value = f.CropTypeId.ToString(),
                                Text = string.Format(Resource.lblTheCropTypeField, f.CropType.ToString())
                            }).ToList();
                            SelectListItem.Insert(0, new SelectListItem { Value = Resource.lblAll, Text = string.Format(Resource.lblAllFieldsInTheYearPlan, model.HarvestYear) });
                            SelectListItem.Add(new SelectListItem { Value = Resource.lblSelectSpecificFields, Text = Resource.lblSelectSpecificFields });
                            ViewBag.FieldGroupList = SelectListItem;
                        }
                    }
                    else
                    {
                        TempData["FieldGroupError"] = error.Message;
                    }
                    return View("Views/OrganicManure/FieldGroup.cshtml", model);
                }

                int cropTypeId = 0;
                if (cropTypeList.Count > 0)
                {
                    if (int.TryParse(model.FieldGroup, out int fieldGroup))
                    {
                        List<string> cropOrderList = cropTypeList.Where(x => x.CropTypeId == fieldGroup).Select(x => x.CropOrder).ToList();
                        if (cropOrderList.Count == 1)
                        {
                            model.CropOrder = Convert.ToInt32(cropOrderList.FirstOrDefault());
                        }
                        else
                        {
                            model.CropOrder = 1;
                        }
                        //string[] parts = model.FieldGroup.Split('-');
                        //model.FieldGroup = parts[0];
                        //model.CropOrder = int.Parse(parts[1]);
                    }
                }
                if (int.TryParse(model.FieldGroup, out int value))
                {
                    model.CropTypeName = cropTypeList.FirstOrDefault(x => x.CropTypeId == value).CropType;
                    model.FieldGroupName = string.Format(Resource.lblTheCropTypeField, model.CropTypeName);
                }
                else
                {
                    if (model.FieldGroup.Equals(Resource.lblAll))
                    {
                        model.FieldGroupName = string.Format(Resource.lblAllFieldsInTheYearPlan, model.HarvestYear);
                    }
                    else
                    {
                        model.FieldGroupName = model.FieldGroup;
                    }
                }
                model.IsComingFromRecommendation = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in FieldGroup() post action : {ex.Message}, {ex.StackTrace}");
                TempData["FieldGroupError"] = ex.Message;
                return View("Views/OrganicManure/FieldGroup.cshtml", model);
            }
            return RedirectToAction("Fields");

        }

        [HttpGet]
        public async Task<IActionResult> Fields()
        {
            _logger.LogTrace($"Organic Manure Controller : Fields() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                {
                    (List<CommonResponse> fieldList, error) = await _organicManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
                    if (error == null)
                    {
                        if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields))
                        {
                            if (fieldList.Count > 0)
                            {

                                var SelectListItem = fieldList.Select(f => new SelectListItem
                                {
                                    Value = f.Id.ToString(),
                                    Text = f.Name.ToString()
                                }).ToList();
                                ViewBag.FieldList = SelectListItem.OrderBy(x => x.Text).ToList();
                            }
                            return View(model);
                        }
                        else
                        {
                            if (fieldList.Count > 0)
                            {
                                model.FieldList = fieldList.Select(x => x.Id.ToString()).ToList();
                                string fieldIds = string.Join(",", model.FieldList);
                                if (model.OrganicManures == null)
                                {
                                    model.OrganicManures = new List<OrganicManure>();
                                }
                                if (model.OrganicManures.Count > 0)
                                {
                                    model.OrganicManures.Clear();
                                }
                                model.IsDoubleCropAvailable = false;
                                foreach (string fieldIdForManID in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldIdForManID));
                                    if (cropList != null && cropList.Count > 0)
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                        }
                                        if (cropList != null && cropList.Count == 2)
                                        {
                                            model.IsDoubleCropAvailable = true;
                                            int counter = 0;
                                            model.DoubleCropCurrentCounter = counter;
                                            model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldIdForManID))).Name;
                                            model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                        }
                                        //cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == Convert.ToInt32(model.FieldGroup)).ToList();
                                        if (cropList.Count > 0)
                                        {
                                            model.CropOrder = Convert.ToInt32(cropList.Select(x => x.CropOrder).FirstOrDefault());
                                        }

                                    }

                                    List<int> managementIds = new List<int>();
                                    if (int.TryParse(model.FieldGroup, out int cropTypeIdForFilter))
                                    {
                                        (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, cropTypeIdForFilter.ToString(), null);
                                    }
                                    else
                                    {
                                        (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, model.CropOrder);
                                    }
                                    if (error == null)
                                    {
                                        if (managementIds.Count > 0)
                                        {
                                            foreach (var manIds in managementIds)
                                            {
                                                var organicManure = new OrganicManure
                                                {
                                                    ManagementPeriodID = manIds
                                                };
                                                model.OrganicManures.Add(organicManure);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        TempData["FieldGroupError"] = error.Message;
                                        return View("FieldGroup", model);
                                    }
                                }
                                foreach (var organicManure in model.OrganicManures)
                                {
                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);
                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                    {
                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                        {
                                            organicManure.FieldID = crop.FieldID;
                                            organicManure.FieldName = (await _fieldService.FetchFieldByFieldId(organicManure.FieldID.Value)).Name;
                                        }
                                    }
                                }
                                var fieldIdsAlreadyProcessed = new List<int>();
                                model.OrganicManures.RemoveAll(item =>
                                {
                                    if (fieldIdsAlreadyProcessed.Contains(item.FieldID.Value))
                                    {
                                        return true;
                                    }
                                    else
                                    {
                                        fieldIdsAlreadyProcessed.Add(item.FieldID.Value);
                                        return false;
                                    }
                                });
                                if (model.IsCheckAnswer && model.OrganicManures.Count > 0)
                                {
                                    int i = 0;
                                    model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                                    foreach (var field in model.FieldList)
                                    {
                                        int fieldId = Convert.ToInt32(field);
                                        Field fieldData = await _fieldService.FetchFieldByFieldId(fieldId);
                                        if (fieldData != null)
                                        {
                                            //if (model.AutumnCropNitrogenUptakes.Any(f => f.FieldName == fieldData.Name.ToString()))
                                            //{
                                            //    break;
                                            //}
                                            (CropTypeResponse cropsResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(fieldId, model.HarvestYear.Value, false);
                                            if (cropsResponse != null)
                                            {
                                                (CropTypeLinkingResponse cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(cropsResponse.CropTypeId);

                                                if (error == null && cropTypeLinkingResponse != null)
                                                {
                                                    int mannerCropTypeId = cropTypeLinkingResponse.MannerCropTypeID;

                                                    var uptakeData = new
                                                    {
                                                        cropTypeId = mannerCropTypeId,
                                                        applicationMonth = model.ApplicationDate.Value.Month
                                                    };

                                                    string jsonString = JsonConvert.SerializeObject(uptakeData);
                                                    (NitrogenUptakeResponse nitrogenUptakeResponse, error) = await _organicManureService.FetchAutumnCropNitrogenUptake(jsonString);
                                                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                                                    {
                                                        TempData["FieldGroupError"] = error.Message;
                                                        return View("FieldGroup", model);
                                                    }
                                                    if (nitrogenUptakeResponse != null && error == null)
                                                    {
                                                        if (model.AutumnCropNitrogenUptakes == null)
                                                        {
                                                            model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                                                        }

                                                        model.AutumnCropNitrogenUptakes.Add(new AutumnCropNitrogenUptakeDetail
                                                        {
                                                            EncryptedFieldId = _organicManureProtector.Protect(fieldId.ToString()),
                                                            FieldName = fieldData.Name ?? string.Empty,
                                                            CropTypeId = cropsResponse.CropTypeId,
                                                            CropTypeName = cropsResponse.CropType,
                                                            AutumnCropNitrogenUptake = nitrogenUptakeResponse.value
                                                        });
                                                    }
                                                }
                                                else if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                                                {
                                                    TempData["FieldGroupError"] = error.Message;
                                                    return View("FieldGroup", model);
                                                }
                                            }
                                        }
                                    }
                                    foreach (var organicManure in model.OrganicManures)
                                    {
                                        if (model.ApplicationDate.HasValue)
                                        {
                                            organicManure.ApplicationDate = model.ApplicationDate.Value;
                                        }
                                        if (model.ApplicationMethod.HasValue)
                                        {
                                            organicManure.ApplicationMethodID = model.ApplicationMethod.Value;
                                        }
                                        if (model.ApplicationRate.HasValue)
                                        {
                                            organicManure.ApplicationRate = model.ApplicationRate.Value;
                                        }
                                        if (model.Area.HasValue)
                                        {
                                            organicManure.AreaSpread = model.Area.Value;
                                        }
                                        if (model.Quantity.HasValue)
                                        {
                                            organicManure.ManureQuantity = model.Quantity.Value;
                                        }
                                        organicManure.ManureTypeID = model.ManureTypeId.Value;
                                        organicManure.ManureTypeName = model.OtherMaterialName;
                                        if (model.TotalRainfall.HasValue)
                                        {
                                            organicManure.Rainfall = model.TotalRainfall.Value;
                                        }
                                        //if (string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && (!model.DefaultNutrientValue.Value))
                                        //{
                                        if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblIwantToEnterARecentOrganicMaterialAnalysis)
                                        {
                                            organicManure.DryMatterPercent = model.DryMatterPercent.Value;
                                            organicManure.K2O = model.K2O.Value;
                                            organicManure.MgO = model.MgO.Value;
                                            organicManure.N = model.N.Value;
                                            organicManure.NH4N = model.NH4N.Value;
                                            organicManure.NO3N = model.NO3N.Value;
                                            organicManure.P2O5 = model.P2O5.Value;
                                            organicManure.SO3 = model.SO3.Value;
                                            organicManure.UricAcid = model.UricAcid.Value;
                                        }
                                        else
                                        {
                                            if (model.ManureType != null)
                                            {
                                                organicManure.DryMatterPercent = model.ManureType.DryMatter.Value;
                                                organicManure.K2O = model.ManureType.K2O.Value;
                                                organicManure.MgO = model.ManureType.MgO.Value;
                                                organicManure.N = model.ManureType.TotalN.Value;
                                                organicManure.NH4N = model.ManureType.NH4N.Value;
                                                organicManure.NO3N = model.ManureType.NO3N.Value;
                                                organicManure.P2O5 = model.ManureType.P2O5.Value;
                                                organicManure.SO3 = model.ManureType.SO3.Value;
                                                organicManure.UricAcid = model.ManureType.Uric.Value;
                                            }
                                        }
                                        if (model.IncorporationDelay.HasValue)
                                        {
                                            organicManure.IncorporationDelayID = model.IncorporationDelay.Value;
                                        }
                                        if (model.IncorporationMethod.HasValue)
                                        {
                                            organicManure.IncorporationMethodID = model.IncorporationMethod.Value;
                                        }
                                        if (model.SoilDrainageEndDate.HasValue)
                                        {
                                            organicManure.EndOfDrain = model.SoilDrainageEndDate.Value;
                                        }
                                        //if (model.AutumnCropNitrogenUptake.HasValue)
                                        //{
                                        //    organicManure.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptake.Value;
                                        //}
                                        if (model.AutumnCropNitrogenUptakes != null && model.AutumnCropNitrogenUptakes.Count > 0 && i < model.AutumnCropNitrogenUptakes.Count)
                                        {
                                            organicManure.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes[i].AutumnCropNitrogenUptake;
                                        }

                                        if (model.WindspeedID.HasValue)
                                        {
                                            organicManure.WindspeedID = model.WindspeedID.Value;
                                        }
                                        if (model.MoistureTypeId.HasValue)
                                        {
                                            organicManure.MoistureID = model.MoistureTypeId.Value;
                                        }
                                        if (model.RainfallWithinSixHoursID.HasValue)
                                        {
                                            organicManure.RainfallWithinSixHoursID = model.RainfallWithinSixHoursID.Value;
                                        }
                                        i++;
                                    }
                                }

                                if (model.IsCheckAnswer && model.IsFieldGroupChange)
                                {
                                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                                    {
                                        OrganicManureViewModel organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                                        if (organicManureViewModel != null && organicManureViewModel.FieldList.Count > 0)
                                        {
                                            var fieldListChange = organicManureViewModel.FieldList.Where(item1 => !model.FieldList.Any(item2 => item2 == item1)).ToList();

                                            // Perform the required action for these items
                                            if (fieldListChange != null && fieldListChange.Count > 0)
                                            {
                                                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                                                var crop = cropsResponse.Where(x => x.Year == model.HarvestYear);
                                                int cropTypeId = crop.Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                                                int cropCategoryId = await _mannerService.FetchCategoryIdByCropTypeIdAsync(cropTypeId);

                                                //check early and late for winter cereals and winter oilseed rape
                                                //if sowing date after 15 sept then late
                                                DateTime? sowingDate = crop.Select(x => x.SowingDate).FirstOrDefault();
                                                if (cropCategoryId == (int)NMP.Portal.Enums.CropCategory.EarlySownWinterCereal || cropCategoryId == (int)NMP.Portal.Enums.CropCategory.EarlyStablishedWinterOilseedRape)
                                                {
                                                    if (sowingDate != null)
                                                    {
                                                        int day = sowingDate.Value.Day;
                                                        int month = sowingDate.Value.Month;
                                                        if (month == (int)NMP.Portal.Enums.Month.September && day > 15)
                                                        {
                                                            if (cropCategoryId == (int)NMP.Portal.Enums.CropCategory.EarlySownWinterCereal)
                                                            {
                                                                cropCategoryId = (int)NMP.Portal.Enums.CropCategory.LateSownWinterCereal;
                                                            }
                                                            else
                                                            {
                                                                cropCategoryId = (int)NMP.Portal.Enums.CropCategory.LateStablishedWinterOilseedRape;
                                                            }
                                                        }
                                                    }
                                                }

                                                if (model.ApplicationDate.Value.Month >= (int)NMP.Portal.Enums.Month.August && model.ApplicationDate.Value.Month <= (int)NMP.Portal.Enums.Month.October)
                                                {

                                                    model.AutumnCropNitrogenUptake = await _mannerService.FetchCropNUptakeDefaultAsync(cropCategoryId);
                                                }
                                                else
                                                {
                                                    model.AutumnCropNitrogenUptake = 0;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        return RedirectToAction("FarmList", "Farm");
                                    }

                                }
                                if (model.FieldList != null && model.FieldList.Count == 1)
                                {
                                    model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(model.FieldList[0]))).Name;
                                }
                            }
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            return RedirectToAction("ManureGroup");
                        }
                    }
                    else
                    {
                        TempData["FieldGroupError"] = error.Message;
                        return View("FieldGroup", model);
                    }
                }
                else
                {
                    int decryptedId = Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedOrgManureId));
                    if (decryptedId > 0 && model.FarmId != null && model.HarvestYear != null)
                    {
                        (List<FertiliserAndOrganicManureUpdateResponse> organicManureResponse, error) = await _organicManureService.FetchFieldWithSameDateAndManureType(decryptedId, model.FarmId.Value, model.HarvestYear.Value);
                        if (string.IsNullOrWhiteSpace(error.Message) && organicManureResponse != null && organicManureResponse.Count > 0)
                        {
                            var SelectListItem = organicManureResponse.Select(f => new SelectListItem
                            {
                                Value = f.Id.ToString(),
                                Text = f.Name.ToString()
                            }).ToList().DistinctBy(x => x.Value);
                            ViewBag.FieldList = SelectListItem.OrderBy(x => x.Text).ToList();
                            return View(model);
                        }
                        else
                        {
                            TempData["AddOrganicManureError"] = error.Message;
                            return RedirectToAction("CheckAnswer");
                        }
                    }
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in Fields() action : {ex.Message}, {ex.StackTrace}");
                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                {
                    TempData["FieldGroupError"] = ex.Message;
                    return RedirectToAction("FieldGroup", model);
                }
                else
                {
                    TempData["AddOrganicManureError"] = ex.Message;
                    return RedirectToAction("CheckAnswer");
                }
            }
            //return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Fields(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : Fields() post action called");
            Error error = null;
            try
            {
                (List<CommonResponse> fieldList, error) = await _organicManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
                if (error == null)
                {
                    var selectListItem = fieldList.Select(f => new SelectListItem
                    {
                        Value = f.Id.ToString(),
                        Text = f.Name.ToString()
                    }).ToList();
                    ViewBag.FieldList = selectListItem.OrderBy(x => x.Text).ToList();
                    if (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                    {
                        (List<FertiliserAndOrganicManureUpdateResponse> organicManureResponse, error) = await _organicManureService.FetchFieldWithSameDateAndManureType(Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedOrgManureId)), model.FarmId.Value, model.HarvestYear.Value);
                        //(List<FertiliserAndOrganicManureUpdateResponse> organicManureResponse, error) = await _organicManureService.FetchFieldWithSameDateAndManureType(decryptedId, model.FarmId.Value, model.HarvestYear.Value);
                        if (string.IsNullOrWhiteSpace(error.Message) && organicManureResponse != null && organicManureResponse.Count > 0)
                        {
                            selectListItem = new List<SelectListItem>();
                            selectListItem = organicManureResponse.Select(f => new SelectListItem
                            {
                                Value = f.Id.ToString(),
                                Text = f.Name.ToString()
                            }).GroupBy(x => x.Value)
                            .Select(g => g.First())
                            .ToList();
                            ViewBag.FieldList = selectListItem.OrderBy(x => x.Text).ToList();
                        }
                        else
                        {
                            TempData["FieldError"] = error.Message;
                            return View(model);
                        }
                    }
                    if (model.FieldList == null || model.FieldList.Count == 0)
                    {
                        ModelState.AddModelError("FieldList", Resource.MsgSelectAtLeastOneField);
                    }
                    if (!ModelState.IsValid)
                    {
                        if (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                        {
                            int decryptedId = Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedOrgManureId));
                            if (decryptedId > 0 && model.FarmId != null && model.HarvestYear != null)
                            {
                                (List<FertiliserAndOrganicManureUpdateResponse> organicManureResponse, error) = await _organicManureService.FetchFieldWithSameDateAndManureType(decryptedId, model.FarmId.Value, model.HarvestYear.Value);
                                if (string.IsNullOrWhiteSpace(error.Message) && organicManureResponse != null && organicManureResponse.Count > 0)
                                {
                                    var SelectListItem = organicManureResponse.Select(f => new SelectListItem
                                    {
                                        Value = f.Id.ToString(),
                                        Text = f.Name.ToString()
                                    }).ToList().DistinctBy(x => x.Value);
                                    ViewBag.FieldList = SelectListItem.OrderBy(x => x.Text).ToList();
                                    return View(model);
                                }
                                else
                                {
                                    TempData["AddOrganicManureError"] = error.Message;
                                    return RedirectToAction("CheckAnswer");
                                }
                            }
                        }
                        return View(model);

                    }

                    if (model.FieldList.Count > 0 && model.FieldList.Contains(Resource.lblSelectAll))
                    {
                        model.FieldList = selectListItem.Where(item => item.Value != Resource.lblSelectAll).Select(item => item.Value).ToList();
                    }
                    string fieldIds = string.Join(",", model.FieldList);
                    //(List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, model.CropOrder);
                    (List<int> managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);
                    if (error == null)
                    {
                        if (managementIds.Count > 0)
                        {
                            if (model.OrganicManures == null)
                            {
                                model.OrganicManures = new List<OrganicManure>();
                            }
                            if (model.OrganicManures.Count > 0)
                            {
                                model.OrganicManures.Clear();
                            }

                            foreach (var manIds in managementIds)
                            {
                                var organicManure = new OrganicManure
                                {
                                    ManagementPeriodID = manIds
                                };
                                model.OrganicManures.Add(organicManure);
                            }

                            if (model.IsCheckAnswer && model.OrganicManures.Count > 0)
                            {
                                int i = 0;
                                model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                                foreach (var field in model.FieldList)
                                {
                                    int fieldId = Convert.ToInt32(field);
                                    Field fieldData = await _fieldService.FetchFieldByFieldId(fieldId);
                                    if (fieldData != null)
                                    {
                                        (CropTypeResponse cropsResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(fieldId, model.HarvestYear.Value, false);

                                        (CropTypeLinkingResponse cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(cropsResponse.CropTypeId);

                                        if (error == null && cropTypeLinkingResponse != null)
                                        {
                                            int mannerCropTypeId = cropTypeLinkingResponse.MannerCropTypeID;

                                            var uptakeData = new
                                            {
                                                cropTypeId = mannerCropTypeId,
                                                applicationMonth = model.ApplicationDate.Value.Month
                                            };

                                            string jsonString = JsonConvert.SerializeObject(uptakeData);
                                            (NitrogenUptakeResponse nitrogenUptakeResponse, error) = await _organicManureService.FetchAutumnCropNitrogenUptake(jsonString);
                                            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                                            {
                                                TempData["FieldError"] = error.Message;
                                                return View(model);
                                            }
                                            if (nitrogenUptakeResponse != null && error == null)
                                            {
                                                if (model.AutumnCropNitrogenUptakes == null)
                                                {
                                                    model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                                                }

                                                model.AutumnCropNitrogenUptakes.Add(new AutumnCropNitrogenUptakeDetail
                                                {
                                                    EncryptedFieldId = _organicManureProtector.Protect(fieldId.ToString()),
                                                    FieldName = fieldData.Name ?? string.Empty,
                                                    CropTypeId = cropsResponse.CropTypeId,
                                                    CropTypeName = cropsResponse.CropType,
                                                    AutumnCropNitrogenUptake = nitrogenUptakeResponse.value
                                                });
                                            }
                                        }
                                        else if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                                        {
                                            TempData["FieldError"] = error.Message;
                                            return View(model);
                                        }
                                    }
                                }
                                (List<FarmManureTypeResponse> farmManureTypeList, error) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                                if (error == null && farmManureTypeList.Count > 0)
                                {
                                    FarmManureTypeResponse farmManureType = farmManureTypeList.Where(x => x.ManureTypeID == model.ManureTypeId).FirstOrDefault();
                                    if (farmManureType != null)
                                    {
                                        model.DefaultFarmManureValueDate = farmManureType.ModifiedOn == null ? farmManureType.CreatedOn : farmManureType.ModifiedOn;
                                    }
                                }
                                foreach (var organicManure in model.OrganicManures)
                                {
                                    if (model.ApplicationDate.HasValue)
                                    {
                                        organicManure.ApplicationDate = model.ApplicationDate.Value;
                                    }
                                    if (model.ApplicationMethod.HasValue)
                                    {
                                        organicManure.ApplicationMethodID = model.ApplicationMethod.Value;
                                    }
                                    if (model.ApplicationRate.HasValue)
                                    {
                                        organicManure.ApplicationRate = model.ApplicationRate.Value;
                                    }
                                    if (model.Area.HasValue)
                                    {
                                        organicManure.AreaSpread = model.Area.Value;
                                    }
                                    if (model.Quantity.HasValue)
                                    {
                                        organicManure.ManureQuantity = model.Quantity.Value;
                                    }
                                    organicManure.ManureTypeID = model.ManureTypeId.Value;
                                    organicManure.ManureTypeName = model.OtherMaterialName;
                                    if (model.TotalRainfall.HasValue)
                                    {
                                        organicManure.Rainfall = model.TotalRainfall.Value;
                                    }
                                    //if (model.DefaultNutrientValue.HasValue && (!model.DefaultNutrientValue.Value))
                                    //{
                                    if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblIwantToEnterARecentOrganicMaterialAnalysis)
                                    {
                                        organicManure.DryMatterPercent = model.DryMatterPercent.Value;
                                        organicManure.K2O = model.K2O.Value;
                                        organicManure.MgO = model.MgO.Value;
                                        organicManure.N = model.N.Value;
                                        organicManure.NH4N = model.NH4N.Value;
                                        organicManure.NO3N = model.NO3N.Value;
                                        organicManure.P2O5 = model.P2O5.Value;
                                        organicManure.SO3 = model.SO3.Value;
                                        organicManure.UricAcid = model.UricAcid.Value;
                                    }
                                    else
                                    {
                                        if (model.ManureType != null)
                                        {
                                            organicManure.DryMatterPercent = model.ManureType.DryMatter.Value;
                                            organicManure.K2O = model.ManureType.K2O.Value;
                                            organicManure.MgO = model.ManureType.MgO.Value;
                                            organicManure.N = model.ManureType.TotalN.Value;
                                            organicManure.NH4N = model.ManureType.NH4N.Value;
                                            organicManure.NO3N = model.ManureType.NO3N.Value;
                                            organicManure.P2O5 = model.ManureType.P2O5.Value;
                                            organicManure.SO3 = model.ManureType.SO3.Value;
                                            organicManure.UricAcid = model.ManureType.Uric.Value;
                                        }
                                    }
                                    if (model.IncorporationDelay.HasValue)
                                    {
                                        organicManure.IncorporationDelayID = model.IncorporationDelay.Value;
                                    }
                                    if (model.IncorporationMethod.HasValue)
                                    {
                                        organicManure.IncorporationMethodID = model.IncorporationMethod.Value;
                                    }
                                    if (model.SoilDrainageEndDate.HasValue)
                                    {
                                        organicManure.EndOfDrain = model.SoilDrainageEndDate.Value;
                                    }
                                    //if (model.AutumnCropNitrogenUptake.HasValue)
                                    //{
                                    //    organicManure.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptake.Value;
                                    //}
                                    if (model.AutumnCropNitrogenUptakes != null && model.AutumnCropNitrogenUptakes.Count > 0 && i < model.AutumnCropNitrogenUptakes.Count)
                                    {
                                        organicManure.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes[i].AutumnCropNitrogenUptake;
                                    }
                                    if (model.WindspeedID.HasValue)
                                    {
                                        organicManure.WindspeedID = model.WindspeedID.Value;
                                    }
                                    if (model.MoistureTypeId.HasValue)
                                    {
                                        organicManure.MoistureID = model.MoistureTypeId.Value;
                                    }
                                    if (model.RainfallWithinSixHoursID.HasValue)
                                    {
                                        organicManure.RainfallWithinSixHoursID = model.RainfallWithinSixHoursID.Value;
                                    }
                                    i++;
                                }
                            }

                        }
                    }
                    else
                    {
                        TempData["FieldError"] = error.Message;
                        return View(model);
                    }
                    foreach (var organicManure in model.OrganicManures)
                    {
                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                            {
                                organicManure.FieldID = crop.FieldID;
                                organicManure.FieldName = (await _fieldService.FetchFieldByFieldId(organicManure.FieldID.Value)).Name;
                            }
                        }
                    }
                    OrganicManureViewModel organicManureViewModel = null;
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                        if (organicManureViewModel != null && organicManureViewModel.FieldList != null && organicManureViewModel.FieldList.Count > 0)
                        {
                            model.IsAnyChangeInField = !organicManureViewModel.FieldList.All(model.FieldList.Contains) || !model.FieldList.All(organicManureViewModel.FieldList.Contains);

                            if (model.IsAnyChangeInField)
                            {
                                model.DoubleCrop = null;
                                model.DoubleCropCurrentCounter = 0;
                                model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                            }
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        }
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    model.IsDoubleCropAvailable = false;
                    foreach (string field in model.FieldList)
                    {
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                        cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                        if (cropList != null && cropList.Count == 2)
                        {
                            model.IsDoubleCropAvailable = true;
                            int counter = 0;
                            model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                            model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                            break;
                        }
                        else
                        {
                            model.IsDoubleCropAvailable = false;
                        }
                    }
                    if (model.IsCheckAnswer && model.IsFieldGroupChange)
                    {

                        if (organicManureViewModel != null && organicManureViewModel.FieldList.Count > 0)
                        {
                            // Perform the required action for these items
                            if (model.IsAnyChangeInField)
                            {
                                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                                var crop = cropsResponse.Where(x => x.Year == model.HarvestYear);
                                int cropTypeId = crop.Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                                int cropCategoryId = await _mannerService.FetchCategoryIdByCropTypeIdAsync(cropTypeId);

                                //check early and late for winter cereals and winter oilseed rape
                                //if sowing date after 15 sept then late
                                DateTime? sowingDate = crop.Select(x => x.SowingDate).FirstOrDefault();
                                if (cropCategoryId == (int)NMP.Portal.Enums.CropCategory.EarlySownWinterCereal || cropCategoryId == (int)NMP.Portal.Enums.CropCategory.EarlyStablishedWinterOilseedRape)
                                {
                                    if (sowingDate != null)
                                    {
                                        int day = sowingDate.Value.Day;
                                        int month = sowingDate.Value.Month;
                                        if (month == (int)NMP.Portal.Enums.Month.September && day > 15)
                                        {
                                            if (cropCategoryId == (int)NMP.Portal.Enums.CropCategory.EarlySownWinterCereal)
                                            {
                                                cropCategoryId = (int)NMP.Portal.Enums.CropCategory.LateSownWinterCereal;
                                            }
                                            else
                                            {
                                                cropCategoryId = (int)NMP.Portal.Enums.CropCategory.LateStablishedWinterOilseedRape;
                                            }
                                        }
                                    }
                                }

                                if (model.ApplicationDate.Value.Month >= (int)NMP.Portal.Enums.Month.August && model.ApplicationDate.Value.Month <= (int)NMP.Portal.Enums.Month.October)
                                {

                                    model.AutumnCropNitrogenUptake = await _mannerService.FetchCropNUptakeDefaultAsync(cropCategoryId);
                                }
                                else
                                {
                                    model.AutumnCropNitrogenUptake = 0;
                                }
                            }
                        }


                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                }
                else
                {
                    TempData["FieldError"] = error.Message;
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in Fields() post action : {ex.Message}, {ex.StackTrace}");
                TempData["FieldError"] = ex.Message;
                return View(model);
            }

            return RedirectToAction("ManureGroup");

        }
        [HttpGet]
        public async Task<IActionResult> ManureGroup()
        {
            _logger.LogTrace($"Organic Manure Controller : ManureGroup() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            try
            {
                (List<CommonResponse> manureGroupList, Error error) = await _organicManureService.FetchManureGroupList();
                (List<FarmManureTypeResponse> farmManureTypeList, Error error1) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                if (error == null)
                {
                    if (manureGroupList.Count > 0)
                    {
                        var SelectListItem = manureGroupList.OrderBy(x => x.SortOrder).Select(f => new SelectListItem
                        {
                            Value = f.Id.ToString(),
                            Text = f.Name.ToString()
                        }).ToList();
                        ViewBag.ManureGroupList = SelectListItem;
                    }
                }

                if (error1 == null)
                {
                    if (farmManureTypeList.Count > 0)
                    {
                        //foreach (var farmManureType in farmManureTypeList)
                        //{
                        var filteredFarmManureTypes = farmManureTypeList
                        .Where(farmManureType => farmManureType.ManureTypeID == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials ||
                        farmManureType.ManureTypeID == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                        .ToList();
                        if (filteredFarmManureTypes != null && filteredFarmManureTypes.Count > 0)
                        {
                            var selectListItems = filteredFarmManureTypes.Select(f => new SelectListItem
                            {
                                Value = f.ManureTypeID.ToString(),
                                Text = f.ManureTypeName
                            }).OrderBy(x => x.Text).ToList();
                            ViewBag.FarmManureTypeList = selectListItems;
                        }
                    }
                }
                else
                {
                    TempData["FieldError"] = error.Message;
                    return RedirectToAction("Fields", model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManureGroup() action : {ex.Message}, {ex.StackTrace}");
                TempData["FieldError"] = ex.Message;
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManureGroup(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ManureGroup() post action called");
            if (model.ManureGroupIdForFilter == null)
            {
                ModelState.AddModelError("ManureGroupIdForFilter", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            Error error = null;
            try
            {
                if (!ModelState.IsValid)
                {
                    (List<CommonResponse> manureGroupList, error) = await _organicManureService.FetchManureGroupList();
                    (List<FarmManureTypeResponse> farmManureTypeList, Error error1) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                    if (error == null)
                    {

                        if (manureGroupList.Count > 0)
                        {

                            var SelectListItem = manureGroupList.OrderBy(x => x.SortOrder).Select(f => new SelectListItem
                            {
                                Value = f.Id.ToString(),
                                Text = f.Name.ToString()
                            }).ToList();
                            ViewBag.ManureGroupList = SelectListItem;
                        }
                    }
                    if (error1 == null)
                    {
                        if (farmManureTypeList.Count > 0)
                        {
                            var filteredFarmManureTypes = farmManureTypeList
                            .Where(farmManureType => farmManureType.ManureTypeID == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials ||
                            farmManureType.ManureTypeID == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                            .ToList();
                            if (filteredFarmManureTypes != null && filteredFarmManureTypes.Count > 0)
                            {
                                var selectListItems = filteredFarmManureTypes.Select(f => new SelectListItem
                                {
                                    Value = f.ManureTypeID.ToString(),
                                    Text = f.ManureTypeName
                                }).OrderBy(x => x.Text).ToList();
                                ViewBag.FarmManureTypeList = selectListItems;
                            }
                        }
                    }
                    else
                    {
                        TempData["ManureGroupError"] = error.Message;
                    }
                    return View(model);

                }

                if (model.IsCheckAnswer)
                {
                    model.IsManureTypeChange = true;
                }
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {

                    (List<FarmManureTypeResponse> farmManureTypeList, error) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                    if (error == null)
                    {
                        if (farmManureTypeList.Count > 0)
                        {
                            (List<CommonResponse> manureGroupList, error) = await _organicManureService.FetchManureGroupList();
                            if (error == null)
                            {
                                model.OtherMaterialName = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureGroupIdForFilter)?.ManureTypeName;
                                model.ManureGroupId = manureGroupList.FirstOrDefault(x => x.Name.Equals(Resource.lblOtherOrganicMaterials, StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
                                model.ManureTypeId = model.ManureGroupIdForFilter;
                                model.ManureTypeName = model.OtherMaterialName;
                                foreach (var organicManures in model.OrganicManures)
                                {
                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManures.ManagementPeriodID);
                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                    {
                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                        {
                                            if (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && crop.DefoliationSequenceID != null)
                                            {
                                                model.IsAnyCropIsGrass = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                                List<string> fieldsToRemove = new List<string>();
                                foreach (string field in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));

                                    if (cropList.Count > 0)
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();

                                            if (cropList != null && cropList.Count == 2)
                                            {
                                                model.IsDoubleCropAvailable = true;
                                                int counter = 0;
                                                model.DoubleCropCurrentCounter = counter;
                                                model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                                                model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                            }
                                            else
                                            {
                                                if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass || (x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.IsBasePlan)))
                                                {
                                                    fieldsToRemove.Add(field);
                                                }
                                                if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                                                {
                                                    model.IsAnyCropIsGrass = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                OrganicManureViewModel orgManureViewModel = null;
                                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                                {
                                    orgManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                                }
                                List<string> fieldListCopy = new List<string>(model.FieldList);
                                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                                {
                                    foreach (var field in fieldsToRemove)
                                    {
                                        fieldListCopy.Remove(field);
                                    }

                                    string fieldIds = string.Join(",", fieldListCopy);
                                    List<int> managementIds = new List<int>();
                                    if (int.TryParse(model.FieldGroup, out int cropTypeIdForFilter))
                                    {
                                        (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, cropTypeIdForFilter.ToString(), null);//1 is CropOrder
                                    }
                                    else
                                    {
                                        (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);//1 is CropOrder
                                    }
                                    if (error == null)
                                    {
                                        if (managementIds.Count > 0)
                                        {
                                            if (model.OrganicManures == null)
                                            {
                                                model.OrganicManures = new List<OrganicManure>();
                                            }
                                            if (model.OrganicManures.Count > 0)
                                            {
                                                model.OrganicManures.Clear();
                                            }
                                            int counter = 1;
                                            foreach (var manIds in managementIds)
                                            {
                                                var organicManure = new OrganicManure
                                                {
                                                    ManagementPeriodID = manIds,
                                                    ManureTypeID = model.ManureTypeId.Value,
                                                    ManureTypeName = model.ManureTypeName,
                                                    ApplicationMethodID = model.ApplicationMethod,
                                                    N = model.N,
                                                    P2O5 = model.P2O5,
                                                    NH4N = model.NH4N,
                                                    K2O = model.K2O,
                                                    MgO = model.MgO,
                                                    NO3N = model.NO3N,
                                                    SO3 = model.SO3,
                                                    ApplicationRate = model.ApplicationRate != null ? model.ApplicationRate : null,
                                                    AreaSpread = model.Area != null ? model.Area : null,
                                                    ManureQuantity = model.Quantity != null ? model.Quantity : null,
                                                    DryMatterPercent = model.DryMatterPercent,
                                                    UricAcid = model.UricAcid,
                                                    EncryptedCounter = _fieldDataProtector.Protect(counter.ToString())

                                                };
                                                if (model.ApplicationDate != null)
                                                {
                                                    organicManure.ApplicationDate = model.ApplicationDate.Value;
                                                }
                                                if (model.WindspeedID != null)
                                                {
                                                    organicManure.WindspeedID = model.WindspeedID.Value;
                                                }
                                                if (model.RainfallWithinSixHoursID != null)
                                                {
                                                    organicManure.RainfallWithinSixHoursID = model.RainfallWithinSixHoursID.Value;
                                                }
                                                if (model.TotalRainfall != null)
                                                {
                                                    organicManure.Rainfall = model.TotalRainfall.Value;
                                                }
                                                if (model.MoistureTypeId != null)
                                                {
                                                    organicManure.MoistureID = model.MoistureTypeId.Value;
                                                }
                                                if (model.SoilDrainageEndDate != null)
                                                {
                                                    organicManure.SoilDrainageEndDate = model.SoilDrainageEndDate.Value;
                                                    organicManure.EndOfDrain = model.SoilDrainageEndDate.Value;
                                                }
                                                counter++;
                                                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                                                {

                                                    if (orgManureViewModel.OrganicManures != null && orgManureViewModel.OrganicManures.Count > 0)
                                                    {
                                                        for (int i = 0; i < orgManureViewModel.OrganicManures.Count; i++)
                                                        {
                                                            if (orgManureViewModel.OrganicManures[i].ManagementPeriodID == manIds && orgManureViewModel.OrganicManures[i].Defoliation != null)
                                                            {
                                                                organicManure.Defoliation = orgManureViewModel.OrganicManures[i].Defoliation;
                                                                (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manIds);
                                                                if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                                                {
                                                                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                                                    if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                                                    {
                                                                        (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                                                        if (error == null && defoliationSequence != null)
                                                                        {
                                                                            string description = defoliationSequence.DefoliationSequenceDescription;

                                                                            string[] defoliationParts = description.Split(',')
                                                                                                                   .Select(x => x.Trim())
                                                                                                                   .ToArray();

                                                                            string selectedDefoliation = (organicManure.Defoliation.Value > 0 && organicManure.Defoliation.Value <= defoliationParts.Length)
                                                                                ? $"{Enum.GetName(typeof(PotentialCut), organicManure.Defoliation.Value)} ({defoliationParts[organicManure.Defoliation.Value - 1]})"
                                                                                : $"{organicManure.Defoliation.Value}";

                                                                            organicManure.DefoliationName = selectedDefoliation;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                model.OrganicManures.Add(organicManure);
                                            }

                                        }
                                        model.DefoliationCurrentCounter = 0;
                                    }
                                    else
                                    {
                                        TempData["ManureGroupError"] = error.Message;
                                        return View(model);
                                    }
                                }

                                int grassCropCounter = 0;
                                foreach (var field in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                                    if (cropList.Count > 0)
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeIdForFilter))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeIdForFilter).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == 1).ToList();
                                        }
                                    }
                                    if (cropList.Count > 0)
                                    {
                                        if (cropList.Any(c => c.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && c.DefoliationSequenceID != null))
                                        {
                                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
                                            {

                                                (List<ManagementPeriod> managementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropList.Select(x => x.ID.Value).FirstOrDefault(), false);

                                                var filteredOrganicManure = model.OrganicManures
                                                 .Where(fm => managementPeriod.Any(mp => mp.ID == fm.ManagementPeriodID) &&
                                                     fm.Defoliation == null).ToList();
                                                if (filteredOrganicManure != null && filteredOrganicManure.Count == managementPeriod.Count)
                                                {
                                                    var managementPeriodIdsToRemove = managementPeriod
                                                   .Skip(1)
                                                   .Select(mp => mp.ID.Value)
                                                   .ToList();
                                                    model.OrganicManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));
                                                }
                                                else
                                                {
                                                    model.OrganicManures.RemoveAll(x => managementPeriod.Any(mp => mp.ID == x.ManagementPeriodID) &&
                                                    x.Defoliation == null);
                                                }
                                                grassCropCounter++;


                                            }
                                            model.IsAnyCropIsGrass = true;
                                        }
                                    }
                                }
                                int orgCounter = 1;
                                foreach (var organic in model.OrganicManures)
                                {
                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                    {
                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                        {
                                            organic.FieldID = crop.FieldID;
                                            organic.FieldName = (await _fieldService.FetchFieldByFieldId(organic.FieldID.Value)).Name;
                                            organic.EncryptedCounter = _fieldDataProtector.Protect(orgCounter.ToString());
                                            orgCounter++;
                                        }
                                    }
                                }
                                model.GrassCropCount = grassCropCounter;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                if (model.IsDoubleCropAvailable)
                                {
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                    return RedirectToAction("DoubleCrop");
                                }
                                if (model.IsAnyCropIsGrass == null || (model.IsAnyCropIsGrass != null && !model.IsAnyCropIsGrass.Value))
                                {
                                    model.GrassCropCount = null;
                                    model.IsSameDefoliationForAll = null;
                                    model.IsAnyChangeInSameDefoliationFlag = false;
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                    return RedirectToAction("ManureApplyingDate");
                                }
                                else
                                {
                                    if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                                    {
                                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                        return RedirectToAction("IsSameDefoliationForAll");
                                    }
                                    model.IsSameDefoliationForAll = true;
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                    return RedirectToAction("Defoliation");
                                }
                                return RedirectToAction("ManureApplyingDate");
                            }

                        }
                    }

                }
                (CommonResponse manureGroup, error) = await _organicManureService.FetchManureGroupById(model.ManureGroupIdForFilter.Value);
                if (error == null)
                {
                    if (manureGroup != null)
                    {
                        model.ManureGroupName = manureGroup.Name;
                    }
                }
                else
                {
                    TempData["ManureGroupError"] = error.Message;
                    return View(model);
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManureGroup() post action : {ex.Message}, {ex.StackTrace}");
                TempData["ManureGroupError"] = ex.Message;
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ManureType");

        }

        //[HttpGet]
        //public async Task<IActionResult> ManureType()
        //{
        //    _logger.LogTrace($"Organic Manure Controller : ManureType() action called");
        //    Error error = null;
        //    OrganicManureViewModel model = new OrganicManureViewModel();
        //    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
        //    {
        //        model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
        //    }
        //    else
        //    {
        //        return RedirectToAction("FarmList", "Farm");
        //    }
        //    try
        //    {
        //        int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
        //        (List<ManureType> manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
        //        if (error == null)
        //        {
        //            if (manureTypeList.Count > 0)
        //            {
        //                var manures = manureTypeList.OrderBy(m => m.SortOrder).ToList();
        //                var SelectListItem = manures.Select(f => new SelectListItem
        //                {
        //                    Value = f.Id.ToString(),
        //                    Text = f.Name
        //                }).ToList();
        //                ViewBag.ManureTypeList = SelectListItem.ToList();
        //            }
        //            return View(model);
        //        }
        //        else
        //        {
        //            TempData["ManureGroupError"] = error.Message;
        //            return RedirectToAction("ManureGroup", model);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogTrace($"Organic Manure Controller : Exception in ManureType() action : {ex.Message}, {ex.StackTrace}");
        //        TempData["ManureGroupError"] = ex.Message;
        //        return RedirectToAction("ManureGroup", model);
        //    }
        //}

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> ManureType(OrganicManureViewModel model)
        //{
        //    _logger.LogTrace($"Organic Manure Controller : ManureType() post action called");
        //    Error error = null;
        //    if (model.ManureTypeId == null)
        //    {
        //        ModelState.AddModelError("ManureTypeId", Resource.MsgSelectAnOptionBeforeContinuing);
        //    }
        //    try
        //    {
        //        OrganicManureViewModel orgManureViewModel = null;
        //        if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
        //        {
        //            orgManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
        //        }
        //        else
        //        {
        //            return RedirectToAction("FarmList", "Farm");
        //        }
        //        int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
        //        (List<ManureType> manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
        //        if (error == null)
        //        {
        //            if (!ModelState.IsValid)
        //            {
        //                if (manureTypeList.Count > 0)
        //                {
        //                    var manures = manureTypeList.OrderBy(m => m.SortOrder).ToList();
        //                    var SelectListItem = manures.Select(f => new SelectListItem
        //                    {
        //                        Value = f.Id.ToString(),
        //                        Text = f.Name
        //                    }).ToList();
        //                    ViewBag.ManureTypeList = SelectListItem.OrderBy(x => x.Text).ToList(); ;

        //                }
        //                return View(model);

        //            }

        //            ManureType manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
        //            if (manureType != null)
        //            {
        //                model.ManureTypeName = manureType.Name;
        //                model.IsManureTypeLiquid = manureType.IsLiquid.Value;
        //                foreach (var orgManure in model.OrganicManures)
        //                {
        //                    orgManure.ManureTypeID = model.ManureTypeId.Value;
        //                    orgManure.K2O = manureType.K2O.Value;
        //                    if (manureType.MgO != null)
        //                    {
        //                        orgManure.MgO = manureType.MgO.Value;
        //                    }
        //                    orgManure.P2O5 = manureType.P2O5.Value;
        //                    if (manureType.SO3 != null)
        //                    {
        //                        orgManure.SO3 = manureType.SO3.Value;
        //                    }
        //                    orgManure.NH4N = manureType.NH4N.Value;
        //                    orgManure.NO3N = manureType.NO3N.Value;
        //                    orgManure.UricAcid = manureType.Uric.Value;
        //                    orgManure.DryMatterPercent = manureType.DryMatter.Value;
        //                    orgManure.N = manureType.TotalN.Value;
        //                }
        //            }

        //            //OrganicManureViewModel organicManureViewModel = JsonConvert.DeserializeObject<OrganicManureViewModel>(HttpContext.Session.GetString("OrganicManure"));
        //            if (orgManureViewModel != null)
        //            {
        //                if (orgManureViewModel.ManureTypeId != model.ManureTypeId)
        //                {
        //                    model.DryMatterPercent = null;
        //                    model.N = null;
        //                    model.P2O5 = null;
        //                    model.NH4N = null;
        //                    model.UricAcid = null;
        //                    model.SO3 = null;
        //                    model.K2O = null;
        //                    model.MgO = null;
        //                    model.NO3N = null;
        //                    model.IsDefaultValueChange = true;
        //                    //model.DefaultNutrientValue = null;
        //                }
        //            }

        //            if (model.ManureGroupIdForFilter.HasValue)
        //            {
        //                model.ManureGroupId = model.ManureGroupIdForFilter;
        //            }


        //            //if manure type change 
        //            if (model.IsCheckAnswer)
        //            {
        //                //OrganicManureViewModel organicManure = new OrganicManureViewModel();
        //                //if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
        //                //{
        //                //    organicManure = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
        //                //}
        //                //else
        //                //{
        //                //    return RedirectToAction("FarmList", "Farm");
        //                //}

        //                if (orgManureViewModel.ManureTypeId != model.ManureTypeId)
        //                {
        //                    model.IsManureTypeChange = true;
        //                    if (model.ApplicationRateMethod == (int)NMP.Portal.Enums.ApplicationRate.UseDefaultApplicationRate)
        //                    {
        //                        //model.ApplicationRateMethod = null;
        //                        model.ApplicationRate = null;
        //                        foreach (var orgManure in model.OrganicManures)
        //                        {
        //                            orgManure.ApplicationRate = null;
        //                        }
        //                    }

        //                    //if manure type is changed liquid to soild or solid to liquid then ApplicationMethod,IncorporationMethod,IncorporationDelay need to set null
        //                    if (orgManureViewModel.IsManureTypeLiquid.Value != model.IsManureTypeLiquid.Value)
        //                    {
        //                        model.ApplicationMethod = null;
        //                        model.IncorporationMethod = null;
        //                        model.IncorporationDelay = null;
        //                        model.ApplicationMethodName = string.Empty;
        //                        model.IncorporationMethodName = string.Empty;
        //                        model.IncorporationDelayName = string.Empty;
        //                        foreach (var orgManure in model.OrganicManures)
        //                        {
        //                            orgManure.ApplicationMethodID = null;
        //                            orgManure.IncorporationDelayID = null;
        //                            orgManure.IncorporationMethodID = null;
        //                        }
        //                    }

        //                    //if manure type is changed then we need to bind default values
        //                    (ManureType manureTypeData, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
        //                    if (error == null)
        //                    {
        //                        model.ManureType = manureTypeData;
        //                        //if (model.DefaultNutrientValue.HasValue && (!model.DefaultNutrientValue.Value))
        //                        //{
        //                        if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblIwantToEnterARecentOrganicMaterialAnalysis)
        //                        {
        //                            model.DryMatterPercent = model.ManureType.DryMatter;
        //                            model.N = model.ManureType.TotalN;
        //                            model.P2O5 = model.ManureType.P2O5;
        //                            model.NH4N = model.ManureType.NH4N;
        //                            model.UricAcid = model.ManureType.Uric;
        //                            model.SO3 = model.ManureType.SO3;
        //                            model.K2O = model.ManureType.K2O;
        //                            model.MgO = model.ManureType.MgO;
        //                            model.NO3N = model.ManureType.NO3N;
        //                        }
        //                        model.DryMatterPercent = manureTypeData.DryMatter;
        //                        model.N = manureTypeData.TotalN;
        //                        model.NH4N = manureTypeData.NH4N;
        //                        model.NO3N = manureTypeData.NO3N;
        //                        model.K2O = manureTypeData.K2O;
        //                        model.SO3 = manureTypeData.SO3;
        //                        model.MgO = manureTypeData.MgO;
        //                        model.P2O5 = manureTypeData.P2O5;
        //                        model.UricAcid = manureTypeData.Uric;
        //                        foreach (var orgManure in model.OrganicManures)
        //                        {
        //                            orgManure.DryMatterPercent = manureTypeData.DryMatter;
        //                            orgManure.N = manureTypeData.TotalN;
        //                            orgManure.NH4N = manureTypeData.NH4N;
        //                            orgManure.NO3N = manureTypeData.NO3N;
        //                            orgManure.K2O = manureTypeData.K2O;
        //                            orgManure.SO3 = manureTypeData.SO3;
        //                            orgManure.MgO = manureTypeData.MgO;
        //                            orgManure.P2O5 = manureTypeData.P2O5;
        //                            orgManure.UricAcid = manureTypeData.Uric;
        //                        }
        //                    }
        //                    else
        //                    {
        //                        TempData["ManureTypeError"] = error.Message;
        //                        return View(model);
        //                    }


        //                    //if manure type is solid then need to set application method value.
        //                    if (!model.IsManureTypeLiquid.Value)
        //                    {
        //                        List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
        //                        var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();


        //                        (List<ApplicationMethodResponse> applicationMethodList, error) = await _organicManureService.FetchApplicationMethodList(fieldType ?? 0, model.IsManureTypeLiquid.Value);
        //                        if (error == null && applicationMethodList.Count > 0)
        //                        {
        //                            model.ApplicationMethod = applicationMethodList[0].ID;
        //                            foreach (var orgManure in model.OrganicManures)
        //                            {
        //                                orgManure.ApplicationMethodID = model.ApplicationMethod.Value;
        //                            }
        //                            (model.ApplicationMethodName, error) = await _organicManureService.FetchApplicationMethodById(model.ApplicationMethod.Value);
        //                            if (error != null)
        //                            {
        //                                TempData["ManureTypeError"] = error.Message;
        //                                return View(model);
        //                            }
        //                        }
        //                        else if (error != null)
        //                        {
        //                            TempData["ManureTypeError"] = error.Message;
        //                            return View(model);
        //                        }

        //                    }
        //                }
        //            }



        //            model.IsAnyCropIsGrass = false;
        //            //logic start for defoliation
        //            List<string> fieldsToRemove = new List<string>();
        //            foreach (string field in model.FieldList)
        //            {
        //                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));

        //                if (cropList.Count > 0)
        //                {
        //                    //temporary croporder==1
        //                    model.CropOrder = 1;
        //                    cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == model.CropOrder).ToList();
        //                }

        //                if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass))
        //                {
        //                    fieldsToRemove.Add(field);
        //                }
        //                if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
        //                {
        //                    model.IsAnyCropIsGrass = true;
        //                }
        //            }
        //            List<string> fieldListCopy = new List<string>(model.FieldList);

        //            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
        //            {
        //                foreach (var field in fieldsToRemove)
        //                {
        //                    fieldListCopy.Remove(field);
        //                }

        //                string fieldIds = string.Join(",", fieldListCopy);
        //                (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, null);//1 is CropOrder
        //                if (error == null)
        //                {
        //                    if (managementIds.Count > 0)
        //                    {
        //                        if (model.OrganicManures == null)
        //                        {
        //                            model.OrganicManures = new List<OrganicManure>();
        //                        }
        //                        if (model.OrganicManures.Count > 0)
        //                        {
        //                            model.OrganicManures.Clear();
        //                        }
        //                        int counter = 1;
        //                        foreach (var manIds in managementIds)
        //                        {
        //                            var organicManure = new OrganicManure
        //                            {
        //                                ManagementPeriodID = manIds,
        //                                ManureTypeID = model.ManureTypeId.Value,
        //                                ManureTypeName = model.ManureTypeName,
        //                                ApplicationMethodID = model.ApplicationMethod,
        //                                N = model.N,
        //                                P2O5 = model.P2O5,
        //                                NH4N = model.NH4N,
        //                                K2O = model.K2O,
        //                                MgO = model.MgO,
        //                                NO3N = model.NO3N,
        //                                SO3 = model.SO3,
        //                                ApplicationRate = model.ApplicationRate != null ? model.ApplicationRate : null,
        //                                AreaSpread = model.Area != null ? model.Area : null,
        //                                ManureQuantity = model.Quantity != null ? model.Quantity : null,
        //                                DryMatterPercent = model.DryMatterPercent,
        //                                UricAcid = model.UricAcid,
        //                                EncryptedCounter = _fieldDataProtector.Protect(counter.ToString())

        //                            };
        //                            if (model.ApplicationDate != null)
        //                            {
        //                                organicManure.ApplicationDate = model.ApplicationDate.Value;
        //                            }
        //                            if (model.WindspeedID != null)
        //                            {
        //                                organicManure.WindspeedID = model.WindspeedID.Value;
        //                            }
        //                            if (model.RainfallWithinSixHoursID != null)
        //                            {
        //                                organicManure.RainfallWithinSixHoursID = model.RainfallWithinSixHoursID.Value;
        //                            }
        //                            if (model.TotalRainfall != null)
        //                            {
        //                                organicManure.Rainfall = model.TotalRainfall.Value;
        //                            }
        //                            if (model.MoistureTypeId != null)
        //                            {
        //                                organicManure.MoistureID = model.MoistureTypeId.Value;
        //                            }
        //                            if (model.SoilDrainageEndDate != null)
        //                            {
        //                                organicManure.SoilDrainageEndDate = model.SoilDrainageEndDate.Value;
        //                                organicManure.EndOfDrain = model.SoilDrainageEndDate.Value;
        //                            }
        //                            counter++;
        //                            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
        //                            {

        //                                if (orgManureViewModel.OrganicManures != null && orgManureViewModel.OrganicManures.Count > 0)
        //                                {
        //                                    for (int i = 0; i < orgManureViewModel.OrganicManures.Count; i++)
        //                                    {
        //                                        if (orgManureViewModel.OrganicManures[i].ManagementPeriodID == manIds && orgManureViewModel.OrganicManures[i].Defoliation != null)
        //                                        {
        //                                            organicManure.Defoliation = orgManureViewModel.OrganicManures[i].Defoliation;
        //                                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manIds);
        //                                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
        //                                            {
        //                                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

        //                                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
        //                                                {
        //                                                    (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
        //                                                    if (error == null && defoliationSequence != null)
        //                                                    {
        //                                                        string description = defoliationSequence.DefoliationSequenceDescription;

        //                                                        string[] defoliationParts = description.Split(',')
        //                                                                                               .Select(x => x.Trim())
        //                                                                                               .ToArray();

        //                                                        string selectedDefoliation = (organicManure.Defoliation.Value > 0 && organicManure.Defoliation.Value <= defoliationParts.Length)
        //                                                            ? $"{Enum.GetName(typeof(PotentialCut), organicManure.Defoliation.Value)} ({defoliationParts[organicManure.Defoliation.Value - 1]})"
        //                                                            : $"{organicManure.Defoliation.Value}";

        //                                                        organicManure.DefoliationName = selectedDefoliation;
        //                                                    }
        //                                                }
        //                                            }
        //                                        }
        //                                    }
        //                                }
        //                            }
        //                            else
        //                            {
        //                                return RedirectToAction("FarmList", "Farm");
        //                            }
        //                            model.OrganicManures.Add(organicManure);
        //                        }

        //                    }
        //                    model.DefoliationCurrentCounter = 0;
        //                }
        //                else
        //                {
        //                    TempData["ManureTypeError"] = error.Message;
        //                    return View(model);
        //                }
        //            }


        //            foreach (var organic in model.OrganicManures)
        //            {
        //                (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
        //                if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
        //                {
        //                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
        //                    if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
        //                    {
        //                        organic.FieldID = crop.FieldID;
        //                        organic.FieldName = (await _fieldService.FetchFieldByFieldId(organic.FieldID.Value)).Name;
        //                    }
        //                }
        //            }

        //            int grassCropCounter = 0;
        //            foreach (var field in model.FieldList)
        //            {
        //                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
        //                if (cropList.Count > 0)
        //                {
        //                    cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == model.CropOrder).ToList();
        //                }
        //                if (cropList.Count > 0)
        //                {
        //                    if (cropList.Any(c => c.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
        //                    {
        //                        if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
        //                        {

        //                            (List<ManagementPeriod> managementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropList.Select(x => x.ID.Value).FirstOrDefault(), false);

        //                            var filteredOrganicManure = model.OrganicManures
        //                             .Where(fm => managementPeriod.Any(mp => mp.ID == fm.ManagementPeriodID) &&
        //                                 fm.Defoliation == null).ToList();
        //                            if (filteredOrganicManure != null && filteredOrganicManure.Count == managementPeriod.Count)
        //                            {
        //                                var managementPeriodIdsToRemove = managementPeriod
        //                               .Skip(1)
        //                               .Select(mp => mp.ID.Value)
        //                               .ToList();
        //                                model.OrganicManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));
        //                            }
        //                            else
        //                            {
        //                                model.OrganicManures.RemoveAll(x => managementPeriod.Any(mp => mp.ID == x.ManagementPeriodID) &&
        //                                x.Defoliation == null);
        //                            }
        //                            grassCropCounter++;


        //                        }
        //                        model.IsAnyCropIsGrass = true;
        //                    }
        //                }
        //            }

        //            model.GrassCropCount = grassCropCounter;
        //        }

        //        //logic end for defoliation
        //        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);


        //        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
        //        {
        //            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
        //            return RedirectToAction("OtherMaterialName");
        //        }
        //        if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
        //        {
        //            //model.GrassCropCount = null;
        //            //model.IsSameDefoliationForAll = null;
        //            //model.IsAnyChangeInSameDefoliationFlag = false;
        //            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
        //            return RedirectToAction("CheckAnswer");

        //        }
        //        else
        //        {
        //            if (model.IsDoubleCropAvailable)
        //            {
        //                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
        //                return RedirectToAction("DoubleCrop");
        //            }
        //            if (!model.IsAnyCropIsGrass.Value)
        //            {
        //                model.GrassCropCount = null;
        //                model.IsSameDefoliationForAll = null;
        //                model.IsAnyChangeInSameDefoliationFlag = false;
        //                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
        //                return RedirectToAction("ManureApplyingDate");
        //            }
        //            else
        //            {
        //                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
        //                {
        //                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
        //                    return RedirectToAction("IsSameDefoliationForAll");
        //                }
        //                model.IsSameDefoliationForAll = true;
        //                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
        //                return RedirectToAction("Defoliation");
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogTrace($"Organic Manure Controller : Exception in ManureType() post action : {ex.Message}, {ex.StackTrace}");
        //        TempData["ManureTypeError"] = ex.Message;
        //    }

        //    return RedirectToAction("ManureApplyingDate");


        //}

        [HttpGet]
        public async Task<IActionResult> ManureApplyingDate()
        {
            _logger.LogTrace($"Organic Manure Controller : ManureApplyingDate() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    return View(model);
                }
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                Error error = null;
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                model.ManureTypeName = (error == null && manureTypeList.Count > 0) ? manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.Name : string.Empty;
                bool isHighReadilyAvailableNitrogen = false;
                if (error == null && manureTypeList.Count > 0)
                {
                    var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                    model.ManureTypeName = manureType.Name;
                    isHighReadilyAvailableNitrogen = manureType.HighReadilyAvailableNitrogen ?? false;
                    model.HighReadilyAvailableNitrogen = manureType.HighReadilyAvailableNitrogen;
                }
                else
                {
                    model.ManureTypeName = string.Empty;
                }
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    model.ManureTypeName = model.OtherMaterialName;
                }
                (List<CommonResponse> manureGroupList, Error error1) = await _organicManureService.FetchManureGroupList();
                model.ManureGroupName = (error1 == null && manureGroupList.Count > 0) ? manureGroupList.FirstOrDefault(x => x.Id == model.ManureGroupId)?.Name : string.Empty;

                int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId));

                (Farm farm, error) = await _farmService.FetchFarmByIdAsync(farmId);
                if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                {
                    TempData["Error"] = error.Message;
                }
                if (farm != null)
                {

                    (FieldDetailResponse fieldDetail, Error error2) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);

                    WarningWithinPeriod warningMessage = new WarningWithinPeriod();
                    string closedPeriod = string.Empty;
                    bool isPerennial = false;

                    List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                    int cropTypeId = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropTypeID).FirstOrDefault() ?? 0;

                    if (!farm.RegisteredOrganicProducer.Value && isHighReadilyAvailableNitrogen)
                    {
                        (CropTypeResponse cropTypeResponse, Error error3) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);
                        if (error3 == null)
                        {
                            isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeResponse.CropTypeId);
                        }
                        closedPeriod = warningMessage.ClosedPeriodNonOrganicFarm(fieldDetail, model.HarvestYear ?? 0, isPerennial);
                    }
                    else
                    {

                        isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeId);
                        int? cropInfo1 = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropInfo1).FirstOrDefault();
                        closedPeriod = warningMessage.ClosedPeriodOrganicFarm(fieldDetail, model.HarvestYear ?? 0, cropTypeId, cropInfo1, isPerennial);
                    }
                    model.ClosedPeriod = closedPeriod;
                    if (!string.IsNullOrWhiteSpace(closedPeriod))
                    {
                        int harvestYear = model.HarvestYear ?? 0;
                        int startYear = harvestYear;
                        int endYear = harvestYear + 1;
                        string pattern = @"(\d{1,2})\s(\w+)\s*to\s*(\d{1,2})\s(\w+)";
                        Regex regex = new Regex(pattern);
                        if (closedPeriod != null)
                        {
                            Match match = regex.Match(closedPeriod);
                            if (match.Success)
                            {
                                int startDay = int.Parse(match.Groups[1].Value);
                                string startMonthStr = match.Groups[2].Value;
                                int endDay = int.Parse(match.Groups[3].Value);
                                string endMonthStr = match.Groups[4].Value;

                                Dictionary<int, string> dtfi = new Dictionary<int, string>();
                                dtfi.Add(0, Resource.lblJanuary);
                                dtfi.Add(1, Resource.lblFebruary);
                                dtfi.Add(2, Resource.lblMarch);
                                dtfi.Add(3, Resource.lblApril);
                                dtfi.Add(4, Resource.lblMay);
                                dtfi.Add(5, Resource.lblJune);
                                dtfi.Add(6, Resource.lblJuly);
                                dtfi.Add(7, Resource.lblAugust);
                                dtfi.Add(8, Resource.lblSeptember);
                                dtfi.Add(9, Resource.lblOctober);
                                dtfi.Add(10, Resource.lblNovember);
                                dtfi.Add(11, Resource.lblDecember);
                                int startMonth = dtfi.FirstOrDefault(v => v.Value == startMonthStr).Key + 1; // Array.IndexOf(dtfi.Values, startMonthStr) + 1;
                                int endMonth = dtfi.FirstOrDefault(v => v.Value == endMonthStr).Key + 1;//Array.IndexOf(dtfi.AbbreviatedMonthNames, endMonthStr) + 1;
                                if (startMonth <= endMonth)
                                {
                                    model.ClosedPeriodStartDate = new DateTime(harvestYear - 1, startMonth, startDay);
                                    model.ClosedPeriodEndDate = new DateTime(harvestYear - 1, endMonth, endDay);
                                }
                                else if (startMonth >= endMonth)
                                {
                                    model.ClosedPeriodStartDate = new DateTime(harvestYear - 1, startMonth, startDay);
                                    model.ClosedPeriodEndDate = new DateTime(harvestYear, endMonth, endDay);
                                }
                                string formattedStartDate = model.ClosedPeriodStartDate?.ToString("d MMMM yyyy");
                                string formattedEndDate = model.ClosedPeriodEndDate?.ToString("d MMMM yyyy");

                                Crop crop = null;
                                CropTypeLinkingResponse cropTypeLinkingResponse = new CropTypeLinkingResponse();

                                (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(cropTypeId);
                                //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                                if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                                {
                                    model.ClosedPeriodForUI = $"{formattedStartDate} to {formattedEndDate}";
                                }

                            }
                        }


                    }
                    foreach (var fieldId in model.FieldList)
                    {
                        Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                        if (field != null && field.IsWithinNVZ == true)
                        {
                            model.IsWithinNVZ = true;
                        }
                    }

                }
                if (model.FieldList.Count == 1)
                {
                    Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(model.FieldList[0]));
                    model.FieldName = field.Name;
                }
                model.IsWarningMsgNeedToShow = false;
                model.IsClosedPeriodWarning = false;
                model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManureApplyingDate() action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return View(model);
            }

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManureApplyingDate(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ManureApplyingDate() post action called");
            try
            {
                int farmId = 0;
                Farm farm = new Farm();
                Error error = new Error();
                if (model.ApplicationDate == null)
                {
                    ModelState.AddModelError("ApplicationDate", Resource.MsgEnterADateBeforeContinuing);
                }
                if (model.ApplicationDate != null)
                {
                    if (model.ApplicationDate.Value.Date.Year > model.HarvestYear + 2 || model.ApplicationDate.Value.Date.Year < model.HarvestYear - 2)
                    {
                        ModelState.AddModelError("ApplicationDate", Resource.MsgEnterADateWithin2YearsOfTheHarvestYear);
                    }
                }

                DateTime minDate = new DateTime(model.HarvestYear.Value - 1, 8, 01);
                DateTime maxDate = new DateTime(model.HarvestYear.Value, 7, 31);

                if (model.ApplicationDate > maxDate)
                {
                    ModelState.AddModelError("ApplicationDate", string.Format(Resource.MsgManureApplicationMaxDate, model.HarvestYear.Value, maxDate.Date.ToString("dd MMMM yyyy")));
                }
                if (model.ApplicationDate < minDate)
                {
                    ModelState.AddModelError("ApplicationDate", string.Format(Resource.MsgManureApplicationMinDate, model.HarvestYear.Value, minDate.Date.ToString("dd MMMM yyyy")));
                }

                if (!ModelState.IsValid)
                {

                    List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                    int cropTypeId = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                    CropTypeLinkingResponse cropTypeLinkingResponse = new CropTypeLinkingResponse();
                    (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(cropTypeId);

                    string formattedStartDate = model.ClosedPeriodStartDate?.ToString("d MMMM yyyy");
                    string formattedEndDate = model.ClosedPeriodEndDate?.ToString("d MMMM yyyy");
                    //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                    if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                    {
                        model.ClosedPeriodForUI = $"{formattedStartDate} to {formattedEndDate}";
                    }

                    //model.ClosedPeriodForUI = $"{formattedStartDate} to {formattedEndDate}";
                    return View(model);
                }

                //check for closed period warning.
                OrganicManureViewModel organicManureViewModel = new OrganicManureViewModel();
                if (model != null)
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }

                    if (model.ApplicationDate != organicManureViewModel.ApplicationDate)
                    {
                        model.IsWarningMsgNeedToShow = false;
                        model.IsApplicationDateChange = true;
                    }
                }
                model.IsClosedPeriodWarning = false;
                model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks = false;
                model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks = false;
                if (int.TryParse(model.FieldGroup, out int value) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count == 1))
                {
                    farmId = Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId));
                    (farm, error) = await _farmService.FetchFarmByIdAsync(farmId);
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        TempData["ManureApplyingDateError"] = error.Message;
                        return View(model);
                    }
                    else
                    {
                        if (farm != null)
                        {
                            bool nonRegisteredOrganicProducer = farm.RegisteredOrganicProducer.Value;
                            if (model.FieldList != null && model.FieldList.Count > 0)
                            {
                                foreach (var fieldId in model.FieldList)
                                {
                                    Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                                    if (field != null)
                                    {

                                        if (field.IsWithinNVZ.Value)
                                        {
                                            CropTypeLinkingResponse cropTypeLinkingResponse = new CropTypeLinkingResponse();
                                            if (!(model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials))
                                            {
                                                //skip elosed period warning message for crop which has 0 NMax
                                                Crop crop = null;
                                                if (model.OrganicManures.Any(x => x.FieldID == Convert.ToInt32(fieldId)))
                                                {
                                                    int manId = model.OrganicManures.Where(x => x.FieldID == Convert.ToInt32(fieldId)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                                    (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID ?? 0);
                                                }
                                                //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                                                if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                                                {
                                                    (model, error) = await IsClosedPeriodWarningMessage(model, field.IsWithinNVZ.Value, farm.RegisteredOrganicProducer.Value, false);
                                                }


                                            }

                                        }
                                    }
                                }
                            }
                        }
                    }

                }

                if (model.IsClosedPeriodWarning || model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks)
                {
                    if (!model.IsWarningMsgNeedToShow)
                    {
                        model.IsWarningMsgNeedToShow = true;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return View(model);
                    }
                }
                else
                {
                    model.IsWarningMsgNeedToShow = false;
                    model.IsClosedPeriodWarning = false;
                    model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks = false;
                }

                if (model.OrganicManures.Count > 0)
                {
                    foreach (var orgManure in model.OrganicManures)
                    {
                        orgManure.ApplicationDate = model.ApplicationDate.Value;
                    }
                }
                //model.IsWarningMsgNeedToShow = false;


                if (model.IsCheckAnswer && (!model.IsManureTypeChange) && (!model.IsFieldGroupChange) && (!model.IsAnyChangeInField))
                {
                    if (model.IsApplicationDateChange.HasValue && model.IsApplicationDateChange.Value)
                    {
                        model.MoistureType = null;
                        model.SoilDrainageEndDate = null;
                        model.TotalRainfall = null;
                        model.AutumnCropNitrogenUptake = null;
                        model.AutumnCropNitrogenUptakes = null;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("ConditionsAffectingNutrients");
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("CheckAnswer");
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("ApplicationMethod");
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManureApplyingDate() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return View(model);
            }

        }

        private async Task PopulateManureApplyingDateModel(OrganicManureViewModel model)
        {
            int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
            List<ManureType> manureTypeList = new List<ManureType>();
            Error error = null;
            if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
            {
                (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
            }
            else
            {
                (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
            }
            model.ManureTypeName = (error == null && manureTypeList.Count > 0) ? manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.Name : string.Empty;

            int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId));
            (Farm farm, Error farmError) = await _farmService.FetchFarmByIdAsync(farmId);
            if (farmError != null && !string.IsNullOrWhiteSpace(farmError.Message))
            {
                TempData["Error"] = farmError.Message;
            }

            bool isHighReadilyAvailableNitrogen = false;
            if (error == null && manureTypeList.Count > 0)
            {
                var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                model.ManureTypeName = manureType?.Name;
                isHighReadilyAvailableNitrogen = manureType?.HighReadilyAvailableNitrogen ?? false;
                ViewBag.HighReadilyAvailableNitrogen = manureType?.HighReadilyAvailableNitrogen;
            }

            if (farm != null)
            {
                (FieldDetailResponse fieldDetail, Error fieldError) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(
                    Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);

                WarningWithinPeriod warningMessage = new WarningWithinPeriod();
                string closedPeriod = string.Empty;
                bool isPerennial = false;

                if (farm.RegisteredOrganicProducer == false && isHighReadilyAvailableNitrogen)
                {
                    (CropTypeResponse cropTypeResponse, Error cropTypeError) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(
                        Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);

                    if (cropTypeError == null)
                    {
                        isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeResponse.CropTypeId);
                    }
                    closedPeriod = warningMessage.ClosedPeriodNonOrganicFarm(fieldDetail, model.HarvestYear ?? 0, isPerennial);
                }
                else
                {
                    List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                    int cropTypeId = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                    isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeId);
                    int? cropInfo1 = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropInfo1).FirstOrDefault();

                    closedPeriod = warningMessage.ClosedPeriodOrganicFarm(fieldDetail, model.HarvestYear ?? 0, cropTypeId, cropInfo1, isPerennial);
                }

                ViewBag.ClosedPeriod = closedPeriod;
            }
        }

        [HttpGet]
        public async Task<IActionResult> ApplicationMethod()
        {
            _logger.LogTrace($"Organic Manure Controller : ApplicationMethod() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                bool isLiquid = false;
                if (error == null && manureTypeList.Count > 0)
                {
                    var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                    model.ManureTypeName = manureType?.Name;
                    isLiquid = manureType.IsLiquid.Value;

                }
                else
                {
                    model.ManureTypeName = string.Empty;
                }
                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();

                (List<ApplicationMethodResponse> applicationMethodList, error) = await _organicManureService.FetchApplicationMethodList(fieldType ?? 0, isLiquid);
                if (error == null && applicationMethodList.Count > 0)
                {
                    ViewBag.ApplicationMethodList = applicationMethodList.OrderBy(a => a.SortOrder).ToList();
                }

                model.ApplicationMethodCount = applicationMethodList.Count;
                if (applicationMethodList.Count == 1)
                {
                    model.ApplicationMethod = applicationMethodList[0].ID;
                    (model.ApplicationMethodName, error) = await _organicManureService.FetchApplicationMethodById(model.ApplicationMethod.Value);
                    if (error != null)
                    {
                        TempData["ManureApplyingDateError"] = error.Message;
                        return RedirectToAction("ManureApplyingDate", model);
                    }
                    if (model.OrganicManures.Count > 0)
                    {
                        foreach (var orgManure in model.OrganicManures)
                        {
                            orgManure.ApplicationMethodID = model.ApplicationMethod.Value;
                        }
                    }
                    if (model.IsCheckAnswer)
                    {
                        OrganicManureViewModel organicManureViewModel = new OrganicManureViewModel();
                        if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                        {
                            organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                        }
                        else
                        {
                            return RedirectToAction("FarmList", "Farm");
                        }
                        if ((organicManureViewModel.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.DeepInjection2530cm) || (organicManureViewModel.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.ShallowInjection57cm))
                        {
                            model.IncorporationDelay = null;
                            model.IncorporationMethod = null;
                            model.IncorporationDelayName = string.Empty;
                            model.IncorporationMethodName = string.Empty;
                            foreach (var orgManure in model.OrganicManures)
                            {
                                orgManure.IncorporationDelayID = null;
                                orgManure.IncorporationMethodID = null;
                            }
                        }
                        if (!(model.IsFieldGroupChange) && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                        {
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            return RedirectToAction("CheckAnswer");
                        }
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);

                    if (model.IsDefaultNutrient.Value)
                    {
                        return RedirectToAction("ManureApplyingDate");
                    }

                    return RedirectToAction("DefaultNutrientValues");
                }

                if (model.IsCheckAnswer && (!model.IsFieldGroupChange) && (!model.IsManureTypeChange))
                {
                    model.IsApplicationMethodChange = true;
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ApplicationMethod() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return RedirectToAction("ManureApplyingDate");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplicationMethod(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ApplicationMethod() post action called");
            Error error = null;
            if (model.ApplicationMethod == null)
            {
                ModelState.AddModelError("ApplicationMethod", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            try
            {
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                bool isLiquid = false;
                if (!ModelState.IsValid)
                {
                    if (error == null && manureTypeList.Count > 0)
                    {
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        model.ManureTypeName = manureType?.Name;
                        isLiquid = manureType.IsLiquid.Value;

                    }
                    else
                    {
                        model.ManureTypeName = string.Empty;
                    }
                    List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                    var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();


                    (List<ApplicationMethodResponse> applicationMethodList, error) = await _organicManureService.FetchApplicationMethodList(fieldType ?? 0, isLiquid);
                    ViewBag.ApplicationMethodList = applicationMethodList.OrderBy(a => a.SortOrder).ToList();
                    model.ApplicationMethodCount = applicationMethodList.Count;
                    return View(model);
                }

                if (model.OrganicManures.Count > 0)
                {
                    foreach (var orgManure in model.OrganicManures)
                    {
                        orgManure.ApplicationMethodID = model.ApplicationMethod.Value;
                    }
                }
                (model.ApplicationMethodName, error) = await _organicManureService.FetchApplicationMethodById(model.ApplicationMethod.Value);

                if ((model.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.DeepInjection2530cm) || (model.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.ShallowInjection57cm))
                {
                    if (manureTypeList.Count > 0)
                    {
                        string applicableFor = Resource.lblNull;
                        List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                        var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();

                        (List<IncorporationMethodResponse> incorporationMethods, error) = await _organicManureService.FetchIncorporationMethodsByApplicationId(model.ApplicationMethod.Value, applicableFor);
                        if (error == null && incorporationMethods.Count == 1)
                        {
                            model.IncorporationMethod = incorporationMethods.FirstOrDefault().ID;
                            (model.IncorporationMethodName, error) = await _organicManureService.FetchIncorporationMethodById(model.IncorporationMethod.Value);
                            if (error == null)
                            {
                                (List<IncorprationDelaysResponse> incorporationDelaysList, error) = await _organicManureService.FetchIncorporationDelaysByMethodIdAndApplicableFor(model.IncorporationMethod ?? 0, applicableFor);
                                if (error == null && incorporationDelaysList.Count == 1)
                                {
                                    model.IncorporationDelay = incorporationDelaysList.FirstOrDefault().ID;
                                    (model.IncorporationDelayName, error) = await _organicManureService.FetchIncorporationDelayById(model.IncorporationDelay.Value);
                                    if (error == null)
                                    {
                                        if (model.OrganicManures.Count > 0)
                                        {
                                            foreach (var orgManure in model.OrganicManures)
                                            {
                                                orgManure.IncorporationMethodID = model.IncorporationMethod.Value;
                                                orgManure.IncorporationDelayID = model.IncorporationDelay.Value;
                                            }
                                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                            if (model.IsCheckAnswer && model.IsApplicationMethodChange && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                                            {
                                                return RedirectToAction("CheckAnswer");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        TempData["ApplicationMethodError"] = error.Message;
                                        return View(model);
                                    }
                                }
                                else
                                {
                                    TempData["ApplicationMethodError"] = error.Message;
                                    return View(model);
                                }
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            }
                            else if (error != null)
                            {
                                TempData["ApplicationMethodError"] = error.Message;
                                return View(model);
                            }
                        }
                        else if (error != null)
                        {
                            TempData["ApplicationMethodError"] = error.Message;
                            return View(model);
                        }
                    }
                }
                else
                {
                    OrganicManureViewModel organicManureViewModel = new OrganicManureViewModel();
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if ((organicManureViewModel.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.DeepInjection2530cm) || (organicManureViewModel.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.ShallowInjection57cm))
                    {
                        model.IncorporationDelay = null;
                        model.IncorporationMethod = null;
                        model.IncorporationDelayName = string.Empty;
                        model.IncorporationMethodName = string.Empty;
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                if (model.IsCheckAnswer && model.IsApplicationMethodChange)
                {
                    return RedirectToAction("IncorporationMethod");
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ApplicationMethod() post action : {ex.Message}, {ex.StackTrace}");
                TempData["ApplicationMethodError"] = ex.Message;
                return ViewBag(model);
            }
            return RedirectToAction("DefaultNutrientValues");
        }

        [HttpGet]
        public async Task<IActionResult> DefaultNutrientValues()
        {
            _logger.LogTrace($"Organic Manure Controller : DefaultNutrientValues() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            try
            {
                if (model.IsCheckAnswer && (!model.IsApplicationMethodChange) && (!model.IsFieldGroupChange)
                    && (!model.IsManureTypeChange) && (!model.IsIncorporationMethodChange))
                {
                    model.IsDefaultNutrientOptionChange = true;
                }
                Error? error = null;
                FarmManureTypeResponse? farmManure = null;

                (List<FarmManureTypeResponse> farmManureTypeList, error) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);

                if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    {
                        (ManureType manureType, Error manureTypeError) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                        model.ManureType = manureType;
                        // (List<FarmManureTypeResponse> farmManureTypeList, Error error1) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                        if (error == null)
                        {
                            if (farmManureTypeList.Count > 0)
                            {
                                farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureGroupIdForFilter);
                                if (farmManure != null)
                                {
                                    model.ManureType.DryMatter = farmManure.DryMatter;
                                    model.ManureType.TotalN = farmManure.TotalN;
                                    model.ManureType.NH4N = farmManure.NH4N;
                                    model.ManureType.Uric = farmManure.Uric;
                                    model.ManureType.NO3N = farmManure.NO3N;
                                    model.ManureType.P2O5 = farmManure.P2O5;
                                    model.ManureType.K2O = farmManure.K2O;
                                    model.ManureType.SO3 = farmManure.SO3;
                                    model.ManureType.MgO = farmManure.MgO;
                                    model.DefaultFarmManureValueDate = farmManure.ModifiedOn == null ? farmManure.CreatedOn : farmManure.ModifiedOn;
                                }
                                else
                                {
                                    model.DefaultFarmManureValueDate = null;
                                }
                            }
                        }
                        if (manureTypeError == null)
                        {
                            model.ManureType = manureType;
                        }
                        model.IsDefaultNutrient = true;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    }
                    else
                    {
                        model.DefaultNutrientValue = Resource.lblIwantToEnterARecentOrganicMaterialAnalysis;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("ManualNutrientValues");
                    }
                }
                else
                {
                    (ManureType manureType, Error manureTypeError) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);

                    if (error == null && farmManureTypeList.Count > 0)//&&(string.IsNullOrWhiteSpace(model.DefaultNutrientValue) || model.DefaultNutrientValue== Resource.lblYesUseTheseValues))
                    {
                        farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureTypeId);
                        if (model.IsDefaultValueChange)
                        {
                            model.IsDefaultValueChange = false;
                            if (farmManure != null)
                            {
                                model.ManureType.DryMatter = farmManure.DryMatter;
                                model.ManureType.TotalN = farmManure.TotalN;
                                model.ManureType.NH4N = farmManure.NH4N;
                                model.ManureType.Uric = farmManure.Uric;
                                model.ManureType.NO3N = farmManure.NO3N;
                                model.ManureType.P2O5 = farmManure.P2O5;
                                model.ManureType.K2O = farmManure.K2O;
                                model.ManureType.SO3 = farmManure.SO3;
                                model.ManureType.MgO = farmManure.MgO;
                                ViewBag.FarmManureApiOption = Resource.lblTrue;
                                model.DefaultFarmManureValueDate = farmManure.ModifiedOn == null ? farmManure.CreatedOn : farmManure.ModifiedOn;
                            }
                            else
                            {
                                if (manureTypeError == null)
                                {
                                    model.ManureType = manureType;
                                }
                            }
                        }
                        else
                        {
                            if (farmManure != null)
                            {
                                model.DefaultFarmManureValueDate = farmManure.ModifiedOn == null ? farmManure.CreatedOn : farmManure.ModifiedOn;
                                ViewBag.FarmManureApiOption = Resource.lblTrue;
                                if ((!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblYesUseTheseValues) || (model.IsThisDefaultValueOfRB209 != null && (!model.IsThisDefaultValueOfRB209.Value)))
                                {
                                    ViewBag.FarmManureApiOption = Resource.lblTrue;
                                }
                                else if ((!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues) || (model.IsThisDefaultValueOfRB209 != null && (model.IsThisDefaultValueOfRB209.Value)))
                                {
                                    ViewBag.FarmManureApiOption = null;
                                    ViewBag.RB209ApiOption = Resource.lblTrue;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (manureTypeError == null)
                        {
                            model.ManureType = manureType;
                        }
                    }
                }

                model.IsDefaultNutrient = true;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);

            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View(model);
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DefaultNutrientValues(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : DefaultNutrientValues() post action called");
            if (model.DefaultNutrientValue == null)
            {
                ModelState.AddModelError("DefaultNutrientValue", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            try
            {
                if (!ModelState.IsValid)
                {
                    Error? error = null;
                    FarmManureTypeResponse? farmManure = null;

                    (List<FarmManureTypeResponse> farmManureTypeList, error) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);

                    if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    {
                        if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                        {
                            (ManureType manureType, Error manureTypeError) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                            model.ManureType = manureType;
                            // (List<FarmManureTypeResponse> farmManureTypeList, Error error1) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                            if (error == null)
                            {
                                if (farmManureTypeList.Count > 0)
                                {
                                    farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureGroupIdForFilter);
                                    model.ManureType.DryMatter = farmManure.DryMatter;
                                    model.ManureType.TotalN = farmManure.TotalN;
                                    model.ManureType.NH4N = farmManure.NH4N;
                                    model.ManureType.Uric = farmManure.Uric;
                                    model.ManureType.NO3N = farmManure.NO3N;
                                    model.ManureType.P2O5 = farmManure.P2O5;
                                    model.ManureType.K2O = farmManure.K2O;
                                    model.ManureType.SO3 = farmManure.SO3;
                                    model.ManureType.MgO = farmManure.MgO;
                                }
                            }
                            if (manureTypeError == null)
                            {
                                model.ManureType = manureType;
                            }
                            model.IsDefaultNutrient = true;

                        }
                        else
                        {
                            model.DefaultNutrientValue = Resource.lblIwantToEnterARecentOrganicMaterialAnalysis;

                        }
                    }
                    else
                    {
                        (ManureType manureType, Error manureTypeError) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);

                        if (error == null && farmManureTypeList.Count > 0)//&&(string.IsNullOrWhiteSpace(model.DefaultNutrientValue) || model.DefaultNutrientValue== Resource.lblYesUseTheseValues))
                        {
                            farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureTypeId);
                            if (model.IsDefaultValueChange)
                            {
                                model.IsDefaultValueChange = false;
                                if (farmManure != null)
                                {
                                    model.ManureType.DryMatter = farmManure.DryMatter;
                                    model.ManureType.TotalN = farmManure.TotalN;
                                    model.ManureType.NH4N = farmManure.NH4N;
                                    model.ManureType.Uric = farmManure.Uric;
                                    model.ManureType.NO3N = farmManure.NO3N;
                                    model.ManureType.P2O5 = farmManure.P2O5;
                                    model.ManureType.K2O = farmManure.K2O;
                                    model.ManureType.SO3 = farmManure.SO3;
                                    model.ManureType.MgO = farmManure.MgO;
                                    ViewBag.FarmManureApiOption = Resource.lblTrue;
                                    model.DefaultFarmManureValueDate = farmManure.ModifiedOn == null ? farmManure.CreatedOn : farmManure.ModifiedOn;
                                }
                                else
                                {
                                    if (manureTypeError == null)
                                    {
                                        model.ManureType = manureType;
                                    }
                                }
                            }
                            else
                            {
                                if (farmManure != null)
                                {
                                    if ((!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblYesUseTheseValues) || (model.IsThisDefaultValueOfRB209 != null && (!model.IsThisDefaultValueOfRB209.Value)))
                                    {
                                        ViewBag.FarmManureApiOption = Resource.lblTrue;
                                    }
                                    else if ((!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues) || (model.IsThisDefaultValueOfRB209 != null && (model.IsThisDefaultValueOfRB209.Value)))
                                    {
                                        ViewBag.RB209ApiOption = Resource.lblTrue;
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (manureTypeError == null)
                            {
                                model.ManureType = manureType;
                            }
                        }
                    }



                    //(ManureType manureType, Error manureTypeError) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                    //FarmManureTypeResponse? farmManure = null;

                    //(List<FarmManureTypeResponse> farmManureTypeList, Error error) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);

                    //if (error == null && farmManureTypeList.Count > 0)
                    //{
                    //    farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureTypeId);
                    //    if (string.IsNullOrWhiteSpace(model.DefaultNutrientValue))
                    //    {
                    //        if (farmManure != null)
                    //        {
                    //            model.ManureType.DryMatter = farmManure.DryMatter;
                    //            model.ManureType.TotalN = farmManure.TotalN;
                    //            model.ManureType.NH4N = farmManure.NH4N;
                    //            model.ManureType.Uric = farmManure.Uric;
                    //            model.ManureType.NO3N = farmManure.NO3N;
                    //            model.ManureType.P2O5 = farmManure.P2O5;
                    //            model.ManureType.K2O = farmManure.K2O;
                    //            model.ManureType.SO3 = farmManure.SO3;
                    //            model.ManureType.MgO = farmManure.MgO;
                    //            if (model.ManureTypeId != (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials && model.ManureTypeId != (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    //            {
                    //                ViewBag.FarmManureApiOption = Resource.lblTrue;
                    //            }
                    //        }
                    //        else
                    //        {
                    //            if (manureTypeError == null)
                    //            {
                    //                model.ManureType = manureType;
                    //            }
                    //            model.DefaultNutrientValue = Resource.lblYes;
                    //        }
                    //    }
                    //}
                    //else
                    //{
                    //    if (manureTypeError == null)
                    //    {
                    //        model.ManureType = manureType;
                    //    }

                    //}
                    return View(model);
                }
                if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblIwantToEnterARecentOrganicMaterialAnalysis)
                {
                    if (model.DryMatterPercent == null)
                    {
                        model.DryMatterPercent = model.ManureType.DryMatter;
                        model.N = model.ManureType.TotalN;
                        model.P2O5 = model.ManureType.P2O5;
                        model.NH4N = model.ManureType.NH4N;
                        model.UricAcid = model.ManureType.Uric;
                        model.SO3 = model.ManureType.SO3;
                        model.K2O = model.ManureType.K2O;
                        model.MgO = model.ManureType.MgO;
                        model.NO3N = model.ManureType.NO3N;
                    }

                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("ManualNutrientValues");
                }
                else
                {

                    model.DryMatterPercent = null;
                    model.N = null;
                    model.P2O5 = null;
                    model.NH4N = null;
                    model.UricAcid = null;
                    model.SO3 = null;
                    model.K2O = null;
                    model.MgO = null;
                    model.NO3N = null;
                    OrganicManureViewModel organicManureViewModel = new OrganicManureViewModel();
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }

                    if (organicManureViewModel != null && (!string.IsNullOrWhiteSpace(organicManureViewModel.DefaultNutrientValue)))
                    {
                        if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && (model.DefaultNutrientValue == Resource.lblYesUseTheseValues || model.DefaultNutrientValue == Resource.lblYes))
                        {
                            (List<FarmManureTypeResponse> farmManureTypeList, Error error1) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                            if (error1 == null && farmManureTypeList.Count > 0)
                            {
                                FarmManureTypeResponse farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureTypeId);

                                if (farmManure != null)
                                {
                                    model.ManureType.DryMatter = farmManure.DryMatter;
                                    model.ManureType.TotalN = farmManure.TotalN;
                                    model.ManureType.NH4N = farmManure.NH4N;
                                    model.ManureType.Uric = farmManure.Uric;
                                    model.ManureType.NO3N = farmManure.NO3N;
                                    model.ManureType.P2O5 = farmManure.P2O5;
                                    model.ManureType.K2O = farmManure.K2O;
                                    model.ManureType.SO3 = farmManure.SO3;
                                    model.ManureType.MgO = farmManure.MgO;
                                }

                                model.IsThisDefaultValueOfRB209 = false;
                                if (organicManureViewModel.DefaultNutrientValue != model.DefaultNutrientValue && model.DefaultNutrientValue == Resource.lblYesUseTheseValues)
                                {
                                    if (farmManure != null)
                                    {
                                        ViewBag.FarmManureApiOption = Resource.lblTrue;
                                    }
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                    if (organicManureViewModel.DefaultNutrientValue != model.DefaultNutrientValue && (organicManureViewModel.DefaultNutrientValue != Resource.lblIwantToEnterARecentOrganicMaterialAnalysis || organicManureViewModel.DefaultNutrientValue != Resource.lblYesUseTheseStandardNutrientValues)
                                        && model.DefaultNutrientValue == Resource.lblYesUseTheseValues)
                                    {
                                        return View(model);
                                    }
                                }
                            }
                        }
                        else
                        {
                            (ManureType manureType, Error error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                            model.ManureType = manureType;

                            model.IsThisDefaultValueOfRB209 = true;
                            if (organicManureViewModel.DefaultNutrientValue != model.DefaultNutrientValue && model.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues)
                            {
                                ViewBag.RB209ApiOption = Resource.lblTrue;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                if (organicManureViewModel.DefaultNutrientValue != model.DefaultNutrientValue && (organicManureViewModel.DefaultNutrientValue != Resource.lblIwantToEnterARecentOrganicMaterialAnalysis || organicManureViewModel.DefaultNutrientValue != Resource.lblYesUseTheseValues)
                                      && model.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues)
                                {
                                    return View(model);
                                }

                            }
                            if (organicManureViewModel.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues && model.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues)
                            {
                                ViewBag.RB209ApiOption = Resource.lblTrue;
                            }
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && (model.DefaultNutrientValue == Resource.lblYesUseTheseValues || model.DefaultNutrientValue == Resource.lblYes))
                        {

                            (List<FarmManureTypeResponse> farmManureTypeList, Error error1) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId ?? 0);
                            if (error1 == null && farmManureTypeList.Count > 0)
                            {
                                FarmManureTypeResponse farmManure = farmManureTypeList.FirstOrDefault(x => x.ManureTypeID == model.ManureTypeId);

                                if (farmManure != null)
                                {
                                    model.ManureType.DryMatter = farmManure.DryMatter;
                                    model.ManureType.TotalN = farmManure.TotalN;
                                    model.ManureType.NH4N = farmManure.NH4N;
                                    model.ManureType.Uric = farmManure.Uric;
                                    model.ManureType.NO3N = farmManure.NO3N;
                                    model.ManureType.P2O5 = farmManure.P2O5;
                                    model.ManureType.K2O = farmManure.K2O;
                                    model.ManureType.SO3 = farmManure.SO3;
                                    model.ManureType.MgO = farmManure.MgO;

                                }
                                if (model.DefaultNutrientValue == Resource.lblYesUseTheseValues)
                                {
                                    model.IsThisDefaultValueOfRB209 = false;
                                    ViewBag.FarmManureApiOption = Resource.lblTrue;
                                }
                            }
                        }
                        else
                        {
                            (ManureType manureType, Error error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                            model.ManureType = manureType;
                            if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblYesUseTheseStandardNutrientValues)
                            {
                                model.IsThisDefaultValueOfRB209 = true;
                                ViewBag.RB209ApiOption = Resource.lblTrue;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                return View(model);
                            }

                        }
                    }
                    if (model.OrganicManures != null && model.OrganicManures.Count > 0)
                    {
                        foreach (var orgManure in model.OrganicManures)
                        {
                            orgManure.DryMatterPercent = model.ManureType.DryMatter;
                            orgManure.N = model.ManureType.TotalN;
                            orgManure.NH4N = model.ManureType.NH4N;
                            orgManure.UricAcid = model.ManureType.Uric;
                            orgManure.NO3N = model.ManureType.NO3N;
                            orgManure.P2O5 = model.ManureType.P2O5;
                            orgManure.K2O = model.ManureType.K2O;
                            orgManure.SO3 = model.ManureType.SO3;
                            orgManure.MgO = model.ManureType.MgO;
                        }
                    }
                    //model.IsAnyNeedToStoreNutrientValueForFuture = true;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    if (model.IsCheckAnswer && model.IsDefaultNutrientOptionChange && (!model.IsApplicationMethodChange) && (!model.IsFieldGroupChange)
                    && (!model.IsManureTypeChange) && (!model.IsIncorporationMethodChange) && (!model.IsAnyChangeInField))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in DefaultNutrientValues() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return View(model);
            }
            return RedirectToAction("ApplicationRateMethod");
        }

        [HttpGet]
        public async Task<IActionResult> ManualNutrientValues()
        {
            _logger.LogTrace($"Organic Manure Controller : ManualNutrientValues() post action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManualNutrientValues(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ManualNutrientValues() post action called");
            try
            {
                if ((!ModelState.IsValid) && ModelState.ContainsKey("DryMatterPercent"))
                {
                    var dryMatterPercentError = ModelState["DryMatterPercent"].Errors.Count > 0 ?
                                    ModelState["DryMatterPercent"].Errors[0].ErrorMessage.ToString() : null;

                    if (dryMatterPercentError != null && dryMatterPercentError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["DryMatterPercent"].RawValue, Resource.lblDryMatterPercent)))
                    {
                        ModelState["DryMatterPercent"].Errors.Clear();
                        ModelState["DryMatterPercent"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblDryMatter));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("N"))
                {
                    var totalNitrogenError = ModelState["N"].Errors.Count > 0 ?
                                    ModelState["N"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalNitrogenError != null && totalNitrogenError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["N"].RawValue, Resource.lblN)))
                    {
                        ModelState["N"].Errors.Clear();
                        ModelState["N"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblTotalNitrogen));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("NH4N"))
                {
                    var ammoniumError = ModelState["NH4N"].Errors.Count > 0 ?
                                    ModelState["NH4N"].Errors[0].ErrorMessage.ToString() : null;

                    if (ammoniumError != null && ammoniumError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["NH4N"].RawValue, Resource.lblNH4N)))
                    {
                        ModelState["NH4N"].Errors.Clear();
                        ModelState["NH4N"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblAmmonium));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("UricAcid"))
                {
                    var uricAcidError = ModelState["UricAcid"].Errors.Count > 0 ?
                                    ModelState["UricAcid"].Errors[0].ErrorMessage.ToString() : null;

                    if (uricAcidError != null && uricAcidError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["UricAcid"].RawValue, Resource.lblUricAcidForError)))
                    {
                        ModelState["UricAcid"].Errors.Clear();
                        ModelState["UricAcid"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblUricAcid));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("NO3N"))
                {
                    var nitrogenError = ModelState["NO3N"].Errors.Count > 0 ?
                                    ModelState["NO3N"].Errors[0].ErrorMessage.ToString() : null;

                    if (nitrogenError != null && nitrogenError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["NO3N"].RawValue, Resource.lblNO3N)))
                    {
                        ModelState["NO3N"].Errors.Clear();
                        ModelState["NO3N"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblNitrogen));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("P2O5"))
                {
                    var totalPhosphateError = ModelState["P2O5"].Errors.Count > 0 ?
                                    ModelState["P2O5"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalPhosphateError != null && totalPhosphateError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["P2O5"].RawValue, Resource.lblP2O5)))
                    {
                        ModelState["P2O5"].Errors.Clear();
                        ModelState["P2O5"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblTotalPhosphate));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("K2O"))
                {
                    var totalPotassiumError = ModelState["K2O"].Errors.Count > 0 ?
                                    ModelState["K2O"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalPotassiumError != null && totalPotassiumError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["K2O"].RawValue, Resource.lblK2O)))
                    {
                        ModelState["K2O"].Errors.Clear();
                        ModelState["K2O"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblTotalPotassium));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("SO3"))
                {
                    var sulphurSO3Error = ModelState["SO3"].Errors.Count > 0 ?
                                    ModelState["SO3"].Errors[0].ErrorMessage.ToString() : null;

                    if (sulphurSO3Error != null && sulphurSO3Error.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SO3"].RawValue, Resource.lblSO3)))
                    {
                        ModelState["SO3"].Errors.Clear();
                        ModelState["SO3"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblTotalSulphur));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("MgO"))
                {
                    var totalMagnesiumOxideError = ModelState["MgO"].Errors.Count > 0 ?
                                    ModelState["MgO"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalMagnesiumOxideError != null && totalMagnesiumOxideError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["MgO"].RawValue, Resource.lblMgO)))
                    {
                        ModelState["MgO"].Errors.Clear();
                        ModelState["MgO"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblMagnesiumMgO));
                    }
                }
                if (model.DryMatterPercent == null)
                {
                    ModelState.AddModelError("DryMatterPercent", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblDryMatter.ToLower()));
                }
                if (model.N == null)
                {
                    ModelState.AddModelError("N", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblTotalNitrogen.ToLower()));
                }
                if (model.NH4N == null)
                {
                    ModelState.AddModelError("NH4N", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblAmmoniumForError));
                }
                if (model.UricAcid == null)
                {
                    ModelState.AddModelError("UricAcid", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.MsgUricAcid));
                }
                if (model.NO3N == null)
                {
                    ModelState.AddModelError("NO3N", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblNitrateForErrorMsg));
                }
                if (model.P2O5 == null)
                {
                    ModelState.AddModelError("P2O5", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblPhosphate.ToLower()));
                }
                if (model.K2O == null)
                {
                    ModelState.AddModelError("K2O", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblPotash.ToLower()));
                }
                if (model.SO3 == null)
                {
                    ModelState.AddModelError("SO3", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblSulphur.ToLower()));
                }
                if (model.MgO == null)
                {
                    ModelState.AddModelError("MgO", string.Format(Resource.MsgEnterTheValueBeforeContinuing, Resource.lblMagnesiumMgO.ToLower()));
                }

                if (model.N != null && model.NH4N != null && model.UricAcid != null && model.NO3N != null)
                {
                    decimal totalValue = model.NH4N.Value + model.UricAcid.Value + model.NO3N.Value;
                    if (model.N < totalValue)
                    {
                        ModelState.AddModelError("N", Resource.lblTotalNitrogenMustBeGreaterOrEqualToAmmoniumUricacidNitrate);
                    }
                }

                if (model.DryMatterPercent != null)
                {
                    if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.PigSlurry ||
                        model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.CattleSlurry)
                    {
                        if (model.DryMatterPercent < 0 || model.DryMatterPercent > 25)
                        {
                            ModelState.AddModelError("DryMatterPercent", string.Format(Resource.MsgMinMaxValidation, Resource.lblDryMatter.ToLower(), 25));
                        }
                    }
                    else
                    {
                        if (model.DryMatterPercent < 0 || model.DryMatterPercent > 99)
                        {
                            ModelState.AddModelError("DryMatterPercent", string.Format(Resource.MsgMinMaxValidation, Resource.lblDryMatter, 99));
                        }
                    }
                }

                if (model.N != null)
                {
                    if (model.N < 0 || model.N > 297)
                    {
                        ModelState.AddModelError("N", string.Format(Resource.MsgMinMaxValidation, Resource.lblTotalNitrogenN, 297));
                    }
                }

                if (model.NH4N != null)
                {
                    if (model.NH4N < 0 || model.NH4N > 99)
                    {
                        ModelState.AddModelError("NH4N", string.Format(Resource.MsgMinMaxValidation, Resource.lblAmmonium, 99));
                    }
                }

                if (model.UricAcid != null)
                {
                    if (model.UricAcid < 0 || model.UricAcid > 99)
                    {
                        ModelState.AddModelError("UricAcid", string.Format(Resource.MsgMinMaxValidation, Resource.lblUricAcid, 99));
                    }
                }

                if (model.NO3N != null)
                {
                    if (model.NO3N < 0 || model.NO3N > 99)
                    {
                        ModelState.AddModelError("NO3N", string.Format(Resource.MsgMinMaxValidation, Resource.lblNitrate, 99));
                    }
                }

                if (model.P2O5 != null)
                {
                    if (model.P2O5 < 0 || model.P2O5 > 99)
                    {
                        ModelState.AddModelError("P2O5", string.Format(Resource.MsgMinMaxValidation, Resource.lblPhosphateP2O5, 99));
                    }
                }

                if (model.K2O != null)
                {
                    if (model.K2O < 0 || model.K2O > 99)
                    {
                        ModelState.AddModelError("K2O", string.Format(Resource.MsgMinMaxValidation, Resource.lblPotashK2O, 99));
                    }
                }
                if (model.MgO != null)
                {
                    if (model.MgO < 0 || model.MgO > 99)
                    {
                        ModelState.AddModelError("MgO", string.Format(Resource.MsgMinMaxValidation, Resource.lblMagnesiumMgO, 99));
                    }
                }

                if (model.SO3 != null)
                {
                    if (model.SO3 < 0 || model.SO3 > 99)
                    {
                        ModelState.AddModelError("SO3", string.Format(Resource.MsgMinMaxValidation, Resource.lblSulphurSO3, 99));
                    }
                }

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (model.ManureType.DryMatter != model.DryMatterPercent || model.ManureType.TotalN != model.N
               || model.ManureType.NH4N != model.NH4N || model.ManureType.Uric != model.UricAcid
                || model.ManureType.NO3N != model.NO3N || model.ManureType.P2O5 != model.P2O5 ||
                model.ManureType.K2O != model.K2O || model.ManureType.MgO != model.MgO
                || model.ManureType.SO3 != model.SO3)
                {
                    model.IsAnyNeedToStoreNutrientValueForFuture = true;
                }
                else
                {
                    model.IsAnyNeedToStoreNutrientValueForFuture = false;
                }
                if (model.OrganicManures.Count > 0)
                {
                    foreach (var orgManure in model.OrganicManures)
                    {
                        orgManure.DryMatterPercent = model.DryMatterPercent;
                        orgManure.N = model.N;
                        orgManure.NH4N = model.NH4N;
                        orgManure.UricAcid = model.UricAcid;
                        orgManure.NO3N = model.NO3N;
                        orgManure.P2O5 = model.P2O5;
                        orgManure.K2O = model.K2O;
                        orgManure.SO3 = model.SO3;
                        orgManure.MgO = model.MgO;
                    }
                }

                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                if (model.IsCheckAnswer && model.IsDefaultNutrientOptionChange && (!model.IsApplicationMethodChange) && (!model.IsFieldGroupChange)
                && (!model.IsManureTypeChange) && (!model.IsIncorporationMethodChange) && (!model.IsAnyChangeInField))
                {
                    return RedirectToAction("CheckAnswer");
                }

                return RedirectToAction("ApplicationRateMethod");
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManualNutrientValues() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return View(model);
            }

        }
        [HttpGet]
        public async Task<IActionResult> NutrientValuesStoreForFuture()
        {
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
            {
                model.IsAnyNeedToStoreNutrientValueForFuture = true;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("ApplicationRateMethod");
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NutrientValuesStoreForFuture(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : NutrientValuesStoreForFuture() post action called");
            if (model.IsAnyNeedToStoreNutrientValueForFuture == null)
            {
                ModelState.AddModelError("IsAnyNeedToStoreNutrientValueForFuture", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.IsCheckAnswer && model.IsDefaultNutrientOptionChange && (!model.IsApplicationMethodChange) && (!model.IsFieldGroupChange)
               && (!model.IsManureTypeChange) && (!model.IsIncorporationMethodChange) && (!model.IsAnyChangeInField))
            {
                return RedirectToAction("CheckAnswer");
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ApplicationRateMethod");
        }

        [HttpGet]
        public async Task<IActionResult> ApplicationRateMethod()
        {
            _logger.LogTrace($"Organic Manure Controller : ApplicationRateMethod() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            try
            {
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                if (model.ManureTypeId != (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials && model.ManureTypeId != (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    List<ManureType> manureTypeList = new List<ManureType>();
                    Error error = null;
                    if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    {
                        (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                    }
                    else
                    {
                        (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                    }
                    if (error == null && manureTypeList.Count > 0)
                    {
                        model.ManureTypeName = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.Name;
                        model.ApplicationRateArable = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.ApplicationRateArable;
                    }
                    else
                    {
                        model.ManureTypeName = string.Empty;
                        ViewBag.Error = error.Message;
                    }
                }


                (List<CommonResponse> manureGroupList, Error error1) = await _organicManureService.FetchManureGroupList();
                model.ManureGroupName = (error1 == null && manureGroupList.Count > 0) ? manureGroupList.FirstOrDefault(x => x.Id == model.ManureGroupId)?.Name : string.Empty;
                if (error1 != null && (!string.IsNullOrWhiteSpace(error1.Message)))
                {
                    ViewBag.Error = error1.Message;
                }
                model.IsWarningMsgNeedToShow = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ApplicationRateMethod() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return RedirectToAction("DefaultNutrientValues");
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplicationRateMethod(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ApplicationRateMethod() post action called");
            try
            {
                Error error = null;
                if (model.ApplicationRateMethod == null)
                {
                    ModelState.AddModelError("ApplicationRateMethod", Resource.MsgSelectAnOptionBeforeContinuing);
                }
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                if (!ModelState.IsValid)
                {
                    if (error == null && manureTypeList.Count > 0)
                    {
                        model.ManureTypeName = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.Name;
                        model.ApplicationRateArable = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.ApplicationRateArable;
                    }
                    else
                    {
                        model.ManureTypeName = string.Empty;
                    }

                    (List<CommonResponse> manureGroupList, Error error1) = await _organicManureService.FetchManureGroupList();
                    model.ManureGroupName = (error1 == null && manureGroupList.Count > 0) ? manureGroupList.FirstOrDefault(x => x.Id == model.ManureGroupId)?.Name : string.Empty;
                    return View("ApplicationRateMethod", model);
                }

                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);

                if (model.ApplicationRateMethod.Value == (int)NMP.Portal.Enums.ApplicationRate.EnterAnApplicationRate)
                {
                    model.Area = null;
                    model.Quantity = null;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("ManualApplicationRate");
                }
                else if (model.ApplicationRateMethod.Value == (int)NMP.Portal.Enums.ApplicationRate.CalculateBasedOnAreaAndQuantity)
                {
                    model.ApplicationRate = null;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("AreaQuantity");
                }
                else if (model.ApplicationRateMethod.Value == (int)NMP.Portal.Enums.ApplicationRate.UseDefaultApplicationRate)
                {
                    model.ApplicationRate = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.ApplicationRateArable;
                    model.Area = null;
                    model.Quantity = null;
                    if (model.OrganicManures.Count > 0)
                    {
                        foreach (var orgManure in model.OrganicManures)
                        {
                            orgManure.AreaSpread = null;
                            orgManure.ManureQuantity = null;
                            orgManure.ApplicationRate = model.ApplicationRate.Value;
                        }
                    }
                    model.IsNMaxLimitWarning = false;
                    model.IsOrgManureNfieldLimitWarning = false;
                    model.IsEndClosedPeriodFebruaryWarning = false;
                    OrganicManureViewModel? organicManureViewModel = new OrganicManureViewModel();
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if (organicManureViewModel != null)
                    {
                        if (model.ApplicationRate != organicManureViewModel.ApplicationRate)
                        {
                            model.IsWarningMsgNeedToShow = false;
                        }
                    }

                    if (model.FieldList != null && model.FieldList.Count > 0)
                    {
                        foreach (var fieldId in model.FieldList)
                        {
                            Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                            if (field != null)
                            {
                                bool isFieldIsInNVZ = field.IsWithinNVZ != null ? field.IsWithinNVZ.Value : false;
                                if (isFieldIsInNVZ)
                                {
                                    (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, null, null);
                                    if (error == null)
                                    {
                                        if (managementIds.Count > 0)
                                        {
                                            (model, error) = await IsNFieldLimitWarningMessage(model, isFieldIsInNVZ, managementIds[0], false, Convert.ToInt32(fieldId));
                                            if (error == null)
                                            {
                                                (model, error) = await IsNMaxWarningMessage(model, Convert.ToInt32(fieldId), managementIds[0], false);
                                                if (error == null)
                                                {
                                                    (model, error) = await IsEndClosedPeriodFebruaryWarningMessage(model, Convert.ToInt32(fieldId), false);

                                                }
                                                else
                                                {
                                                    ViewBag.Error = error.Message;
                                                    return View(model);
                                                }
                                            }
                                            else
                                            {
                                                ViewBag.Error = error.Message;
                                                return View(model);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ViewBag.Error = error.Message;
                                        return View(model);
                                    }
                                }
                            }
                        }
                    }
                }
                if (model.IsOrgManureNfieldLimitWarning || model.IsNMaxLimitWarning || model.IsEndClosedPeriodFebruaryWarning)
                {
                    if (!model.IsWarningMsgNeedToShow)
                    {
                        model.IsWarningMsgNeedToShow = true;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return View(model);
                    }
                }
                else
                {
                    model.IsWarningMsgNeedToShow = false;
                    if (model.IsOrgManureNfieldLimitWarning)
                    {
                        model.IsOrgManureNfieldLimitWarning = false;
                    }
                    if (model.IsNMaxLimitWarning)
                    {
                        model.IsNMaxLimitWarning = false;
                    }
                    if (model.IsEndClosedPeriodFebruaryWarning)
                    {
                        model.IsEndClosedPeriodFebruaryWarning = false;
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                if (model.IsCheckAnswer && (!model.IsManureTypeChange) && (!model.IsFieldGroupChange) && (!model.IsAnyChangeInField))
                {
                    return RedirectToAction("CheckAnswer");
                }
                return RedirectToAction("IncorporationMethod");
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ApplicationRateMethod() post action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ManualApplicationRate()
        {
            _logger.LogTrace($"Organic Manure Controller : ManualApplicationRate() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                Error error = null;
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                model.ManureTypeName = (error == null && manureTypeList.Count > 0) ? manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.Name : string.Empty;

                (List<CommonResponse> manureGroupList, Error error1) = await _organicManureService.FetchManureGroupList();
                model.ManureGroupName = (error1 == null && manureGroupList.Count > 0) ? manureGroupList.FirstOrDefault(x => x.Id == model.ManureGroupId)?.Name : string.Empty;
                model.IsWarningMsgNeedToShow = false;
                model.IsOrgManureNfieldLimitWarning = false;
                model.IsNMaxLimitWarning = false;
                model.IsEndClosedPeriodFebruaryWarning = false;
                model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManualApplicationRate() action : {ex.Message}, {ex.StackTrace}");
                ViewBag.Error = ex.Message;
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManualApplicationRate(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ManualApplicationRate() post action called");
            Error? error = null;
            try
            {
                if ((!ModelState.IsValid) && ModelState.ContainsKey("ApplicationRate"))
                {
                    var applicationRateError = ModelState["ApplicationRate"].Errors.Count > 0 ?
                                    ModelState["ApplicationRate"].Errors[0].ErrorMessage.ToString() : null;

                    if (applicationRateError != null && applicationRateError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["ApplicationRate"].RawValue, Resource.lblApplicationRate)))
                    {
                        ModelState["ApplicationRate"].Errors.Clear();
                        ModelState["ApplicationRate"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.MsgApplicationRate));
                    }
                }

                if (model.ApplicationRate == null)
                {
                    ModelState.AddModelError("ApplicationRate", Resource.MsgEnterAnapplicationRateBeforeContinuing);
                }
                if (model.ApplicationRate != null && model.ApplicationRate < 0)
                {
                    ModelState.AddModelError("ApplicationRate", Resource.MsgEnterANumberWhichIsGreaterThanZero);
                }
                if (model.ApplicationRate != null && model.ApplicationRate > 250)
                {
                    ModelState.AddModelError("ApplicationRate", Resource.MsgForApplicationRate);
                }
                //if (model.ApplicationRate != null)
                //{
                //    string input = model.ApplicationRate.ToString();
                //    if (input.Split('.').Length > 2)
                //    {
                //        //error msg
                //    }
                //}

                if (!ModelState.IsValid)
                {
                    return View("ManualApplicationRate", model);
                }
                model.IsNMaxLimitWarning = false;
                model.IsOrgManureNfieldLimitWarning = false;
                model.IsEndClosedPeriodFebruaryWarning = false;
                model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = false;

                string message = string.Empty;
                //Error error = null;
                OrganicManureViewModel? organicManureViewModel = new OrganicManureViewModel();
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if (organicManureViewModel != null)
                {
                    if (model.ApplicationRate != organicManureViewModel.ApplicationRate)
                    {
                        model.IsWarningMsgNeedToShow = false;
                    }
                }

                if (model.FieldList != null && model.FieldList.Count > 0)
                {
                    foreach (var fieldId in model.FieldList)
                    {
                        Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                        if (field != null)
                        {
                            bool isFieldIsInNVZ = field.IsWithinNVZ != null ? field.IsWithinNVZ.Value : false;
                            if (isFieldIsInNVZ)
                            {
                                (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, null, null);
                                if (error == null)
                                {
                                    if (managementIds.Count > 0)
                                    {
                                        (model, error) = await IsNFieldLimitWarningMessage(model, isFieldIsInNVZ, managementIds[0], false, Convert.ToInt32(fieldId));
                                        if (error == null)
                                        {
                                            (model, error) = await IsNMaxWarningMessage(model, Convert.ToInt32(fieldId), managementIds[0], false);
                                            if (error == null)
                                            {
                                                if (!(model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials))
                                                {
                                                    (model, error) = await IsEndClosedPeriodFebruaryWarningMessage(model, Convert.ToInt32(fieldId), false);

                                                }

                                            }
                                            else
                                            {
                                                TempData["ManualApplicationRateError"] = error.Message;
                                                return View(model);
                                            }

                                            //Closed period and maximum application rate for high N organic manure on a registered organic farm message - Max Application Rate - Warning Message
                                            if (!(model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials))
                                            {
                                                (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, message, error) = await IsClosedPeriodStartAndEndFebExceedNRateException(model, Convert.ToInt32(fieldId));
                                                if (error == null)
                                                {
                                                    if (!string.IsNullOrWhiteSpace(message))
                                                    {
                                                        TempData["AppRateExceeds150WithinClosedPeriodOrganic"] = message;
                                                    }
                                                }
                                                else
                                                {
                                                    TempData["ManualApplicationRateError"] = error.Message;
                                                    return View(model);
                                                }
                                            }

                                        }
                                        else
                                        {
                                            TempData["ManualApplicationRateError"] = error.Message;
                                            return View(model);
                                        }
                                    }
                                }
                                else
                                {
                                    TempData["ManualApplicationRateError"] = error.Message;
                                    return View(model);
                                }
                            }
                        }
                    }
                }

                if (model.IsOrgManureNfieldLimitWarning || model.IsNMaxLimitWarning || model.IsEndClosedPeriodFebruaryWarning || model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150)
                {
                    if (!model.IsWarningMsgNeedToShow)
                    {
                        model.IsWarningMsgNeedToShow = true;
                        (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                        ViewBag.IsWales = farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales ? true : false;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return View(model);
                    }
                }
                else
                {
                    model.IsWarningMsgNeedToShow = false;
                    if (model.IsOrgManureNfieldLimitWarning)
                    {
                        model.IsOrgManureNfieldLimitWarning = false;
                    }
                    if (model.IsNMaxLimitWarning)
                    {
                        model.IsNMaxLimitWarning = false;
                    }
                    if (model.IsEndClosedPeriodFebruaryWarning)
                    {
                        model.IsEndClosedPeriodFebruaryWarning = false;
                    }
                }

                model.Area = null;
                model.Quantity = null;
                if (model.OrganicManures.Count > 0)
                {
                    foreach (var orgManure in model.OrganicManures)
                    {
                        orgManure.AreaSpread = null;
                        orgManure.ManureQuantity = null;
                        orgManure.ApplicationRate = model.ApplicationRate.Value;
                    }
                }
                model.IsWarningMsgNeedToShow = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                if (model.IsCheckAnswer && (!model.IsManureTypeChange) && (!model.IsFieldGroupChange) && (!model.IsAnyChangeInField))
                {
                    return RedirectToAction("CheckAnswer");
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManualApplicationRate() post action : {ex.Message}, {ex.StackTrace}");
                TempData["ManualApplicationRateError"] = ex.Message;
                return View(model);
            }
            return RedirectToAction("IncorporationMethod");
        }

        [HttpGet]
        public async Task<IActionResult> AreaQuantity()
        {
            _logger.LogTrace($"Organic Manure Controller : AreaQuantity() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            model.IsWarningMsgNeedToShow = false;
            model.IsOrgManureNfieldLimitWarning = false;
            model.IsNMaxLimitWarning = false;
            model.IsEndClosedPeriodFebruaryWarning = false;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return View(model);

        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AreaQuantity(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : AreaQuantity() post action called");
            int farmId = 0;
            Farm farm = new Farm();
            if ((!ModelState.IsValid) && ModelState.ContainsKey("Area"))
            {
                var areaError = ModelState["Area"].Errors.Count > 0 ?
                                ModelState["Area"].Errors[0].ErrorMessage.ToString() : null;

                if (areaError != null && areaError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["Area"].RawValue, Resource.lblAreas)))
                {
                    ModelState["Area"].Errors.Clear();
                    ModelState["Area"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblArea));
                }
            }
            if ((!ModelState.IsValid) && ModelState.ContainsKey("Quantity"))
            {
                var quantityError = ModelState["Quantity"].Errors.Count > 0 ?
                                ModelState["Quantity"].Errors[0].ErrorMessage.ToString() : null;

                if (quantityError != null && quantityError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["Quantity"].RawValue, Resource.lblQuantity)))
                {
                    ModelState["Quantity"].Errors.Clear();
                    ModelState["Quantity"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.MsgQuantity));
                }
            }

            if (model.Area == null)
            {
                ModelState.AddModelError("Area", Resource.MsgEnterAValidArea);
            }
            if (model.Quantity == null)
            {
                ModelState.AddModelError("Quantity", Resource.MsgEnterAValidQuantity);
            }
            if (model.Area != null && model.Area == 0)
            {
                ModelState.AddModelError("Area", Resource.MsgAreaMustBeGreaterThanZero);
            }
            if (model.Area != null && model.Area < 0)
            {
                ModelState.AddModelError("Area", Resource.MsgEnterANumberWhichIsGreaterThanZero);
            }
            if (model.Quantity != null && model.Quantity < 0)
            {
                ModelState.AddModelError("Quantity", Resource.MsgEnterANumberWhichIsGreaterThanZero);
            }
            if (model.Quantity != null && model.Area != null && model.Area > 0 && model.Quantity > 0)
            {
                model.ApplicationRate = model.Quantity.Value / model.Area.Value;

                if (model.ApplicationRate != null && model.ApplicationRate > 250)
                {
                    ModelState.AddModelError("Quantity", Resource.MsgForApplicationRate);
                }
            }
            if (!ModelState.IsValid)
            {
                return View("AreaQuantity", model);
            }
            model.ApplicationRate = Math.Round((model.Quantity.Value / model.Area.Value), 1);
            Error error = new Error();
            if (model.OrganicManures.Count > 0)
            {
                foreach (var orgManure in model.OrganicManures)
                {
                    orgManure.AreaSpread = model.Area.Value;
                    orgManure.ManureQuantity = model.Quantity.Value;
                    orgManure.ApplicationRate = model.ApplicationRate.Value;
                }
            }
            model.IsNMaxLimitWarning = false;
            model.IsOrgManureNfieldLimitWarning = false;
            model.IsEndClosedPeriodFebruaryWarning = false;
            OrganicManureViewModel? organicManureViewModel = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (organicManureViewModel != null)
            {
                if (model.ApplicationRate != organicManureViewModel.ApplicationRate)
                {
                    model.IsWarningMsgNeedToShow = false;
                }
            }
            if (model.FieldList != null && model.FieldList.Count > 0)
            {
                foreach (var fieldId in model.FieldList)
                {
                    Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                    if (field != null)
                    {
                        bool isFieldIsInNVZ = field.IsWithinNVZ != null ? field.IsWithinNVZ.Value : false;
                        if (isFieldIsInNVZ)
                        {
                            (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, null, null);
                            if (error == null)
                            {
                                if (managementIds.Count > 0)
                                {
                                    (model, error) = await IsNFieldLimitWarningMessage(model, isFieldIsInNVZ, managementIds[0], false, Convert.ToInt32(fieldId));
                                    if (error == null)
                                    {
                                        (model, error) = await IsNMaxWarningMessage(model, Convert.ToInt32(fieldId), managementIds[0], false);
                                        if (error == null)
                                        {
                                            (model, error) = await IsEndClosedPeriodFebruaryWarningMessage(model, Convert.ToInt32(fieldId), false);

                                        }
                                        else
                                        {
                                            TempData["AreaAndQuantityError"] = error.Message;
                                            return View(model);
                                        }
                                    }
                                    else
                                    {
                                        TempData["AreaAndQuantityError"] = error.Message;
                                        return View(model);
                                    }
                                }
                            }
                            else
                            {
                                TempData["AreaAndQuantityError"] = error.Message;
                                return View(model);
                            }
                        }
                    }
                }
            }
            if (model.IsOrgManureNfieldLimitWarning || model.IsNMaxLimitWarning || model.IsEndClosedPeriodFebruaryWarning)
            {
                if (!model.IsWarningMsgNeedToShow)
                {
                    model.IsWarningMsgNeedToShow = true;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return View(model);
                }
            }
            else
            {
                model.IsWarningMsgNeedToShow = false;
                if (model.IsOrgManureNfieldLimitWarning)
                {
                    model.IsOrgManureNfieldLimitWarning = false;
                }
                if (model.IsNMaxLimitWarning)
                {
                    model.IsNMaxLimitWarning = false;
                }
                if (model.IsEndClosedPeriodFebruaryWarning)
                {
                    model.IsEndClosedPeriodFebruaryWarning = false;
                }
            }
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            if (model.IsCheckAnswer && (!model.IsManureTypeChange) && (!model.IsFieldGroupChange) && (!model.IsAnyChangeInField))
            {
                return RedirectToAction("CheckAnswer");
            }
            return RedirectToAction("IncorporationMethod");
        }

        [HttpGet]
        public async Task<IActionResult> IncorporationMethod()
        {
            _logger.LogTrace($"Organic Manure Controller : IncorporationMethod() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            Error error = null;
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if ((model.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.DeepInjection2530cm) || (model.ApplicationMethod == (int)NMP.Portal.Enums.ApplicationMethod.ShallowInjection57cm))
            {
                return RedirectToAction("ConditionsAffectingNutrients");
            }
            try
            {
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                bool isLiquid = false;
                if (error == null && manureTypeList.Count > 0)
                {
                    var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                    isLiquid = manureType.IsLiquid.Value;

                }

                string applicableFor = isLiquid ? Resource.lblL : Resource.lblB;

                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();
                string applicableForArableOrGrass = fieldType == 1 ? Resource.lblA : Resource.lblG;
                (List<IncorporationMethodResponse> incorporationMethods, error) = await _organicManureService.FetchIncorporationMethodsByApplicationId(model.ApplicationMethod.Value, applicableForArableOrGrass);
                if (error == null && incorporationMethods.Count > 0)
                {
                    ViewBag.IncorporationMethod = incorporationMethods.OrderBy(i => i.SortOrder).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in IncorporationMethod() : {ex.Message}, {ex.StackTrace}");
                if (model.ApplicationRateMethod != (int)NMP.Portal.Enums.ApplicationRate.UseDefaultApplicationRate)
                {
                    TempData["ManualApplicationRateError"] = ex.Message;
                    return RedirectToAction("ManualApplicationRate");
                }
                else
                {
                    ViewBag.Error = ex.Message;
                    return RedirectToAction("ApplicationRateMethod");
                }
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IncorporationMethod(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : IncorporationMethod() post action called");
            Error error = null;
            if (model.IncorporationMethod == null)
            {
                ModelState.AddModelError("IncorporationMethod", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            try
            {
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                if (!ModelState.IsValid)
                {

                    //(manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupId.Value, countryId);
                    bool isLiquid = false;
                    if (error == null && manureTypeList.Count > 0)
                    {
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        isLiquid = manureType.IsLiquid.Value;

                    }

                    string applicableFor = isLiquid ? Resource.lblL : Resource.lblB;
                    List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                    var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();
                    string applicableForArableOrGrass = fieldType == 1 ? Resource.lblA : Resource.lblG;
                    (List<IncorporationMethodResponse> incorporationMethods, error) = await _organicManureService.FetchIncorporationMethodsByApplicationId(model.ApplicationMethod.Value, applicableForArableOrGrass);

                    ViewBag.IncorporationMethod = incorporationMethods;
                    return View(model);
                }

                (model.IncorporationMethodName, error) = await _organicManureService.FetchIncorporationMethodById(model.IncorporationMethod.Value);

                if (model.OrganicManures.Count > 0)
                {
                    foreach (var orgManure in model.OrganicManures)
                    {
                        orgManure.IncorporationMethodID = model.IncorporationMethod.Value;
                    }
                }
                if (model.IsCheckAnswer && (!model.IsFieldGroupChange) && (!model.IsManureTypeChange) && (!model.IsApplicationMethodChange))
                {
                    model.IsIncorporationMethodChange = true;
                }

                if (model.IncorporationMethod == (int)NMP.Portal.Enums.IncorporationMethod.NotIncorporated)
                {
                    //int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;

                    if (error == null && manureTypeList.Count > 0)
                    {
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        bool isLiquid = manureType.IsLiquid.Value;
                        string applicableFor = Resource.lblNull;// isLiquid ? Resource.lblL : Resource.lblS;
                                                                //if (manureType.Id == (int)NMP.Portal.Enums.ManureTypes.PoultryManure)
                                                                //{
                                                                //    applicableFor = Resource.lblP;
                                                                //}
                        (List<IncorprationDelaysResponse> incorporationDelaysList, error) = await _organicManureService.FetchIncorporationDelaysByMethodIdAndApplicableFor(model.IncorporationMethod ?? 0, applicableFor);
                        if (error == null && incorporationDelaysList.Count == 1)
                        {
                            model.IncorporationDelay = incorporationDelaysList.FirstOrDefault().ID;
                            (model.IncorporationDelayName, error) = await _organicManureService.FetchIncorporationDelayById(model.IncorporationDelay.Value);
                            if (error == null)
                            {
                                if (model.OrganicManures.Count > 0)
                                {
                                    foreach (var orgManure in model.OrganicManures)
                                    {
                                        orgManure.IncorporationDelayID = model.IncorporationDelay.Value;
                                    }
                                }
                            }
                            else
                            {
                                TempData["IncorporationMethodError"] = error.Message;
                                applicableFor = isLiquid ? Resource.lblL : Resource.lblS;
                                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                                var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();
                                string applicableForArableOrGrass = fieldType == 1 ? Resource.lblA : Resource.lblG;
                                (List<IncorporationMethodResponse> incorporationMethods, error) = await _organicManureService.FetchIncorporationMethodsByApplicationId(model.ApplicationMethod.Value, applicableForArableOrGrass);
                                if (error == null && incorporationMethods.Count > 0)
                                {
                                    ViewBag.IncorporationMethod = incorporationMethods;
                                }
                                return View(model);
                            }

                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        }
                        else if (error != null)
                        {
                            TempData["IncorporationMethodError"] = error.Message;
                            applicableFor = isLiquid ? Resource.lblL : Resource.lblB;
                            List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                            var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();
                            string applicableForArableOrGrass = fieldType == 1 ? Resource.lblA : Resource.lblG;
                            (List<IncorporationMethodResponse> incorporationMethods, error) = await _organicManureService.FetchIncorporationMethodsByApplicationId(model.ApplicationMethod.Value, applicableForArableOrGrass);
                            if (error == null && incorporationMethods.Count > 0)
                            {
                                ViewBag.IncorporationMethod = incorporationMethods;
                            }
                            return View(model);
                        }
                    }
                    else if (error != null)
                    {
                        TempData["IncorporationMethodError"] = error.Message;
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        bool isLiquid = manureType.IsLiquid.Value;
                        string applicableFor = isLiquid ? Resource.lblL : Resource.lblB;
                        List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                        var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();
                        string applicableForArableOrGrass = fieldType == 1 ? Resource.lblA : Resource.lblG;
                        (List<IncorporationMethodResponse> incorporationMethods, error) = await _organicManureService.FetchIncorporationMethodsByApplicationId(model.ApplicationMethod.Value, applicableForArableOrGrass);
                        if (error == null && incorporationMethods.Count > 0)
                        {
                            ViewBag.IncorporationMethod = incorporationMethods;
                        }

                        return View(model);
                    }
                    if (model.IsCheckAnswer && (!model.IsFieldGroupChange) && (!model.IsManureTypeChange) && (!model.IsAnyChangeInField))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                    return RedirectToAction("ConditionsAffectingNutrients");
                }
                else
                {
                    OrganicManureViewModel? organicManure = new OrganicManureViewModel();
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        organicManure = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if (organicManure.IncorporationMethod != null && organicManure.IncorporationMethod == (int)NMP.Portal.Enums.IncorporationMethod.NotIncorporated)
                    {
                        model.IncorporationDelay = null;
                        model.IncorporationDelayName = string.Empty;
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in IncorporationMethod() : {ex.Message}, {ex.StackTrace}");
                TempData["IncorporationMethodError"] = ex.Message;
                return View(model);
            }
            return RedirectToAction("IncorporationDelay");

        }

        [HttpGet]
        public async Task<IActionResult> IncorporationDelay()
        {
            _logger.LogTrace($"Organic Manure Controller : IncorporationDelay() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            Error error = null;
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            try
            {
                string applicableFor = string.Empty;
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                bool isLiquid = false;
                if (error == null && manureTypeList.Count > 0)
                {
                    var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                    isLiquid = manureType.IsLiquid.Value;
                    applicableFor = isLiquid ? Resource.lblL : Resource.lblS;
                    if (manureType.Id == (int)NMP.Portal.Enums.ManureTypes.PoultryManure)
                    {
                        applicableFor = Resource.lblP;
                    }
                }
                if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
                    {
                        applicableFor = Resource.lblL;
                    }
                    else
                    {
                        applicableFor = Resource.lblS;
                    }
                }

                (List<IncorprationDelaysResponse> incorporationDelaysList, error) = await _organicManureService.FetchIncorporationDelaysByMethodIdAndApplicableFor(model.IncorporationMethod ?? 0, applicableFor);
                if (error == null && incorporationDelaysList.Count > 0)
                {
                    ViewBag.IncorporationDelaysList = incorporationDelaysList;
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in IncorporationDelay() action : {ex.Message}, {ex.StackTrace}");
                TempData["IncorporationMethodError"] = ex.Message;
                return View(model);
            }
            return View(model);


        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IncorporationDelay(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : IncorporationDelay() post action called");
            Error error = null;
            try
            {
                if (model.IncorporationDelay == null)
                {
                    ModelState.AddModelError("IncorporationDelay", Resource.MsgSelectAnOptionBeforeContinuing);
                }
                if (!ModelState.IsValid)
                {
                    string applicableFor = string.Empty;
                    int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                    List<ManureType> manureTypeList = new List<ManureType>();
                    if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    {
                        (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                    }
                    else
                    {
                        (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                    }
                    bool isLiquid = false;
                    if (error == null && manureTypeList.Count > 0)
                    {
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        isLiquid = manureType.IsLiquid.Value;
                        applicableFor = isLiquid ? Resource.lblL : Resource.lblS;
                        if (manureType.Id == (int)NMP.Portal.Enums.ManureTypes.PoultryManure)
                        {
                            applicableFor = Resource.lblP;
                        }
                    }

                    (List<IncorprationDelaysResponse> incorporationDelaysList, error) = await _organicManureService.FetchIncorporationDelaysByMethodIdAndApplicableFor(model.IncorporationMethod ?? 0, applicableFor);
                    ViewBag.IncorporationDelaysList = incorporationDelaysList;
                    return View(model);
                }
                (model.IncorporationDelayName, error) = await _organicManureService.FetchIncorporationDelayById(model.IncorporationDelay.Value);
                if (error == null)
                {
                    if (model.OrganicManures.Count > 0)
                    {
                        foreach (var orgManure in model.OrganicManures)
                        {
                            orgManure.IncorporationDelayID = model.IncorporationDelay.Value;
                        }
                    }
                }
                else
                {
                    TempData["IncorporationDelayError"] = error.Message;
                    return View(model);
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                if ((!model.IsFieldGroupChange) && (!model.IsManureTypeChange) && model.IsCheckAnswer && (!model.IsAnyChangeInField))// && model.IsApplicationMethodChange)
                {
                    return RedirectToAction("CheckAnswer");
                }

                return RedirectToAction("ConditionsAffectingNutrients");
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in IncorporationDelay() post action : {ex.Message}, {ex.StackTrace}");
                TempData["IncorporationDelayError"] = ex.Message;
                return View(model);
            }

        }

        [HttpGet]
        public async Task<IActionResult> ConditionsAffectingNutrients()
        {
            _logger.LogTrace($"Organic Manure Controller : ConditionsAffectingNutrients() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            Error error = new Error();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                //Autumn crop Nitrogen uptake
                if (model.AutumnCropNitrogenUptake == null)
                {
                    model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                    foreach (var field in model.FieldList)
                    {
                        int fieldId = Convert.ToInt32(field);
                        (CropTypeResponse cropsResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(fieldId, model.HarvestYear.Value, false);

                        (CropTypeLinkingResponse cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(cropsResponse.CropTypeId);

                        if (error == null && cropTypeLinkingResponse != null)
                        {
                            int mannerCropTypeId = cropTypeLinkingResponse.MannerCropTypeID;

                            //check early and late for winter cereals and winter oilseed rape
                            //if sowing date after 15 sept then late
                            //DateTime? sowingDate = crop.Select(x => x.SowingDate).FirstOrDefault();

                            var uptakeData = new
                            {
                                cropTypeId = mannerCropTypeId,
                                applicationMonth = model.ApplicationDate.Value.Month
                            };

                            string jsonString = JsonConvert.SerializeObject(uptakeData);
                            (NitrogenUptakeResponse nitrogenUptakeResponse, error) = await _organicManureService.FetchAutumnCropNitrogenUptake(jsonString);
                            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                            {
                                if (model.IsApplicationMethodChange)
                                {
                                    TempData["ManureApplyingDateError"] = error.Message;
                                    return RedirectToAction("ManureApplyingDate");
                                }
                                else
                                {
                                    TempData["IncorporationDelayError"] = error.Message;
                                    return RedirectToAction("IncorporationDelay");
                                }
                            }
                            if (nitrogenUptakeResponse != null && error == null)
                            {
                                if (model.AutumnCropNitrogenUptakes == null)
                                {
                                    model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                                }
                                var fieldData = await _fieldService.FetchFieldByFieldId(fieldId);
                                model.AutumnCropNitrogenUptakes.Add(new AutumnCropNitrogenUptakeDetail
                                {
                                    EncryptedFieldId = _organicManureProtector.Protect(fieldId.ToString()),
                                    FieldName = fieldData.Name ?? string.Empty,
                                    CropTypeId = cropsResponse.CropTypeId,
                                    CropTypeName = cropsResponse.CropType,
                                    AutumnCropNitrogenUptake = nitrogenUptakeResponse.value
                                });
                            }

                        }
                        else if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                        {
                            if (model.IsApplicationMethodChange)
                            {
                                TempData["ManureApplyingDateError"] = error.Message;
                                return RedirectToAction("ManureApplyingDate");
                            }
                            else
                            {
                                TempData["IncorporationDelayError"] = error.Message;
                                return RedirectToAction("IncorporationDelay");
                            }
                        }
                    }
                }

                //Soil drainage end date
                if (model.SoilDrainageEndDate == null)
                {
                    if (model.ApplicationDate.Value.Month >= 8)
                    {
                        model.SoilDrainageEndDate = new DateTime(model.ApplicationDate.Value.AddYears(1).Year, (int)NMP.Portal.Enums.Month.March, 31);
                    }
                    else
                    {
                        model.SoilDrainageEndDate = new DateTime(model.ApplicationDate.Value.Year, (int)NMP.Portal.Enums.Month.March, 31);
                    }
                }

                //Rainfall within 6 hours
                if (model.RainfallWithinSixHoursID == null)
                {
                    (RainTypeResponse rainType, error) = await _organicManureService.FetchRainTypeDefault();
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        ViewBag.Error = error.Message;
                        return View(model);
                    }
                    else
                    {
                        model.RainfallWithinSixHours = rainType.Name;
                        model.RainfallWithinSixHoursID = rainType.ID;
                    }
                }
                else
                {
                    (RainTypeResponse rainType, error) = await _organicManureService.FetchRainTypeById(model.RainfallWithinSixHoursID.Value);
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        ViewBag.Error = error.Message;
                        return View(model);
                    }
                    else
                    {
                        model.RainfallWithinSixHours = rainType.Name;
                    }
                }

                //Effective rainfall after application
                (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                string halfPostCode = string.Empty;
                if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                {
                    if (model.IsApplicationMethodChange)
                    {
                        TempData["ManureApplyingDateError"] = error.Message;
                        return RedirectToAction("ManureApplyingDate");
                    }
                    else
                    {
                        TempData["IncorporationDelayError"] = error.Message;
                        return RedirectToAction("IncorporationDelay");
                    }
                }
                else
                {
                    halfPostCode = farm.ClimateDataPostCode.Substring(0, 4).Trim();
                }

                if (model.ApplicationDate.HasValue && model.SoilDrainageEndDate.HasValue)
                {
                    if (model.TotalRainfall == null)
                    {
                        var rainfallPostCodeApplication = new
                        {
                            applicationDate = model.ApplicationDate.Value.ToString("yyyy-MM-dd"),
                            endOfSoilDrainageDate = model.SoilDrainageEndDate.Value.ToString("yyyy-MM-dd"),
                            climateDataPostcode = halfPostCode
                        };

                        string jsonString = JsonConvert.SerializeObject(rainfallPostCodeApplication);
                        model.TotalRainfall = await _organicManureService.FetchRainfallByPostcodeAndDateRange(jsonString);
                    }
                }

                //Windspeed during application 
                if (model.WindspeedID == null)
                {
                    (WindspeedResponse windspeed, error) = await _organicManureService.FetchWindspeedDataDefault();
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        if (model.IsApplicationMethodChange)
                        {
                            TempData["ManureApplyingDateError"] = error.Message;
                            return RedirectToAction("ManureApplyingDate");
                        }
                        else
                        {
                            TempData["IncorporationDelayError"] = error.Message;
                            return RedirectToAction("IncorporationDelay");
                        }
                    }
                    else
                    {
                        model.WindspeedID = windspeed.ID;
                        model.Windspeed = windspeed.Name;
                    }
                }
                else
                {
                    (WindspeedResponse windspeed, error) = await _organicManureService.FetchWindspeedById(model.WindspeedID.Value);
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        if (model.IsApplicationMethodChange)
                        {
                            TempData["ManureApplyingDateError"] = error.Message;
                            return RedirectToAction("ManureApplyingDate");
                        }
                        else
                        {
                            TempData["IncorporationDelayError"] = error.Message;
                            return RedirectToAction("IncorporationDelay");
                        }
                    }
                    else
                    {
                        model.Windspeed = windspeed.Name;
                    }
                }

                //Topsoil moisture
                if (model.MoistureTypeId == null)
                {
                    (MoistureTypeResponse moisterType, error) = await _organicManureService.FetchMoisterTypeDefaultByApplicationDate(model.ApplicationDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"));
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        if (model.IsApplicationMethodChange)
                        {
                            TempData["ManureApplyingDateError"] = error.Message;
                            return RedirectToAction("ManureApplyingDate");
                        }
                        else
                        {
                            TempData["IncorporationDelayError"] = error.Message;
                            return RedirectToAction("IncorporationDelay");
                        }
                    }
                    else
                    {
                        model.MoistureType = moisterType.Name;
                        model.MoistureTypeId = moisterType.ID;
                    }
                }
                else
                {
                    (MoistureTypeResponse moisterType, error) = await _organicManureService.FetchMoisterTypeById(model.MoistureTypeId.Value);
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        if (model.IsApplicationMethodChange)
                        {
                            TempData["ManureApplyingDateError"] = error.Message;
                            return RedirectToAction("ManureApplyingDate");
                        }
                        else
                        {
                            TempData["IncorporationDelayError"] = error.Message;
                            return RedirectToAction("IncorporationDelay");
                        }
                    }
                    else
                    {
                        model.MoistureType = moisterType.Name;
                    }
                }


                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ConditionsAffectingNutrients() action : {ex.Message}, {ex.StackTrace}");
                if (model.IsApplicationMethodChange)
                {
                    TempData["ManureApplyingDateError"] = ex.Message;
                    return RedirectToAction("ManureApplyingDate");
                }
                else
                {
                    TempData["IncorporationDelayError"] = ex.Message;
                    return RedirectToAction("IncorporationDelay");
                }
            }


            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConditionsAffectingNutrients(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ConditionsAffectingNutrients() post action called");
            if (!ModelState.IsValid)
            {
                return View("ConditionsAffectingNutrients", model);
            }
            try
            {
                if (model.OrganicManures.Count > 0)
                {
                    int i = 0;
                    foreach (var orgManure in model.OrganicManures)
                    {
                        if (model.AutumnCropNitrogenUptakes != null && model.AutumnCropNitrogenUptakes.Count > 0)
                        {
                            //orgManure.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptake ?? 0;
                            var matchingUptake = model.AutumnCropNitrogenUptakes?
                         .FirstOrDefault(uptake => uptake.FieldName == orgManure.FieldName);

                            if (matchingUptake != null)
                            {
                                orgManure.AutumnCropNitrogenUptake = matchingUptake.AutumnCropNitrogenUptake;
                            }
                            else
                            {
                                orgManure.AutumnCropNitrogenUptake = 0;
                            }
                        }
                        orgManure.SoilDrainageEndDate = model.SoilDrainageEndDate.Value;
                        orgManure.RainfallWithinSixHoursID = model.RainfallWithinSixHoursID.Value;
                        orgManure.Rainfall = model.TotalRainfall.Value;
                        orgManure.WindspeedID = model.WindspeedID.Value;
                        orgManure.MoistureID = model.MoistureTypeId.Value;

                        i++;
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ConditionsAffectingNutrients() : {ex.Message}, {ex.StackTrace}");
                TempData["ConditionsAffectingNutrientsError"] = ex.Message;
                return View(model);
            }
            return RedirectToAction("CheckAnswer");

        }

        [HttpGet]
        public async Task<IActionResult> backActionForManureGroup()
        {
            _logger.LogTrace($"Organic Manure Controller : BackActionForManureGroup() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            if (model.IsCheckAnswer)
            {
                model.ManureGroupIdForFilter = model.ManureGroupId;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                (CommonResponse manureGroup, Error error) = await _organicManureService.FetchManureGroupById(model.ManureGroupId.Value);
                if (error == null)
                {
                    if (manureGroup != null)
                    {
                        model.ManureGroupName = manureGroup.Name;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    }
                }
                else
                {
                    TempData["ManureGroupError"] = error.Message;
                    return View(model);
                }

                if (!model.IsFieldGroupChange && (!model.IsAnyChangeInField))
                {
                    return RedirectToAction("CheckAnswer");
                }
            }
            if (model.FieldGroup == Resource.lblSelectSpecificFields && model.IsComingFromRecommendation)
            {

                if (model.FieldList.Count > 0 && model.FieldList.Count == 1)
                {
                    string fieldId = model.FieldList[0];
                    return RedirectToAction("Recommendations", "Crop", new
                    {
                        q = model.EncryptedFarmId,
                        r = _fieldDataProtector.Protect(fieldId),
                        s = model.EncryptedHarvestYear

                    });
                }
            }
            //if (model.IsDoubleCropAvailable)
            //{
            //    return RedirectToAction("DoubleCrop", new
            //    {
            //        q = model.DoubleCropEncryptedCounter

            //    });
            //}

            if (model.FieldGroup == Resource.lblSelectSpecificFields && (!model.IsComingFromRecommendation))
            {
                return RedirectToAction("Fields");
            }

            return RedirectToAction("FieldGroup", new
            {
                q = model.EncryptedFarmId,
                r = model.EncryptedHarvestYear
            });
        }

        [HttpGet]
        public async Task<IActionResult> CheckAnswer(string? q, string? r, string? s, string? t, string? u)//q=encryptedId,r=encryptedFramId,s=encryptedHarvestYear,t=encryptedFieldName
        {
            _logger.LogTrace($"Organic Manure Controller : CheckAnswer() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            Error error = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(r) && !string.IsNullOrWhiteSpace(s))
                {
                    if (!string.IsNullOrWhiteSpace(u))
                    {
                        model.IsComingFromRecommendation = true;
                    }
                    model.EncryptedOrgManureId = q;
                    int decryptedId = Convert.ToInt32(_cropDataProtector.Unprotect(q));
                    int decryptedFarmId = Convert.ToInt32(_farmDataProtector.Unprotect(r));
                    model.FarmId = decryptedFarmId;
                    int decryptedHarvestYear = Convert.ToInt32(_farmDataProtector.Unprotect(s));
                    (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                    if (error.Message == null)
                    {
                        model.FarmCountryId = farm.CountryID;
                        model.isEnglishRules = farm.EnglishRules;
                    }
                    if (decryptedId > 0)
                    {
                        (OrganicManure organicManure, error) = await _organicManureService.FetchOrganicManureById(decryptedId);
                        if (error == null && organicManure != null)
                        {
                            int counter = 1;
                            (List<FertiliserAndOrganicManureUpdateResponse> organicManureResponse, error) = await _organicManureService.FetchFieldWithSameDateAndManureType(decryptedId, decryptedFarmId, decryptedHarvestYear);
                            if (string.IsNullOrWhiteSpace(error.Message) && organicManureResponse != null && organicManureResponse.Count > 0)
                            {
                                model.UpdatedOrganicIds = organicManureResponse;
                                if (model.IsComingFromRecommendation)
                                {
                                    model.FieldGroup = Resource.lblSelectSpecificFields;
                                    model.FieldGroupName = Resource.lblSelectSpecificFields;
                                    model.UpdatedOrganicIds.RemoveAll(x => x.OrganicManureId != organicManure.ID);
                                    organicManureResponse.RemoveAll(x => x.OrganicManureId != organicManure.ID);
                                }
                                var SelectListItem = organicManureResponse.Select(f => new SelectListItem
                                {
                                    Value = f.Id.ToString(),
                                    Text = f.Name.ToString()
                                }).ToList().DistinctBy(x => x.Value);
                                ViewBag.Fields = SelectListItem.OrderBy(x => x.Text).ToList();
                                List<string> fieldName = new List<string>();
                                fieldName.Add(_cropDataProtector.Unprotect(t));
                                ViewBag.SelectedFields = fieldName;
                                if (SelectListItem != null)
                                {
                                    var filteredList = SelectListItem
                                  .Where(item => item.Text.Contains(_cropDataProtector.Unprotect(t)))
                                  .ToList();
                                    if (filteredList != null)
                                    {
                                        model.FieldName = filteredList.Select(item => item.Text).FirstOrDefault();
                                        model.FieldList = filteredList.Select(item => item.Value).ToList();
                                    }
                                }
                                foreach (string field in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                                    cropList = cropList.Where(x => x.Year == decryptedHarvestYear).ToList();

                                    if (cropList != null && cropList.Count == 2)
                                    {
                                        model.IsDoubleCropAvailable = true;
                                        model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                                    }
                                }
                                ManagementPeriod managementPeriod = new ManagementPeriod();
                                if (model.IsDoubleCropAvailable)
                                {
                                    string cropTypeName = string.Empty;
                                    if (model.DoubleCrop == null)
                                    {
                                        model.DoubleCrop = new List<DoubleCrop>();
                                        foreach (string fieldId in model.FieldList)
                                        {
                                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                                            cropList = cropList.Where(x => x.Year == decryptedHarvestYear).ToList();
                                            if (cropList != null && cropList.Count == 2)
                                            {
                                                (managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);
                                                if (managementPeriod != null && (string.IsNullOrWhiteSpace(error.Message)))
                                                {
                                                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                                    if (crop != null && (string.IsNullOrWhiteSpace(error.Message)))
                                                    {
                                                        cropTypeName = await _fieldService.FetchCropTypeById(crop.CropTypeID.Value);
                                                        var doubleCrop = new DoubleCrop
                                                        {
                                                            CropID = crop.ID.Value,
                                                            CropName = cropTypeName,
                                                            CropOrder = crop.CropOrder.Value,
                                                            FieldID = crop.FieldID.Value,
                                                            FieldName = (await _fieldService.FetchFieldByFieldId(crop.FieldID.Value)).Name,
                                                            EncryptedCounter = _fieldDataProtector.Protect(counter.ToString()), //model.DoubleCropEncryptedCounter,
                                                            Counter = model.DoubleCropCurrentCounter,
                                                        };
                                                        model.DoubleCrop.Add(doubleCrop);
                                                        counter++;
                                                    }

                                                }
                                            }
                                        }
                                    }
                                }
                                int fieldIdForUpdate = Convert.ToInt32(model.FieldList.FirstOrDefault());
                                if (model.OrganicManures == null)
                                {
                                    model.OrganicManures = new List<OrganicManure>();
                                }
                                int? defoliation = null;
                                string defoliationName = string.Empty;
                                (managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);
                                if (!string.IsNullOrWhiteSpace(error.Message))
                                {
                                    TempData["CheckYourAnswerError"] = error.Message;
                                }
                                else
                                {
                                    defoliation = managementPeriod.Defoliation;
                                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                    if (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass)
                                    {
                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                        {
                                            model.FieldID = crop.FieldID;
                                            model.CropOrder = crop.CropOrder;
                                            model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                                            (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                            if (error == null && defoliationSequence != null)
                                            {
                                                string description = defoliationSequence.DefoliationSequenceDescription;

                                                string[] defoliationParts = description.Split(',')
                                                                                       .Select(x => x.Trim())
                                                                                       .ToArray();

                                                //string selectedDefoliation = (defoliation > 0 && defoliation.Value <= defoliationParts.Length)
                                                //    ? $"{Enum.GetName(typeof(PotentialCut), defoliation.Value)} ({defoliationParts[defoliation.Value - 1]})"
                                                //    : $"{defoliation}";
                                                string selectedDefoliation = (defoliation > 0 && defoliation.Value <= defoliationParts.Length)
                                            ? $"{Enum.GetName(typeof(PotentialCut), defoliation.Value)} - {defoliationParts[defoliation.Value - 1]}"
                                            : $"{defoliation}";
                                                var parts = selectedDefoliation.Split('-');
                                                if (parts.Length == 2)
                                                {
                                                    var left = parts[0].Trim();
                                                    var right = parts[1].Trim();

                                                    if (!string.IsNullOrWhiteSpace(right))
                                                    {
                                                        right = char.ToUpper(right[0]) + right.Substring(1);
                                                    }

                                                    selectedDefoliation = $"{left} - {right}";
                                                }
                                                model.IsAnyCropIsGrass = true;
                                                model.IsSameDefoliationForAll = true;
                                                model.GrassCropCount = 1;
                                                defoliationName = selectedDefoliation;
                                            }
                                        }

                                        organicManure.EncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                        organicManure.Defoliation = defoliation;
                                        organicManure.DefoliationName = defoliationName;
                                    }
                                }
                                organicManure.FieldID = model.FieldID;
                                organicManure.FieldName = model.FieldName;
                                var organic = new OrganicManure
                                {
                                    ManagementPeriodID = organicManure.ManagementPeriodID,
                                    ManureTypeID = organicManure.ManureTypeID,
                                    ManureTypeName = organicManure.ManureTypeName,
                                    ApplicationDate = organicManure.ApplicationDate.Value.ToLocalTime(),
                                    Confirm = organicManure.Confirm,
                                    N = organicManure.N,
                                    P2O5 = organicManure.P2O5,
                                    K2O = organicManure.K2O,
                                    MgO = organicManure.MgO,
                                    SO3 = organicManure.SO3,
                                    AvailableN = organicManure.AvailableN,
                                    ApplicationRate = organicManure.ApplicationRate,
                                    DryMatterPercent = organicManure.DryMatterPercent,
                                    UricAcid = organicManure.UricAcid,
                                    EndOfDrain = organicManure.EndOfDrain.ToLocalTime(),
                                    Rainfall = organicManure.Rainfall,
                                    AreaSpread = organicManure.AreaSpread,
                                    ManureQuantity = organicManure.ManureQuantity,
                                    ApplicationMethodID = organicManure.ApplicationMethodID,
                                    IncorporationMethodID = organicManure.IncorporationMethodID,
                                    IncorporationDelayID = organicManure.IncorporationDelayID,
                                    NH4N = organicManure.NH4N,
                                    NO3N = organicManure.NO3N,
                                    AvailableP2O5 = organicManure.AvailableP2O5,
                                    AvailableK2O = organicManure.AvailableK2O,
                                    AvailableSO3 = organicManure.AvailableSO3,
                                    WindspeedID = organicManure.WindspeedID,
                                    RainfallWithinSixHoursID = organicManure.RainfallWithinSixHoursID,
                                    MoistureID = organicManure.MoistureID,
                                    AutumnCropNitrogenUptake = organicManure.AutumnCropNitrogenUptake,
                                    AvailableNForNMax = organicManure.AvailableNForNMax,
                                    FieldID = fieldIdForUpdate,
                                    Defoliation = organicManure.Defoliation,
                                    DefoliationName = organicManure.DefoliationName,
                                    EncryptedCounter = organicManure.EncryptedCounter,
                                    FieldName = model.FieldName

                                };
                                model.OrganicManures.Add(organic);

                            }
                            model.IsSameDefoliationForAll = true;
                            //model.DefoliationCurrentCounter = 1;
                            model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                            model.HarvestYear = decryptedHarvestYear;
                            model.FarmId = decryptedFarmId;
                            model.EncryptedHarvestYear = s;
                            model.EncryptedFarmId = r;
                            model.ManureTypeId = organicManure.ManureTypeID;
                            model.ManureTypeName = organicManure.ManureTypeName;
                            model.ApplicationDate = organicManure.ApplicationDate?.ToLocalTime();
                            model.ApplicationMethod = organicManure.ApplicationMethodID;
                            (model.ApplicationMethodName, error) = await _organicManureService.FetchApplicationMethodById(model.ApplicationMethod.Value);

                            if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                            {
                                model.OtherMaterialName = model.ManureTypeName;
                            }
                            if (error != null && !string.IsNullOrWhiteSpace(error.Message))
                            {
                                _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                {
                                    id = model.EncryptedFarmId,
                                    year = model.EncryptedHarvestYear
                                });
                            }

                            (ManureType manureType, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                            if (error == null && manureType != null)
                            {
                                model.IsManureTypeLiquid = manureType.IsLiquid;
                                model.ManureGroupId = manureType.ManureGroupId;
                                model.ManureGroupIdForFilter = manureType.ManureGroupId;
                                //model.ManureTypeName = manureType.Name;
                                model.ApplicationRateArable = manureType.ApplicationRateArable;
                            }
                            //model.ManureGroupId = (await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value)).Item1.ManureGroupId;
                            model.N = organicManure.N;
                            model.P2O5 = organicManure.P2O5;
                            model.MgO = organicManure.MgO;
                            model.NH4N = organicManure.NH4N;
                            model.NO3N = organicManure.NO3N;
                            model.SO3 = organicManure.SO3;
                            model.K2O = organicManure.K2O;
                            model.DryMatterPercent = organicManure.DryMatterPercent;
                            model.UricAcid = organicManure.UricAcid;
                            model.ManureType.TotalN = organicManure.N;
                            model.ManureType.P2O5 = organicManure.P2O5;
                            model.ManureType.MgO = organicManure.MgO;
                            model.ManureType.NH4N = organicManure.NH4N;
                            model.ManureType.NO3N = organicManure.NO3N;
                            model.ManureType.SO3 = organicManure.SO3;
                            model.ManureType.K2O = organicManure.K2O;
                            model.ManureType.DryMatter = organicManure.DryMatterPercent;
                            model.ManureType.Uric = organicManure.UricAcid;

                            (List<FarmManureTypeResponse> farmManureTypeResponse, error) = await _organicManureService.FetchFarmManureTypeByFarmId(model.FarmId.Value);
                            if (error == null && farmManureTypeResponse != null && farmManureTypeResponse.Count > 0)
                            {
                                FarmManureTypeResponse farmManureType = farmManureTypeResponse.Where(x => x.ManureTypeID == model.ManureTypeId && x.ManureTypeName == model.ManureTypeName).FirstOrDefault();
                                if (farmManureType != null)
                                {
                                    if (model.ManureTypeId != null && (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials) &&
                                   farmManureType.ManureTypeName.Equals(organicManure.ManureTypeName))
                                    {
                                        if (farmManureType.TotalN == model.N && farmManureType.P2O5 == model.P2O5 &&
                                        farmManureType.DryMatter == model.DryMatterPercent && farmManureType.Uric == model.UricAcid &&
                                        farmManureType.NH4N == model.NH4N && farmManureType.NO3N == model.NO3N &&
                                        farmManureType.SO3 == model.SO3 && farmManureType.K2O == model.K2O &&
                                        farmManureType.MgO == model.MgO)
                                        {
                                            model.DefaultNutrientValue = Resource.lblYes;
                                        }
                                    }
                                    else
                                    {
                                        if (farmManureType.TotalN == model.N && farmManureType.P2O5 == model.P2O5 &&
                                        farmManureType.DryMatter == model.DryMatterPercent && farmManureType.Uric == model.UricAcid &&
                                        farmManureType.NH4N == model.NH4N && farmManureType.NO3N == model.NO3N &&
                                        farmManureType.SO3 == model.SO3 && farmManureType.K2O == model.K2O &&
                                        farmManureType.MgO == model.MgO)
                                        {
                                            model.DefaultNutrientValue = Resource.lblYesUseTheseValues;
                                        }
                                    }

                                    if (model.ManureTypeId != null && (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials) &&
                                        farmManureType.ManureTypeName.Equals(organicManure.ManureTypeName))
                                    {
                                        model.ManureGroupId = organicManure.ManureTypeID;
                                        model.ManureGroupIdForFilter = organicManure.ManureTypeID;
                                        model.OrganicManures.ForEach(x => x.SoilDrainageEndDate = x.EndOfDrain.ToLocalTime());
                                    }
                                    model.DefaultFarmManureValueDate = farmManureType.ModifiedOn == null ? farmManureType.CreatedOn : farmManureType.ModifiedOn;
                                }
                                else
                                {
                                    model.DefaultNutrientValue = Resource.lblYes;
                                }
                            }
                            else if (farmManureTypeResponse.Count == 0)
                            {
                                model.DefaultNutrientValue = Resource.lblYes;
                            }
                            if (string.IsNullOrWhiteSpace(model.DefaultNutrientValue))
                            {
                                if (manureType.TotalN == model.N && manureType.MgO == model.MgO && manureType.P2O5 == model.P2O5 &&
                                            manureType.NH4N == model.NH4N && manureType.NO3N == model.NO3N
                                            && manureType.SO3 == model.SO3 && manureType.K2O == model.K2O
                                            && manureType.DryMatter == model.DryMatterPercent && manureType.Uric == model.UricAcid)
                                {
                                    model.DefaultNutrientValue = Resource.lblYesUseTheseStandardNutrientValues;
                                }
                                else
                                {
                                    model.DefaultNutrientValue = Resource.lblIwantToEnterARecentOrganicMaterialAnalysis;
                                }
                            }
                            model.ManureTypeName = organicManure.ManureTypeName;
                            model.ApplicationRate = organicManure.ApplicationRate;
                            model.SoilDrainageEndDate = organicManure.EndOfDrain.ToLocalTime();
                            int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                            //(List<ManureType> manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupId.Value, countryId);
                            //if (error == null && manureTypeList.Count > 0)
                            //{
                            //    model.ManureTypeName = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.Name;
                            //    model.ApplicationRateArable = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId)?.ApplicationRateArable;
                            //}
                            //else
                            //{
                            //    _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                            //    TempData["ErrorOnHarvestYearOverview"] = error.Message;
                            //    return RedirectToAction("HarvestYearOverview", "Crop", new
                            //    {
                            //        id = model.EncryptedFarmId,
                            //        year = model.EncryptedHarvestYear
                            //    });

                            //}
                            if (organicManure.AreaSpread != null && organicManure.ManureQuantity != null)
                            {
                                model.Area = organicManure.AreaSpread;
                                model.Quantity = organicManure.ManureQuantity;
                                model.ApplicationRateMethod = (int)NMP.Portal.Enums.ApplicationRate.CalculateBasedOnAreaAndQuantity;
                            }
                            else if (model.ApplicationRateArable == model.ApplicationRate)
                            {
                                model.ApplicationRateMethod = (int)NMP.Portal.Enums.ApplicationRate.UseDefaultApplicationRate;
                            }
                            else
                            {
                                model.ApplicationRateMethod = (int)NMP.Portal.Enums.ApplicationRate.EnterAnApplicationRate;
                            }
                            model.IncorporationDelay = organicManure.IncorporationDelayID;

                            (model.IncorporationDelayName, error) = await _organicManureService.FetchIncorporationDelayById(model.IncorporationDelay.Value);
                            if (error != null && string.IsNullOrWhiteSpace(error.Message))
                            {
                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                {
                                    id = model.EncryptedFarmId,
                                    year = model.EncryptedHarvestYear
                                });
                            }
                            model.IncorporationMethod = organicManure.IncorporationMethodID;
                            (model.IncorporationMethodName, error) = await _organicManureService.FetchIncorporationMethodById(model.IncorporationMethod.Value);
                            if (error != null && string.IsNullOrWhiteSpace(error.Message))
                            {
                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                {
                                    id = model.EncryptedFarmId,
                                    year = model.EncryptedHarvestYear
                                });
                            }
                            model.MoistureTypeId = organicManure.MoistureID;
                            (MoistureTypeResponse moistureTypeResponse, error) = await _organicManureService.FetchMoisterTypeById(model.MoistureTypeId.Value);
                            if (error != null && string.IsNullOrWhiteSpace(error.Message))
                            {
                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                {
                                    id = model.EncryptedFarmId,
                                    year = model.EncryptedHarvestYear
                                });
                            }
                            else if (moistureTypeResponse != null)
                            {
                                model.MoistureType = moistureTypeResponse.Name;
                            }

                            model.RainfallWithinSixHoursID = organicManure.RainfallWithinSixHoursID;
                            (RainTypeResponse rainTypeResponse, error) = await _organicManureService.FetchRainTypeById(model.RainfallWithinSixHoursID.Value);
                            if (error != null && string.IsNullOrWhiteSpace(error.Message))
                            {
                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                {
                                    id = model.EncryptedFarmId,
                                    year = model.EncryptedHarvestYear
                                });
                            }
                            else if (rainTypeResponse != null)
                            {
                                model.RainfallWithinSixHours = rainTypeResponse.Name;
                            }
                            model.WindspeedID = organicManure.WindspeedID;
                            (WindspeedResponse windspeedResponse, error) = await _organicManureService.FetchWindspeedById(model.WindspeedID.Value);
                            if (error != null && string.IsNullOrWhiteSpace(error.Message))
                            {
                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                {
                                    id = model.EncryptedFarmId,
                                    year = model.EncryptedHarvestYear
                                });
                            }
                            else if (windspeedResponse != null)
                            {
                                model.Windspeed = windspeedResponse.Name;
                            }
                            model.SoilDrainageEndDate = organicManure.EndOfDrain.ToLocalTime();
                            model.TotalRainfall = organicManure.Rainfall;
                            model.FieldGroup = Resource.lblSelectSpecificFields;
                            if (model.FieldList != null && model.FieldList.Count > 0)
                            {
                                (CropTypeResponse cropsResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList.FirstOrDefault()), model.HarvestYear.Value, false);
                                if (model.AutumnCropNitrogenUptakes == null)
                                {
                                    model.AutumnCropNitrogenUptakes = new List<AutumnCropNitrogenUptakeDetail>();
                                }
                                var fieldData = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(model.FieldList.FirstOrDefault()));
                                model.AutumnCropNitrogenUptakes.Add(new AutumnCropNitrogenUptakeDetail
                                {
                                    EncryptedFieldId = _organicManureProtector.Protect(model.FieldList.FirstOrDefault()),
                                    FieldName = fieldData.Name ?? string.Empty,
                                    CropTypeId = cropsResponse.CropTypeId,
                                    CropTypeName = cropsResponse.CropType,
                                    AutumnCropNitrogenUptake = organicManure.AutumnCropNitrogenUptake
                                });
                            }

                            foreach (var updateOrganic in model.UpdatedOrganicIds)
                            {
                                if (!model.OrganicManures.Any(x => x.ManagementPeriodID == updateOrganic.ManagementPeriodId))
                                {
                                    (ManagementPeriod managementPeriod, Error updateOrganicError) = await _cropService.FetchManagementperiodById(updateOrganic.ManagementPeriodId.Value);
                                    if (managementPeriod != null)
                                    {
                                        (Crop crop, updateOrganicError) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (crop != null)
                                        {
                                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(crop.FieldID.Value);
                                            if (cropList != null && cropList.Count > 0)
                                            {
                                                cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                                if (cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
                                                {
                                                    int cropId = cropList.Where(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).
                                                        Select(x => x.ID.Value).FirstOrDefault();
                                                    (List<ManagementPeriod> managementPeriods, updateOrganicError) = await _cropService.FetchManagementperiodByCropId(cropId, false);
                                                    if (managementPeriods.Count > 0)
                                                    {
                                                        updateOrganic.ManagementPeriodId = managementPeriods.Select(x => x.ID).FirstOrDefault();
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        }
                    }
                }
                else
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                }
                if (string.IsNullOrWhiteSpace(s))
                {
                    (List<CommonResponse> fieldList, error) = await _organicManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
                    if (error == null)
                    {
                        if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll))
                        {
                            if (fieldList.Count > 0)
                            {
                                var fieldNames = fieldList
                                                 .Where(field => model.FieldList.Contains(field.Id.ToString())).OrderBy(field => field.Name)
                                                 .Select(field => field.Name)
                                                 .ToList();
                                ViewBag.SelectedFields = fieldNames.OrderBy(name => name).ToList();
                                //ViewBag.Fields = fieldList;
                                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                {
                                    ViewBag.Fields = fieldList;
                                }
                                if (model.FieldList != null && model.FieldList.Count == 1 && fieldNames != null)
                                {
                                    model.FieldName = fieldNames.FirstOrDefault();
                                }
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                        {
                            (List<FertiliserAndOrganicManureUpdateResponse> organicResponse, error) = await _organicManureService.FetchFieldWithSameDateAndManureType(Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedOrgManureId)), model.FarmId.Value, model.HarvestYear.Value);
                            if (string.IsNullOrWhiteSpace(error.Message) && organicResponse != null && organicResponse.Count > 0)
                            {
                                var SelectListItem = organicResponse.Select(f => new SelectListItem
                                {
                                    Value = f.Id.ToString(),
                                    Text = f.Name.ToString()
                                }).ToList().DistinctBy(x => x.Value);
                                ViewBag.Fields = SelectListItem.OrderBy(x => x.Text).ToList();
                            }
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                        {
                            TempData["ConditionsAffectingNutrientsError"] = error.Message;
                            return RedirectToAction("ConditionsAffectingNutrients");
                        }
                        else
                        {
                            TempData["ErrorOnHarvestYearOverview"] = error.Message;
                            _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                            return RedirectToAction("HarvestYearOverview", "Crop", new
                            {
                                id = model.EncryptedFarmId,
                                year = model.EncryptedHarvestYear
                            });
                        }
                    }
                }
                string message = string.Empty;
                model.IsOrgManureNfieldLimitWarning = false;
                model.IsNMaxLimitWarning = false;
                model.IsAnyChangeInField = false;
                model.IsEndClosedPeriodFebruaryWarning = false;
                model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = false;
                if (model.FieldList != null && model.FieldList.Count > 0)
                {
                    foreach (var fieldId in model.FieldList)
                    {
                        Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                        if (field != null)
                        {
                            bool isFieldIsInNVZ = field.IsWithinNVZ != null ? field.IsWithinNVZ.Value : false;
                            if (isFieldIsInNVZ)
                            {
                                (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, null, null);
                                if (error == null)
                                {
                                    if (managementIds.Count > 0)
                                    {
                                        (model, error) = await IsNFieldLimitWarningMessage(model, isFieldIsInNVZ, managementIds[0], true, Convert.ToInt32(fieldId));
                                        if (error == null)
                                        {
                                            (model, error) = await IsNMaxWarningMessage(model, Convert.ToInt32(fieldId), managementIds[0], true);
                                            if (error == null)
                                            {
                                                if (!(model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials))
                                                {
                                                    (model, error) = await IsEndClosedPeriodFebruaryWarningMessage(model, Convert.ToInt32(fieldId), true);
                                                    if (error != null)
                                                    {
                                                        if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                                        {
                                                            TempData["ConditionsAffectingNutrientsError"] = error.Message;
                                                            return RedirectToAction("ConditionsAffectingNutrients");
                                                        }
                                                        else
                                                        {
                                                            TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                                            _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                                            return RedirectToAction("HarvestYearOverview", "Crop", new
                                                            {
                                                                id = model.EncryptedFarmId,
                                                                year = model.EncryptedHarvestYear
                                                            });
                                                        }
                                                    }
                                                }

                                            }
                                            else
                                            {
                                                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                                {
                                                    TempData["ConditionsAffectingNutrientsError"] = error.Message;
                                                    return RedirectToAction("ConditionsAffectingNutrients");
                                                }
                                                else
                                                {
                                                    TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                                    _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                                    return RedirectToAction("HarvestYearOverview", "Crop", new
                                                    {
                                                        id = model.EncryptedFarmId,
                                                        year = model.EncryptedHarvestYear
                                                    });
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                            {
                                                TempData["ConditionsAffectingNutrientsError"] = error.Message;
                                                return RedirectToAction("ConditionsAffectingNutrients");
                                            }
                                            else
                                            {
                                                TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                                _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                                return RedirectToAction("HarvestYearOverview", "Crop", new
                                                {
                                                    id = model.EncryptedFarmId,
                                                    year = model.EncryptedHarvestYear
                                                });
                                            }
                                        }

                                        if (!(model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials))
                                        {
                                            (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, message, error) = await IsClosedPeriodStartAndEndFebExceedNRateException(model, Convert.ToInt32(fieldId));
                                            if (error == null)
                                            {
                                                if (!string.IsNullOrWhiteSpace(message))
                                                {
                                                    TempData["AppRateExceeds150WithinClosedPeriodOrganic"] = message;
                                                }
                                            }
                                            else
                                            {
                                                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                                {
                                                    TempData["ConditionsAffectingNutrientsError"] = error.Message;
                                                    return RedirectToAction("ConditionsAffectingNutrients");
                                                }
                                                else
                                                {
                                                    TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                                    _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                                    return RedirectToAction("HarvestYearOverview", "Crop", new
                                                    {
                                                        id = model.EncryptedFarmId,
                                                        year = model.EncryptedHarvestYear
                                                    });
                                                }
                                            }
                                        }

                                    }
                                }
                                else
                                {
                                    if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                    {
                                        TempData["ConditionsAffectingNutrientsError"] = error.Message;
                                        return RedirectToAction("ConditionsAffectingNutrients");
                                    }
                                    else
                                    {
                                        TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                        _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                        return RedirectToAction("HarvestYearOverview", "Crop", new
                                        {
                                            id = model.EncryptedFarmId,
                                            year = model.EncryptedHarvestYear
                                        });
                                    }
                                }
                            }
                        }
                    }
                }


                model.IsClosedPeriodWarning = false;
                model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks = false;
                if (int.TryParse(model.FieldGroup, out int value) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList != null && model.FieldList.Count == 1))
                {
                    int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId));
                    (Farm farm, error) = await _farmService.FetchFarmByIdAsync(farmId);
                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                    {
                        if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                        {
                            TempData["ConditionsAffectingNutrientsError"] = error.Message;
                            return RedirectToAction("ConditionsAffectingNutrients");
                        }
                        else
                        {
                            TempData["ErrorOnHarvestYearOverview"] = error.Message;
                            _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                            return RedirectToAction("HarvestYearOverview", "Crop", new
                            {
                                id = model.EncryptedFarmId,
                                year = model.EncryptedHarvestYear
                            });
                        }
                    }
                    else
                    {
                        if (farm != null)
                        {
                            bool nonRegisteredOrganicProducer = farm.RegisteredOrganicProducer.Value;
                            if (model.FieldList != null && model.FieldList.Count > 0)
                            {
                                foreach (var fieldId in model.FieldList)
                                {
                                    Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                                    if (field != null)
                                    {

                                        if (field.IsWithinNVZ.Value)
                                        {
                                            if (!(model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials))
                                            {
                                                Crop crop = null;
                                                CropTypeLinkingResponse cropTypeLinkingResponse = new CropTypeLinkingResponse();
                                                if (model.OrganicManures.Any(x => x.FieldID == Convert.ToInt32(fieldId)))
                                                {
                                                    int manId = model.OrganicManures.Where(x => x.FieldID == Convert.ToInt32(fieldId)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                                    (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID ?? 0);
                                                }
                                                //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                                                if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                                                {
                                                    (model, error) = await IsClosedPeriodWarningMessage(model, field.IsWithinNVZ.Value, farm.RegisteredOrganicProducer.Value, true);

                                                    if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
                                                    {
                                                        if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                                                        {
                                                            TempData["ConditionsAffectingNutrientsError"] = error.Message;
                                                            return RedirectToAction("ConditionsAffectingNutrients");
                                                        }
                                                        else
                                                        {
                                                            TempData["ErrorOnHarvestYearOverview"] = error.Message;
                                                            _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                                            return RedirectToAction("HarvestYearOverview", "Crop", new
                                                            {
                                                                id = model.EncryptedFarmId,
                                                                year = model.EncryptedHarvestYear
                                                            });
                                                        }
                                                    }
                                                }


                                            }


                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (model.IsNMaxLimitWarning || model.IsOrgManureNfieldLimitWarning || model.IsClosedPeriodWarning || model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks)
                {
                    model.IsWarningMsgNeedToShow = true;
                }
                model.IsCheckAnswer = true;
                model.IsManureTypeChange = false;
                model.IsApplicationMethodChange = false;
                model.IsFieldGroupChange = false;
                model.IsIncorporationMethodChange = false;
                model.IsApplicationDateChange = false;
                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                {
                    foreach (var organicManure in model.OrganicManures)
                    {
                        if (organicManure.ApplicationRate == null)
                        {
                            organicManure.ManureTypeID = model.ManureTypeId.Value;
                            organicManure.ManureTypeName = model.ManureTypeName;
                            organicManure.ApplicationMethodID = model.ApplicationMethod;
                            organicManure.N = model.OrganicManures.Select(x => x.N).FirstOrDefault();
                            organicManure.P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault();
                            organicManure.NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault();
                            organicManure.K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault();
                            organicManure.MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault();
                            organicManure.NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault();
                            organicManure.Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault();
                            organicManure.SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault();
                            organicManure.ApplicationDate = model.ApplicationDate.Value;
                            organicManure.ApplicationRate = model.ApplicationRate;
                            organicManure.AreaSpread = model.Area;
                            organicManure.ManureQuantity = model.Quantity;
                            organicManure.DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault();
                            organicManure.UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault();
                            organicManure.EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime();
                            organicManure.SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime();
                            organicManure.Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault();
                            organicManure.RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault();
                            organicManure.WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault();
                            organicManure.MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault();
                            organicManure.IncorporationDelayID = model.IncorporationDelay;
                            organicManure.IncorporationMethodID = model.IncorporationMethod;
                            organicManure.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes
                                .Where(x => x.FieldName == model.FieldName)
                                .Select(x => x.AutumnCropNitrogenUptake)
                                .FirstOrDefault();
                        }
                    }
                }

                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in CheckAnswer() action : {ex.Message}, {ex.StackTrace}");
                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                {
                    TempData["ConditionsAffectingNutrientsError"] = ex.Message;
                    return RedirectToAction("ConditionsAffectingNutrients");
                }
                else
                {
                    TempData["ErrorOnHarvestYearOverview"] = ex.Message;
                    _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                    return RedirectToAction("HarvestYearOverview", "Crop", new
                    {
                        id = model.EncryptedFarmId,
                        year = model.EncryptedHarvestYear
                    });
                }
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckAnswer(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : CheckAnswer() post action called");
            Error error = null;
            try
            {
                if (model.ManureTypeId == null)
                {
                    ModelState.AddModelError("ManureTypeId", Resource.MsgManureTypeNotSet);
                }

                List<string> grassFieldIds = new List<string>();
                foreach (string field in model.FieldList)
                {
                    Crop crop = null;
                    if (model.OrganicManures.Any(x => x.FieldID == Convert.ToInt32(field)))
                    {
                        int manId = model.OrganicManures.Where(x => x.FieldID == Convert.ToInt32(field)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                    }
                    int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(field))?.CropOrder
                       ?? crop?.CropOrder.Value ?? 1;
                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));

                    if (cropList.Count > 0)
                    {
                        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();
                    }

                    if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                    {
                        grassFieldIds.Add(field);
                    }
                }
                if (model.GrassCropCount.HasValue && model.GrassCropCount > 1 && model.IsSameDefoliationForAll == null)
                {
                    ModelState.AddModelError("IsSameDefoliationForAll", string.Format("{0} {1}", Resource.lblForMultipleDefoliation, Resource.lblNotSet));
                }
                int i = 0;
                foreach (var organic in model.OrganicManures)
                {
                    if (grassFieldIds.Any(x => x == organic.FieldID.ToString()) && organic.Defoliation == null)
                    {
                        if (model.IsSameDefoliationForAll.HasValue && (model.IsSameDefoliationForAll.Value))
                        {
                            ModelState.AddModelError(string.Concat("OrganicManures[", i, "].Defoliation"), string.Format("{0} {1}", string.Format(Resource.lblWhichCutOrGrazingInThisApplicationForInField, organic.FieldName), Resource.lblNotSet));
                        }
                        else
                        {
                            ModelState.AddModelError(string.Concat("OrganicManures[", i, "].Defoliation"), string.Format("{0} {1}", Resource.lblWhichCutOrGrazingInThisApplicationForAllField, Resource.lblNotSet));
                        }
                    }
                    i++;
                }

                if (model.ApplicationMethod == null)
                {
                    ModelState.AddModelError("ApplicationMethod", string.Format(Resource.MsgApplicationMethodNotSet, model.ManureTypeName));
                }
                if (model.ApplicationDate == null)
                {
                    ModelState.AddModelError("ApplicationDate", string.Format(Resource.MsgApplyingDateNotSet, model.ManureTypeName));
                }
                if (model.DefaultNutrientValue == null)
                {
                    ModelState.AddModelError("DefaultNutrientValue", string.Format(Resource.MsgDefaultNutrientValuesNotSet, model.ManureTypeName));
                }
                if (model.ApplicationRateMethod == null)
                {
                    ModelState.AddModelError("ApplicationRateMethod", string.Format(Resource.MsgApplicationRateMethodNotSet, model.ManureTypeName));
                }
                if (model.ApplicationRate == null)
                {
                    ModelState.AddModelError("ApplicationRate", Resource.MsgApplicationRateNotSet);
                }
                if (model.ApplicationRateMethod == (int)NMP.Portal.Enums.ApplicationRate.CalculateBasedOnAreaAndQuantity)
                {
                    if (model.Area == null)
                    {
                        ModelState.AddModelError("Area", Resource.MsgAreaNotSet);
                    }
                    if (model.Quantity == null)
                    {
                        ModelState.AddModelError("Quantity", Resource.MsgQuantityNotSet);
                    }
                }
                if (model.IncorporationMethod == null)
                {
                    ModelState.AddModelError("IncorporationMethod", string.Format(Resource.MsgIncorporationMethodNotSet, model.ManureTypeName));
                }
                if (model.IncorporationDelay == null)
                {
                    ModelState.AddModelError("IncorporationDelay", string.Format(Resource.MsgIncorporationDelayNotSet, model.ManureTypeName));
                }
                //if (model.AutumnCropNitrogenUptake == null)
                //{
                //    ModelState.AddModelError("AutumnCropNitrogenUptake", Resource.MsgAutumnCropNitrogenUptakeNotSet);
                //}
                if (model.SoilDrainageEndDate == null)
                {
                    ModelState.AddModelError("SoilDrainageEndDate", Resource.MsgEndOfSoilDrainageNotSet);
                }
                if (model.RainfallWithinSixHoursID == null)
                {
                    ModelState.AddModelError("RainfallWithinSixHoursID", Resource.MsgRainfallWithinSixHoursOfApplicationNotSet);
                }
                if (model.TotalRainfall == null)
                {
                    ModelState.AddModelError("TotalRainfall", Resource.MsgTotalRainfallSinceApplicationNotSet);
                }
                if (model.WindspeedID == null)
                {
                    ModelState.AddModelError("WindspeedID", Resource.MsgWindspeedAtApplicationNotSet);
                }
                if (model.MoistureTypeId == null)
                {
                    ModelState.AddModelError("MoistureTypeId", Resource.MsgTopsoilMoistureNotSet);
                }
                if (model.DoubleCrop == null && model.IsDoubleCropAvailable)
                {
                    int index = 0;
                    List<Crop> cropList = new List<Crop>();
                    string cropTypeName = string.Empty;
                    if (model.DoubleCrop == null)
                    {
                        foreach (string fieldId in model.FieldList)
                        {
                            cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                            if (cropList != null && cropList.Count == 2)
                            {
                                ModelState.AddModelError("MoistureTypeId", string.Format("{0} {1}", string.Format(Resource.lblWhichCropIsThisManureApplication, (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name), Resource.lblNotSet));
                                index++;
                            }
                        }
                    }

                }
                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                List<string> fieldListCopy = new List<string>(model.FieldList);

                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                {
                    foreach (var field in grassFieldIds)
                    {
                        fieldListCopy.Remove(field);
                    }

                    List<int> managementIds = new List<int>();
                    //string arableFieldIds = fieldListCopy.Count > 0 ? string.Join(",", fieldListCopy) : string.Empty;
                    if (fieldListCopy.Count > 0)
                    {
                        foreach (string fieldIdForManID in fieldListCopy)
                        {
                            if (!model.OrganicManures.Any(x => x.FieldID == Convert.ToInt32(fieldIdForManID)))
                            {
                                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldIdForManID));
                                if (cropList != null && cropList.Count > 0)
                                {
                                    int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldIdForManID))?.CropOrder
                       ?? 1;
                                    if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields))
                                    {
                                        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();
                                    }
                                    else
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();

                                        }
                                    }
                                    if (cropList.Count > 0)
                                    {
                                        model.CropOrder = Convert.ToInt32(cropList.Select(x => x.CropOrder).FirstOrDefault());
                                        model.FieldID = Convert.ToInt32(cropList.Select(x => x.FieldID).FirstOrDefault());
                                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                                    }
                                }

                                (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, model.CropOrder);

                                if (managementIds.Count > 0)
                                {
                                    foreach (var manIds in managementIds)
                                    {
                                        var organic = new OrganicManure
                                        {
                                            ManagementPeriodID = manIds,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName,
                                            ManureTypeID = model.ManureTypeId.Value,
                                            ManureTypeName = model.ManureTypeName,
                                            ApplicationMethodID = model.ApplicationMethod,
                                            N = model.OrganicManures.Select(x => x.N).FirstOrDefault(),
                                            P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault(),
                                            NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault(),
                                            K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault(),
                                            MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault(),
                                            NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault(),
                                            Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault(),
                                            SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault(),
                                            ApplicationDate = model.ApplicationDate.Value,
                                            ApplicationRate = model.ApplicationRate,
                                            AreaSpread = model.Area,
                                            ManureQuantity = model.Quantity,
                                            DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault(),
                                            UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault(),
                                            EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault(),
                                            RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault(),
                                            WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault(),
                                            MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault(),
                                            IncorporationDelayID = model.IncorporationDelay,
                                            IncorporationMethodID = model.IncorporationMethod,
                                            AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes.Where(x => x.FieldName == model.FieldName).Select(x => x.AutumnCropNitrogenUptake).FirstOrDefault()
                                        };
                                        model.OrganicManures.Add(organic);
                                    }
                                }
                            }
                        }
                    }
                    if (model.DoubleCrop != null && model.DoubleCrop.Count > 0)
                    {
                        foreach (var doubleCrop in model.DoubleCrop)
                        {
                            (List<ManagementPeriod> managementPeriods, error) = await _cropService.FetchManagementperiodByCropId(doubleCrop.CropID, true);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriods?.Count > 0)
                            {
                                var managementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();

                                var organicManure = model.OrganicManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (organicManure != null)
                                    {
                                        organicManure.ManagementPeriodID = managementPeriodId;
                                    }
                                }
                            }
                        }
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    //if (!string.IsNullOrWhiteSpace(arableFieldIds))
                    //{
                    //    (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, arableFieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, null);//1 is CropOrder    
                    //}

                    //if (error == null)
                    //{
                    //    if (managementIds.Count > 0)
                    //    {
                    //        foreach (int manid in managementIds)
                    //        {
                    //            var organic = new OrganicManure
                    //            {
                    //                ManagementPeriodID = manid
                    //            };
                    //            model.OrganicManures.Add(organic);
                    //        }
                    //    }
                    //}
                }
                else
                {

                    if (model.DoubleCrop?.Count > 0)
                    {
                        foreach (var doubleCrop in model.DoubleCrop)
                        {
                            (List<ManagementPeriod> managementPeriods, error) = await _cropService.FetchManagementperiodByCropId(doubleCrop.CropID, true);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriods?.Count > 0)
                            {
                                var managementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();

                                var organicManure = model.OrganicManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (organicManure != null)
                                    {
                                        organicManure.ManagementPeriodID = managementPeriodId;
                                    }
                                    else
                                    {
                                        model.OrganicManures.Add(new OrganicManure
                                        {
                                            ManagementPeriodID = managementPeriodId,
                                            FieldID = doubleCrop.FieldID,
                                            FieldName = doubleCrop.FieldName,
                                            ManureTypeID = model.ManureTypeId.Value,
                                            ManureTypeName = model.ManureTypeName,
                                            ApplicationMethodID = model.ApplicationMethod,
                                            N = model.OrganicManures.Select(x => x.N).FirstOrDefault(),
                                            P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault(),
                                            NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault(),
                                            K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault(),
                                            MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault(),
                                            NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault(),
                                            Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault(),
                                            SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault(),
                                            ApplicationDate = model.ApplicationDate.Value,
                                            ApplicationRate = model.ApplicationRate,
                                            AreaSpread = model.Area,
                                            ManureQuantity = model.Quantity,
                                            DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault(),
                                            UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault(),
                                            EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault(),
                                            RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault(),
                                            WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault(),
                                            MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault(),
                                            IncorporationDelayID = model.IncorporationDelay,
                                            IncorporationMethodID = model.IncorporationMethod,
                                            AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes.Where(x => x.FieldName == model.FieldName).Select(x => x.AutumnCropNitrogenUptake).FirstOrDefault()
                                        });
                                    }
                                }
                            }
                        }
                    }

                    List<string> targetFieldIds;

                    // If DoubleCrop exists, remove its FieldIDs from fieldListCopy
                    if (model.DoubleCrop?.Count > 0)
                    {
                        var doubleCropFieldIds = model.DoubleCrop
                            .Select(dc => dc.FieldID.ToString())
                            .ToHashSet();

                        targetFieldIds = fieldListCopy
                            .Where(id => !doubleCropFieldIds.Contains(id))
                            .ToList();
                    }
                    else
                    {
                        targetFieldIds = fieldListCopy;
                    }
                    if (!(model.OrganicManures != null && model.OrganicManures.Count > 0))
                    {
                        model.OrganicManures = new List<OrganicManure>();
                    }
                    if (model.OrganicManures.Count < model.FieldList.Count && targetFieldIds?.Count > 0)
                    {
                        foreach (string fieldIdStr in targetFieldIds)
                        {
                            int fieldId = Convert.ToInt32(fieldIdStr);
                            if (!model.OrganicManures.Any(x => x.FieldID == fieldId))
                            {

                                var cropList = await _cropService.FetchCropsByFieldId(fieldId);

                                if (cropList?.Count > 0)
                                {
                                    if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields))
                                    {
                                        cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                    }
                                    else
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                        }
                                    }
                                    cropList = cropList
                                        .Where(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                        .ToList();

                                    if (cropList.Count > 0)
                                    {
                                        model.CropOrder = cropList.First().CropOrder ?? 1;
                                        model.FieldID = cropList.First().FieldID;
                                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                                    }
                                }

                                (List<int> managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(
                                    model.HarvestYear.Value,
                                    fieldIdStr,
                                    (model.FieldGroup == Resource.lblSelectSpecificFields || model.FieldGroup == Resource.lblAll)
                                        ? null
                                        : model.FieldGroup,
                                    model.CropOrder
                                );

                                if (managementIds?.Count > 0)
                                {
                                    foreach (var manId in managementIds)
                                    {
                                        model.OrganicManures.Add(new OrganicManure
                                        {
                                            ManagementPeriodID = manId,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName,
                                            ManureTypeID = model.ManureTypeId.Value,
                                            ManureTypeName = model.ManureTypeName,
                                            ApplicationMethodID = model.ApplicationMethod,
                                            N = model.OrganicManures.Select(x => x.N).FirstOrDefault(),
                                            P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault(),
                                            NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault(),
                                            K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault(),
                                            MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault(),
                                            NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault(),
                                            Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault(),
                                            SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault(),
                                            ApplicationDate = model.ApplicationDate.Value,
                                            ApplicationRate = model.ApplicationRate,
                                            AreaSpread = model.Area,
                                            ManureQuantity = model.Quantity,
                                            DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault(),
                                            UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault(),
                                            EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault(),
                                            RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault(),
                                            WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault(),
                                            MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault(),
                                            IncorporationDelayID = model.IncorporationDelay,
                                            IncorporationMethodID = model.IncorporationMethod,
                                            AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes.Where(x => x.FieldName == model.FieldName).Select(x => x.AutumnCropNitrogenUptake).FirstOrDefault()
                                        });
                                    }
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }

                }
                if (model.OrganicManures != null)
                {
                    model.OrganicManures.ForEach(x => x.EndOfDrain = x.SoilDrainageEndDate);
                    if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    {
                        model.OrganicManures.ForEach(x => x.ManureTypeName = model.OtherMaterialName);
                        if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                        {
                            model.OrganicManures.ForEach(x => x.ManureTypeID = model.ManureGroupIdForFilter ?? 0);
                        }
                    }
                    else
                    {
                        model.OrganicManures.ForEach(x => x.ManureTypeName = model.ManureTypeName);
                    }

                    //logic for AvailableNForNMax column that will be used to get sum of previous manure applications
                    int? percentOfTotalNForUseInNmaxCalculation = null;
                    decimal? currentApplicationNitrogen = null;
                    (ManureType manure, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId ?? 0);
                    if (manure != null)
                    {
                        percentOfTotalNForUseInNmaxCalculation = manure.PercentOfTotalNForUseInNmaxCalculation;
                    }
                    decimal totalNitrogen = 0;
                    if (percentOfTotalNForUseInNmaxCalculation != null)
                    {
                        if (model.OrganicManures != null && model.OrganicManures.Any())
                        {
                            foreach (var organicManure in model.OrganicManures)
                            {
                                totalNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                                break;
                            }

                            decimal decimalOfTotalNForUseInNmaxCalculation = Convert.ToDecimal(percentOfTotalNForUseInNmaxCalculation / 100.0);
                            if (model.ApplicationRate.HasValue)
                            {
                                currentApplicationNitrogen = (totalNitrogen * model.ApplicationRate.Value * decimalOfTotalNForUseInNmaxCalculation);
                            }
                        }
                    }

                   
                    //foreach (string field in model.FieldList)
                    //{
                    (Farm farmData, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                    if (farmData != null && (string.IsNullOrWhiteSpace(error.Message)))
                    {
                        //Field fieldData = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field));
                        //if (fieldData != null)
                        //{
                        foreach (var organic in model.OrganicManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                if (crop != null && string.IsNullOrWhiteSpace(error.Message))
                                {
                                    Field fieldData = await _fieldService.FetchFieldByFieldId(crop.FieldID.Value);
                                    if (fieldData != null)
                                    {
                                        (SoilTypeSoilTextureResponse soilTexture, error) = await _organicManureService.FetchSoilTypeSoilTextureBySoilTypeId(fieldData.SoilTypeID ?? 0);
                                        int topSoilID = 0;
                                        int subSoilID = 0;
                                        if (error == null && soilTexture != null)
                                        {
                                            topSoilID = soilTexture.TopSoilID;
                                            subSoilID = soilTexture.SubSoilID;
                                        }
                                        (CropTypeLinkingResponse cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID.Value);
                                        if (error == null && cropTypeLinkingResponse != null)
                                        {
                                            (ManureType manureType, error) = await _organicManureService.FetchManureTypeByManureTypeId(organic.ManureTypeID);
                                            if (error == null && manureType != null)
                                            {
                                                var mannerOutput = new
                                                {
                                                    runType = farmData.EnglishRules ? (int)NMP.Portal.Enums.RunType.MannerEngland : (int)NMP.Portal.Enums.RunType.MannerScotland,
                                                    postcode = farmData.ClimateDataPostCode.Split(" ")[0],
                                                    countryID = farmData.CountryID,
                                                    field = new
                                                    {
                                                        fieldID = fieldData.ID,
                                                        fieldName = fieldData.Name,
                                                        MannerCropTypeID = cropTypeLinkingResponse.MannerCropTypeID,
                                                        topsoilID = topSoilID,
                                                        subsoilID = subSoilID,
                                                        isInNVZ = Convert.ToBoolean(fieldData.IsWithinNVZ)
                                                    },
                                                    manureApplications = new[]
                                                 {
                                                        new
                                                        {
                                                            manureDetails = new
                                                            {
                                                                manureID = organic.ManureTypeID,
                                                                name = organic.ManureTypeName,
                                                                isLiquid = manureType.IsLiquid,
                                                                dryMatter = organic.DryMatterPercent,
                                                                totalN = organic.N,
                                                                nH4N = organic.NH4N,
                                                                uric = organic.UricAcid,
                                                                nO3N = organic.NO3N,
                                                                p2O5 = organic.P2O5,
                                                                k2O = organic.K2O,
                                                                sO3 = organic.SO3,
                                                                mgO = organic.MgO
                                                            },
                                                            applicationDate = organic.ApplicationDate?.ToString("yyyy-MM-dd"),
                                                            applicationRate = new
                                                            {
                                                                value = organic.ApplicationRate,
                                                                unit = model.IsManureTypeLiquid.Value ? Resource.lblMeterCubePerHectare : Resource.lblTonnesPerHectare
                                                            },
                                                            applicationMethodID = organic.ApplicationMethodID,
                                                            incorporationMethodID = organic.IncorporationMethodID,
                                                            incorporationDelayID = organic.IncorporationDelayID,
                                                            autumnCropNitrogenUptake = new
                                                            {
                                                                value = organic.AutumnCropNitrogenUptake,
                                                                unit = Resource.lblKgPerHectare
                                                            },
                                                            endOfDrainageDate = organic.SoilDrainageEndDate.ToString("yyyy-MM-dd"),
                                                            rainfallPostApplication = organic.Rainfall,
                                                            windspeedID = organic.WindspeedID,
                                                            rainTypeID = organic.RainfallWithinSixHoursID,
                                                            topsoilMoistureID = organic.MoistureID
                                                        }
                                                    }
                                                };
                                                //var mannerJsonData = new
                                                //{
                                                //    mannerOutput
                                                //};
                                                string mannerJsonString = JsonConvert.SerializeObject(mannerOutput);
                                                (MannerCalculateNutrientResponse mannerCalculateNutrientResponse, error) = await _organicManureService.FetchMannerCalculateNutrient(mannerJsonString);
                                                if (error == null && mannerCalculateNutrientResponse != null)
                                                {
                                                    organic.AvailableN = mannerCalculateNutrientResponse.CurrentCropAvailableN;
                                                    organic.AvailableSO3 = mannerCalculateNutrientResponse.CropAvailableSO3;
                                                    organic.AvailableP2O5 = mannerCalculateNutrientResponse.CropAvailableP2O5;
                                                    organic.AvailableK2O = mannerCalculateNutrientResponse.CropAvailableK2O;
                                                    organic.TotalN = mannerCalculateNutrientResponse.TotalN;
                                                    organic.TotalP2O5 = mannerCalculateNutrientResponse.TotalP2O5;
                                                    organic.TotalSO3 = mannerCalculateNutrientResponse.TotalSO3;
                                                    organic.TotalK2O = mannerCalculateNutrientResponse.TotalK2O;
                                                    organic.TotalMgO = mannerCalculateNutrientResponse.TotalMgO;
                                                    organic.AvailableNForNextYear = mannerCalculateNutrientResponse.FollowingCropYear2AvailableN;
                                                    organic.AvailableNForNextDefoliation = mannerCalculateNutrientResponse.NextGrassNCropCurrentYear;
                                                    organic.AvailableNForNMax = currentApplicationNitrogen != null ? currentApplicationNitrogen : mannerCalculateNutrientResponse.CurrentCropAvailableN;
                                                }
                                                else
                                                {
                                                    TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                                                    return View(model);
                                                }
                                            }
                                            else
                                            {
                                                TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                                                return View(model);
                                            }
                                        }
                                        else
                                        {
                                            TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                                            return View(model);
                                        }
                                    }
                                    else
                                    {
                                        TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                                        return View(model);
                                    }
                                }
                                else
                                {
                                    TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                                    return View(model);
                                }
                            }
                            else
                            {
                                TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                                return View(model);
                            }
                        }
                        //}
                    }
                    else
                    {
                        TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                        return View(model);
                    }
                    //}
                }
                var OrganicManures = new List<object>();

                List<WarningMessage> warningMessageList = new List<WarningMessage>();
                foreach (var orgManure in model.OrganicManures)
                {
                    int fieldTypeId = (int)NMP.Portal.Enums.FieldType.Arable;
                    (ManagementPeriod ManData, error) = await _cropService.FetchManagementperiodById(orgManure.ManagementPeriodID);
                    if (ManData != null)
                    {

                        (Crop crop, error) = (await _cropService.FetchCropById(ManData.CropID.Value));
                        if (crop != null)
                        {
                            fieldTypeId = (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass) ?
                             (int)NMP.Portal.Enums.FieldType.Grass : (int)NMP.Portal.Enums.FieldType.Arable;

                        }
                    }
                    warningMessageList = new List<WarningMessage>();
                    warningMessageList = await GetWarningMessages(model);

                    OrganicManures.Add(new
                    {
                        OrganicManure = orgManure,
                        WarningMessages = warningMessageList.Count > 0 ? warningMessageList : null,
                        FarmID = model.FarmId,
                        FieldTypeID = fieldTypeId,
                        SaveDefaultForFarm = model.IsAnyNeedToStoreNutrientValueForFuture

                    });
                }

                //var OrganicManures = model.OrganicManures.Select(orgManure => new
                //{
                //    OrganicManure = orgManure,
                //    FarmID = model.FarmId,
                //    FieldTypeID = (int)NMP.Portal.Enums.FieldType.Arable,
                //    SaveDefaultForFarm = model.IsAnyNeedToStoreNutrientValueForFuture
                //}).ToList();
                var jsonData = new
                {
                    OrganicManures
                };


                string jsonString = JsonConvert.SerializeObject(jsonData);
                (bool success, error) = await _organicManureService.AddOrganicManuresAsync(jsonString);
                if (!success || error != null)
                {
                    TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                    return View(model);
                }

                string successMsg = string.Empty;
                if (int.TryParse(model.FieldGroup, out int value))
                {
                    successMsg = string.Format(Resource.lblOrganicManureCreatedSuccessfullyForCropType, model.CropTypeName);
                }
                else
                {
                    (List<CommonResponse> organicManureField, error) = await _organicManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
                    if (error == null)
                    {
                        if (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count < organicManureField.Count)
                        {

                            List<string> fieldNames = model.FieldList
                           .Select(id => organicManureField.FirstOrDefault(f => f.Id == Convert.ToInt64(id))?.Name).ToList();
                            string concatenatedFieldNames = string.Join(", ", fieldNames);
                            successMsg = string.Format(Resource.lblOrganicManureCreatedSuccessfullyForSpecificField, concatenatedFieldNames);

                        }
                        else
                        {
                            successMsg = Resource.lblOrganicManureCreatedSuccessfullyForAllField;
                        }
                    }
                    else
                    {
                        TempData["AddOrganicManureError"] = error.Message;
                        return View(model);
                    }

                }
                if (success)
                {
                    successMsg = Resource.lblOrganicManureCreatedSuccessfullyForAllField;
                    string successMsgSecond = Resource.lblSelectAFieldToSeeItsUpdatedNutrientRecommendation;
                    _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                    if (!model.IsComingFromRecommendation)
                    {
                        return RedirectToAction("HarvestYearOverview", "Crop", new
                        {
                            id = model.EncryptedFarmId,
                            year = model.EncryptedHarvestYear,
                            q = _farmDataProtector.Protect(success.ToString()),
                            r = _cropDataProtector.Protect(successMsg),
                            v = _cropDataProtector.Protect(successMsgSecond)
                        });
                    }
                    else
                    {
                        string fieldId = model.FieldList[0];
                        return RedirectToAction("Recommendations", "Crop", new
                        {
                            q = model.EncryptedFarmId,
                            r = _fieldDataProtector.Protect(fieldId),
                            s = model.EncryptedHarvestYear,
                            t = _cropDataProtector.Protect(Resource.lblOrganicManureCreatedSuccessfullyForAllField),
                            u = _cropDataProtector.Protect(Resource.lblSelectAFieldToSeeItsUpdatedNutrientRecommendation)

                        });
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in CheckAnswer() post action : {ex.Message}, {ex.StackTrace}");
                TempData["AddOrganicManureError"] = Resource.MsgWeCounldNotAddOrganicManure;
                return View(model);
            }
            return View(model);

        }

        public IActionResult BackCheckAnswer()
        {
            _logger.LogTrace($"Organic Manure Controller : BackCheckAnswer() post action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            model.IsCheckAnswer = false;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            if (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId) && (!model.IsComingFromRecommendation))
            {
                _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                return RedirectToAction("HarvestYearOverview", "Crop", new
                {
                    id = model.EncryptedFarmId,
                    year = model.EncryptedHarvestYear
                });
            }
            else if (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId) && (model.IsComingFromRecommendation))
            {
                _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                string fieldId = model.FieldList[0];
                return RedirectToAction("Recommendations", "Crop", new
                {
                    q = model.EncryptedFarmId,
                    r = _fieldDataProtector.Protect(fieldId),
                    s = model.EncryptedHarvestYear

                });
            }
            return RedirectToAction("ConditionsAffectingNutrients");
        }

        [HttpGet]
        public async Task<IActionResult> AutumnCropNitrogenUptake(string? f)
        {
            _logger.LogTrace($"Organic Manure Controller : AutumnCropNitrogenUptake() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (f != null)
            {
                int fieldId = Convert.ToInt32(_organicManureProtector.Unprotect(f));
                Field field = await _fieldService.FetchFieldByFieldId(fieldId);
                model.EncryptedFieldId = f;
                ViewBag.FieldName = field.Name;
                ViewBag.CropTypeName = model.CropTypeName;
                model.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes?.FirstOrDefault(x => x.EncryptedFieldId == f)?.AutumnCropNitrogenUptake;
            }
            if (model.FieldList.Count == 1)
            {
                Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(model.FieldList[0]));
                ViewBag.FieldName = field.Name;
                ViewBag.CropTypeName = model.CropTypeName;
                model.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes[0].AutumnCropNitrogenUptake;
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutumnCropNitrogenUptake(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : AutumnCropNitrogenUptake() post action called");
            if (!ModelState.IsValid && ModelState.ContainsKey("AutumnCropNitrogenUptake"))
            {
                var autumnCropNitrogenUptakeState = ModelState["AutumnCropNitrogenUptake"];

                if (autumnCropNitrogenUptakeState.Errors.Count > 0)
                {
                    var firstError = autumnCropNitrogenUptakeState.Errors[0];

                    if (firstError.ErrorMessage == string.Format(Resource.lblEnterNumericValue, autumnCropNitrogenUptakeState.RawValue, "AutumnCropNitrogenUptake"))
                    {
                        autumnCropNitrogenUptakeState.Errors.Clear();
                        autumnCropNitrogenUptakeState.Errors.Add(Resource.MsgEnterValidNumericValueBeforeContinuing);
                    }
                }
            }

            if (model.AutumnCropNitrogenUptake == null)
            {
                ModelState.AddModelError("AutumnCropNitrogenUptake", Resource.MsgEnterAValueBeforeContinue);
            }
            if (model.AutumnCropNitrogenUptake != null && model.AutumnCropNitrogenUptake < 0)
            {
                ModelState.AddModelError("AutumnCropNitrogenUptake", Resource.MsgEnterANumberWhichIsGreaterThanZero);
            }
            if (model.AutumnCropNitrogenUptake != null)
            {
                decimal value = model.AutumnCropNitrogenUptake.Value;

                if (value % 1 != 0)
                {
                    ModelState.AddModelError("AutumnCropNitrogenUptake", Resource.lblEnterANumberWhichIsAnIntegerValue);
                }

            }

            if (!ModelState.IsValid)
            {
                Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(_organicManureProtector.Unprotect(model.EncryptedFieldId)));
                ViewBag.FieldName = field.Name;
                ViewBag.CropTypeName = model.CropTypeName;
                model.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes?.FirstOrDefault(x => x.EncryptedFieldId == model.EncryptedFieldId)?.AutumnCropNitrogenUptake;
                return View("AutumnCropNitrogenUptake", model);
            }

            if (model.FieldList.Count == 1)
            {
                model.AutumnCropNitrogenUptakes[0].AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptake ?? 0;

                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("ConditionsAffectingNutrients");
            }
            else
            {
                model.AutumnCropNitrogenUptakes?
                     .Where(detail => detail.EncryptedFieldId == model.EncryptedFieldId)
                     .ToList()
                     .ForEach(detail => detail.AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptake ?? 0);

                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("AutumnCropNitrogenUptakeDetail");
            }

        }

        [HttpGet]
        public async Task<IActionResult> SoilDrainageEndDate()
        {
            _logger.LogTrace($"Organic Manure Controller : SoilDrainageEndDate() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            return View(model);

        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoilDrainageEndDate(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : SoilDrainageEndDate() post action called");
            if ((!ModelState.IsValid) && ModelState.ContainsKey("SoilDrainageEndDate"))
            {
                var dateError = ModelState["SoilDrainageEndDate"].Errors.Count > 0 ?
                                ModelState["SoilDrainageEndDate"].Errors[0].ErrorMessage.ToString() : null;

                if (dateError != null && dateError.Equals(string.Format(Resource.MsgDateMustBeARealDate, "SoilDrainageEndDate")))
                {
                    ModelState["SoilDrainageEndDate"].Errors.Clear();
                    ModelState["SoilDrainageEndDate"].Errors.Add(Resource.MsgEnterValidDate);
                }
                if (dateError != null && (
                    dateError.Equals(string.Format(Resource.MsgDateMustIncludeAMonth, "SoilDrainageEndDate")) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeAMonthAndYear, "SoilDrainageEndDate")) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeADayAndYear, "SoilDrainageEndDate")) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeAYear, "SoilDrainageEndDate")) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeADay, "SoilDrainageEndDate")) ||
                     dateError.Equals(string.Format(Resource.MsgDateMustIncludeADayAndMonth, "SoilDrainageEndDate"))))
                {
                    ModelState["SoilDrainageEndDate"].Errors.Clear();
                    ModelState["SoilDrainageEndDate"].Errors.Add(Resource.MsgTheDateMustInclude);
                }


            }

            if (model.SoilDrainageEndDate == null)
            {
                ModelState.AddModelError("SoilDrainageEndDate", Resource.MsgEnterADateBeforeContinuing);
            }
            if (model.SoilDrainageEndDate != null)
            {
                //if (model.SoilDrainageEndDate.Value.Date.Year > model.HarvestYear + 1)
                //{
                //    ModelState.AddModelError("SoilDrainageEndDate", Resource.MsgDateCannotBeLaterThanHarvestYear);
                //}
                if (DateTime.TryParseExact(model.SoilDrainageEndDate.Value.Date.ToString(), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    ModelState.AddModelError("SoilDrainageEndDate", Resource.MsgEnterValidDate);
                }

                if (!(model.SoilDrainageEndDate.Value.Month >= (int)NMP.Portal.Enums.Month.January && model.SoilDrainageEndDate.Value.Month <= (int)NMP.Portal.Enums.Month.April))
                {
                    ModelState.AddModelError("SoilDrainageEndDate", Resource.MsgSoilDrainageEndDate1stJan30Apr);
                }
            }
            if (!ModelState.IsValid)
            {
                return View("SoilDrainageEndDate", model);
            }


            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }

        [HttpGet]
        public async Task<IActionResult> RainfallWithinSixHour()
        {
            _logger.LogTrace($"Organic Manure Controller : RainfallWithinSixHour() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            (List<RainTypeResponse> rainType, Error error) = await _organicManureService.FetchRainTypeList();
            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
            {
                ViewBag.Error = error.Message;
            }
            else
            {
                ViewBag.RainTypes = rainType;
            }

            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RainfallWithinSixHour(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : RainfallWithinSixHour() post action called");
            if (model.RainfallWithinSixHoursID == null)
            {
                ModelState.AddModelError("RainfallWithinSixHoursID", Resource.MsgSelectAnOptionBeforeContinuing);
            }

            if (!ModelState.IsValid)
            {
                return View("RainfallWithinSixHour", model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }

        [HttpGet]
        public async Task<IActionResult> EffectiveRainfall()
        {
            _logger.LogTrace($"Organic Manure Controller : EffectiveRainfall() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);

        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EffectiveRainfall(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : EffectiveRainfall() post action called");
            if (!ModelState.IsValid)
            {
                return View("EffectiveRainfall", model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }

        [HttpGet]
        public async Task<IActionResult> EffectiveRainfallManual()
        {
            _logger.LogTrace($"Organic Manure Controller : EffectiveRainfallManual() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EffectiveRainfallManual(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : EffectiveRainfallManual() post action called");
            if ((!ModelState.IsValid) && ModelState.ContainsKey("TotalRainfall"))
            {
                var RainfallError = ModelState["TotalRainfall"].Errors.Count > 0 ?
                                ModelState["TotalRainfall"].Errors[0].ErrorMessage.ToString() : null;

                if (RainfallError != null && RainfallError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["TotalRainfall"].RawValue, "TotalRainfall")))
                {
                    ModelState["TotalRainfall"].Errors.Clear();
                    decimal decimalValue;
                    if (decimal.TryParse(ModelState["TotalRainfall"].RawValue.ToString(), out decimalValue))
                    {
                        ModelState["TotalRainfall"].Errors.Add(Resource.MsgIfUserEnterDecimalValueInRainfall);
                    }
                    else
                    {
                        ModelState["TotalRainfall"].Errors.Add(Resource.MsgForEffectiveRainfallManual);
                    }
                }
            }

            if (model.TotalRainfall == null)
            {
                ModelState.AddModelError("TotalRainfall", Resource.MsgEnterRainfallAmountBeforeContinuing);
            }
            if (model.TotalRainfall != null && model.TotalRainfall < 0)
            {
                ModelState.AddModelError("TotalRainfall", Resource.MsgEnterANumberWhichIsGreaterThanZero);
            }

            if (!ModelState.IsValid)
            {
                return View("EffectiveRainfallManual", model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }

        [HttpGet]
        public async Task<IActionResult> Windspeed()
        {
            _logger.LogTrace($"Organic Manure Controller : Windspeed() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            (List<WindspeedResponse> windspeeds, Error error) = await _organicManureService.FetchWindspeedList();
            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
            {
                ViewBag.Error = error.Message;
            }
            else
            {
                ViewBag.Windspeeds = windspeeds;
            }

            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Windspeed(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : Windspeed() post action called");
            if (model.WindspeedID == null)
            {
                ModelState.AddModelError("WindspeedID", Resource.MsgSelectAWindConditionBeforeContinuing);
            }

            if (!ModelState.IsValid)
            {
                return View("Windspeed", model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }

        [HttpGet]
        public async Task<IActionResult> TopsoilMoisture()
        {
            _logger.LogTrace($"Organic Manure Controller : TopsoilMoisture() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            (List<MoistureTypeResponse> moisterTypes, Error error) = await _organicManureService.FetchMoisterTypeList();
            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
            {
                ViewBag.Error = error.Message;
            }
            else
            {
                ViewBag.moisterTypes = moisterTypes;
            }

            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TopsoilMoisture(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : TopsoilMoisture() post action called");
            if (model.MoistureTypeId == null)
            {
                ModelState.AddModelError("MoistureTypeId", Resource.MsgSelectATopsoilWetnessConditionBeforeContinuing);
            }

            if (!ModelState.IsValid)
            {
                return View("TopsoilMoisture", model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }
        private async Task<(OrganicManureViewModel, Error?)> IsNFieldLimitWarningMessage(OrganicManureViewModel model, bool isFieldIsInNVZ, int managementId, bool isGetCheckAnswer, int fieldId)
        {
            Error? error = null;
            decimal defaultNitrogen = 0;
            if (model.OrganicManures != null && model.OrganicManures.Any())
            {
                foreach (var organicManure in model.OrganicManures)
                {
                    defaultNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                    break;
                }
            }

            if (model.ApplicationRate.HasValue && model.ApplicationDate.HasValue)
            {
                decimal previousAppliedTotalN = 0;
                decimal totalN = 0;
                (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);

                //DateTime startDate = model.ApplicationDate.Value.AddDays(-364);
                //DateTime endDate = model.ApplicationDate.Value;

                //The planned application would result in more than 250 kg/ha of total N from all applications of any Manure type apart from ‘Green compost’ or ‘Green/food compost’, applied or planned to the field in the last 365 days up to and including the application date of the manure
                if (model.ManureTypeId != (int)NMP.Portal.Enums.ManureTypes.GreenCompost && model.ManureTypeId != (int)NMP.Portal.Enums.ManureTypes.GreenFoodCompost)
                {
                    //passing orgId
                    if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                    {
                        (previousAppliedTotalN, error) = await _organicManureService.FetchTotalNBasedByFieldIdAppDateAndIsGreenCompost(fieldId, model.ApplicationDate.Value.AddDays(-364), model.ApplicationDate.Value, false, false, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.OrganicManureId).FirstOrDefault());
                    }
                    else
                    {
                        (previousAppliedTotalN, error) = await _organicManureService.FetchTotalNBasedByFieldIdAppDateAndIsGreenCompost(fieldId, model.ApplicationDate.Value.AddDays(-364), model.ApplicationDate.Value, false, false, null);
                    }
                    if (error == null)
                    {
                        decimal currentApplicationNitrogen = 0;
                        currentApplicationNitrogen = (defaultNitrogen * model.ApplicationRate.Value);
                        totalN = previousAppliedTotalN + currentApplicationNitrogen;
                        if (totalN > 250)
                        {
                            model.IsOrgManureNfieldLimitWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                            {
                                model.NmaxWarningHeader = Resource.MsgOrganicManureNfieldLimit;
                                model.NmaxWarningCodeID = (int)NMP.Portal.Enums.WarningCode.FieldAppLimit;
                                model.NmaxWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                model.NmaxWarningHeading = Resource.lblThisApplicationWillTakeYouOverTheOrganicManureNFieldLimit;
                                model.NmaxWarningPara1 = Resource.MsgIfOrganicManureNMaxLimitExceed;
                                model.NmaxWarningPara2 = Resource.MsgIfOrganicManureNMaxLimitExceedAdditional;
                            }
                            if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                            {
                                model.NmaxWarningHeader = Resource.MsgOrganicManureNfieldLimit;
                                model.NmaxWarningCodeID = (int)NMP.Portal.Enums.WarningCode.FieldAppLimit;
                                model.NmaxWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                model.NmaxWarningHeading = Resource.lblThisApplicationWillTakeYouOverTheOrganicManureNFieldLimitWales;
                                model.NmaxWarningPara1 = Resource.MsgIfOrganicManureNMaxLimitExceedWales;
                                model.NmaxWarningPara2 = Resource.MsgIfOrganicManureNMaxLimitExceedAdditionalWales;
                            }

                            //}
                            //else
                            //{
                            //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                            //    {
                            //        model.NmaxWarningHeading = Resource.lblThisApplicationWillTakeYouOverTheOrganicManureNFieldLimit;
                            //    }
                            //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                            //    {
                            //        model.NmaxWarningHeading = Resource.lblThisApplicationWillTakeYouOverTheOrganicManureNFieldLimitWales;
                            //    }

                            //}

                        }
                    }
                }

                if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.GreenCompost || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.GreenFoodCompost)
                {
                    var cropTypeIdsForTrigger = new HashSet<int> {
                        (int)NMP.Portal.Enums.CropTypes.CiderApples,
                        (int)NMP.Portal.Enums.CropTypes.CulinaryApples,
                        (int)NMP.Portal.Enums.CropTypes.DessertApples,
                        (int)NMP.Portal.Enums.CropTypes.Cherries,
                        (int)NMP.Portal.Enums.CropTypes.Pears,
                        (int)NMP.Portal.Enums.CropTypes.Plums
                    };

                    //The planned application would result in more than 500 of total N from all applications of Green compost & Green/food compost applied or planned to the field in the last 730 days up to and including the application date of the manure.

                    (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);
                    if (!cropTypeIdsForTrigger.Contains(cropTypeResponse.CropTypeId))
                    {
                        //passing orgId
                        if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                        {
                            (previousAppliedTotalN, error) = await _organicManureService.FetchTotalNBasedByFieldIdAppDateAndIsGreenCompost(fieldId, model.ApplicationDate.Value.AddDays(-729), model.ApplicationDate.Value, false, true, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.OrganicManureId).FirstOrDefault());
                        }
                        else
                        {
                            (previousAppliedTotalN, error) = await _organicManureService.FetchTotalNBasedByFieldIdAppDateAndIsGreenCompost(fieldId, model.ApplicationDate.Value.AddDays(-729), model.ApplicationDate.Value, false, true, null);
                        }

                        if (error == null)
                        {

                            decimal currentApplicationNitrogen = 0;
                            currentApplicationNitrogen = (defaultNitrogen * model.ApplicationRate.Value);
                            totalN = previousAppliedTotalN + currentApplicationNitrogen;
                            if (totalN > 500)
                            {
                                model.IsOrgManureNfieldLimitWarning = true;
                                //if (!isGetCheckAnswer)
                                //{
                                if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                {
                                    model.NmaxWarningHeader = Resource.MsgOrganicManureNFieldLimitForCompostWorkedIntoTheSoil;
                                    model.NmaxWarningCodeID = (int)NMP.Portal.Enums.WarningCode.FieldAppLimit;
                                    model.NmaxWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingEngland;
                                    model.NmaxWarningPara1 = Resource.MsgNmaxWarningPara1England;
                                    model.NmaxWarningPara2 = Resource.MsgNmaxWarningPara2England;
                                }
                                if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                {
                                    model.NmaxWarningHeader = Resource.MsgOrganicManureNFieldLimitForCompostWorkedIntoTheSoil;
                                    model.NmaxWarningCodeID = (int)NMP.Portal.Enums.WarningCode.FieldAppLimit;
                                    model.NmaxWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingWales;
                                    model.NmaxWarningPara1 = Resource.MsgNmaxWarningPara1Wales;
                                    model.NmaxWarningPara2 = Resource.MsgIfOrganicManureNMaxLimitExceedAdditionalWales;
                                }

                                //}
                                //else
                                //{
                                //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                //    {
                                //        model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingEngland;
                                //    }
                                //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                //    {
                                //        model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingWales;
                                //    }

                                //}

                            }

                        }
                    }

                    if (cropTypeIdsForTrigger.Contains(cropTypeResponse.CropTypeId))
                    {
                        //passing orgId
                        if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                        {
                            (previousAppliedTotalN, error) = await _organicManureService.FetchTotalNBasedByFieldIdAppDateAndIsGreenCompost(fieldId, model.ApplicationDate.Value.AddDays(-1459), model.ApplicationDate.Value, false, true, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.OrganicManureId).FirstOrDefault());
                        }
                        else
                        {
                            (previousAppliedTotalN, error) = await _organicManureService.FetchTotalNBasedByFieldIdAppDateAndIsGreenCompost(fieldId, model.ApplicationDate.Value.AddDays(-1459), model.ApplicationDate.Value, false, true, null);
                        }

                        if (error == null)
                        {
                            decimal currentApplicationNitrogen = 0;
                            currentApplicationNitrogen = (defaultNitrogen * model.ApplicationRate.Value);
                            totalN = previousAppliedTotalN + currentApplicationNitrogen;
                            if (totalN > 1000)
                            {
                                model.IsOrgManureNfieldLimitWarning = true;
                                //if (!isGetCheckAnswer)
                                //{
                                if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                {
                                    model.NmaxWarningHeader = Resource.MsgOrganicManureNFieldLimitForCompostAppliedAsAMulch;
                                    model.NmaxWarningCodeID = (int)NMP.Portal.Enums.WarningCode.FieldAppLimit;
                                    model.NmaxWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingGreaterThan1000England;
                                    model.NmaxWarningPara1 = Resource.MsgNmaxWarningPara1GreaterThan1000England;
                                    model.NmaxWarningPara2 = Resource.MsgNmaxWarningPara2England;
                                }
                                if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                {
                                    model.NmaxWarningHeader = Resource.MsgOrganicManureNFieldLimitForCompostAppliedAsAMulch;
                                    model.NmaxWarningCodeID = (int)NMP.Portal.Enums.WarningCode.FieldAppLimit;
                                    model.NmaxWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingGreaterThan1000Wales;
                                    model.NmaxWarningPara1 = Resource.MsgNmaxWarningPara1GreaterThan1000Wales;
                                    model.NmaxWarningPara2 = Resource.MsgIfOrganicManureNMaxLimitExceedAdditionalWales;
                                }

                                //}
                                //else
                                //{
                                //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                //    {
                                //        model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingGreaterThan1000England;
                                //    }
                                //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                //    {
                                //        model.NmaxWarningHeading = Resource.MsgNmaxWarningHeadingGreaterThan1000Wales;
                                //    }


                                //}
                            }

                        }
                    }

                }

            }
            return (model, error);

        }
        private async Task<(OrganicManureViewModel, Error?)> IsNMaxWarningMessage(OrganicManureViewModel model, int fieldId, int managementId, bool isGetCheckAnswer)
        {
            Error? error = null;
            //bool IsNMaxLimitWarning = false;
            decimal defaultNitrogen = 0;
            if (model.OrganicManures != null && model.OrganicManures.Any())
            {
                foreach (var organicManure in model.OrganicManures)
                {
                    defaultNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                    break;
                }
            }

            if (model.ApplicationRate.HasValue && model.ApplicationDate.HasValue)
            {
                decimal totalN = 0;
                decimal previousApplicationsN = 0;
                //DateTime startDate = model.ApplicationDate.Value.AddDays(-364);
                //DateTime endDate = model.ApplicationDate.Value;
                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                var crop = cropsResponse.Where(x => x.Year == model.HarvestYear && x.Confirm == false).ToList();
                if (crop != null)
                {
                    //(totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementId, startDate, endDate, false);

                    (CropTypeLinkingResponse cropTypeLinking, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop[0].CropTypeID.Value);
                    if (error == null)
                    {
                        int? nmaxLimitEnglandOrWales = (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales ? cropTypeLinking.NMaxLimitWales : cropTypeLinking.NMaxLimitEngland);
                        if (nmaxLimitEnglandOrWales != null)
                        {
                            (FieldDetailResponse fieldDetail, error) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(fieldId, model.HarvestYear.Value, false);
                            if (error == null)
                            {
                                //passing orgId
                                if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                {
                                    (previousApplicationsN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId, false, null, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.OrganicManureId).FirstOrDefault());
                                }
                                else
                                {
                                    (previousApplicationsN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId, false, null, null);
                                }
                                // (previousApplicationsN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId,false, null, null);
                                if (error == null)
                                {
                                    decimal nMaxLimit = 0;
                                    int? percentOfTotalNForUseInNmaxCalculation = null;
                                    (ManureType manureType, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId ?? 0);
                                    if (manureType != null)
                                    {
                                        percentOfTotalNForUseInNmaxCalculation = manureType.PercentOfTotalNForUseInNmaxCalculation;
                                    }

                                    decimal currentApplicationNitrogen = 0;
                                    if (percentOfTotalNForUseInNmaxCalculation != null)
                                    {
                                        decimal decimalOfTotalNForUseInNmaxCalculation = Convert.ToDecimal(percentOfTotalNForUseInNmaxCalculation / 100.0);
                                        currentApplicationNitrogen = (defaultNitrogen * model.ApplicationRate.Value * decimalOfTotalNForUseInNmaxCalculation);
                                        totalN = previousApplicationsN + currentApplicationNitrogen;

                                        (List<int> currentYearManureTypeIds, error) = await _organicManureService.FetchManureTypsIdsByFieldIdYearAndConfirmFromOrgManure(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                                        (List<int> previousYearManureTypeIds, error) = await _organicManureService.FetchManureTypsIdsByFieldIdYearAndConfirmFromOrgManure(Convert.ToInt32(fieldId), model.HarvestYear.Value - 1, false);
                                        if (error == null)
                                        {
                                            nMaxLimit = nmaxLimitEnglandOrWales ?? 0;
                                            OrganicManureNMaxLimitLogic organicManureNMaxLimitLogic = new OrganicManureNMaxLimitLogic();
                                            //nMaxLimit = organicManureNMaxLimitLogic.NMaxLimit(nmaxLimitEnglandOrWales ?? 0, crop[0].Yield == null ? null : crop[0].Yield.Value, fieldDetail.SoilTypeName, crop[0].CropInfo1 == null ? null : crop[0].CropInfo1.Value, crop[0].CropTypeID.Value, currentYearManureTypeIds, previousYearManureTypeIds, model.ManureTypeId.Value);

                                            if (totalN > nMaxLimit)
                                            {
                                                model.IsNMaxLimitWarning = true;
                                                (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                                                //if (!isGetCheckAnswer)
                                                //{
                                                if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                                {
                                                    model.CropNmaxLimitWarningHeader = Resource.MsgNMaxLimitMessage;
                                                    model.CropNmaxLimitWarningCodeID = (int)NMP.Portal.Enums.WarningCode.NMaxLimit;
                                                    model.CropNmaxLimitWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                    model.CropNmaxLimitWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingEngland;
                                                    model.CropNmaxLimitWarningPara1 = string.Format(Resource.MsgCropNmaxLimitWarningPara1England, nMaxLimit);
                                                    model.CropNmaxLimitWarningPara1Additional = string.Format(Resource.MsgCropNmaxLimitWarningPara1Additional, nMaxLimit);
                                                    model.CropNmaxLimitWarningPara2 = Resource.MsgCropNmaxLimitWarningPara2England;
                                                }

                                                if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                                {
                                                    model.CropNmaxLimitWarningHeader = Resource.MsgNMaxLimitMessage;
                                                    model.CropNmaxLimitWarningCodeID = (int)NMP.Portal.Enums.WarningCode.NMaxLimit;
                                                    model.CropNmaxLimitWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                    model.CropNmaxLimitWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingWales;
                                                    model.CropNmaxLimitWarningPara1 = string.Format(Resource.MsgCropNmaxLimitWarningPara1Wales, nMaxLimit);
                                                    model.CropNmaxLimitWarningPara1Additional = string.Format(Resource.MsgCropNmaxLimitWarningPara1Additional, nMaxLimit);
                                                    model.CropNmaxLimitWarningPara2 = Resource.MsgCropNmaxLimitWarningPara2Wales;
                                                }

                                                //}
                                                //else
                                                //{
                                                //    if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                                //    {
                                                //        model.CropNmaxLimitWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingWales;
                                                //    }
                                                //    else
                                                //    {
                                                //        model.CropNmaxLimitWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingEngland;


                                                //    }
                                                //}

                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (isGetCheckAnswer)
                                        {
                                            (decimal? availableNFromMannerOutput, error) = await GetAvailableNFromMannerOutput(model);
                                            //decimal? currentApplicationN = totalNitrogen * model.ApplicationRate.Value;
                                            if (error == null)
                                            {
                                                (List<int> currentYearManureTypeIds, error) = await _organicManureService.FetchManureTypsIdsByFieldIdYearAndConfirmFromOrgManure(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                                                (List<int> previousYearManureTypeIds, error) = await _organicManureService.FetchManureTypsIdsByFieldIdYearAndConfirmFromOrgManure(Convert.ToInt32(fieldId), model.HarvestYear.Value - 1, false);
                                                if (error == null)
                                                {
                                                    nMaxLimit = nmaxLimitEnglandOrWales ?? 0;
                                                    //string cropInfo1 = await _cropService.FetchCropInfo1NameByCropTypeIdAndCropInfo1Id(crop[0].CropTypeID.Value, crop[0].CropInfo1.Value);
                                                    OrganicManureNMaxLimitLogic organicManureNMaxLimitLogic = new OrganicManureNMaxLimitLogic();
                                                    //nMaxLimit = organicManureNMaxLimitLogic.NMaxLimit(nmaxLimitEnglandOrWales ?? 0, crop[0].Yield == null ? null : crop[0].Yield.Value, fieldDetail.SoilTypeName, crop[0].CropInfo1 == null ? null : crop[0].CropInfo1.Value, crop[0].CropTypeID.Value, currentYearManureTypeIds, previousYearManureTypeIds, model.ManureTypeId.Value);
                                                    if ((previousApplicationsN + availableNFromMannerOutput) > nMaxLimit)
                                                    {
                                                        model.IsNMaxLimitWarning = true;
                                                        (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);

                                                        if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                                        {
                                                            model.CropNmaxLimitWarningHeader = Resource.MsgNMaxLimitMessage;
                                                            model.CropNmaxLimitWarningCodeID = (int)NMP.Portal.Enums.WarningCode.NMaxLimit;
                                                            model.CropNmaxLimitWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                            model.CropNmaxLimitWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingEngland;
                                                            model.CropNmaxLimitWarningPara1 = string.Format(Resource.MsgCropNmaxLimitWarningPara1England, nMaxLimit);
                                                            model.CropNmaxLimitWarningPara1Additional = string.Format(Resource.MsgCropNmaxLimitWarningPara1Additional, nMaxLimit);
                                                            model.CropNmaxLimitWarningPara2 = Resource.MsgCropNmaxLimitWarningPara2England;
                                                        }

                                                        if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                                        {
                                                            model.CropNmaxLimitWarningHeader = Resource.MsgNMaxLimitMessage;
                                                            model.CropNmaxLimitWarningCodeID = (int)NMP.Portal.Enums.WarningCode.NMaxLimit;
                                                            model.CropNmaxLimitWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                            model.CropNmaxLimitWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingWales;
                                                            model.CropNmaxLimitWarningPara1 = string.Format(Resource.MsgCropNmaxLimitWarningPara1Wales, nMaxLimit);
                                                            model.CropNmaxLimitWarningPara1Additional = string.Format(Resource.MsgCropNmaxLimitWarningPara1Additional, nMaxLimit);
                                                            model.CropNmaxLimitWarningPara2 = Resource.MsgCropNmaxLimitWarningPara2Wales;
                                                        }

                                                    }
                                                }
                                                else
                                                {
                                                    return (model, string.IsNullOrWhiteSpace(error?.Message) ? null : error);
                                                }

                                            }
                                            else
                                            {
                                                return (model, string.IsNullOrWhiteSpace(error?.Message) ? null : error);
                                            }
                                        }
                                    }

                                }
                                else
                                {
                                    return (model, string.IsNullOrWhiteSpace(error?.Message) ? null : error);
                                }
                            }
                            else
                            {
                                return (model, string.IsNullOrWhiteSpace(error?.Message) ? null : error);
                            }
                        }
                    }
                    else
                    {
                        return (model, string.IsNullOrWhiteSpace(error?.Message) ? null : error);
                    }
                }
            }

            return (model, string.IsNullOrWhiteSpace(error?.Message) ? null : error);
        }
        private async Task<(OrganicManureViewModel, Error?)> IsEndClosedPeriodFebruaryWarningMessage(OrganicManureViewModel model, int fieldId, bool isGetCheckAnswer)
        {
            Error? error = null;
            string warningMsg = string.Empty;
            //bool IsEndClosedPeriodFebruaryWarning = false;
            //end of closed period and end of february warning message
            int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId));
            (Farm farm, error) = await _farmService.FetchFarmByIdAsync(farmId);
            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
            {
                return (model, error);
            }
            if (farm != null)
            {
                bool nonRegisteredOrganicProducer = farm.RegisteredOrganicProducer.Value;
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                bool isHighReadilyAvailableNitrogen = false;
                if (error != null)
                {
                    return (model, error);
                }
                else
                {
                    if (manureTypeList.Count > 0)
                    {
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        isHighReadilyAvailableNitrogen = manureType.HighReadilyAvailableNitrogen ?? false;
                        model.HighReadilyAvailableNitrogen = manureType.HighReadilyAvailableNitrogen;
                    }
                    (FieldDetailResponse fieldDetail, error) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(fieldId, model.HarvestYear.Value, false);
                    if (error != null)
                    {
                        return (model, error);
                    }
                    else
                    {
                        WarningWithinPeriod warningMessage = new WarningWithinPeriod();
                        string closedPeriod = string.Empty;
                        bool isPerennial = false;

                        //Non Organic farm closed period
                        if (!farm.RegisteredOrganicProducer.Value && isHighReadilyAvailableNitrogen)
                        {
                            (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear ?? 0, false);
                            if (error == null)
                            {
                                isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeResponse.CropTypeId);
                            }
                            else
                            {
                                return (model, error);
                            }
                            closedPeriod = warningMessage.ClosedPeriodNonOrganicFarm(fieldDetail, model.HarvestYear ?? 0, isPerennial);
                        }

                        //Organic farm closed period
                        if (farm.RegisteredOrganicProducer.Value && isHighReadilyAvailableNitrogen)
                        {
                            List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                            if (cropsResponse.Count > 0)
                            {
                                //List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                                int cropTypeId = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                                isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeId);
                                int? cropInfo1 = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropInfo1).FirstOrDefault();
                                closedPeriod = warningMessage.ClosedPeriodOrganicFarm(fieldDetail, model.HarvestYear ?? 0, cropTypeId, cropInfo1, isPerennial);
                            }
                        }
                        bool isSlurry = false;
                        bool isPoultryManure = false;

                        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.PigSlurry || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.CattleSlurry || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedCattleSlurryStrainerBox || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedCattleSlurryWeepingWall || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedCattleSlurryMechanicalSeparator || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedPigSlurryLiquidPortion)
                        {
                            isSlurry = true;
                        }
                        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.PoultryManure)
                        {
                            isPoultryManure = true;
                        }
                        string message = warningMessage.EndClosedPeriodAndFebruaryWarningMessage(model.ApplicationDate.Value, closedPeriod, model.ApplicationRate, isSlurry, isPoultryManure);
                        bool? isWithinClosedPeriodAndFebruary = warningMessage.CheckEndClosedPeriodAndFebruary(model.ApplicationDate.Value, closedPeriod);
                        //(bool? isSlurryOrPoultryExistWithing20Days, error) = await _organicManureService.FetchOrganicManureExistanceByDateRange(model.OrganicManures[0].ManagementPeriodID,model.ApplicationDate.Value.AddDays(-20).ToString("yyyy-MM-dd"), model.ApplicationDate.Value.ToString("yyyy-MM-dd"), false);

                        if (isWithinClosedPeriodAndFebruary == true)
                        {
                            if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                            {
                                if (isSlurry)
                                {
                                    if (model.ApplicationRate.HasValue && model.ApplicationRate.Value > 30)
                                    {
                                        model.IsEndClosedPeriodFebruaryWarning = true;
                                        //if (!isGetCheckAnswer)
                                        //{
                                        model.EndClosedPeriodEndFebWarningHeader = Resource.MsgSlurryMaxApplicationRate;
                                        model.EndClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                        model.EndClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                        model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading30SlurryEngland;
                                        model.EndClosedPeriodEndFebWarningPara1 = Resource.MsgEndPeriodEndFebWarningPara1st30SlurryEngland;
                                        model.EndClosedPeriodEndFebWarningPara2 = Resource.MsgEndPeriodEndFebWarningPara2nd30SlurryEngland;
                                        //}
                                        //else
                                        //{
                                        //    model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading30SlurryEngland;
                                        //}

                                    }
                                }
                                if (isPoultryManure)
                                {
                                    if (model.ApplicationRate.HasValue && model.ApplicationRate.Value > 8)
                                    {
                                        model.IsEndClosedPeriodFebruaryWarning = true;
                                        //if (!isGetCheckAnswer)
                                        //{
                                        model.EndClosedPeriodEndFebWarningHeader = Resource.MsgPoultryManureMaxApplicationRate;
                                        model.EndClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                        model.EndClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                        model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading8PoultryEngland;
                                        model.EndClosedPeriodEndFebWarningPara1 = Resource.MsgEndPeriodEndFebWarningPara1st8PoultryEngland;
                                        model.EndClosedPeriodEndFebWarningPara2 = Resource.MsgEndPeriodEndFebWarningPara2nd8PoultryEngland;
                                        //}
                                        //else
                                        //{
                                        //    model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading8PoultryEngland;
                                        //}

                                    }
                                }

                            }
                            if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                            {
                                if (isSlurry)
                                {
                                    if (model.ApplicationRate.HasValue && model.ApplicationRate.Value > 30)
                                    {
                                        model.IsEndClosedPeriodFebruaryWarning = true;
                                        //if (!isGetCheckAnswer)
                                        //{
                                        model.EndClosedPeriodEndFebWarningHeader = Resource.MsgSlurryMaxApplicationRate;
                                        model.EndClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                        model.EndClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                        model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading30SlurryWales;
                                        model.EndClosedPeriodEndFebWarningPara1 = Resource.MsgEndPeriodEndFebWarningPara30SlurryWales;
                                        model.EndClosedPeriodEndFebWarningPara2 = Resource.MsgEndPeriodEndFebWarningPara2nd30SlurryWales;
                                        //}
                                        //else
                                        //{
                                        //    model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading30SlurryWales;
                                        //}

                                    }
                                }
                                if (isPoultryManure)
                                {
                                    if (model.ApplicationRate.HasValue && model.ApplicationRate.Value > 8)
                                    {
                                        model.IsEndClosedPeriodFebruaryWarning = true;
                                        //if (!isGetCheckAnswer)
                                        //{
                                        model.EndClosedPeriodEndFebWarningHeader = Resource.MsgPoultryManureMaxApplicationRate;
                                        model.EndClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                        model.EndClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                        model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading8PoultryWales;
                                        model.EndClosedPeriodEndFebWarningPara1 = Resource.MsgEndPeriodEndFebWarningPara8PoultryWales;
                                        model.EndClosedPeriodEndFebWarningPara2 = Resource.MsgEndPeriodEndFebWarningPara2nd8PoultryWales;
                                        //}
                                        //else
                                        //{
                                        //    model.EndClosedPeriodEndFebWarningHeading = Resource.MsgEndPeriodEndFebWarningHeading8PoultryWales;
                                        //}
                                    }
                                }

                            }

                        }

                    }
                }


            }
            return (model, error);
        }
        private async Task<(OrganicManureViewModel, Error?)> IsClosedPeriodWarningMessage(OrganicManureViewModel model, bool isWithinNVZ, bool registeredOrganicProducer, bool isGetCheckAnswer)
        {
            Error? error = null;
            string warningMsg = string.Empty;
            string? closedPeriod = string.Empty;
            string SlurryOrPoultryManureExistWithinLast20Days = string.Empty;
            bool isWithinClosedPeriod = false;
            string message = string.Empty;
            int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
            List<ManureType> manureTypeList = new List<ManureType>();
            if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
            {
                (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
            }
            else
            {
                (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
            }
            if (error != null)
            {
                return (model, error);
            }
            else
            {
                bool isHighReadilyAvailableNitrogen = false;
                if (manureTypeList.Count > 0)
                {
                    var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                    isHighReadilyAvailableNitrogen = manureType.HighReadilyAvailableNitrogen ?? false;
                    model.HighReadilyAvailableNitrogen = manureType.HighReadilyAvailableNitrogen;
                }
                (FieldDetailResponse fieldDetail, error) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);
                if (error != null)
                {
                    return (model, error);
                }
                else
                {
                    WarningWithinPeriod warningMessage = new WarningWithinPeriod();

                    bool isPerennial = false;
                    if (!registeredOrganicProducer && isHighReadilyAvailableNitrogen && isWithinNVZ)
                    {
                        (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);
                        if (error != null)
                        {
                            return (model, error);
                        }
                        else
                        {
                            isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeResponse.CropTypeId);

                            closedPeriod = warningMessage.ClosedPeriodNonOrganicFarm(fieldDetail, model.HarvestYear ?? 0, isPerennial);

                            isWithinClosedPeriod = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, model.ClosedPeriodEndDate);
                            if (isWithinClosedPeriod)
                            {
                                (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                                //if (!isGetCheckAnswer)
                                //{
                                if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                {
                                    model.ClosedPeriodWarningHeader = Resource.MsgClosedPeriodForTheApplicationOfHighNOrganicManureMessage;
                                    model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;
                                    model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;


                                    model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriod;
                                    model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodWarningPara2England;
                                }
                                if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                {
                                    model.ClosedPeriodWarningHeader = Resource.MsgClosedPeriodForTheApplicationOfHighNOrganicManureMessage;
                                    model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;
                                    model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriodWales;
                                    model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodWarningPara2Wales;
                                }
                                //}
                                //else
                                //{
                                //    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                //    {
                                //        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriod;
                                //    }
                                //    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                //    {
                                //        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriodWales;
                                //    }
                                //}

                                model.IsClosedPeriodWarning = true;
                            }
                        }
                    }

                    //Organic farm

                    HashSet<int> cropTypeIdsForTrigger = new HashSet<int>
                    {
                    (int)NMP.Portal.Enums.CropTypes.Asparagus,
                    (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape,
                    (int)NMP.Portal.Enums.CropTypes.ForageRape,
                    (int)NMP.Portal.Enums.CropTypes.ForageSwedesRootsLifted,
                    (int)NMP.Portal.Enums.CropTypes.KaleGrazed,
                    (int)NMP.Portal.Enums.CropTypes.StubbleTurnipsGrazed,
                    (int)NMP.Portal.Enums.CropTypes.SwedesGrazed,
                    (int)NMP.Portal.Enums.CropTypes.TurnipsRootLifted,
                    (int)NMP.Portal.Enums.CropTypes.BrusselSprouts,
                    (int)NMP.Portal.Enums.CropTypes.Cabbage,
                    (int)NMP.Portal.Enums.CropTypes.Calabrese,
                    (int)NMP.Portal.Enums.CropTypes.Cauliflower,
                    (int)NMP.Portal.Enums.CropTypes.Radish,
                    (int)NMP.Portal.Enums.CropTypes.WildRocket,
                    (int)NMP.Portal.Enums.CropTypes.Swedes,
                    (int)NMP.Portal.Enums.CropTypes.Turnips,
                    (int)NMP.Portal.Enums.CropTypes.BulbOnions,
                    (int)NMP.Portal.Enums.CropTypes.SaladOnions,
                    (int)NMP.Portal.Enums.CropTypes.Grass
                    };

                    decimal totalNitrogen = 0;
                    if (model.OrganicManures != null && model.OrganicManures.Any())
                    {
                        foreach (var organicManure in model.OrganicManures)
                        {
                            totalNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                            break;
                        }
                    }
                    //decimal totalManureNitrogen = totalNitrogen * model.ApplicationRate.Value;

                    if (registeredOrganicProducer && isHighReadilyAvailableNitrogen && isWithinNVZ)
                    {
                        (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(model.FieldList[0]), model.HarvestYear ?? 0, false);
                        if (error != null)
                        {
                            return (model, error);
                        }
                        else
                        {
                            List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                            int cropTypeId = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                            isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeId);
                            int? cropInfo1 = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropInfo1).FirstOrDefault();
                            closedPeriod = warningMessage.ClosedPeriodOrganicFarm(fieldDetail, model.HarvestYear ?? 0, cropTypeId, cropInfo1, isPerennial);

                            isWithinClosedPeriod = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, model.ClosedPeriodEndDate);
                            if (isWithinClosedPeriod)
                            {

                                if (!cropTypeIdsForTrigger.Contains(cropTypeResponse.CropTypeId))
                                {
                                    model.IsClosedPeriodWarning = true;

                                    //if (!isGetCheckAnswer)
                                    //{
                                    model.ClosedPeriodWarningHeader = Resource.MsgClosedSpreadingPeriod;
                                    model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;
                                    model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                    {

                                        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriod;
                                        model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodWarningPara2England;
                                    }
                                    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                    {

                                        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriodWales;
                                        model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodWarningPara2Wales;
                                    }

                                    //}
                                    //else
                                    //{
                                    //    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                    //    {
                                    //        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriod;
                                    //    }
                                    //    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                    //    {
                                    //        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriodWales;
                                    //    }

                                    //}

                                }

                            }

                            DateTime endOfOctober = new DateTime((model.HarvestYear ?? 0) - 1, 10, 31);
                            if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape || cropTypeId == (int)NMP.Portal.Enums.CropTypes.Grass)
                            {
                                bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, endOfOctober, model.ClosedPeriodEndDate);
                                if (isWithinDateRange)
                                {
                                    model.ClosedPeriodWarningHeader = Resource.MsgClosedSpreadingPeriodApplicationAfter31October;
                                    model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;
                                    model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                    {
                                        model.IsClosedPeriodWarning = true;
                                        model.ClosedPeriodWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriod;
                                        model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodWarningPara2England;
                                    }
                                }
                            }
                        }
                    }
                    //if application date is between end of closed period and end of february.
                    //check 20 days or less since the last application of slurry or poultry manure.
                    bool isOrganicManureExist = false;
                    bool? isWithinClosedPeriodAndFebruary = warningMessage.CheckEndClosedPeriodAndFebruary(model.ApplicationDate.Value, closedPeriod);

                    if (isWithinClosedPeriodAndFebruary != null && isWithinClosedPeriodAndFebruary == true)
                    {

                        (isOrganicManureExist, error) = await _organicManureService.FetchOrganicManureExistanceByDateRange(model.OrganicManures[0].ManagementPeriodID, model.ApplicationDate.Value.AddDays(-20).ToString("yyyy-MM-dd"), model.ApplicationDate.Value.ToString("yyyy-MM-dd"), false);
                        if (error != null)
                        {
                            return (model, error);
                        }
                    }
                    if (isOrganicManureExist)
                    {
                        bool isSlurry = false;
                        bool isPoultryManure = false;

                        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.PigSlurry || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.CattleSlurry || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedCattleSlurryStrainerBox || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedCattleSlurryWeepingWall || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedCattleSlurryMechanicalSeparator || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.SeparatedPigSlurryLiquidPortion)
                        {
                            isSlurry = true;
                        }
                        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.PoultryManure)
                        {
                            isPoultryManure = true;
                        }
                        if (isSlurry || isPoultryManure)
                        {
                            model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks = true;
                            //if (!isGetCheckAnswer)
                            //{

                            model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeader = Resource.Msg3WeeksBetweenApplications;
                            model.EndClosedPeriodFebruaryExistWithinThreeWeeksCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;
                            model.EndClosedPeriodFebruaryExistWithinThreeWeeksLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                            if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                            {
                                model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeading = Resource.MsgEndPeriodEndFebWarningHeadingWithin20DaysEngland;
                                model.EndClosedPeriodFebruaryExistWithinThreeWeeksPara1 = Resource.MsgEndPeriodEndFebWarningPara1Within20DaysEngland;
                                model.EndClosedPeriodFebruaryExistWithinThreeWeeksPara2 = Resource.MsgEndPeriodEndFebWarningPara2Within20DaysEngland;
                            }
                            if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                            {
                                model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeading = Resource.MsgEndPeriodEndFebWarningHeadingWithin20DaysWales;
                                model.EndClosedPeriodFebruaryExistWithinThreeWeeksPara1 = Resource.MsgEndPeriodEndFebWarningPara1Within20DaysWales;
                                model.EndClosedPeriodFebruaryExistWithinThreeWeeksPara2 = Resource.MsgEndPeriodEndFebWarningPara2ndWithin20DaysWales;

                            }
                            //}
                            //else
                            //{

                            //    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                            //    {
                            //        model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeading = Resource.MsgEndPeriodEndFebWarningHeadingWithin20DaysEngland;

                            //    }
                            //    if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                            //    {
                            //        model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeading = Resource.MsgEndPeriodEndFebWarningHeadingWithin20DaysWales;

                            //    }
                            //}
                        }
                    }

                }
            }

            model.ClosedPeriod = closedPeriod;
            model.IsWithinClosedPeriod = isWithinClosedPeriod;
            return (model, error);
        }

        private async Task<(bool, string, Error?)> IsClosedPeriodStartAndEndFebExceedNRateException(OrganicManureViewModel model, int fieldId)
        {
            Error? error = null;
            string warningMsg = string.Empty;
            HashSet<int> cropTypeIdsForTrigger = new HashSet<int>
            {
                    (int)NMP.Portal.Enums.CropTypes.Asparagus,
                    (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape,
                    (int)NMP.Portal.Enums.CropTypes.ForageRape,
                    (int)NMP.Portal.Enums.CropTypes.ForageSwedesRootsLifted,
                    (int)NMP.Portal.Enums.CropTypes.KaleGrazed,
                    (int)NMP.Portal.Enums.CropTypes.StubbleTurnipsGrazed,
                    (int)NMP.Portal.Enums.CropTypes.SwedesGrazed,
                    (int)NMP.Portal.Enums.CropTypes.TurnipsRootLifted,
                    (int)NMP.Portal.Enums.CropTypes.BrusselSprouts,
                    (int)NMP.Portal.Enums.CropTypes.Cabbage,
                    (int)NMP.Portal.Enums.CropTypes.Calabrese,
                    (int)NMP.Portal.Enums.CropTypes.Cauliflower,
                    (int)NMP.Portal.Enums.CropTypes.Radish,
                    (int)NMP.Portal.Enums.CropTypes.WildRocket,
                    (int)NMP.Portal.Enums.CropTypes.Swedes,
                    (int)NMP.Portal.Enums.CropTypes.Turnips,
                    (int)NMP.Portal.Enums.CropTypes.BulbOnions,
                    (int)NMP.Portal.Enums.CropTypes.SaladOnions,
                    (int)NMP.Portal.Enums.CropTypes.Grass
            };

            HashSet<int> brassicaCrops = new HashSet<int>
            {
                (int)NMP.Portal.Enums.CropTypes.ForageRape,
                    (int)NMP.Portal.Enums.CropTypes.ForageSwedesRootsLifted,
                    (int)NMP.Portal.Enums.CropTypes.KaleGrazed,
                    (int)NMP.Portal.Enums.CropTypes.StubbleTurnipsGrazed,
                    (int)NMP.Portal.Enums.CropTypes.SwedesGrazed,
                    (int)NMP.Portal.Enums.CropTypes.TurnipsRootLifted,
                    (int)NMP.Portal.Enums.CropTypes.BrusselSprouts,
                    (int)NMP.Portal.Enums.CropTypes.Cabbage,
                    (int)NMP.Portal.Enums.CropTypes.Calabrese,
                    (int)NMP.Portal.Enums.CropTypes.Cauliflower,
                    (int)NMP.Portal.Enums.CropTypes.Radish,
                    (int)NMP.Portal.Enums.CropTypes.WildRocket,
                    (int)NMP.Portal.Enums.CropTypes.Swedes,
                    (int)NMP.Portal.Enums.CropTypes.Turnips

            };

            int farmId = Convert.ToInt32(_farmDataProtector.Unprotect(model.EncryptedFarmId));
            (Farm farm, error) = await _farmService.FetchFarmByIdAsync(farmId);
            if (error != null && (!string.IsNullOrWhiteSpace(error.Message)))
            {
                return (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, warningMsg, error);
            }
            if (farm != null)
            {
                bool nonRegisteredOrganicProducer = farm.RegisteredOrganicProducer.Value;
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                bool isHighReadilyAvailableNitrogen = false;

                decimal totalNitrogen = 0;
                foreach (var organicManure in model.OrganicManures)
                {
                    totalNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                    break;
                }

                if (error != null)
                {
                    return (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, warningMsg, error);
                }
                else
                {
                    if (manureTypeList.Count > 0)
                    {
                        var manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                        isHighReadilyAvailableNitrogen = manureType?.HighReadilyAvailableNitrogen ?? false;
                    }
                    FieldDetailResponse fieldDetail = new FieldDetailResponse();
                    if (model.HarvestYear != null)
                    {
                        (fieldDetail, error) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(fieldId, model.HarvestYear.Value, false);
                    }

                    if (error != null)
                    {
                        return (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, warningMsg, error);
                    }
                    else
                    {
                        WarningWithinPeriod warningMessage = new WarningWithinPeriod();
                        string? warningPeriod = string.Empty;
                        //bool isWithinClosedPeriod = false;

                        Field field = await _fieldService.FetchFieldByFieldId(fieldId);
                        bool isFieldIsInNVZ = false;
                        bool isPerennial = false;
                        if (field.IsWithinNVZ != null)
                        {
                            isFieldIsInNVZ = field.IsWithinNVZ.Value;
                        }

                        (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear ?? 0, false);
                        if (error == null)
                        {
                            if (farm.RegisteredOrganicProducer.Value && isHighReadilyAvailableNitrogen && isFieldIsInNVZ)
                            {
                                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                                if (cropsResponse.Count > 0)
                                {
                                    int cropTypeId = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropTypeID).FirstOrDefault() ?? 0;
                                    isPerennial = await _organicManureService.FetchIsPerennialByCropTypeId(cropTypeId);
                                    int? cropInfo1 = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.CropInfo1).FirstOrDefault();

                                    DateTime endDateFebruary = new DateTime((model.HarvestYear ?? 0), 3, 1).AddDays(-1);
                                    DateTime endOfOctober = new DateTime((model.HarvestYear ?? 0) - 1, 10, 31);
                                    decimal totalN = 0;

                                    (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId.ToString(), null, null);

                                    if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.Grass)
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                        {
                                            bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, endOfOctober);
                                            if (isWithinDateRange)
                                            {
                                                //passing orgId
                                                if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endOfOctober, false, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementIds[0]).Select(x => x.OrganicManureId).FirstOrDefault());
                                                }
                                                else
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endOfOctober, false, null);
                                                }
                                                //  (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endOfOctober, false);

                                                decimal? currentNitrogen = totalNitrogen * model.ApplicationRate;
                                                if (currentNitrogen != null)
                                                {
                                                    if (currentNitrogen > 40 || currentNitrogen + totalN > 150)
                                                    {
                                                        model.StartClosedPeriodEndFebWarningHeader = Resource.MsgClosedPeriodAndMaximumApplicationRateForHighN;
                                                        model.StartClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                                        model.StartClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                        model.StartClosedPeriodEndFebWarningHeading = Resource.MsgStartClosedPeriodEndFebWarningHeading;
                                                        model.StartClosedPeriodEndFebWarningPara1 = Resource.MsgStartClosedPeriodEndFebWarningPara1Grass;
                                                        model.StartClosedPeriodEndFebWarningPara2 = Resource.MsgStartClosedPeriodEndFebWarningPara2;
                                                        model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = true;
                                                    }

                                                }
                                            }
                                        }
                                    }
                                    if ((cropTypeId == (int)NMP.Portal.Enums.CropTypes.Asparagus) || (cropTypeId == (int)NMP.Portal.Enums.CropTypes.BulbOnions) || (cropTypeId == (int)NMP.Portal.Enums.CropTypes.SaladOnions))
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                        {
                                            bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, endDateFebruary);
                                            if (isWithinDateRange)
                                            {
                                                decimal? currentNitrogen = totalNitrogen * model.ApplicationRate;
                                                //passing orgId
                                                if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementIds[0]).Select(x => x.OrganicManureId).FirstOrDefault());
                                                }
                                                else
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false, null);
                                                }
                                                // (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false);
                                                if (currentNitrogen + totalN > 150)
                                                {
                                                    model.StartClosedPeriodEndFebWarningHeader = Resource.MsgClosedPeriodAndMaximumApplicationRateForHighN;
                                                    model.StartClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                                    model.StartClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;


                                                    model.StartClosedPeriodEndFebWarningHeading = Resource.MsgStartClosedPeriodEndFebWarningHeading;
                                                    model.StartClosedPeriodEndFebWarningPara1 = Resource.MsgStartClosedPeriodEndFebWarningPara1;
                                                    model.StartClosedPeriodEndFebWarningPara2 = Resource.MsgStartClosedPeriodEndFebWarningPara2;
                                                    model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = true;
                                                }
                                            }
                                        }
                                    }
                                    if (cropTypeIdsForTrigger.Contains(cropTypeId))
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                        {
                                            bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, endDateFebruary);
                                            if (isWithinDateRange)
                                            {

                                                decimal? currentNitrogen = totalNitrogen * model.ApplicationRate;
                                                //passing orgId
                                                if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementIds[0]).Select(x => x.OrganicManureId).FirstOrDefault());
                                                }
                                                else
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false, null);
                                                }
                                                // (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false);
                                                if (currentNitrogen + totalN > 150)
                                                {
                                                    model.StartClosedPeriodEndFebWarningHeader = Resource.MsgClosedPeriodAndMaximumApplicationRateForHighN;
                                                    model.StartClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                                    model.StartClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;


                                                    model.StartClosedPeriodEndFebWarningHeading = Resource.MsgStartClosedPeriodEndFebWarningHeadingWales;
                                                    model.StartClosedPeriodEndFebWarningPara1 = Resource.MsgStartClosedPeriodEndFebWarningPara1Wales;
                                                    model.StartClosedPeriodEndFebWarningPara2 = Resource.MsgStartClosedPeriodEndFebWarningPara2Wales;
                                                    model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = true;
                                                }
                                            }
                                        }

                                    }
                                    if (brassicaCrops.Contains(cropTypeId))
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                        {
                                            bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, endDateFebruary);
                                            if (isWithinDateRange)
                                            {
                                                totalN = 0;
                                                if (managementIds.Count > 0)
                                                {
                                                    //passing orgId
                                                    if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                                    {
                                                        (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementIds[0]).Select(x => x.OrganicManureId).FirstOrDefault());
                                                    }
                                                    else
                                                    {
                                                        (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false, null);
                                                    }
                                                    //  (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endDateFebruary, false);

                                                    (bool isOrganicManureExistWithin4Weeks, error) = await _organicManureService.FetchOrganicManureExistanceByDateRange(model.OrganicManures[0].ManagementPeriodID, model.ApplicationDate.Value.AddDays(-28).ToString("yyyy-MM-dd"), model.ApplicationDate.Value.ToString("yyyy-MM-dd"), false);

                                                    decimal? currentNitrogen = totalNitrogen * model.ApplicationRate;
                                                    if (currentNitrogen != null)
                                                    {
                                                        if (currentNitrogen > 50 || currentNitrogen + totalN > 150 || isOrganicManureExistWithin4Weeks)
                                                        {
                                                            model.StartClosedPeriodEndFebWarningHeader = Resource.MsgClosedPeriodAndMaximumApplicationRateForHighN;
                                                            model.StartClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                                            model.StartClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                            model.StartClosedPeriodEndFebWarningHeading = Resource.MsgStartClosedPeriodEndFebWarningHeading;
                                                            model.StartClosedPeriodEndFebWarningPara1 = Resource.MsgStartClosedPeriodEndFebWarningPara1Brassica;
                                                            model.StartClosedPeriodEndFebWarningPara2 = Resource.MsgStartClosedPeriodEndFebWarningPara2;
                                                            model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = true;
                                                        }

                                                    }
                                                }
                                            }
                                        }

                                    }
                                    if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape)
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                        {
                                            bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, model.ClosedPeriodStartDate, endOfOctober);
                                            if (isWithinDateRange)
                                            {
                                                decimal? currentNitrogen = totalNitrogen * model.ApplicationRate;
                                                //passing orgId
                                                if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endOfOctober, false, model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId == managementIds[0]).Select(x => x.OrganicManureId).FirstOrDefault());
                                                }
                                                else
                                                {
                                                    (totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endOfOctober, false, null);
                                                }
                                                //(totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdAndAppDate(managementIds[0], model.ClosedPeriodStartDate.Value, endOfOctober, false);

                                                if (currentNitrogen + totalN > 150)
                                                {
                                                    model.StartClosedPeriodEndFebWarningHeader = Resource.MsgClosedPeriodAndMaximumApplicationRateForHighN;
                                                    model.StartClosedPeriodEndFebWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ManureApplicationLimitCloseFeb2013_30;
                                                    model.StartClosedPeriodEndFebWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;

                                                    model.StartClosedPeriodEndFebWarningHeading = Resource.MsgStartClosedPeriodEndFebWarningHeading;
                                                    model.StartClosedPeriodEndFebWarningPara1 = Resource.MsgStartClosedPeriodEndFebWarningPara1WinterOilseed;
                                                    model.StartClosedPeriodEndFebWarningPara2 = Resource.MsgStartClosedPeriodEndFebWarningPara2;
                                                    model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = true;
                                                }
                                            }
                                        }

                                    }
                                    //if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape || cropTypeId == (int)NMP.Portal.Enums.CropTypes.Grass)
                                    //{
                                    //    bool isWithinDateRange = warningMessage.IsApplicationDateWithinDateRange(model.ApplicationDate, endOfOctober, model.ClosedPeriodEndDate);
                                    //    if (isWithinDateRange)
                                    //    {
                                    //        model.StartClosedPeriodEndFebWarningHeading = Resource.MsgApplicationDateEnteredIsInsideClosedPeriod;
                                    //        model.StartClosedPeriodEndFebWarningPara2 = Resource.MsgClosedPeriodWarningPara2England;
                                    //        model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150 = true;
                                    //    }

                                    //}

                                }
                            }
                        }
                        else
                        {
                            return (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, warningMsg, error);
                        }

                    }
                }


            }
            return (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150, warningMsg, error);
        }

        private async Task<(decimal?, Error?)> GetAvailableNFromMannerOutput(OrganicManureViewModel model)
        {
            Error error = new Error();
            decimal? availableNfromManner = null;

            if (model.OrganicManures != null)
            {
                model.OrganicManures.ForEach(x => x.EndOfDrain = x.SoilDrainageEndDate);
                if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    model.OrganicManures.ForEach(x => x.ManureTypeName = model.OtherMaterialName);
                    if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                    {
                        model.OrganicManures.ForEach(x => x.ManureTypeID = model.ManureGroupIdForFilter ?? 0);
                        model.OrganicManures.ForEach(x => x.ManureTypeName = model.ManureTypeName);

                    }
                }
                else
                {
                    model.OrganicManures.ForEach(x => x.ManureTypeName = model.ManureTypeName);
                }

                //logic for AvailableNForNMax column that will be used to get sum of previous manure applications
                int? percentOfTotalNForUseInNmaxCalculation = null;
                decimal? currentApplicationNitrogen = null;
                (ManureType manure, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId ?? 0);
                if (manure != null)
                {
                    percentOfTotalNForUseInNmaxCalculation = manure.PercentOfTotalNForUseInNmaxCalculation;
                }
                decimal totalNitrogen = 0;
                if (percentOfTotalNForUseInNmaxCalculation != null)
                {
                    if (model.OrganicManures != null && model.OrganicManures.Any())
                    {
                        foreach (var organicManure in model.OrganicManures)
                        {
                            totalNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                            break;
                        }

                        decimal decimalOfTotalNForUseInNmaxCalculation = Convert.ToDecimal(percentOfTotalNForUseInNmaxCalculation / 100.0);
                        if (model.ApplicationRate.HasValue)
                        {
                            currentApplicationNitrogen = (totalNitrogen * model.ApplicationRate.Value * decimalOfTotalNForUseInNmaxCalculation);
                        }
                    }
                }


                //foreach (string field in model.FieldList)
                //{
                (Farm farmData, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                if (farmData != null && (string.IsNullOrWhiteSpace(error.Message)))
                {
                    //Field fieldData = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field));
                    //if (fieldData != null)
                    //{
                    foreach (var organic in model.OrganicManures)
                    {
                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                            if (crop != null && string.IsNullOrWhiteSpace(error.Message))
                            {
                                Field fieldData = await _fieldService.FetchFieldByFieldId(crop.FieldID.Value);
                                if (fieldData != null)
                                {
                                    (SoilTypeSoilTextureResponse soilTexture, error) = await _organicManureService.FetchSoilTypeSoilTextureBySoilTypeId(fieldData.SoilTypeID ?? 0);
                                    int topSoilID = 0;
                                    int subSoilID = 0;
                                    if (error == null && soilTexture != null)
                                    {
                                        topSoilID = soilTexture.TopSoilID;
                                        subSoilID = soilTexture.SubSoilID;
                                    }
                                    (CropTypeLinkingResponse cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID.Value);
                                    if (error == null && cropTypeLinkingResponse != null)
                                    {
                                        (ManureType manureType, error) = await _organicManureService.FetchManureTypeByManureTypeId(organic.ManureTypeID);
                                        if (error == null && manureType != null)
                                        {
                                            var mannerOutput = new
                                            {
                                                runType = farmData.EnglishRules ? (int)NMP.Portal.Enums.RunType.MannerEngland : (int)NMP.Portal.Enums.RunType.MannerScotland,
                                                postcode = farmData.ClimateDataPostCode.Split(" ")[0],
                                                countryID = farmData.CountryID,
                                                field = new
                                                {
                                                    fieldID = fieldData.ID,
                                                    fieldName = fieldData.Name,
                                                    MannerCropTypeID = cropTypeLinkingResponse.MannerCropTypeID,
                                                    topsoilID = topSoilID,
                                                    subsoilID = subSoilID,
                                                    isInNVZ = Convert.ToBoolean(fieldData.IsWithinNVZ)
                                                },
                                                manureApplications = new[]
                                             {
                                                new
                                                {
                                                    manureDetails = new
                                                    {
                                                        manureID = organic.ManureTypeID,
                                                        name = organic.ManureTypeName,
                                                        isLiquid = manureType.IsLiquid,
                                                        dryMatter = organic.DryMatterPercent,
                                                        totalN = organic.N,
                                                        nH4N = organic.NH4N,
                                                        uric = organic.UricAcid,
                                                        nO3N = organic.NO3N,
                                                        p2O5 = organic.P2O5,
                                                        k2O = organic.K2O,
                                                        sO3 = organic.SO3,
                                                        mgO = organic.MgO
                                                    },
                                                    applicationDate = organic.ApplicationDate?.ToString("yyyy-MM-dd"),
                                                    applicationRate = new
                                                    {
                                                        value = organic.ApplicationRate,
                                                        unit = model.IsManureTypeLiquid.Value ? Resource.lblMeterCubePerHectare : Resource.lblTonnesPerHectare
                                                    },
                                                    applicationMethodID = organic.ApplicationMethodID,
                                                    incorporationMethodID = organic.IncorporationMethodID,
                                                    incorporationDelayID = organic.IncorporationDelayID,
                                                    autumnCropNitrogenUptake = new
                                                    {
                                                        value = organic.AutumnCropNitrogenUptake,
                                                        unit = Resource.lblKgPerHectare
                                                    },
                                                    endOfDrainageDate = organic.EndOfDrain.ToString("yyyy-MM-dd"),
                                                    rainfallPostApplication = organic.Rainfall,
                                                    windspeedID = organic.WindspeedID,
                                                    rainTypeID = organic.RainfallWithinSixHoursID,
                                                    topsoilMoistureID = organic.MoistureID
                                                }
                                            }
                                            };
                                            //var mannerJsonData = new
                                            //{
                                            //    mannerOutput
                                            //};
                                            string mannerJsonString = JsonConvert.SerializeObject(mannerOutput);
                                            (MannerCalculateNutrientResponse mannerCalculateNutrientResponse, error) = await _organicManureService.FetchMannerCalculateNutrient(mannerJsonString);
                                            if (error == null && mannerCalculateNutrientResponse != null)
                                            {
                                                availableNfromManner = mannerCalculateNutrientResponse.CurrentCropAvailableN;
                                                return (availableNfromManner, error);

                                            }
                                            else
                                            {
                                                return (availableNfromManner, error);
                                            }
                                        }
                                        else
                                        {
                                            return (availableNfromManner, error);
                                        }
                                    }
                                    else
                                    {
                                        return (availableNfromManner, error);
                                    }
                                }
                                else
                                {
                                    return (availableNfromManner, error);
                                }
                            }
                            else
                            {
                                return (availableNfromManner, error);
                            }
                        }
                        else
                        {
                            return (availableNfromManner, error);
                        }
                    }
                    //}
                }
                else
                {
                    return (availableNfromManner, error);
                }
                //}
            }
            return (availableNfromManner, error);
        }

        [HttpGet]
        public async Task<IActionResult> AutumnCropNitrogenUptakeDetail()
        {
            _logger.LogTrace($"Organic Manure Controller : AutumnCropNitrogenUptakeDetail() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            return View(model);

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutumnCropNitrogenUptakeDetail(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : AutumnCropNitrogenUptakeDetail() post action called");

            if (!ModelState.IsValid)
            {
                return View("AutumnCropNitrogenUptakeDetail", model);
            }

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            return RedirectToAction("ConditionsAffectingNutrients");
        }


        [HttpGet]
        public async Task<IActionResult> OtherMaterialName()
        {
            _logger.LogTrace("Organic Manure Controller : OtherMaterialName() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in OtherMaterialName() get action : {ex.Message}, {ex.StackTrace}");
                TempData["CropTypeError"] = ex.Message;
                return RedirectToAction("ManureTypes");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OtherMaterialName(OrganicManureViewModel model)
        {
            _logger.LogTrace("Organic Manure Controller : OtherMaterialName() post action called");
            try
            {
                if (model.OtherMaterialName == null)
                {
                    ModelState.AddModelError("OtherMaterialName", Resource.MsgEnterNameOfTheMaterial);
                }


                (bool farmManureExist, Error error) = await _organicManureService.FetchFarmManureTypeCheckByFarmIdAndManureTypeId(model.FarmId.Value, model.ManureTypeId.Value, model.OtherMaterialName);
                if (string.IsNullOrWhiteSpace(error.Message))
                {
                    if (farmManureExist)
                    {
                        ModelState.AddModelError("OtherMaterialName", Resource.MsgThisManureTypeNameAreadyExist);
                    }
                }
                if (!ModelState.IsValid)
                {
                    return View(model);
                }
                foreach (var manure in model.OrganicManures)
                {
                    manure.ManureTypeName = model.OtherMaterialName;
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in OtherMaterialName() post action : {ex.Message}, {ex.StackTrace}");
                TempData["ErrorOnVariety"] = ex.Message;
                return View(model);
            }
            if (model.IsDoubleCropAvailable)
            {
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("DoubleCrop");
            }
            if (model.IsAnyCropIsGrass == null || (model.IsAnyCropIsGrass != null && !model.IsAnyCropIsGrass.Value))
            {
                model.GrassCropCount = null;
                model.IsSameDefoliationForAll = null;
                model.IsAnyChangeInSameDefoliationFlag = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("ManureApplyingDate");
            }
            else
            {
                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                {
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("IsSameDefoliationForAll");
                }
                model.IsSameDefoliationForAll = true;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                return RedirectToAction("Defoliation");
            }
            return RedirectToAction("ManureApplyingDate");
        }
        [HttpGet]
        public async Task<IActionResult> RemoveOrganicManure(string q, string r, string s, string? t, string? u, string? v)
        {
            _logger.LogTrace($"Organic  Manure Controller : RemoveOrganicManure() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            Error error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                    {
                        model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if (model != null)
                    {
                        if (model.FieldList != null && model.FieldList.Count > 0)
                        {
                            (List<CommonResponse> fieldList, error) = await _fertiliserManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, null);
                            if (error == null)
                            {
                                if (fieldList.Count > 0)
                                {
                                    var fieldNames = fieldList
                                                     .Where(field => model.FieldList.Contains(field.Id.ToString())).OrderBy(field => field.Name)
                                                     .Select(field => field.Name)
                                                     .ToList();

                                    if (fieldNames != null && fieldNames.Count == 1)
                                    {
                                        model.FieldName = fieldNames.FirstOrDefault();
                                    }
                                    else if (fieldNames != null)
                                    {
                                        model.FieldName = string.Empty;
                                        ViewBag.SelectedFields = fieldNames.OrderBy(name => name).ToList();
                                    }
                                    ViewBag.EncryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                                }
                            }
                        }
                    }
                }
                else
                {
                    model.IsComingFromRecommendation = true;
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        model.EncryptedOrgManureId = q;
                    }
                    if (!string.IsNullOrWhiteSpace(r))
                    {
                        ViewBag.EncryptedFieldId = r;
                        model.FieldList = new List<string>();
                        model.FieldList.Add(_fieldDataProtector.Unprotect(r));
                    }
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        model.FieldName = _cropDataProtector.Unprotect(s);
                    }

                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        model.EncryptedFarmId = t;
                        model.FarmId = Convert.ToInt32(_farmDataProtector.Unprotect(t));
                    }

                    if (!string.IsNullOrWhiteSpace(u))
                    {
                        model.EncryptedHarvestYear = u;
                        model.HarvestYear = Convert.ToInt32(_farmDataProtector.Unprotect(u));
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in RemoveOrganicManure() action : {ex.Message}, {ex.StackTrace}");
                if (model.IsComingFromRecommendation)
                {
                    TempData["NutrientRecommendationsError"] = ex.Message;
                    return RedirectToAction("Recommendations", "Crop", new { q = model.EncryptedFarmId, r = r, s = model.EncryptedHarvestYear });
                }
                TempData["AddOrganicManureError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveOrganicManure(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : RemoveOrganicManure() post action called");
            Error error = null;
            if (model.IsDeleteOrganic == null)
            {
                ModelState.AddModelError("IsDeleteOrganic", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                if (model.FieldList != null && model.FieldList.Count > 0)
                {
                    (List<CommonResponse> fieldList, error) = await _fertiliserManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, null);
                    if (error == null)
                    {
                        if (fieldList.Count > 0)
                        {
                            var fieldNames = fieldList
                                             .Where(field => model.FieldList.Contains(field.Id.ToString())).OrderBy(field => field.Name)
                                             .Select(field => field.Name)
                                             .ToList();

                            if (fieldNames != null && fieldNames.Count == 1)
                            {
                                model.FieldName = fieldNames.FirstOrDefault();
                            }
                            else if (fieldNames != null)
                            {
                                model.FieldName = string.Empty;
                                ViewBag.SelectedFields = fieldNames.OrderBy(name => name).ToList();
                            }
                            ViewBag.EncryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                        }
                    }
                }
                return View(model);
            }
            try
            {
                if (!model.IsDeleteOrganic.Value)
                {
                    //if (model.IsComingFromRecommendation)
                    //{
                    //    string encryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                    //    ViewBag.EncryptedFieldId = encryptedFieldId;
                    //    if (!string.IsNullOrWhiteSpace(encryptedFieldId))
                    //    {
                    //        return RedirectToAction("Recommendations", "Crop", new { q = model.EncryptedFarmId, r = encryptedFieldId, s = model.EncryptedHarvestYear });
                    //    }

                    //}
                    //else
                    //{
                    return RedirectToAction("CheckAnswer");
                    //}
                }
                else
                {

                    List<int> organicManureIds = new List<int>();
                    if (model.IsComingFromRecommendation && (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId)))
                    {
                        ViewBag.EncryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                        organicManureIds.Add(Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedOrgManureId)));
                    }
                    else if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0 && model.OrganicManures != null && model.OrganicManures.Count > 0)
                    {
                        foreach (string fieldId in model.FieldList)
                        {
                            string fieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name;
                            foreach (var organicManure in model.UpdatedOrganicIds)
                            {
                                if (fieldName.Equals(organicManure.Name))
                                {
                                    organicManureIds.Add(organicManure.OrganicManureId.Value);
                                }
                            }
                        }

                        //foreach (var organicManure in model.OrganicManures)
                        //{
                        //    organicManureIds.Add(model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId.Value == organicManure.ManagementPeriodID).Select(x => x.OrganicManureId.Value).FirstOrDefault());
                        //}

                    }



                    if (organicManureIds.Count > 0)
                    {
                        var result = new
                        {
                            organicManureIds = organicManureIds
                        };
                        string jsonString = JsonConvert.SerializeObject(result);
                        (string success, error) = await _organicManureService.DeleteOrganicManureByIdAsync(jsonString);
                        if (string.IsNullOrWhiteSpace(error.Message))
                        {
                            _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                            if (model.IsComingFromRecommendation)
                            {
                                if (model.FieldList != null && model.FieldList.Count > 0)
                                {
                                    string encryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                                    if (!string.IsNullOrWhiteSpace(encryptedFieldId))
                                    {
                                        return RedirectToAction("Recommendations", "Crop", new { q = model.EncryptedFarmId, r = encryptedFieldId, s = model.EncryptedHarvestYear, t = _cropDataProtector.Protect(Resource.lblOrganicMaterialApplicationRemoved), u = _cropDataProtector.Protect(Resource.lblSelectFieldToSeeItsUpdatedNutrientRecommendations) });
                                    }
                                }
                            }
                            else
                            {
                                return Redirect(Url.Action("HarvestYearOverview", "Crop", new { Id = model.EncryptedFarmId, year = model.EncryptedHarvestYear, q = Resource.lblTrue, r = _cropDataProtector.Protect(Resource.lblOrganicMaterialApplicationRemoved), v = _cropDataProtector.Protect(Resource.lblSelectFieldToSeeItsUpdatedNutrientRecommendations) }) + Resource.lblOrganicMaterialApplicationsForSorting);

                            }
                        }
                        else
                        {
                            if (model.FieldList != null && model.FieldList.Count > 0)
                            {
                                (List<CommonResponse> fieldList, Error fieldListError) = await _fertiliserManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, null);
                                if (fieldListError == null)
                                {
                                    if (fieldList.Count > 0)
                                    {
                                        var fieldNames = fieldList
                                                         .Where(field => model.FieldList.Contains(field.Id.ToString())).OrderBy(field => field.Name)
                                                         .Select(field => field.Name)
                                                         .ToList();

                                        if (fieldNames != null && fieldNames.Count == 1)
                                        {
                                            model.FieldName = fieldNames.FirstOrDefault();
                                        }
                                        else if (fieldNames != null)
                                        {
                                            model.FieldName = string.Empty;
                                            ViewBag.SelectedFields = fieldNames.OrderBy(name => name).ToList();
                                        }
                                    }
                                }
                                else
                                {
                                    TempData["RemoveOrganicManureError"] = fieldListError.Message;
                                }
                            }
                            TempData["RemoveOrganicManureError"] = error.Message;
                            return View(model);
                        }
                    }

                    //foreach (var fertliser in model.UpdatedOrganicIds)
                    //    {
                    //        fertiliserIds.Add(fertliser.FertiliserId.Value);
                    //    }


                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in RemoveOrganicManure() post action : {ex.Message}, {ex.StackTrace}");
                TempData["RemoveOrganicManureError"] = ex.Message;
                return View(model);
            }
            return View(model);


        }
        [HttpGet]
        public IActionResult Cancel()
        {
            _logger.LogTrace("Organic Manure Controller : Cancel() action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in Cancel() action : {ex.Message}, {ex.StackTrace}");
                TempData["AddOrganicManureError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(OrganicManureViewModel model)
        {
            _logger.LogTrace("Organic Manure Controller : Cancel() post action called");
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
                return RedirectToAction("CheckAnswer");
            }
            else
            {
                _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                if (!model.IsComingFromRecommendation)
                {
                    return RedirectToAction("HarvestYearOverview", "Crop", new
                    {
                        id = model.EncryptedFarmId,
                        year = model.EncryptedHarvestYear
                    });
                }
                else
                {
                    string fieldId = model.FieldList[0];
                    return RedirectToAction("Recommendations", "Crop", new
                    {
                        q = model.EncryptedFarmId,
                        r = _fieldDataProtector.Protect(fieldId),
                        s = model.EncryptedHarvestYear

                    });
                }
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OrganicManureUpdate(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : OrganicManureUpdate() post action called");
            try
            {
                if (model.ManureTypeId == null)
                {
                    ModelState.AddModelError("ManureTypeId", Resource.MsgManureTypeNotSet);
                }
                if (model.ApplicationMethod == null)
                {
                    ModelState.AddModelError("ApplicationMethod", string.Format(Resource.MsgApplicationMethodNotSet, model.ManureTypeName));
                }
                if (model.ApplicationDate == null)
                {
                    ModelState.AddModelError("ApplicationDate", string.Format(Resource.MsgApplyingDateNotSet, model.ManureTypeName));
                }
                if (model.DefaultNutrientValue == null)
                {
                    ModelState.AddModelError("DefaultNutrientValue", string.Format(Resource.MsgDefaultNutrientValuesNotSet, model.ManureTypeName));
                }
                if (model.ApplicationRateMethod == null)
                {
                    ModelState.AddModelError("ApplicationRateMethod", string.Format(Resource.MsgApplicationRateMethodNotSet, model.ManureTypeName));
                }
                if (model.ApplicationRate == null)
                {
                    ModelState.AddModelError("ApplicationRate", Resource.MsgApplicationRateNotSet);
                }
                if (model.ApplicationRateMethod == (int)NMP.Portal.Enums.ApplicationRate.CalculateBasedOnAreaAndQuantity)
                {
                    if (model.Area == null)
                    {
                        ModelState.AddModelError("Area", Resource.MsgAreaNotSet);
                    }
                    if (model.Quantity == null)
                    {
                        ModelState.AddModelError("Quantity", Resource.MsgQuantityNotSet);
                    }
                }
                if (model.IncorporationMethod == null)
                {
                    ModelState.AddModelError("IncorporationMethod", string.Format(Resource.MsgIncorporationMethodNotSet, model.ManureTypeName));
                }
                if (model.IncorporationDelay == null)
                {
                    ModelState.AddModelError("IncorporationDelay", string.Format(Resource.MsgIncorporationDelayNotSet, model.ManureTypeName));
                }
                //if (model.AutumnCropNitrogenUptake == null)
                //{
                //    ModelState.AddModelError("AutumnCropNitrogenUptake", Resource.MsgAutumnCropNitrogenUptakeNotSet);
                //}
                if (model.SoilDrainageEndDate == null)
                {
                    ModelState.AddModelError("SoilDrainageEndDate", Resource.MsgEndOfSoilDrainageNotSet);
                }
                if (model.RainfallWithinSixHoursID == null)
                {
                    ModelState.AddModelError("RainfallWithinSixHoursID", Resource.MsgRainfallWithinSixHoursOfApplicationNotSet);
                }
                if (model.TotalRainfall == null)
                {
                    ModelState.AddModelError("TotalRainfall", Resource.MsgTotalRainfallSinceApplicationNotSet);
                }
                if (model.WindspeedID == null)
                {
                    ModelState.AddModelError("WindspeedID", Resource.MsgWindspeedAtApplicationNotSet);
                }
                if (model.MoistureTypeId == null)
                {
                    ModelState.AddModelError("MoistureTypeId", Resource.MsgTopsoilMoistureNotSet);
                }
                if (model.DoubleCrop == null && model.IsDoubleCropAvailable)
                {
                    int index = 0;
                    List<Crop> cropList = new List<Crop>();
                    string cropTypeName = string.Empty;
                    if (model.DoubleCrop == null)
                    {
                        foreach (string fieldId in model.FieldList)
                        {
                            cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                            if (cropList != null && cropList.Count == 2)
                            {
                                ModelState.AddModelError("MoistureTypeId", string.Format("{0} {1}", string.Format(Resource.lblWhichCropIsThisManureApplication, (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name), Resource.lblNotSet));
                                index++;
                            }
                        }
                    }

                }
                Error error = new Error();
                List<string> grassFieldIds = new List<string>();
                foreach (string field in model.FieldList)
                {
                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                    Crop crop = null;
                    if (model.OrganicManures.Any(x => x.FieldID == Convert.ToInt32(field)))
                    {
                        int manId = model.OrganicManures.Where(x => x.FieldID == Convert.ToInt32(field)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                    }
                    int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(field))?.CropOrder
       ?? crop?.CropOrder.Value ?? 1;
                    if (cropList.Count > 0)
                    {
                        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == model.CropOrder).ToList();
                    }

                    if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
                    {
                        grassFieldIds.Add(field);
                    }
                }
                if (model.GrassCropCount.HasValue && model.GrassCropCount > 1 && model.IsSameDefoliationForAll == null)
                {
                    ModelState.AddModelError("IsSameDefoliationForAll", string.Format("{0} {1}", Resource.lblForMultipleDefoliation, Resource.lblNotSet));
                }
                int i = 0;
                foreach (var organic in model.OrganicManures)
                {
                    if (grassFieldIds.Any(x => x == organic.FieldID.ToString()) && organic.Defoliation == null)
                    {
                        if (model.IsSameDefoliationForAll.HasValue && (model.IsSameDefoliationForAll.Value))
                        {
                            ModelState.AddModelError(string.Concat("OrganicManures[", i, "].Defoliation"), string.Format("{0} {1}", string.Format(Resource.lblWhichCutOrGrazingInThisApplicationForInField, organic.FieldName), Resource.lblNotSet));
                            break;
                        }
                        else
                        {
                            ModelState.AddModelError(string.Concat("OrganicManures[", i, "].Defoliation"), string.Format("{0} {1}", Resource.lblWhichCutOrGrazingInThisApplicationForAllField, Resource.lblNotSet));
                            break;
                        }
                    }
                    i++;
                }


                if (!ModelState.IsValid)
                {
                    return RedirectToAction("CheckAnswer");

                }


                List<string> fieldListCopy = new List<string>(model.FieldList);

                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                {
                    foreach (var field in grassFieldIds)
                    {
                        fieldListCopy.Remove(field);
                    }

                    List<int> managementIds = new List<int>();

                    if (fieldListCopy.Count > 0)
                    {
                        foreach (string fieldIdForManID in fieldListCopy)
                        {
                            if (!model.OrganicManures.Any(x => x.FieldID == Convert.ToInt32(fieldIdForManID)))
                            {
                                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldIdForManID));
                                if (cropList != null && cropList.Count > 0)
                                {
                                    int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldIdForManID))?.CropOrder
                       ?? 1;
                                    if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields))
                                    {
                                        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();
                                    }
                                    else
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();

                                        }
                                    }
                                    if (cropList.Count > 0)
                                    {
                                        model.CropOrder = Convert.ToInt32(cropList.Select(x => x.CropOrder).FirstOrDefault());
                                        model.FieldID = Convert.ToInt32(cropList.Select(x => x.FieldID).FirstOrDefault());
                                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                                    }
                                }

                                (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, model.CropOrder);

                                if (managementIds.Count > 0)
                                {
                                    foreach (var manIds in managementIds)
                                    {
                                        var organic = new OrganicManure
                                        {
                                            ManagementPeriodID = manIds,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName,
                                            ManureTypeID = model.ManureTypeId.Value,
                                            ManureTypeName = model.ManureTypeName,
                                            ApplicationMethodID = model.ApplicationMethod,
                                            N = model.OrganicManures.Select(x => x.N).FirstOrDefault(),
                                            P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault(),
                                            NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault(),
                                            K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault(),
                                            MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault(),
                                            NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault(),
                                            Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault(),
                                            SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault(),
                                            ApplicationDate = model.ApplicationDate.Value,
                                            ApplicationRate = model.ApplicationRate,
                                            AreaSpread = model.Area,
                                            ManureQuantity = model.Quantity,
                                            DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault(),
                                            UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault(),
                                            EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault(),
                                            RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault(),
                                            WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault(),
                                            MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault(),
                                            IncorporationDelayID = model.IncorporationDelay,
                                            IncorporationMethodID = model.IncorporationMethod,
                                            AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes.Where(x => x.FieldName == model.FieldName).Select(x => x.AutumnCropNitrogenUptake).FirstOrDefault()
                                        };
                                        model.OrganicManures.Add(organic);
                                    }
                                }

                            }
                        }
                    }
                    if (model.DoubleCrop != null && model.DoubleCrop.Count > 0)
                    {
                        foreach (var doubleCrop in model.DoubleCrop)
                        {
                            (List<ManagementPeriod> managementPeriods, error) = await _cropService.FetchManagementperiodByCropId(doubleCrop.CropID, true);
                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriods != null && managementPeriods.Count > 0)
                            {
                                var managementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();

                                var organicManure = model.OrganicManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (organicManure != null)
                                    {
                                        if ((!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId)))
                                        {
                                            if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                            {
                                                foreach (var item in model.UpdatedOrganicIds)
                                                {
                                                    if (item.ManagementPeriodId == managementPeriodId)
                                                    {
                                                        item.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault(); ;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        organicManure.ManagementPeriodID = managementPeriodId;
                                    }
                                }
                            }
                        }
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);

                }
                else
                {
                    if (model.DoubleCrop?.Count > 0)
                    {
                        foreach (var doubleCrop in model.DoubleCrop)
                        {
                            (List<ManagementPeriod> managementPeriods, error) = await _cropService.FetchManagementperiodByCropId(doubleCrop.CropID, true);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriods?.Count > 0)
                            {
                                var managementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();

                                var organicManure = model.OrganicManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (organicManure != null)
                                    {
                                        if ((!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId)))
                                        {
                                            if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                            {
                                                foreach (var item in model.UpdatedOrganicIds)
                                                {
                                                    if (item.ManagementPeriodId == managementPeriodId)
                                                    {
                                                        item.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault(); ;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        organicManure.ManagementPeriodID = managementPeriodId;
                                    }
                                    else
                                    {
                                        model.OrganicManures.Add(new OrganicManure
                                        {
                                            ManagementPeriodID = managementPeriodId,
                                            FieldID = doubleCrop.FieldID,
                                            FieldName = doubleCrop.FieldName,
                                            ManureTypeID = model.ManureTypeId.Value,
                                            ManureTypeName = model.ManureTypeName,
                                            ApplicationMethodID = model.ApplicationMethod,
                                            N = model.OrganicManures.Select(x => x.N).FirstOrDefault(),
                                            P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault(),
                                            NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault(),
                                            K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault(),
                                            MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault(),
                                            NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault(),
                                            Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault(),
                                            SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault(),
                                            ApplicationDate = model.ApplicationDate.Value,
                                            ApplicationRate = model.ApplicationRate,
                                            AreaSpread = model.Area,
                                            ManureQuantity = model.Quantity,
                                            DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault(),
                                            UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault(),
                                            EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault(),
                                            RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault(),
                                            WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault(),
                                            MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault(),
                                            IncorporationDelayID = model.IncorporationDelay,
                                            IncorporationMethodID = model.IncorporationMethod,
                                            AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes.Where(x => x.FieldName == model.FieldName).Select(x => x.AutumnCropNitrogenUptake).FirstOrDefault()
                                        });
                                    }
                                }
                            }
                        }
                    }

                    List<string> targetFieldIds;

                    // If DoubleCrop exists, remove its FieldIDs from fieldListCopy
                    if (model.DoubleCrop?.Count > 0)
                    {
                        var doubleCropFieldIds = model.DoubleCrop
                            .Select(dc => dc.FieldID.ToString())
                            .ToHashSet();

                        targetFieldIds = fieldListCopy
                            .Where(id => !doubleCropFieldIds.Contains(id))
                            .ToList();
                    }
                    else
                    {
                        targetFieldIds = fieldListCopy;
                    }
                    if (!(model.OrganicManures != null && model.OrganicManures.Count > 0))
                    {
                        model.OrganicManures = new List<OrganicManure>();
                    }
                    if (model.OrganicManures.Count < model.FieldList.Count && targetFieldIds?.Count > 0)
                    {
                        foreach (string fieldIdStr in targetFieldIds)
                        {
                            int fieldId = Convert.ToInt32(fieldIdStr);
                            if (!model.OrganicManures.Any(x => x.FieldID == fieldId))
                            {

                                var cropList = await _cropService.FetchCropsByFieldId(fieldId);

                                if (cropList?.Count > 0)
                                {
                                    if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields))
                                    {
                                        cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                    }
                                    else
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                        }
                                    }
                                    cropList = cropList
                                        .Where(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                        .ToList();

                                    if (cropList.Count > 0)
                                    {
                                        model.CropOrder = cropList.First().CropOrder ?? 1;
                                        model.FieldID = cropList.First().FieldID;
                                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                                    }
                                }

                                (List<int> managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(
                                    model.HarvestYear.Value,
                                    fieldIdStr,
                                    (model.FieldGroup == Resource.lblSelectSpecificFields || model.FieldGroup == Resource.lblAll)
                                        ? null
                                        : model.FieldGroup,
                                    model.CropOrder
                                );

                                if (managementIds?.Count > 0)
                                {
                                    foreach (var manId in managementIds)
                                    {
                                        model.OrganicManures.Add(new OrganicManure
                                        {
                                            ManagementPeriodID = manId,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName,
                                            ManureTypeID = model.ManureTypeId.Value,
                                            ManureTypeName = model.ManureTypeName,
                                            ApplicationMethodID = model.ApplicationMethod,
                                            N = model.OrganicManures.Select(x => x.N).FirstOrDefault(),
                                            P2O5 = model.OrganicManures.Select(x => x.P2O5).FirstOrDefault(),
                                            NH4N = model.OrganicManures.Select(x => x.NH4N).FirstOrDefault(),
                                            K2O = model.OrganicManures.Select(x => x.K2O).FirstOrDefault(),
                                            MgO = model.OrganicManures.Select(x => x.MgO).FirstOrDefault(),
                                            NO3N = model.OrganicManures.Select(x => x.NO3N).FirstOrDefault(),
                                            Confirm = model.OrganicManures.Select(x => x.Confirm).FirstOrDefault(),
                                            SO3 = model.OrganicManures.Select(x => x.SO3).FirstOrDefault(),
                                            ApplicationDate = model.ApplicationDate.Value,
                                            ApplicationRate = model.ApplicationRate,
                                            AreaSpread = model.Area,
                                            ManureQuantity = model.Quantity,
                                            DryMatterPercent = model.OrganicManures.Select(x => x.DryMatterPercent).FirstOrDefault(),
                                            UricAcid = model.OrganicManures.Select(x => x.UricAcid).FirstOrDefault(),
                                            EndOfDrain = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            SoilDrainageEndDate = model.SoilDrainageEndDate.Value.ToLocalTime(),
                                            Rainfall = model.OrganicManures.Select(x => x.Rainfall).FirstOrDefault(),
                                            RainfallWithinSixHoursID = model.OrganicManures.Select(x => x.RainfallWithinSixHoursID).FirstOrDefault(),
                                            WindspeedID = model.OrganicManures.Select(x => x.WindspeedID).FirstOrDefault(),
                                            MoistureID = model.OrganicManures.Select(x => x.MoistureID).FirstOrDefault(),
                                            IncorporationDelayID = model.IncorporationDelay,
                                            IncorporationMethodID = model.IncorporationMethod,
                                            AutumnCropNitrogenUptake = model.AutumnCropNitrogenUptakes.Where(x => x.FieldName == model.FieldName).Select(x => x.AutumnCropNitrogenUptake).FirstOrDefault()
                                        });
                                    }
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }

                }

                if (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                {
                    if (model.OrganicManures != null && model.OrganicManures.Count > 0)
                    {

                        model.OrganicManures.ForEach(x => x.EndOfDrain = x.SoilDrainageEndDate);
                        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                        {
                            model.OrganicManures.ForEach(x => x.ManureTypeName = model.OtherMaterialName);
                            if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                            {
                                model.OrganicManures.ForEach(x => x.ManureTypeID = model.ManureGroupIdForFilter ?? 0);
                            }
                        }
                        else
                        {
                            model.OrganicManures.ForEach(x => x.ManureTypeName = model.ManureTypeName);
                        }

                        //logic for AvailableNForNMax column that will be used to get sum of previous manure applications
                        int? percentOfTotalNForUseInNmaxCalculation = null;
                        decimal? currentApplicationNitrogen = null;
                        (ManureType manure, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId ?? 0);
                        if (manure != null)
                        {
                            percentOfTotalNForUseInNmaxCalculation = manure.PercentOfTotalNForUseInNmaxCalculation;
                        }
                        decimal totalNitrogen = 0;
                        if (percentOfTotalNForUseInNmaxCalculation != null)
                        {
                            if (model.OrganicManures != null && model.OrganicManures.Any())
                            {
                                foreach (var organicManure in model.OrganicManures)
                                {
                                    totalNitrogen = organicManure.N != null ? organicManure.N.Value : 0;
                                    break;
                                }

                                decimal decimalOfTotalNForUseInNmaxCalculation = Convert.ToDecimal(percentOfTotalNForUseInNmaxCalculation / 100.0);
                                if (model.ApplicationRate.HasValue)
                                {
                                    currentApplicationNitrogen = (totalNitrogen * model.ApplicationRate.Value * decimalOfTotalNForUseInNmaxCalculation);
                                }
                            }
                        }
                        (Farm farmData, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                        if (farmData != null && string.IsNullOrWhiteSpace(error.Message))
                        {
                            if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                            {
                                List<OrganicManureUpdateData> organicManureList = new List<OrganicManureUpdateData>();
                                foreach (OrganicManure organic in model.OrganicManures)
                                {
                                    //for updated manner output
                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                    {
                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (crop != null && string.IsNullOrWhiteSpace(error.Message))
                                        {
                                            Field fieldData = await _fieldService.FetchFieldByFieldId(crop.FieldID.Value);
                                            if (fieldData != null)
                                            {
                                                (SoilTypeSoilTextureResponse soilTexture, error) = await _organicManureService.FetchSoilTypeSoilTextureBySoilTypeId(fieldData.SoilTypeID ?? 0);
                                                int topSoilID = 0;
                                                int subSoilID = 0;
                                                if (error == null && soilTexture != null)
                                                {
                                                    topSoilID = soilTexture.TopSoilID;
                                                    subSoilID = soilTexture.SubSoilID;
                                                }
                                                (CropTypeLinkingResponse cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID.Value);
                                                if (error == null && cropTypeLinkingResponse != null)
                                                {
                                                    (ManureType manureType, error) = await _organicManureService.FetchManureTypeByManureTypeId(organic.ManureTypeID);
                                                    if (error == null && manureType != null)
                                                    {
                                                        var mannerOutput = new
                                                        {
                                                            runType = farmData.EnglishRules ? (int)NMP.Portal.Enums.RunType.MannerEngland : (int)NMP.Portal.Enums.RunType.MannerScotland,
                                                            postcode = farmData.ClimateDataPostCode.Split(" ")[0],
                                                            countryID = farmData.CountryID,
                                                            field = new
                                                            {
                                                                fieldID = fieldData.ID,
                                                                fieldName = fieldData.Name,
                                                                MannerCropTypeID = cropTypeLinkingResponse.MannerCropTypeID,
                                                                topsoilID = topSoilID,
                                                                subsoilID = subSoilID,
                                                                isInNVZ = Convert.ToBoolean(fieldData.IsWithinNVZ)
                                                            },
                                                            manureApplications = new[]
                                                         {
                                                new
                                                {
                                                    manureDetails = new
                                                    {
                                                        manureID = organic.ManureTypeID,
                                                        name = organic.ManureTypeName,
                                                        isLiquid = manureType.IsLiquid,
                                                        dryMatter = organic.DryMatterPercent,
                                                        totalN = organic.N,
                                                        nH4N = organic.NH4N,
                                                        uric = organic.UricAcid,
                                                        nO3N = organic.NO3N,
                                                        p2O5 = organic.P2O5,
                                                        k2O = organic.K2O,
                                                        sO3 = organic.SO3,
                                                        mgO = organic.MgO
                                                    },
                                                    applicationDate = organic.ApplicationDate?.ToString("yyyy-MM-dd"),
                                                    applicationRate = new
                                                    {
                                                        value = organic.ApplicationRate,
                                                        unit = model.IsManureTypeLiquid.Value ? Resource.lblMeterCubePerHectare : Resource.lblTonnesPerHectare
                                                    },
                                                    applicationMethodID = organic.ApplicationMethodID,
                                                    incorporationMethodID = organic.IncorporationMethodID,
                                                    incorporationDelayID = organic.IncorporationDelayID,
                                                    autumnCropNitrogenUptake = new
                                                    {
                                                        value = organic.AutumnCropNitrogenUptake,
                                                        unit = Resource.lblKgPerHectare
                                                    },
                                                    endOfDrainageDate = organic.SoilDrainageEndDate.ToString("yyyy-MM-dd"),
                                                    rainfallPostApplication = organic.Rainfall,
                                                    windspeedID = organic.WindspeedID,
                                                    rainTypeID = organic.RainfallWithinSixHoursID,
                                                    topsoilMoistureID = organic.MoistureID
                                                }
                                            }
                                                        };

                                                        string mannerJsonString = JsonConvert.SerializeObject(mannerOutput);
                                                        (MannerCalculateNutrientResponse mannerCalculateNutrientResponse, error) = await _organicManureService.FetchMannerCalculateNutrient(mannerJsonString);
                                                        if (error == null && mannerCalculateNutrientResponse != null)
                                                        {

                                                            OrganicManureUpdateData organicManure = new OrganicManureUpdateData
                                                            {
                                                                ID = model.UpdatedOrganicIds != null ? (model.UpdatedOrganicIds.Where(x => x.ManagementPeriodId.Value == organic.ManagementPeriodID).Select(x => x.OrganicManureId.Value).FirstOrDefault()) : 0,
                                                                ManagementPeriodID = organic.ManagementPeriodID,
                                                                ManureTypeID = organic.ManureTypeID,
                                                                ManureTypeName = model.ManureTypeName,
                                                                ApplicationDate = organic.ApplicationDate.Value,
                                                                Confirm = organic.Confirm,
                                                                N = organic.N,
                                                                P2O5 = organic.P2O5,
                                                                K2O = organic.K2O,
                                                                MgO = organic.MgO,
                                                                SO3 = organic.SO3,
                                                                AvailableN = mannerCalculateNutrientResponse.CurrentCropAvailableN,
                                                                ApplicationRate = organic.ApplicationRate,
                                                                DryMatterPercent = organic.DryMatterPercent,
                                                                UricAcid = organic.UricAcid,
                                                                EndOfDrain = organic.SoilDrainageEndDate,
                                                                Rainfall = organic.Rainfall,
                                                                AreaSpread = organic.AreaSpread,
                                                                ManureQuantity = organic.ManureQuantity,
                                                                ApplicationMethodID = organic.ApplicationMethodID,
                                                                IncorporationMethodID = organic.IncorporationMethodID,
                                                                IncorporationDelayID = organic.IncorporationDelayID,
                                                                NH4N = organic.NH4N,
                                                                NO3N = organic.NO3N,
                                                                AvailableP2O5 = mannerCalculateNutrientResponse.CropAvailableP2O5,
                                                                AvailableK2O = mannerCalculateNutrientResponse.CropAvailableK2O,
                                                                AvailableSO3 = mannerCalculateNutrientResponse.CropAvailableSO3,
                                                                WindspeedID = organic.WindspeedID,
                                                                RainfallWithinSixHoursID = organic.RainfallWithinSixHoursID,
                                                                MoistureID = organic.MoistureID,
                                                                AutumnCropNitrogenUptake = organic.AutumnCropNitrogenUptake,
                                                                AvailableNForNMax = currentApplicationNitrogen != null ? currentApplicationNitrogen : mannerCalculateNutrientResponse.CurrentCropAvailableN,
                                                                AvailableNForNextYear = mannerCalculateNutrientResponse.FollowingCropYear2AvailableN,
                                                                AvailableNForNextDefoliation = mannerCalculateNutrientResponse.NextGrassNCropCurrentYear

                                                            };

                                                            organicManureList.Add(organicManure);

                                                        }
                                                        else
                                                        {
                                                            TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                                                            return RedirectToAction("CheckAnswer");
                                                        }
                                                    }
                                                    else
                                                    {
                                                        TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                                                        return RedirectToAction("CheckAnswer");
                                                    }
                                                }
                                                else
                                                {
                                                    TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                                                    return RedirectToAction("CheckAnswer");
                                                }
                                            }
                                            else
                                            {
                                                TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                                                return RedirectToAction("CheckAnswer");
                                            }
                                        }
                                        else
                                        {
                                            TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                                            return RedirectToAction("CheckAnswer");
                                        }
                                    }

                                }

                                if (organicManureList != null && organicManureList.Count > 0)
                                {
                                    var OrganicManures = new List<object>();
                                    List<WarningMessage> warningMessageList = new List<WarningMessage>();
                                    foreach (var orgManure in organicManureList)
                                    {
                                        int? fieldID = null;
                                        int fieldTypeId = (int)NMP.Portal.Enums.FieldType.Arable;
                                        (ManagementPeriod ManData, error) = await _cropService.FetchManagementperiodById(orgManure.ManagementPeriodID);
                                        if (ManData != null)
                                        {

                                            (Crop crop, error) = (await _cropService.FetchCropById(ManData.CropID.Value));
                                            if (crop != null)
                                            {
                                                fieldTypeId = (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass) ?
                                                 (int)NMP.Portal.Enums.FieldType.Grass : (int)NMP.Portal.Enums.FieldType.Arable;
                                                fieldID= crop.FieldID;
                                            }
                                        }
                                        warningMessageList = new List<WarningMessage>();
                                        warningMessageList = await GetWarningMessages(model);
                                        warningMessageList.ForEach(x => x.JoiningID = x.WarningCodeID != (int)NMP.Portal.Enums.WarningCode.NMaxLimit ? orgManure.ID : fieldID);
                                        OrganicManures.Add(new
                                        {
                                            OrganicManure = orgManure,
                                            WarningMessages = warningMessageList.Count > 0 ? warningMessageList : null,
                                            FarmID = model.FarmId,
                                            FieldTypeID = fieldTypeId,
                                            SaveDefaultForFarm = model.IsAnyNeedToStoreNutrientValueForFuture
                                        });
                                    }

                                    var jsonData = new
                                    {
                                        OrganicManures
                                    };
                                    //var result = new
                                    //{
                                    //    OrganicManures = organicManureList.Select(orgManure => new
                                    //    {
                                    //        OrganicManure = orgManure,
                                    //        FarmID = model.FarmId,
                                    //        FieldTypeID = (int)NMP.Portal.Enums.FieldType.Arable,
                                    //        SaveDefaultForFarm = model.IsAnyNeedToStoreNutrientValueForFuture
                                    //    }).ToList()
                                    //};

                                    string jsonString = JsonConvert.SerializeObject(jsonData);
                                    (List<OrganicManure> organicManures, error) = await _organicManureService.UpdateOrganicManure(jsonString);
                                    if (string.IsNullOrWhiteSpace(error.Message) && organicManures.Count > 0)
                                    {
                                        bool success = true;
                                        _httpContextAccessor.HttpContext?.Session.Remove("OrganicManure");
                                        if (model.FieldList != null && model.FieldList.Count == 1)
                                        {

                                            if (model.IsComingFromRecommendation)
                                            {
                                                string fieldId = model.FieldList[0];
                                                return RedirectToAction("Recommendations", "Crop", new
                                                {
                                                    q = model.EncryptedFarmId,
                                                    r = _fieldDataProtector.Protect(fieldId),
                                                    s = model.EncryptedHarvestYear,
                                                    t = _cropDataProtector.Protect(Resource.MsgOrganicMaterialApplicationUpdated),
                                                    u = _cropDataProtector.Protect(Resource.MsgNutrientRecommendationsMayBeUpdated)

                                                });
                                            }
                                            else
                                            {
                                                return Redirect(Url.Action("HarvestYearOverview", "Crop", new
                                                {
                                                    id = model.EncryptedFarmId,
                                                    year = model.EncryptedHarvestYear,
                                                    q = _farmDataProtector.Protect(success.ToString()),
                                                    r = _cropDataProtector.Protect(Resource.MsgOrganicMaterialApplicationUpdated),
                                                    w = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault())
                                                }) + Resource.lblOrganicMaterialApplicationsForSorting);
                                            }
                                        }
                                        else if (!model.IsComingFromRecommendation)
                                        {
                                            return Redirect(Url.Action("HarvestYearOverview", "Crop", new
                                            {
                                                id = model.EncryptedFarmId,
                                                year = model.EncryptedHarvestYear,
                                                q = _farmDataProtector.Protect(success.ToString()),
                                                r = _cropDataProtector.Protect(Resource.MsgOrganicMaterialApplicationUpdated),
                                                v = _cropDataProtector.Protect(Resource.lblSelectAFieldToSeeItsUpdatedRecommendations)
                                            }) + Resource.lblOrganicMaterialApplicationsForSorting);
                                        }

                                    }
                                    else
                                    {
                                        TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                                        return RedirectToAction("CheckAnswer");
                                    }
                                }
                            }
                        }
                        else
                        {
                            TempData["UpdateOrganicManureError"] = Resource.MsgWeCouldNotUpdateOrganicManure;
                            return RedirectToAction("CheckAnswer");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["UpdateOrganicManureError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }
            return RedirectToAction("CheckAnswer");
        }
        [HttpGet]
        public async Task<IActionResult> DoubleCrop(string q)
        {
            _logger.LogTrace($"Organic Manure Controller : DoubleCrop({q}) action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            try
            {
                if (string.IsNullOrWhiteSpace(q) && model.OrganicManures != null && model.OrganicManures.Count > 0)
                {
                    model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(model.DoubleCropCurrentCounter.ToString());

                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                }
                else if (!string.IsNullOrWhiteSpace(q) && (model.DoubleCrop != null && model.DoubleCrop.Count > 0))
                {
                    int itemCount = Convert.ToInt32(_fieldDataProtector.Unprotect(q));
                    int index = itemCount - 1;
                    if (itemCount == 0)
                    {
                        model.DoubleCropCurrentCounter = 0;
                        model.DoubleCropEncryptedCounter = string.Empty;
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        if (model.IsCheckAnswer && (!model.IsManureTypeChange) && (!model.IsFieldGroupChange))
                        {
                            return RedirectToAction("CheckAnswer");
                        }
                        if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
                        {
                            return RedirectToAction("ManureGroup");
                        }
                        else if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
                        {
                            return RedirectToAction("OtherMaterialName");
                        }
                        else
                        {
                            return RedirectToAction("ManureType");
                        }
                    }
                    model.FieldID = model.DoubleCrop[index].FieldID;
                    model.FieldName = (await _fieldService.FetchFieldByFieldId(model.DoubleCrop[index].FieldID)).Name;
                    model.DoubleCropCurrentCounter = index;
                    model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(model.DoubleCropCurrentCounter.ToString());
                }
                if (model.FieldList != null && model.FieldList.Count > 0)
                {
                    List<Crop> cropList = new List<Crop>();
                    string cropTypeName = string.Empty;
                    if (model.DoubleCrop == null || model.IsAnyChangeInField)
                    {
                        if (model.DoubleCrop == null)
                        {
                            model.DoubleCrop = new List<DoubleCrop>();
                        }

                        int counter = model.DoubleCrop.Count + 1;

                        foreach (string fieldIdStr in model.FieldList)
                        {
                            int fieldId = Convert.ToInt32(fieldIdStr);

                            bool isFieldAlreadyPresent = model.DoubleCrop.Any(dc => dc.FieldID == fieldId);
                            if (model.IsAnyChangeInField && isFieldAlreadyPresent)
                            {
                                continue;
                            }

                            cropList = await _cropService.FetchCropsByFieldId(fieldId);
                            cropList = cropList?.Where(x => x.Year == model.HarvestYear).ToList();

                            if (cropList != null && cropList.Count == 2)
                            {
                                var cropTypeId = cropList.FirstOrDefault()?.CropTypeID;
                                if (cropTypeId.HasValue)
                                {
                                    cropTypeName = await _fieldService.FetchCropTypeById(cropTypeId.Value);
                                    var field = await _fieldService.FetchFieldByFieldId(fieldId);

                                    var doubleCrop = new DoubleCrop
                                    {
                                        CropName = cropTypeName,
                                        CropOrder = cropList.FirstOrDefault().CropOrder ?? 1,
                                        FieldID = fieldId,
                                        FieldName = field?.Name,
                                        EncryptedCounter = _fieldDataProtector.Protect(counter.ToString()),
                                        Counter = counter,
                                    };

                                    model.DoubleCrop.Add(doubleCrop);
                                    counter++;
                                }
                            }
                        }
                        //    model.DoubleCropCurrentCounter = counter;
                    }

                    if (model.DoubleCrop != null && model.DoubleCrop.Count > 0 &&
                    model.DoubleCrop.Any(dc => !model.FieldList.Contains(dc.FieldID.ToString())))
                    {
                        model.DoubleCrop?.RemoveAll(dc => !model.FieldList.Contains(dc.FieldID.ToString()));
                    }
                    cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.DoubleCrop[model.DoubleCropCurrentCounter].FieldID));
                    cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();

                    if (cropList != null && cropList.Count == 2)
                    {
                        var cropOptions = new List<SelectListItem>();
                        foreach (var crop in cropList.OrderBy(x => x.CropOrder))
                        {
                            cropTypeName = await _fieldService.FetchCropTypeById(crop.CropTypeID.Value);
                            cropOptions.Add(new SelectListItem
                            {
                                Text = $"{Resource.lblCrop} {crop.CropOrder} : {cropTypeName}",
                                Value = crop.ID.ToString()
                            });
                        }

                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        ViewBag.DoubleCropOptions = cropOptions;
                    }
                    if (model.DoubleCropCurrentCounter == 0)
                    {
                        model.FieldID = model.DoubleCrop[0].FieldID;
                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.DoubleCrop[0].FieldID)).Name;

                    }
                }

                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                TempData["ManureTypeError"] = ex.Message;
                return RedirectToAction("ManureType");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DoubleCrop(OrganicManureViewModel model)
        {
            _logger.LogTrace("Organic Manure Controller : DoubleCrop() post action called");
            if (model.DoubleCrop[model.DoubleCropCurrentCounter].CropID == null || model.DoubleCrop[model.DoubleCropCurrentCounter].CropID == 0)
            {
                ModelState.AddModelError("DoubleCrop[" + model.DoubleCropCurrentCounter + "].CropID", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            Error error = new Error();
            try
            {
                if (!ModelState.IsValid)
                {
                    if (model.FieldList != null && model.FieldList.Count > 0)
                    {

                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.DoubleCrop[model.DoubleCropCurrentCounter].FieldID));
                        cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                        if (model.DoubleCrop == null)
                        {
                            model.DoubleCrop = new List<DoubleCrop>();
                        }
                        if (cropList != null && cropList.Count == 2)
                        {
                            var cropOptions = new List<SelectListItem>();
                            foreach (var crop in cropList.OrderBy(x => x.CropOrder))
                            {
                                string cropTypeName = await _fieldService.FetchCropTypeById(crop.CropTypeID.Value);
                                cropOptions.Add(new SelectListItem
                                {
                                    Text = $"{Resource.lblCrop} {crop.CropOrder} : {cropTypeName}",
                                    Value = crop.ID.ToString()
                                });
                            }
                            _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                            ViewBag.DoubleCropOptions = cropOptions;
                        }
                    }
                    return View(model);
                }


                if (model.DoubleCrop.Any(x => x.FieldID == model.FieldID))
                {
                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(model.FieldID.Value);
                    cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                    if (cropList != null && cropList.Count == 2)
                    {
                        cropList = cropList.Where(x => x.ID == model.DoubleCrop[model.DoubleCropCurrentCounter].CropID).ToList();
                        if (cropList.Count > 0)
                        {
                            model.DoubleCrop[model.DoubleCropCurrentCounter].CropOrder = cropList.Select(x => x.CropOrder.Value).FirstOrDefault();
                            model.DoubleCrop[model.DoubleCropCurrentCounter].CropName = await _fieldService.FetchCropTypeById(Convert.ToInt32(cropList.Select(x => x.CropTypeID.Value).FirstOrDefault()));
                        }
                    }
                }
                if (model.DoubleCrop.Count > 0)
                {
                    (List<ManagementPeriod> managementPeriods, error) = await _cropService.FetchManagementperiodByCropId(model.DoubleCrop[model.DoubleCropCurrentCounter].CropID, true);
                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriods != null && managementPeriods.Count > 0)
                    {
                        foreach (var organicManure in model.OrganicManures)
                        {
                            if (organicManure.FieldID == model.DoubleCrop[model.DoubleCropCurrentCounter].FieldID)
                            {
                                if (model.IsCheckAnswer && (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId)))
                                {
                                    if (model.UpdatedOrganicIds != null)
                                    {
                                        foreach (var updatedOrganicIds in model.UpdatedOrganicIds)
                                        {
                                            if (organicManure.FieldName.Equals(updatedOrganicIds.Name))
                                            {
                                                updatedOrganicIds.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();
                                                break;
                                            }
                                        }
                                    }
                                }
                                //if (model.UpdatedOrganicIds != null)
                                //{
                                //    foreach (var updatedOrganicIds in model.UpdatedOrganicIds)
                                //    {
                                //        if (organicManure.FieldName.Equals(updatedOrganicIds.Name))
                                //        {
                                //            updatedOrganicIds.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();
                                //            break;
                                //        }
                                //    }
                                //}

                                organicManure.ManagementPeriodID = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();
                                break;
                            }
                        }
                    }
                }
                for (int i = 0; i < model.DoubleCrop.Count; i++)
                {
                    if (model.FieldID == model.DoubleCrop[i].FieldID)
                    {
                        model.DoubleCropCurrentCounter++;
                        if (i + 1 < model.DoubleCrop.Count)
                        {
                            model.FieldID = model.DoubleCrop[i + 1].FieldID;
                            model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                        }

                        break;
                    }
                }
                model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(model.DoubleCropCurrentCounter.ToString());
                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                foreach (var organic in model.OrganicManures)
                {
                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                    {
                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                        {
                            if (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && crop.DefoliationSequenceID != null)
                            {
                                model.IsAnyCropIsGrass = true;
                                break;
                            }
                            else
                            {
                                model.IsAnyCropIsGrass = false;
                            }
                        }
                    }
                }
                bool isCurrentFieldGrass = false;
                if (model.IsCheckAnswer)
                {
                    isCurrentFieldGrass = (model.DoubleCrop != null &&
                        model.DoubleCrop.Any(x => x.FieldID == model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID &&
                        string.Equals(x.CropName, Resource.lblGrass.Trim(), StringComparison.OrdinalIgnoreCase))) ? true : false;
                    int grassCropCounter = 0;
                    int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID)?.CropOrder
                    ?? model.CropOrder ?? 1;

                    if (isCurrentFieldGrass)
                    {
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID);
                        if (cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                        }
                        if (cropList.Count > 0)
                        {
                            if (cropList.Any(c => c.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && c.DefoliationSequenceID != null))
                            {
                                if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                                {
                                    (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropList.Select(x => x.ID.Value).FirstOrDefault(), false);
                                    var managementPeriodIdsToRemove = ManagementPeriod
                                    .Skip(1)
                                    .Select(mp => mp.ID.Value)
                                    .ToList();
                                    bool anyExists = ManagementPeriod
                                    .Any(mp => model.OrganicManures.Any(fm => fm.ManagementPeriodID == mp.ID.Value));
                                    if (!anyExists)
                                    {
                                        int counter = model.OrganicManures.Count();
                                        model.OrganicManures.AddRange(new List<OrganicManure>
                                        {
                                            new OrganicManure
                                            {
                                                ManagementPeriodID = ManagementPeriod.First().ID.Value,
                                                FieldID=model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID,
                                                FieldName=model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldName,
                                                EncryptedCounter = _fieldDataProtector.Protect((counter+1).ToString())
                                            }
                                        });
                                        model.GrassCropCount = model.OrganicManures.Count;
                                    }

                                    model.IsAnyCropIsGrass = true;
                                }
                            }
                        }
                    }

                }
                if (!isCurrentFieldGrass)
                {
                    if (model.IsAnyCropIsGrass.Value)
                    {
                        if (model.DoubleCropCurrentCounter == model.DoubleCrop.Count)
                        {
                            foreach (var doubleCrop in model.DoubleCrop)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(doubleCrop.CropID);
                                if (crop != null && string.IsNullOrWhiteSpace(error.Message) &&
                                    crop.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                {
                                    model.OrganicManures.RemoveAll(f => f.FieldID == doubleCrop.FieldID);
                                }
                            }
                        }

                    }
                    int counter = 0;
                    foreach (var organic in model.OrganicManures)
                    {
                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                            {
                                if (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass)
                                {
                                    counter++;
                                }
                            }
                        }
                    }
                    model.GrassCropCount = counter;
                }

                if (model.DoubleCropCurrentCounter == model.DoubleCrop.Count)
                {
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange) && (!isCurrentFieldGrass))
                    {
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("CheckAnswer");
                    }
                    else
                    {
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange) && (!isCurrentFieldGrass))
                        {
                            //model.GrassCropCount = null;
                            //model.IsSameDefoliationForAll = null;
                            //model.IsAnyChangeInSameDefoliationFlag = false;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            return RedirectToAction("CheckAnswer");

                        }
                        else
                        {
                            if (!model.IsAnyCropIsGrass.Value)
                            {
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                return RedirectToAction("ManureApplyingDate");
                            }
                            else
                            {
                                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                                {
                                    if (model.OrganicManures.Any(z => z.Defoliation != null))
                                    {
                                        model.IsSameDefoliationForAll = null;
                                    }
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                    return RedirectToAction("IsSameDefoliationForAll");
                                }
                                model.IsSameDefoliationForAll = true;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                return RedirectToAction("Defoliation");
                            }
                        }

                        //logic end for defoliation

                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("ManureApplyingDate");
                    }
                }
                else
                {
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange) && (!isCurrentFieldGrass))
                    {
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("CheckAnswer");
                    }
                    else if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
                    {
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange) && (!isCurrentFieldGrass))
                        {
                            //model.GrassCropCount = null;
                            //model.IsSameDefoliationForAll = null;
                            //model.IsAnyChangeInSameDefoliationFlag = false;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            return RedirectToAction("CheckAnswer");

                        }
                        else
                        {
                            if (!model.IsAnyCropIsGrass.Value)
                            {
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                return RedirectToAction("ManureApplyingDate");
                            }
                            else
                            {
                                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                                {
                                    if (model.OrganicManures.Any(z => z.Defoliation != null))
                                    {
                                        model.IsSameDefoliationForAll = null;
                                    }
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                    return RedirectToAction("IsSameDefoliationForAll");
                                }
                                model.IsSameDefoliationForAll = true;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                                return RedirectToAction("Defoliation");
                            }
                        }

                        //logic end for defoliation

                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("ManureApplyingDate");
                    }
                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(model.FieldID.Value);
                    cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();

                    if (cropList != null && cropList.Count == 2)
                    {
                        var cropOptions = new List<SelectListItem>();
                        foreach (var crop in cropList.OrderBy(x => x.CropOrder))
                        {
                            string cropTypeName = await _fieldService.FetchCropTypeById(crop.CropTypeID.Value);
                            cropOptions.Add(new SelectListItem
                            {
                                Text = $"{Resource.lblCrop} {crop.CropOrder} : {cropTypeName}",
                                Value = crop.ID.ToString()
                            });
                        }

                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                        ViewBag.DoubleCropOptions = cropOptions;

                    }
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["DoubleCropError"] = ex.Message;
                return View(model);
            }
        }



        //
        [HttpGet]
        public async Task<IActionResult> ManureType()
        {
            _logger.LogTrace($"Organic Manure Controller : ManureType() action called");
            Error error = null;
            OrganicManureViewModel model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            try
            {
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                if (error == null)
                {
                    if (manureTypeList.Count > 0)
                    {
                        var manures = manureTypeList.OrderBy(m => m.SortOrder).ToList();
                        var SelectListItem = manures.Select(f => new SelectListItem
                        {
                            Value = f.Id.ToString(),
                            Text = f.Name
                        }).ToList();
                        ViewBag.ManureTypeList = SelectListItem.ToList();
                    }
                    return View(model);
                }
                else
                {
                    TempData["ManureGroupError"] = error.Message;
                    return RedirectToAction("ManureGroup", model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManureType() action : {ex.Message}, {ex.StackTrace}");
                TempData["ManureGroupError"] = ex.Message;
                return RedirectToAction("ManureGroup", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManureType(OrganicManureViewModel model)
        {
            _logger.LogTrace($"Organic Manure Controller : ManureType() post action called");
            Error error = null;
            if (model.ManureTypeId == null)
            {
                ModelState.AddModelError("ManureTypeId", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            try
            {
                OrganicManureViewModel orgManureViewModel = null;
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    orgManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                int countryId = model.isEnglishRules ? (int)NMP.Portal.Enums.RB209Country.England : (int)NMP.Portal.Enums.RB209Country.Scotland;
                List<ManureType> manureTypeList = new List<ManureType>();
                if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials)
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList((int)NMP.Portal.Enums.ManureGroup.AnotherTypeOfOrganicMaterial, countryId);
                }
                else
                {
                    (manureTypeList, error) = await _organicManureService.FetchManureTypeList(model.ManureGroupIdForFilter.Value, countryId);
                }
                if (error == null)
                {
                    if (manureTypeList.Count > 0)
                    {
                        var manures = manureTypeList.OrderBy(m => m.SortOrder).ToList();
                        var SelectListItem = manures.Select(f => new SelectListItem
                        {
                            Value = f.Id.ToString(),
                            Text = f.Name
                        }).ToList();
                        ViewBag.ManureTypeList = SelectListItem.OrderBy(x => x.Text).ToList(); ;

                    }
                    if (!ModelState.IsValid)
                    {
                        return View(model);
                    }

                    ManureType manureType = manureTypeList.FirstOrDefault(x => x.Id == model.ManureTypeId);
                    if (manureType != null)
                    {
                        model.ManureTypeName = manureType.Name;
                        model.IsManureTypeLiquid = manureType.IsLiquid.Value;
                        foreach (var orgManure in model.OrganicManures)
                        {
                            orgManure.ManureTypeID = model.ManureTypeId.Value;
                            orgManure.K2O = manureType.K2O.Value;
                            if (manureType.MgO != null)
                            {
                                orgManure.MgO = manureType.MgO.Value;
                            }
                            orgManure.P2O5 = manureType.P2O5.Value;
                            if (manureType.SO3 != null)
                            {
                                orgManure.SO3 = manureType.SO3.Value;
                            }
                            orgManure.NH4N = manureType.NH4N.Value;
                            orgManure.NO3N = manureType.NO3N.Value;
                            orgManure.UricAcid = manureType.Uric.Value;
                            orgManure.DryMatterPercent = manureType.DryMatter.Value;
                            orgManure.N = manureType.TotalN.Value;
                        }
                    }

                    //OrganicManureViewModel organicManureViewModel = JsonConvert.DeserializeObject<OrganicManureViewModel>(HttpContext.Session.GetString("OrganicManure"));
                    if (orgManureViewModel != null)
                    {
                        if (orgManureViewModel.ManureTypeId != model.ManureTypeId)
                        {
                            model.DryMatterPercent = null;
                            model.N = null;
                            model.P2O5 = null;
                            model.NH4N = null;
                            model.UricAcid = null;
                            model.SO3 = null;
                            model.K2O = null;
                            model.MgO = null;
                            model.NO3N = null;
                            model.IsDefaultValueChange = true;
                            //model.DefaultNutrientValue = null;
                        }
                    }

                    if (model.ManureGroupIdForFilter.HasValue)
                    {
                        model.ManureGroupId = model.ManureGroupIdForFilter;
                    }
                    foreach (var organicManures in model.OrganicManures)
                    {
                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManures.ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                            {
                                if (crop.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && crop.DefoliationSequenceID != null)
                                {
                                    model.IsAnyCropIsGrass = true;
                                }
                            }
                        }
                    }
                    //if manure type change 
                    if (model.IsCheckAnswer)
                    {
                        //OrganicManureViewModel organicManure = new OrganicManureViewModel();
                        //if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                        //{
                        //    organicManure = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                        //}
                        //else
                        //{
                        //    return RedirectToAction("FarmList", "Farm");
                        //}

                        if (orgManureViewModel.ManureTypeId != model.ManureTypeId)
                        {
                            model.IsManureTypeChange = true;
                            if (model.ApplicationRateMethod == (int)NMP.Portal.Enums.ApplicationRate.UseDefaultApplicationRate)
                            {
                                //model.ApplicationRateMethod = null;
                                model.ApplicationRate = null;
                                foreach (var orgManure in model.OrganicManures)
                                {
                                    orgManure.ApplicationRate = null;
                                }
                            }

                            //if manure type is changed liquid to soild or solid to liquid then ApplicationMethod,IncorporationMethod,IncorporationDelay need to set null
                            if (orgManureViewModel.IsManureTypeLiquid.Value != model.IsManureTypeLiquid.Value)
                            {
                                model.ApplicationMethod = null;
                                model.IncorporationMethod = null;
                                model.IncorporationDelay = null;
                                model.ApplicationMethodName = string.Empty;
                                model.IncorporationMethodName = string.Empty;
                                model.IncorporationDelayName = string.Empty;
                                foreach (var orgManure in model.OrganicManures)
                                {
                                    orgManure.ApplicationMethodID = null;
                                    orgManure.IncorporationDelayID = null;
                                    orgManure.IncorporationMethodID = null;
                                }
                            }

                            //if manure type is changed then we need to bind default values
                            (ManureType manureTypeData, error) = await _organicManureService.FetchManureTypeByManureTypeId(model.ManureTypeId.Value);
                            if (error == null)
                            {
                                model.ManureType = manureTypeData;
                                //if (model.DefaultNutrientValue.HasValue && (!model.DefaultNutrientValue.Value))
                                //{
                                if (!string.IsNullOrWhiteSpace(model.DefaultNutrientValue) && model.DefaultNutrientValue == Resource.lblIwantToEnterARecentOrganicMaterialAnalysis)
                                {
                                    model.DryMatterPercent = model.ManureType.DryMatter;
                                    model.N = model.ManureType.TotalN;
                                    model.P2O5 = model.ManureType.P2O5;
                                    model.NH4N = model.ManureType.NH4N;
                                    model.UricAcid = model.ManureType.Uric;
                                    model.SO3 = model.ManureType.SO3;
                                    model.K2O = model.ManureType.K2O;
                                    model.MgO = model.ManureType.MgO;
                                    model.NO3N = model.ManureType.NO3N;
                                }
                                model.DryMatterPercent = manureTypeData.DryMatter;
                                model.N = manureTypeData.TotalN;
                                model.NH4N = manureTypeData.NH4N;
                                model.NO3N = manureTypeData.NO3N;
                                model.K2O = manureTypeData.K2O;
                                model.SO3 = manureTypeData.SO3;
                                model.MgO = manureTypeData.MgO;
                                model.P2O5 = manureTypeData.P2O5;
                                model.UricAcid = manureTypeData.Uric;
                                foreach (var orgManure in model.OrganicManures)
                                {
                                    orgManure.DryMatterPercent = manureTypeData.DryMatter;
                                    orgManure.N = manureTypeData.TotalN;
                                    orgManure.NH4N = manureTypeData.NH4N;
                                    orgManure.NO3N = manureTypeData.NO3N;
                                    orgManure.K2O = manureTypeData.K2O;
                                    orgManure.SO3 = manureTypeData.SO3;
                                    orgManure.MgO = manureTypeData.MgO;
                                    orgManure.P2O5 = manureTypeData.P2O5;
                                    orgManure.UricAcid = manureTypeData.Uric;
                                }
                            }
                            else
                            {
                                TempData["ManureTypeError"] = error.Message;
                                return View(model);
                            }


                            //if manure type is solid then need to set application method value.
                            if (!model.IsManureTypeLiquid.Value)
                            {
                                List<Crop> cropsResponse = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FieldList[0]));
                                var fieldType = cropsResponse.Where(x => x.Year == model.HarvestYear).Select(x => x.FieldType).FirstOrDefault();


                                (List<ApplicationMethodResponse> applicationMethodList, error) = await _organicManureService.FetchApplicationMethodList(fieldType ?? 0, model.IsManureTypeLiquid.Value);
                                if (error == null && applicationMethodList.Count > 0)
                                {
                                    model.ApplicationMethod = applicationMethodList[0].ID;
                                    foreach (var orgManure in model.OrganicManures)
                                    {
                                        orgManure.ApplicationMethodID = model.ApplicationMethod.Value;
                                    }
                                    (model.ApplicationMethodName, error) = await _organicManureService.FetchApplicationMethodById(model.ApplicationMethod.Value);
                                    if (error != null)
                                    {
                                        TempData["ManureTypeError"] = error.Message;
                                        return View(model);
                                    }
                                }
                                else if (error != null)
                                {
                                    TempData["ManureTypeError"] = error.Message;
                                    return View(model);
                                }

                            }
                        }
                    }



                    //model.IsAnyCropIsGrass = false;
                    //logic start for defoliation
                    List<string> fieldsToRemove = new List<string>();
                    foreach (string field in model.FieldList)
                    {
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));

                        if (cropList.Count > 0)
                        {
                            if (int.TryParse(model.FieldGroup, out int cropTypeId))
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                            }
                            else
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();

                                if (cropList != null && cropList.Count == 2)
                                {
                                    model.IsDoubleCropAvailable = true;
                                    int counter = 0;
                                    model.DoubleCropCurrentCounter = counter;
                                    model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                                    model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                }
                                else
                                {
                                    if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass || (x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.IsBasePlan)))
                                    {
                                        fieldsToRemove.Add(field);
                                    }
                                    if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                                    {
                                        model.IsAnyCropIsGrass = true;
                                    }
                                }
                            }
                        }
                    }
                    List<string> fieldListCopy = new List<string>(model.FieldList);
                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {
                        foreach (var field in fieldsToRemove)
                        {
                            fieldListCopy.Remove(field);
                        }

                        string fieldIds = string.Join(",", fieldListCopy);
                        List<int> managementIds = new List<int>();
                        if (int.TryParse(model.FieldGroup, out int cropTypeIdForFilter))
                        {
                            (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, cropTypeIdForFilter.ToString(), null);//1 is CropOrder
                        }
                        else
                        {
                            (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);//1 is CropOrder
                        }
                        if (error == null)
                        {
                            if (managementIds.Count > 0)
                            {
                                if (model.OrganicManures == null)
                                {
                                    model.OrganicManures = new List<OrganicManure>();
                                }
                                if (model.OrganicManures.Count > 0)
                                {
                                    model.OrganicManures.Clear();
                                }
                                int counter = 1;
                                foreach (var manIds in managementIds)
                                {
                                    var organicManure = new OrganicManure
                                    {
                                        ManagementPeriodID = manIds,
                                        ManureTypeID = model.ManureTypeId.Value,
                                        ManureTypeName = model.ManureTypeName,
                                        ApplicationMethodID = model.ApplicationMethod,
                                        N = model.N,
                                        P2O5 = model.P2O5,
                                        NH4N = model.NH4N,
                                        K2O = model.K2O,
                                        MgO = model.MgO,
                                        NO3N = model.NO3N,
                                        SO3 = model.SO3,
                                        ApplicationRate = model.ApplicationRate != null ? model.ApplicationRate : null,
                                        AreaSpread = model.Area != null ? model.Area : null,
                                        ManureQuantity = model.Quantity != null ? model.Quantity : null,
                                        DryMatterPercent = model.DryMatterPercent,
                                        UricAcid = model.UricAcid,
                                        EncryptedCounter = _fieldDataProtector.Protect(counter.ToString())

                                    };
                                    if (model.ApplicationDate != null)
                                    {
                                        organicManure.ApplicationDate = model.ApplicationDate.Value;
                                    }
                                    if (model.WindspeedID != null)
                                    {
                                        organicManure.WindspeedID = model.WindspeedID.Value;
                                    }
                                    if (model.RainfallWithinSixHoursID != null)
                                    {
                                        organicManure.RainfallWithinSixHoursID = model.RainfallWithinSixHoursID.Value;
                                    }
                                    if (model.TotalRainfall != null)
                                    {
                                        organicManure.Rainfall = model.TotalRainfall.Value;
                                    }
                                    if (model.MoistureTypeId != null)
                                    {
                                        organicManure.MoistureID = model.MoistureTypeId.Value;
                                    }
                                    if (model.SoilDrainageEndDate != null)
                                    {
                                        organicManure.SoilDrainageEndDate = model.SoilDrainageEndDate.Value;
                                        organicManure.EndOfDrain = model.SoilDrainageEndDate.Value;
                                    }
                                    counter++;
                                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                                    {

                                        if (orgManureViewModel.OrganicManures != null && orgManureViewModel.OrganicManures.Count > 0)
                                        {
                                            for (int i = 0; i < orgManureViewModel.OrganicManures.Count; i++)
                                            {
                                                if (orgManureViewModel.OrganicManures[i].ManagementPeriodID == manIds && orgManureViewModel.OrganicManures[i].Defoliation != null)
                                                {
                                                    organicManure.Defoliation = orgManureViewModel.OrganicManures[i].Defoliation;
                                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manIds);
                                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                                    {
                                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                                        {
                                                            (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                                            if (error == null && defoliationSequence != null)
                                                            {
                                                                string description = defoliationSequence.DefoliationSequenceDescription;

                                                                string[] defoliationParts = description.Split(',')
                                                                                                       .Select(x => x.Trim())
                                                                                                       .ToArray();

                                                                string selectedDefoliation = (organicManure.Defoliation.Value > 0 && organicManure.Defoliation.Value <= defoliationParts.Length)
                                                                    ? $"{Enum.GetName(typeof(PotentialCut), organicManure.Defoliation.Value)} ({defoliationParts[organicManure.Defoliation.Value - 1]})"
                                                                    : $"{organicManure.Defoliation.Value}";

                                                                organicManure.DefoliationName = selectedDefoliation;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        return RedirectToAction("FarmList", "Farm");
                                    }
                                    model.OrganicManures.Add(organicManure);
                                }

                            }
                            model.DefoliationCurrentCounter = 0;
                        }
                        else
                        {
                            TempData["ManureTypeError"] = error.Message;
                            return View(model);
                        }
                    }

                    int grassCropCounter = 0;
                    foreach (var field in model.FieldList)
                    {
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                        if (cropList.Count > 0)
                        {
                            if (int.TryParse(model.FieldGroup, out int cropTypeIdForFilter))
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeIdForFilter).ToList();
                            }
                            else
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == 1).ToList();
                            }
                        }
                        if (cropList.Count > 0)
                        {
                            if (cropList.Any(c => c.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && c.DefoliationSequenceID != null))
                            {
                                if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
                                {

                                    (List<ManagementPeriod> managementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropList.Select(x => x.ID.Value).FirstOrDefault(), false);

                                    var filteredOrganicManure = model.OrganicManures
                                     .Where(fm => managementPeriod.Any(mp => mp.ID == fm.ManagementPeriodID) &&
                                         fm.Defoliation == null).ToList();
                                    if (filteredOrganicManure != null && filteredOrganicManure.Count == managementPeriod.Count)
                                    {
                                        var managementPeriodIdsToRemove = managementPeriod
                                       .Skip(1)
                                       .Select(mp => mp.ID.Value)
                                       .ToList();
                                        model.OrganicManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));
                                    }
                                    else
                                    {
                                        model.OrganicManures.RemoveAll(x => managementPeriod.Any(mp => mp.ID == x.ManagementPeriodID) &&
                                        x.Defoliation == null);
                                    }
                                    grassCropCounter++;


                                }
                                model.IsAnyCropIsGrass = true;
                            }
                        }
                    }
                    int orgCounter = 1;
                    foreach (var organic in model.OrganicManures)
                    {
                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organic.ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                            {
                                organic.FieldID = crop.FieldID;
                                organic.FieldName = (await _fieldService.FetchFieldByFieldId(organic.FieldID.Value)).Name;
                                organic.EncryptedCounter = _fieldDataProtector.Protect(orgCounter.ToString());
                                orgCounter++;
                            }
                        }
                    }
                    model.GrassCropCount = grassCropCounter;
                }

                //logic end for defoliation
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);


                if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
                {
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("OtherMaterialName");
                }
                if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                {
                    //model.GrassCropCount = null;
                    //model.IsSameDefoliationForAll = null;
                    //model.IsAnyChangeInSameDefoliationFlag = false;
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                    return RedirectToAction("CheckAnswer");

                }
                else
                {
                    if (model.IsDoubleCropAvailable)
                    {
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("DoubleCrop");
                    }
                    if (model.IsAnyCropIsGrass == null || (model.IsAnyCropIsGrass != null && !model.IsAnyCropIsGrass.Value))
                    {
                        model.GrassCropCount = null;
                        model.IsSameDefoliationForAll = null;
                        model.IsAnyChangeInSameDefoliationFlag = false;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("ManureApplyingDate");
                    }
                    else
                    {
                        if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                        {
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            return RedirectToAction("IsSameDefoliationForAll");
                        }
                        model.IsSameDefoliationForAll = true;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                        return RedirectToAction("Defoliation");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Organic Manure Controller : Exception in ManureType() post action : {ex.Message}, {ex.StackTrace}");
                TempData["ManureTypeError"] = ex.Message;
                return View(model);
            }

            return RedirectToAction("ManureApplyingDate");


        }
        //


        [HttpGet]
        public async Task<IActionResult> IsSameDefoliationForAll()
        {
            _logger.LogTrace($"OrganicManure Controller : IsSameDefoliationForAll() action called");
            Error error = new Error();

            OrganicManureViewModel model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (model.IsAnyChangeInSameDefoliationFlag)
            {
                model.IsAnyChangeInSameDefoliationFlag = false;
            }
            try
            {

                List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();
                foreach (var organic in model.OrganicManures)
                {
                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(organic.FieldID));

                    if (cropList.Count > 0)
                    {
                        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == model.CropOrder).ToList();
                    }

                    if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                    {
                        var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                        (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                        if (ManagementPeriod != null)
                        {
                            List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();
                            List<SelectListItem> defoliationSelectList = new List<SelectListItem>();
                            //var defoliationIdsList = ManagementPeriod.Select(x => x.Defoliation.Value.ToString()).ToList();

                            (Crop crop, error) = await _cropService.FetchCropById(cropId);
                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                            {
                                (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                if (error == null && defoliationSequence != null)
                                {
                                    string description = defoliationSequence.DefoliationSequenceDescription;
                                    string[] defoliationParts = description.Split(',')
                                                                            .Select(x => x.Trim())
                                                                            .ToArray();
                                    List<SelectListItem> allDefoliationWithName = new List<SelectListItem>();
                                    foreach (int defoliation in defoliationList)
                                    {
                                        string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                        ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                        : defoliation.ToString();

                                        allDefoliationWithName.Add(new SelectListItem
                                        {
                                            Text = text,
                                            Value = defoliation.ToString()
                                        });


                                    }
                                    allDefoliations.Add(allDefoliationWithName);
                                }
                            }


                        }

                    }
                }
                if (allDefoliations.Count > 0)
                {
                    List<List<string>> defoliationSequenceList = allDefoliations
                .Select(list => list.Select(item => item.Text).ToList())
                .ToList();

                    if (defoliationSequenceList.Count > 0)
                    {
                        List<string> commonDefoliations = defoliationSequenceList.Count > 0
                        ? defoliationSequenceList.Aggregate((prev, next) => prev.Intersect(next).ToList())
                        : new List<string>();
                        if (commonDefoliations.Count > 0)
                        {

                            List<SelectListItem> flattenedList = allDefoliations
                           .SelectMany(list => list)
                           .ToList();

                            if (flattenedList.Count > 0)
                            {
                                List<SelectListItem> commonDefoliationItems = flattenedList
                                .Where(item => commonDefoliations.Contains(item.Text))
                                .GroupBy(item => item.Text)
                                .Select(g => g.First())
                                .ToList();
                                model.NeedToShowSameDefoliationForAll = true;
                            }
                        }
                        else
                        {
                            model.IsSameDefoliationForAll = false;
                            model.NeedToShowSameDefoliationForAll = false;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                            return RedirectToAction("Defoliation");
                        }

                    }
                }


                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                if (model.IsDoubleCropAvailable)
                {
                    TempData["DoubleCropError"] = ex.Message;
                    return RedirectToAction("DoubleCrop", new { q = model.EncryptedCounter });
                }
                else
                {
                    TempData["ManureTypeError"] = ex.Message;
                    return RedirectToAction("ManureType");

                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult IsSameDefoliationForAll(OrganicManureViewModel model)
        {
            _logger.LogTrace($"OrganicManure Controller : IsSameDefoliationForAll() post action called");
            if (model.IsSameDefoliationForAll == null)
            {
                ModelState.AddModelError("IsSameDefoliationForAll", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            try
            {
                model.DefoliationCurrentCounter = 0;
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    OrganicManureViewModel organicManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");

                    if (model.IsSameDefoliationForAll != organicManureViewModel.IsSameDefoliationForAll)
                    {
                        model.IsAnyChangeInSameDefoliationFlag = true;
                    }
                    else
                    {
                        model.IsAnyChangeInSameDefoliationFlag = false;
                    }
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                if (model.IsAnyChangeInSameDefoliationFlag)
                {
                    foreach (var organic in model.OrganicManures)
                    {
                        organic.Defoliation = null;
                        organic.DefoliationName = null;
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("OrganicManure", model);
                if (!model.IsAnyChangeInSameDefoliationFlag && model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                {
                    return RedirectToAction("CheckAnswer");
                }
            }
            catch (Exception ex)
            {
                TempData["IsSameDefoliationForAllError"] = ex.Message;
                return View(model);
            }
            return RedirectToAction("Defoliation");
        }

        [HttpGet]
        public async Task<IActionResult> Defoliation(string q)
        {
            _logger.LogTrace($"OrganicManure Controller : Defoliation({q}) action called");
            OrganicManureViewModel model = new OrganicManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FieldID.Value)?.CropOrder
                       ?? 1;
                if (string.IsNullOrWhiteSpace(q) && model.OrganicManures != null && model.OrganicManures.Count > 0)
                {
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                    model.FieldID = model.OrganicManures[0].FieldID.Value;
                    model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                    int manId = model.OrganicManures.Where(x => x.FieldID == model.FieldID).Select(x => x.ManagementPeriodID).FirstOrDefault();

                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                    cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FieldID.Value)?.CropOrder
                       ?? crop.CropOrder.Value;
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                }
                else if (!string.IsNullOrWhiteSpace(q) && (model.OrganicManures != null && model.OrganicManures.Count > 0))
                {
                    int itemCount = Convert.ToInt32(_fieldDataProtector.Unprotect(q));
                    int index = itemCount - 1;
                    if (itemCount == 0)
                    {
                        model.DefoliationCurrentCounter = 0;
                        model.DefoliationEncryptedCounter = string.Empty;
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);

                        if (model.GrassCropCount != null && model.GrassCropCount.Value > 1 && model.NeedToShowSameDefoliationForAll)
                        {
                            return RedirectToAction("IsSameDefoliationForAll");
                        }
                        if (model.IsDoubleCropAvailable)
                        {
                            return RedirectToAction("DoubleCrop", new { q = model.DoubleCropEncryptedCounter });
                        }
                        if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
                        {
                            return RedirectToAction("ManureGroup");
                        }
                        if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
                        {
                            return RedirectToAction("OtherMaterialName");
                        }
                        return RedirectToAction("ManureType");
                    }
                    model.FieldID = model.OrganicManures[index].FieldID.Value;
                    model.FieldName = (await _fieldService.FetchFieldByFieldId(model.OrganicManures[index].FieldID.Value)).Name;
                    model.DefoliationCurrentCounter = index;
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());

                    int manId = model.OrganicManures.Where(x => x.FieldID == model.FieldID).Select(x => x.ManagementPeriodID).FirstOrDefault();

                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                    cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FieldID.Value)?.CropOrder
                       ?? crop.CropOrder.Value;
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                }

                if (model.IsSameDefoliationForAll.HasValue && model.IsSameDefoliationForAll.Value)
                {
                    //List<List<string>> allDefoliations = new List<List<string>>();
                    List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();
                    foreach (var organic in model.OrganicManures)
                    {
                        int manId = model.OrganicManures.Where(x => x.FieldID == model.FieldID).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                        cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == organic.FieldID.Value)?.CropOrder
                        ?? crop.CropOrder.Value;
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(organic.FieldID));

                        if (cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();
                        }

                        if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                        {
                            var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                            (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                            if (ManagementPeriod != null)
                            {
                                List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();
                                List<SelectListItem> defoliationSelectList = new List<SelectListItem>();
                                //var defoliationIdsList = ManagementPeriod.Select(x => x.Defoliation.Value.ToString()).ToList();

                                (crop, error) = await _cropService.FetchCropById(cropId);
                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                {
                                    (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                    if (error == null && defoliationSequence != null)
                                    {
                                        string description = defoliationSequence.DefoliationSequenceDescription;
                                        string[] defoliationParts = description.Split(',')
                                                                                .Select(x => x.Trim())
                                                                                .ToArray();
                                        List<SelectListItem> allDefoliationWithName = new List<SelectListItem>();
                                        foreach (int defoliation in defoliationList)
                                        {
                                            string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                            ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                            : defoliation.ToString();

                                            allDefoliationWithName.Add(new SelectListItem
                                            {
                                                Text = text,
                                                Value = defoliation.ToString()
                                            });


                                        }
                                        allDefoliations.Add(allDefoliationWithName);
                                    }
                                }


                            }

                        }
                    }
                    if (allDefoliations.Count > 0)
                    {
                        List<List<string>> defoliationValues = allDefoliations
                    .Select(list => list.Select(item => item.Text).ToList())
                    .ToList();

                        if (defoliationValues.Count > 0)
                        {
                            List<string> commonDefoliations = defoliationValues.Count > 0
                            ? defoliationValues.Aggregate((prev, next) => prev.Intersect(next).ToList())
                            : new List<string>();

                            if (commonDefoliations.Count > 0)
                            {
                                List<SelectListItem> flattenedList = allDefoliations
                                .SelectMany(list => list)
                                .ToList();

                                if (flattenedList.Count > 0)
                                {
                                    List<SelectListItem> commonDefoliationItems = flattenedList
                                    .Where(item => commonDefoliations.Contains(item.Text))
                                    .GroupBy(item => item.Text)
                                    .Select(g => g.First())
                                    .ToList();
                                    foreach (var item in commonDefoliationItems)
                                    {
                                        var parts = item.Text.Split('-');
                                        if (parts.Length == 2)
                                        {
                                            var left = parts[0].Trim();
                                            var right = parts[1].Trim();

                                            if (!string.IsNullOrWhiteSpace(right))
                                            {
                                                right = char.ToUpper(right[0]) + right.Substring(1);
                                            }

                                            item.Text = $"{left} - {right}";
                                        }
                                    }

                                    //ViewBag.DefoliationList = commonDefoliationItems;
                                    ViewBag.DefoliationList = commonDefoliationItems.Select(f => new SelectListItem
                                    {
                                        Value = f.Value,
                                        Text = f.Text.ToString()
                                    }).ToList();
                                }
                            }
                        }
                    }

                }
                else
                {
                    if (model.DefoliationCurrentCounter >= 0)
                    {
                        int manId = model.OrganicManures.Where(x => x.FieldID == model.FieldID).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                        cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FieldID.Value)?.CropOrder
                       ?? crop.CropOrder.Value;
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.OrganicManures[model.DefoliationCurrentCounter].FieldID));

                        if (cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == cropOrder).ToList();
                        }

                        if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                        {
                            var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                            (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                            if (ManagementPeriod != null)
                            {
                                List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();

                                List<SelectListItem> defoliationSelectList = new List<SelectListItem>();

                                (crop, error) = await _cropService.FetchCropById(cropId);
                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                {
                                    (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                    if (error == null && defoliationSequence != null)
                                    {
                                        string description = defoliationSequence.DefoliationSequenceDescription;
                                        string[] defoliationParts = description.Split(',')
                                                                                .Select(x => x.Trim())
                                                                                .ToArray();

                                        foreach (int defoliation in defoliationList)
                                        {
                                            string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                            ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                            : defoliation.ToString();

                                            defoliationSelectList.Add(new SelectListItem
                                            {
                                                Text = text,
                                                Value = defoliation.ToString()
                                            });
                                            foreach (var item in defoliationSelectList)
                                            {
                                                var parts = item.Text.Split('-');
                                                if (parts.Length == 2)
                                                {
                                                    var left = parts[0].Trim();
                                                    var right = parts[1].Trim();

                                                    if (!string.IsNullOrWhiteSpace(right))
                                                    {
                                                        right = char.ToUpper(right[0]) + right.Substring(1);
                                                    }

                                                    item.Text = $"{left} - {right}";
                                                }
                                            }

                                        }
                                    }
                                }

                                //ViewBag.DefoliationList = defoliationSelectList;
                                ViewBag.DefoliationList = defoliationSelectList.Select(f => new SelectListItem
                                {
                                    Value = f.Value,
                                    Text = f.Text.ToString()
                                }).ToList();
                            }
                        }
                    }

                }
                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in Defoliation() action : {ex.Message}, {ex.StackTrace}");
                if (string.IsNullOrWhiteSpace(model.EncryptedOrgManureId))
                {
                    TempData["FieldGroupError"] = ex.Message;
                    if (TempData["FieldError"] != null)
                    {
                        TempData["FieldError"] = null;
                    }
                }
                else
                {
                    TempData["CheckYourAnswerError"] = ex.Message;
                    return RedirectToAction("CheckAnswer");
                }
                return RedirectToAction("FieldGroup", new { q = model.EncryptedFarmId, r = model.EncryptedHarvestYear });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Defoliation(OrganicManureViewModel model)
        {
            _logger.LogTrace($"OrganicManure Controller : Defoliation() post action called");
            Error error = null;
            try
            {
                if (model.OrganicManures[model.DefoliationCurrentCounter].Defoliation == null)
                {
                    ModelState.AddModelError("OrganicManures[" + model.DefoliationCurrentCounter + "].Defoliation", Resource.MsgSelectAnOptionBeforeContinuing);
                }

                if (!ModelState.IsValid)
                {
                    if (model.IsSameDefoliationForAll.HasValue && model.IsSameDefoliationForAll.Value)
                    {
                        //List<List<string>> allDefoliations = new List<List<string>>();
                        List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();
                        foreach (var organic in model.OrganicManures)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(organic.FieldID));

                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                            }

                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                            {
                                var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                                (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                                if (ManagementPeriod != null)
                                {
                                    List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();
                                    List<SelectListItem> defoliationSelectList = new List<SelectListItem>();
                                    //var defoliationIdsList = ManagementPeriod.Select(x => x.Defoliation.Value.ToString()).ToList();

                                    (Crop crop, error) = await _cropService.FetchCropById(cropId);
                                    if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                    {
                                        (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                        if (error == null && defoliationSequence != null)
                                        {
                                            string description = defoliationSequence.DefoliationSequenceDescription;
                                            string[] defoliationParts = description.Split(',')
                                                                                    .Select(x => x.Trim())
                                                                                    .ToArray();
                                            List<SelectListItem> allDefoliationWithName = new List<SelectListItem>();
                                            foreach (int defoliation in defoliationList)
                                            {
                                                string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                                ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                                : defoliation.ToString();

                                                allDefoliationWithName.Add(new SelectListItem
                                                {
                                                    Text = text,
                                                    Value = defoliation.ToString()
                                                });

                                            }
                                            allDefoliations.Add(allDefoliationWithName);
                                        }
                                    }


                                }

                            }
                        }
                        if (allDefoliations.Count > 0)
                        {
                            List<List<string>> defoliationValues = allDefoliations
                        .Select(list => list.Select(item => item.Text).ToList())
                        .ToList();

                            if (defoliationValues.Count > 0)
                            {
                                List<string> commonDefoliations = defoliationValues.Count > 0
                                ? defoliationValues.Aggregate((prev, next) => prev.Intersect(next).ToList())
                                : new List<string>();

                                if (commonDefoliations.Count > 0)
                                {
                                    List<SelectListItem> flattenedList = allDefoliations
                                .SelectMany(list => list)
                                .ToList();

                                    if (flattenedList.Count > 0)
                                    {
                                        List<SelectListItem> commonDefoliationItems = flattenedList
                                        .Where(item => commonDefoliations.Contains(item.Text))
                                        .GroupBy(item => item.Text)
                                        .Select(g => g.First())
                                        .ToList();

                                        foreach (var item in commonDefoliationItems)
                                        {
                                            var parts = item.Text.Split('-');
                                            if (parts.Length == 2)
                                            {
                                                var left = parts[0].Trim();
                                                var right = parts[1].Trim();

                                                if (!string.IsNullOrWhiteSpace(right))
                                                {
                                                    right = char.ToUpper(right[0]) + right.Substring(1);
                                                }

                                                item.Text = $"{left} - {right}";
                                            }
                                        }
                                        //ViewBag.DefoliationList = commonDefoliationItems;
                                        ViewBag.DefoliationList = commonDefoliationItems.Select(f => new SelectListItem
                                        {
                                            Value = f.Value,
                                            Text = f.Text.ToString()
                                        }).ToList();
                                    }
                                }
                            }
                        }

                    }
                    else
                    {
                        if (model.DefoliationCurrentCounter >= 0)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.OrganicManures[model.DefoliationCurrentCounter].FieldID));

                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                            }

                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                            {
                                var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                                (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                                if (ManagementPeriod != null)
                                {
                                    List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();

                                    List<SelectListItem> defoliationSelectList = new List<SelectListItem>();

                                    (Crop crop, error) = await _cropService.FetchCropById(cropId);
                                    if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                    {
                                        (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                        if (error == null && defoliationSequence != null)
                                        {
                                            string description = defoliationSequence.DefoliationSequenceDescription;
                                            string[] defoliationParts = description.Split(',')
                                                                                    .Select(x => x.Trim())
                                                                                    .ToArray();

                                            foreach (int defoliation in defoliationList)
                                            {
                                                string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                                ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                                : defoliation.ToString();

                                                defoliationSelectList.Add(new SelectListItem
                                                {
                                                    Text = text,
                                                    Value = defoliation.ToString()
                                                });
                                                foreach (var item in defoliationSelectList)
                                                {
                                                    var parts = item.Text.Split('-');
                                                    if (parts.Length == 2)
                                                    {
                                                        var left = parts[0].Trim();
                                                        var right = parts[1].Trim();

                                                        if (!string.IsNullOrWhiteSpace(right))
                                                        {
                                                            right = char.ToUpper(right[0]) + right.Substring(1);
                                                        }

                                                        item.Text = $"{left} - {right}";
                                                    }
                                                }

                                            }
                                        }
                                    }

                                    //ViewBag.DefoliationList = defoliationSelectList;
                                    ViewBag.DefoliationList = defoliationSelectList.Select(f => new SelectListItem
                                    {
                                        Value = f.Value,
                                        Text = f.Text.ToString()
                                    }).ToList();

                                }
                            }
                        }

                    }
                    return View(model);
                }
                if (model.IsSameDefoliationForAll.HasValue && (!model.IsSameDefoliationForAll.Value))
                {

                    for (int i = 0; i < model.OrganicManures.Count; i++)
                    {
                        if (model.FieldID == model.OrganicManures[i].FieldID.Value)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(model.OrganicManures[model.DefoliationCurrentCounter].ManagementPeriodID);
                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                {
                                    (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                    if (error == null && defoliationSequence != null)
                                    {
                                        string description = defoliationSequence.DefoliationSequenceDescription;

                                        string[] defoliationParts = description.Split(',')
                                                                               .Select(x => x.Trim())
                                                                               .ToArray();

                                        string selectedDefoliation = (model.OrganicManures[model.DefoliationCurrentCounter].Defoliation.Value > 0 && model.OrganicManures[model.DefoliationCurrentCounter].Defoliation.Value <= defoliationParts.Length)
                                            ? $"{Enum.GetName(typeof(PotentialCut), model.OrganicManures[model.DefoliationCurrentCounter].Defoliation.Value)} -{defoliationParts[model.OrganicManures[model.DefoliationCurrentCounter].Defoliation.Value - 1]}"
                                            : $"{model.OrganicManures[model.DefoliationCurrentCounter].Defoliation.Value}";
                                        var parts = selectedDefoliation.Split('-');
                                        if (parts.Length == 2)
                                        {
                                            var left = parts[0].Trim();
                                            var right = parts[1].Trim();

                                            if (!string.IsNullOrWhiteSpace(right))
                                            {
                                                right = char.ToUpper(right[0]) + right.Substring(1);
                                            }

                                            selectedDefoliation = $"{left} - {right}";
                                        }
                                        model.OrganicManures[model.DefoliationCurrentCounter].DefoliationName = selectedDefoliation;
                                    }
                                }
                            }
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.OrganicManures[model.DefoliationCurrentCounter].FieldID));

                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                            }
                            int? managementPeriodID = null;
                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                            {
                                var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                                (List<ManagementPeriod> managementPeriodList, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                                if (managementPeriodList != null)
                                {
                                    managementPeriodID = managementPeriodList.Where(x => x.Defoliation == model.OrganicManures[model.DefoliationCurrentCounter].Defoliation).Select(x => x.ID.Value).FirstOrDefault();
                                    if (model.IsCheckAnswer && (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId)))
                                    {
                                        int filteredManId = managementPeriodList
                                     .Where(fm => model.UpdatedOrganicIds.Any(mp => mp.ManagementPeriodId == fm.ID))
                                     .Select(x => x.ID.Value)
                                     .FirstOrDefault();

                                        if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                        {
                                            foreach (var item in model.UpdatedOrganicIds)
                                            {
                                                if (item.ManagementPeriodId == filteredManId)
                                                {
                                                    item.ManagementPeriodId = managementPeriodID;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    model.OrganicManures[i].ManagementPeriodID = managementPeriodID.Value;
                                }
                            }

                            model.DefoliationCurrentCounter++;

                            if (i + 1 < model.OrganicManures.Count)
                            {
                                model.FieldID = model.OrganicManures[i + 1].FieldID.Value;
                                model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                            }

                            break;
                        }
                    }
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {
                        var itemsToRemove = new List<OrganicManure>();
                        foreach (var organicManure in model.OrganicManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                {
                                    if (crop.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                    {
                                        itemsToRemove.Add(organicManure);
                                    }
                                }
                            }
                        }

                        foreach (var item in itemsToRemove)
                        {
                            model.OrganicManures.Remove(item);
                        }
                    }
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInSameDefoliationFlag) && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                }
                else if (model.IsSameDefoliationForAll.HasValue && (model.IsSameDefoliationForAll.Value))
                {
                    model.DefoliationCurrentCounter = 1;
                    for (int i = 0; i < model.OrganicManures.Count; i++)
                    {
                        model.OrganicManures[i].Defoliation = model.OrganicManures[0].Defoliation;
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.OrganicManures[i].FieldID));

                        if (cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                        }
                        int? managementPeriodID = null;
                        if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                        {
                            var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                            (List<ManagementPeriod> managementPeriodList, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                            if (managementPeriodList != null)
                            {
                                managementPeriodID = managementPeriodList.Where(x => x.Defoliation == model.OrganicManures[i].Defoliation).Select(x => x.ID.Value).FirstOrDefault();
                                if (model.IsCheckAnswer && (!string.IsNullOrWhiteSpace(model.EncryptedOrgManureId)))
                                {
                                    int filteredManId = managementPeriodList
                                 .Where(fm => model.UpdatedOrganicIds.Any(mp => mp.ManagementPeriodId == fm.ID))
                                 .Select(x => x.ID.Value)
                                 .FirstOrDefault();

                                    if (model.UpdatedOrganicIds != null && model.UpdatedOrganicIds.Count > 0)
                                    {
                                        foreach (var item in model.UpdatedOrganicIds)
                                        {
                                            if (item.ManagementPeriodId == filteredManId)
                                            {
                                                item.ManagementPeriodId = managementPeriodID;
                                                break;
                                            }
                                        }
                                    }
                                }
                                model.OrganicManures[i].ManagementPeriodID = managementPeriodID.Value;
                            }
                        }



                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(model.OrganicManures[i].ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                            {
                                (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                if (error == null && defoliationSequence != null)
                                {
                                    string description = defoliationSequence.DefoliationSequenceDescription;

                                    string[] defoliationParts = description.Split(',')
                                                                           .Select(x => x.Trim())
                                                                           .ToArray();
                                    string selectedDefoliation = (model.OrganicManures[i].Defoliation.Value > 0 && model.OrganicManures[i].Defoliation.Value <= defoliationParts.Length)
                                 ? $"{Enum.GetName(typeof(PotentialCut), model.OrganicManures[i].Defoliation.Value)} - {defoliationParts[model.OrganicManures[i].Defoliation.Value - 1]}"
                                 : $"{model.OrganicManures[i].Defoliation.Value}";
                                    var parts = selectedDefoliation.Split('-');
                                    if (parts.Length == 2)
                                    {
                                        var left = parts[0].Trim();
                                        var right = parts[1].Trim();

                                        if (!string.IsNullOrWhiteSpace(right))
                                        {
                                            right = char.ToUpper(right[0]) + right.Substring(1);
                                        }

                                        selectedDefoliation = $"{left} - {right}";
                                    }
                                    model.OrganicManures[i].DefoliationName = selectedDefoliation;
                                }
                            }
                        }

                    }
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {
                        var itemsToRemove = new List<OrganicManure>();
                        foreach (var organicManure in model.OrganicManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                {
                                    if (crop.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                    {
                                        itemsToRemove.Add(organicManure);
                                    }
                                }
                            }
                        }

                        foreach (var item in itemsToRemove)
                        {
                            model.OrganicManures.Remove(item);
                        }
                    }
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                    return RedirectToAction("ManureApplyingDate");
                }

                if (model.DefoliationCurrentCounter == model.OrganicManures.Count)
                {
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                    return RedirectToAction("ManureApplyingDate");
                }
                else
                {
                    if (model.IsSameDefoliationForAll.HasValue && model.IsSameDefoliationForAll.Value)
                    {
                        List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();

                        foreach (var organic in model.OrganicManures)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(organic.FieldID));

                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                            }

                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                            {
                                var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                                (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                                if (ManagementPeriod != null)
                                {
                                    List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();
                                    List<SelectListItem> defoliationSelectList = new List<SelectListItem>();
                                    //var defoliationIdsList = ManagementPeriod.Select(x => x.Defoliation.Value.ToString()).ToList();

                                    (Crop crop, error) = await _cropService.FetchCropById(cropId);
                                    if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                    {
                                        (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                        if (error == null && defoliationSequence != null)
                                        {
                                            string description = defoliationSequence.DefoliationSequenceDescription;
                                            string[] defoliationParts = description.Split(',')
                                                                                    .Select(x => x.Trim())
                                                                                    .ToArray();
                                            List<SelectListItem> allDefoliationWithName = new List<SelectListItem>();
                                            foreach (int defoliation in defoliationList)
                                            {
                                                string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                                ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                                : defoliation.ToString();

                                                allDefoliationWithName.Add(new SelectListItem
                                                {
                                                    Text = text,
                                                    Value = defoliation.ToString()
                                                });

                                            }
                                            allDefoliations.Add(allDefoliationWithName);
                                        }
                                    }


                                }
                            }
                        }
                        if (allDefoliations.Count > 0)
                        {
                            List<List<string>> defoliationValues = allDefoliations
                        .Select(list => list.Select(item => item.Text).ToList())
                        .ToList();

                            if (defoliationValues.Count > 0)
                            {
                                List<string> commonDefoliations = defoliationValues.Count > 0
                                ? defoliationValues.Aggregate((prev, next) => prev.Intersect(next).ToList())
                                : new List<string>();

                                if (commonDefoliations.Count > 0)
                                {
                                    List<SelectListItem> flattenedList = allDefoliations
                                .SelectMany(list => list)
                                .ToList();

                                    if (flattenedList.Count > 0)
                                    {
                                        List<SelectListItem> commonDefoliationItems = flattenedList
                                        .Where(item => commonDefoliations.Contains(item.Text))
                                        .GroupBy(item => item.Text)
                                        .Select(g => g.First())
                                        .ToList();

                                        foreach (var item in commonDefoliationItems)
                                        {
                                            var parts = item.Text.Split('-');
                                            if (parts.Length == 2)
                                            {
                                                var left = parts[0].Trim();
                                                var right = parts[1].Trim();

                                                if (!string.IsNullOrWhiteSpace(right))
                                                {
                                                    right = char.ToUpper(right[0]) + right.Substring(1);
                                                }

                                                item.Text = $"{left} - {right}";
                                            }
                                        }
                                        //ViewBag.DefoliationList = commonDefoliationItems;
                                        ViewBag.DefoliationList = commonDefoliationItems.Select(f => new SelectListItem
                                        {
                                            Value = f.Value,
                                            Text = f.Text.ToString()
                                        }).ToList();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (model.DefoliationCurrentCounter >= 0)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.OrganicManures[model.DefoliationCurrentCounter].FieldID));

                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                            }

                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                            {
                                var cropId = cropList.Select(x => x.ID.Value).FirstOrDefault();
                                (List<ManagementPeriod> ManagementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropId, false);

                                if (ManagementPeriod != null)
                                {
                                    // ViewBag.DefoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();
                                    List<int> defoliationList = ManagementPeriod.Select(x => x.Defoliation.Value).ToList();

                                    List<SelectListItem> defoliationSelectList = new List<SelectListItem>();

                                    (Crop crop, error) = await _cropService.FetchCropById(cropId);
                                    if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                    {
                                        (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                        if (error == null && defoliationSequence != null)
                                        {
                                            string description = defoliationSequence.DefoliationSequenceDescription;
                                            string[] defoliationParts = description.Split(',')
                                                                                    .Select(x => x.Trim())
                                                                                    .ToArray();

                                            foreach (int defoliation in defoliationList)
                                            {
                                                string text = (defoliation > 0 && defoliation <= defoliationParts.Length)
                                                ? $"{Enum.GetName(typeof(PotentialCut), defoliation)} - {defoliationParts[defoliation - 1]}"
                                                : defoliation.ToString();

                                                var parts = text.Split('-');
                                                if (parts.Length == 2)
                                                {
                                                    var left = parts[0].Trim();
                                                    var right = parts[1].Trim();

                                                    if (!string.IsNullOrWhiteSpace(right))
                                                    {
                                                        right = char.ToUpper(right[0]) + right.Substring(1);
                                                    }

                                                    text = $"{left} - {right}";
                                                }
                                                defoliationSelectList.Add(new SelectListItem
                                                {
                                                    Text = text,
                                                    Value = defoliation.ToString()
                                                });

                                            }
                                        }
                                    }

                                    //ViewBag.DefoliationList = defoliationSelectList;
                                    ViewBag.DefoliationList = defoliationSelectList.Select(f => new SelectListItem
                                    {
                                        Value = f.Value,
                                        Text = f.Text.ToString()
                                    }).ToList();
                                }
                            }
                        }

                    }
                    return View(model);
                }
                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("OrganicManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in Defoliation() post action : {ex.Message}, {ex.StackTrace}");
                TempData["DefoliationError"] = ex.Message;
                return View(model);
            }
            //return RedirectToAction("ManureApplyingDate");

        }

        [HttpGet]
        public IActionResult backFromManureApplyingDate()
        {
            _logger.LogTrace($"OrganicManure Controller : backFromManureApplyingDate() action called");
            OrganicManureViewModel? model = new OrganicManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("OrganicManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<OrganicManureViewModel>("OrganicManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
            {
                return RedirectToAction("Defoliation", new { q = model.DefoliationEncryptedCounter });
            }
            if (model.IsDoubleCropAvailable)
            {
                return RedirectToAction("DoubleCrop", new { q = model.DoubleCropEncryptedCounter });
            }
            if (model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureGroupIdForFilter == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
            {
                return RedirectToAction("ManureGroup");
            }
            if (model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherSolidMaterials || model.ManureTypeId == (int)NMP.Portal.Enums.ManureTypes.OtherLiquidMaterials)
            {
                return RedirectToAction("OtherMaterialName");
            }
            else
            {
                return RedirectToAction("ManureType");
            }
            if (model.FieldGroup == Resource.lblSelectSpecificFields && model.IsComingFromRecommendation)
            {
                if (model.FieldList.Count > 0 && model.FieldList.Count == 1)
                {
                    string fieldId = model.FieldList[0];
                    return RedirectToAction("Recommendations", "Crop", new
                    {
                        q = model.EncryptedFarmId,
                        r = _fieldDataProtector.Protect(fieldId),
                        s = model.EncryptedHarvestYear

                    });
                }
            }
            else if (model.FieldGroup == Resource.lblSelectSpecificFields && (!model.IsComingFromRecommendation))
            {
                return RedirectToAction("ManureType");
            }
            return RedirectToAction("FieldGroup", new
            {
                q = model.EncryptedFarmId,
                r = model.EncryptedHarvestYear
            });
        }

        private async Task<List<WarningMessage>> GetWarningMessages(OrganicManureViewModel model)
        {
            List<WarningMessage> warningMessages = new List<WarningMessage>();
            try
            {
                if (model != null && model.OrganicManures != null && model.OrganicManures.Count > 0)
                {
                    foreach (var organicManure in model.OrganicManures)
                    {
                        (ManagementPeriod managementPeriod, Error error) = await _cropService.FetchManagementperiodById(organicManure.ManagementPeriodID);


                        if (model.IsOrgManureNfieldLimitWarning || model.IsNMaxLimitWarning || model.IsClosedPeriodWarning || model.IsEndClosedPeriodFebruaryWarning || model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks || model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150)
                        {

                            if (model.IsOrgManureNfieldLimitWarning)
                            {
                                WarningMessage warningMessage = new WarningMessage();

                                warningMessage.FieldID = organicManure.FieldID ?? 0;
                                warningMessage.CropID = managementPeriod.CropID ?? 0;
                                warningMessage.JoiningID = null;
                                warningMessage.WarningLevelID = model.NmaxWarningLevelID;
                                warningMessage.WarningCodeID = model.NmaxWarningCodeID;

                                warningMessage.Header = model.NmaxWarningHeader;
                                warningMessage.Para1 = model.NmaxWarningHeading;
                                warningMessage.Para2 = model.NmaxWarningPara1;
                                warningMessage.Para3 = model.NmaxWarningPara2;

                                warningMessages.Add(warningMessage);

                            }
                            if (model.IsNMaxLimitWarning)
                            {
                                WarningMessage warningMessage = new WarningMessage();

                                warningMessage.FieldID = organicManure.FieldID ?? 0;
                                warningMessage.CropID = managementPeriod.CropID ?? 0;
                                warningMessage.JoiningID = null;
                                warningMessage.WarningLevelID = model.CropNmaxLimitWarningLevelID;
                                warningMessage.WarningCodeID = model.CropNmaxLimitWarningCodeID;

                                warningMessage.Header = model.CropNmaxLimitWarningHeader;
                                warningMessage.Para1 = model.CropNmaxLimitWarningHeading;
                                warningMessage.Para2 = model.CropNmaxLimitWarningPara1;
                                warningMessage.Para3 = model.CropNmaxLimitWarningPara2;

                                warningMessages.Add(warningMessage);
                            }

                            if (model.IsClosedPeriodWarning)
                            {
                                WarningMessage warningMessage = new WarningMessage();

                                warningMessage.FieldID = organicManure.FieldID ?? 0;
                                warningMessage.CropID = managementPeriod.CropID ?? 0;
                                warningMessage.JoiningID = null;
                                warningMessage.WarningLevelID = model.ClosedPeriodWarningLevelID;
                                warningMessage.WarningCodeID = model.ClosedPeriodWarningCodeID;

                                warningMessage.Header = model.ClosedPeriodWarningHeader;
                                warningMessage.Para1 = model.ClosedPeriodWarningHeading;
                                warningMessage.Para2 = model.ClosedPeriodWarningPara1;
                                warningMessage.Para3 = model.ClosedPeriodWarningPara2;

                                warningMessages.Add(warningMessage);
                            }
                            if (model.IsEndClosedPeriodFebruaryWarning)
                            {
                                WarningMessage warningMessage = new WarningMessage();

                                warningMessage.FieldID = organicManure.FieldID ?? 0;
                                warningMessage.CropID = managementPeriod.CropID ?? 0;
                                warningMessage.JoiningID = null;
                                warningMessage.WarningLevelID = model.EndClosedPeriodEndFebWarningLevelID;
                                warningMessage.WarningCodeID = model.EndClosedPeriodEndFebWarningCodeID;

                                warningMessage.Header = model.EndClosedPeriodEndFebWarningHeader;
                                warningMessage.Para1 = model.EndClosedPeriodEndFebWarningHeading;
                                warningMessage.Para2 = model.EndClosedPeriodEndFebWarningPara1;
                                warningMessage.Para3 = model.EndClosedPeriodEndFebWarningPara2;

                                warningMessages.Add(warningMessage);
                            }
                            if (model.IsEndClosedPeriodFebruaryExistWithinThreeWeeks)
                            {
                                WarningMessage warningMessage = new WarningMessage();

                                warningMessage.FieldID = organicManure.FieldID ?? 0;
                                warningMessage.CropID = managementPeriod.CropID ?? 0;
                                warningMessage.JoiningID = null;
                                warningMessage.WarningLevelID = model.EndClosedPeriodFebruaryExistWithinThreeWeeksLevelID;
                                warningMessage.WarningCodeID = model.EndClosedPeriodFebruaryExistWithinThreeWeeksCodeID;

                                warningMessage.Header = model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeader;
                                warningMessage.Para1 = model.EndClosedPeriodFebruaryExistWithinThreeWeeksHeading;
                                warningMessage.Para2 = model.EndClosedPeriodFebruaryExistWithinThreeWeeksPara1;
                                warningMessage.Para3 = model.EndClosedPeriodFebruaryExistWithinThreeWeeksPara2;

                                warningMessages.Add(warningMessage);
                            }
                            if (model.IsStartPeriodEndFebOrganicAppRateExceedMaxN150)
                            {
                                WarningMessage warningMessage = new WarningMessage();

                                warningMessage.FieldID = organicManure.FieldID ?? 0;
                                warningMessage.CropID = managementPeriod.CropID ?? 0;
                                warningMessage.JoiningID = null;
                                warningMessage.WarningLevelID = model.StartClosedPeriodEndFebWarningLevelID;
                                warningMessage.WarningCodeID = model.StartClosedPeriodEndFebWarningCodeID;

                                warningMessage.Header = model.StartClosedPeriodEndFebWarningHeader;
                                warningMessage.Para1 = model.StartClosedPeriodEndFebWarningHeading;
                                warningMessage.Para2 = model.StartClosedPeriodEndFebWarningPara1;
                                warningMessage.Para3 = model.StartClosedPeriodEndFebWarningPara2;

                                warningMessages.Add(warningMessage);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in GetWarningMessages() method : {ex.Message}, {ex.StackTrace}");
            }
            return warningMessages;
        }
    }
}