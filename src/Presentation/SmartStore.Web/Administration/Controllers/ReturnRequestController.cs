﻿using System;
using System.Collections.Generic;
using System.Web.Mvc;
using SmartStore.Admin.Models.Orders;
using SmartStore.Core;
using SmartStore.Core.Domain.Localization;
using SmartStore.Core.Domain.Orders;
using SmartStore.Services.Customers;
using SmartStore.Services.Helpers;
using SmartStore.Services.Localization;
using SmartStore.Core.Logging;
using SmartStore.Services.Messages;
using SmartStore.Services.Orders;
using SmartStore.Services.Security;
using SmartStore.Services.Catalog;
using SmartStore.Web.Framework;
using SmartStore.Web.Framework.Controllers;
using Telerik.Web.Mvc;

namespace SmartStore.Admin.Controllers
{
    [AdminAuthorize]
    public class ReturnRequestController : AdminControllerBase
    {
        #region Fields

        private readonly IOrderService _orderService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICustomerService _customerService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly LocalizationSettings _localizationSettings;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly IPermissionService _permissionService;
		private readonly IOrderProcessingService _orderProcessingService;
		private readonly OrderSettings _orderSettings;

        #endregion Fields

        #region Constructors

        public ReturnRequestController(IOrderService orderService,
            ICustomerService customerService, IDateTimeHelper dateTimeHelper,
            ILocalizationService localizationService, IWorkContext workContext,
            IWorkflowMessageService workflowMessageService, LocalizationSettings localizationSettings,
            ICustomerActivityService customerActivityService, IPermissionService permissionService,
			IOrderProcessingService orderProcessingService,
			OrderSettings orderSettings)
        {
            this._orderService = orderService;
            this._customerService = customerService;
            this._dateTimeHelper = dateTimeHelper;
            this._localizationService = localizationService;
            this._workContext = workContext;
            this._workflowMessageService = workflowMessageService;
            this._localizationSettings = localizationSettings;
            this._customerActivityService = customerActivityService;
            this._permissionService = permissionService;
			this._orderProcessingService = orderProcessingService;
			this._orderSettings = orderSettings;
        }

        #endregion

        #region Utilities

        [NonAction]
        private bool PrepareReturnRequestModel(ReturnRequestModel model, ReturnRequest returnRequest, bool excludeProperties)
        {
            if (model == null)
                throw new ArgumentNullException("model");

            if (returnRequest == null)
                throw new ArgumentNullException("returnRequest");

            var orderItem = _orderService.GetOrderItemById(returnRequest.OrderItemId);
            if (orderItem == null)
                return false;

            model.Id = returnRequest.Id;
            model.ProductId = orderItem.ProductId;
			model.ProductSku = orderItem.Product.Sku;
			model.ProductName = orderItem.Product.Name;
			model.ProductTypeName = orderItem.Product.GetProductTypeLabel(_localizationService);
			model.ProductTypeLabelHint = orderItem.Product.ProductTypeLabelHint;
            model.OrderId = orderItem.OrderId;
			model.OrderNumber = orderItem.Order.GetOrderNumber();
            model.CustomerId = returnRequest.CustomerId;
			model.CustomerFullName = returnRequest.Customer.GetFullName();
            model.Quantity = returnRequest.Quantity;
            model.ReturnRequestStatusStr = returnRequest.ReturnRequestStatus.GetLocalizedEnum(_localizationService, _workContext);
            model.CreatedOn = _dateTimeHelper.ConvertToUserTime(returnRequest.CreatedOnUtc, DateTimeKind.Utc);

            if (!excludeProperties)
            {
                model.ReasonForReturn = returnRequest.ReasonForReturn;
                model.RequestedAction = returnRequest.RequestedAction;
                model.CustomerComments = returnRequest.CustomerComments;
                model.StaffNotes = returnRequest.StaffNotes;
                model.ReturnRequestStatusId = returnRequest.ReturnRequestStatusId;
            }

			string unspec = _localizationService.GetResource("Common.Unspecified");
			model.AvailableReasonForReturn.Add(new SelectListItem() { Text = unspec, Value = "" });
			model.AvailableRequestedAction.Add(new SelectListItem() { Text = unspec, Value = "" });

			// that's what we also offer in frontend
			string returnRequestReasons = _orderSettings.GetLocalized(x => x.ReturnRequestReasons, orderItem.Order.CustomerLanguageId, true, false);
			string returnRequestActions = _orderSettings.GetLocalized(x => x.ReturnRequestActions, orderItem.Order.CustomerLanguageId, true, false);

			foreach (var rrr in returnRequestReasons.SplitSafe(","))
			{
				model.AvailableReasonForReturn.Add(new SelectListItem() { Text = rrr, Value = rrr, Selected = (rrr == returnRequest.ReasonForReturn) });
			}

			foreach (var rra in returnRequestActions.SplitSafe(","))
			{
				model.AvailableRequestedAction.Add(new SelectListItem() { Text = rra, Value = rra, Selected = (rra == returnRequest.RequestedAction) });
			}

			var urlHelper = new UrlHelper(Request.RequestContext);

			model.AutoUpdateOrderItem.Id = returnRequest.Id;
			model.AutoUpdateOrderItem.Caption = _localizationService.GetResource("Admin.ReturnRequests.Accept.Caption");
			model.AutoUpdateOrderItem.PostUrl = urlHelper.Action("Accept", "ReturnRequest");
			model.AutoUpdateOrderItem.ShowUpdateTotals = (orderItem.Order.OrderStatusId <= (int)OrderStatus.Pending);
			model.AutoUpdateOrderItem.ShowUpdateRewardPoints = (orderItem.Order.OrderStatusId > (int)OrderStatus.Pending && orderItem.Order.RewardPointsWereAdded);
			model.AutoUpdateOrderItem.UpdateTotals = model.AutoUpdateOrderItem.ShowUpdateTotals;
			model.AutoUpdateOrderItem.UpdateRewardPoints = orderItem.Order.RewardPointsWereAdded;

			model.ReturnRequestInfo = TempData[AutoUpdateOrderItemContext.InfoKey] as string;

            return true;
        }

        #endregion

        #region Methods

        //list
        public ActionResult Index()
        {
            return RedirectToAction("List");
        }

        public ActionResult List()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
                return AccessDeniedView();

            var gridModel = new GridModel<ReturnRequestModel>();
            return View(gridModel);
        }

        [HttpPost, GridAction(EnableCustomBinding = true)]
        public ActionResult List(GridCommand command)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
                return AccessDeniedView();

			var data = new List<ReturnRequestModel>();
			var returnRequests = _orderService.SearchReturnRequests(0, 0, 0, null, command.Page - 1, command.PageSize);

            foreach (var rr in returnRequests)
            {
                var m = new ReturnRequestModel();
                if (PrepareReturnRequestModel(m, rr, false))
                    data.Add(m);
            }

            var gridModel = new GridModel<ReturnRequestModel>
            {
                Data = data,
                Total = returnRequests.TotalCount,
            };
            return new JsonResult
            {
                Data = gridModel
            };
        }

        //edit
        public ActionResult Edit(int id)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
                return AccessDeniedView();

            var returnRequest = _orderService.GetReturnRequestById(id);
            if (returnRequest == null)
                //No return request found with the specified id
                return RedirectToAction("List");
            
            var model = new ReturnRequestModel();
            PrepareReturnRequestModel(model, returnRequest, false);
            return View(model);
        }

        [HttpPost, ParameterBasedOnFormNameAttribute("save-continue", "continueEditing")]
        [FormValueRequired("save", "save-continue")]
        public ActionResult Edit(ReturnRequestModel model, bool continueEditing)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
                return AccessDeniedView();

            var returnRequest = _orderService.GetReturnRequestById(model.Id);
            if (returnRequest == null)
                return RedirectToAction("List");

            if (ModelState.IsValid)
            {
				returnRequest.Quantity = model.Quantity;
                returnRequest.ReasonForReturn = model.ReasonForReturn;
                returnRequest.RequestedAction = model.RequestedAction;
                returnRequest.CustomerComments = model.CustomerComments;
                returnRequest.StaffNotes = model.StaffNotes;
                returnRequest.ReturnRequestStatusId = model.ReturnRequestStatusId;
                returnRequest.UpdatedOnUtc = DateTime.UtcNow;

				if (returnRequest.ReasonForReturn == null)
					returnRequest.ReasonForReturn = "";

				if (returnRequest.RequestedAction == null)
					returnRequest.RequestedAction = "";

                _customerService.UpdateCustomer(returnRequest.Customer);

                //activity log
                _customerActivityService.InsertActivity("EditReturnRequest", _localizationService.GetResource("ActivityLog.EditReturnRequest"), returnRequest.Id);

                NotifySuccess(_localizationService.GetResource("Admin.ReturnRequests.Updated"));
                return continueEditing ? RedirectToAction("Edit", returnRequest.Id) : RedirectToAction("List");
            }


            //If we got this far, something failed, redisplay form
            PrepareReturnRequestModel(model, returnRequest, true);
            return View(model);
        }

        [HttpPost, ActionName("Edit")]
        [FormValueRequired("notify-customer")]
        public ActionResult NotifyCustomer(ReturnRequestModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
                return AccessDeniedView();

            var returnRequest = _orderService.GetReturnRequestById(model.Id);
            if (returnRequest == null)
                //No return request found with the specified id
                return RedirectToAction("List");

            //var customer = returnRequest.Customer;
            var orderItem = _orderService.GetOrderItemById(returnRequest.OrderItemId);
            int queuedEmailId = _workflowMessageService.SendReturnRequestStatusChangedCustomerNotification(returnRequest, orderItem, _localizationSettings.DefaultAdminLanguageId);
            if (queuedEmailId > 0)
                NotifySuccess(_localizationService.GetResource("Admin.ReturnRequests.Notified"));
            return RedirectToAction("Edit", returnRequest.Id);
        }

        //delete
        [HttpPost, ActionName("Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
                return AccessDeniedView();

            var returnRequest = _orderService.GetReturnRequestById(id);
            if (returnRequest == null)
                return RedirectToAction("List");

            _orderService.DeleteReturnRequest(returnRequest);

            //activity log
            _customerActivityService.InsertActivity("DeleteReturnRequest", _localizationService.GetResource("ActivityLog.DeleteReturnRequest"), returnRequest.Id);

            NotifySuccess(_localizationService.GetResource("Admin.ReturnRequests.Deleted"));
            return RedirectToAction("List");
        }

		[HttpPost]
		public ActionResult Accept(AutoUpdateOrderItemModel model)
		{
			if (!_permissionService.Authorize(StandardPermissionProvider.ManageReturnRequests))
				return AccessDeniedView();

			var returnRequest = _orderService.GetReturnRequestById(model.Id);
			var oi = _orderService.GetOrderItemById(returnRequest.OrderItemId);

			int cancelQuantity = (returnRequest.Quantity > oi.Quantity ? oi.Quantity : returnRequest.Quantity);

			var context = new AutoUpdateOrderItemContext()
			{
				OrderItem = oi,
				QuantityOld = oi.Quantity,
				QuantityNew = Math.Max(oi.Quantity - cancelQuantity, 0),
				AdjustInventory = model.AdjustInventory,
				UpdateRewardPoints = model.UpdateRewardPoints,
				UpdateTotals = model.UpdateTotals
			};

			returnRequest.ReturnRequestStatus = ReturnRequestStatus.ReturnAuthorized;
			_customerService.UpdateCustomer(returnRequest.Customer);

			_orderProcessingService.AutoUpdateOrderDetails(context);

			TempData[AutoUpdateOrderItemContext.InfoKey] = context.ToString(_localizationService);

			return RedirectToAction("Edit", new { id = returnRequest.Id });
		}

        #endregion
    }
}
