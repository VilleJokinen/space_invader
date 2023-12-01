// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

export interface StaticConfig {
  clusterConfig: ClusterConfig
  defaultLanguage: string
  supportedLogicVersions: { minVersion: number, maxVersion: number }
  serverReflection: { activablesMetadata: ActivablesMetadata }
  configBuildInfo: ServerConfigBuildInfo
}

type ClusteringMode = 'Static' | 'Kubernetes'

export interface ClusterConfig {
  mode: ClusteringMode
  nodeSets: NodeSetConfig[]
}

export interface NodeSetConfig {
  mode: ClusteringMode
  shardName: string
  hostName: string
  port: number
  nodeCount: number
  EntityKindMask: string[]
}

export interface ActivablesMetadata{
  categories: {[key: string]: ActivableCategoryMetadata}
  kinds: {[key: string]: ActivableCategoryMetadata}
}

export interface ActivableCategoryMetadata{
  displayName: string
  shortSingularDisplayName: string
  description: string
  kinds: string[]
}

export interface ActivableKindMetadata{
  displayName: string
  category: string
  description: string
  gameSpecificConfigDataMembers: string[]
}

export interface ServerConfigBuildInfo {
  gameConfigBuildSupported: boolean
  gameConfigBuildParametersType: string
  gameConfigBuildParametersNamespaceQualifiedName: string
  slotToAvailableSourcesMapping: { [key: string]: Array<{ displayName: string }> }
}
