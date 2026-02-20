#!/usr/bin/env pwsh

# Successful commands extracted from terminal history

az iot adr ns delete --name pjgadrnamespace004 --resource-group pjgiothubdemo004h

az identity list -o table

az role assignment create --assignee "89d10474-74af-4874-99a7-c23c2f643083" --role "Contributor" --scope "/subscriptions/c044d19f-c216-4af1-99b3-c4ac22336067/resourceGroups/pjgiothubdemo003"

az identity create --name pjgiothubdemoidentity003 --resource-group pjgiothubdemo003 --location northeurope

$uamiPrincipalId = az identity show `
	--name pjgiothubdemoidentity003 `
	--resource-group pjgiothubdemo003 `
	--query principalId -o tsv

$adrResourceId = az iot adr ns show `
	--name pjgadrnamespace003 `
	--resource-group pjgiothubdemo003 `
	--query id -o tsv

echo $adrResourceId

az role assignment create `
	--assignee $uamiPrincipalId `
	--role "a5c3590a-3a1a-4cd4-9648-ea0a32b15137" `
	--scope $adrResourceId

$UAMI_RESOURCE_ID = (az identity show --name pjgiothubdemoidentity003 --resource-group pjgiothubdemo003 --query id -o tsv)

az iot hub create `
	--name pjgiothubdemo0032 `
	--resource-group pjgiothubdemo003 `
	--location northeurope `
	--sku GEN2 `
	--mi-user-assigned $UAMI_RESOURCE_ID `
	--ns-resource-id $adrResourceId `
	--ns-identity-id $UAMI_RESOURCE_ID

$ADR_PRINCIPAL_ID = az iot adr ns show --name pjgadrnamespace003 --resource-group pjgiothubdemo003 --query "identity.principalId" -o tsv

echo $ADR_PRINCIPAL_ID

$HUB_RESOURCE_ID = az iot hub show --name pjgiothubdemo0032 --resource-group pjgiothubdemo003 --query id -o tsv

echo $HUB_RESOURCE_ID

az role assignment create --assignee $ADR_PRINCIPAL_ID --role "Contributor" --scope $HUB_RESOURCE_ID

az role assignment create --assignee $ADR_PRINCIPAL_ID --role "IoT Hub Registry Contributor" --scope $HUB_RESOURCE_ID

az iot dps create --name pjgiothubdemodps003 --resource-group pjgiothubdemo003 --location northeurope --mi-user-assigned $UAMI_RESOURCE_ID --ns-resource-id $adrResourceId --ns-identity-id $UAMI_RESOURCE_ID

# Remaining setup to provision orphaned device through new DPS -> new IoT Hub

$resourceGroup = "pjgiothubdemo003"
$legacyHubName = "pjgiothubdemo003"
$newHubName = "pjgiothubdemo0032"
$newDpsName = "pjgiothubdemodps003"
$adrNamespace = "pjgadrnamespace003"
$enrollmentId = "skittlesorter"
$caName = "skittlesorter-intermediate"
$credentialPolicyName = "cert-policy"

# 1) Link the new DPS to the new IoT Hub
az iot dps linked-hub create `
	--dps-name $newDpsName `
	--resource-group $resourceGroup `
	--hub-name $newHubName

# Ensure ADR namespace identity has access to legacy linked hub too (sync touches all linked hubs)
$legacyHubResourceId = az iot hub show `
	--name $legacyHubName `
	--resource-group $resourceGroup `
	--query id -o tsv 2>$null

if ($legacyHubResourceId) {
	az role assignment create `
		--assignee $ADR_PRINCIPAL_ID `
		--role "Contributor" `
		--scope $legacyHubResourceId

	az role assignment create `
		--assignee $ADR_PRINCIPAL_ID `
		--role "IoT Hub Registry Contributor" `
		--scope $legacyHubResourceId
}

# Cleanup stale legacy linked-hub entry from DPS (if old hub was deleted)
if ($legacyHubName -ne $newHubName) {
	try {
		az iot dps linked-hub delete `
			--dps-name $newDpsName `
			--resource-group $resourceGroup `
			--linked-hub $legacyHubName `
			--output none
	}
	catch {
	}
}

# Cleanup stale legacy IoT Hub endpoint from ADR namespace messaging endpoints
if ($legacyHubName -ne $newHubName) {
	$subscriptionId = az account show --query id -o tsv
	$legacyHubResourceIdUpper = "/SUBSCRIPTIONS/$($subscriptionId.ToUpper())/RESOURCEGROUPS/$($resourceGroup.ToUpper())/PROVIDERS/MICROSOFT.DEVICES/IOTHUBS/$($legacyHubName.ToUpper())"
	$endpointKeysRaw = az iot adr ns show `
		--name $adrNamespace `
		--resource-group $resourceGroup `
		--query "properties.messaging.endpoints | keys(@)" -o tsv

	if ($endpointKeysRaw) {
		$endpointKeys = $endpointKeysRaw -split "`r?`n"
		foreach ($endpointKey in $endpointKeys) {
			$endpointResourceId = az iot adr ns show `
				--name $adrNamespace `
				--resource-group $resourceGroup `
				--query "properties.messaging.endpoints.$endpointKey.resourceId" -o tsv

			if ($endpointResourceId -and $endpointResourceId.ToUpper() -eq $legacyHubResourceIdUpper) {
				az iot adr ns update `
					--name $adrNamespace `
					--resource-group $resourceGroup `
					--remove "properties.messaging.endpoints.$endpointKey" `
					--output none
			}
		}
	}
}

# 2) Sync ADR credentials/policies to the new IoT Hub
try {
	az iot adr ns credential show `
		--namespace $adrNamespace `
		--resource-group $resourceGroup `
		--output none
}
catch {
	az iot adr ns credential create `
		--namespace $adrNamespace `
		--resource-group $resourceGroup `
		--output none
}

az iot adr ns credential sync `
	--namespace $adrNamespace `
	--resource-group $resourceGroup

az iot hub certificate list `
	--hub-name $newHubName `
	--resource-group $resourceGroup

# 3) Get new DPS ID scope for device configuration
$idScope = az iot dps show `
	--name $newDpsName `
	--resource-group $resourceGroup `
	--query properties.idScope -o tsv

echo $idScope

# 4) Create/recreate enrollment group on new DPS (X.509 flow)
az iot dps enrollment-group create `
	--dps-name $newDpsName `
	--resource-group $resourceGroup `
	--enrollment-id $enrollmentId `
	--ca-name $caName `
	--credential-policy $credentialPolicyName `
	--provisioning-status enabled

# 5) Verify end-to-end wiring
az iot dps linked-hub list `
	--dps-name $newDpsName `
	--resource-group $resourceGroup -o table

az iot dps enrollment-group show `
	--dps-name $newDpsName `
	--resource-group $resourceGroup `
	--enrollment-id $enrollmentId -o json
