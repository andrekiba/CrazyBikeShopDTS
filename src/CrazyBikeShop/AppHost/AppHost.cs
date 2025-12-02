using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Resources;

const string durableTaskDataContributor = "0ad04412-c4d5-4796-b79c-f76d14c8d402";
var builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> env = null!;
IResourceBuilder<IResourceWithConnectionString> dts;
IResourceBuilder<IResourceWithConnectionString> ai = null!;
IResourceBuilder<AzureBicepResource> dtsBicep = null!;
IResourceBuilder<AzureUserAssignedIdentityResource> identity = null!;

const string projectName = "cbs";

if (builder.ExecutionContext.IsRunMode)
{
    var scheduler =
        builder.AddContainer("scheduler", "mcr.microsoft.com/dts/dts-emulator", "latest")
            .WithHttpEndpoint(name: "grpc", port:8080, targetPort: 8080)
            .WithHttpEndpoint(name: "dashboard", port:8082, targetPort: 8082);
    dts = builder.AddConnectionString("dts", 
        ReferenceExpression.Create($"Endpoint={scheduler.GetEndpoint("grpc")};TaskHub=default;Authentication=None"))
        .WaitFor(scheduler);
}
else
{
    env = builder.AddParameter("environment");
    var userPrincipalId = builder.AddParameter("userPrincipalId");

    identity = builder.AddAzureUserAssignedIdentity("identity")
        .ConfigureInfrastructure(infra =>
        {
            var resources = infra.GetProvisionableResources();
            var i = resources.OfType<Azure.Provisioning.Roles.UserAssignedIdentity>().Single();
            var envParam = env.AsProvisioningParameter(infra);
            i.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-identity").Compile();
        });
    
    var log = builder.AddAzureLogAnalyticsWorkspace("log")
        .ConfigureInfrastructure(infra =>
        {
            var resources = infra.GetProvisionableResources();
            var log = resources.OfType<Azure.Provisioning.OperationalInsights.OperationalInsightsWorkspace>().Single();
            var envParam = env.AsProvisioningParameter(infra);
            log.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-log").Compile();
        });
    
    ai = builder.AddAzureApplicationInsights("ai", log)
        .ConfigureInfrastructure(infra =>
        {
            var resources = infra.GetProvisionableResources();
            var appInsights = resources.OfType<Azure.Provisioning.ApplicationInsights.ApplicationInsightsComponent>()
                .Single();
            var envParam = env.AsProvisioningParameter(infra);
            appInsights.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-ai").Compile();
            appInsights.IngestionMode = Azure.Provisioning.ApplicationInsights.ComponentIngestionMode.LogAnalytics;
            appInsights.RetentionInDays = 30;
        });
    
    var acr = builder.AddAzureContainerRegistry("acr")
        .ConfigureInfrastructure(infra =>    
        {
            var resources = infra.GetProvisionableResources();
            var acr = resources.OfType<ContainerRegistryService>().Single();
            var envParam = env.AsProvisioningParameter(infra);
            acr.Name = BicepFunction.Interpolate($"{projectName.Replace("-","")}{envParam}cr").Compile();
        });
    
    var cae = builder.AddAzureContainerAppEnvironment("cae")
        .ConfigureInfrastructure(infra =>    
        {
            var resources = infra.GetProvisionableResources();
            var cae = resources.OfType<ContainerAppManagedEnvironment>().Single();
            var envParam = env.AsProvisioningParameter(infra);
            cae.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-cae").Compile();
        })
        .WithAzureContainerRegistry(acr)
        .WithAzureLogAnalyticsWorkspace(log);
    
    dtsBicep = builder.AddBicepTemplate("dts-bicep", "./bicep/dts.bicep")
        .WithParameter("dtsName", ReferenceExpression.Create($"{projectName}-{env}-dts"));
    dts = builder.AddConnectionString("dts", ReferenceExpression.Create($"{dtsBicep.GetOutput("dts_endpoint")};TaskHub=default;Authentication=AzureDefault"));
    
    var dtsDataContributorRoleForApps = builder.AddBicepTemplate("identityAssignDTS", "./bicep/role.bicep")
        .WithParameter("principalId", identity.Resource.PrincipalId)
        .WithParameter("roleDefinitionId", durableTaskDataContributor)
        .WithParameter("principalType", "ServicePrincipal");
    
    var dtsDataContributorRoleForUser = builder.AddBicepTemplate("identityAssignDTSDash", "./bicep/role.bicep")
        .WithParameter("principalId", userPrincipalId)
        .WithParameter("roleDefinitionId", durableTaskDataContributor)
        .WithParameter("principalType", "User");
}

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(dts)
    .WaitFor(dts)
    .PublishAsAzureContainerApp((infra, app) =>
    {
        var envParam = env.AsProvisioningParameter(infra);
        app.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-api").Compile();
    });

var orchestrator = builder.AddProject<Projects.Orchestrator>("orchestrator")
    .WithReference(dts)
    .WaitFor(dts)
    .PublishAsAzureContainerApp((infra, app) =>
    {
        var envParam = env.AsProvisioningParameter(infra);
        app.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-orchestrator").Compile();
        app.Template.Scale.MinReplicas = 0;
        app.Template.Scale.MaxReplicas = 5;
        app.Template.Scale.Rules.Add(new ContainerAppScaleRule
        {
            Name = "dts-orchestration-scaler",
            Custom = new ContainerAppCustomScaleRule
            {
                CustomScaleRuleType = "azure-durabletask-scheduler",
                Metadata =
                {
                    { "endpoint", dtsBicep.GetOutput("dts_endpoint").AsProvisioningParameter(infra) },
                    { "taskhubName", dtsBicep.GetOutput("taskhub_name").AsProvisioningParameter(infra) },
                    { "maxConcurrentWorkItemsCount", "1" },
                    { "workItemType", "Orchestration" }
                },
                Identity = identity.Resource.Id.AsProvisioningParameter(infra)
            }
        });
    });

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithReference(ai);
    api.WithAzureUserAssignedIdentity(identity);
    orchestrator.WithReference(ai);
    orchestrator.WithAzureUserAssignedIdentity(identity);
}

builder.Build().Run();
