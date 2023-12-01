import { useCoreStore } from '../../coreStore'
import { type IGeneratedUiFieldTypeSchema } from './generatedUiTypes'
import { useGameServerApi } from '@metaplay/game-server-api'
import { sleep } from '../../coreUtils'

/**
 * Returns schema data from the server for the given type. Note that this function
 * uses a cache: typeSchemas. Entries in the cache have three states:
 * - Uncached. Has no entry in the cache.
 * - Loading. Has a null value in the cache.
 * - Cached. Has an object value in the cache.
 * @param typeName Name of the schema to load.
 * @returns Scheme data object.
 */
export async function GetTypeSchemaForTypeName (typeName: string): Promise<IGeneratedUiFieldTypeSchema> {
  const gameServerApi = useGameServerApi()
  const coreStore = useCoreStore()

  // Is the schema cached?
  if (typeName in coreStore.schemas) {
    // Already an entry in the cache.
    while (coreStore.schemas[typeName] === null) {
      // Wait until the schema is loaded (ie: not still loading)
      await sleep(10)
    }

    // Return data from the cache.
    return coreStore.schemas[typeName] as IGeneratedUiFieldTypeSchema
  } else {
    // Not in the cache. Set schema data to 'null' in the cache to mark it as 'loading'.
    coreStore.setSchemaForType(typeName, null)

    // Load the schema and store the data in the cache.
    try {
      const schema = (await gameServerApi.get(`forms/schema/${typeName}`)).data as IGeneratedUiFieldTypeSchema
      coreStore.setSchemaForType(typeName, schema)
    } catch (err: any) {
      throw new Error(`Failed to load schema for ${typeName} from the server! Reason: ${err.message}.`)
    }

    return coreStore.schemas[typeName] as IGeneratedUiFieldTypeSchema
  }
}

export async function PreloadAllSchemasForTypeName (typeName: string): Promise<IGeneratedUiFieldTypeSchema> {
  return await GetTypeSchemaForTypeName(typeName)
}
