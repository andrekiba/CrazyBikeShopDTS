using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Resources;

const string durableTaskDataContributor = "0ad04412-c4d5-4796-b79c-f76d14c8d402";
const string taskHubNames= "orchestrator,assembler,ship";
var builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<IResourceWithConnectionString> dtsOrchestrator;
IResourceBuilder<IResourceWithConnectionString> dtsAssembler;
IResourceBuilder<IResourceWithConnectionString> dtsShip;
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
            .WithHttpEndpoint(name: "dashboard", port:8082, targetPort: 8082)
            .WithEnvironment("DTS_TASK_HUB_NAMES", taskHubNames);
    dtsOrchestrator = builder.AddConnectionString("dts-orchestrator", 
        ReferenceExpression.Create($"Endpoint={scheduler.GetEndpoint("grpc")};TaskHub=orchestrator;Authentication=None"))
        .WaitFor(scheduler);
    dtsAssembler = builder.AddConnectionString("dts-assembler", 
            ReferenceExpression.Create($"Endpoint={scheduler.GetEndpoint("grpc")};TaskHub=assembler;Authentication=None"))
        .WaitFor(scheduler);
    dtsShip = builder.AddConnectionString("dts-ship", 
            ReferenceExpression.Create($"Endpoint={scheduler.GetEndpoint("grpc")};TaskHub=ship;Authentication=None"))
        .WaitFor(scheduler);
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
    dtsOrchestrator = builder.AddConnectionString("dts-orchestrator", ReferenceExpression.Create($"{dtsBicep.GetOutput("dts_endpoint")};TaskHub=orchestrator;Authentication=AzureDefault"));
    dtsAssembler = builder.AddConnectionString("dts-assembler", ReferenceExpression.Create($"{dtsBicep.GetOutput("dts_endpoint")};TaskHub=assembler;Authentication=AzureDefault"));
    dtsShip = builder.AddConnectionString("dts-ship", ReferenceExpression.Create($"{dtsBicep.GetOutput("dts_endpoint")};TaskHub=ship;Authentication=AzureDefault"));

    var dtsAdminRole = builder.AddBicepTemplate("identityAssignDTS", "./bicep/role.bicep")
        .WithParameter("principalId", identity.Resource.PrincipalId)
        .WithParameter("roleDefinitionId", durableTaskDataContributor)
        .WithParameter("principalType", "ServicePrincipal");
    
    // var role2 = builder.AddBicepTemplate("identityAssignDTSDash", "./bicep/role.bicep")
    //     .WithParameter("principalId", "userPrincipalId")
    //     .WithParameter("roleDefinitionId", durableTaskSchedulerAdmin)
    //     .WithParameter("principalType", "User");
}

//builder.AddProject<Projects.Client>("client");
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(dtsOrchestrator)
    .WaitFor(dtsOrchestrator)
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
    .WithReference(dtsOrchestrator)
    .WaitFor(dtsOrchestrator)
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
                                { "taskhubName", "orchestrator" },
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
    .WithReference(dtsAssembler)
    .WaitFor(dtsAssembler)
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
                                { "taskhubName", "assembler" },
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
    .WithReference(dtsShip)
    .WaitFor(dtsShip)
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
                                { "taskhubName", "ship" },
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
