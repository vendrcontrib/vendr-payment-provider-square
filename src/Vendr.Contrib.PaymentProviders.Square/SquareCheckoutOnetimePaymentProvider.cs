using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Mvc;
using Square.Apis;
using Square.Models;
using SquareSdk = Square;
using Vendr.Core;
using Vendr.Core.Models;
using Vendr.Core.Web.Api;
using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.Square
{
    [PaymentProvider("square", "Square", "Square payment provider", Icon = "icon-invoice")]
    public class SquareCheckoutOnetimePaymentProvider : PaymentProviderBase<SquareSettings>
    {
        public SquareCheckoutOnetimePaymentProvider(VendrContext vendr)
            : base(vendr)
        { }

        public override bool FinalizeAtContinueUrl => true;

        public override PaymentFormResult GenerateForm(OrderReadOnly order, string continueUrl, string cancelUrl, string callbackUrl, SquareSettings settings)
        {
            var currency = Vendr.Services.CurrencyService.GetCurrency(order.CurrencyId);
            var currencyCode = currency.Code.ToUpperInvariant();

            var accessToken = settings.SandboxMode ? settings.SandboxAccessToken : settings.LiveAccessToken;
            var environment = settings.SandboxMode ? SquareSdk.Environment.Sandbox : SquareSdk.Environment.Production;

            SquareSdk.SquareClient client = new SquareSdk.SquareClient.Builder()
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
                    $"Order #{order.OrderNumber}", 
                    basePriceMoney: new Money(totalPrice, currencyCode))
            };

            var bodyOrderOrder = new SquareSdk.Models.Order.Builder(settings.LocationId)
                .CustomerId(order.CustomerInfo.CustomerReference)
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
                .RedirectUrl("https://meganthomaslashes.co.uk/checkout/order-confirmation/")
                .Build();

            CreateCheckoutResponse result = checkoutApi.CreateCheckout(settings.LocationId, body);

            return new PaymentFormResult()
            {
                Form = new PaymentForm(result.Checkout.CheckoutPageUrl, FormMethod.Post)
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
            return new CallbackResult
            {
                TransactionInfo = new TransactionInfo
                {
                    AmountAuthorized = order.TotalPrice.Value.WithTax,
                    TransactionFee = 0m,
                    TransactionId = Guid.NewGuid().ToString("N"),
                    PaymentStatus = PaymentStatus.Authorized
                }
            };
        }
    }
}
