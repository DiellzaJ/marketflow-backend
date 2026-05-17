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
        { typeof(SalesController), nameof(SalesController.GetAsync), AuthorizationPolicies.ReadSales },
        { typeof(SalesController), nameof(SalesController.CreateAsync), AuthorizationPolicies.CreateSales },
        { typeof(PurchasesController), nameof(PurchasesController.GetAsync), AuthorizationPolicies.ReadPurchases },
        { typeof(PurchasesController), nameof(PurchasesController.CreateAsync), AuthorizationPolicies.CreatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.UpdateAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(PurchasesController), nameof(PurchasesController.PatchAsync), AuthorizationPolicies.UpdatePurchases },
        { typeof(InventoryController), nameof(InventoryController.GetAsync), AuthorizationPolicies.ReadInventory },
        { typeof(InventoryController), nameof(InventoryController.UpdateAsync), AuthorizationPolicies.UpdateInventory },
        { typeof(InventoryController), nameof(InventoryController.PatchAsync), AuthorizationPolicies.UpdateInventory },
        { typeof(UsersController), nameof(UsersController.GetAsync), AuthorizationPolicies.ReadUsers },
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

        var authorizeAttribute = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal(expectedPolicy, authorizeAttribute.Policy);
    }
}
