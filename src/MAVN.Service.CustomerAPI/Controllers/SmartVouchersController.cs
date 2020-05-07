﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Common.Log;
using MAVN.Common.Middleware.Authentication;
using MAVN.Common.Middleware.Version;
using Lykke.Common.ApiLibrary.Exceptions;
using Lykke.Common.Log;
using MAVN.Service.PartnerManagement.Client;
using MAVN.Service.CustomerAPI.Core.Constants;
using MAVN.Service.CustomerAPI.Models.Enums;
using MAVN.Service.CustomerAPI.Models.SmartVouchers;
using MAVN.Service.PartnerManagement.Client.Models;
using MAVN.Service.SmartVouchers.Client;
using MAVN.Service.SmartVouchers.Client.Models.Requests;
using MAVN.Service.SmartVouchers.Client.Models.Responses.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MAVN.Service.CustomerAPI.Controllers
{
    [ApiController]
    [LykkeAuthorize]
    [Route("api/smartVouchers")]
    [LowerVersion(Devices = "android", LowerVersion = 659)]
    [LowerVersion(Devices = "IPhone,IPad", LowerVersion = 181)]
    public class SmartVouchersController : ControllerBase
    {
        private readonly IRequestContext _requestContext;
        private readonly ISmartVouchersClient _smartVouchersClient;
        private readonly IPartnerManagementClient _partnerManagementClient;
        private readonly IMapper _mapper;
        private readonly ILog _log;

        public SmartVouchersController(
            IRequestContext requestContext,
            ISmartVouchersClient smartVouchersClient,
            IPartnerManagementClient partnerManagementClient,
            IMapper mapper,
            ILogFactory logFactory)
        {
            _requestContext = requestContext;
            _smartVouchersClient = smartVouchersClient;
            _partnerManagementClient = partnerManagementClient;
            _mapper = mapper;
            _log = logFactory.CreateLog(this);
        }

        /// <summary>
        /// Returns a collection of smart voucher campaigns.
        /// </summary>
        /// <remarks>
        /// Used to get collection of smart voucher campaigns.
        /// </remarks>
        /// <returns>
        /// 200 - a collection of smart voucher campaigns.
        /// </returns>
        [HttpGet("campaigns")]
        [ProducesResponseType(typeof(SmartVoucherCampaignsListResponse), (int)HttpStatusCode.OK)]
        public async Task<SmartVoucherCampaignsListResponse> GetSmartVouchersCampaignsAsync([FromQuery] GetSmartVoucherCampaignsRequest request)
        {
            var paginatedCampaigns = await _smartVouchersClient.CampaignsApi.GetAsync(new VoucherCampaignsPaginationRequestModel
            {
                CampaignName = request.CampaignName,
                CurrentPage = request.CurrentPage,
                PageSize = request.PageSize,
                OnlyActive = request.OnlyActive
            });

            var result = _mapper.Map<SmartVoucherCampaignsListResponse>(paginatedCampaigns);

            var partnersIds = result.SmartVoucherCampaigns.Select(x => Guid.Parse(x.PartnerId)).ToArray();

            var partners =
                await _partnerManagementClient.Partners.GetByIdsAsync(partnersIds);

            var partnersInfo = partners.ToDictionary(k => k.Id,
                v => (v.BusinessVertical, v.Name,
                    v.Locations.Where(l => l.Latitude.HasValue && l.Longitude.HasValue).Select(x =>
                        new GeolocationModel { Latitude = x.Latitude.Value, Longitude = x.Longitude.Value }).ToList()));

            foreach (var campaign in result.SmartVoucherCampaigns)
            {
                var partnerId = Guid.Parse(campaign.PartnerId);
                (Vertical? BusinessVertical, string Name, List<GeolocationModel> Geolocations) partnerInfo;
                var partnerExists = partnersInfo.TryGetValue(partnerId, out partnerInfo);

                if (!partnerExists)
                {
                    _log.Warning("Smart voucher campaign partner does not exist", context: new { partnerId, campaignId = campaign.Id });
                    continue;;
                }

                campaign.Vertical = (BusinessVertical?)partnerInfo.BusinessVertical;
                campaign.PartnerName = partnerInfo.Name;
                campaign.Geolocations = partnerInfo.Geolocations;
            }

            return result;
        }

        /// <summary>
        /// Returns smart voucher campaign details.
        /// </summary>
        /// <returns>
        /// 200 - smart voucher campaign.
        /// </returns>
        [HttpGet("campaigns/search")]
        [ProducesResponseType(typeof(SmartVoucherCampaignDetailsModel), (int)HttpStatusCode.OK)]
        public async Task<SmartVoucherCampaignDetailsModel> GetSmartVouchersCampaignByIdAsync([FromQuery] Guid id)
        {
            if (id == default)
                throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.SmartVoucherCampaignNotFound);

            var campaign = await _smartVouchersClient.CampaignsApi.GetByIdAsync(id);

            if (campaign == null)
                throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.SmartVoucherCampaignNotFound);

            var result = _mapper.Map<SmartVoucherCampaignDetailsModel>(campaign);

            var partner = await _partnerManagementClient.Partners.GetByIdAsync(campaign.PartnerId);

            if (partner == null)
            {
                _log.Warning("Smart voucher campaign partner does not exist", context: new { campaign.PartnerId, campaignId = campaign.Id });
                return result;
            }

            var geolocations = partner.Locations.Where(l => l.Longitude.HasValue && l.Latitude.HasValue)
                .Select(l => new GeolocationModel { Latitude = l.Latitude.Value, Longitude = l.Longitude.Value })
                .ToList();

            result.Vertical = (BusinessVertical?)partner.BusinessVertical;
            result.PartnerName = partner.Name;
            result.Geolocations = geolocations;

            return result;
        }

        /// <summary>
        /// Reserves a smart voucher
        /// </summary>
        /// <returns>
        /// 200 - payment url
        /// </returns>
        [HttpPost("reserve")]
        [ProducesResponseType(typeof(ReserveSmartVoucherResponse), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<ReserveSmartVoucherResponse> ReserveSmartVoucherAsync([FromBody] ReserveSmartVoucherRequest request)
        {
            var customerId = Guid.Parse(_requestContext.UserId);
            var result = await _smartVouchersClient.VouchersApi.ReserveVoucherAsync(new VoucherProcessingModel
            {
                CustomerId = customerId,
                VoucherCampaignId = request.SmartVoucherCampaignId
            });

            switch (result.ErrorCode)
            {
                case ProcessingVoucherErrorCodes.None:
                    return new ReserveSmartVoucherResponse { PaymentUrl = result.PaymentUrl };
                case ProcessingVoucherErrorCodes.VoucherCampaignNotFound:
                    throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.SmartVoucherCampaignNotFound);
                case ProcessingVoucherErrorCodes.VoucherCampaignNotActive:
                    throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.SmartVoucherCampaignNotActive);
                case ProcessingVoucherErrorCodes.NoAvailableVouchers:
                case ProcessingVoucherErrorCodes.InvalidPartnerPaymentConfiguration:
                    throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.NoAvailableVouchers);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Cancels smart voucher reservation
        /// </summary>
        /// <returns>
        /// </returns>
        [HttpPost("cancelReservation")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task CancelSmartVoucherReservationAsync([FromBody] CancelSmartVoucherReservationRequest request)
        {
            var result = await _smartVouchersClient.VouchersApi.CancelVoucherReservationAsync(new VoucherCancelReservationModel
            {
                ShortCode = request.ShortCode
            });

            if (result != ProcessingVoucherErrorCodes.None)
                throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.SmartVoucherNotFound);
        }

        /// <summary>
        /// Returns smart vouchers for the logged in customer
        /// </summary>
        /// <returns>
        /// 200 - list of smart vouchers
        /// </returns>
        [HttpGet]
        [ProducesResponseType(typeof(SmartVouchersListResponse), (int)HttpStatusCode.OK)]
        public async Task<SmartVouchersListResponse> GetSmartVouchersForCustomerAsync([FromQuery] BasePaginationRequestModel request)
        {
            var customerId = Guid.Parse(_requestContext.UserId);

            var vouchersResponse =
                await _smartVouchersClient.VouchersApi.GetCustomerVouchersAsync(customerId,
                    new BasePaginationRequestModel { CurrentPage = request.CurrentPage, PageSize = request.PageSize });

            var result = _mapper.Map<SmartVouchersListResponse>(vouchersResponse);
            return result;
        }

        /// <summary>
        /// Returns details for a smart voucher
        /// </summary>
        /// <returns>
        /// 200 - smart voucher details
        /// </returns>
        [HttpGet("{voucherShortCode}")]
        [ProducesResponseType(typeof(SmartVoucherDetailsResponse), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<SmartVoucherDetailsResponse> GetSmartVoucherByShortCodeAsync([FromRoute] string voucherShortCode)
        {
            var voucherResponse = await _smartVouchersClient.VouchersApi.GetByShortCodeAsync(voucherShortCode);

            if (voucherResponse == null)
                throw LykkeApiErrorException.BadRequest(ApiErrorCodes.Service.SmartVoucherNotFound);

            var result = _mapper.Map<SmartVoucherDetailsResponse>(voucherResponse);
            return result;
        }
    }
}
