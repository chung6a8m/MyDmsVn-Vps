using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyDmsVn.Desktop.Application;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    public static class HttpDesktopDependencyInjection
    {
        public static IServiceCollection AddHttpDesktopAdapter(
            this IServiceCollection services,
            HttpDesktopOptions options,
            HttpDesktopOperationSettings operations)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            services.AddSingleton(options);
            services.AddSingleton(operations);
            services.TryAddSingleton<IDesktopNotificationService, DesktopNotificationCenter>();
            services.TryAddSingleton<IMessenger>(_ => new WeakReferenceMessenger());
            services.TryAddSingleton<IAsyncDelay, SystemAsyncDelay>();
            services.TryAddSingleton<IUiDispatcher, ImmediateUiDispatcher>();
            services.TryAddSingleton<IMonotonicClock, HttpMonotonicClock>();
            services.TryAddSingleton(DesktopSessionOptions.Default);
            services.TryAddSingleton<HttpDesktopSession>();
            services.TryAddSingleton<IDesktopSession>(provider =>
                provider.GetRequiredService<HttpDesktopSession>());
            services.TryAddSingleton<IDesktopSessionActivity>(provider =>
                provider.GetRequiredService<HttpDesktopSession>());
            services.TryAddSingleton<HttpDesktopTransport>();
            services.TryAddSingleton<IHttpPendingWriteStore, WindowsHttpPendingWriteStore>();
            services.TryAddSingleton<HttpDesktopOperations>();
            services.TryAddSingleton<IDesktopWriteRecovery>(provider =>
                provider.GetRequiredService<HttpDesktopOperations>());

            services.AddTransient<IFoundationApiClient, HttpFoundationApiClient>();
            services.AddTransient<IIdentityApiClient, HttpIdentityApiClient>();
            services.AddTransient<IPermissionApiClient, HttpPermissionApiClient>();
            services.AddTransient<IPermissionCatalogApiClient, HttpPermissionCatalogApiClient>();
            services.AddTransient<ISecurityAdministrationApiClient, HttpSecurityAdministrationApiClient>();
            services.AddTransient<IProductApiClient, HttpProductApiClient>();
            services.AddTransient<IWarehouseApiClient, HttpWarehouseApiClient>();
            services.AddTransient<IEmployeeApiClient, HttpEmployeeApiClient>();
            services.AddTransient<ICustomerApiClient, HttpCustomerApiClient>();
            services.AddTransient<IGoodsReceiptApiClient, HttpGoodsReceiptApiClient>();
            services.AddTransient<ISalesReceiptApiClient, HttpSalesReceiptApiClient>();
            services.AddTransient<IInventoryApiClient, HttpInventoryApiClient>();
            services.TryAddSingleton<ISalesReceiptPreviewCalculator, HttpDesktopPreviewCalculator>();
            services.TryAddSingleton<IDesktopExceptionReporter, HttpDesktopSafeExceptionReporter>();
            services.AddTransient<ProductCatalogLookupSource>();
            services.AddTransient<WarehouseCatalogLookupSource>();
            services.AddTransient<EmployeeCatalogLookupSource>();
            services.AddTransient<CustomerCatalogLookupSource>();
            return services;
        }
    }

    internal sealed class HttpDesktopSafeExceptionReporter : IDesktopExceptionReporter
    {
        // No SQL, credentials, access token, refresh cookie or raw exception text
        // is written by the Desktop production adapter.
        public string Report(string operationName, Exception exception) =>
            Guid.NewGuid().ToString("N");
    }
}
