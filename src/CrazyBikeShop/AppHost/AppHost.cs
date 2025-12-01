using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Resources;

const string durableTaskSchedulerAdmin = "0ad04412-c4d5-4796-b79c-f76d14c8d402";
var builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<IResourceWithConnectionString> dts;
IResourceBuilder<IResourceWithConnectionString> ai = null!;
IResourceBuilder<ParameterResource> env = null!;
IResourceBuilder<AzureUserAssignedIdentityResource> identity = null!;
IResourceBuilder<AzureBicepResource> dtsBicep = null!;

const string projectName = "crazybikeshop";

if (builder.ExecutionContext.IsRunMode)
{
    var scheduler =
        builder.AddContainer("scheduler", "mcr.microsoft.com/dts/dts-emulator", "latest")
            .WithHttpEndpoint(name: "grpc", port:8080, targetPort: 8080)
            .WithHttpEndpoint(name: "dashboard", port:8082, targetPort: 8082);
    dts = builder.AddConnectionString("dts", 
        ReferenceExpression.Create($"Endpoint={scheduler.GetEndpoint("grpc")};TaskHub=default;Authentication=None"));
}
else
{
    env = builder.AddParameter("environment");

    identity = builder.AddAzureUserAssignedIdentity("identity");
    
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
            acr.Name = BicepFunction.Interpolate($"{projectName}{envParam}cr").Compile();
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
    
    //dts = builder.AddConnectionString("dts");
    dtsBicep = builder.AddBicepTemplate("dts", "./bicep/dts.bicep")
        .WithParameter("dtsName", $"{projectName}-{env}-dts");
    dts = builder.AddConnectionString("dts", ReferenceExpression.Create($"{dtsBicep.GetOutput("dts_endpoint")}"));

    var dtsAdminRole = builder.AddBicepTemplate("identityAssignDTS", "./bicep/role.bicep")
        .WithParameter("principalId", identity.Resource.PrincipalId)
        .WithParameter("roleDefinitionId", durableTaskSchedulerAdmin)
        .WithParameter("principalType", "ServicePrincipal");
    
    // var role2 = builder.AddBicepTemplate("identityAssignDTSDash", "./bicep/role.bicep")
    //     .WithParameter("principalId", "userPrincipalId")
    //     .WithParameter("roleDefinitionId", durableTaskSchedulerAdmin)
    //     .WithParameter("principalType", "User");
}

//builder.AddProject<Projects.Client>("client");
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(dts)
    .WaitFor(dts)
    .PublishAsAzureContainerApp((infra, app) =>
    {
        var envParam = env.AsProvisioningParameter(infra);
        app.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-api").Compile();
        app.Identity = new ManagedServiceIdentity
        {
            ManagedServiceIdentityType = ManagedServiceIdentityType.UserAssigned,
            UserAssignedIdentities = { { identity.Resource.Id.ToString()!, new UserAssignedIdentityDetails() } }
        };
    });

var orchestrator = builder.AddProject<Projects.Orchestrator>("orchestrator")
    .WithReference(dts)
    .WaitFor(dts)
    .PublishAsAzureContainerApp((infra, app) =>
    {
        var envParam = env.AsProvisioningParameter(infra);
        app.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-orchestrator").Compile();
        app.Identity = new ManagedServiceIdentity
        {
            ManagedServiceIdentityType = ManagedServiceIdentityType.UserAssigned,
            UserAssignedIdentities = { { identity.Resource.Id.ToString()!, new UserAssignedIdentityDetails() } }
        };
        app.Template = new ContainerAppTemplate
        {
            Scale = new ContainerAppScale
            {
                MinReplicas = 0,
                MaxReplicas = 5,
                Rules =
                {
                    new ContainerAppScaleRule
                    {
                        Custom = new ContainerAppCustomScaleRule
                        {
                            Metadata =
                            {
                                { "endpoint", BicepFunction.Interpolate($"{dtsBicep.GetOutput("dts_endpoint")}") },
                                { "taskhubName", BicepFunction.Interpolate($"{dtsBicep.GetOutput("taskhub_name")}") },
                                { "maxConcurrentWorkItemsCount", "1" },
                                { "workItemType", "Orchestration" }
                            },
                            Identity = BicepFunction.Interpolate($"{identity.Resource.Id}")
                        }
                    }
                }
            }
        };
    });

var assembler = builder.AddProject<Projects.Assembler>("assembler")
    .WithReference(dts)
    .WaitFor(dts)
    .PublishAsAzureContainerApp((infra, app) =>
    {
        var envParam = env.AsProvisioningParameter(infra);
        app.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-assembler").Compile();
        app.Identity = new ManagedServiceIdentity
        {
            ManagedServiceIdentityType = ManagedServiceIdentityType.UserAssigned,
            UserAssignedIdentities = { { identity.Resource.Id.ToString()!, new UserAssignedIdentityDetails() } }
        };
        app.Template = new ContainerAppTemplate
        {
            Scale = new ContainerAppScale
            {
                MinReplicas = 0,
                MaxReplicas = 5,
                Rules =
                {
                    new ContainerAppScaleRule
                    {
                        Custom = new ContainerAppCustomScaleRule
                        {
                            Metadata =
                            {
                                { "endpoint", BicepFunction.Interpolate($"{dtsBicep.GetOutput("dts_endpoint")}") },
                                { "taskhubName", BicepFunction.Interpolate($"{dtsBicep.GetOutput("taskhub_name")}") },
                                { "maxConcurrentWorkItemsCount", "1" },
                                { "workItemType", "Activity" }
                            },
                            Identity = BicepFunction.Interpolate($"{identity.Resource.Id}")
                        }
                    }
                }
            }
        };
    });

var ship = builder.AddProject<Projects.Ship>("ship")
    .WithReference(dts)
    .WaitFor(dts)
    .PublishAsAzureContainerApp((infra, app) =>
    {
        var envParam = env.AsProvisioningParameter(infra);
        app.Name = BicepFunction.Interpolate($"{projectName}-{envParam}-ship").Compile();
        app.Identity = new ManagedServiceIdentity
        {
            ManagedServiceIdentityType = ManagedServiceIdentityType.UserAssigned,
            UserAssignedIdentities = { { identity.Resource.Id.ToString()!, new UserAssignedIdentityDetails() } }
        };
        app.Template = new ContainerAppTemplate
        {
            Scale = new ContainerAppScale
            {
                MinReplicas = 0,
                MaxReplicas = 5,
                Rules =
                {
                    new ContainerAppScaleRule
                    {
                        Custom = new ContainerAppCustomScaleRule
                        {
                            Metadata =
                            {
                                { "endpoint", BicepFunction.Interpolate($"{dtsBicep.GetOutput("dts_endpoint")}") },
                                { "taskhubName", BicepFunction.Interpolate($"{dtsBicep.GetOutput("taskhub_name")}") },
                                { "maxConcurrentWorkItemsCount", "1" },
                                { "workItemType", "Activity" }
                            },
                            Identity = BicepFunction.Interpolate($"{identity.Resource.Id}")
                        }
                    }
                }
            }
        };
    });

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithReference(ai);
    orchestrator.WithReference(ai);
    assembler.WithReference(ai);
    ship.WithReference(ai);
}

builder.Build().Run();
