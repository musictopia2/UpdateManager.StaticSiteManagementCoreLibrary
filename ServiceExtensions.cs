using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UpdateManager.StaticSiteManagementCoreLibrary;
public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection RegisterStaticSiteManagementServices()
        {
            services
                .AddTransient<IStaticSitesContext, FileStaticSitesContext>()
                .AddTransient<StaticSiteDiscoveryService>()
                .AddTransient<IStaticSiteVersionWriter, StaticSiteVersionWriter>()
                .AddTransient<StaticSiteUpdateSupportInstaller>()
                .AddTransient<IStaticSitePreparationService, StaticSitePreparationService>()
                .AddTransient<IStaticSiteRepositoryService, StaticSiteRepositoryService>()
                .AddTransient<StaticSiteHostRegistryBuilder>()
                .AddTransient<StaticSiteHostRegistryWriter>()
                .AddTransient<IStaticSiteHostPublicationService, StaticSiteHostPublicationService>()
                .AddTransient<StaticSiteRegistrationManager>();
            return services;
        }
        public IServiceCollection RegisterCombinedStaticSiteDeploymentServices<T>()
            where T : class, IStaticSiteHostCleanupService
        {
            services.RegisterStaticSiteDeploymentServices<T>();
            services.RegisterLocalStaticSiteDeploymentServices();
            services.TryAddTransient<CombinedStaticSiteDeploymentManager>();

            return services;
        }

        public IServiceCollection RegisterStaticSiteDeploymentServices<T>()
            where T : class, IStaticSiteHostCleanupService
        {
            services
                .AddTransient<IStaticSitesContext, FileStaticSitesContext>()
                .AddTransient<IStaticSiteVersionWriter, StaticSiteVersionWriter>()
                .AddTransient<IStaticSiteRepositoryService, StaticSiteRepositoryService>()
                .AddTransient<StaticSiteDependencyUpdateService>()
                .AddTransient<StaticSitePackageInspector>()
                .AddTransient<StaticSiteVersionPreparationService>()
                .AddTransient<StaticSiteSourceSubmissionService>()
                .AddTransient<IStaticSiteWorkflowDispatcher, StaticSiteWorkflowDispatcher>()
                .AddTransient<StaticSiteHostRegistryBuilder>()
                .AddTransient<StaticSiteHostRegistryWriter>()
                .AddTransient<IStaticSiteHostPublicationService, StaticSiteHostPublicationService>()
                .AddTransient<StaticSiteHostMembershipService>()
                .AddTransient<PublicStaticSiteDeploymentManager>()
                .AddTransient<IStaticSiteHostCleanupService, T>();

            return services;
        }
        //this means a person could choose local only if they chose.
        public IServiceCollection RegisterLocalStaticSiteDeploymentServices()
        {
            services.TryAddTransient<IStaticSitesContext, FileStaticSitesContext>();
            services.TryAddTransient<IPackagesContext, FilePackagesContext>();
            services.TryAddTransient<StaticSiteDependencyUpdateService>();
            services.TryAddTransient<LocalStaticSiteDeploymentService>();
            services.TryAddTransient<LocalStaticSiteDeploymentManager>();
            return services;
        }
    }
}
