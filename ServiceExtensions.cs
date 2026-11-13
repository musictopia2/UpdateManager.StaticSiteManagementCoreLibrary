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
                .AddTransient<StaticSiteDeploymentManager>()
                .AddTransient<IStaticSiteHostCleanupService, T>();

            return services;
        }
    }
}