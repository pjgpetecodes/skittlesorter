param namespaceName string
param groupName string
param jobName string
param groupQuery string
param updateVersion string = '1.1.0'
@allowed(['linux-arm', 'linux-arm64'])
param runtime string

resource ns 'Microsoft.DeviceRegistry/namespaces@2026-11-02-preview' existing = {
  name: namespaceName
}

// Queries and job targets are immutable; use new names when changing a cohort or update.
resource group 'Microsoft.DeviceRegistry/namespaces/groups@2026-11-02-preview' = {
  parent: ns
  name: groupName
  location: ns.location
  properties: {
    description: 'One-device Skittle Sorter demonstration cohort; inspect membership before scheduling.'
    query: groupQuery
  }
}

resource job 'Microsoft.DeviceRegistry/namespaces/jobs@2026-11-02-preview' = {
  parent: ns
  name: jobName
  location: ns.location
  properties: {
    description: 'Custom C# regular-update demonstration'
    jobType: 'SoftwareUpdate'
    target: {
      resourceId: group.id
    }
    definition: {
      schedulingType: 'Continuous'
      update: {
        updateId: {
          provider: 'SkittleSorter'
          name: 'sorter-${runtime}'
          version: updateVersion
        }
      }
    }
  }
}

output jobId string = job.id
