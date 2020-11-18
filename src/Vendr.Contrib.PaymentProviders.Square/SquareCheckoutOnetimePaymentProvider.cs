using Newtonsoft.Json.Linq;
using Square.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Vendr.Core;
using Vendr.Core.Models;
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

            try
            {
                var orderId = "";
                var referenceId = "";
                if (request.InputStream.CanSeek)
                    request.InputStream.Seek(0, SeekOrigin.Begin);

                using (var reader = new StreamReader(request.InputStream))
                {
                    var json = reader.ReadToEnd();

                    var jsonObject = JObject.Parse(json);

                    orderId = jsonObject["data"]["object"]["payment"]["order_id"].ToString();
                }

                if (!string.IsNullOrWhiteSpace(orderId))
                {
                    var result = orderApi.BatchRetrieveOrders(
                        new BatchRetrieveOrdersRequest(new List<string>() { orderId }));

                    squareOrder = result.Orders.FirstOrDefault();
                    referenceId = squareOrder.ReferenceId;

                    if(!string.IsNullOrWhiteSpace(referenceId))
                    {
                        return OrderReference.Parse(referenceId);
                    }
                }
            }
            catch (Exception ex)
            {
                Vendr.Log.Error<SquareCheckoutOnetimePaymentProvider>(ex, "Square - GetOrderReference");
            }

            return base.GetOrderReference(request, settings);
        }

        public override bool FinalizeAtContinueUrl => false;

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, SquareSettings settings)
        {
            var currency = Vendr.Services.CurrencyService.GetCurrency(order.CurrencyId);
            var currencyCode = currency.Code.ToUpperInvariant();

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

            var totalPrice = Convert.ToInt64(order.TotalPrice.Value.WithoutTax * 100);
            var totalTax = Convert.ToInt64(order.TotalPrice.Value.Tax * 100);

            var bodyOrderOrderLineItems = new List<OrderLineItem>()
            {
                new OrderLineItem("1",
                    order.Id.ToString(),
                    order.OrderNumber,
                    basePriceMoney: new Money(totalPrice, currencyCode))
            };

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

            var client = new SquareSdk.SquareClient.Builder()
                .Environment(environment)
                .AccessToken(accessToken)
                .Build();

            var orderApi = client.OrdersApi;

            var transactionId = request.QueryString["transactionId"];

            var paymentStatus = PaymentStatus.PendingExternalSystem;
            SquareSdk.Models.Order squareOrder = null;

            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                var result = orderApi.BatchRetrieveOrders(
                    new BatchRetrieveOrdersRequest(new List<string>() { transactionId }));

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

            return new CallbackResult
            {
                TransactionInfo = new TransactionInfo
                {
                    AmountAuthorized = order.TotalPrice.Value.WithTax,
                    TransactionFee = 0m,
                    TransactionId = Guid.NewGuid().ToString("N"),
                    PaymentStatus = paymentStatus
                }
            };
        }
    }
}
