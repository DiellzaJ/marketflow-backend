using System.Reflection;
using MarketFlow.Api.Authorization;
using MarketFlow.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace MarketFlow.Api.Tests.Authorization;

public sealed class EndpointAuthorizationPolicyTests
{
    public static TheoryData<Type, string, string> EndpointPolicies => new()
    {
        { typeof(ProductsController), nameof(ProductsController.GetAsync), AuthorizationPolicies.ReadProducts },
        { typeof(ProductsController), nameof(ProductsController.CreateAsync), AuthorizationPolicies.CreateProducts },
        { typeof(ProductsController), nameof(ProductsController.UpdateAsync), AuthorizationPolicies.UpdateProducts },
        { typeof(ProductsController), nameof(ProductsController.PatchAsync), AuthorizationPolicies.UpdateProducts },
        { typeof(ProductsController), nameof(ProductsController.DeleteAsync), AuthorizationPolicies.DeleteProducts },
        { typeof(ProductsController), nameof(ProductsController.DeactivateAsync), AuthorizationPolicies.DeleteProducts },
        { typeof(ProductsController), nameof(ProductsController.ReactivateAsync), AuthorizationPolicies.UpdateProducts },
        { typeof(SalesController), nameof(SalesController.GetAsync), AuthorizationPolicies.ReadSales },
        { typeof(SalesController), nameof(SalesController.CreateAsync), AuthorizationPolicies.CreateSales },
        { typeof(SalesController), nameof(SalesController.UpdateAsync), AuthorizationPolicies.UpdateSales },
        { typeof(SalesController), nameof(SalesController.PatchAsync), AuthorizationPolicies.UpdateSales },
        { typeof(SalesController), nameof(SalesController.DeleteAsync), AuthorizationPolicies.DeleteSales },
        { typeof(PurchasesController), nameof(PurchasesController.GetAsync), AuthorizationPolicies.ReadPurchases },
        { typeof(PurchasesController), nameof(PurchasesController.CreateAsync), AuthorizationPolicies.CreatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.UpdateAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.PatchAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.DeleteAsync), AuthorizationPolicies.DeletePurchases },
        { typeof(InventoryController), nameof(InventoryController.GetAsync), AuthorizationPolicies.ReadInventory },
        { typeof(InventoryController), nameof(InventoryController.CreateAsync), AuthorizationPolicies.CreateInventory },
        { typeof(InventoryController), nameof(InventoryController.UpdateAsync), AuthorizationPolicies.UpdateInventoryStock },
        { typeof(InventoryController), nameof(InventoryController.PatchAsync), AuthorizationPolicies.AdjustInventoryStock },
        { typeof(InventoryController), nameof(InventoryController.DeleteAsync), AuthorizationPolicies.DeleteInventory },
        { typeof(AuthController), nameof(AuthController.CreateRootAdmin), AuthorizationPolicies.RootAdminOnly },
        { typeof(CompaniesController), nameof(CompaniesController.GetAsync), AuthorizationPolicies.RootAdminOnly },
        { typeof(CompaniesController), nameof(CompaniesController.GetByIdAsync), AuthorizationPolicies.RootAdminOnly },
        { typeof(CompaniesController), nameof(CompaniesController.CreateAsync), AuthorizationPolicies.RootAdminOnly },
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
