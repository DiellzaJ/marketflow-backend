using System.Reflection;
using MarketFlow.Api.Authorization;
using MarketFlow.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class EndpointAuthorizationPolicyTests
{
    public static TheoryData<Type, string, string> EndpointPolicies => new()
    {
        { typeof(AiController), nameof(AiController.GenerateDashboardSummaryAsync), AuthorizationPolicies.CompanyAdminOnly },
        { typeof(AiController), nameof(AiController.GenerateInventoryForecastAsync), AuthorizationPolicies.CompanyAdminOnly },
        { typeof(AiController), nameof(AiController.GenerateInventoryRecommendationsAsync), AuthorizationPolicies.CompanyAdminOnly },
        { typeof(AiController), nameof(AiController.GeneratePurchaseRecommendationsAsync), AuthorizationPolicies.CompanyAdminOrMainOperator },
        { typeof(AiController), nameof(AiController.GenerateSupplierPerformanceInsightsAsync), AuthorizationPolicies.CompanyAdminOnly },
        { typeof(AiController), nameof(AiController.DetectAnomaliesAsync), AuthorizationPolicies.CompanyAdminOnly },
        { typeof(ProductsController), nameof(ProductsController.GetAsync), AuthorizationPolicies.ReadProducts },
        { typeof(ProductsController), nameof(ProductsController.CreateAsync), AuthorizationPolicies.CreateProducts },
        { typeof(ProductsController), nameof(ProductsController.UpdateAsync), AuthorizationPolicies.UpdateProducts },
        { typeof(ProductsController), nameof(ProductsController.PatchAsync), AuthorizationPolicies.UpdateProducts },
        { typeof(ProductsController), nameof(ProductsController.DeleteAsync), AuthorizationPolicies.DeleteProducts },
        { typeof(ProductsController), nameof(ProductsController.DeactivateAsync), AuthorizationPolicies.DeleteProducts },
        { typeof(ProductsController), nameof(ProductsController.ReactivateAsync), AuthorizationPolicies.UpdateProducts },
        { typeof(SalesController), nameof(SalesController.GetAsync), AuthorizationPolicies.ReadSales },
        { typeof(SalesController), nameof(SalesController.GetHistoryAsync), AuthorizationPolicies.ReadSales },
        { typeof(SalesController), nameof(SalesController.GetByIdAsync), AuthorizationPolicies.ReadSales },
        { typeof(SalesController), nameof(SalesController.CreateAsync), AuthorizationPolicies.CreateSales },
        { typeof(SalesController), nameof(SalesController.UpdateAsync), AuthorizationPolicies.UpdateSales },
        { typeof(SalesController), nameof(SalesController.PatchAsync), AuthorizationPolicies.UpdateSales },
        { typeof(SalesController), nameof(SalesController.DeleteAsync), AuthorizationPolicies.DeleteSales },
        { typeof(DashboardController), nameof(DashboardController.GetSalesSummaryAsync), AuthorizationPolicies.ReadSales },
        { typeof(PurchasesController), nameof(PurchasesController.GetAsync), AuthorizationPolicies.ReadPurchases },
        { typeof(PurchasesController), nameof(PurchasesController.GetByIdAsync), AuthorizationPolicies.ReadPurchases },
        { typeof(PurchasesController), nameof(PurchasesController.CreateAsync), AuthorizationPolicies.CreatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.UpdateAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.PatchAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.ReceiveAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.CancelAsync), AuthorizationPolicies.DeletePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.DeleteAsync), AuthorizationPolicies.DeletePurchases },
        { typeof(SuppliersController), nameof(SuppliersController.GetAsync), AuthorizationPolicies.ReadSuppliers },
        { typeof(SuppliersController), nameof(SuppliersController.GetByIdAsync), AuthorizationPolicies.ReadSuppliers },
        { typeof(SuppliersController), nameof(SuppliersController.CreateAsync), AuthorizationPolicies.CreateSuppliers },
        { typeof(SuppliersController), nameof(SuppliersController.UpdateAsync), AuthorizationPolicies.UpdateSuppliers },
        { typeof(SuppliersController), nameof(SuppliersController.UpdateStatusAsync), AuthorizationPolicies.UpdateSuppliers },
        { typeof(SuppliersController), nameof(SuppliersController.DeactivateAsync), AuthorizationPolicies.DeleteSuppliers },
        { typeof(InventoryController), nameof(InventoryController.GetAsync), AuthorizationPolicies.ReadInventory },
        { typeof(InventoryController), nameof(InventoryController.GetLowStockAsync), AuthorizationPolicies.ReadInventory },
        { typeof(InventoryController), nameof(InventoryController.GetPosProductsAsync), AuthorizationPolicies.ReadInventory },
        { typeof(InventoryController), nameof(InventoryController.GetByIdAsync), AuthorizationPolicies.ReadInventory },
        { typeof(InventoryController), nameof(InventoryController.GetMovementsAsync), AuthorizationPolicies.ReadInventoryMovements },
        { typeof(InventoryController), nameof(InventoryController.GetMovementsForInventoryAsync), AuthorizationPolicies.ReadInventoryMovements },
        { typeof(InventoryController), nameof(InventoryController.TransferAsync), AuthorizationPolicies.TransferInventory },
        { typeof(InventoryController), nameof(InventoryController.CreateAsync), AuthorizationPolicies.CreateInventory },
        { typeof(InventoryController), nameof(InventoryController.UpdateAsync), AuthorizationPolicies.UpdateInventory },
        { typeof(InventoryController), nameof(InventoryController.PatchAsync), AuthorizationPolicies.AdjustInventory },
        { typeof(InventoryController), nameof(InventoryController.AdjustAsync), AuthorizationPolicies.AdjustInventory },
        { typeof(InventoryController), nameof(InventoryController.DeleteAsync), AuthorizationPolicies.DeleteInventory },
        { typeof(MarketsController), nameof(MarketsController.GetAsync), AuthorizationPolicies.ReadMarkets },
        { typeof(MarketsController), nameof(MarketsController.GetByIdAsync), AuthorizationPolicies.ReadMarkets },
        { typeof(MarketsController), nameof(MarketsController.CreateAsync), AuthorizationPolicies.CreateMarkets },
        { typeof(MarketsController), nameof(MarketsController.UpdateAsync), AuthorizationPolicies.UpdateMarkets },
        { typeof(MarketsController), nameof(MarketsController.DeactivateAsync), AuthorizationPolicies.DeleteMarkets },
        { typeof(MarketsController), nameof(MarketsController.ActivateAsync), AuthorizationPolicies.UpdateMarkets },
        { typeof(DepartmentsController), nameof(DepartmentsController.GetAsync), AuthorizationPolicies.ReadDepartments },
        { typeof(AuthController), nameof(AuthController.CreateRootAdmin), AuthorizationPolicies.RootAdminOnly },
        { typeof(CompaniesController), nameof(CompaniesController.GetAsync), AuthorizationPolicies.RootAdminOnly },
        { typeof(CompaniesController), nameof(CompaniesController.GetByIdAsync), AuthorizationPolicies.RootAdminOnly },
        { typeof(CompaniesController), nameof(CompaniesController.CreateAsync), AuthorizationPolicies.RootAdminOnly },
        { typeof(ProfileController), nameof(ProfileController.GetAsync), AuthorizationPolicies.ActiveUser },
        { typeof(ProfileController), nameof(ProfileController.UpdateAsync), AuthorizationPolicies.ActiveUser },
        { typeof(ProfileController), nameof(ProfileController.ChangePasswordAsync), AuthorizationPolicies.ActiveUser },
        { typeof(UsersController), nameof(UsersController.GetAsync), AuthorizationPolicies.ReadUsers },
        { typeof(UsersController), nameof(UsersController.GetByIdAsync), AuthorizationPolicies.ReadUsers },
        { typeof(UsersController), nameof(UsersController.CreateAsync), AuthorizationPolicies.CreateUsers },
        { typeof(UsersController), nameof(UsersController.UpdateAsync), AuthorizationPolicies.UpdateUsers },
        { typeof(UsersController), nameof(UsersController.PatchAsync), AuthorizationPolicies.UpdateUsers },
        { typeof(UsersController), nameof(UsersController.DeleteAsync), AuthorizationPolicies.DeleteUsers }
    };

    [Theory]
    [MemberData(nameof(EndpointPolicies))]
    public void Endpoint_HasExpectedAuthorizationPolicy(
        Type controllerType,
        string actionName,
        string expectedPolicy)
    {
        var method = controllerType.GetMethod(
            actionName,
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);

        var authorizeAttribute =
            method.GetCustomAttribute<AuthorizeAttribute>() ??
            controllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal(expectedPolicy, authorizeAttribute.Policy);
    }
}
