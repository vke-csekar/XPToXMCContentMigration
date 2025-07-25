using CWXPMigration;
using CWXPMigration.Services;
using Microsoft.Extensions.DependencyInjection;
using Sitecore.DependencyInjection;

namespace CWXPTool
{
    public class RegisterDependencies : IServicesConfigurator
    {
        public void Configure(IServiceCollection services)
        {
            // Register your services here
            services.AddTransient<ISitecoreGraphQLClient, SitecoreGraphQLClient>();
            services.AddTransient<ISideNavMigrationService, SideNavMigrationService>();
            services.AddTransient<ITeachingSheetMigrationService, TeachingSheetMigrationService>();
        }
    }
}