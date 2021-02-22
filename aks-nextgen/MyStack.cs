using System;
using System.Text;
using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureAD;
using Pulumi.AzureNextGen.ContainerService.Latest;
using Pulumi.AzureNextGen.ContainerService.Latest.Inputs;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Storage.Latest.Inputs;
using Pulumi.Random;
using Pulumi.Serialization;
using Pulumi.Tls;

namespace aks_nextgen
{
    class MyStack : Stack
    {
        public MyStack()
        {
            var resourceGroupName = "r500-mano";
            var tags = new InputMap<string>
            {
                {"owner", "m.novak@quadient.com"},
                {"budget", "200"},
                {"trackingId", "CSE-3200"},
            };
            var config = new Pulumi.Config();
            var location = config.Get("location") ?? "West Europe";

            // Create an Azure Resource Group
            var resourceGroup = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName,
                Location = location,
                Tags = tags,
            });

            var mano1StorageAccount = new StorageAccount("mano1", new StorageAccountArgs
            {
                ResourceGroupName = resourceGroup.Name,
                AccountName = "mano1",
                Kind = Kind.StorageV2,
                Sku = new SkuArgs { Name = SkuName.Standard_LRS },
                // AccountReplicationType = "LRS",
                // AccountTier = "Standard",
            });

            var mano2StorageAccount = new StorageAccount("mano2", new StorageAccountArgs
            {
                ResourceGroupName = resourceGroup.Name,
                AccountName = "mano2",
                Kind = Kind.StorageV2,
                Sku = new SkuArgs { Name = SkuName.Standard_LRS },
                // AccountReplicationType = "LRS",
                // AccountTier = "Standard",
            });

            // Create an AD service principal
            var adApp = new Application("aks", new ApplicationArgs { Name = "aks" });
            var adSp = new ServicePrincipal("aksSp", new ServicePrincipalArgs
            {
                ApplicationId = adApp.ApplicationId
            });

            // Generate random password
            var password = new RandomPassword("password", new RandomPasswordArgs
            {
                Length = 20,
                Special = true
            });

            // Create the Service Principal Password
            var adSpPassword = new ServicePrincipalPassword("aksSpPassword", new ServicePrincipalPasswordArgs
            {
                ServicePrincipalId = adSp.Id,
                Value = password.Result,
                EndDate = "2099-01-01T00:00:00Z"
            });

            // Generate an SSH key
            var sshKey = new PrivateKey("ssh-key", new PrivateKeyArgs
            {
                Algorithm = "RSA",
                RsaBits = 4096
            });

            var managedClusterName = config.Get("managedClusterName") ?? "r500-mano";
            var cluster = new ManagedCluster(
                "r500-mano",
                new ManagedClusterArgs
                {
                    ResourceGroupName = resourceGroupName,
                    Tags = tags,
                    AddonProfiles =
                        {
                            { "KubeDashboard", new ManagedClusterAddonProfileArgs { Enabled = true } }
                        },
                    AgentPoolProfiles =
                        {
                            new ManagedClusterAgentPoolProfileArgs
                            {
                                Count = 3,
                                MaxPods = 110,
                                Mode = "System",
                                Name = "agentpool",
                                OsDiskSizeGB = 30,
                                OsType = "Linux",
                                Type = "VirtualMachineScaleSets",
                                VmSize = "Standard_DS2_v2",
                            }
                },
                    DnsPrefix = "azurenextgenprovider",
                    EnableRBAC = true,
                    KubernetesVersion = "1.18.14",
                    LinuxProfile = new ContainerServiceLinuxProfileArgs
                    {
                        AdminUsername = "testuser",
                        Ssh = new ContainerServiceSshConfigurationArgs
                        {
                            PublicKeys =
                    {
                        new ContainerServiceSshPublicKeyArgs
                        {
                            KeyData = sshKey.PublicKeyOpenssh,
                        }
                    }
                        }
                    },
                    Location = resourceGroup.Location,
                    NodeResourceGroup = $"MC_azure-nextgen-cs_{managedClusterName}",
                    ResourceName = managedClusterName,
                    ServicePrincipalProfile = new ManagedClusterServicePrincipalProfileArgs
                    {
                        ClientId = adApp.ApplicationId,
                        Secret = adSpPassword.Value
                    }
                });

            // Export the KubeConfig
            this.KubeConfig = Output.Tuple(resourceGroup.Name, cluster.Name).Apply(names =>
                GetKubeConfig(names.Item1, names.Item2));
        }

        [Output]
        public Output<string> KubeConfig { get; set; }

        private static async Task<string> GetKubeConfig(string resourceGroupName, string clusterName)
        {
            var credentials = await ListManagedClusterUserCredentials.InvokeAsync(new ListManagedClusterUserCredentialsArgs
            {
                ResourceGroupName = resourceGroupName,
                ResourceName = clusterName
            });
            var encoded = credentials.Kubeconfigs[0].Value;
            var data = Convert.FromBase64String(encoded);
            return Encoding.UTF8.GetString(data);
        }
    }
}