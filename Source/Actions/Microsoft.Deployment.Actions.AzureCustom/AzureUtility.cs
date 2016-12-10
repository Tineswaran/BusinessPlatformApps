﻿
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure;
using Microsoft.Azure.Management.Resources;
using Microsoft.Azure.Management.Resources.Models;
using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.ErrorCode;
using Microsoft.Deployment.Common.Helpers;
using Newtonsoft.Json.Linq;

namespace Microsoft.Deployment.Actions.AzureCustom
{
    public class AzureUtility
    {
        public static async Task<ActionResponse> WaitForArmDeployment(SubscriptionCloudCredentials creds, string resourceGroup, string deploymentName)
        {
            ResourceManagementClient client = new ResourceManagementClient(creds);

            while (true)
            {
                Thread.Sleep(5000);
                var status = await client.Deployments.GetAsync(resourceGroup, deploymentName, new CancellationToken());
                var operations = await client.DeploymentOperations.ListAsync(resourceGroup, deploymentName, new DeploymentOperationsListParameters(), new CancellationToken());
                var provisioningState = status.Deployment.Properties.ProvisioningState;

                if (provisioningState == "Accepted" || provisioningState == "Running")
                    continue;

                if (provisioningState == "Succeeded")
                    return new ActionResponse(ActionStatus.Success, operations);

                var operation = operations.Operations.First(p => p.Properties.ProvisioningState == ProvisioningState.Failed);
                var operationFailed = await client.DeploymentOperations.GetAsync(resourceGroup, deploymentName, operation.OperationId, new CancellationToken());

                return new ActionResponse(ActionStatus.Failure, operationFailed);
            }
        }

        public static async Task<ActionResponse> ValidateAndDeployArm(SubscriptionCloudCredentials creds, string resourceGroup, string deploymentName, string template)
        {
            var deployment = new Microsoft.Azure.Management.Resources.Models.Deployment()
            {
                Properties = new DeploymentPropertiesExtended()
                {
                    Template = template.ToString(),
                    Parameters = JsonUtility.GetEmptyJObject().ToString()
                }
            };

            ResourceManagementClient client = new ResourceManagementClient(creds);
            var validate = client.Deployments.ValidateAsync(resourceGroup, deploymentName, deployment, new CancellationToken()).Result;
            if (!validate.IsValid)
            {
                return new ActionResponse(ActionStatus.Failure, JsonUtility.GetJObjectFromObject(validate), null,
                     DefaultErrorCodes.DefaultErrorCode, $"Azure:{validate.Error.Message} Details:{validate.Error.Details}");
            }

            var deploymentItem = await client.Deployments.CreateOrUpdateAsync(resourceGroup, deploymentName, deployment, new CancellationToken());
            return new ActionResponse(ActionStatus.Success, deploymentItem);
        }

        public static async Task<AzureArmParameterGenerator> GetAzureArmParameters(string armTemplate, JToken armParameters)
        {
            var param = new AzureArmParameterGenerator();
            foreach (var prop in armParameters.Children())
            {
                string key = prop.Path.Split('.').Last();
                string value = prop.First().ToString();

                param.AddStringParam(key, value);
            }

            var armTemplateContents = JsonUtility.GetJObjectFromJsonString(armTemplate);
            var armParamTemplate = JsonUtility.GetJObjectFromObject(param.GetDynamicObject());
            armTemplateContents.Remove("parameters");
            armTemplateContents.Add("parameters", armParamTemplate["parameters"]);
            return param;
        }
    }
}