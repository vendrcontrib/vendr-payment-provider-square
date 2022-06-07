using Newtonsoft.Json;
using Square.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Vendr.Common.Logging;
using Vendr.Contrib.PaymentProviders.Square.Models;
using Vendr.Core.Api;
using Vendr.Core.Models;
using Vendr.Core.PaymentProviders;
using Vendr.Extensions;
using SquareSdk = Square;

namespace Vendr.Contrib.PaymentProviders.Square
{
    [PaymentProvider("square-checkout-onetime", "Square Checkout (One Time)", "Square payment provider for one time payments", Icon = "icon-invoice")]
    public class SquareCheckoutOnetimePaymentProvider : PaymentProviderBase<SquareSettings>
    {
        protected readonly ILogger<SquareCheckoutOnetimePaymentProvider> _logger;

        public override bool FinalizeAtContinueUrl => false;

        public SquareCheckoutOnetimePaymentProvider(VendrContext vendr, ILogger<SquareCheckoutOnetimePaymentProvider> logger)
            : base(vendr)
        {
            _logger = logger;
        }

        public override string GetContinueUrl(PaymentProviderContext<SquareSettings> context)
        {
            context.Settings.MustNotBeNull("settings");
            context.Settings.ContinueUrl.MustNotBeNull("settings.ContinueUrl");

            return context.Settings.ContinueUrl;
        }

        public override string GetCancelUrl(PaymentProviderContext<SquareSettings> context)
        {
            return string.Empty;
        }

        public override string GetErrorUrl(PaymentProviderContext<SquareSettings> context)
        {
            return string.Empty;
        }

        public override async Task<OrderReference> GetOrderReferenceAsync(PaymentProviderContext<SquareSettings> context)
        {
            var accessToken = context.Settings.SandboxMode ? context.Settings.SandboxAccessToken : context.Settings.LiveAccessToken;
            var environment = context.Settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            var client = new SquareSdk.SquareClient.Builder()
                .Environment(environment)
                .AccessToken(accessToken)
                .Build();

            var orderApi = client.OrdersApi;
            var squareEvent = await GetSquareWebhookEvent(context);

            if (squareEvent != null && squareEvent.IsValid)
            {
                try
                {
                    var orderId = GetOrderId(squareEvent);
                    if (!string.IsNullOrWhiteSpace(orderId))
                    {
                        var result = await orderApi.BatchRetrieveOrdersAsync(
                            new BatchRetrieveOrdersRequest(new List<string>() { orderId }));

                        var squareOrder = result.Orders.FirstOrDefault();
                        var referenceId = squareOrder.ReferenceId;

                        if (!string.IsNullOrWhiteSpace(referenceId) && Guid.TryParse(referenceId, out var vendrOrderId))
                        {
                            var vendrOrder = Vendr.Services.OrderService.GetOrder(vendrOrderId);

                            return vendrOrder.GenerateOrderReference();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Square - GetOrderReference");
                }
            }

            return await base.GetOrderReferenceAsync(context);
        }

        public override async Task<PaymentFormResult> GenerateFormAsync(PaymentProviderContext<SquareSettings> context)
        {
            var currency = Vendr.Services.CurrencyService.GetCurrency(context.Order.CurrencyId);
            var currencyCode = currency.Code.ToUpperInvariant();

            // Ensure currency has valid ISO 4217 code
            if (!Iso4217.CurrencyCodes.ContainsKey(currencyCode))
            {
                throw new Exception("Currency must be a valid ISO 4217 currency code: " + currency.Name);
            }

            var accessToken = context.Settings.SandboxMode ? context.Settings.SandboxAccessToken : context.Settings.LiveAccessToken;
            var environment = context.Settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            var client = new SquareSdk.SquareClient.Builder()
                .Environment(environment)
                .AccessToken(accessToken)
                .Build();

            var checkoutApi = client.CheckoutApi;

            var bodyOrderOrderSource = new OrderSource.Builder()
                .Name("Vendr")
                .Build();

            var orderAmount = AmountToMinorUnits(context.Order.TransactionAmount.Value);

            var bodyOrderOrderLineItems = new List<OrderLineItem>()
            {
                new OrderLineItem("1",
                    context.Order.Id.ToString(),
                    context.Order.OrderNumber,
                    basePriceMoney: new Money(orderAmount, currencyCode))
            };

            var orderReference = context.Order.GenerateOrderReference();
            var shortOrderReference = $"{context.Order.Id},{context.Order.OrderNumber}";

            var bodyOrderOrder = new SquareSdk.Models.Order.Builder(context.Settings.LocationId)
                .CustomerId(context.Order.CustomerInfo.CustomerReference)
                .ReferenceId(context.Order.Id.ToString())
                .Source(bodyOrderOrderSource)
                .LineItems(bodyOrderOrderLineItems)
                .Build();

            var bodyOrder = new CreateOrderRequest.Builder()
                .Order(bodyOrderOrder)
                .IdempotencyKey(Guid.NewGuid().ToString())
                .Build();

            var body = new CreateCheckoutRequest.Builder(
                Guid.NewGuid().ToString(), bodyOrder)
                .RedirectUrl(context.Urls.ContinueUrl)
                .Build();

            var result = await checkoutApi.CreateCheckoutAsync(context.Settings.LocationId, body);

            return new PaymentFormResult()
            {
                Form = new PaymentForm(result.Checkout.CheckoutPageUrl, PaymentFormMethod.Get)
            };
        }

        public override async Task<CallbackResult> ProcessCallbackAsync(PaymentProviderContext<SquareSettings> context)
        {
            var accessToken = context.Settings.SandboxMode ? context.Settings.SandboxAccessToken : context.Settings.LiveAccessToken;
            var environment = context.Settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            var squareEvent = await GetSquareWebhookEvent(context);

            if(squareEvent != null && squareEvent.IsValid)
            {
                try
                {
                    var client = new SquareSdk.SquareClient.Builder()
                        .Environment(environment)
                        .AccessToken(accessToken)
                        .Build();

                    var orderApi = client.OrdersApi;

                    var orderId = GetOrderId(squareEvent);

                    var paymentStatus = PaymentStatus.PendingExternalSystem;
                    
                    SquareSdk.Models.Order squareOrder = null;

                    if (!string.IsNullOrWhiteSpace(orderId))
                    {
                        var result = await orderApi.BatchRetrieveOrdersAsync(
                            new BatchRetrieveOrdersRequest(new List<string>() { orderId }));

                        squareOrder = result.Orders.FirstOrDefault();
                    }

                    if (squareOrder != null)
                    {
                        var orderStatus = squareOrder.State ?? "";

                        switch (orderStatus.ToUpper())
                        {
                            case "COMPLETED":
                            case "AUTHORIZED":
                                paymentStatus = PaymentStatus.Authorized;
                                break;
                            case "CANCELED":
                                paymentStatus = PaymentStatus.Cancelled;
                                break;
                        }
                        
                        return CallbackResult.Ok(new TransactionInfo
                        {
                            AmountAuthorized = context.Order.TransactionAmount.Value,
                            TransactionFee = 0m,
                            TransactionId = squareOrder.Id,
                            PaymentStatus = paymentStatus
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Square - ProcessCallback");
                }
            }

            return CallbackResult.BadRequest();
        }

        protected async Task<SquareWebhookEvent> GetSquareWebhookEvent(PaymentProviderContext<SquareSettings> context)
        {
            const string EventKey = "Vendr_SquareEvent";

            SquareWebhookEvent squareEvent = null;

            if(context.AdditionalData.ContainsKey(EventKey))
            {
                squareEvent = (SquareWebhookEvent)context.AdditionalData[EventKey];
            }
            else
            {
                try
                {
                    var json = await context.Request.Content.ReadAsStringAsync();
                    var url = context.Request.RequestUri.ToString();
                    var signature = context.Request.Headers.GetValues("x-square-signature").FirstOrDefault();

                    squareEvent = JsonConvert.DeserializeObject<SquareWebhookEvent>(json);
                    squareEvent.IsValid = ValidateSquareSignature(json, url, signature, context.Settings);

                    context.AdditionalData.Add(EventKey, squareEvent);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Square - GetSquareWebhookEvent");
                }
            }

            return squareEvent;
        }

        protected bool ValidateSquareSignature(string body, string url, string signature, SquareSettings settings)
        {
            var signatureKey = settings.SandboxMode ? settings.SandboxWebhookSigningSecret : settings.LiveWebhookSigningSecret;

            var combined = url + body;

            var hmac = HMACSHA1Hash(signatureKey, combined);
            var checkHash = Base64Encode(hmac);

            return !string.IsNullOrWhiteSpace(signature) && signature == checkHash;
        }

        protected byte[] HMACSHA1Hash(string key, string input)
        {
            byte[] result = new byte[0];
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);

            using (var algorithm = new HMACSHA1(keyBytes))
            {
                result = algorithm.ComputeHash(Encoding.UTF8.GetBytes(input));
            }

            return result;
        }

        protected static string Base64Encode(byte[] plainTextBytes)
        {
            return Convert.ToBase64String(plainTextBytes);
        }

        protected string GetOrderId(SquareWebhookEvent squareEvent)
        {
            return squareEvent?.data?._object?.payment?.order_id ?? "";
        }
    }
}
