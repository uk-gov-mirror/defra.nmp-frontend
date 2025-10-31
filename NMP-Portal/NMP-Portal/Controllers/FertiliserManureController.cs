using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
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
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
using static System.Net.Mime.MediaTypeNames;

namespace NMP.Portal.Controllers
{
    [Authorize]
    public class FertiliserManureController : Controller
    {
        private readonly ILogger<FertiliserManureController> _logger;
        private readonly IDataProtector _fertiliserManureProtector;
        private readonly IDataProtector _farmDataProtector;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IDataProtector _cropDataProtector;
        private readonly IFarmService _farmService;
        private readonly IFertiliserManureService _fertiliserManureService;
        private readonly ICropService _cropService;
        private readonly IFieldService _fieldService;
        private readonly IOrganicManureService _organicManureService;
        private readonly IDataProtector _fieldDataProtector;

        public FertiliserManureController(ILogger<FertiliserManureController> logger, IDataProtectionProvider dataProtectionProvider,
            IHttpContextAccessor httpContextAccessor, IFarmService farmService, IFertiliserManureService fertiliserManureService, ICropService cropService, IFieldService fieldService, IOrganicManureService organicManureService)
        {
            _logger = logger;
            _fertiliserManureProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FertiliserManureController");
            _httpContextAccessor = httpContextAccessor;
            _farmDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FarmController");
            _cropDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.CropController");
            _farmService = farmService;
            _fertiliserManureService = fertiliserManureService;
            _cropService = cropService;
            _fieldService = fieldService;
            _organicManureService = organicManureService;
            _fieldDataProtector = dataProtectionProvider.CreateProtector("NMP.Portal.Controllers.FieldController");
        }

        public IActionResult Index()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : Index() action called");
            return View();
        }

        public IActionResult CreateFertiliserManureCancel(string q, string r)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : CreateFertiliserManureCancel({q}, {r}) action called");
            _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
            return RedirectToAction("HarvestYearOverview", "Crop", new { Id = q, year = r });
        }

        [HttpGet]
        public IActionResult backActionForInOrganicManure()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : backActionForInOrganicManure() action called");
            FertiliserManureViewModel? model = new FertiliserManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
            {
                return RedirectToAction("CheckAnswer");
            }
            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
            {
                return RedirectToAction("Defoliation", new { q = model.DefoliationEncryptedCounter });
            }
            if (model.IsDoubleCropAvailable)
            {
                return RedirectToAction("DoubleCrop", new { q = model.DoubleCropEncryptedCounter });
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
                return RedirectToAction("Fields");
            }
            return RedirectToAction("FieldGroup", new
            {
                q = model.EncryptedFarmId,
                r = model.EncryptedHarvestYear
            });



        }
        [HttpGet]
        public async Task<IActionResult> FieldGroup(string q, string r, string? s)//q=FarmId,r=harvestYear,s=fieldId
        {
            _logger.LogTrace($"Fertiliser Manure Controller : FieldGroup({q}, {r}, {s}) action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
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
                    model.CropOrder = 1;
                    (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                    if (error.Message == null)
                    {
                        model.FarmName = farm.Name;
                        model.isEnglishRules = farm.EnglishRules;
                        model.FarmCountryId = farm.CountryID;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                    }
                    else
                    {
                        TempData["ErrorOnHarvestYearOverview"] = error.Message;
                        if (TempData["FieldGroupError"] != null)
                        {
                            TempData["FieldGroupError"] = null;
                        }
                        if (TempData["FieldError"] != null)
                        {
                            TempData["FieldError"] = null;
                        }
                        return RedirectToAction("HarvestYearOverview", "Crop", new { id = model.EncryptedFarmId, year = model.EncryptedHarvestYear });
                    }
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        model.IsAnyCropIsGrass = false;
                        model.FieldList = new List<string>();
                        model.FieldGroup = Resource.lblSelectSpecificFields;
                        model.FieldGroupName = Resource.lblSelectSpecificFields;
                        model.IsComingFromRecommendation = true;
                        string fieldId = _fieldDataProtector.Unprotect(s);
                        model.FieldList.Add(fieldId);
                        List<string> fieldsToRemove = new List<string>();
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                        if (cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                        }
                        if (cropList != null && cropList.Count == 2)
                        {
                            model.IsDoubleCropAvailable = true;
                            int counter = 0;
                            model.DoubleCropCurrentCounter = counter;
                            model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name;
                            model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                        }
                        else
                        {
                            model.IsDoubleCropAvailable = false;
                        }
                        if (!model.IsDoubleCropAvailable)
                        {
                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass || (x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.IsBasePlan)))
                            {
                                fieldsToRemove.Add(fieldId);
                            }
                            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                            {
                                model.IsAnyCropIsGrass = true;
                            }
                        }
                        List<string> fieldListCopy = new List<string>(model.FieldList);

                        if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                        {
                            foreach (var field in fieldsToRemove)
                            {
                                fieldListCopy.Remove(field);
                            }
                        }
                        string fieldIds = string.Join(",", fieldListCopy);
                        (List<int> managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);// 1 id cropOrder
                        if (error == null)
                        {
                            if (managementIds.Count > 0)
                            {
                                if (model.FertiliserManures == null)
                                {
                                    model.FertiliserManures = new List<FertiliserManure>();
                                }
                                if (model.FertiliserManures.Count > 0)
                                {
                                    model.FertiliserManures.Clear();
                                }
                                int counter = 1;
                                foreach (var manIds in managementIds)
                                {
                                    var fertiliserManure = new FertiliserManure
                                    {
                                        ManagementPeriodID = manIds,
                                        EncryptedCounter = _fieldDataProtector.Protect(counter.ToString()),
                                        FieldID = Convert.ToInt32(fieldId),
                                        FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name
                                    };
                                    counter++;
                                    model.FertiliserManures.Add(fertiliserManure);
                                }
                                model.DefoliationCurrentCounter = 0;
                            }
                        }
                        else
                        {
                            TempData["NutrientRecommendationsError"] = error.Message;
                            if (TempData["FieldGroupError"] != null)
                            {
                                TempData["FieldGroupError"] = null;
                            }
                            if (TempData["FieldError"] != null)
                            {
                                TempData["FieldError"] = null;
                            }
                            return RedirectToAction("Recommendations", "Crop", new { q = q, r = s, s = r });
                        }
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
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
                        if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                        {
                            int grassCropCounter = 0;
                            foreach (var field in model.FieldList)
                            {
                                cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                                if (cropList.Count > 0)
                                {
                                    cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == model.CropOrder).ToList();
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
                                            grassCropCounter++;
                                            model.FertiliserManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));

                                        }
                                        model.IsAnyCropIsGrass = true;
                                    }
                                }
                            }
                            model.GrassCropCount = grassCropCounter;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);

                            model.IsSameDefoliationForAll = true;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);

                        }
                        if (model.IsDoubleCropAvailable)
                        {
                            return RedirectToAction("DoubleCrop");
                        }
                        if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                        {
                            return RedirectToAction("Defoliation");
                        }
                        else
                        {
                            model.GrassCropCount = null;
                            model.IsSameDefoliationForAll = null;
                            model.IsAnyChangeInSameDefoliationFlag = false;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        }

                        return RedirectToAction("InOrgnaicManureDuration");
                    }


                }
                (List<ManureCropTypeResponse> cropTypeList, error) = await _fertiliserManureService.FetchCropTypeByFarmIdAndHarvestYear(model.FarmId.Value, model.HarvestYear.Value);
                cropTypeList = cropTypeList.DistinctBy(x => x.CropTypeId).ToList();
                if (error == null && cropTypeList.Count > 0)
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
                    TempData["ErrorOnHarvestYearOverview"] = error.Message;
                    if (TempData["FieldGroupError"] != null)
                    {
                        TempData["FieldGroupError"] = null;
                    }
                    if (TempData["FieldError"] != null)
                    {
                        TempData["FieldError"] = null;
                    }
                    return RedirectToAction("HarvestYearOverview", "Crop", new { id = model.EncryptedFarmId, year = model.EncryptedHarvestYear });
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Farm Controller : Exception in FieldGroup() action : {ex.Message}, {ex.StackTrace}");
                TempData["ErrorOnHarvestYearOverview"] = ex.Message;
                if (TempData["FieldGroupError"] != null)
                {
                    TempData["FieldGroupError"] = null;
                }
                if (TempData["FieldError"] != null)
                {
                    TempData["FieldError"] = null;
                }
                return RedirectToAction("HarvestYearOverview", "Crop", new { id = model.EncryptedFarmId, year = model.EncryptedHarvestYear });
            }

            //if (model.IsCheckAnswer && (string.IsNullOrWhiteSpace(s)))
            //{
            //    model.IsFieldGroupChange = true;
            //}
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            return View("Views/FertiliserManure/FieldGroup.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FieldGroup(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : FieldGroup() post action called");
            Error error = null;
            if (model.FieldGroup == null)
            {
                ModelState.AddModelError("FieldGroup", Resource.MsgSelectAnOptionBeforeContinuing);
            }
            try
            {
                (List<ManureCropTypeResponse> cropTypeList, error) = await _fertiliserManureService.FetchCropTypeByFarmIdAndHarvestYear(model.FarmId.Value, model.HarvestYear.Value);
                if (!ModelState.IsValid)
                {
                    if (error == null && cropTypeList.Count > 0)
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
                    return View("Views/FertiliserManure/FieldGroup.cshtml", model);
                }
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
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Farm Controller : Exception in FieldGroup() post action : {ex.Message}, {ex.StackTrace}");
                TempData["FieldGroupError"] = ex.Message;
                return View("Views/FertiliserManure/FieldGroup.cshtml", model);
            }
            return RedirectToAction("Fields");


        }
        [HttpGet]
        public async Task<IActionResult> Fields()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : Fields() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }
                model.CropOrder = 1;
                if (string.IsNullOrWhiteSpace(model.EncryptedFertId))
                {
                    (List<CommonResponse> fieldList, error) = await _fertiliserManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
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
                            FertiliserManureViewModel fertiliserManureViewModel = null;
                            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                            {
                                fertiliserManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                            }
                            else
                            {
                                return RedirectToAction("FarmList", "Farm");
                            }
                            if (fieldList.Count > 0)
                            {
                                model.IsAnyCropIsGrass = false;
                                model.FieldList = fieldList.Select(x => x.Id.ToString()).ToList();
                                List<string> fieldsToRemove = new List<string>();
                                model.IsDoubleCropAvailable = false;
                                foreach (string field in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));

                                    if (cropList.Count > 0)
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();
                                            if (cropList != null && cropList.Count == 2)
                                            {
                                                model.IsDoubleCropAvailable = true;
                                                int counter = 0;
                                                model.DoubleCropCurrentCounter = counter;
                                                model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                                                model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                            }
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
                                            //else
                                            //{
                                            //    model.IsDoubleCropAvailable = false;
                                            //}
                                            //   cropList = cropList.Where(x => x.CropOrder == 1).ToList();
                                        }

                                    }
                                    //if (!model.IsDoubleCropAvailable)
                                    //{

                                    //}
                                }
                                List<string> fieldListCopy = new List<string>(model.FieldList);

                                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                                {
                                    foreach (var field in fieldsToRemove)
                                    {
                                        fieldListCopy.Remove(field);
                                    }
                                }
                                string fieldIds = string.Join(",", fieldListCopy);
                                List<int> managementIds = new List<int>();
                                if (model.IsAnyCropIsGrass.Value)
                                {
                                    if (int.TryParse(model.FieldGroup, out int value))
                                    {
                                        (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, null);//1 is CropOrder
                                    }
                                    else
                                    {
                                        (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);
                                    }
                                    if (error == null)
                                    {
                                        if (managementIds.Count > 0)
                                        {
                                            if (model.FertiliserManures == null)
                                            {
                                                model.FertiliserManures = new List<FertiliserManure>();
                                            }
                                            if (model.FertiliserManures.Count > 0)
                                            {
                                                model.FertiliserManures.Clear();
                                            }
                                            int counter = 1;
                                            foreach (var manIds in managementIds)
                                            {
                                                var fertiliserManure = new FertiliserManure
                                                {
                                                    ManagementPeriodID = manIds,
                                                    EncryptedCounter = _fieldDataProtector.Protect(counter.ToString())
                                                };
                                                counter++;
                                                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                                                {
                                                    if (fertiliserManureViewModel.FertiliserManures != null && fertiliserManureViewModel.FertiliserManures.Count > 0)
                                                    {
                                                        for (int i = 0; i < fertiliserManureViewModel.FertiliserManures.Count; i++)
                                                        {
                                                            if (fertiliserManureViewModel.FertiliserManures[i].ManagementPeriodID == manIds)
                                                            {
                                                                fertiliserManure.Defoliation = fertiliserManureViewModel.FertiliserManures[i].Defoliation;
                                                                if (fertiliserManure.Defoliation != null)
                                                                {
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

                                                                                string selectedDefoliation = (fertiliserManure.Defoliation.Value > 0 && fertiliserManure.Defoliation.Value <= defoliationParts.Length)
                                                                                    ? $"{Enum.GetName(typeof(PotentialCut), fertiliserManure.Defoliation.Value)} ({defoliationParts[fertiliserManure.Defoliation.Value - 1]})"
                                                                                    : $"{fertiliserManure.Defoliation.Value}";

                                                                                fertiliserManure.DefoliationName = selectedDefoliation;
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }

                                                }
                                                model.FertiliserManures.Add(fertiliserManure);
                                            }
                                            model.DefoliationCurrentCounter = 0;
                                        }
                                    }
                                    else
                                    {
                                        TempData["FieldGroupError"] = error.Message;
                                        if (TempData["FieldError"] != null)
                                        {
                                            TempData["FieldError"] = null;
                                        }
                                        return RedirectToAction("FieldGroup", new { q = model.EncryptedFarmId, r = model.EncryptedHarvestYear });
                                    }
                                }
                                else
                                {
                                    model.FieldList = fieldList.Select(x => x.Id.ToString()).ToList();
                                    fieldIds = string.Join(",", model.FieldList);
                                    if (model.FertiliserManures == null)
                                    {
                                        model.FertiliserManures = new List<FertiliserManure>();
                                    }
                                    if (model.FertiliserManures.Count > 0)
                                    {
                                        model.FertiliserManures.Clear();
                                    }
                                    foreach (string fieldIdForManID in model.FieldList)
                                    {
                                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldIdForManID));
                                        if (cropList != null && cropList.Count > 0)
                                        {
                                            if (int.TryParse(model.FieldGroup, out int cropTypeIdForFilter))
                                            {
                                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeIdForFilter).ToList();
                                            }
                                            else
                                            {
                                                cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                            }

                                            if (cropList.Count > 0)
                                            {
                                                model.CropOrder = Convert.ToInt32(cropList.Select(x => x.CropOrder).FirstOrDefault());
                                            }
                                        }
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, model.FieldGroup, null);
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
                                                    var fertiliser = new FertiliserManure
                                                    {
                                                        ManagementPeriodID = manIds
                                                    };
                                                    model.FertiliserManures.Add(fertiliser);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            TempData["FieldGroupError"] = error.Message;
                                            return View("FieldGroup", model);
                                        }
                                    }
                                }

                            }
                            bool anyNewManId = false;

                            //foreach (string field in model.FieldList)
                            //{
                            //    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                            //    cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                            //    if (cropList != null && cropList.Count == 2)
                            //    {
                            //        model.IsDoubleCropAvailable = true;
                            //        int counter = 0;
                            //        model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                            //        model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                            //    }
                            //    else
                            //    {
                            //        model.IsDoubleCropAvailable = false;
                            //    }
                            //    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                            //}

                            foreach (var fertiliser in model.FertiliserManures)
                            {
                                (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
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
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                            {
                                int grassCropCounter = 0;
                                foreach (var field in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                                    if (cropList.Count > 0)
                                    {
                                        if (int.TryParse(model.FieldGroup, out int cropTypeId))
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == cropTypeId).ToList();// && x.CropOrder == model.CropOrder
                                        }
                                        else
                                        {
                                            cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == 1).ToList();// && x.CropOrder == model.CropOrder
                                        }
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
                                                grassCropCounter++;
                                                model.FertiliserManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));

                                            }
                                            model.IsAnyCropIsGrass = true;
                                        }
                                    }
                                }
                                if (fertiliserManureViewModel != null && fertiliserManureViewModel.FertiliserManures != null)
                                {
                                    anyNewManId = model.FertiliserManures.Any(newId => !fertiliserManureViewModel.FertiliserManures.Contains(newId));
                                    if (anyNewManId)
                                    {
                                        model.IsAnyChangeInField = true;
                                    }
                                }
                                int counter = 1;
                                foreach (var fertiliser in model.FertiliserManures)
                                {
                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                    {
                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                        {
                                            fertiliser.FieldID = crop.FieldID;
                                            fertiliser.FieldName = (await _fieldService.FetchFieldByFieldId(fertiliser.FieldID.Value)).Name;
                                            fertiliser.EncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                            counter++;
                                        }
                                    }
                                }
                                model.GrassCropCount = grassCropCounter;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);

                                var fieldIdsAlreadyProcessed = new List<int>();
                                model.FertiliserManures.RemoveAll(item =>
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
                                if (model.IsDoubleCropAvailable)
                                {
                                    return RedirectToAction("DoubleCrop");
                                }

                                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                                {
                                    return RedirectToAction("IsSameDefoliationForAll");
                                }
                                model.IsSameDefoliationForAll = true;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                return RedirectToAction("Defoliation");

                            }
                            else
                            {
                                int counter = 1;
                                foreach (var fertiliser in model.FertiliserManures)
                                {
                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
                                    if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                                    {
                                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                        {
                                            fertiliser.FieldID = crop.FieldID;
                                            fertiliser.FieldName = (await _fieldService.FetchFieldByFieldId(fertiliser.FieldID.Value)).Name;
                                            fertiliser.EncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                            counter++;
                                        }
                                    }
                                }

                                var fieldIdsAlreadyProcessed = new List<int>();
                                model.FertiliserManures.RemoveAll(item =>
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
                                model.GrassCropCount = null;
                                model.IsSameDefoliationForAll = null;
                                model.IsAnyChangeInSameDefoliationFlag = false;
                                if (fertiliserManureViewModel != null && fertiliserManureViewModel.FertiliserManures != null)
                                {
                                    anyNewManId = model.FertiliserManures.Any(newId => !fertiliserManureViewModel.FertiliserManures.Contains(newId));
                                    if (anyNewManId)
                                    {
                                        model.IsAnyChangeInField = true;
                                    }

                                }
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                            }
                            if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
                            {
                                if (model.IsAnyCropIsGrass.HasValue && (!model.IsAnyCropIsGrass.Value))
                                {
                                    model.GrassCropCount = null;
                                    model.IsSameDefoliationForAll = null;
                                    model.IsAnyChangeInSameDefoliationFlag = false;
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                }
                                return RedirectToAction("CheckAnswer");
                            }
                            if (model.IsDoubleCropAvailable)
                            {
                                return RedirectToAction("DoubleCrop");
                            }
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);

                            return RedirectToAction("InOrgnaicManureDuration");
                        }
                    }
                    else
                    {
                        TempData["FieldGroupError"] = error.Message;
                        if (TempData["FieldError"] != null)
                        {
                            TempData["FieldError"] = null;
                        }
                        return RedirectToAction("FieldGroup", new { q = model.EncryptedFarmId, r = model.EncryptedHarvestYear });
                    }
                }
                else
                {
                    int decryptedId = Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedFertId));
                    if (decryptedId > 0 && model.FarmId != null && model.HarvestYear != null)
                    {
                        (List<FertiliserAndOrganicManureUpdateResponse> fertiliserResponse, error) = await _fertiliserManureService.FetchFieldWithSameDateAndNutrient(decryptedId, model.FarmId.Value, model.HarvestYear.Value);
                        if (string.IsNullOrWhiteSpace(error.Message) && fertiliserResponse != null && fertiliserResponse.Count > 0)
                        {
                            var SelectListItem = fertiliserResponse.Select(f => new SelectListItem
                            {
                                Value = f.Id.ToString(),
                                Text = f.Name.ToString()
                            }).ToList().DistinctBy(x => x.Value);
                            ViewBag.FieldList = SelectListItem.OrderBy(x => x.Text).ToList();

                        }
                        else
                        {
                            TempData["CheckYourAnswerError"] = error.Message;
                            return RedirectToAction("CheckAnswer");
                        }
                    }
                }
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Farm Controller : Exception in Fields() action : {ex.Message}, {ex.StackTrace}");
                if (string.IsNullOrWhiteSpace(model.EncryptedFertId))
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
        public async Task<IActionResult> Fields(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : Fields() post action called");
            Error error = null;
            try
            {
                (List<CommonResponse> fieldList, error) = await _fertiliserManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
                if (error == null)
                {
                    var selectListItem = fieldList.Select(f => new SelectListItem
                    {
                        Value = f.Id.ToString(),
                        Text = f.Name.ToString()
                    }).ToList();
                    ViewBag.FieldList = selectListItem.OrderBy(x => x.Text).ToList();
                    if (!string.IsNullOrWhiteSpace(model.EncryptedFertId))
                    {
                        (List<FertiliserAndOrganicManureUpdateResponse> fertiliserResponse, error) = await _fertiliserManureService.FetchFieldWithSameDateAndNutrient(Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedFertId)), model.FarmId.Value, model.HarvestYear.Value);
                        if (string.IsNullOrWhiteSpace(error.Message) && fertiliserResponse != null && fertiliserResponse.Count > 0)
                        {
                            selectListItem = new List<SelectListItem>();
                            selectListItem = fertiliserResponse.Select(f => new SelectListItem
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
                        return View(model);
                    }
                    if (model.FieldList.Count > 0 && model.FieldList.Contains(Resource.lblSelectAll))
                    {
                        model.FieldList = selectListItem.Where(item => item.Value != Resource.lblSelectAll).Select(item => item.Value).ToList();
                    }
                    FertiliserManureViewModel fertiliserManureViewModel = null;
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                    {
                        fertiliserManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    model.IsAnyCropIsGrass = false;
                    List<string> fieldsToRemove = new List<string>();
                    foreach (string field in model.FieldList)
                    {
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));

                        if (cropList.Count > 0)
                        {
                            cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                        }
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
                    List<string> fieldListCopy = new List<string>(model.FieldList);

                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {
                        foreach (var field in fieldsToRemove)
                        {
                            fieldListCopy.Remove(field);
                        }
                    }
                    string fieldIds = string.Join(",", fieldListCopy);
                    (List<int> managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIds, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, 1);//1 is CropOrder
                    if (error == null)
                    {
                        if (managementIds.Count > 0)
                        {
                            if (model.FertiliserManures == null)
                            {
                                model.FertiliserManures = new List<FertiliserManure>();
                            }
                            if (model.FertiliserManures.Count > 0)
                            {
                                model.FertiliserManures.Clear();
                            }
                            int counter = 1;
                            foreach (var manIds in managementIds)
                            {
                                var fertiliserManure = new FertiliserManure
                                {
                                    ManagementPeriodID = manIds,
                                    EncryptedCounter = _fieldDataProtector.Protect(counter.ToString())
                                };
                                counter++;
                                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                                {
                                    if (fertiliserManureViewModel.FertiliserManures != null && fertiliserManureViewModel.FertiliserManures.Count > 0)
                                    {
                                        for (int i = 0; i < fertiliserManureViewModel.FertiliserManures.Count; i++)
                                        {
                                            if (fertiliserManureViewModel.FertiliserManures[i].ManagementPeriodID == manIds && fertiliserManureViewModel.FertiliserManures[i].Defoliation != null)
                                            {
                                                fertiliserManure.Defoliation = fertiliserManureViewModel.FertiliserManures[i].Defoliation;
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

                                                            string selectedDefoliation = (fertiliserManure.Defoliation.Value > 0 && fertiliserManure.Defoliation.Value <= defoliationParts.Length)
                                                                ? $"{Enum.GetName(typeof(PotentialCut), fertiliserManure.Defoliation.Value)} ({defoliationParts[fertiliserManure.Defoliation.Value - 1]})"
                                                                : $"{fertiliserManure.Defoliation.Value}";

                                                            fertiliserManure.DefoliationName = selectedDefoliation;
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                    }

                                }
                                model.FertiliserManures.Add(fertiliserManure);
                            }

                            model.DefoliationCurrentCounter = 0;
                        }
                    }
                    else
                    {
                        TempData["FieldError"] = error.Message;
                        return View(model);
                    }
                    if (!model.IsAnyCropIsGrass.Value)
                    {
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
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
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        }
                    }
                    //model.IsDoubleCropAvailable = false;
                    //foreach (string field in model.FieldList)
                    //{
                    //    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                    //    cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                    //    if (cropList != null && cropList.Count == 2)
                    //    {
                    //        model.IsDoubleCropAvailable = true;
                    //        int counter = 0;
                    //        model.DoubleCropCurrentCounter = counter;
                    //        model.FieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(field))).Name;
                    //        model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                    //    }
                    //    //else
                    //    //{
                    //    //    model.IsDoubleCropAvailable = false;
                    //    //}
                    //    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                    //}


                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {

                        int grassCropCounter = 0;
                        foreach (var field in model.FieldList)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == 1).ToList();
                            }
                            if (cropList.Count > 0)
                            {
                                if (cropList.Any(c => c.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && c.DefoliationSequenceID != null))
                                {
                                    if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                                    {
                                        (List<ManagementPeriod> managementPeriod, error) = await _cropService.FetchManagementperiodByCropId(cropList.Select(x => x.ID.Value).FirstOrDefault(), false);

                                        var filteredFertiliserManure = model.FertiliserManures
                                    .Where(fm => managementPeriod.Any(mp => mp.ID == fm.ManagementPeriodID) &&
                                        fm.Defoliation == null).ToList();
                                        if (filteredFertiliserManure != null && filteredFertiliserManure.Count == managementPeriod.Count)
                                        {
                                            var managementPeriodIdsToRemove = managementPeriod
                                           .Skip(1)
                                           .Select(mp => mp.ID.Value)
                                           .ToList();
                                            model.FertiliserManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));
                                        }
                                        else
                                        {
                                            model.FertiliserManures.RemoveAll(x => managementPeriod.Any(mp => mp.ID == x.ManagementPeriodID) &&
                                            x.Defoliation == null);
                                        }
                                        grassCropCounter++;


                                    }
                                    model.IsAnyCropIsGrass = true;
                                }
                            }
                        }
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                {
                                    fertiliser.FieldID = crop.FieldID;
                                    fertiliser.FieldName = (await _fieldService.FetchFieldByFieldId(fertiliser.FieldID.Value)).Name;
                                }
                            }
                        }
                        if (fertiliserManureViewModel != null && fertiliserManureViewModel.FertiliserManures != null)
                        {
                            bool anyNewManId = model.FertiliserManures.Any(newId => !fertiliserManureViewModel.FertiliserManures.Contains(newId));
                            if (anyNewManId)
                            {
                                model.IsAnyChangeInField = true;
                            }
                        }
                        model.GrassCropCount = grassCropCounter;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                    }
                    else
                    {
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                {
                                    fertiliser.FieldID = crop.FieldID;
                                    fertiliser.FieldName = (await _fieldService.FetchFieldByFieldId(fertiliser.FieldID.Value)).Name;
                                }
                            }
                        }
                        model.GrassCropCount = null;
                        model.IsSameDefoliationForAll = null;
                        model.IsAnyChangeInSameDefoliationFlag = false;
                        if (fertiliserManureViewModel != null && fertiliserManureViewModel.FertiliserManures != null)
                        {
                            bool anyNewManId = model.FertiliserManures.Any(newId => !fertiliserManureViewModel.FertiliserManures.Contains(newId));
                            if (anyNewManId)
                            {
                                model.IsAnyChangeInField = true;
                            }
                        }
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                    }
                    if (model.IsCheckAnswer && model.IsAnyCropIsGrass.HasValue && (!model.IsAnyCropIsGrass.Value) && (!model.IsAnyChangeInField))
                    {
                        model.GrassCropCount = null;
                        model.IsSameDefoliationForAll = null;
                        model.IsAnyChangeInSameDefoliationFlag = false;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        return RedirectToAction("CheckAnswer");

                    }
                    else
                    {
                        if (model.IsDoubleCropAvailable)
                        {
                            return RedirectToAction("DoubleCrop");
                        }

                        if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                        {
                            if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                            {
                                return RedirectToAction("IsSameDefoliationForAll");
                            }
                            model.IsSameDefoliationForAll = true;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                            return RedirectToAction("Defoliation");
                        }
                    }
                }
                else
                {
                    TempData["FieldError"] = error.Message;
                    return View(model);
                }
                return RedirectToAction("InOrgnaicManureDuration");
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Farm Controller : Exception in Fields() post action : {ex.Message}, {ex.StackTrace}");
                TempData["FieldError"] = ex.Message;
                return View(model);
            }


        }

        [HttpGet]
        public async Task<IActionResult> InOrgnaicManureDuration()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : InOrgnaicManureDuration() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                (List<InOrganicManureDurationResponse> OrganicManureDurationList, error) = await _fertiliserManureService.FetchInOrganicManureDurations();
                if (error == null && OrganicManureDurationList.Count > 0)
                {
                    var SelectListItem = OrganicManureDurationList.Select(f => new SelectListItem
                    {
                        Value = f.Id.ToString(),
                        Text = f.Name.ToString()
                    }).ToList();
                    ViewBag.InOrganicManureDurationsList = SelectListItem;
                }
                //if (int.TryParse(model.FieldGroup, out int value) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count == 1))
                //{
                foreach (var fieldId in model.FieldList)
                {
                    (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                    if (error == null)
                    {
                        WarningWithinPeriod warning = new WarningWithinPeriod();
                        string closedPeriod = warning.ClosedPeriodForFertiliser(cropTypeResponse.CropTypeId);

                        //model.ClosedPeriod = closedPeriod;
                        if (!string.IsNullOrWhiteSpace(closedPeriod))
                        {
                            int harvestYear = model.HarvestYear ?? 0;
                            //int startYear = harvestYear;
                            //int endYear = harvestYear + 1;
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
                                    DateTime? closedPeriodStartDate = null;
                                    DateTime? closedPeriodEndDate = null;
                                    if (startMonth <= endMonth)
                                    {
                                        closedPeriodStartDate = new DateTime(harvestYear - 1, startMonth, startDay);
                                        closedPeriodEndDate = new DateTime(harvestYear - 1, endMonth, endDay);
                                    }
                                    else if (startMonth >= endMonth)
                                    {
                                        closedPeriodStartDate = new DateTime(harvestYear - 1, startMonth, startDay);
                                        closedPeriodEndDate = new DateTime(harvestYear, endMonth, endDay);
                                    }

                                    string formattedStartDate = closedPeriodStartDate?.ToString("d MMMM yyyy");
                                    string formattedEndDate = closedPeriodEndDate?.ToString("d MMMM yyyy");

                                    Crop crop = null;
                                    CropTypeLinkingResponse cropTypeLinkingResponse = new CropTypeLinkingResponse();
                                    if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldId)))
                                    {
                                        int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldId)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                        (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                        (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID ?? 0);
                                    }
                                    //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                                    if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                                    {
                                        ViewBag.ClosedPeriod = $"{formattedStartDate} to {formattedEndDate}";
                                    }
                                }
                            }


                        }

                    }

                    Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                    if (field != null && field.IsWithinNVZ == true)
                    {
                        model.IsWithinNVZ = true;
                    }
                }
                //}

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Farm Controller : Exception in InOrgnaicManureDuration() action : {ex.Message}, {ex.StackTrace}");
                if (model.FieldGroup.Equals(Resource.lblSelectSpecificFields))
                {
                    TempData["FieldError"] = ex.Message;
                    if (TempData["InOrgnaicManureDurationError"] != null)
                    {
                        TempData["InOrgnaicManureDurationError"] = null;
                    }
                    return RedirectToAction("Fields");
                }
                else
                {
                    TempData["FieldGroupError"] = ex.Message;
                    if (TempData["InOrgnaicManureDurationError"] != null)
                    {
                        TempData["InOrgnaicManureDurationError"] = null;
                    }
                    return RedirectToAction("FieldGroup", new { q = model.EncryptedFarmId, r = model.EncryptedHarvestYear });

                }
            }
            //int counter = 0;
            //if (model.ApplicationForFertiliserManures == null)
            //{
            //    model.ApplicationForFertiliserManures = new List<ApplicationForFertiliserManure>();
            //    var AppForFertManure = new ApplicationForFertiliserManure
            //    {
            //        EncryptedCounter = _fertiliserManureProtector.Protect(counter.ToString()),
            //        Counter = counter
            //    };
            //    model.Counter = counter;
            //    model.EncryptedCounter = _fertiliserManureProtector.Protect(counter.ToString());
            //    model.ApplicationForFertiliserManures.Add(AppForFertManure);
            //    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            //}
            //if (!string.IsNullOrWhiteSpace(q))
            //{
            //    int currentCounter = Convert.ToInt32(_fertiliserManureProtector.Unprotect(q));
            //    model.Counter = currentCounter;
            //    model.EncryptedCounter = q;
            //    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            //}

            if (model.FieldList.Count == 1)
            {
                Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(model.FieldList[0]));
                model.FieldName = field.Name;
            }
            model.IsClosedPeriodWarning = false;
            model.IsClosedPeriodWarningOnlyForGrassAndOilseed = false;
            model.IsWarningMsgNeedToShow = false;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InOrgnaicManureDuration(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : InOrgnaicManureDuration() post action called");
            Error? error = null;
            try
            {
                if ((!ModelState.IsValid) && ModelState.ContainsKey("Date"))
                {
                    var dateError = ModelState["Date"]?.Errors.Count > 0 ?
                                    ModelState["Date"]?.Errors[0].ErrorMessage.ToString() : null;

                    //if (dateError != null && dateError.Equals(string.Format(Resource.MsgDateMustBeARealDate, Resource.lblDate)))
                    //{
                    //    ModelState["Date"]?.Errors.Clear();
                    //    ModelState["Date"]?.Errors.Add(Resource.MsgEnterTheDateInNumber);
                    //}
                    if (dateError != null && (dateError.Equals(Resource.MsgDateMustBeARealDate) ||
                    dateError.Equals(Resource.MsgDateMustIncludeAMonth) ||
                     dateError.Equals(Resource.MsgDateMustIncludeAMonthAndYear) ||
                     dateError.Equals(Resource.MsgDateMustIncludeADayAndYear) ||
                     dateError.Equals(Resource.MsgDateMustIncludeAYear) ||
                     dateError.Equals(Resource.MsgDateMustIncludeADay) ||
                     dateError.Equals(Resource.MsgDateMustIncludeADayAndMonth)))
                    {
                        ModelState["Date"].Errors.Clear();
                        ModelState["Date"].Errors.Add(Resource.MsgTheDateMustInclude);
                    }
                }

                if (model.Date == null)
                {
                    ModelState.AddModelError("Date", Resource.MsgEnterADateBeforeContinuing);
                }

                DateTime maxDate = new DateTime(model.HarvestYear.Value + 1, 12, 31);
                DateTime minDate = new DateTime(model.HarvestYear.Value - 1, 01, 01);

                if (model.Date > maxDate)
                {
                    ModelState.AddModelError("Date", string.Format(Resource.MsgManureApplicationMaxDate, model.HarvestYear.Value, maxDate.Date.ToString("dd MMMM yyyy")));
                }
                if (model.Date < minDate)
                {
                    ModelState.AddModelError("Date", string.Format(Resource.MsgManureApplicationMinDate, model.HarvestYear.Value, minDate.Date.ToString("dd MMMM yyyy")));
                }
                if (!ModelState.IsValid)
                {
                    if (int.TryParse(model.FieldGroup, out int fieldGroup) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count == 1))
                    {
                        foreach (var fieldId in model.FieldList)
                        {
                            (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                            if (error == null)
                            {
                                WarningWithinPeriod warning = new WarningWithinPeriod();
                                string closedPeriod = warning.ClosedPeriodForFertiliser(cropTypeResponse.CropTypeId);

                                //model.ClosedPeriod = closedPeriod;
                                if (!string.IsNullOrWhiteSpace(closedPeriod))
                                {
                                    int harvestYear = model.HarvestYear ?? 0;
                                    //int startYear = harvestYear;
                                    //int endYear = harvestYear + 1;
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
                                            DateTime? closedPeriodStartDate = null;
                                            DateTime? closedPeriodEndDate = null;
                                            if (startMonth <= endMonth)
                                            {
                                                closedPeriodStartDate = new DateTime(harvestYear - 1, startMonth, startDay);
                                                closedPeriodEndDate = new DateTime(harvestYear - 1, endMonth, endDay);
                                            }
                                            else if (startMonth >= endMonth)
                                            {
                                                closedPeriodStartDate = new DateTime(harvestYear - 1, startMonth, startDay);
                                                closedPeriodEndDate = new DateTime(harvestYear, endMonth, endDay);
                                            }
                                            string formattedStartDate = closedPeriodStartDate?.ToString("d MMMM yyyy");
                                            string formattedEndDate = closedPeriodEndDate?.ToString("d MMMM yyyy");

                                            Crop crop = null;
                                            CropTypeLinkingResponse cropTypeLinkingResponse = new CropTypeLinkingResponse();
                                            if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldId)))
                                            {
                                                int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldId)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                                (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                                (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                                (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID ?? 0);
                                            }
                                            //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                                            if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                                            {
                                                ViewBag.ClosedPeriod = $"{formattedStartDate} to {formattedEndDate}";
                                            }
                                        }
                                    }


                                }
                            }
                        }
                    }
                    return View(model);
                }

                model.IsClosedPeriodWarning = false;
                model.IsClosedPeriodWarningOnlyForGrassAndOilseed = false;
                if (int.TryParse(model.FieldGroup, out int value) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count == 1))
                {
                    FertiliserManureViewModel fertiliserManureViewModel = new FertiliserManureViewModel();
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                    {
                        fertiliserManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if (fertiliserManureViewModel != null)
                    {
                        if (model.Date != fertiliserManureViewModel.Date)
                        {
                            model.IsWarningMsgNeedToShow = false;
                        }
                    }
                    Crop crop = null;
                    CropTypeLinkingResponse cropTypeLinkingResponse = null;
                    if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(model.FieldList[0])))
                    {
                        int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(model.FieldList[0])).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                        (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID ?? 0);
                    }
                    //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                    if (cropTypeLinkingResponse.NMaxLimitEngland != 0)
                    {
                        (model, error) = await IsClosedPeriodWarningMessageShow(model, false);
                    }

                }
                if (model.IsClosedPeriodWarningOnlyForGrassAndOilseed || model.IsClosedPeriodWarning)
                {
                    if (!model.IsWarningMsgNeedToShow)
                    {
                        model.IsWarningMsgNeedToShow = true;
                        foreach (var fieldId in model.FieldList)
                        {
                            (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                            if (error == null)
                            {
                                WarningWithinPeriod warning = new WarningWithinPeriod();
                                ViewBag.ClosedPeriod = warning.ClosedPeriodForFertiliser(cropTypeResponse.CropTypeId);
                            }
                        }
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        return View("InOrgnaicManureDuration", model);
                    }
                }
                else
                {
                    model.IsClosedPeriodWarningOnlyForGrassAndOilseed = false;
                    model.IsClosedPeriodWarning = false;
                    model.IsWarningMsgNeedToShow = false;
                }


                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Farm Controller : Exception in InOrgnaicManureDuration() post action : {ex.Message}, {ex.StackTrace}");
                TempData["InOrgnaicManureDurationError"] = ex.Message;
                return View(model);
            }

            if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
            {
                return RedirectToAction("CheckAnswer");
            }

            return RedirectToAction("NutrientValues");
        }

        [HttpGet]
        public async Task<IActionResult> NutrientValues()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : NutrientValues() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            try
            {
                if (model.FieldList.Count == 1)
                {
                    RecommendationViewModel recommendationViewModel = new RecommendationViewModel();
                    Error error = null;
                    int fieldId;
                    try
                    {
                        if (int.TryParse(model.FieldList[0], out fieldId))
                        {
                            model.FieldName = (await _fieldService.FetchFieldByFieldId(fieldId)).Name;
                            List<RecommendationHeader> recommendationsHeader = null;

                            (recommendationsHeader, error) = await _cropService.FetchRecommendationByFieldIdAndYear(fieldId, model.HarvestYear.Value);
                            if (error == null && recommendationsHeader.Count > 0)
                            {
                                int manId = model.FertiliserManures.FirstOrDefault().ManagementPeriodID;

                                var matchedHeader = recommendationsHeader?
                                .FirstOrDefault(header => header.RecommendationData != null &&
                                header.RecommendationData.Any(rd => rd.ManagementPeriod != null &&
                                                                   rd.ManagementPeriod.ID == manId));


                                if (matchedHeader != null)
                                {
                                    if (matchedHeader.RecommendationData != null)
                                    {
                                        matchedHeader.RecommendationData = matchedHeader.RecommendationData.Where(x => x.ManagementPeriod.ID == manId).ToList();
                                        foreach (var recData in matchedHeader.RecommendationData)
                                        {
                                            model.Recommendation = new Recommendation
                                            {
                                                ID = recData.Recommendation.ID,
                                                ManagementPeriodID = recData.Recommendation.ManagementPeriodID,
                                                CropN = recData.Recommendation.CropN,
                                                CropP2O5 = recData.Recommendation.CropP2O5,
                                                CropK2O = recData.Recommendation.CropK2O,
                                                CropSO3 = recData.Recommendation.CropSO3,
                                                CropMgO = recData.Recommendation.CropMgO,
                                                CropLime = (recData.Recommendation.PreviousAppliedLime != null && recData.Recommendation.PreviousAppliedLime > 0) ? recData.Recommendation.PreviousAppliedLime : recData.Recommendation.CropLime,
                                                ManureN = recData.Recommendation.ManureN,
                                                ManureP2O5 = recData.Recommendation.ManureP2O5,
                                                ManureK2O = recData.Recommendation.ManureK2O,
                                                ManureSO3 = recData.Recommendation.ManureSO3,
                                                ManureMgO = recData.Recommendation.ManureMgO,
                                                ManureLime = recData.Recommendation.ManureLime,
                                                FertilizerN = recData.Recommendation.FertilizerN,
                                                FertilizerP2O5 = recData.Recommendation.FertilizerP2O5,
                                                FertilizerK2O = recData.Recommendation.FertilizerK2O,
                                                FertilizerSO3 = recData.Recommendation.FertilizerSO3,
                                                FertilizerMgO = recData.Recommendation.FertilizerMgO,
                                                FertilizerLime = recData.Recommendation.FertilizerLime,
                                                SNSIndex = recData.Recommendation.SNSIndex,
                                                NIndex = recData.Recommendation.NIndex,
                                                SIndex = recData.Recommendation.SIndex,
                                                LimeIndex = recData.Recommendation.PH,
                                                KIndex = recData.Recommendation.KIndex != null ? (recData.Recommendation.KIndex == Resource.lblMinusTwo ? Resource.lblTwoMinus : (recData.Recommendation.KIndex == Resource.lblPlusTwo ? Resource.lblTwoPlus : recData.Recommendation.KIndex)) : null,
                                                MgIndex = recData.Recommendation.MgIndex,
                                                PIndex = recData.Recommendation.PIndex,
                                                NaIndex = recData.Recommendation.NaIndex,
                                                FertiliserAppliedN = recData.Recommendation.FertiliserAppliedN,
                                                FertiliserAppliedP2O5 = recData.Recommendation.FertiliserAppliedP2O5,
                                                FertiliserAppliedK2O = recData.Recommendation.FertiliserAppliedK2O,
                                                FertiliserAppliedMgO = recData.Recommendation.FertiliserAppliedMgO,
                                                FertiliserAppliedSO3 = recData.Recommendation.FertiliserAppliedSO3,
                                                FertiliserAppliedNa2O = recData.Recommendation.FertiliserAppliedNa2O,
                                                FertiliserAppliedLime = recData.Recommendation.FertiliserAppliedLime,
                                                FertiliserAppliedNH4N = recData.Recommendation.FertiliserAppliedNH4N,
                                                FertiliserAppliedNO3N = recData.Recommendation.FertiliserAppliedNO3N,
                                            };

                                            //model.Recommendations = rec;
                                        }
                                    }

                                }
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace($"Farm Controller : Exception in NutrientValues() action : {ex.Message}, {ex.StackTrace}");
                        TempData["InOrgnaicManureDurationError"] = ex.Message;
                        return RedirectToAction("InOrgnaicManureDuration", model);
                    }

                }

                model.IsNitrogenExceedWarning = false;
                model.IsWarningMsgNeedToShow = false;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                //if (!string.IsNullOrWhiteSpace(q))
                //{
                //    int currentCounter = Convert.ToInt32(_fertiliserManureProtector.Unprotect(q));
                //    model.Counter = currentCounter;
                //    model.EncryptedCounter = q;
                //    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                //}
            }
            catch (Exception ex)
            {
                TempData["InOrgnaicManureDuration"] = ex.Message;
                return RedirectToAction("InOrgnaicManureDuration");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NutrientValues(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : NutrientValues() post action called");
            int index = 0;
            Error error = null;
            try
            {
                if ((!ModelState.IsValid) && ModelState.ContainsKey("N"))
                {
                    var totalNitrogenError = ModelState["N"].Errors.Count > 0 ?
                                    ModelState["N"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalNitrogenError != null && totalNitrogenError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["N"].RawValue, Resource.lblN)))
                    {
                        ModelState["N"].Errors.Clear();
                        ModelState["N"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblNitrogen));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("P2O5"))
                {
                    var totalPhosphateError = ModelState["P2O5"].Errors.Count > 0 ?
                                    ModelState["P2O5"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalPhosphateError != null && totalPhosphateError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["P2O5"].RawValue, Resource.lblP2O5)))
                    {
                        ModelState["P2O5"].Errors.Clear();
                        ModelState["P2O5"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblPhosphateP2O5));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("K2O"))
                {
                    var totalPotassiumError = ModelState["K2O"].Errors.Count > 0 ?
                                    ModelState["K2O"].Errors[0].ErrorMessage.ToString() : null;

                    if (totalPotassiumError != null && totalPotassiumError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["K2O"].RawValue, Resource.lblK2O)))
                    {
                        ModelState["K2O"].Errors.Clear();
                        ModelState["K2O"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblPotashK2O));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("SO3"))
                {
                    var sulphurSO3Error = ModelState["SO3"].Errors.Count > 0 ?
                                    ModelState["SO3"].Errors[0].ErrorMessage.ToString() : null;

                    if (sulphurSO3Error != null && sulphurSO3Error.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["SO3"].RawValue, Resource.lblSO3)))
                    {
                        ModelState["SO3"].Errors.Clear();
                        ModelState["SO3"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblSulphurSO3));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("MgO"))
                {
                    var magnesiumMgOError = ModelState["MgO"].Errors.Count > 0 ?
                                    ModelState["MgO"].Errors[0].ErrorMessage.ToString() : null;

                    if (magnesiumMgOError != null && magnesiumMgOError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["MgO"].RawValue, Resource.lblMgO)))
                    {
                        ModelState["MgO"].Errors.Clear();
                        ModelState["MgO"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblMagnesiumMgO));
                    }
                }
                if ((!ModelState.IsValid) && ModelState.ContainsKey("Lime"))
                {
                    var limeError = ModelState["Lime"].Errors.Count > 0 ?
                                    ModelState["Lime"].Errors[0].ErrorMessage.ToString() : null;

                    if (limeError != null && limeError.Equals(string.Format(Resource.lblEnterNumericValue, ModelState["Lime"].RawValue, Resource.lblLime)))
                    {
                        ModelState["Lime"].Errors.Clear();
                        ModelState["Lime"].Errors.Add(string.Format(Resource.MsgEnterDataOnlyInNumber, Resource.lblLime));
                    }
                }

                if (model.N == null && model.P2O5 == null
                    && model.K2O == null && model.SO3 == null && model.MgO == null
                    && model.Lime == null)
                {
                    ModelState.AddModelError("AllNutrientNull", Resource.MsgEnterAnAmountForAMinimumOfOneNutrientBeforeContinuing);
                    ViewData["IsPostRequest"] = true;
                    //return View(model);
                }


                //if (ModelState.IsValid)
                //{
                //    if(model.N!=0)
                //    {
                //        decimal totalNutrientValue = (model.N ?? 0) + (model.P2O5 ?? 0) +
                //         (model.K2O ?? 0) + (model.SO3 ?? 0) + (model.MgO ?? 0) +
                //         (model.Lime ?? 0);
                //        if (totalNutrientValue == 0)
                //        {
                //            ModelState.AddModelError("CropTypeName", Resource.MsgEnterANumberWhichIsGreaterThanZero);
                //        }
                //    }                   
                //}
                if (model.N != null)
                {
                    if (model.N < 0 || model.N > 9999)
                    {
                        ModelState.AddModelError("N", string.Format(Resource.MsgMinMaxValidation, Resource.lblNitrogenLowercase, 9999));
                    }
                }
                if (model.P2O5 != null)
                {
                    if (model.P2O5 < 0 || model.P2O5 > 9999)
                    {
                        ModelState.AddModelError("P2O5", string.Format(Resource.MsgMinMaxValidation, Resource.lblPhosphateP2O5Lowercase, 9999));
                    }
                }
                if (model.K2O != null)
                {
                    if (model.K2O < 0 || model.K2O > 9999)
                    {
                        ModelState.AddModelError("K2O", string.Format(Resource.MsgMinMaxValidation, Resource.lblPotashK2OLowecase, 9999));
                    }
                }
                if (model.MgO != null)
                {
                    if (model.MgO < 0 || model.MgO > 9999)
                    {
                        ModelState.AddModelError("MgO", string.Format(Resource.MsgMinMaxValidation, Resource.lblMagnesiumMgO, 9999));
                    }
                }
                if (model.SO3 != null)
                {
                    if (model.SO3 < 0 || model.SO3 > 9999)
                    {
                        ModelState.AddModelError("SO3", string.Format(Resource.MsgMinMaxValidation, Resource.lblSulphurSO3Lowercase, 9999));
                    }
                }
                if (model.Lime != null)
                {
                    if (model.Lime < 0 || model.Lime > 99.9m)
                    {
                        ModelState.AddModelError("Lime", string.Format(Resource.MsgMinMaxValidation, Resource.lblLime.ToLower(), 99.9));
                    }
                }

                if (!ModelState.IsValid)
                {
                    if (model.FieldList.Count == 1)
                    {
                        RecommendationViewModel recommendationViewModel = new RecommendationViewModel();
                        int fieldId;
                        try
                        {
                            if (int.TryParse(model.FieldList[0], out fieldId))
                            {
                                model.FieldName = (await _fieldService.FetchFieldByFieldId(fieldId)).Name;
                                List<RecommendationHeader> recommendationsHeader = null;

                                (recommendationsHeader, error) = await _cropService.FetchRecommendationByFieldIdAndYear(fieldId, model.HarvestYear.Value);
                                if (error == null && recommendationsHeader.Count > 0)
                                {
                                    int manId = model.FertiliserManures.FirstOrDefault().ManagementPeriodID;

                                    var matchedHeader = recommendationsHeader?
                                    .FirstOrDefault(header => header.RecommendationData != null &&
                                    header.RecommendationData.Any(rd => rd.ManagementPeriod != null &&
                                                                       rd.ManagementPeriod.ID == manId));


                                    if (matchedHeader != null)
                                    {
                                        if (matchedHeader.RecommendationData != null)
                                        {
                                            matchedHeader.RecommendationData = matchedHeader.RecommendationData.Where(x => x.ManagementPeriod.ID == manId).ToList();
                                            foreach (var recData in matchedHeader.RecommendationData)
                                            {
                                                model.Recommendation = new Recommendation
                                                {
                                                    ID = recData.Recommendation.ID,
                                                    ManagementPeriodID = recData.Recommendation.ManagementPeriodID,
                                                    CropN = recData.Recommendation.CropN,
                                                    CropP2O5 = recData.Recommendation.CropP2O5,
                                                    CropK2O = recData.Recommendation.CropK2O,
                                                    CropSO3 = recData.Recommendation.CropSO3,
                                                    CropMgO = recData.Recommendation.CropMgO,
                                                    CropLime = (recData.Recommendation.PreviousAppliedLime != null && recData.Recommendation.PreviousAppliedLime > 0) ? recData.Recommendation.PreviousAppliedLime : recData.Recommendation.CropLime,
                                                    ManureN = recData.Recommendation.ManureN,
                                                    ManureP2O5 = recData.Recommendation.ManureP2O5,
                                                    ManureK2O = recData.Recommendation.ManureK2O,
                                                    ManureSO3 = recData.Recommendation.ManureSO3,
                                                    ManureMgO = recData.Recommendation.ManureMgO,
                                                    ManureLime = recData.Recommendation.ManureLime,
                                                    FertilizerN = recData.Recommendation.FertilizerN,
                                                    FertilizerP2O5 = recData.Recommendation.FertilizerP2O5,
                                                    FertilizerK2O = recData.Recommendation.FertilizerK2O,
                                                    FertilizerSO3 = recData.Recommendation.FertilizerSO3,
                                                    FertilizerMgO = recData.Recommendation.FertilizerMgO,
                                                    FertilizerLime = recData.Recommendation.FertilizerLime,
                                                    SNSIndex = recData.Recommendation.SNSIndex,
                                                    NIndex = recData.Recommendation.NIndex,
                                                    SIndex = recData.Recommendation.SIndex,
                                                    LimeIndex = recData.Recommendation.PH,
                                                    KIndex = recData.Recommendation.KIndex != null ? (recData.Recommendation.KIndex == Resource.lblMinusTwo ? Resource.lblTwoMinus : (recData.Recommendation.KIndex == Resource.lblPlusTwo ? Resource.lblTwoPlus : recData.Recommendation.KIndex)) : null,
                                                    MgIndex = recData.Recommendation.MgIndex,
                                                    PIndex = recData.Recommendation.PIndex,
                                                    NaIndex = recData.Recommendation.NaIndex,
                                                    FertiliserAppliedN = recData.Recommendation.FertiliserAppliedN,
                                                    FertiliserAppliedP2O5 = recData.Recommendation.FertiliserAppliedP2O5,
                                                    FertiliserAppliedK2O = recData.Recommendation.FertiliserAppliedK2O,
                                                    FertiliserAppliedMgO = recData.Recommendation.FertiliserAppliedMgO,
                                                    FertiliserAppliedSO3 = recData.Recommendation.FertiliserAppliedSO3,
                                                    FertiliserAppliedNa2O = recData.Recommendation.FertiliserAppliedNa2O,
                                                    FertiliserAppliedLime = recData.Recommendation.FertiliserAppliedLime,
                                                    FertiliserAppliedNH4N = recData.Recommendation.FertiliserAppliedNH4N,
                                                    FertiliserAppliedNO3N = recData.Recommendation.FertiliserAppliedNO3N,
                                                };
                                            }
                                        }
                                    }
                                }

                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogTrace($"Farm Controller : Exception in NutrientValues() post action : {ex.Message}, {ex.StackTrace}");
                            TempData["NutrientValuesError"] = ex.Message;
                            return View(model);
                        }

                    }
                    return View(model);
                }

                //if (model.Lime == null)
                //{
                //    model.Lime = 0;
                //}
                //if (model.N == null)
                //{
                //    model.N = 0;
                //}
                //if (model.P2O5 == null)
                //{
                //    model.P2O5 = 0;
                //}
                //if (model.K2O == null)
                //{
                //    model.K2O = 0;
                //}
                //if (model.SO3 == null)
                //{
                //    model.SO3 = 0;
                //}

                model.IsNitrogenExceedWarning = false;


                if (int.TryParse(model.FieldGroup, out int value) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count == 1))
                {
                    FertiliserManureViewModel fertiliserManureViewModel = new FertiliserManureViewModel();
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                    {
                        fertiliserManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                    if (fertiliserManureViewModel != null)
                    {
                        if (model.N != fertiliserManureViewModel.N)
                        {
                            model.IsWarningMsgNeedToShow = false;
                        }
                    }
                    if (model.FieldList.Count > 0)
                    {
                        foreach (var fieldId in model.FieldList)
                        {
                            Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                            if (field != null)
                            {
                                bool isFieldIsInNVZ = field.IsWithinNVZ.Value;
                                if (isFieldIsInNVZ)
                                {
                                    (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                                    if (error == null)
                                    {
                                        int year = model.HarvestYear.Value;
                                        WarningWithinPeriod warning = new WarningWithinPeriod();
                                        string closedPeriod = warning.ClosedPeriodForFertiliser(cropTypeResponse.CropTypeId) ?? string.Empty;

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

                                                DateTime startDate = new DateTime();
                                                DateTime endDate = new DateTime();

                                                if (startMonth <= endMonth)
                                                {
                                                    startDate = new DateTime(year - 1, startMonth, startDay);
                                                    endDate = new DateTime(year - 1, endMonth, endDay);
                                                }
                                                else if (startMonth >= endMonth)
                                                {
                                                    startDate = new DateTime(year - 1, startMonth, startDay);
                                                    endDate = new DateTime(year, endMonth, endDay);
                                                }
                                                Crop crop = null;
                                                if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldId)))
                                                {
                                                    int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldId)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                                }
                                                int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldId))?.CropOrder
                                                   ?? crop?.CropOrder.Value ?? 1;

                                                List<int> managementIds = new List<int>();
                                                if (int.TryParse(model.FieldGroup, out int fieldGroup))
                                                {
                                                    (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, cropOrder);//1 is CropOrder
                                                }
                                                else
                                                {
                                                    (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, null);//1 is CropOrder
                                                }
                                                if (error == null)
                                                {
                                                    if (managementIds.Count > 0)
                                                    {
                                                        //(model.IsNitrogenExceedWarning, string nitrogenExceedMessageTitle, string warningMsg, string nitrogenExceedFirstAdditionalMessage, string nitrogenExceedSecondAdditionalMessage, error) = await isNitrogenExceedWarning(model, managementIds[0], cropTypeResponse.CropTypeId, model.N.Value, startDate, endDate, cropTypeResponse.CropType);
                                                        if(model.N != null)
                                                        {
                                                            (model, error) = await isNitrogenExceedWarning(model, managementIds[0], cropTypeResponse.CropTypeId, model.N.Value, startDate, endDate, cropTypeResponse.CropType, false, Convert.ToInt32(fieldId));
                                                        }

                                                    }
                                                }
                                                else
                                                {
                                                    TempData["NutrientValuesError"] = error.Message;
                                                    return RedirectToAction("NutrientValues", model);
                                                }
                                            }
                                        }


                                    }
                                    else
                                    {
                                        TempData["NutrientValuesError"] = error.Message;
                                        return RedirectToAction("NutrientValues", model);
                                    }
                                }
                            }
                        }
                    }
                }
                if (model.IsNitrogenExceedWarning)
                {
                    if (!model.IsWarningMsgNeedToShow)
                    {
                        model.IsWarningMsgNeedToShow = true;
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        return View(model);
                    }
                }
                else
                {
                    model.IsNitrogenExceedWarning = false;
                    model.IsWarningMsgNeedToShow = false;
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            }
            catch (Exception ex)
            {
                TempData["NutrientValuesError"] = ex.Message;
                return View(model);
            }
            return RedirectToAction("CheckAnswer");
        }



        [HttpGet]
        public async Task<IActionResult> CheckAnswer(string? q, string? r, string? s, string? t, string? u)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : CheckAnswer() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();

            Error? error = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(r) && !string.IsNullOrWhiteSpace(s))
                {
                    if (!string.IsNullOrWhiteSpace(u))
                    {
                        model.IsComingFromRecommendation = true;
                    }
                    model.EncryptedFertId = q;
                    int decryptedId = Convert.ToInt32(_cropDataProtector.Unprotect(q));
                    int decryptedFarmId = Convert.ToInt32(_farmDataProtector.Unprotect(r));
                    model.FarmId = decryptedFarmId;
                    int decryptedHarvestYear = Convert.ToInt32(_farmDataProtector.Unprotect(s));
                    (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);
                    if (error.Message == null)
                    {
                        model.FarmCountryId = farm.CountryID;
                    }
                    if (decryptedId > 0)
                    {
                        (FertiliserManure fertiliserManure, error) = await _fertiliserManureService.FetchFertiliserByIdAsync(decryptedId);

                        int counter = 1;
                        if (string.IsNullOrWhiteSpace(error.Message) && fertiliserManure != null)
                        {
                            (List<FertiliserAndOrganicManureUpdateResponse> fertiliserResponse, error) = await _fertiliserManureService.FetchFieldWithSameDateAndNutrient(decryptedId, decryptedFarmId, decryptedHarvestYear);
                            if (string.IsNullOrWhiteSpace(error.Message) && fertiliserResponse != null && fertiliserResponse.Count > 0)
                            {
                                model.UpdatedFertiliserIds = fertiliserResponse;
                                if (model.IsComingFromRecommendation)
                                {
                                    model.FieldGroup = Resource.lblSelectSpecificFields;
                                    model.FieldGroupName = Resource.lblSelectSpecificFields;
                                    model.UpdatedFertiliserIds.RemoveAll(x => x.FertiliserId != fertiliserManure.ID);
                                    fertiliserResponse.RemoveAll(x => x.FertiliserId != fertiliserManure.ID);
                                }

                                var SelectListItem = fertiliserResponse.Select(f => new SelectListItem
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
                                        model.FieldID = fertiliserResponse.Select(x => x.Id.Value).FirstOrDefault();
                                    }
                                }
                                foreach (string field in model.FieldList)
                                {
                                    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                                    cropList = cropList.Where(x => x.Year == decryptedHarvestYear).ToList();

                                    if (cropList != null && cropList.Count == 2)
                                    {
                                        model.FieldID = Convert.ToInt32(field);
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
                                        int fertiliserCounter = 1;
                                        foreach (string fieldId in model.FieldList)
                                        {
                                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldId));
                                            cropList = cropList.Where(x => x.Year == decryptedHarvestYear).ToList();
                                            if (cropList != null && cropList.Count == 2)
                                            {
                                                (managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliserManure.ManagementPeriodID);
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
                                                            EncryptedCounter = _fieldDataProtector.Protect(fertiliserCounter.ToString()), //model.DoubleCropEncryptedCounter,
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
                                if (model.FertiliserManures == null)
                                {
                                    model.FertiliserManures = new List<FertiliserManure>();
                                }
                                int? defoliation = null;
                                string defoliationName = string.Empty;
                                (managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliserManure.ManagementPeriodID);
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

                                        fertiliserManure.EncryptedCounter = _fieldDataProtector.Protect(counter.ToString());
                                        fertiliserManure.Defoliation = defoliation;
                                        fertiliserManure.DefoliationName = defoliationName;

                                    }
                                }
                                fertiliserManure.FieldID = model.FieldID;
                                fertiliserManure.FieldName = model.FieldName;
                                //var fertiliser = new FertiliserManure
                                //{
                                //    ManagementPeriodID = fertiliserManure.ManagementPeriodID
                                //};
                                counter++;
                                model.FertiliserManures.Add(fertiliserManure);

                            }
                            ;

                            model.IsSameDefoliationForAll = true;
                            model.HarvestYear = decryptedHarvestYear;
                            //model.DefoliationCurrentCounter = 1;
                            model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                            model.FarmId = decryptedFarmId;
                            model.EncryptedHarvestYear = s;
                            model.EncryptedFarmId = r;
                            model.N = fertiliserManure.N;
                            model.P2O5 = fertiliserManure.P2O5;
                            model.MgO = fertiliserManure.MgO;
                            model.Lime = fertiliserManure.Lime;
                            model.SO3 = fertiliserManure.SO3;
                            model.K2O = fertiliserManure.K2O;
                            model.Date = fertiliserManure.ApplicationDate.Value.ToLocalTime();
                            model.FieldGroup = Resource.lblSelectSpecificFields;

                            foreach (var updateFertiliser in model.UpdatedFertiliserIds)
                            {
                                if (!model.FertiliserManures.Any(x => x.ManagementPeriodID == updateFertiliser.ManagementPeriodId))
                                {
                                    (ManagementPeriod managementPeriod, Error updateFertilserError) = await _cropService.FetchManagementperiodById(updateFertiliser.ManagementPeriodId.Value);
                                    if (managementPeriod != null)
                                    {
                                        (Crop crop, updateFertilserError) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                        if (crop != null)
                                        {
                                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(crop.FieldID.Value);
                                            if (cropList != null && cropList.Count > 0)
                                            {
                                                cropList = cropList.Where(x => x.Year == model.HarvestYear).ToList();
                                                if (cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null))
                                                {
                                                    int cropId = cropList.Where(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass && x.DefoliationSequenceID != null).
                                                        Select(x => x.ID.Value).FirstOrDefault();
                                                    (List<ManagementPeriod> managementPeriods, updateFertilserError) = await _cropService.FetchManagementperiodByCropId(cropId, false);
                                                    if (managementPeriods.Count > 0)
                                                    {
                                                        updateFertiliser.ManagementPeriodId = managementPeriods.Select(x => x.ID).FirstOrDefault();
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        }
                    }
                }
                else
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                    {
                        model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                    }
                    else
                    {
                        return RedirectToAction("FarmList", "Farm");
                    }
                }
                if (model != null && model.FieldList != null)
                {
                    model.IsClosedPeriodWarningOnlyForGrassAndOilseed = false;
                    model.IsClosedPeriodWarning = false;
                    if (int.TryParse(model.FieldGroup, out int value) || (model.FieldGroup == Resource.lblSelectSpecificFields && model.FieldList.Count == 1))
                    {
                        Crop crop = null;
                        CropTypeLinkingResponse cropTypeLinkingResponse = null;
                        if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(model.FieldList[0])))
                        {
                            int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(model.FieldList[0])).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            (cropTypeLinkingResponse, error) = await _organicManureService.FetchCropTypeLinkingByCropTypeId(crop.CropTypeID ?? 0);
                        }
                        //NMaxLimitEngland is 0 for England and Whales for crops Winter beans​ ,Spring beans​, Peas​ ,Market pick peas
                        if (cropTypeLinkingResponse != null && cropTypeLinkingResponse.NMaxLimitEngland != 0)
                        {
                            (model, error) = await IsClosedPeriodWarningMessageShow(model, false);
                        }
                        //(model, error) = await IsClosedPeriodWarningMessageShow(model, true);
                        //if (error != null)
                        //{
                        //    TempData["NutrientValuesError"] = error.Message;
                        //    return RedirectToAction("NutrientValues", model);
                        //}
                    }

                    foreach (var fieldId in model.FieldList)
                    {
                        Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                        if (field != null)
                        {
                            bool isFieldIsInNVZ = field.IsWithinNVZ.Value;
                            if (isFieldIsInNVZ)
                            {
                                (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                                if (error == null)
                                {
                                    int year = model.HarvestYear.Value;
                                    WarningWithinPeriod warning = new WarningWithinPeriod();
                                    string closedPeriod = warning.ClosedPeriodForFertiliser(cropTypeResponse.CropTypeId) ?? string.Empty;

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

                                            DateTime startDate = new DateTime();
                                            DateTime endDate = new DateTime();

                                            if (startMonth <= endMonth)
                                            {
                                                startDate = new DateTime(year - 1, startMonth, startDay);
                                                endDate = new DateTime(year - 1, endMonth, endDay);
                                            }
                                            else if (startMonth >= endMonth)
                                            {
                                                startDate = new DateTime(year - 1, startMonth, startDay);
                                                endDate = new DateTime(year, endMonth, endDay);
                                            }

                                            Crop crop = null;
                                            if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldId)))
                                            {
                                                int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldId)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                                (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                                (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                            }
                                            int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldId))?.CropOrder
                                               ?? crop?.CropOrder.Value ?? 1;

                                            List<int> managementIds = new List<int>();
                                            if (int.TryParse(model.FieldGroup, out int fieldGroup))
                                            {
                                                (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, cropOrder);//1 is CropOrder
                                            }
                                            else
                                            {
                                                (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldId, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, null);//1 is CropOrder
                                            }
                                            if (error == null)
                                            {
                                                if (managementIds.Count > 0)
                                                {
                                                    //(model.IsNitrogenExceedWarning, string nitrogenExceedMessageTitle, string warningMsg, string nitrogenExceedFirstAdditionalMessage, string nitrogenExceedSecondAdditionalMessage, error) = await isNitrogenExceedWarning(model, managementIds[0], cropTypeResponse.CropTypeId, model.N.Value, startDate, endDate, cropTypeResponse.CropType);
                                                    if(model.N != null)
                                                    {
                                                        (model, error) = await isNitrogenExceedWarning(model, managementIds[0], cropTypeResponse.CropTypeId, model.N.Value, startDate, endDate, cropTypeResponse.CropType, false, Convert.ToInt32(fieldId));
                                                    }

                                                }
                                            }
                                            else
                                            {
                                                TempData["NutrientValuesError"] = error.Message;
                                                return RedirectToAction("NutrientValues", model);
                                            }
                                        }
                                    }


                                }
                                else
                                {
                                    if (string.IsNullOrWhiteSpace(model.EncryptedFertId))
                                    {
                                        TempData["NutrientValuesError"] = error.Message;
                                        return RedirectToAction("NutrientValues", model);
                                    }
                                    else
                                    {
                                        TempData["CheckYourAnswerError"] = error.Message;
                                        return View(model);

                                    }
                                }
                                if (model.IsNitrogenExceedWarning)
                                {
                                    if (!model.IsWarningMsgNeedToShow)
                                    {
                                        model.IsWarningMsgNeedToShow = true;
                                    }
                                }
                                else
                                {
                                    model.IsNitrogenExceedWarning = false;
                                    model.IsWarningMsgNeedToShow = false;
                                }
                                //if (model.IsNitrogenExceedWarning)
                                //{
                                //    break;
                                //}
                            }
                        }
                    }
                }

                model.IsCheckAnswer = true;
                model.IsAnyChangeInField = false;
                model.IsAnyChangeInSameDefoliationFlag = false;
                if (model.IsClosedPeriodWarningOnlyForGrassAndOilseed || model.IsClosedPeriodWarning || model.IsNitrogenExceedWarning)
                {
                    model.IsWarningMsgNeedToShow = true;
                }
                if (string.IsNullOrWhiteSpace(s))
                {
                    (List<CommonResponse> fieldList, error) = await _fertiliserManureService.FetchFieldByFarmIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, model.FarmId.Value, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup);
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
                                if (string.IsNullOrWhiteSpace(model.EncryptedFertId))
                                {
                                    ViewBag.Fields = fieldList;
                                }
                                if (model.FieldList != null && model.FieldList.Count == 1 && fieldNames != null)
                                {
                                    model.FieldName = fieldNames.FirstOrDefault();
                                }
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(model.EncryptedFertId))
                    {
                        (List<FertiliserAndOrganicManureUpdateResponse> fertiliserResponse, error) = await _fertiliserManureService.FetchFieldWithSameDateAndNutrient(Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedFertId)), model.FarmId.Value, model.HarvestYear.Value);
                        if (string.IsNullOrWhiteSpace(error.Message) && fertiliserResponse != null && fertiliserResponse.Count > 0)
                        {
                            var SelectListItem = fertiliserResponse.Select(f => new SelectListItem
                            {
                                Value = f.Id.ToString(),
                                Text = f.Name.ToString()
                            }).ToList().DistinctBy(x => x.Value);
                            ViewBag.Fields = SelectListItem.OrderBy(x => x.Text).ToList();
                        }
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(model.EncryptedFertId))
                {
                    TempData["NutrientValuesError"] = ex.Message;
                    return RedirectToAction("NutrientValues");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(model.EncryptedFertId) && (model.IsComingFromRecommendation))
                    {
                        TempData["NutrientRecommendationsError"] = ex.Message;
                        string fieldId = model.FieldList[0];
                        return RedirectToAction("Recommendations", "Crop", new
                        {
                            q = model.EncryptedFarmId,
                            r = _fieldDataProtector.Protect(fieldId),
                            s = model.EncryptedHarvestYear

                        });
                    }
                    else
                    {
                        TempData["ErrorOnHarvestYearOverview"] = ex.Message;
                        return RedirectToAction("HarvestYearOverview", "Crop", new
                        {
                            id = model.EncryptedFarmId,
                            year = model.EncryptedHarvestYear
                        });

                    }
                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckAnswer(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : CheckAnswer() post action called");
            Error error = new Error();
            try
            {
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
                                ModelState.AddModelError("FieldName", string.Format("{0} {1}", string.Format(Resource.lblWhichCropIsThisManureApplication, (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name), Resource.lblNotSet));
                                index++;
                            }
                        }
                    }

                }

                List<string> fieldListCopy = new List<string>(model.FieldList);
                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                {
                    List<string> grassFieldIds = new List<string>();
                    foreach (string field in model.FieldList)
                    {
                        Crop crop = null;
                        if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(field)))
                        {
                            int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(field)).Select(x => x.ManagementPeriodID).FirstOrDefault();

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
                    foreach (var fertiliser in model.FertiliserManures)
                    {
                        if (grassFieldIds.Any(x => x == fertiliser.FieldID.ToString()) && fertiliser.Defoliation == null)
                        {
                            if (model.IsSameDefoliationForAll.HasValue && (model.IsSameDefoliationForAll.Value) && (model.GrassCropCount > 1))
                            {
                                ModelState.AddModelError(string.Concat("FertiliserManures[", i, "].Defoliation"), string.Format("{0} {1}", Resource.lblWhichCutOrGrazingInThisInorganicApplicationForAllField, Resource.lblNotSet));
                            }
                            else
                            {
                                ModelState.AddModelError(string.Concat("FertiliserManures[", i, "].Defoliation"), string.Format("{0} {1}", string.Format(Resource.lblWhichCutOrGrazingInThisInorganicApplicationForInField, fertiliser.FieldName), Resource.lblNotSet));
                            }
                        }
                        i++;
                    }
                    foreach (var field in grassFieldIds)
                    {
                        fieldListCopy.Remove(field);
                    }

                    List<int> managementIds = new List<int>();
                    // string arableFieldIds = fieldListCopy.Count > 0 ? string.Join(",", fieldListCopy) : string.Empty;
                    if (model.FertiliserManures.Count < model.FieldList.Count)
                    {
                        if (fieldListCopy.Count > 0)
                        {
                            foreach (string fieldIdForManID in fieldListCopy)
                            {
                                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldIdForManID));
                                Crop crop = null;
                                if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldIdForManID)))
                                {
                                    int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldIdForManID)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                }
                                int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldIdForManID))?.CropOrder
                                   ?? crop?.CropOrder.Value ?? 1;

                                if (cropList != null && cropList.Count > 0)
                                {

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

                                (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, cropOrder);

                                if (managementIds.Count > 0)
                                {
                                    foreach (var manIds in managementIds)
                                    {
                                        var fertiliser = new FertiliserManure
                                        {
                                            ManagementPeriodID = manIds,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName
                                        };
                                        model.FertiliserManures.Add(fertiliser);
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

                                var fertiliser = model.FertiliserManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (fertiliser != null)
                                    {
                                        fertiliser.ManagementPeriodID = managementPeriodId;
                                    }
                                }
                            }
                        }
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
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
                    //            var fertiliser = new FertiliserManure
                    //            {
                    //                ManagementPeriodID = manid
                    //            };
                    //            model.FertiliserManures.Add(fertiliser);
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

                                var fertiliser = model.FertiliserManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (fertiliser != null)
                                    {
                                        fertiliser.ManagementPeriodID = managementPeriodId;
                                    }
                                    else
                                    {
                                        model.FertiliserManures.Add(new FertiliserManure
                                        {
                                            ManagementPeriodID = managementPeriodId,
                                            FieldID = doubleCrop.FieldID,
                                            FieldName = doubleCrop.FieldName
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
                    if (!(model.FertiliserManures != null && model.FertiliserManures.Count > 0))
                    {
                        model.FertiliserManures = new List<FertiliserManure>();
                    }
                    if (model.FertiliserManures.Count < model.FieldList.Count && targetFieldIds?.Count > 0)
                    {
                        foreach (string fieldIdStr in targetFieldIds)
                        {
                            int fieldId = Convert.ToInt32(fieldIdStr);
                            if (!model.FertiliserManures.Any(x => x.FieldID == fieldId))
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

                                Crop crop = null;
                                if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldIdStr)))
                                {
                                    int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldIdStr)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                }
                                int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldIdStr))?.CropOrder
                                   ?? crop?.CropOrder.Value ?? 1;


                                (List<int> managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(
                                    model.HarvestYear.Value,
                                    fieldIdStr,
                                    (model.FieldGroup == Resource.lblSelectSpecificFields || model.FieldGroup == Resource.lblAll)
                                        ? null
                                        : model.FieldGroup,
                                    cropOrder
                                );

                                if (managementIds?.Count > 0)
                                {
                                    foreach (var managementId in managementIds)
                                    {
                                        model.FertiliserManures.Add(new FertiliserManure
                                        {
                                            ManagementPeriodID = managementId,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName
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
                if (!ModelState.IsValid)
                {
                    return View(model);
                }
                if (model.FertiliserManures.Count > 0)
                {
                    foreach (var fertiliserManure in model.FertiliserManures)
                    {
                        fertiliserManure.ManagementPeriodID = fertiliserManure.ManagementPeriodID;
                        fertiliserManure.ApplicationDate = model.Date.Value;
                        fertiliserManure.N = model.N ?? 0;
                        fertiliserManure.P2O5 = model.P2O5 ?? 0;
                        fertiliserManure.K2O = model.K2O ?? 0;
                        fertiliserManure.SO3 = model.SO3 ?? 0;
                        fertiliserManure.Lime = model.Lime ?? 0;
                        fertiliserManure.MgO = model.MgO ?? 0;
                        fertiliserManure.ApplicationRate = 1;
                    }
                }
                if (model.FertiliserManures.Count > 0)
                {
                    List<FertiliserManure> fertiliserList = new List<FertiliserManure>();
                    List<WarningMessage> warningMessageList = new List<WarningMessage>();
                    var FertiliserManure = new List<object>();
                    foreach (FertiliserManure fertiliserManure in model.FertiliserManures)
                    {
                        FertiliserManure fertManure = new FertiliserManure
                        {
                            ManagementPeriodID = fertiliserManure.ManagementPeriodID,
                            ApplicationDate = fertiliserManure.ApplicationDate,
                            ApplicationRate = fertiliserManure.ApplicationRate ?? 0,
                            Confirm = fertiliserManure.Confirm,
                            N = fertiliserManure.N ?? 0,
                            P2O5 = fertiliserManure.P2O5 ?? 0,
                            K2O = fertiliserManure.K2O ?? 0,
                            MgO = fertiliserManure.MgO ?? 0,
                            SO3 = fertiliserManure.SO3 ?? 0,
                            Na2O = fertiliserManure.Na2O ?? 0,
                            NFertAnalysisPercent = fertiliserManure.NFertAnalysisPercent ?? 0,
                            P2O5FertAnalysisPercent = fertiliserManure.P2O5FertAnalysisPercent ?? 0,
                            K2OFertAnalysisPercent = fertiliserManure.K2OFertAnalysisPercent ?? 0,
                            MgOFertAnalysisPercent = fertiliserManure.MgOFertAnalysisPercent ?? 0,
                            SO3FertAnalysisPercent = fertiliserManure.SO3FertAnalysisPercent ?? 0,
                            Na2OFertAnalysisPercent = fertiliserManure.Na2OFertAnalysisPercent ?? 0,
                            Lime = fertiliserManure.Lime ?? 0,
                            NH4N = fertiliserManure.NH4N ?? 0,
                            NO3N = fertiliserManure.NO3N ?? 0,
                        };
                        fertiliserList.Add(fertManure);


                        warningMessageList = new List<WarningMessage>();
                        warningMessageList = await GetWarningMessages(model);

                        FertiliserManure.Add(new
                        {
                            FertiliserManure = fertManure,
                            WarningMessages = warningMessageList.Count > 0 ? warningMessageList:null,
                        });
                    }

                    var jsonData = new
                    {
                        FertiliserManure
                    };
                    string jsonString = JsonConvert.SerializeObject(jsonData);

                    (List<FertiliserManure> fertiliserResponse, error) = await _fertiliserManureService.AddFertiliserManureAsync(jsonString);

                    if (error == null)
                    {
                        string successMsg = Resource.lblFertilisersHavebeenSuccessfullyAdded;
                        string successMsgSecond = Resource.lblSelectAFieldToSeeItsUpdatedNutrientRecommendation;
                        bool success = true;
                        _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
                        if (!model.IsComingFromRecommendation)
                            return RedirectToAction("HarvestYearOverview", "Crop", new
                            {
                                id = model.EncryptedFarmId,
                                year = model.EncryptedHarvestYear,
                                q = _farmDataProtector.Protect(success.ToString()),
                                r = _cropDataProtector.Protect(successMsg),
                                v = _cropDataProtector.Protect(successMsgSecond)
                            });
                        else
                        {
                            string fieldId = model.FieldList[0];
                            return RedirectToAction("Recommendations", "Crop", new
                            {
                                q = model.EncryptedFarmId,
                                r = _fieldDataProtector.Protect(fieldId),
                                s = model.EncryptedHarvestYear,
                                t = _cropDataProtector.Protect(successMsg),
                                u = _cropDataProtector.Protect(successMsgSecond)

                            });
                        }
                    }
                    else
                    {
                        TempData["CheckYourAnswerError"] = error.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["CheckYourAnswerError"] = ex.Message;
                return View(model);
            }
            return View(model);
        }
        public IActionResult BackCheckAnswer()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : BackCheckAnswer() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            model.IsCheckAnswer = false;
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            if (!string.IsNullOrWhiteSpace(model.EncryptedFertId) && (!model.IsComingFromRecommendation))
            {
                _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
                return RedirectToAction("HarvestYearOverview", "Crop", new
                {
                    id = model.EncryptedFarmId,
                    year = model.EncryptedHarvestYear
                });
            }
            else if (!string.IsNullOrWhiteSpace(model.EncryptedFertId) && (model.IsComingFromRecommendation))
            {
                _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
                string fieldId = model.FieldList[0];
                return RedirectToAction("Recommendations", "Crop", new
                {
                    q = model.EncryptedFarmId,
                    r = _fieldDataProtector.Protect(fieldId),
                    s = model.EncryptedHarvestYear

                });
            }

            return RedirectToAction("NutrientValues");
        }

        private async Task<(FertiliserManureViewModel, Error?)> IsClosedPeriodWarningMessageShow(FertiliserManureViewModel model, bool isGetCheckAnswer)
        {
            Error? error = null;
            bool IsClosedPeriodWarningOnlyForGrassAndOilseed = false;
            bool IsClosedPeriodWarningExceptGrassAndOilseed = false;
            string warningMsg = string.Empty;
            foreach (var fieldId in model.FieldList)
            {
                Field field = await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId));
                if (field != null)
                {
                    bool isFieldIsInNVZ = field.IsWithinNVZ.Value;
                    if (isFieldIsInNVZ)
                    {
                        (CropTypeResponse cropTypeResponse, error) = await _organicManureService.FetchCropTypeByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                        if (error == null)
                        {
                            (FieldDetailResponse fieldDetail, error) = await _fieldService.FetchFieldDetailByFieldIdAndHarvestYear(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                            if (error == null)
                            {

                                HashSet<int> filterCrops = new HashSet<int>
                                {
                                    (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape,
                                    (int)NMP.Portal.Enums.CropTypes.Asparagus,
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


                                WarningWithinPeriod warning = new WarningWithinPeriod();
                                string closedPeriod = warning.ClosedPeriodForFertiliser(cropTypeResponse.CropTypeId) ?? string.Empty;
                                bool isWithinClosedPeriod = warning.IsFertiliserApplicationWithinWarningPeriod(model.Date.Value, closedPeriod);

                                if (!filterCrops.Contains(cropTypeResponse.CropTypeId))
                                {
                                    if (isWithinClosedPeriod)
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                        {
                                            //if (!isGetCheckAnswer)
                                            //{
                                                model.IsClosedPeriodWarning = true;
                                            model.ClosedPeriodWarningHeader = Resource.MsgClosedSpreadingPeriod;
                                            model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                            model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                            model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                                model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodFertiliserWarningPara2;
                                            //}
                                            //else
                                            //{
                                            //    model.IsClosedPeriodWarning = true;
                                            //    model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                            //}
                                        }
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                        {
                                            //if (!isGetCheckAnswer)
                                            //{
                                                model.IsClosedPeriodWarning = true;
                                            model.ClosedPeriodWarningHeader = Resource.MsgClosedSpreadingPeriod;
                                            model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                            model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                            model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                                model.ClosedPeriodWarningPara2 = Resource.MsgClosedPeriodFertiliserWarningPara2Wales;
                                            //}
                                            //else
                                            //{
                                            //    model.IsClosedPeriodWarning = true;
                                            //    model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                            //}
                                        }
                                    }
                                }

                                if (cropTypeResponse.CropTypeId == (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape || cropTypeResponse.CropTypeId == (int)NMP.Portal.Enums.CropTypes.Grass)
                                {
                                    //31 october and end of closed period
                                    string warningPeriod = string.Empty;
                                    string startPeriod = string.Empty;
                                    string endPeriod = string.Empty;
                                    string[] periods = closedPeriod.Split(" to ");

                                    if (periods.Length == 2)
                                    {
                                        startPeriod = Resource.lbl31October;
                                        endPeriod = periods[1];
                                        warningPeriod = $"{startPeriod} to {endPeriod}";
                                    }
                                    bool isWithinWarningPeriod = warning.IsFertiliserApplicationWithinWarningPeriod(model.Date.Value, warningPeriod);

                                    if (isWithinWarningPeriod)
                                    {
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                                        {
                                            //if (!isGetCheckAnswer)
                                            //{
                                            model.IsClosedPeriodWarning = true;
                                            model.ClosedPeriodWarningHeader = Resource.MsgApplicationAfter31October;
                                            model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                            model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                            model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                                model.ClosedPeriodWarningPara2 = Resource.Msg31OctoberToEndPeriodFertiliserWarningPara2;
                                            //}
                                            //else
                                            //{
                                            //    model.IsClosedPeriodWarning = true;
                                            //    model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                            //}
                                        }
                                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                        {
                                            //if (!isGetCheckAnswer)
                                            //{
                                                model.IsClosedPeriodWarning = true;
                                            model.ClosedPeriodWarningHeader = Resource.MsgApplicationAfter31October;
                                            model.ClosedPeriodWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                            model.ClosedPeriodWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                            model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                                model.ClosedPeriodWarningPara2 = Resource.Msg31OctoberToEndPeriodFertiliserWarningPara2Wales;
                                            //}
                                            //else
                                            //{
                                            //    model.IsClosedPeriodWarning = true;
                                            //    model.ClosedPeriodWarningHeading = Resource.MsgClosedPeriodFertiliserWarningHeading;
                                            //}
                                        }
                                    }

                                }

                            }
                            else
                            {
                                return (model, error);
                            }
                        }
                        else
                        {
                            return (model, error);

                        }
                    }
                }
            }
            return (model, error);
        }
        private async Task<(FertiliserManureViewModel, Error?)> isNitrogenExceedWarning(FertiliserManureViewModel model, int managementId, int cropTypeId, decimal appNitrogen, DateTime startDate, DateTime endDate, string cropType, bool isGetCheckAnswer, int fieldId)
        {
            Error? error = null;
            bool isNitrogenExceedWarning = false;
            string nitrogenExceedMessageTitle = string.Empty;
            string warningMsg = string.Empty;
            string nitrogenExceedFirstAdditionalMessage = string.Empty;
            string nitrogenExceedSecondAdditionalMessage = string.Empty;
            decimal totalNitrogen = 0;
            //if we are coming for update then we will exclude the fertiliserId.
            if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
            {
                (totalNitrogen, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, startDate, endDate, model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.FertiliserId).FirstOrDefault(), false);
            }
            else
            {
                (totalNitrogen, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, startDate, endDate, null, false);

            }
            if (error == null)
            {
                WarningWithinPeriod warningMessage = new WarningWithinPeriod();
                string message = string.Empty;
                totalNitrogen = totalNitrogen + Convert.ToDecimal(model.N);

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
                string closedPeriod = warningMessage.ClosedPeriodForFertiliser(cropTypeId) ?? string.Empty;
                bool isWithinClosedPeriod = warningMessage.IsFertiliserApplicationWithinWarningPeriod(model.Date.Value, closedPeriod);
                string startPeriod = string.Empty;
                string endPeriod = string.Empty;
                string[] periods = closedPeriod.Split(" to ");

                if (periods.Length == 2)
                {
                    startPeriod = periods[0];
                    endPeriod = periods[1];
                }
                if (brassicaCrops.Contains(cropTypeId) && isWithinClosedPeriod)
                {
                    DateTime fourWeekDate = model.Date.Value.AddDays(-28);
                    decimal nitrogenInFourWeek = 0;
                    //if we are coming for update then we will exclude the fertiliserId.
                    if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                    {
                        (nitrogenInFourWeek, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, fourWeekDate, model.Date.Value, model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.FertiliserId).FirstOrDefault(), false);
                    }
                    else
                    {
                        (nitrogenInFourWeek, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, fourWeekDate, model.Date.Value, null, false);

                    }

                    if (error == null)
                    {
                        //nitrogenInFourWeek = nitrogenInFourWeek + Convert.ToDecimal(model.N);

                        if (totalNitrogen > 100 || model.N.Value > 50 || nitrogenInFourWeek > 0)  //nitrogenInFourWeek>0 means check Nitrogen applied within 28 days
                        {
                            if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                            {
                                model.IsNitrogenExceedWarning = true;
                                //if (!isGetCheckAnswer)
                                //{
                                model.ClosedPeriodNitrogenExceedWarningHeader = Resource.lblMaxApplicationRateForBrasicas;
                                model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;

                                model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                                    model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgClosedPeriodNitrogenExceedWarningPara1England, startPeriod, endPeriod);
                                    model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNitrogenExceedWarningPara2England;
                                //}
                                //else
                                //{
                                //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                                //}
                            }
                            if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                            {
                                model.IsNitrogenExceedWarning = true;
                                //if (!isGetCheckAnswer)
                                //{
                                model.ClosedPeriodNitrogenExceedWarningHeader = Resource.lblMaxApplicationRateForBrasicas;
                                model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingWales;
                                    model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgClosedPeriodNitrogenExceedWarningPara1Wales, startPeriod, endPeriod);
                                    model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNitrogenExceedWarningPara2Wales;
                                //}
                                //else
                                //{
                                //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingWales;
                                //}
                            }
                        }

                    }
                    else
                    {
                        return (model, error);
                    }
                }
                if ((cropTypeId == (int)NMP.Portal.Enums.CropTypes.Asparagus || cropTypeId == (int)NMP.Portal.Enums.CropTypes.BulbOnions || cropTypeId == (int)NMP.Portal.Enums.CropTypes.SaladOnions) && isWithinClosedPeriod)
                {
                    bool isNitrogenRateExceeded = false;
                    int maxNitrogenRate = 0;
                    if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.Asparagus)
                    {
                        if (totalNitrogen > 50)
                        {
                            isNitrogenRateExceeded = true;
                            maxNitrogenRate = 50;
                        }
                    }
                    if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.BulbOnions)
                    {
                        if (totalNitrogen > 40)
                        {
                            isNitrogenRateExceeded = true;
                            maxNitrogenRate = 40;
                        }
                    }
                    if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.SaladOnions)
                    {
                        if (totalNitrogen > 40)
                        {
                            isNitrogenRateExceeded = true;
                            maxNitrogenRate = 40;
                        }
                    }
                    if (isNitrogenRateExceeded)
                    {
                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                        {
                            model.IsNitrogenExceedWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            model.ClosedPeriodNitrogenExceedWarningHeader = Resource.lblMaxApplicationRateEverythingExceptWosr;
                            model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                            model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                            model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgClosedPeriodNRateExceedWarningPara1England, startPeriod, endPeriod, maxNitrogenRate);
                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNitrogenExceedWarningPara2England;
                            //}
                            //else
                            //{
                            //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                            //}
                        }
                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                        {
                            model.IsNitrogenExceedWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            model.ClosedPeriodNitrogenExceedWarningHeader = Resource.lblMaxApplicationRateEverythingExceptWosr;
                            model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                            model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                            model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNRateExceedWarningHeadingWales;
                                //model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgClosedPeriodNitrogenExceedWarningPara1Wales, startPeriod, endPeriod);
                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNRateExceedWarningPara2Wales;
                            //}
                            //else
                            //{
                            //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNRateExceedWarningHeadingWales;
                            //}
                        }
                    }
                }

                string warningPeriod = string.Empty;
                periods = closedPeriod.Split(" to ");

                if (periods.Length == 2)
                {
                    startPeriod = periods[0];
                    endPeriod = Resource.lbl31October;
                    warningPeriod = $"{startPeriod} to {endPeriod}";
                }
                bool isWithinWarningPeriod = warningMessage.IsFertiliserApplicationWithinWarningPeriod(model.Date.Value, warningPeriod);

                DateTime endOfOctober = new DateTime(model.Date.Value.Year, 10, 31);
                decimal PreviousApplicationsNitrogen = 0;
                //if we are coming for update then we will exclude the fertiliserId.
                if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                {
                    (PreviousApplicationsNitrogen, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, startDate, endOfOctober, model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.FertiliserId).FirstOrDefault(), false);
                }
                else
                {
                    (PreviousApplicationsNitrogen, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, startDate, endOfOctober, null, false);

                }
                // (decimal PreviousApplicationsNitrogen, error) = await _fertiliserManureService.FetchTotalNBasedOnManIdAndAppDate(managementId, startDate, endOfOctober,null, false);

                if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.WinterOilseedRape && isWithinWarningPeriod)
                {
                    bool isNitrogenRateExceeded = false;
                    int maxNitrogenRate = 0;

                    if ((PreviousApplicationsNitrogen + model.N.Value) > 30)
                    {
                        isNitrogenRateExceeded = true;
                        maxNitrogenRate = 30;
                    }

                    if (isNitrogenRateExceeded)
                    {
                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                        {
                            model.IsNitrogenExceedWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            model.ClosedPeriodNitrogenExceedWarningHeader = Resource.lblMaxApplicationRateForWinterOilseedRape;
                            model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                            model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                            model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgWinterOilseedRapeNRateExceedWarningPara1England, startPeriod);
                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNitrogenExceedWarningPara2England;
                            //}
                            //else
                            //{
                            //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                            //}
                        }
                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                        {
                            model.IsNitrogenExceedWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            model.ClosedPeriodNitrogenExceedWarningHeader = Resource.lblMaxApplicationRateForWinterOilseedRape;
                            model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                            model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                            model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgWinterOilseedRapeNRateExceedWarningHeadingWales;
                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgWinterOilseedRapeNRateExceedWarningPara1Wales, startPeriod);
                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNRateExceedWarningPara2Wales;
                            //}
                            //else
                            //{
                            //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgWinterOilseedRapeNRateExceedWarningHeadingWales;
                            //}
                        }
                    }
                }

                if (cropTypeId == (int)NMP.Portal.Enums.CropTypes.Grass && isWithinWarningPeriod)
                {
                    bool isNitrogenRateExceeded = false;
                    int maxNitrogenRate = 0;
                    string startString = $"{startPeriod} {startDate.Year}";
                    DateTime start = DateTime.ParseExact(startString, "d MMMM yyyy", CultureInfo.InvariantCulture);
                    string endString = $"{endPeriod} {startDate.Year}";  //because closed period start and 31 october will be in same year
                    DateTime end = DateTime.ParseExact(endString, "d MMMM yyyy", CultureInfo.InvariantCulture);
                    decimal nitrogenWithinWarningPeriod = 0;
                    //if we are coming for update then we will exclude the fertiliserId.
                    if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                    {
                        (nitrogenWithinWarningPeriod, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, start, end, model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.FertiliserId).FirstOrDefault(), false);
                    }
                    else
                    {
                        (nitrogenWithinWarningPeriod, error) = await _fertiliserManureService.FetchTotalNBasedOnFieldIdAndAppDate(fieldId, start, end, null, false);

                    }
                    // (decimal nitrogenWithinWarningPeriod, error) = await _fertiliserManureService.FetchTotalNBasedOnManIdAndAppDate(managementId, start, end,null, false);
                    if (model.N.Value > 40 || (nitrogenWithinWarningPeriod + model.N.Value) > 80)
                    {
                        isNitrogenRateExceeded = true;
                    }

                    if (isNitrogenRateExceeded)
                    {
                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.England)
                        {
                            model.IsNitrogenExceedWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            model.ClosedPeriodNitrogenExceedWarningHeader = Resource.MsgApplicationToGrassAtRateOfMoreThan;
                            model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                            model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                            model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgWinterGrassNRateExceedWarningPara1England, startPeriod);
                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNitrogenExceedWarningPara2England;
                            //}
                            //else
                            //{
                            //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgClosedPeriodNitrogenExceedWarningHeadingEngland;
                            //}
                        }
                        if (model.FarmCountryId == (int)NMP.Portal.Enums.FarmCountry.Wales)
                        {
                            model.IsNitrogenExceedWarning = true;
                            //if (!isGetCheckAnswer)
                            //{
                            model.ClosedPeriodNitrogenExceedWarningHeader = Resource.MsgApplicationToGrassAtRateOfMoreThan;
                            model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                            model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                            model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgWinterOilseedRapeNRateExceedWarningHeadingWales;
                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgWinterGrassNRateExceedWarningPara1Wales, startPeriod);
                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgClosedPeriodNRateExceedWarningPara2Wales;
                            //}
                            //else
                            //{
                            //    model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgWinterOilseedRapeNRateExceedWarningHeadingWales;
                            //}
                        }
                    }
                }

                //NMax limit for crop logic
                decimal previousApplicationsN = 0;
                decimal currentApplicationNitrogen = Convert.ToDecimal(model.N);
                //if we are coming for update then we will exclude the fertiliserId.
                if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                {
                    (previousApplicationsN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId, false, model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId == managementId).Select(x => x.FertiliserId).FirstOrDefault(), null);
                }
                else
                {
                    (previousApplicationsN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId, false, null, null);
                }
                //(previousApplicationsN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId, false);
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
                                //(totalN, error) = await _organicManureService.FetchTotalNBasedOnManIdFromOrgManureAndFertiliser(managementId, false);
                                if (error == null)
                                {
                                    decimal nMaxLimit = 0;
                                    //totalN = totalN + (totalNitrogen * model.ApplicationRate.Value);
                                    (List<int> currentYearManureTypeIds, error) = await _organicManureService.FetchManureTypsIdsByFieldIdYearAndConfirmFromOrgManure(Convert.ToInt32(fieldId), model.HarvestYear.Value, false);
                                    (List<int> previousYearManureTypeIds, error) = await _organicManureService.FetchManureTypsIdsByFieldIdYearAndConfirmFromOrgManure(Convert.ToInt32(fieldId), model.HarvestYear.Value - 1, false);
                                    if (error == null)
                                    {
                                        nMaxLimit = nmaxLimitEnglandOrWales ?? 0;
                                        //string cropInfo1 = await _cropService.FetchCropInfo1NameByCropTypeIdAndCropInfo1Id(crop[0].CropTypeID.Value, crop[0].CropInfo1.Value);
                                        OrganicManureNMaxLimitLogic organicManureNMaxLimitLogic = new OrganicManureNMaxLimitLogic();
                                        //nMaxLimit = organicManureNMaxLimitLogic.NMaxLimit(nmaxLimitEnglandOrWales ?? 0, crop[0].Yield == null ? null : crop[0].Yield.Value, fieldDetail.SoilTypeName, crop[0].CropInfo1 == null ? null : crop[0].CropInfo1.Value, crop[0].CropTypeID.Value, currentYearManureTypeIds, previousYearManureTypeIds, null);

                                        //correction begin for user story NMPT-1742
                                        decimal totalNitrogenApplied = 0;
                                        if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                                        {
                                            totalNitrogenApplied = previousApplicationsN;
                                        }
                                        else
                                        {
                                            totalNitrogenApplied = previousApplicationsN + currentApplicationNitrogen;
                                        }
                                        //end
                                        if (totalNitrogenApplied > nMaxLimit)
                                        {
                                            model.IsNitrogenExceedWarning = true;
                                            (Farm farm, error) = await _farmService.FetchFarmByIdAsync(model.FarmId.Value);

                                            if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.England)
                                            {
                                                model.ClosedPeriodNitrogenExceedWarningHeader = Resource.MsgNMaxLimitMessage;
                                                model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                                model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                                model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingEngland;
                                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgCropNmaxLimitWarningPara1England, nMaxLimit);
                                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgCropNmaxLimitWarningPara2England;
                                            }

                                            if (farm.CountryID == (int)NMP.Portal.Enums.FarmCountry.Wales)
                                            {
                                                model.ClosedPeriodNitrogenExceedWarningHeader = Resource.MsgNMaxLimitMessage;
                                                model.ClosedPeriodNitrogenExceedWarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodFertiliser;
                                                model.ClosedPeriodNitrogenExceedWarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Fertiliser;
                                                model.ClosedPeriodNitrogenExceedWarningHeading = Resource.MsgCropNmaxLimitWarningHeadingWales;
                                                model.ClosedPeriodNitrogenExceedWarningPara1 = string.Format(Resource.MsgCropNmaxLimitWarningPara1Wales, nMaxLimit);
                                                model.ClosedPeriodNitrogenExceedWarningPara2 = Resource.MsgCropNmaxLimitWarningPara2Wales;
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
            else
            {
                return (model, error);
            }

            return (model, error);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFertiliser(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : UpdateFertiliser() post action called");
            Error error = new Error();
            try
            {
                List<string> fieldListCopy = new List<string>(model.FieldList);
                if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                {
                    List<string> grassFieldIds = new List<string>();
                    foreach (string field in model.FieldList)
                    {
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
                        Crop crop = null;
                        if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(field)))
                        {
                            int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(field)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                        }
                        int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(field))?.CropOrder
                           ?? crop?.CropOrder.Value ?? 1;

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
                    foreach (var fertiliser in model.FertiliserManures)
                    {
                        if (grassFieldIds.Any(x => x == fertiliser.FieldID.ToString()) && fertiliser.Defoliation == null)
                        {
                            if (model.IsSameDefoliationForAll.HasValue && (model.IsSameDefoliationForAll.Value) && (model.GrassCropCount > 1))
                            {
                                ModelState.AddModelError(string.Concat("FertiliserManures[", i, "].Defoliation"), string.Format("{0} {1}", Resource.lblWhichCutOrGrazingInThisInorganicApplicationForAllField, Resource.lblNotSet));
                                break;
                            }
                            else
                            {
                                ModelState.AddModelError(string.Concat("FertiliserManures[", i, "].Defoliation"), string.Format("{0} {1}", string.Format(Resource.lblWhichCutOrGrazingInThisInorganicApplicationForInField, fertiliser.FieldName), Resource.lblNotSet));
                                break;
                            }
                        }
                        i++;
                    }
                    foreach (var field in grassFieldIds)
                    {
                        fieldListCopy.Remove(field);
                    }
                    List<int> managementIds = new List<int>();
                    // string arableFieldIds = fieldListCopy.Count > 0 ? string.Join(",", fieldListCopy) : string.Empty;
                    if (model.FertiliserManures.Count < model.FieldList.Count)
                    {
                        if (fieldListCopy.Count > 0)
                        {
                            foreach (string fieldIdForManID in fieldListCopy)
                            {
                                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fieldIdForManID));
                                Crop crop = null;
                                if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldIdForManID)))
                                {
                                    int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldIdForManID)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                }
                                int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldIdForManID))?.CropOrder
                                   ?? crop?.CropOrder.Value ?? 1;

                                if (cropList != null && cropList.Count > 0)
                                {

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

                                (managementIds, error) = await _fertiliserManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(model.HarvestYear.Value, fieldIdForManID, model.FieldGroup.Equals(Resource.lblSelectSpecificFields) || model.FieldGroup.Equals(Resource.lblAll) ? null : model.FieldGroup, cropOrder);

                                if (managementIds.Count > 0)
                                {
                                    foreach (var manIds in managementIds)
                                    {
                                        var fertiliser = new FertiliserManure
                                        {
                                            ManagementPeriodID = manIds,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName
                                        };
                                        model.FertiliserManures.Add(fertiliser);
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

                                var fertiliser = model.FertiliserManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (fertiliser != null)
                                    {
                                        if ((!string.IsNullOrWhiteSpace(model.EncryptedFertId)))
                                        {
                                            if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                                            {
                                                //int filteredManId = managementPeriods
                                                //     .Where(fm => model.UpdatedFertiliserIds.Any(mp => mp.ManagementPeriodId == fm.ID))
                                                //     .Select(x => x.ID.Value)
                                                //     .FirstOrDefault();


                                                foreach (var item in model.UpdatedFertiliserIds)
                                                {
                                                    if (item.ManagementPeriodId == managementPeriodId)
                                                    {
                                                        item.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault(); ;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        fertiliser.ManagementPeriodID = managementPeriodId;
                                    }
                                }
                            }
                        }
                    }
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
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

                                var fertiliser = model.FertiliserManures
                                    .FirstOrDefault(x => x.FieldID == doubleCrop.FieldID);

                                if (doubleCrop.CropName != Resource.lblGrass)
                                {
                                    if (fertiliser != null)
                                    {
                                        if ((!string.IsNullOrWhiteSpace(model.EncryptedFertId)))
                                        {
                                            if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                                            {
                                                //int filteredManId = managementPeriods
                                                //     .Where(fm => model.UpdatedFertiliserIds.Any(mp => mp.ManagementPeriodId == fm.ID))
                                                //     .Select(x => x.ID.Value)
                                                //     .FirstOrDefault();


                                                foreach (var item in model.UpdatedFertiliserIds)
                                                {
                                                    if (item.ManagementPeriodId == managementPeriodId)
                                                    {
                                                        item.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault(); ;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        fertiliser.ManagementPeriodID = managementPeriodId;
                                    }
                                    else
                                    {
                                        model.FertiliserManures.Add(new FertiliserManure
                                        {
                                            ManagementPeriodID = managementPeriodId,
                                            FieldID = doubleCrop.FieldID,
                                            FieldName = doubleCrop.FieldName
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
                    if (!(model.FertiliserManures != null && model.FertiliserManures.Count > 0))
                    {
                        model.FertiliserManures = new List<FertiliserManure>();
                    }
                    if (model.FertiliserManures.Count < model.FieldList.Count && targetFieldIds?.Count > 0)
                    {
                        foreach (string fieldIdStr in targetFieldIds)
                        {
                            int fieldId = Convert.ToInt32(fieldIdStr);
                            if (!model.FertiliserManures.Any(x => x.FieldID == fieldId))
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
                                        model.CropOrder = cropList.First().CropOrder ?? 0;
                                        model.FieldID = cropList.First().FieldID;
                                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                                    }
                                }
                                Crop crop = null;
                                if (model.FertiliserManures.Any(x => x.FieldID == Convert.ToInt32(fieldIdStr)))
                                {
                                    int manId = model.FertiliserManures.Where(x => x.FieldID == Convert.ToInt32(fieldIdStr)).Select(x => x.ManagementPeriodID).FirstOrDefault();

                                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                                    (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);
                                }
                                int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == Convert.ToInt32(fieldIdStr))?.CropOrder
                                   ?? crop?.CropOrder.Value ?? 1;

                                (List<int> managementIds, error) = await _organicManureService.FetchManagementIdsByFieldIdAndHarvestYearAndCropTypeId(
                                    model.HarvestYear.Value,
                                    fieldIdStr,
                                    (model.FieldGroup == Resource.lblSelectSpecificFields || model.FieldGroup == Resource.lblAll)
                                        ? null
                                        : model.FieldGroup,
                                    cropOrder
                                );

                                if (managementIds?.Count > 0)
                                {
                                    foreach (var managementId in managementIds)
                                    {
                                        model.FertiliserManures.Add(new FertiliserManure
                                        {
                                            ManagementPeriodID = managementId,
                                            FieldID = model.FieldID,
                                            FieldName = model.FieldName
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
                if (!ModelState.IsValid)
                {
                    return RedirectToAction("CheckAnswer");
                }

                if (!string.IsNullOrWhiteSpace(model.EncryptedFertId))
                {
                    if (model.FertiliserManures != null && model.FertiliserManures.Count > 0)
                    {
                        if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                        {
                            List<FertiliserManure> fertiliserList = new List<FertiliserManure>();
                            List<WarningMessage> warningMessageList = new List<WarningMessage>();
                            var FertiliserManure = new List<object>();
                            foreach (FertiliserManure fertiliserManure in model.FertiliserManures)
                            {
                                int? fertID = model.UpdatedFertiliserIds != null ? (model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId.Value == fertiliserManure.ManagementPeriodID).Select(x => x.FertiliserId.Value).FirstOrDefault()) : 0;

                                FertiliserManure fertManure = new FertiliserManure
                                {
                                    ID = fertID,
                                    ManagementPeriodID = fertiliserManure.ManagementPeriodID,
                                    ApplicationDate = model.Date,
                                    Defoliation = fertiliserManure.Defoliation,
                                    DefoliationName = fertiliserManure.DefoliationName,
                                    FieldID = fertiliserManure.FieldID,
                                    FieldName = fertiliserManure.FieldName,
                                    EncryptedCounter = fertiliserManure.EncryptedCounter,
                                    ApplicationRate = 1,
                                    Confirm = fertiliserManure.Confirm,
                                    N = model.N ?? 0,
                                    P2O5 = model.P2O5 ?? 0,
                                    K2O = model.K2O ?? 0,
                                    SO3 = model.SO3 ?? 0,
                                    Lime = model.Lime ?? 0,
                                    MgO = model.MgO ?? 0,
                                    Na2O = fertiliserManure.Na2O ?? 0,
                                    NFertAnalysisPercent = fertiliserManure.NFertAnalysisPercent ?? 0,
                                    P2O5FertAnalysisPercent = fertiliserManure.P2O5FertAnalysisPercent ?? 0,
                                    K2OFertAnalysisPercent = fertiliserManure.K2OFertAnalysisPercent ?? 0,
                                    MgOFertAnalysisPercent = fertiliserManure.MgOFertAnalysisPercent ?? 0,
                                    SO3FertAnalysisPercent = fertiliserManure.SO3FertAnalysisPercent ?? 0,
                                    Na2OFertAnalysisPercent = fertiliserManure.Na2OFertAnalysisPercent ?? 0,
                                    NH4N = fertiliserManure.NH4N ?? 0,
                                    NO3N = fertiliserManure.NO3N ?? 0,
                                };
                                fertiliserList.Add(fertManure);

                                warningMessageList = new List<WarningMessage>();
                                warningMessageList = await GetWarningMessages(model);
                                warningMessageList.ForEach(x => x.JoiningID = x.WarningCodeID != (int)NMP.Portal.Enums.WarningCode.NMaxLimit ? fertID : fertiliserManure.FieldID);
                                FertiliserManure.Add(new
                                {
                                    FertiliserManure = fertManure,
                                    WarningMessages = warningMessageList.Count > 0 ? warningMessageList : null,
                                });
                            }
                            var jsonData = new
                            {
                                FertiliserManure
                            };
                            string jsonString = JsonConvert.SerializeObject(jsonData);
                            (List<FertiliserManure> fertiliser, error) = await _fertiliserManureService.UpdateFertiliser(jsonString);
                            if (string.IsNullOrWhiteSpace(error.Message) && fertiliser.Count > 0)
                            {
                                bool success = true;
                                _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
                                if (model.FieldList != null && model.FieldList.Count == 1)
                                {
                                    if (!model.IsComingFromRecommendation)
                                    {
                                        return Redirect(Url.Action("HarvestYearOverview", "Crop", new
                                        {
                                            id = model.EncryptedFarmId,
                                            year = model.EncryptedHarvestYear,
                                            q = _farmDataProtector.Protect(success.ToString()),
                                            r = _cropDataProtector.Protect(Resource.MsgInorganicFertiliserApplicationUpdated),
                                            w = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault())
                                        }) + Resource.lblInorganicFertiliserApplicationsForSorting);
                                    }
                                    else
                                    {
                                        return RedirectToAction("Recommendations", "Crop", new
                                        {
                                            q = model.EncryptedFarmId,
                                            r = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault()),
                                            s = model.EncryptedHarvestYear,
                                            t = _cropDataProtector.Protect(Resource.MsgInorganicFertiliserApplicationUpdated),
                                            u = _cropDataProtector.Protect(Resource.MsgNutrientRecommendationsMayBeUpdated)

                                        });
                                    }
                                }
                                else
                                {
                                    return Redirect(Url.Action("HarvestYearOverview", "Crop", new
                                    {
                                        id = model.EncryptedFarmId,
                                        year = model.EncryptedHarvestYear,
                                        q = _farmDataProtector.Protect(success.ToString()),
                                        r = _cropDataProtector.Protect(Resource.MsgInorganicFertiliserApplicationUpdated),
                                        v = _cropDataProtector.Protect(Resource.lblSelectAFieldToSeeItsUpdatedRecommendations)
                                    }) + Resource.lblInorganicFertiliserApplicationsForSorting);
                                }
                            }
                            else
                            {
                                TempData["CheckYourAnswerError"] = error.Message;
                                return RedirectToAction("CheckAnswer");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["CheckYourAnswerError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }
            return RedirectToAction("CheckAnswer");
        }
        [HttpGet]
        public async Task<IActionResult> RemoveFertiliser(string q, string r, string s, string? t, string? u, string? v)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : RemoveFertiliser() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            Error error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                    {
                        model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
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
                        model.EncryptedFertId = q;
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
                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in RemoveFertiliser() action : {ex.Message}, {ex.StackTrace}");
                if (model.IsComingFromRecommendation)
                {
                    TempData["NutrientRecommendationsError"] = ex.Message;
                    return RedirectToAction("Recommendations", "Crop", new { q = model.EncryptedFarmId, r = r, s = model.EncryptedHarvestYear });
                }
                TempData["CheckYourAnswerError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFertiliser(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : RemoveFertiliser() post action called");
            Error error = null;
            if (model.IsDeleteFertliser == null)
            {
                ModelState.AddModelError("IsDeleteFertliser", Resource.MsgSelectAnOptionBeforeContinuing);
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
                if (!model.IsDeleteFertliser.Value)
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
                    // }
                }
                else
                {

                    List<int> fertiliserIds = new List<int>();
                    if (model.IsComingFromRecommendation && (!string.IsNullOrWhiteSpace(model.EncryptedFertId)))
                    {
                        ViewBag.EncryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                        fertiliserIds.Add(Convert.ToInt32(_cropDataProtector.Unprotect(model.EncryptedFertId)));
                    }
                    else if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0 && model.FertiliserManures != null && model.FertiliserManures.Count > 0)
                    {
                        foreach (string fieldId in model.FieldList)
                        {
                            string fieldName = (await _fieldService.FetchFieldByFieldId(Convert.ToInt32(fieldId))).Name;
                            foreach (var FertManure in model.UpdatedFertiliserIds)
                            {
                                if (fieldName.Equals(FertManure.Name))
                                {
                                    fertiliserIds.Add(FertManure.FertiliserId.Value);
                                }
                            }
                        }
                        //foreach (var fertiliserManure in model.FertiliserManures)
                        //{
                        //    fertiliserIds.Add(model.UpdatedFertiliserIds.Where(x => x.ManagementPeriodId.Value == fertiliserManure.ManagementPeriodID).Select(x => x.FertiliserId.Value).FirstOrDefault());
                        //}

                    }

                    if (fertiliserIds.Count > 0)
                    {
                        var result = new
                        {
                            fertliserManureIds = fertiliserIds
                        };
                        string jsonString = JsonConvert.SerializeObject(result);
                        (string success, error) = await _fertiliserManureService.DeleteFertiliserByIdAsync(jsonString);
                        if (string.IsNullOrWhiteSpace(error.Message))
                        {
                            _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
                            if (model.IsComingFromRecommendation)
                            {
                                if (model.FieldList != null && model.FieldList.Count > 0)
                                {
                                    string encryptedFieldId = _fieldDataProtector.Protect(model.FieldList.FirstOrDefault());
                                    if (!string.IsNullOrWhiteSpace(encryptedFieldId))
                                    {
                                        return RedirectToAction("Recommendations", "Crop", new { q = model.EncryptedFarmId, r = encryptedFieldId, s = model.EncryptedHarvestYear, t = _cropDataProtector.Protect(Resource.MsgInorganicFertiliserApplicationRemoved) });
                                        //return RedirectToAction("Recommendations", "Crop", new { q = model.EncryptedFarmId, r = encryptedFieldId, s = model.EncryptedHarvestYear });
                                    }
                                }
                            }
                            else
                            {
                                return Redirect(Url.Action("HarvestYearOverview", "Crop", new { Id = model.EncryptedFarmId, year = model.EncryptedHarvestYear, q = Resource.lblTrue, r = _cropDataProtector.Protect(Resource.MsgInorganicFertiliserApplicationRemoved) }) + Resource.lblInorganicFertiliserApplicationsForSorting); ;

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
                                    TempData["RemoveFertiliserError"] = fieldListError.Message;
                                }
                            }
                            TempData["RemoveFertiliserError"] = error.Message;
                            return View(model);
                        }
                    }

                    //foreach (var fertliser in model.UpdatedFertiliserIds)
                    //    {
                    //        fertiliserIds.Add(fertliser.FertiliserId.Value);
                    //    }


                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"OrganicManure Controller : Exception in RemoveFertiliser() post action : {ex.Message}, {ex.StackTrace}");
                TempData["RemoveFertiliserError"] = ex.Message;
                return View(model);
            }
            return View(model);


        }

        [HttpGet]
        public IActionResult Cancel()
        {
            _logger.LogTrace("Fertiliser Manure Controller : Cancel() action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Fertiliser Manure Controller : Exception in Cancel() action : {ex.Message}, {ex.StackTrace}");
                TempData["CheckYourAnswerError"] = ex.Message;
                return RedirectToAction("CheckAnswer");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(FertiliserManureViewModel model)
        {
            _logger.LogTrace("Fertiliser Manure Controller : Cancel() post action called");
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
                _httpContextAccessor.HttpContext?.Session.Remove("FertiliserManure");
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

        [HttpGet]
        public async Task<IActionResult> Defoliation(string q)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : Defoliation({q}) action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            Error error = null;
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                if (string.IsNullOrWhiteSpace(q) && model.FertiliserManures != null && model.FertiliserManures.Count > 0)
                {
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                    model.FieldID = model.FertiliserManures[0].FieldID.Value;
                    model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                }
                else if (!string.IsNullOrWhiteSpace(q) && (model.FertiliserManures != null && model.FertiliserManures.Count > 0))
                {
                    int itemCount = Convert.ToInt32(_fieldDataProtector.Unprotect(q));
                    int index = itemCount - 1;
                    if (itemCount == 0)
                    {
                        model.DefoliationCurrentCounter = 0;
                        model.DefoliationEncryptedCounter = string.Empty;
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);

                        if (model.GrassCropCount != null && model.GrassCropCount.Value > 1 && model.NeedToShowSameDefoliationForAll)
                        {
                            return RedirectToAction("IsSameDefoliationForAll");
                        }
                        if (model.IsDoubleCropAvailable)
                        {
                            return RedirectToAction("DoubleCrop", new { q = model.DoubleCropEncryptedCounter });
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
                            return RedirectToAction("Fields");
                        }
                        return RedirectToAction("FieldGroup", new
                        {
                            q = model.EncryptedFarmId,
                            r = model.EncryptedHarvestYear
                        });
                    }
                    model.FieldID = model.FertiliserManures[index].FieldID.Value;
                    model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FertiliserManures[index].FieldID.Value)).Name;
                    model.DefoliationCurrentCounter = index;
                    model.IsSameDefoliationForAll = model.IsSameDefoliationForAll ?? false;
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                }

                if (model.IsSameDefoliationForAll.HasValue && model.IsSameDefoliationForAll.Value)
                {
                    List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();
                    foreach (var fertiliser in model.FertiliserManures)
                    {
                        int manId = model.FertiliserManures.Where(x => x.FieldID == fertiliser.FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);


                        int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == fertiliser.FieldID.Value)?.CropOrder
                        ?? crop.CropOrder.Value;
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fertiliser.FieldID));

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
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FertiliserManures[model.DefoliationCurrentCounter].FieldID));

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
                                                //if (item.Text.Contains(Resource.lblSilage, StringComparison.OrdinalIgnoreCase))
                                                //{
                                                //    item.Text = item.Text.Replace(Resource.lblSilage, Resource.lblCut, StringComparison.OrdinalIgnoreCase);

                                                //}
                                                //if (item.Text.Contains(Resource.lblHay, StringComparison.OrdinalIgnoreCase))
                                                //{
                                                //    item.Text = item.Text.Replace(Resource.lblHay, Resource.lblCut, StringComparison.OrdinalIgnoreCase);

                                                //}
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

                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Fertiliser Controller : Exception in Defoliation() action : {ex.Message}, {ex.StackTrace}");
                if (string.IsNullOrWhiteSpace(model.EncryptedFertId))
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
        public async Task<IActionResult> Defoliation(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : Defoliation() post action called");
            Error error = null;
            try
            {
                if (model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation == null)
                {
                    ModelState.AddModelError("FertiliserManures[" + model.DefoliationCurrentCounter + "].Defoliation", Resource.MsgSelectAnOptionBeforeContinuing);
                }

                if (!ModelState.IsValid)
                {
                    if (model.IsSameDefoliationForAll.HasValue && model.IsSameDefoliationForAll.Value)
                    {
                        List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            int manId = model.FertiliserManures.Where(x => x.FieldID == fertiliser.FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == fertiliser.FieldID.Value)?.CropOrder
                            ?? crop.CropOrder.Value;
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fertiliser.FieldID));

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
                            int manId = model.FertiliserManures.Where(x => x.FieldID == model.FertiliserManures[model.DefoliationCurrentCounter].FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);


                            int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FertiliserManures[model.DefoliationCurrentCounter].FieldID.Value)?.CropOrder
                            ?? crop.CropOrder.Value;
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FertiliserManures[model.DefoliationCurrentCounter].FieldID));

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
                    return View(model);
                }


                if (model.IsSameDefoliationForAll.HasValue && (!model.IsSameDefoliationForAll.Value))
                {

                    for (int i = 0; i < model.FertiliserManures.Count; i++)
                    {
                        if (model.FieldID == model.FertiliserManures[i].FieldID.Value)
                        {
                            int manId = model.FertiliserManures.Where(x => x.FieldID == model.FertiliserManures[i].FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FertiliserManures[i].FieldID.Value)?.CropOrder
                        ?? crop.CropOrder.Value;
                            (managementPeriod, error) = await _cropService.FetchManagementperiodById(model.FertiliserManures[model.DefoliationCurrentCounter].ManagementPeriodID);
                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                                {
                                    (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                    if (error == null && defoliationSequence != null)
                                    {
                                        string description = defoliationSequence.DefoliationSequenceDescription;

                                        string[] defoliationParts = description.Split(',')
                                                                               .Select(x => x.Trim())
                                                                               .ToArray();

                                        string selectedDefoliation = (model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation.Value > 0 && model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation.Value <= defoliationParts.Length)
                                            ? $"{Enum.GetName(typeof(PotentialCut), model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation.Value)} - {defoliationParts[model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation.Value - 1]}"
                                            : $"{model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation.Value}";
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
                                        model.FertiliserManures[model.DefoliationCurrentCounter].DefoliationName = selectedDefoliation;
                                    }
                                }
                            }
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FertiliserManures[model.DefoliationCurrentCounter].FieldID));

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
                                    managementPeriodID = managementPeriodList.Where(x => x.Defoliation == model.FertiliserManures[model.DefoliationCurrentCounter].Defoliation).Select(x => x.ID.Value).FirstOrDefault();

                                    if (model.IsCheckAnswer && (!string.IsNullOrWhiteSpace(model.EncryptedFertId)))
                                    {
                                        int filteredManId = managementPeriodList
                                     .Where(fm => model.UpdatedFertiliserIds.Any(mp => mp.ManagementPeriodId == fm.ID))
                                     .Select(x => x.ID.Value)
                                     .FirstOrDefault();

                                        if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                                        {
                                            foreach (var item in model.UpdatedFertiliserIds)
                                            {
                                                if (item.ManagementPeriodId == filteredManId)
                                                {
                                                    item.ManagementPeriodId = managementPeriodID;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                model.FertiliserManures[i].ManagementPeriodID = managementPeriodID.Value;
                            }

                            model.DefoliationCurrentCounter++;

                            if (i + 1 < model.FertiliserManures.Count)
                            {
                                model.FieldID = model.FertiliserManures[i + 1].FieldID.Value;
                                model.FieldName = (await _fieldService.FetchFieldByFieldId(model.FieldID.Value)).Name;
                            }

                            break;
                        }
                    }
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());

                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {
                        var itemsToRemove = new List<FertiliserManure>();
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                {
                                    if (crop.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                    {
                                        itemsToRemove.Add(fertiliser);
                                    }
                                }
                            }
                        }

                        foreach (var item in itemsToRemove)
                        {
                            model.FertiliserManures.Remove(item);
                        }
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                    }
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInSameDefoliationFlag) && (!model.IsAnyChangeInField))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                }
                else if (model.IsSameDefoliationForAll.HasValue && (model.IsSameDefoliationForAll.Value))
                {
                    model.DefoliationCurrentCounter = 1;
                    for (int i = 0; i < model.FertiliserManures.Count; i++)
                    {
                        int manId = model.FertiliserManures.Where(x => x.FieldID == model.FertiliserManures[i].FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                        (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                        int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FertiliserManures[i].FieldID.Value)?.CropOrder
                        ?? crop.CropOrder.Value;
                        model.FertiliserManures[i].Defoliation = model.FertiliserManures[0].Defoliation;
                        List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FertiliserManures[i].FieldID));

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
                                managementPeriodID = managementPeriodList.Where(x => x.Defoliation == model.FertiliserManures[i].Defoliation).Select(x => x.ID.Value).FirstOrDefault();
                                if (model.IsCheckAnswer && (!string.IsNullOrWhiteSpace(model.EncryptedFertId)))
                                {
                                    int filteredManId = managementPeriodList
                                 .Where(fm => model.UpdatedFertiliserIds.Any(mp => mp.ManagementPeriodId == fm.ID))
                                 .Select(x => x.ID.Value)
                                 .FirstOrDefault();

                                    if (model.UpdatedFertiliserIds != null && model.UpdatedFertiliserIds.Count > 0)
                                    {
                                        foreach (var item in model.UpdatedFertiliserIds)
                                        {
                                            if (item.ManagementPeriodId == filteredManId)
                                            {
                                                item.ManagementPeriodId = managementPeriodID;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            model.FertiliserManures[i].ManagementPeriodID = managementPeriodID ?? 0;
                        }


                        (managementPeriod, error) = await _cropService.FetchManagementperiodById(model.FertiliserManures[i].ManagementPeriodID);
                        if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                        {
                            (crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            if (string.IsNullOrWhiteSpace(error.Message) && crop != null && crop.DefoliationSequenceID != null)
                            {
                                (DefoliationSequenceResponse defoliationSequence, error) = await _cropService.FetchDefoliationSequencesById(crop.DefoliationSequenceID.Value);
                                if (error == null && defoliationSequence != null)
                                {
                                    string description = defoliationSequence.DefoliationSequenceDescription;

                                    string[] defoliationParts = description.Split(',')
                                                                           .Select(x => x.Trim())
                                                                           .ToArray();

                                    string selectedDefoliation = (model.FertiliserManures[i].Defoliation.Value > 0 && model.FertiliserManures[i].Defoliation.Value <= defoliationParts.Length)
                                ? $"{Enum.GetName(typeof(PotentialCut), model.FertiliserManures[i].Defoliation.Value)} -{defoliationParts[model.FertiliserManures[i].Defoliation.Value - 1]}"
                                : $"{model.FertiliserManures[i].Defoliation.Value}";
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
                                    model.FertiliserManures[i].DefoliationName = selectedDefoliation;
                                }
                            }
                        }

                    }
                    model.DefoliationEncryptedCounter = _fieldDataProtector.Protect(model.DefoliationCurrentCounter.ToString());

                    if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                    {
                        var itemsToRemove = new List<FertiliserManure>();
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);

                            if (string.IsNullOrWhiteSpace(error.Message) && managementPeriod != null)
                            {
                                (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                                if (string.IsNullOrWhiteSpace(error.Message) && crop != null)
                                {
                                    if (crop.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                                    {
                                        itemsToRemove.Add(fertiliser);
                                    }
                                }
                            }
                        }

                        foreach (var item in itemsToRemove)
                        {
                            model.FertiliserManures.Remove(item);
                        }
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                    }
                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                    return RedirectToAction("InOrgnaicManureDuration");
                }
                model.GrassCropCount = model.FertiliserManures.Count;
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                if (model.DefoliationCurrentCounter == model.FertiliserManures.Count)
                {
                    if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
                    {
                        return RedirectToAction("CheckAnswer");
                    }
                    return RedirectToAction("InOrgnaicManureDuration");
                }
                else
                {
                    if (model.IsSameDefoliationForAll.HasValue && model.IsSameDefoliationForAll.Value)
                    {
                        List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();

                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fertiliser.FieldID));

                            int manId = model.FertiliserManures.Where(x => x.FieldID == fertiliser.FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == fertiliser.FieldID.Value)?.CropOrder
                            ?? crop.CropOrder.Value;
                            if (cropList.Count > 0)
                            {
                                cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                            }

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
                            int manId = model.FertiliserManures.Where(x => x.FieldID == model.FertiliserManures[model.DefoliationCurrentCounter].FieldID.Value).Select(x => x.ManagementPeriodID).FirstOrDefault();

                            (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                            (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                            int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.FertiliserManures[model.DefoliationCurrentCounter].FieldID.Value)?.CropOrder
                            ?? crop.CropOrder.Value;
                            List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(model.FertiliserManures[model.DefoliationCurrentCounter].FieldID));

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
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"Fertiliser Controller : Exception in Defoliation() post action : {ex.Message}, {ex.StackTrace}");
                TempData["DefoliationError"] = ex.Message;
                return View(model);
            }
            return RedirectToAction("InOrgnaicManureDuration");

        }
        [HttpGet]
        public async Task<IActionResult> backActionForDefoliation()
        {
            _logger.LogTrace($"Fertiliser Manure Controller : backActionForDefoliation() action called");
            FertiliserManureViewModel? model = new FertiliserManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }

            if (model.IsCheckAnswer && (!model.IsAnyChangeInSameDefoliationFlag) && (!model.IsAnyChangeInField))
            {
                return RedirectToAction("CheckAnswer");
            }

            //int grassCropCounter = 0;
            //foreach (var field in model.FieldList)
            //{
            //    List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(field));
            //    if (cropList.Count > 0)
            //    {
            //        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == model.CropOrder).ToList();
            //    }
            //    if (cropList.Count > 0)
            //    {
            //        if (cropList.Any(c => c.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
            //        {
            //            if (cropList.Count > 0 && cropList.Any(x => x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass))
            //            {
            //                grassCropCounter++;

            //            }
            //            model.IsAnyCropIsGrass = true;
            //        }
            //    }
            //}
            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
            {
                return RedirectToAction("IsSameDefoliationForAll");
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
                return RedirectToAction("Fields");
            }
            return RedirectToAction("FieldGroup", new
            {
                q = model.EncryptedFarmId,
                r = model.EncryptedHarvestYear
            });



        }
        [HttpGet]
        public async Task<IActionResult> IsSameDefoliationForAll()
        {
            _logger.LogTrace($"Fertiliser Controller : IsSameDefoliationForAll() action called");
            Error error = new Error();

            FertiliserManureViewModel model = new FertiliserManureViewModel();
            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
            {
                model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
            }
            else
            {
                return RedirectToAction("FarmList", "Farm");
            }
            if (model.IsAnyChangeInSameDefoliationFlag)
            {
                model.IsAnyChangeInSameDefoliationFlag = false;
            }
            List<List<SelectListItem>> allDefoliations = new List<List<SelectListItem>>();
            foreach (var fertiliser in model.FertiliserManures)
            {
                List<Crop> cropList = await _cropService.FetchCropsByFieldId(Convert.ToInt32(fertiliser.FieldID));

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

            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult IsSameDefoliationForAll(FertiliserManureViewModel model)
        {
            _logger.LogTrace($"Fertiliser Controller : IsSameDefoliationForAll() post action called");
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
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    FertiliserManureViewModel fertiliserManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");

                    if (model.IsSameDefoliationForAll != fertiliserManureViewModel.IsSameDefoliationForAll)
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
                    foreach (var fertliser in model.FertiliserManures)
                    {
                        fertliser.Defoliation = null;
                        fertliser.DefoliationName = null;
                    }
                }
                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                if (!model.IsAnyChangeInSameDefoliationFlag && model.IsCheckAnswer && (!model.IsAnyChangeInField))
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
        public async Task<IActionResult> DoubleCrop(string q)
        {
            _logger.LogTrace($"Fertiliser Manure Controller : DoubleCrop({q}) action called");
            FertiliserManureViewModel model = new FertiliserManureViewModel();
            try
            {
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    model = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
                }
                else
                {
                    return RedirectToAction("FarmList", "Farm");
                }

                if (string.IsNullOrWhiteSpace(q) && model.FertiliserManures != null && model.FertiliserManures.Count > 0)
                {
                    model.DoubleCropEncryptedCounter = _fieldDataProtector.Protect(model.DoubleCropCurrentCounter.ToString());

                    _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                }
                else if (!string.IsNullOrWhiteSpace(q) && (model.DoubleCrop != null && model.DoubleCrop.Count > 0))
                {
                    int itemCount = Convert.ToInt32(_fieldDataProtector.Unprotect(q));
                    int index = itemCount - 1;
                    if (itemCount == 0)
                    {
                        model.DoubleCropCurrentCounter = 0;
                        model.DoubleCropEncryptedCounter = string.Empty;
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                        if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
                        {
                            return RedirectToAction("CheckAnswer");
                        }
                        else if (model.FieldGroup == Resource.lblSelectSpecificFields && model.IsComingFromRecommendation)
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
                            return RedirectToAction("Fields");
                        }
                        return RedirectToAction("FieldGroup", new
                        {
                            q = model.EncryptedFarmId,
                            r = model.EncryptedHarvestYear
                        });
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

                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                        ViewBag.DoubleCropOptions = cropOptions;
                    }
                    if (model.DoubleCropCurrentCounter == 0)
                    {
                        model.FieldID = model.DoubleCrop[0].FieldID;
                        model.FieldName = (await _fieldService.FetchFieldByFieldId(model.DoubleCrop[0].FieldID)).Name;

                    }
                }

                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
            }
            catch (Exception ex)
            {
                if (model.FieldGroup == Resource.lblSelectSpecificFields && model.IsComingFromRecommendation)
                {
                    if (model.FieldList.Count > 0 && model.FieldList.Count == 1)
                    {
                        TempData["NutrientRecommendationsError"] = ex.Message;
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
                    TempData["FieldError"] = ex.Message;
                    return RedirectToAction("Fields");
                }
                TempData["FieldGroupError"] = ex.Message;
                return RedirectToAction("FieldGroup", new
                {
                    q = model.EncryptedFarmId,
                    r = model.EncryptedHarvestYear
                });
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DoubleCrop(FertiliserManureViewModel model)
        {
            _logger.LogTrace("Fertiliser Manure Controller : DoubleCrop() post action called");
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
                            _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                            ViewBag.DoubleCropOptions = cropOptions;
                        }
                    }
                    return View(model);
                }


                FertiliserManureViewModel fertiliserManureViewModel = new FertiliserManureViewModel();
                if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Session.Keys.Contains("FertiliserManure"))
                {
                    fertiliserManureViewModel = _httpContextAccessor.HttpContext?.Session.GetObjectFromJson<FertiliserManureViewModel>("FertiliserManure");
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
                        foreach (var fertiliser in model.FertiliserManures)
                        {
                            if (fertiliser.FieldID == model.DoubleCrop[model.DoubleCropCurrentCounter].FieldID)
                            {
                                if (model.IsCheckAnswer && (!string.IsNullOrWhiteSpace(model.EncryptedFertId)))
                                {

                                    if (model.UpdatedFertiliserIds != null)
                                    {
                                        foreach (var updatedFertIds in model.UpdatedFertiliserIds)
                                        {
                                            if (fertiliser.FieldName.Equals(updatedFertIds.Name))
                                            {
                                                updatedFertIds.ManagementPeriodId = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();
                                                break;
                                            }
                                        }
                                    }
                                }

                                fertiliser.ManagementPeriodID = managementPeriods.Select(x => x.ID.Value).FirstOrDefault();
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
                _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                //if (model.IsCheckAnswer && (!model.IsAnyChangeInField) && (!model.IsManureTypeChange))
                //{
                //    return RedirectToAction("CheckAnswer");
                //}
                bool isDoubleCropValueChange = false;
                if (model.IsCheckAnswer)
                {
                    if (fertiliserManureViewModel != null && fertiliserManureViewModel?.DoubleCrop != null && model?.DoubleCrop != null)
                    {

                        var newItem = model.DoubleCrop.FirstOrDefault(x => x.FieldID == model.FieldID.Value);
                        var oldItem = fertiliserManureViewModel.DoubleCrop.FirstOrDefault(x => x.FieldID == model.FieldID.Value);
                        if (newItem != null)
                        {
                            if (newItem.CropOrder != oldItem.CropOrder)
                            {
                                isDoubleCropValueChange = true;
                            }
                        }
                    }
                }
                foreach (var fertiliser in model.FertiliserManures)
                {
                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
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

                    int manId = model.FertiliserManures.Where(x => x.FieldID == model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID).Select(x => x.ManagementPeriodID).FirstOrDefault();

                    (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(manId);
                    (Crop crop, error) = await _cropService.FetchCropById(managementPeriod.CropID.Value);

                    int cropOrder = model.DoubleCrop?.FirstOrDefault(x => x.FieldID == model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID)?.CropOrder
                    ?? crop.CropOrder.Value;

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
                                    .Any(mp => model.FertiliserManures.Any(fm => fm.ManagementPeriodID == mp.ID.Value));
                                    if (!anyExists)
                                    {
                                        int counter = model.FertiliserManures.Count();
                                        model.FertiliserManures.AddRange(new List<FertiliserManure>
                                        {
                                            new FertiliserManure
                                            {
                                                ManagementPeriodID = ManagementPeriod.First().ID.Value,
                                                FieldID=model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID,
                                                FieldName=model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldName,
                                                EncryptedCounter = _fieldDataProtector.Protect((counter+1).ToString())
                                            }
                                        });
                                        model.GrassCropCount = model.FertiliserManures.Count; ;
                                    }

                                    //grassCropCounter++;

                                    //grassCropCounter++;
                                    //model.FertiliserManures.RemoveAll(fm => managementPeriodIdsToRemove.Contains(fm.ManagementPeriodID));
                                    model.IsAnyCropIsGrass = true;
                                }

                            }
                            //int grassCount = model.FertiliserManures.Where(x => x.Defoliation != null).Count();
                            //grassCropCounter += grassCount;
                        }

                        //model.GrassCropCount = grassCropCounter;
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
                                    model.FertiliserManures.RemoveAll(f => f.FieldID == doubleCrop.FieldID);
                                }
                            }
                        }
                        //if (crop.CropTypeID != (int)NMP.Portal.Enums.CropTypes.Grass)
                        //{
                        //    List<Crop> cropList = await _cropService.FetchCropsByFieldId(model.DoubleCrop[model.DoubleCropCurrentCounter - 1].FieldID);
                        //    if (cropList.Count > 0)
                        //    {
                        //        cropList = cropList.Where(x => x.Year == model.HarvestYear && x.CropOrder == 1 && x.CropTypeID == (int)NMP.Portal.Enums.CropTypes.Grass).ToList();
                        //        if (cropList.Count > 0)
                        //        {
                        //            (List<ManagementPeriod> manList, error) = await _cropService.FetchManagementperiodByCropId(cropList.FirstOrDefault().ID.Value, false);

                        //            if (string.IsNullOrWhiteSpace(error.Message) && manList.Count > 0)
                        //            {
                        //                var manIds = manList.Select(m => m.ID).ToList();

                        //                var itemsToRemove = model.FertiliserManures
                        //                    .Where(x => manIds.Contains(x.ManagementPeriodID))
                        //                    .ToList();

                        //                foreach (var item in itemsToRemove)
                        //                {
                        //                    model.FertiliserManures.Remove(item);
                        //                    model.GrassCropCount--;
                        //                }
                        //            }

                        //        }

                        //    }
                        //}
                    }
                    int counter = 0;
                    foreach (var fertiliser in model.FertiliserManures)
                    {
                        (ManagementPeriod managementPeriod, error) = await _cropService.FetchManagementperiodById(fertiliser.ManagementPeriodID);
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
                    if (model.IsCheckAnswer && (model.IsAnyCropIsGrass.HasValue && !model.IsAnyCropIsGrass.Value) && (!model.IsAnyChangeInField) && (!isCurrentFieldGrass))
                    {
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                        return RedirectToAction("CheckAnswer");
                    }
                    else
                    {
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        if (model.IsCheckAnswer && model.IsAnyCropIsGrass.HasValue && (!model.IsAnyChangeInField) && (!isCurrentFieldGrass))
                        {
                            model.GrassCropCount = null;
                            model.IsSameDefoliationForAll = null;
                            model.IsAnyChangeInSameDefoliationFlag = false;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                            return RedirectToAction("CheckAnswer");

                        }
                        else
                        {
                            if (model.IsAnyCropIsGrass == null || (model.IsAnyCropIsGrass.HasValue && !model.IsAnyCropIsGrass.Value))
                            {
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                return RedirectToAction("InOrgnaicManureDuration");
                            }

                            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                            {
                                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                                {
                                    if (model.FertiliserManures.Any(z => z.Defoliation != null))
                                    {
                                        model.IsSameDefoliationForAll = null;
                                    }
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                    return RedirectToAction("IsSameDefoliationForAll");
                                }
                                model.IsSameDefoliationForAll = true;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                return RedirectToAction("Defoliation");
                            }
                        }

                        //logic end for defoliation
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                        return RedirectToAction("InOrgnaicManureDuration");
                    }
                }
                else
                {
                    if (model.IsCheckAnswer && (model.IsAnyCropIsGrass.HasValue && !model.IsAnyCropIsGrass.Value) && (!model.IsAnyChangeInField) && (!isCurrentFieldGrass))
                    {
                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
                        return RedirectToAction("CheckAnswer");
                    }
                    else if (model.IsCheckAnswer && (!model.IsAnyChangeInField))
                    {
                        _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                        if (model.IsCheckAnswer && model.IsAnyCropIsGrass.HasValue && (!model.IsAnyChangeInField) && (!isCurrentFieldGrass))
                        {
                            model.GrassCropCount = null;
                            model.IsSameDefoliationForAll = null;
                            model.IsAnyChangeInSameDefoliationFlag = false;
                            _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                            return RedirectToAction("CheckAnswer");

                        }
                        else
                        {
                            if (model.IsAnyCropIsGrass == null || (model.IsAnyCropIsGrass.HasValue && !model.IsAnyCropIsGrass.Value))
                            {
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                return RedirectToAction("InOrgnaicManureDuration");
                            }

                            if (model.IsAnyCropIsGrass.HasValue && model.IsAnyCropIsGrass.Value)
                            {
                                if (model.GrassCropCount != null && model.GrassCropCount.Value > 1)
                                {
                                    if (model.FertiliserManures.Any(z => z.Defoliation != null))
                                    {
                                        model.IsSameDefoliationForAll = null;
                                    }
                                    _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                    return RedirectToAction("IsSameDefoliationForAll");
                                }
                                model.IsSameDefoliationForAll = true;
                                _httpContextAccessor.HttpContext?.Session.SetObjectAsJson("FertiliserManure", model);
                                return RedirectToAction("Defoliation");
                            }
                        }
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

                        _httpContextAccessor.HttpContext.Session.SetObjectAsJson("FertiliserManure", model);
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

        private async Task<List<WarningMessage>> GetWarningMessages(FertiliserManureViewModel model)
        {
            List<WarningMessage> warningMessages = new List<WarningMessage>();
            try
            {
                if (model != null && model.FertiliserManures != null && model.FertiliserManures.Count > 0)
                {
                    foreach (var fertiliserManure in model.FertiliserManures)
                    {
                        (ManagementPeriod managementPeriod, Error error) = await _cropService.FetchManagementperiodById(fertiliserManure.ManagementPeriodID);


                        if (!string.IsNullOrWhiteSpace(model.ClosedPeriodWarningHeading))
                        {
                            WarningMessage warningMessage = new WarningMessage();

                            warningMessage.FieldID = fertiliserManure.FieldID ?? 0;
                            warningMessage.CropID = managementPeriod.CropID ?? 0;
                            warningMessage.JoiningID = null;
                            warningMessage.WarningLevelID = model.ClosedPeriodWarningLevelID;
                            warningMessage.WarningCodeID = model.ClosedPeriodWarningCodeID;

                            warningMessage.Header = model.ClosedPeriodWarningHeader;
                            warningMessage.Para1 = model.ClosedPeriodWarningHeading;
                            warningMessage.Para2 = null;
                            warningMessage.Para3 = model.ClosedPeriodWarningPara2;

                            warningMessages.Add(warningMessage);
                        }
                        //if (model.IsClosedPeriodWarningExceptGrassAndOilseed)
                        //{
                        //    WarningMessage warningMessage = new WarningMessage();

                        //    warningMessage.FieldID = fertiliserManure.FieldID ?? 0;
                        //    warningMessage.CropID = managementPeriod.CropID ?? 0;
                        //    warningMessage.JoiningID = null;
                        //    warningMessage.WarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;
                        //    warningMessage.WarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;

                        //    warningMessage.Header = "Closed period for fertliser except Oilseed and Grass";
                        //    warningMessage.Para1 = Resource.MsgClosedPeriodForFertliserExceptOilseedAndGrassTitle;
                        //    warningMessage.Para2 = null;
                        //    warningMessage.Para3 = null;
                        //}
                        //if (model.IsClosedPeriodWarningOnlyForGrassAndOilseed)
                        //{
                        //    WarningMessage warningMessage = new WarningMessage();

                        //    warningMessage.FieldID = fertiliserManure.FieldID ?? 0;
                        //    warningMessage.CropID = managementPeriod.CropID ?? 0;
                        //    warningMessage.JoiningID = null;
                        //    warningMessage.WarningLevelID = (int)NMP.Portal.Enums.WarningLevel.Manure;
                        //    warningMessage.WarningCodeID = (int)NMP.Portal.Enums.WarningCode.ClosedPeriodOrganicManure;

                        //    warningMessage.Header = "Is closed period warning only for grass and oilseed";
                        //    warningMessage.Para1 = Resource.MsgClosedPeriodForGrassAndOilseedFertliserWarningMsgTitle;
                        //    warningMessage.Para2 = null;
                        //    warningMessage.Para3 = null;
                        //}
                        if (model.IsNitrogenExceedWarning)
                        {
                            WarningMessage warningMessage = new WarningMessage();

                            warningMessage.FieldID = fertiliserManure.FieldID ?? 0;
                            warningMessage.CropID = managementPeriod.CropID ?? 0;
                            warningMessage.JoiningID = null;
                            warningMessage.WarningLevelID = model.ClosedPeriodNitrogenExceedWarningLevelID;
                            warningMessage.WarningCodeID = model.ClosedPeriodNitrogenExceedWarningCodeID;

                            warningMessage.Header = model.ClosedPeriodNitrogenExceedWarningHeader;
                            warningMessage.Para1 = model.ClosedPeriodNitrogenExceedWarningHeading;
                            warningMessage.Para2 = model.ClosedPeriodNitrogenExceedWarningPara1;
                            warningMessage.Para3 = model.ClosedPeriodNitrogenExceedWarningPara2;
                            warningMessages.Add(warningMessage);
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
