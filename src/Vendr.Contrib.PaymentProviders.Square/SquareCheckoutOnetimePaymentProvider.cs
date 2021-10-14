using Newtonsoft.Json;
using Square.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Vendr.Contrib.PaymentProviders.Square.Models;
using Vendr.Core;
using Vendr.Core.Models;
using Vendr.Core.Web;
using Vendr.Core.Web.Api;
using Vendr.Core.Web.PaymentProviders;
using SquareSdk = Square;

namespace Vendr.Contrib.PaymentProviders.Square
{
    [PaymentProvider("square-checkout-onetime", "Square Checkout (One Time)", "Square payment provider for one time payments", Icon = "icon-invoice")]
    public class SquareCheckoutOnetimePaymentProvider : PaymentProviderBase<SquareSettings>
    {
        public SquareCheckoutOnetimePaymentProvider(VendrContext vendr)
            : base(vendr)
        { }

        public override OrderReference GetOrderReference(HttpRequestBase request, SquareSettings settings)
        {
            var accessToken = settings.SandboxMode ? settings.SandboxAccessToken : settings.LiveAccessToken;
            var environment = settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            var client = new SquareSdk.SquareClient.Builder()
                .Environment(environment)
                .AccessToken(accessToken)
                .Build();

            var orderApi = client.OrdersApi;
            SquareSdk.Models.Order squareOrder = null;

            var squareEvent = GetSquareWebhookEvent(request, settings);

            if(squareEvent != null && squareEvent.IsValid)
            {
                try
                {
                    var referenceId = "";

                    var orderId = GetOrderId(squareEvent);
                    if (!string.IsNullOrWhiteSpace(orderId))
                    {
                        var result = orderApi.BatchRetrieveOrders(
                            new BatchRetrieveOrdersRequest(new List<string>() { orderId }));

                        squareOrder = result.Orders.FirstOrDefault();
                        referenceId = squareOrder.ReferenceId;

                        if (!string.IsNullOrWhiteSpace(referenceId) && Guid.TryParse(referenceId, out var vendrOrderId))
                        {
                            OrderReadOnly vendrOrder = Vendr.Services.OrderService.GetOrder(vendrOrderId);
                            return vendrOrder.GenerateOrderReference();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Vendr.Log.Error<SquareCheckoutOnetimePaymentProvider>(ex, "Square - GetOrderReference");
                }
            }

            return base.GetOrderReference(request, settings);
        }

        public override bool FinalizeAtContinueUrl => false;

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, SquareSettings settings)
        {
            var currency = Vendr.Services.CurrencyService.GetCurrency(order.CurrencyId);
            var currencyCode = currency.Code.ToUpperInvariant();

            // Ensure currency has valid ISO 4217 code
            if (!Iso4217.CurrencyCodes.ContainsKey(currencyCode))
            {
                throw new Exception("Currency must be a valid ISO 4217 currency code: " + currency.Name);
            }

            var accessToken = settings.SandboxMode ? settings.SandboxAccessToken : settings.LiveAccessToken;
            var environment = settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            var client = new SquareSdk.SquareClient.Builder()
                .Environment(environment)
                .AccessToken(accessToken)
                .Build();

            var checkoutApi = client.CheckoutApi;

            var bodyOrderOrderSource = new OrderSource.Builder()
                .Name("Vendr")
                .Build();

            var orderAmount = AmountToMinorUnits(order.TransactionAmount.Value);

            var bodyOrderOrderLineItems = new List<OrderLineItem>()
            {
                new OrderLineItem("1",
                    order.Id.ToString(),
                    order.OrderNumber,
                    basePriceMoney: new Money(orderAmount, currencyCode))
            };

            var orderReference = order.GenerateOrderReference();
            var shortOrderReference = $"{order.Id},{order.OrderNumber}";

            var bodyOrderOrder = new SquareSdk.Models.Order.Builder(settings.LocationId)
                .CustomerId(order.CustomerInfo.CustomerReference)
                .ReferenceId(order.Id.ToString())
                .Source(bodyOrderOrderSource)
                .LineItems(bodyOrderOrderLineItems)
                .Build();

            var bodyOrder = new CreateOrderRequest.Builder()
                .Order(bodyOrderOrder)
                .LocationId(settings.LocationId)
                .IdempotencyKey(Guid.NewGuid().ToString())
                .Build();

            var body = new CreateCheckoutRequest.Builder(
                Guid.NewGuid().ToString(), bodyOrder)
                .RedirectUrl(continueUrl)
                .Build();

            var result = checkoutApi.CreateCheckout(settings.LocationId, body);

            return new PaymentFormResult()
            {
                Form = new PaymentForm(result.Checkout.CheckoutPageUrl, FormMethod.Get)
            };
        }

        public override string GetCancelUrl(OrderReadOnly order, SquareSettings settings)
        {
            return string.Empty;
        }

        public override string GetErrorUrl(OrderReadOnly order, SquareSettings settings)
        {
            return string.Empty;
        }

        public override string GetContinueUrl(OrderReadOnly order, SquareSettings settings)
        {
            settings.MustNotBeNull("settings");
            settings.ContinueUrl.MustNotBeNull("settings.ContinueUrl");

            return settings.ContinueUrl;
        }

        public override CallbackResult ProcessCallback(OrderReadOnly order, HttpRequestBase request, SquareSettings settings)
        {
            var accessToken = settings.SandboxMode ? settings.SandboxAccessToken : settings.LiveAccessToken;
            var environment = settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            var squareEvent = GetSquareWebhookEvent(request, settings);

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
                        var result = orderApi.BatchRetrieveOrders(
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
                    }

                    var callbackResult = CallbackResult.Ok(new TransactionInfo
                    {
                        AmountAuthorized = order.TransactionAmount.Value,
                        TransactionFee = 0m,
                        TransactionId = squareOrder.Id,
                        PaymentStatus = paymentStatus
                    });

                    return callbackResult;
                }
                catch (Exception ex)
                {
                    Vendr.Log.Error<SquareCheckoutOnetimePaymentProvider>(ex, "Square - ProcessCallback");
                }
            }

            return CallbackResult.BadRequest();
        }

        protected SquareWebhookEvent GetSquareWebhookEvent(HttpRequestBase request, SquareSettings settings)
        {
            const string EventKey = "Vendr_SquareEvent";
            SquareWebhookEvent squareEvent = null;

            if(HttpContext.Current.Items[EventKey] != null)
            {
                squareEvent = (SquareWebhookEvent)HttpContext.Current.Items[EventKey];
            }
            else
            {
                try
                {
                    if (request.InputStream.CanSeek)
                        request.InputStream.Seek(0, SeekOrigin.Begin);

                    using (var reader = new StreamReader(request.InputStream))
                    {
                        var json = reader.ReadToEnd();

                        var url = request.Url.ToString();
                        var signature = request.Headers["x-square-signature"];

                        squareEvent = JsonConvert.DeserializeObject<SquareWebhookEvent>(json);
                        squareEvent.IsValid = ValidateSquareSignature(json, url, signature, settings);

                        HttpContext.Current.Items[EventKey] = squareEvent;
                    }
                }
                catch (Exception ex)
                {
                    Vendr.Log.Error<SquareCheckoutOnetimePaymentProvider>(ex, "Square - GetSquareWebhookEvent");
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
